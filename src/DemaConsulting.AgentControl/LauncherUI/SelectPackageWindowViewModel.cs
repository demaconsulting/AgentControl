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
///     All filesystem access is routed through the shared, session-scoped
///     <see cref="AgentPackageManagement.PackageVersionCache"/> rather than
///     <see cref="PackageSource"/> directly, so opening this dialog a second time in the same app
///     session does not re-scan the filesystem, consistent with architecture.md's caching
///     strategy. Not thread-safe; every member is expected to be called from the UI thread.
/// </remarks>
internal sealed class SelectPackageWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     The configured package-source directory this dialog enumerates names/versions from.
    /// </summary>
    private readonly string _sourceDirectory;

    /// <summary>
    ///     The shared, session-scoped package-version cache.
    /// </summary>
    private readonly PackageVersionCache _cache;

    private string? _selectedPackageName;
    private string? _selectedVersion;
    private IReadOnlyList<string> _availableVersions = [];

    /// <summary>
    ///     Initializes a new <see cref="SelectPackageWindowViewModel"/>, populating
    ///     <see cref="PackageNames"/> from the configured source via the shared cache.
    /// </summary>
    /// <param name="sourceDirectory">The configured package-source directory to enumerate.</param>
    /// <param name="cache">The shared, session-scoped package-version cache.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/> or
    ///     <paramref name="cache"/> is <see langword="null"/>.</exception>
    public SelectPackageWindowViewModel(string sourceDirectory, PackageVersionCache cache)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(cache);

        _sourceDirectory = sourceDirectory;
        _cache = cache;

        PackageNames = _cache.GetPackageNames(sourceDirectory);

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
    }

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
                : _cache.GetVersionsDescending(_sourceDirectory, value)
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
