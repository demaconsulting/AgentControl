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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for the "Select Package..." dialog: lists distinct package names discoverable
///     at a configured source, then that name's discoverable versions (descending, defaulting to
///     the latest), per architecture.md's "Initial package selection" decision.
/// </summary>
/// <remarks>
///     <para>
///     All source access is routed through the shared, session-scoped
///     <see cref="AgentPackageManagement.PackageVersionCache"/> rather than a source provider
///     directly, so opening this dialog a second time in the same app session does not query the
///     source again, consistent with architecture.md's caching strategy. The dialog lists the
///     packages of exactly one configured source (<see cref="SourceAlias"/>) and never consults
///     another.
///     </para>
///     <para>
///     The asynchronous cache is awaited synchronously on a worker thread (the dialog's lists are
///     populated before it is shown), as the single-source dialog did before; a source-aware,
///     asynchronous dialog with progress and cancellation is part of the multi-source UI work.
///     Not thread-safe; every member is expected to be called from the UI thread.
///     </para>
/// </remarks>
internal sealed class SelectPackageWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     The registry that resolves <see cref="SourceAlias"/> to its provider on a cache miss.
    /// </summary>
    private readonly PackageSourceRegistry _registry;

    /// <summary>
    ///     The shared, session-scoped package-version cache.
    /// </summary>
    private readonly PackageVersionCache _cache;

    private string? _selectedPackageName;
    private string? _selectedVersion;
    private IReadOnlyList<string> _availableVersions = [];

    /// <summary>
    ///     Initializes a new <see cref="SelectPackageWindowViewModel"/>, populating
    ///     <see cref="PackageNames"/> from the chosen source via the shared cache.
    /// </summary>
    /// <param name="registry">The configured sources.</param>
    /// <param name="sourceAlias">The alias of the source to select a package from.</param>
    /// <param name="cache">The shared, session-scoped package-version cache.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> or
    ///     <paramref name="cache"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceAlias"/> is null,
    ///     empty or whitespace.</exception>
    /// <exception cref="UnknownSourceException">Thrown when <paramref name="sourceAlias"/> is not
    ///     configured.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when a folder source does not exist or
    ///     is unreachable.</exception>
    public SelectPackageWindowViewModel(PackageSourceRegistry registry, string sourceAlias, PackageVersionCache cache)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
        ArgumentNullException.ThrowIfNull(cache);

        _registry = registry;
        _cache = cache;
        SourceAlias = sourceAlias;

        PackageNames = Task.Run(() => _cache.GetPackageIdsAsync(_registry, sourceAlias, CancellationToken.None))
            .GetAwaiter().GetResult();

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
    }

    /// <summary>
    ///     Gets the alias of the source this dialog lists packages from; reported back with the
    ///     selection so the pin records it.
    /// </summary>
    public string SourceAlias { get; }

    /// <summary>
    ///     Gets the distinct package base names discoverable at the configured source.
    /// </summary>
    public IReadOnlyList<string> PackageNames { get; }

    /// <summary>
    ///     Gets or sets the package name currently selected by the user. Setting this recomputes
    ///     <see cref="AvailableVersions"/> and resets <see cref="SelectedVersion"/> to the first
    ///     (highest/latest) entry, or <see langword="null"/> if the chosen name has no
    ///     discoverable versions.
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

            AvailableVersions = value is null
                ? []
                : Task.Run(() => _cache.GetVersionsDescendingAsync(_registry, SourceAlias, value, CancellationToken.None))
                    .GetAwaiter().GetResult()
                    .Select(p => p.Version.ToString())
                    .ToList();
            SelectedVersion = AvailableVersions.Count > 0 ? AvailableVersions[0] : null;
            OnPropertyChanged(nameof(CanConfirm));
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    ///     Gets the currently-selected package name's discoverable versions, descending (highest
    ///     first).
    /// </summary>
    public IReadOnlyList<string> AvailableVersions
    {
        get => _availableVersions;
        private set => SetField(ref _availableVersions, value);
    }

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
    ///     Gets a value indicating whether both a package name and a version are currently
    ///     selected, i.e. whether <see cref="ConfirmCommand"/> may execute.
    /// </summary>
    public bool CanConfirm => SelectedPackageName is not null && SelectedVersion is not null;

    /// <summary>
    ///     Gets the command bound to the dialog's "Select" button.
    /// </summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>
    ///     Raised when the user confirms their selection, carrying the selected package name and
    ///     version, so the owning window can close itself with that result.
    /// </summary>
    public event EventHandler<(string Name, string Version)>? Confirmed;

    /// <summary>
    ///     Raises <see cref="Confirmed"/> with the currently-selected package name/version.
    /// </summary>
    private void Confirm()
    {
        if (SelectedPackageName is null || SelectedVersion is null)
        {
            return;
        }

        Confirmed?.Invoke(this, (SelectedPackageName, SelectedVersion));
    }
}
