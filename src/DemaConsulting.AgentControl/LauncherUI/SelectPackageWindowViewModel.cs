// Copyright (c) DEMA Consulting
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.PackageCredentials;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for the "Select Package..." dialog: lists every configured package source, the
///     packages the selected source offers, and the selected package's versions (descending,
///     defaulting to the latest), per architecture.md's "Initial package selection" decision.
/// </summary>
/// <remarks>
///     <para>
///     The dialog reads left to right - source, package, version - because a package is only
///     identified by the source it comes from: the same package ID may exist in two sources, and
///     the choice is returned as a <see cref="PackageReference"/> (source alias, package ID,
///     version) so the repo's pin records exactly which source it came from. A package is never
///     looked up in a source other than the selected one.
///     </para>
///     <para>
///     All source access is asynchronous and routed through the shared, session-scoped
///     <see cref="AgentPackageManagement.PackageVersionCache"/> rather than a source provider
///     directly, so opening the dialog again in the same app session does not query a source
///     again. <see cref="LoadAsync"/> reads every source's package list concurrently, and each
///     source reports its own failure (<see cref="SelectPackageSourceViewModel.ErrorMessage"/>),
///     so one unreachable source never blocks or hides the others. Selecting a package loads its
///     versions in the background; a newer selection supersedes the load still running for an
///     older one. Not thread-safe; every member is expected to be called from the UI thread.
///     </para>
/// </remarks>
internal sealed class SelectPackageWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     The registry that resolves a source alias to its provider on a cache miss.
    /// </summary>
    private readonly PackageSourceRegistry _registry;

    /// <summary>
    ///     The shared, session-scoped package-version cache.
    /// </summary>
    private readonly PackageVersionCache _cache;

    /// <summary>
    ///     Cancels the version load for the package selected most recently; replaced whenever the
    ///     selection changes.
    /// </summary>
    private CancellationTokenSource _versionsCancellation = new();

    /// <summary>
    ///     Identifies the most recent version load, so a result that arrives after the selection
    ///     moved on is discarded.
    /// </summary>
    private int _versionsGeneration;

    /// <summary>
    ///     Backing field for <see cref="SelectedSource"/>.
    /// </summary>
    private SelectPackageSourceViewModel? _selectedSource;

    /// <summary>
    ///     Backing field for <see cref="SelectedPackageName"/>.
    /// </summary>
    private string? _selectedPackageName;

    /// <summary>
    ///     Backing field for <see cref="SelectedVersion"/>.
    /// </summary>
    private string? _selectedVersion;

    /// <summary>
    ///     Backing field for <see cref="AvailableVersions"/>.
    /// </summary>
    private IReadOnlyList<string> _availableVersions = [];

    /// <summary>
    ///     Backing field for <see cref="IsLoading"/>.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    ///     Backing field for <see cref="IsLoadingVersions"/>.
    /// </summary>
    private bool _isLoadingVersions;

    /// <summary>
    ///     Backing field for <see cref="VersionsError"/>.
    /// </summary>
    private string? _versionsError;

    /// <summary>
    ///     Initializes a new <see cref="SelectPackageWindowViewModel"/> listing every source in
    ///     the registry, without reading any of them; call <see cref="LoadAsync"/> to do that.
    /// </summary>
    /// <param name="registry">The configured sources.</param>
    /// <param name="cache">The shared, session-scoped package-version cache.</param>
    /// <param name="preferredSourceAlias">The alias of the source to start on (for example the
    ///     source the repo is already pinned to), or <see langword="null"/>/an unknown alias to
    ///     start on the first source.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> or
    ///     <paramref name="cache"/> is <see langword="null"/>.</exception>
    public SelectPackageWindowViewModel(
        PackageSourceRegistry registry, PackageVersionCache cache, string? preferredSourceAlias = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(cache);

        _registry = registry;
        _cache = cache;
        Sources = [.. registry.Definitions.Select(definition => new SelectPackageSourceViewModel(definition))];
        _isLoading = Sources.Count > 0;
        _selectedSource = Sources.FirstOrDefault(
                              source => string.Equals(source.Alias, preferredSourceAlias, StringComparison.OrdinalIgnoreCase))
                          ?? Sources.FirstOrDefault();
        if (_selectedSource is not null)
        {
            _selectedSource.PropertyChanged += OnSelectedSourceChanged;
        }

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        RetryCommand = new RelayCommand(Retry, () => CanRetry);
    }

    /// <summary>
    ///     Gets every configured source, in configured order, each with its own loading state.
    /// </summary>
    public IReadOnlyList<SelectPackageSourceViewModel> Sources { get; }

    /// <summary>
    ///     Gets or sets the source the package column shows. Setting it clears the package and
    ///     version selection; the new source's packages are shown as soon as they are loaded.
    /// </summary>
    public SelectPackageSourceViewModel? SelectedSource
    {
        get => _selectedSource;
        set
        {
            var previous = _selectedSource;
            if (!SetField(ref _selectedSource, value))
            {
                return;
            }

            if (previous is not null)
            {
                previous.PropertyChanged -= OnSelectedSourceChanged;
            }

            if (value is not null)
            {
                value.PropertyChanged += OnSelectedSourceChanged;
            }

            SelectedPackageName = null;
            OnPropertyChanged(nameof(PackageNames));
            OnPropertyChanged(nameof(SourceErrorMessage));
            OnPropertyChanged(nameof(HasSourceError));
            OnPropertyChanged(nameof(HasNoPackages));
            RaiseCanRetryChanged();
        }
    }

    /// <summary>
    ///     Gets the packages the selected source offers; empty while it loads or when it failed.
    /// </summary>
    public IReadOnlyList<string> PackageNames => _selectedSource?.PackageNames ?? [];

    /// <summary>
    ///     Gets a value indicating whether the selected source finished loading and offers no
    ///     packages, so the dialog can say so instead of showing an unexplained empty list.
    /// </summary>
    public bool HasNoPackages =>
        _selectedSource is { IsLoading: false, HasError: false } source && source.PackageNames.Count == 0;

    /// <summary>
    ///     Gets why the selected source could not be read, or <see langword="null"/> when it was
    ///     read or is still loading.
    /// </summary>
    public string? SourceErrorMessage => _selectedSource?.ErrorMessage;

    /// <summary>
    ///     Gets a value indicating whether the selected source could not be read.
    /// </summary>
    public bool HasSourceError => _selectedSource?.HasError ?? false;

    /// <summary>
    ///     Gets a value indicating whether any source's package list is still being read, so the
    ///     dialog shows a progress indicator.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    /// <summary>
    ///     Gets or sets the package name currently selected by the user. Setting this starts
    ///     loading the package's versions (see <see cref="AvailableVersions"/>) and resets
    ///     <see cref="SelectedVersion"/>.
    /// </summary>
    public string? SelectedPackageName
    {
        get => _selectedPackageName;
        set
        {
            if (!SetField(ref _selectedPackageName, value))
            {
                return;
            }

            // A newer selection supersedes the version load still running for the older one
            _versionsCancellation.Cancel();
            _versionsCancellation.Dispose();
            _versionsCancellation = new CancellationTokenSource();
            var generation = ++_versionsGeneration;

            AvailableVersions = [];
            SelectedVersion = null;
            VersionsError = null;
            IsLoadingVersions = false;
            PendingVersionsLoad = value is null || _selectedSource is null
                ? Task.CompletedTask
                : LoadVersionsAsync(_selectedSource, value, generation, _versionsCancellation.Token);
            OnPropertyChanged(nameof(CanConfirm));
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    ///     Gets the currently-selected package name's discoverable versions, descending (highest
    ///     first); empty until they have loaded.
    /// </summary>
    public IReadOnlyList<string> AvailableVersions
    {
        get => _availableVersions;
        private set => SetField(ref _availableVersions, value);
    }

    /// <summary>
    ///     Gets a value indicating whether the selected package's versions are being read, so the
    ///     dialog shows a progress indicator beside the version list.
    /// </summary>
    public bool IsLoadingVersions
    {
        get => _isLoadingVersions;
        private set => SetField(ref _isLoadingVersions, value);
    }

    /// <summary>
    ///     Gets why the selected package's versions could not be read, or <see langword="null"/>.
    /// </summary>
    public string? VersionsError
    {
        get => _versionsError;
        private set
        {
            if (SetField(ref _versionsError, value))
            {
                OnPropertyChanged(nameof(HasVersionsError));
                RaiseCanRetryChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="VersionsError"/> has text to show.
    /// </summary>
    public bool HasVersionsError => _versionsError is not null;

    /// <summary>
    ///     Gets the task of the most recently started version load, so a caller (a test, or code
    ///     that must wait for the list) can await it; completed when none is running.
    /// </summary>
    internal Task PendingVersionsLoad { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Gets or sets the version currently selected by the user.
    /// </summary>
    public string? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetField(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(CanConfirm));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether a source, a package name and a version are currently
    ///     selected, i.e. whether <see cref="ConfirmCommand"/> may execute.
    /// </summary>
    public bool CanConfirm => SelectedSource is not null && SelectedPackageName is not null && SelectedVersion is not null;

    /// <summary>
    ///     Gets the command bound to the dialog's "Select" button.
    /// </summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>
    ///     Gets a value indicating whether there is a failed read to retry: the selected source
    ///     could not be read, or the selected package's versions could not be read.
    /// </summary>
    public bool CanRetry => HasSourceError || (HasVersionsError && _selectedSource is not null && _selectedPackageName is not null);

    /// <summary>
    ///     Gets the command bound to the "Retry" button shown beside an inline error: reads the
    ///     source (or, when only the versions failed, the package's versions) again. A server
    ///     that needs a sign-in asks again; a server that was unreachable is tried again.
    /// </summary>
    public RelayCommand RetryCommand { get; }

    /// <summary>
    ///     Gets the task of the most recently started retry of a source read, so a caller (a test)
    ///     can await it; completed when none is running.
    /// </summary>
    internal Task PendingSourceRetry { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Raised when the user confirms their selection, carrying the selected source alias,
    ///     package name and version, so the owning window can close itself with that result.
    /// </summary>
    public event EventHandler<PackageReference>? Confirmed;

    /// <summary>
    ///     Reads the package list of every source concurrently, without blocking the caller.
    /// </summary>
    /// <remarks>
    ///     Each source succeeds or fails on its own: an unreachable source records its error on its
    ///     own row and the others still load. <see cref="IsLoading"/> is set until every source has
    ///     finished.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the reads still running (for example when the
    ///     dialog is closed).</param>
    /// <returns>A task that completes when every source has loaded or failed.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled.</exception>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = Sources.Count > 0;
        try
        {
            await Task.WhenAll(Sources.Select(source => LoadSourceAsync(source, cancellationToken)));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     Cancels the version load still running, for when the dialog closes.
    /// </summary>
    public void CancelLoading() => _versionsCancellation.Cancel();

    /// <summary>
    ///     Reads again whatever failed for the selected source: its package list when the source
    ///     could not be read, otherwise the versions of the selected package.
    /// </summary>
    private void Retry()
    {
        if (_selectedSource is null)
        {
            return;
        }

        if (_selectedSource.HasError)
        {
            // Read the failed source again, on its own, without touching the others
            var source = _selectedSource;
            source.BeginLoading();
            PendingSourceRetry = RetrySourceAsync(source);
            return;
        }

        if (_selectedPackageName is null)
        {
            return;
        }

        // Start a fresh version load exactly as a new selection would, superseding the failed one
        _versionsCancellation.Cancel();
        _versionsCancellation.Dispose();
        _versionsCancellation = new CancellationTokenSource();
        var generation = ++_versionsGeneration;
        VersionsError = null;
        PendingVersionsLoad = LoadVersionsAsync(_selectedSource, _selectedPackageName, generation, _versionsCancellation.Token);
    }

    /// <summary>
    ///     Reads one source's package list again after a failure and records the outcome.
    /// </summary>
    /// <param name="source">The source row to reload.</param>
    /// <returns>A task that completes when the row shows the outcome; cancellation (the dialog
    ///     closing) is not an error.</returns>
    private async Task RetrySourceAsync(SelectPackageSourceViewModel source)
    {
        try
        {
            await LoadSourceAsync(source, _versionsCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // The dialog closed meanwhile: nothing is left to show
        }
    }

    /// <summary>
    ///     Reads one source's package list and records the outcome on its row.
    /// </summary>
    /// <param name="source">The source row to load.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the row shows the outcome.</returns>
    private async Task LoadSourceAsync(SelectPackageSourceViewModel source, CancellationToken cancellationToken)
    {
        try
        {
            var names = await _cache.GetPackageIdsAsync(_registry, source.Alias, cancellationToken);
            source.SetLoaded(names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // An unreachable or misconfigured source is reported on its own row; it must not
            // stop the other sources from loading
            source.SetFailed($"This source could not be read: {SecretRedactor.SafeMessage(ex)}");
        }
    }

    /// <summary>
    ///     Reads the versions of one package at one source and shows them, unless the selection
    ///     has moved on meanwhile.
    /// </summary>
    /// <param name="source">The source the package was selected from.</param>
    /// <param name="packageName">The selected package.</param>
    /// <param name="generation">Identifies this load; a result is discarded when a newer load has
    ///     started.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the versions or the failure are shown; cancellation is
    ///     not an error.</returns>
    private async Task LoadVersionsAsync(
        SelectPackageSourceViewModel source, string packageName, int generation, CancellationToken cancellationToken)
    {
        IsLoadingVersions = true;
        try
        {
            var versions = (await _cache.GetVersionsDescendingAsync(
                    _registry, source.Alias, packageName, cancellationToken))
                .Select(package => package.Version.ToString())
                .ToList();
            if (generation != _versionsGeneration)
            {
                return;
            }

            AvailableVersions = versions;
            SelectedVersion = versions.Count > 0 ? versions[0] : null;
            if (versions.Count == 0)
            {
                VersionsError = $"No versions of '{packageName}' were found in source '{source.Alias}'.";
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection, or the dialog closed: nothing to show
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (generation == _versionsGeneration)
            {
                VersionsError = $"The versions of '{packageName}' could not be read: {SecretRedactor.SafeMessage(ex)}";
            }
        }
        finally
        {
            if (generation == _versionsGeneration)
            {
                IsLoadingVersions = false;
            }
        }
    }

    /// <summary>
    ///     Forwards the selected source's state changes to the properties derived from it.
    /// </summary>
    /// <param name="sender">The selected source row.</param>
    /// <param name="e">Describes the changed property.</param>
    private void OnSelectedSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PackageNames));
        OnPropertyChanged(nameof(SourceErrorMessage));
        OnPropertyChanged(nameof(HasSourceError));
        OnPropertyChanged(nameof(HasNoPackages));
        RaiseCanRetryChanged();
    }

    /// <summary>
    ///     Notifies that whether there is something to retry may have changed.
    /// </summary>
    private void RaiseCanRetryChanged()
    {
        OnPropertyChanged(nameof(CanRetry));
        RetryCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    ///     Raises <see cref="Confirmed"/> with the currently-selected source, package name and
    ///     version.
    /// </summary>
    private void Confirm()
    {
        if (SelectedSource is null || SelectedPackageName is null || SelectedVersion is null)
        {
            return;
        }

        Confirmed?.Invoke(this, new PackageReference(SelectedSource.Alias, SelectedPackageName, SelectedVersion));
    }
}
