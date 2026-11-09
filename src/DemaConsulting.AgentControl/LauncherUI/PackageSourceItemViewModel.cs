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
using Material.Icons;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     One row of the Settings window's list of package sources: a source's alias, kind icon and
///     location, and whether its folder can currently be found.
/// </summary>
/// <remarks>
///     The folder check can be slow (a network share that does not answer), so it is never run
///     when the row is created: <see cref="CheckStatusAsync"/> runs it on a worker thread and the
///     row shows a "checking" state until it finishes. Not thread-safe; like every view model it
///     is created and read on the UI thread, and the awaited check resumes there.
/// </remarks>
internal sealed class PackageSourceItemViewModel : ViewModelBase
{
    /// <summary>
    ///     Backing field for <c>IsChecking</c>: true until the folder check has finished.
    /// </summary>
    private bool _isChecking = true;

    /// <summary>
    ///     Whether the folder was found by the status check; meaningful once <c>IsChecking</c> is false.
    /// </summary>
    private bool _folderFound;

    /// <summary>
    ///     Initializes a new <see cref="PackageSourceItemViewModel"/>: in the "checking" state for a
    ///     folder source, and straight to "online source" for a server (which is only contacted when
    ///     needed, never merely to list it).
    /// </summary>
    /// <param name="definition">The source this row shows; the row keeps this instance, so the
    ///     owner must pass a copy it is willing to let the row hold.</param>
    /// <param name="pinnedRepoCount">How many recent repos are pinned to this source; used only
    ///     to describe the effect of removing it.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is
    ///     <see langword="null"/>.</exception>
    public PackageSourceItemViewModel(PackageSourceDefinition definition, int pinnedRepoCount)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Definition = definition;
        PinnedRepoCount = pinnedRepoCount;

        // A remote source needs the network (and perhaps a sign-in) to be checked, so the row never
        // does that on its own; the editor's "Test connection" is the way to check one
        if (IsRemote)
        {
            _isChecking = false;
        }
    }

    /// <summary>
    ///     Gets the source this row shows.
    /// </summary>
    public PackageSourceDefinition Definition { get; }

    /// <summary>
    ///     Gets a value indicating whether the source is a server (an address) rather than a
    ///     folder.
    /// </summary>
    public bool IsServer => Definition.Type == PackageSourceType.NuGet &&
                            Definition.LocationKind == PackageSourceLocationKind.Server;

    /// <summary>
    ///     Gets a value indicating whether the source is only reachable through the network: a NuGet
    ///     server or a Git repository that is not a local path. Such a source has no folder to look
    ///     for, so its row says "Online source" (a neutral, informational status, not a warning)
    ///     instead of a folder status.
    /// </summary>
    public bool IsRemote => IsServer ||
                            (Definition.Type == PackageSourceType.Git &&
                             !(GitRepositoryAddress.TryParse(Definition.Location, out var address, out _) &&
                               address?.Kind == GitAddressKind.Local));

    /// <summary>
    ///     Gets how many recent repos are pinned to this source.
    /// </summary>
    public int PinnedRepoCount { get; }

    /// <summary>
    ///     Gets the source's alias.
    /// </summary>
    public string Alias => Definition.Alias;

    /// <summary>
    ///     Gets the source's location, shown shortened with the full text in a tooltip.
    /// </summary>
    public string Location => Definition.Location;

    /// <summary>
    ///     Gets the icon showing the kind of source.
    /// </summary>
    public MaterialIconKind TypeIcon => PackageSourceIcons.GetIcon(Definition);

    /// <summary>
    ///     Gets the name of the kind of source, shown beside the alias and in the icon's tooltip.
    /// </summary>
    public string TypeName => PackageSourceIcons.GetTypeName(Definition);

    /// <summary>
    ///     Gets a value indicating whether the folder check is still running.
    /// </summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetField(ref _isChecking, value))
            {
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the folder check finished and found the folder (always
    ///     <see langword="false"/> for a server, which has no folder).
    /// </summary>
    public bool IsAvailable => !_isChecking && !IsRemote && _folderFound;

    /// <summary>
    ///     Gets a value indicating whether the folder check finished and did not find the folder
    ///     (always <see langword="false"/> for a server).
    /// </summary>
    public bool IsMissing => !_isChecking && !IsRemote && !_folderFound;

    /// <summary>
    ///     Gets the short status text shown at the end of the row.
    /// </summary>
    public string StatusText => GetStatusText();

    /// <summary>
    ///     Gets the icon shown beside <see cref="StatusText"/>.
    /// </summary>
    public MaterialIconKind StatusIcon
    {
        get
        {
            if (IsRemote)
            {
                return MaterialIconKind.CloudOutline;
            }

            if (_isChecking)
            {
                return MaterialIconKind.ProgressClock;
            }

            return _folderFound ? MaterialIconKind.CheckCircleOutline : MaterialIconKind.AlertCircleOutline;
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the source requires signed packages, so its row shows
    ///     the "Signed only" badge. Only a NuGet source (folder or server) can require signatures;
    ///     a zip-folder or Git source never shows the badge.
    /// </summary>
    public bool RequiresSignature =>
        Definition is { Type: PackageSourceType.NuGet, RequireSignedPackages: true };

    /// <summary>
    ///     Gets the tooltip of the "Signed only" badge: that signed packages are required and, when
    ///     the source is pinned to one publisher, that only that publisher is accepted, naming it
    ///     by the start and end of its certificate fingerprint (never the whole fingerprint).
    /// </summary>
    public string SignatureTooltip =>
        PackageSourceDefinition.TryNormalizeFingerprint(Definition.SignerFingerprint, out var fingerprint)
            ? "Signed packages are required. Only packages signed by the pinned publisher " +
              $"(certificate {NuGetSourceProvider.SummarizeFingerprint(fingerprint)}) are accepted."
            : "Signed packages are required. Any validly signed package from a trusted publisher is accepted.";

    /// <summary>
    ///     Gets the tooltip explaining the status.
    /// </summary>
    public string StatusTooltip
    {
        get
        {
            if (IsRemote)
            {
                return "AgentControl contacts this source only when it needs it (selecting a package, " +
                       "checking for upgrades, syncing at launch). Use Edit > Test connection to check it now.";
            }

            if (_isChecking)
            {
                return Definition.Type == PackageSourceType.Git
                    ? "Checking whether the repository folder can be reached..."
                    : "Checking whether the folder can be reached...";
            }

            return _folderFound
                ? "The folder exists and can be reached."
                : "The folder does not exist or cannot be reached right now (for example a network " +
                  "share that is offline). Repos pinned to this source cannot be upgraded until it is reachable.";
        }
    }

    /// <summary>
    ///     Describes the status of the folder check in a few words.
    /// </summary>
    /// <returns>The status text.</returns>
    private string GetStatusText()
    {
        if (IsRemote)
        {
            return "Online source";
        }

        if (_isChecking)
        {
            return "Checking...";
        }

        return _folderFound ? "Folder found" : "Folder not found";
    }

    /// <summary>
    ///     Checks, without blocking the caller, whether the source's folder exists, and updates
    ///     the status.
    /// </summary>
    /// <param name="folderExists">Tells whether a folder exists; invoked on a worker thread
    ///     because a network path may take a long time to answer.</param>
    /// <param name="cancellationToken">Cancels waiting for the result (for example when the
    ///     window closes); the row then keeps its "checking" state.</param>
    /// <returns>A task that completes when the status has been updated or the wait was
    ///     cancelled.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderExists"/> is
    ///     <see langword="null"/>.</exception>
    public async Task CheckStatusAsync(
        Func<string, CancellationToken, Task<bool>> folderExists, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folderExists);

        // A remote source has no folder to look for; its row is already in its final state
        if (IsRemote)
        {
            return;
        }

        bool found;
        try
        {
            found = await folderExists(Definition.Location, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A path that cannot even be examined is as unusable as one that does not exist
            found = false;
        }

        _folderFound = found;
        IsChecking = false;
    }

    /// <summary>
    ///     Gets the text UI Automation (screen readers, tests) reads for the whole row: the alias, the
    ///     kind of source and its status, because the row's visible parts are separate text elements.
    /// </summary>
    public string AutomationName => $"{Alias}, {TypeName}, {StatusText}";

    /// <summary>
    ///     Returns <see cref="AutomationName"/>: a list box names each row for UI Automation by the
    ///     row object's text, so this is what a screen reader or a test reads for the row.
    /// </summary>
    /// <returns>The row's automation name.</returns>
    public override string ToString() => AutomationName;

    /// <summary>
    ///     Raises change notifications for every property derived from the status.
    /// </summary>
    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(AutomationName));
    }
}
