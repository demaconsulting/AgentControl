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

using System.Globalization;
using Avalonia.Media.Imaging;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Utilities;
using Material.Icons;
using Microsoft.Extensions.Logging;
using AgentLauncher = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for a single card in the main window's recent-repos list: describes the repo
///     with a row of chips (agent package and its source, available upgrade, branch, status
///     warnings) and exposes the Launch/Git Pull/Upgrade/Select Package actions.
/// </summary>
/// <remarks>
///     <para>
///     All business logic (upgrade detection, launch orchestration, pull-eligibility checks) is
///     implemented here or delegated to the Phase 1 subsystems (<see cref="GitClient"/>,
///     <see cref="PackageSourceRegistry"/>, <see cref="PackageContentExtractor"/>,
///     <see cref="GitIgnoreEnsurer"/>, <see cref="RepoPinStore"/>, <see cref="AgentLauncher"/>)
///     rather than in any view
///     code-behind, so this class is fully unit-testable without an Avalonia window.
///     </para>
///     <para>
///     Everything that reads a package source or copies package content is asynchronous: the
///     commands are <see cref="AsyncRelayCommand"/>s, so the window stays responsive while a
///     source is slow or unreachable, only one package operation per card runs at a time
///     (<see cref="IsBusy"/>), and the user can cancel a running operation
///     (<see cref="CancelOperationCommand"/>). Not thread-safe; every member is expected to be
///     called from the UI thread, consistent with <see cref="ViewModelBase"/>.
///     </para>
/// </remarks>
internal sealed class RepoCardViewModel : ViewModelBase
{
    /// <summary>
    ///     Well-known agent tool commands for the non-<see cref="AgentToolKind.Custom"/>
    ///     <see cref="AgentToolKind"/> values, matching each tool's published CLI command name.
    /// </summary>
    private static readonly Dictionary<AgentToolKind, string> WellKnownAgentCommands = new()
    {
        [AgentToolKind.CopilotCli] = "copilot",
        [AgentToolKind.Cursor] = "cursor",
        [AgentToolKind.ClaudeCode] = "claude"
    };

    /// <summary>
    ///     File name of the optional, user-customizable root-level AGENTS.md template a package
    ///     may offer to place at a repo's root - never one of the four managed folders.
    /// </summary>
    private const string AgentsMdFileName = "AGENTS.md";

    /// <summary>
    ///     The shared recent-repos settings entry this card represents. Held by reference (not
    ///     copied) so that mutations here (favorite toggle, launch timestamp, refreshed pin
    ///     fields) are visible immediately to <see cref="MainWindowViewModel"/>'s settings
    ///     persistence without any extra synchronization step.
    /// </summary>
    private readonly RecentRepo _recentRepo;

    /// <summary>
    ///     Supplies the current <see cref="AppSettings"/> at the moment an action runs, rather
    ///     than a snapshot captured at construction time, so edits made in
    ///     <see cref="SettingsWindowViewModel"/> take effect on the very next action without
    ///     requiring every card to be recreated.
    /// </summary>
    private readonly Func<AppSettings> _getSettings;

    /// <summary>
    ///     The shared, session-scoped package-version cache, owned by
    ///     <see cref="MainWindowViewModel"/> and passed to every card so a package source scan is
    ///     performed at most once per app session (or until explicitly invalidated), per
    ///     architecture.md's repo-fact caching strategy.
    /// </summary>
    private readonly PackageVersionCache _packageVersionCache;

    /// <summary>
    ///     Per-card cache of the "committed agent files" badge result, keyed by this repo's
    ///     current <c>HEAD</c> commit hash, per architecture.md's repo-fact caching strategy.
    /// </summary>
    private readonly CommittedAgentFilesCache _committedAgentFilesCache = new();

    /// <summary>
    ///     Test-only startup overrides consulted as a first-run fallback for the git executable
    ///     path and agent tool command, per architecture.md's testability strategy.
    /// </summary>
    private readonly Startup.StartupOptions? _startupOptions;

    /// <summary>
    ///     Builds the registry of configured sources from the current settings; replaced in tests
    ///     by one that supplies in-memory sources.
    /// </summary>
    private readonly Func<AppSettings, PackageSourceRegistry>? _registryFactory;

    /// <summary>
    ///     The credential facilities (session store and sign-in prompt) NuGet server sources use,
    ///     or <see langword="null"/> when there are none.
    /// </summary>
    private readonly PackageCredentialContext? _credentials;

    /// <summary>
    ///     The longest agent tool name (in characters) that is spelled out in the Launch button
    ///     label; a longer name would make the button too wide, so the label falls back to a plain
    ///     "Launch".
    /// </summary>
    private const int MaxLaunchToolNameLength = 10;

    /// <summary>
    ///     Cancels the package operation currently running for this card (the one that holds
    ///     <see cref="IsBusy"/>), or <see langword="null"/> when none is running.
    /// </summary>
    private CancellationTokenSource? _operationCancellation;

    /// <summary>
    ///     Identifies the most recently started upgrade-availability check, so a slower, older
    ///     check (for example one begun before the user edited the sources) cannot overwrite the
    ///     result of a newer one.
    /// </summary>
    private int _upgradeCheckGeneration;

    private bool _isMissing;
    private bool _isUpgradeAvailable;
    private string? _latestAvailableVersion;
    private string? _sourceProblem;
    private bool _sourceNeedsSignIn;
    private string? _currentBranch;
    private bool _hasCommittedAgentFiles;
    private bool _canPull;
    private bool _gitStatusUnavailable;
    private bool _gitStatusChecked;
    private string? _statusMessage;
    /// <summary>Backing field for <see cref="IsBusy"/>.</summary>
    private bool _isBusy;

    /// <summary>Backing field for <see cref="CanCancelOperation"/>.</summary>
    private bool _canCancelOperation;

    /// <summary>Backing field for <see cref="BusyMessage"/>.</summary>
    private string? _busyMessage;

    /// <summary>Backing field for <see cref="IsCheckingUpgrade"/>.</summary>
    private bool _isCheckingUpgrade;

    /// <summary>Backing field for <see cref="LastActionFailed"/>.</summary>
    private bool _lastActionFailed;

    /// <summary>
    ///     Decodes the repo's picture file for the avatar tile; see the constructor.
    /// </summary>
    private readonly Func<RepoImageFile, Bitmap?> _decodeImage;

    /// <summary>
    ///     The picture file whose decode result (a bitmap, or a failure) <see cref="RepoImage"/>
    ///     currently reflects, so a refresh that finds the same file (path, last-write time and
    ///     size) does not decode it again; <see langword="null"/> when there is no picture file.
    /// </summary>
    private RepoImageFile? _loadedImageFile;

    /// <summary>
    ///     Identifies the most recent picture refresh, so a slower, older one cannot overwrite the
    ///     result of a newer one.
    /// </summary>
    private int _imageGeneration;

    /// <summary>Backing field for <see cref="RepoImage"/>.</summary>
    private Bitmap? _repoImage;

    /// <summary>
    ///     Logs the actual work this card performs on behalf of the user - fetching/applying
    ///     packages, pulling the repo, and launching the agent tool - so an operator can audit
    ///     what happened (and when) from the log file, not just from transient UI status text.
    /// </summary>
    private readonly ILogger _logger = new SecretRedactingLogger(AppLogging.Factory.CreateLogger<RepoCardViewModel>());

    /// <summary>
    ///     Initializes a new <see cref="RepoCardViewModel"/> for a single recent repo.
    /// </summary>
    /// <param name="recentRepo">The shared recent-repos settings entry this card represents.</param>
    /// <param name="getSettings">Supplies the current <see cref="AppSettings"/> on demand.</param>
    /// <param name="packageVersionCache">The shared, session-scoped package-version cache.</param>
    /// <param name="startupOptions">Test-only startup overrides, or <see langword="null"/>.</param>
    /// <param name="registryFactory">Builds the registry of configured sources from the settings;
    ///     <see langword="null"/> uses the real providers. Supplied so tests can substitute
    ///     in-memory sources whose timing they control.</param>
    /// <param name="credentials">The credential facilities NuGet server sources use to sign in,
    ///     or <see langword="null"/> for none.</param>
    /// <param name="decodeImage">Decodes the repo's picture file for the avatar tile;
    ///     <see langword="null"/> uses <see cref="RepoImageLocator.Decode"/>. Supplied so tests can
    ///     count decodes or avoid needing a graphics platform.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recentRepo"/>,
    ///     <paramref name="getSettings"/>, or <paramref name="packageVersionCache"/> is
    ///     <see langword="null"/>.</exception>
    public RepoCardViewModel(
        RecentRepo recentRepo,
        Func<AppSettings> getSettings,
        PackageVersionCache packageVersionCache,
        Startup.StartupOptions? startupOptions = null,
        Func<AppSettings, PackageSourceRegistry>? registryFactory = null,
        PackageCredentialContext? credentials = null,
        Func<RepoImageFile, Bitmap?>? decodeImage = null)
    {
        ArgumentNullException.ThrowIfNull(recentRepo);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(packageVersionCache);

        _recentRepo = recentRepo;
        _getSettings = getSettings;
        _packageVersionCache = packageVersionCache;
        _startupOptions = startupOptions;
        _registryFactory = registryFactory;
        _credentials = credentials;
        _decodeImage = decodeImage ?? (file => RepoImageLocator.Decode(file));

        LaunchCommand = new AsyncRelayCommand(LaunchAsync, ReportUnexpectedFailure, () => !IsMissing && !IsBusy);
        PullCommand = new AsyncRelayCommand(PullAsync, ReportUnexpectedFailure, () => CanPull && !IsMissing && !IsBusy);
        UpgradeCommand = new AsyncRelayCommand(
            UpgradeAsync, ReportUnexpectedFailure, () => IsUpgradeAvailable && !IsMissing && !IsBusy);
        SelectPackageCommand = new RelayCommand(
            SelectPackage, () => IsPackageSelectionNeeded && !IsMissing && !IsBusy);
        // Refresh stays available for a repo whose folder is missing: it is the only in-app way to
        // re-check once the folder (a remounted drive, a restored clone) is back
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportUnexpectedFailure, () => !IsBusy);
        NewGroupCommand = new RelayCommand(() => NewGroupRequested?.Invoke(this, EventArgs.Empty));
        RemoveFromGroupCommand = new RelayCommand(() => MoveToGroup(null), () => HasGroup);
        RemoveCommand = new RelayCommand(() => RemoveRequested?.Invoke(this, EventArgs.Empty));
        CancelOperationCommand = new RelayCommand(CancelOperation, () => CanCancelOperation);

        // Every error the card reports to the user also marks the card's status as a problem, so
        // the status accent needs no bookkeeping at the dozens of places that raise an error
        ErrorOccurred += (_, _) => LastActionFailed = true;
    }

    /// <summary>
    ///     Gets the absolute filesystem path to the repository root.
    /// </summary>
    /// <remarks>
    ///     Fixed for the card's lifetime - a repo card is never "moved" to a different path, so
    ///     no setter is exposed.
    /// </remarks>
    public string RepoPath => _recentRepo.Path;

    /// <summary>
    ///     Gets the repo's display name (its folder name), for the card's title.
    /// </summary>
    public string RepoName => Path.GetFileName(RepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    ///     Gets a value indicating whether the repo path no longer exists on disk (deleted, or an
    ///     unmounted network/removable drive), per architecture.md's "Missing" badge.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true"/>, every other check/action short-circuits to its
    ///     "unavailable" default rather than attempting a git/filesystem operation against a path
    ///     that does not exist. A missing repo is never auto-removed from the list (the path may
    ///     reappear, e.g. a removable drive being reconnected) - removal is always an explicit,
    ///     confirmed user action via <see cref="RemoveCommand"/>.
    /// </remarks>
    public bool IsMissing
    {
        get => _isMissing;
        private set
        {
            if (SetField(ref _isMissing, value))
            {
                LaunchCommand.RaiseCanExecuteChanged();
                PullCommand.RaiseCanExecuteChanged();
                UpgradeCommand.RaiseCanExecuteChanged();
                SelectPackageCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(RefreshMenuText));
                OnPropertyChanged(nameof(RefreshTooltip));
                OnPropertyChanged(nameof(IsWorkingTreeDirty));
                OnPropertyChanged(nameof(PullTooltip));
                OnPropertyChanged(nameof(HasPinnedPackage));
                OnPropertyChanged(nameof(ShowNoPackageChip));
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    ///     Gets or sets whether the user has pinned/favorited this repo.
    /// </summary>
    /// <remarks>
    ///     Mutates the shared <see cref="RecentRepo"/> in place and raises
    ///     <see cref="FavoriteChanged"/> so the owning <see cref="MainWindowViewModel"/> can
    ///     persist settings and re-sort <see cref="MainWindowViewModel.DisplayedRepoCards"/>.
    ///     This property is the <b>only</b> owner of the toggle state: the card's star is a
    ///     <c>ToggleButton</c> whose <c>IsChecked</c> binds here two-way, and it has no command.
    ///     An earlier design also bound a toggling command, so one click flipped the state twice
    ///     (the toggle itself, then the command) and a repo could not be favorited or unfavorited.
    /// </remarks>
    public bool IsFavorite
    {
        get => _recentRepo.IsFavorite;
        set
        {
            if (_recentRepo.IsFavorite == value)
            {
                return;
            }

            _recentRepo.IsFavorite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGroupChip));
            FavoriteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///     Gets the name of the user's own group this repo is listed under, or
    ///     <see langword="null"/> when it is in no group.
    /// </summary>
    /// <remarks>
    ///     Read through <see cref="RepoGroupName.Sanitize"/>, so whatever the settings hold (even
    ///     text that was not repaired at load, as in a test that builds settings directly) yields a
    ///     valid name or no group, never a failure. Change it with <see cref="MoveToGroup"/>.
    /// </remarks>
    public string? GroupName => RepoGroupName.Sanitize(_recentRepo.GroupName);

    /// <summary>
    ///     Gets a value indicating whether this repo is in one of the user's own groups.
    /// </summary>
    public bool HasGroup => GroupName is not null;

    /// <summary>
    ///     Gets a value indicating whether the card shows its group's name as a chip. A favorite repo
    ///     is listed under Favorites rather than under its group, so the chip is what tells the user
    ///     which group it belongs to; for any other repo the section header already says so.
    /// </summary>
    public bool ShowGroupChip => IsFavorite && HasGroup;

    /// <summary>
    ///     Gets the names of the groups that exist now (the groups of all repos in the settings),
    ///     sorted, for the "Move to group" submenu.
    /// </summary>
    /// <returns>The distinct group names, alphabetically.</returns>
    public IReadOnlyList<string> GetExistingGroupNames() =>
        RepoGroupName.DistinctNames(_getSettings().RecentRepos.Select(repo => repo.GroupName));

    /// <summary>
    ///     Moves this repo into a group, or out of every group.
    /// </summary>
    /// <remarks>
    ///     A name that matches an existing group ignoring letter case joins that group under its own
    ///     spelling, so look-alike groups cannot arise. The choice is stored only in the per-user
    ///     settings (on the shared <see cref="RecentRepo"/>), never in the repository. Raises
    ///     <see cref="GroupChanged"/> when the group actually changed, so the owning
    ///     <see cref="MainWindowViewModel"/> persists the settings and rebuilds the list.
    /// </remarks>
    /// <param name="groupName">The group's name, or <see langword="null"/> (or blank) to take the
    ///     repo out of its group.</param>
    /// <returns><see langword="true"/> when the repo is now in the requested place (including when
    ///     it already was); <see langword="false"/> when <paramref name="groupName"/> is not a valid
    ///     group name (too long, or a built-in section's name) and nothing changed.</returns>
    public bool MoveToGroup(string? groupName)
    {
        // Blank means "no group"; anything else must be a name the rules accept
        var requested = RepoGroupName.Normalize(groupName);
        if (requested is not null && RepoGroupName.Validate(requested) != RepoGroupNameProblem.None)
        {
            return false;
        }

        var target = requested is null ? null : RepoGroupName.Canonicalize(requested, GetExistingGroupNames());
        if (string.Equals(_recentRepo.GroupName, target, StringComparison.Ordinal))
        {
            return true;
        }

        _recentRepo.GroupName = target;
        RaiseGroupPropertiesChanged();
        GroupChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    ///     Tells the view that this card's group properties may have changed because the group was
    ///     renamed or removed from outside the card (by the owning view model).
    /// </summary>
    internal void RaiseGroupPropertiesChanged()
    {
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(HasGroup));
        OnPropertyChanged(nameof(ShowGroupChip));
        RemoveFromGroupCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    ///     Gets the command that asks for a new group to move this repo into; it raises
    ///     <see cref="NewGroupRequested"/> (the window shows the name prompt).
    /// </summary>
    public RelayCommand NewGroupCommand { get; }

    /// <summary>
    ///     Gets the command that takes this repo out of its group; available while the repo is in one.
    /// </summary>
    public RelayCommand RemoveFromGroupCommand { get; }

    /// <summary>
    ///     Gets the name of the palette color the user chose for this repo's tile, or
    ///     <see langword="null"/> for the automatic color (including when the stored name is not a
    ///     color this version knows).
    /// </summary>
    public string? CardColorName => CardColorPalette.Find(_recentRepo.CardColor)?.Name;

    /// <summary>
    ///     Gets a value indicating whether the user chose a color for this repo's tile.
    /// </summary>
    public bool HasCustomCardColor => CardColorName is not null;

    /// <summary>
    ///     Gets a value indicating whether the tile uses the automatic color (the opposite of
    ///     <see cref="HasCustomCardColor"/>), for the palette popup's "Automatic" entry.
    /// </summary>
    public bool IsCardColorAutomatic => !HasCustomCardColor;

    /// <summary>
    ///     Gets the palette heading's text: <c>Card color: Teal</c>, or <c>Card color: Automatic</c>,
    ///     so the chosen color is named in words in the palette itself and not only by a swatch's
    ///     tooltip, accessible name and check mark.
    /// </summary>
    public string CardColorHeading => $"Card color: {CardColorName ?? "Automatic"}";

    /// <summary>
    ///     Chooses the color of this repo's tile.
    /// </summary>
    /// <remarks>
    ///     Only the tile changes: the status accent bar keeps its fixed, meaningful colors. The choice
    ///     is stored by name in the per-user settings only, and raises <see cref="CardColorChanged"/>
    ///     so the owning <see cref="MainWindowViewModel"/> persists it.
    /// </remarks>
    /// <param name="colorName">A palette color's name (letter case ignored), or
    ///     <see langword="null"/> (or blank) for the automatic color.</param>
    /// <returns><see langword="true"/> when the card now has the requested color (including when it
    ///     already had); <see langword="false"/> when <paramref name="colorName"/> names no palette
    ///     color and nothing changed.</returns>
    public bool SetCardColor(string? colorName)
    {
        var requested = string.IsNullOrWhiteSpace(colorName) ? null : CardColorPalette.Find(colorName);
        if (requested is null && !string.IsNullOrWhiteSpace(colorName))
        {
            return false;
        }

        if (string.Equals(_recentRepo.CardColor, requested?.Name, StringComparison.Ordinal))
        {
            return true;
        }

        _recentRepo.CardColor = requested?.Name;
        OnPropertyChanged(nameof(CardColorName));
        OnPropertyChanged(nameof(HasCustomCardColor));
        OnPropertyChanged(nameof(IsCardColorAutomatic));
        OnPropertyChanged(nameof(CardColorHeading));
        OnPropertyChanged(nameof(AvatarColorIndex));
        OnPropertyChanged(nameof(AvatarStyleClass));
        CardColorChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    ///     Gets the UTC timestamp this repo was last successfully launched, or
    ///     <see langword="null"/> if it has never been launched.
    /// </summary>
    public DateTimeOffset? LastLaunchedUtc => _recentRepo.LastLaunchedUtc;

    /// <summary>
    ///     Gets the pinned agent package's name, or <see langword="null"/> if the repo has never
    ///     been synced.
    /// </summary>
    public string? PinnedPackageName
    {
        get => _recentRepo.PinnedPackageName;
        private set
        {
            if (_recentRepo.PinnedPackageName == value)
            {
                return;
            }

            _recentRepo.PinnedPackageName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPackageSelectionNeeded));
            RaisePackageChipChanged();
            SelectPackageCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    ///     Gets the pinned agent package's version, or <see langword="null"/> if the repo has
    ///     never been synced.
    /// </summary>
    public string? PinnedPackageVersion
    {
        get => _recentRepo.PinnedPackageVersion;
        private set
        {
            if (_recentRepo.PinnedPackageVersion == value)
            {
                return;
            }

            _recentRepo.PinnedPackageVersion = value;
            OnPropertyChanged();
            RaisePackageChipChanged();
        }
    }

    /// <summary>
    ///     Gets the alias of the package source the pinned package comes from, or
    ///     <see langword="null"/> if the repo has never been synced. A pin file written before
    ///     sources existed reports the legacy <c>Default</c> alias.
    /// </summary>
    public string? PinnedSource
    {
        get => _recentRepo.PinnedSource;
        private set
        {
            if (_recentRepo.PinnedSource == value)
            {
                return;
            }

            _recentRepo.PinnedSource = value;
            OnPropertyChanged();
            RaisePackageChipChanged();
        }
    }

    /// <summary>
    ///     Gets a user-readable description of why the pinned package's source cannot be used (for
    ///     example the pin names a source that is not configured), or <see langword="null"/> when
    ///     there is no such problem.
    /// </summary>
    /// <remarks>
    ///     This is the card's error state for a pin whose source alias is missing from the
    ///     configured sources. The card never substitutes another source: upgrade checking is
    ///     disabled and every action that needs the source reports this problem instead.
    /// </remarks>
    public string? SourceProblem
    {
        get => _sourceProblem;
        private set
        {
            if (SetField(ref _sourceProblem, value))
            {
                OnPropertyChanged(nameof(HasSourceProblem));
                OnPropertyChanged(nameof(SourceProblemChipText));
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="SourceProblem"/> is set, for binding the
    ///     card's error indicator.
    /// </summary>
    public bool HasSourceProblem => _sourceProblem is not null;

    /// <summary>
    ///     Gets a value indicating whether the source problem is that the pinned package's server
    ///     needs a sign-in (which the background check never asks for), so the card can say so on
    ///     the chip itself instead of the generic "Package source problem".
    /// </summary>
    public bool SourceNeedsSignIn => _sourceProblem is not null && _sourceNeedsSignIn;

    /// <summary>
    ///     Gets the text of the card's red source-problem chip: <c>Sign-in needed</c> when the
    ///     server needs a sign-in, otherwise <c>Package source problem</c>.
    /// </summary>
    public string SourceProblemChipText => SourceNeedsSignIn ? "Sign-in needed" : "Package source problem";

    /// <summary>
    ///     Gets the repo's current git branch name, or <see langword="null"/> if it could not be
    ///     determined (not a git repository, missing repo, etc.).
    /// </summary>
    public string? CurrentBranch
    {
        get => _currentBranch;
        private set
        {
            if (SetField(ref _currentBranch, value))
            {
                OnPropertyChanged(nameof(HasBranch));
                OnPropertyChanged(nameof(BranchTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the current branch is known, i.e. whether the branch
    ///     chip is shown.
    /// </summary>
    public bool HasBranch => !string.IsNullOrEmpty(_currentBranch);

    /// <summary>
    ///     Gets the tooltip of the branch chip, which says what the chip shows.
    /// </summary>
    public string BranchTooltip => $"Current git branch: {CurrentBranch}";

    /// <summary>
    ///     Gets a value indicating whether one or more of the four known agent folders are
    ///     tracked by git at <c>HEAD</c> in this repo, per architecture.md's "Committed agent
    ///     files" warning badge.
    /// </summary>
    public bool HasCommittedAgentFiles
    {
        get => _hasCommittedAgentFiles;
        private set => SetField(ref _hasCommittedAgentFiles, value);
    }

    /// <summary>
    ///     Gets the highest version discovered at the configured package source, or
    ///     <see langword="null"/> if none was found (e.g. no package source configured, or the
    ///     source is unreachable).
    /// </summary>
    public string? LatestAvailableVersion
    {
        get => _latestAvailableVersion;
        private set
        {
            if (SetField(ref _latestAvailableVersion, value))
            {
                OnPropertyChanged(nameof(UpgradeTooltip));
                OnPropertyChanged(nameof(UpgradeChipText));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether a newer package version than
    ///     <see cref="PinnedPackageVersion"/> was discovered at the pinned package's source.
    /// </summary>
    /// <remarks>
    ///     Drives both the upgrade chip's visibility in <c>MainWindow.axaml</c> and the
    ///     "Upgrade" menu item's visibility/enablement.
    /// </remarks>
    public bool IsUpgradeAvailable
    {
        get => _isUpgradeAvailable;
        private set
        {
            if (SetField(ref _isUpgradeAvailable, value))
            {
                OnPropertyChanged(nameof(UpgradeTooltip));
                UpgradeCommand.RaiseCanExecuteChanged();
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the tooltip text shown on the upgrade chip and the "Upgrade" menu item: what
    ///     clicking does (upgrade the agent files to the newer version), the version it replaces
    ///     and that the chip and the "..." menu start the same upgrade, or <see langword="null"/>
    ///     when no upgrade is available.
    /// </summary>
    public string? UpgradeTooltip => IsUpgradeAvailable
        ? $"Upgrade the agent files to {LatestAvailableVersion} (pinned: {PinnedPackageVersion}). " +
          "Same as the \"...\" menu > Upgrade."
        : null;

    /// <summary>
    ///     Gets a value indicating whether the last action the user started on this card (launch,
    ///     pull, upgrade, package selection or refresh) reported an error.
    /// </summary>
    /// <remarks>
    ///     Set whenever <see cref="ErrorOccurred"/> is raised or a Git Pull ends with git's
    ///     refusal, and cleared when the next operation starts, so a problem stays visible on the
    ///     card (as <see cref="RepoCardStatus.Problem"/>) until the user tries again.
    /// </remarks>
    public bool LastActionFailed
    {
        get => _lastActionFailed;
        private set
        {
            if (SetField(ref _lastActionFailed, value))
            {
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the overall status of the card, which drives the colored accent bar on its left
    ///     edge.
    /// </summary>
    /// <remarks>
    ///     A problem (missing folder, package source problem or sign-in needed, failed last action)
    ///     outranks everything else, then an available upgrade, then the absence of a package;
    ///     a pinned package whose upgrade check is still running is neutral until the check ends,
    ///     and only a pinned package with no newer version reports <see cref="RepoCardStatus.UpToDate"/>.
    /// </remarks>
    public RepoCardStatus Status => this switch
    {
        { IsMissing: true } or { HasSourceProblem: true } or { LastActionFailed: true } => RepoCardStatus.Problem,
        { IsUpgradeAvailable: true } => RepoCardStatus.UpgradeAvailable,
        { IsPackageSelectionNeeded: true } => RepoCardStatus.NoPackage,
        { IsCheckingUpgrade: true } => RepoCardStatus.Checking,
        _ => RepoCardStatus.UpToDate
    };

    /// <summary>
    ///     Gets a value indicating whether the card menu's "Agent files" group has anything to offer:
    ///     "Select Package..." (no package pinned yet) or "Upgrade to ..." (a newer version exists).
    ///     When neither applies the group, its header and its separator are hidden rather than shown
    ///     empty.
    /// </summary>
    public bool HasAgentFileActions => IsPackageSelectionNeeded || IsUpgradeAvailable;

    /// <summary>
    ///     Gets the status in words, for the accent bar's accessible name so the status is never
    ///     conveyed by color alone.
    /// </summary>
    public string StatusText => Status switch
    {
        RepoCardStatus.UpToDate => "Up to date",
        RepoCardStatus.UpgradeAvailable => "Upgrade available",
        RepoCardStatus.Problem => "Needs attention",
        RepoCardStatus.NoPackage => "No agent package",
        _ => "Checking for updates"
    };

    /// <summary>
    ///     Gets the tooltip of the accent bar: the status and, for a problem, the reason.
    /// </summary>
    public string StatusTooltip => Status switch
    {
        RepoCardStatus.UpToDate => "Up to date: the pinned agent package is the newest one available.",
        RepoCardStatus.UpgradeAvailable => $"Upgrade available: {PinnedPackageVersion} -> {LatestAvailableVersion}.",
        RepoCardStatus.NoPackage => "No agent package is pinned for this repo.",
        RepoCardStatus.Checking => "Checking whether a newer agent package version is available...",
        _ when IsMissing => "Needs attention: this repo's folder could not be found on disk. Use Check again in the card menu once it is back.",
        _ when HasSourceProblem => $"Needs attention: {SourceProblem}",
        _ => "Needs attention: the last action failed."
    };

    /// <summary>
    ///     The number of colors in the avatar palette. The view defines one background and one
    ///     foreground resource per index (<c>AvatarBackground0</c>, <c>AvatarForeground0</c>, ...)
    ///     for each theme variant.
    /// </summary>
    internal const int AvatarPaletteSize = 10;

    /// <summary>
    ///     Gets the one or two capital letters shown in the card's avatar tile: the first letters of
    ///     the first two words of the repo name (words are separated by anything that is not a
    ///     letter or digit, so <c>alpha-service</c> gives <c>AS</c>), or the first letter of a
    ///     single-word name. Empty when the name has no letter or digit.
    /// </summary>
    public string AvatarInitials => GetAvatarInitials(RepoName);

    /// <summary>
    ///     Gets a value indicating whether <see cref="AvatarInitials"/> has text; when not, the
    ///     avatar shows a folder glyph instead.
    /// </summary>
    public bool HasAvatarInitials => AvatarInitials.Length > 0;

    /// <summary>
    ///     Gets the repo's own picture (its root <c>icon.png</c>, else <c>logo.png</c>) decoded at the
    ///     tile's size, or <see langword="null"/> when the repo has none, it is not acceptable, or
    ///     it has not finished loading; the tile then shows the generated initials instead.
    /// </summary>
    /// <remarks>
    ///     Loaded in the background by <see cref="RefreshCheap"/> (so at startup and on every
    ///     Refresh) and never on the UI thread; see <see cref="RepoImageLocator"/> for what is
    ///     accepted.
    /// </remarks>
    public Bitmap? RepoImage
    {
        get => _repoImage;
        private set
        {
            if (SetField(ref _repoImage, value))
            {
                OnPropertyChanged(nameof(HasRepoImage));
                OnPropertyChanged(nameof(ShowAvatarInitials));
                OnPropertyChanged(nameof(ShowAvatarFolderGlyph));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the tile shows the repo's own picture.
    /// </summary>
    public bool HasRepoImage => _repoImage is not null;

    /// <summary>
    ///     Gets a value indicating whether the tile shows the generated initials: the repo has no
    ///     picture and its name has initials.
    /// </summary>
    public bool ShowAvatarInitials => !HasRepoImage && HasAvatarInitials;

    /// <summary>
    ///     Gets a value indicating whether the tile shows the folder glyph: the repo has no picture
    ///     and its name has no letter or digit to make initials from.
    /// </summary>
    public bool ShowAvatarFolderGlyph => !HasRepoImage && !HasAvatarInitials;

    /// <summary>
    ///     Gets the task of the most recent picture refresh, so a caller (a test) can wait for it.
    ///     Completes without throwing: every failure means "no picture".
    /// </summary>
    internal Task RepoImageRefreshTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Looks for the repo's picture and decodes it on a worker thread, then publishes it (or
    ///     its absence) to <see cref="RepoImage"/>.
    /// </summary>
    /// <remarks>
    ///     A file already decoded (same path, last-write time and size) is not decoded again, so
    ///     repeated refreshes cost one folder listing. A decode that failed is remembered the same
    ///     way, so an unusable file is not retried until it changes. The publish step runs on the
    ///     caller's context (the UI thread in the application), after the work on the pool.
    /// </remarks>
    /// <returns>A task that completes when the picture state is current.</returns>
    private async Task RefreshRepoImageAsync()
    {
        try
        {
            await LoadRepoImageAsync();
        }
        catch (Exception)
        {
            // Whatever went wrong with a repo's picture file - the locator, the decoder or an
            // injected decoder - the card just keeps its generated tile, and the background task
            // never faults
            _loadedImageFile = null;
            RepoImage = null;
        }
    }

    /// <summary>
    ///     The body of <see cref="RefreshRepoImageAsync"/>, which only adds the "any failure means
    ///     no picture" guard around it.
    /// </summary>
    /// <returns>A task that completes when the picture state is current.</returns>
    private async Task LoadRepoImageAsync()
    {
        var generation = ++_imageGeneration;
        var previous = _loadedImageFile;
        var path = RepoPath;

        // Off the UI thread: the folder listing, the file read and the decode
        var (file, bitmap, decoded) = await Task.Run(() =>
        {
            var found = RepoImageLocator.Find(path);
            return found is { } candidate && candidate != previous
                ? (found, _decodeImage(candidate), true)
                : (found, null, false);
        }, CancellationToken.None);

        // A newer refresh started meanwhile: let it have the last word
        if (generation != _imageGeneration)
        {
            return;
        }

        // Same file as before: nothing to do; no file at all: back to the generated tile
        if (!decoded)
        {
            if (file is null)
            {
                _loadedImageFile = null;
                RepoImage = null;
            }

            return;
        }

        _loadedImageFile = file;
        RepoImage = bitmap;
    }

    /// <summary>
    ///     Gets the index (0 up to <see cref="AvatarPaletteSize"/> - 1) of the palette color of this
    ///     repo's tile: the color the user chose (<see cref="CardColorName"/>), otherwise the
    ///     automatic color, derived from a stable hash of the repo path so the same repo always has
    ///     the same color in every session and on every machine.
    /// </summary>
    public int AvatarColorIndex => CardColorPalette.Find(_recentRepo.CardColor)?.Index ?? GetAvatarColorIndex(RepoPath);

    /// <summary>
    ///     Gets the style class (<c>avatar0</c>, <c>avatar1</c>, ...) that selects this repo's avatar
    ///     colors from the theme-aware palette resources, so no color value lives in the view model
    ///     or the card template.
    /// </summary>
    public string AvatarStyleClass => $"avatar{AvatarColorIndex}";

    /// <summary>
    ///     Derives the avatar's initials from a repo name.
    /// </summary>
    /// <param name="repoName">The repo's folder name; may be empty.</param>
    /// <returns>One or two upper-case letters or digits; an empty string when
    ///     <paramref name="repoName"/> has none.</returns>
    internal static string GetAvatarInitials(string repoName)
    {
        // Split into the runs of letters and digits (so '-', '_', '.' and spaces all separate
        // words), then take the first text element of up to two words
        var words = SplitOnNonAlphanumeric(repoName).Take(2);
        return string.Concat(words.Select(word => StringInfo.GetNextTextElement(word))).ToUpperInvariant();
    }

    /// <summary>
    ///     Splits text into the runs of letters and digits it contains.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <returns>The non-empty runs of letters and digits, in order.</returns>
    private static IEnumerable<string> SplitOnNonAlphanumeric(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isNameChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isNameChar && start < 0)
            {
                start = i;
            }
            else if (!isNameChar && start >= 0)
            {
                yield return text[start..i];
                start = -1;
            }
        }
    }

    /// <summary>
    ///     Maps a repo path to an avatar palette index with a deterministic 32-bit FNV-1a hash.
    /// </summary>
    /// <remarks>
    ///     <see cref="string.GetHashCode()"/> is deliberately not used: it is randomized per process,
    ///     which would give a repo a different color on every launch. The path is compared
    ///     without regard to case and trailing separators, as Windows paths are, so the same
    ///     folder never changes color because of how it was typed.
    /// </remarks>
    /// <param name="repoPath">The repo path (or any name); may be empty.</param>
    /// <returns>An index from 0 up to <see cref="AvatarPaletteSize"/> - 1.</returns>
    internal static int GetAvatarColorIndex(string repoPath)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var normalized = repoPath
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = offsetBasis;
        foreach (var character in normalized)
        {
            hash = (hash ^ character) * prime;
        }

        return (int)(hash % AvatarPaletteSize);
    }

    /// <summary>
    ///     Gets a value indicating whether this repo has never had an agent package pinned (its
    ///     <c>.agentcontrol.json</c> pin file does not exist), i.e. whether the "Select
    ///     Package..." menu item should be offered instead of "Upgrade".
    /// </summary>
    /// <remarks>
    ///     Mutually exclusive with <see cref="IsUpgradeAvailable"/> being ever <see langword="true"/>
    ///     - <see cref="RefreshUpgradeStatusAsync"/> already short-circuits to <see langword="false"/>
    ///     whenever <see cref="PinnedPackageName"/> is <see langword="null"/>, so a repo is either
    ///     never-pinned (this is <see langword="true"/>) or already-pinned (this is
    ///     <see langword="false"/>), never both.
    /// </remarks>
    public bool IsPackageSelectionNeeded => PinnedPackageName is null;

    /// <summary>
    ///     Gets a value indicating whether the agent package chip is shown: the repo has a pinned
    ///     package and its folder exists.
    /// </summary>
    public bool HasPinnedPackage => !IsPackageSelectionNeeded && !IsMissing;

    /// <summary>
    ///     Gets a value indicating whether the agent package chip shows its small "Agent files are up
    ///     to date" check mark: the package is pinned and the card's overall status is
    ///     <see cref="RepoCardStatus.UpToDate"/>.
    /// </summary>
    /// <remarks>
    ///     A healthy card would otherwise be told apart only by the green accent bar and the
    ///     absence of an upgrade chip. The cue sits inside the existing chip (no extra chip or
    ///     row, so no new clutter) and is hidden for every other status - an available upgrade, a
    ///     problem, a running check or no package - so it never contradicts the rest of the card.
    /// </remarks>
    public bool ShowUpToDateCue => HasPinnedPackage && Status == RepoCardStatus.UpToDate;

    /// <summary>
    ///     Gets the part of <see cref="RepoPath"/> before its last directory separator (for example
    ///     <c>C:\work\clients</c>), or an empty string when the path has no separator. The card
    ///     shows it with an end ellipsis next to <see cref="RepoPathLeaf"/>, which is never
    ///     trimmed, so a narrow card reads <c>C:\work\cl…\repo</c>: a middle ellipsis that keeps the
    ///     folder name - the part that tells similar repos apart - visible.
    /// </summary>
    public string RepoPathParent => SplitRepoPath().Parent;

    /// <summary>
    ///     Gets the last segment of <see cref="RepoPath"/> (the repo's folder name) with the
    ///     separator before it (for example <c>\repo</c>): the part of the path the card never trims.
    ///     The whole path when it has no separator to split at.
    /// </summary>
    public string RepoPathLeaf => SplitRepoPath().Leaf;

    /// <summary>
    ///     Splits <see cref="RepoPath"/> at its last directory separator, ignoring a trailing one.
    /// </summary>
    /// <returns>The parent (without the separator, possibly empty) and the leaf (with it).</returns>
    private (string Parent, string Leaf) SplitRepoPath()
    {
        // A trailing separator belongs to the last segment's folder, not to a further empty segment
        var path = RepoPath;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var split = trimmed.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return split < 0 ? (string.Empty, path) : (trimmed[..split], trimmed[split..]);
    }
    /// <summary>
    ///     Gets a value indicating whether the grey "No agent package" chip is shown: the repo
    ///     exists but has never had an agent package pinned.
    /// </summary>
    public bool ShowNoPackageChip => IsPackageSelectionNeeded && !IsMissing;

    /// <summary>
    ///     Gets the text of the agent package chip: the pinned package's name and version.
    /// </summary>
    public string PackageChipText => $"{PinnedPackageName} {PinnedPackageVersion}".TrimEnd();

    /// <summary>
    ///     Gets the text of the upgrade chip: the version an upgrade would move the repo to.
    /// </summary>
    public string UpgradeChipText => LatestAvailableVersion ?? string.Empty;

    /// <summary>
    ///     Gets the definition of the source the pinned package comes from, or
    ///     <see langword="null"/> when the repo is not pinned or the source is not configured
    ///     (the card never substitutes another source).
    /// </summary>
    private PackageSourceDefinition? PinnedSourceDefinition
    {
        get
        {
            var alias = PinnedSource;
            return alias is null
                ? null
                : _getSettings().GetEffectiveSources().FirstOrDefault(
                    source => string.Equals(source.Alias, alias, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    ///     Gets the icon of the pinned package's source type, shown inside the agent package chip.
    /// </summary>
    public MaterialIconKind PinnedSourceIcon => PackageSourceIcons.GetIcon(PinnedSourceDefinition);

    /// <summary>
    ///     Gets the alias of the pinned package's source, shown as muted text beside the source
    ///     icon in the agent package chip so the source is recognizable at a glance without
    ///     hovering (a pin written before sources existed reports the legacy <c>Default</c>
    ///     alias).
    /// </summary>
    public string PinnedSourceLabel => PinnedSource ?? RepoPin.LegacySourceAlias;

    /// <summary>
    ///     Gets a value indicating whether the pinned package's source requires signed packages,
    ///     so the agent package chip shows a small shield. A zip source, a source that is not
    ///     configured and a source without the requirement show none.
    /// </summary>
    public bool RequiresSignature =>
        PinnedSourceDefinition is { Type: PackageSourceType.NuGet, RequireSignedPackages: true };

    /// <summary>
    ///     Gets the tooltip of the signature shield: that signed packages are required and, when
    ///     the source is pinned to a publisher, the start of that certificate's fingerprint.
    /// </summary>
    public string SignatureTooltip
    {
        get
        {
            var source = PinnedSourceDefinition;
            if (source is null || !PackageSourceDefinition.TryNormalizeFingerprint(source.SignerFingerprint, out var fingerprint))
            {
                return "Signature required (any trusted signer)";
            }

            return $"Signature required (signer pinned: {NuGetSourceProvider.SummarizeFingerprint(fingerprint)})";
        }
    }

    /// <summary>
    ///     Gets the tooltip of the agent package chip: which package and version the agent files
    ///     came from and from which source (by alias and kind).
    /// </summary>
    public string PackageChipTooltip =>
        $"Agent files: {PackageChipText} from source '{PinnedSource ?? RepoPin.LegacySourceAlias}' " +
        $"({PackageSourceIcons.GetShortName(PinnedSourceDefinition)}).";

    /// <summary>
    ///     Gets the label of the Launch button: <c>Launch</c> followed by the configured agent
    ///     tool's short name (for example <c>Launch Copilot</c>), or just <c>Launch</c> when the
    ///     tool's name is unknown or would make the button too wide.
    /// </summary>
    public string LaunchButtonText
    {
        get
        {
            var toolName = GetAgentToolDisplayName(_getSettings());
            return toolName is null ? "Launch" : $"Launch {toolName}";
        }
    }

    /// <summary>
    ///     Gets the tooltip of the Launch button.
    /// </summary>
    public string LaunchTooltip
    {
        get
        {
            var toolName = GetAgentToolDisplayName(_getSettings());
            return toolName is null
                ? "Launch the configured agent tool in this repo"
                : $"Launch {toolName} in this repo";
        }
    }

    /// <summary>
    ///     Gets a value indicating whether a package operation (launch preparation, upgrade,
    ///     package selection, refresh or pull) is running for this card, during which every other
    ///     action on the card is disabled.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                LaunchCommand.RaiseCanExecuteChanged();
                PullCommand.RaiseCanExecuteChanged();
                UpgradeCommand.RaiseCanExecuteChanged();
                SelectPackageCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a short description of the running operation (for example "Upgrading agent
    ///     package..."), or <see langword="null"/> when none is running.
    /// </summary>
    public string? BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    /// <summary>
    ///     Gets a value indicating whether the running operation can be cancelled by the user;
    ///     false for an operation (such as a git pull) that cannot be interrupted once started.
    /// </summary>
    public bool CanCancelOperation
    {
        get => _canCancelOperation;
        private set
        {
            if (SetField(ref _canCancelOperation, value))
            {
                CancelOperationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the background check for a newer package version is
    ///     running, shown as a small progress indicator beside the package chip.
    /// </summary>
    public bool IsCheckingUpgrade
    {
        get => _isCheckingUpgrade;
        private set
        {
            if (SetField(ref _isCheckingUpgrade, value))
            {
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the shared, session-scoped package-version cache passed to this card at
    ///     construction, exposed only so <c>MainWindow.axaml.cs</c> can construct a
    ///     <see cref="SelectPackageWindowViewModel"/> for this card's "Select Package..." dialog
    ///     without threading the cache through <see cref="MainWindowViewModel"/> a second way.
    /// </summary>
    internal PackageVersionCache PackageVersionCache => _packageVersionCache;

    /// <summary>
    ///     Builds a <see cref="PackageSourceRegistry"/> from the current settings' configured
    ///     sources, for the "Select Package..." dialog (and this card's own source lookups).
    /// </summary>
    /// <param name="allowPrompt">Whether a NuGet server in the registry may open the sign-in
    ///     dialog; <see langword="false"/> for background work.</param>
    /// <returns>A registry over the sources in effect right now (a snapshot: callers build a new
    ///     one after settings change).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the configured sources are invalid
    ///     (for example a duplicated alias).</exception>
    internal PackageSourceRegistry CreateSourceRegistry(bool allowPrompt = true)
    {
        var settings = _getSettings();
        if (_registryFactory is not null)
        {
            return _registryFactory(settings);
        }

        // Work the user did not ask for (the background upgrade check) shares the session
        // credentials but is never allowed to open the sign-in dialog
        var credentials = allowPrompt ? _credentials : _credentials?.WithoutPrompt();
        return PackageSourceRegistry.Create(settings.GetEffectiveSources(), GetNuGetRootBase(_startupOptions), credentials);
    }

    /// <summary>
    ///     Gets the folder under which NuGet sources keep their private package caches: a
    ///     <c>nuget</c> folder inside the test-isolation configuration directory when one was
    ///     supplied at startup (so automated tests never touch the real user profile), otherwise
    ///     <see langword="null"/> for the per-user default.
    /// </summary>
    /// <param name="startupOptions">The startup overrides, or <see langword="null"/>.</param>
    /// <returns>The override base folder, or <see langword="null"/> for the default. Never a
    ///     repo.</returns>
    internal static string? GetNuGetRootBase(Startup.StartupOptions? startupOptions) =>
        string.IsNullOrWhiteSpace(startupOptions?.ConfigDirectory)
            ? null
            : Path.Combine(Path.GetFullPath(startupOptions.ConfigDirectory), "nuget");

    /// <summary>
    ///     Determines the short name of the configured agent tool for the Launch button label.
    /// </summary>
    /// <param name="settings">The current application settings.</param>
    /// <returns>The tool's short name (for example <c>Copilot</c>), or <see langword="null"/> when
    ///     the tool has no usable short name: a custom command whose program name cannot be
    ///     determined or is longer than <see cref="MaxLaunchToolNameLength"/> characters.</returns>
    private static string? GetAgentToolDisplayName(AppSettings settings)
    {
        switch (settings.AgentTool)
        {
            case AgentToolKind.CopilotCli:
                return "Copilot";
            case AgentToolKind.Cursor:
                return "Cursor";
            case AgentToolKind.ClaudeCode:
                return "Claude";
        }

        // A custom command is described by the program it runs: its first token, without any
        // quotes, folder or extension
        var command = settings.CustomAgentCommand?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            return null;
        }

        string firstToken;
        if (command[0] == '"')
        {
            var closingQuote = command.IndexOf('"', 1);
            firstToken = closingQuote > 1 ? command[1..closingQuote] : command[1..];
        }
        else
        {
            var space = command.IndexOf(' ', StringComparison.Ordinal);
            firstToken = space > 0 ? command[..space] : command;
        }

        var name = Path.GetFileNameWithoutExtension(firstToken.Trim());
        return string.IsNullOrWhiteSpace(name) || name.Length > MaxLaunchToolNameLength ? null : name;
    }

    /// <summary>
    ///     Raises change notifications for every property that depends on the pinned package, its
    ///     source or the configured sources/agent tool, so the chips and the Launch label follow
    ///     whenever any of them change.
    /// </summary>
    private void RaisePackageChipChanged()
    {
        OnPropertyChanged(nameof(HasPinnedPackage));
        OnPropertyChanged(nameof(ShowNoPackageChip));
        OnPropertyChanged(nameof(PackageChipText));
        OnPropertyChanged(nameof(PackageChipTooltip));
        OnPropertyChanged(nameof(PinnedSourceIcon));
        OnPropertyChanged(nameof(PinnedSourceLabel));
        OnPropertyChanged(nameof(RequiresSignature));
        OnPropertyChanged(nameof(SignatureTooltip));
        OnPropertyChanged(nameof(UpgradeTooltip));
        OnPropertyChanged(nameof(LaunchButtonText));
        OnPropertyChanged(nameof(LaunchTooltip));
        RaiseStatusChanged();
    }

    /// <summary>
    ///     Raises the change notifications for the card's overall status after any fact it
    ///     is derived from changed.
    /// </summary>
    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(HasAgentFileActions));
        OnPropertyChanged(nameof(ShowUpToDateCue));
    }

    /// <summary>
    ///     Determines whether a discovered package is the version recorded in a pin: equal text, or
    ///     - when both parse - an equal version (so a pin of <c>1.0.0.0</c> still finds the
    ///     NuGet-normalized <c>1.0.0</c>).
    /// </summary>
    /// <param name="package">The discovered package.</param>
    /// <param name="versionText">The pinned or selected version text.</param>
    /// <returns><see langword="true"/> if the package is that version.</returns>
    private static bool IsVersion(DiscoveredPackage package, string? versionText) =>
        string.Equals(package.Version.ToString(), versionText, StringComparison.Ordinal) ||
        (PackageVersion.TryParse(versionText, out var parsed) && parsed is not null && package.Version.Equals(parsed));

    /// <summary>
    ///     Gets a value indicating whether the "Pull" action is currently offered, per
    ///     architecture.md's rule that pull is only offered when the working tree is clean.
    /// </summary>
    public bool CanPull
    {
        get => _canPull;
        private set
        {
            if (SetField(ref _canPull, value))
            {
                PullCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsWorkingTreeDirty));
                OnPropertyChanged(nameof(PullTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the most recent <see cref="RefreshGitStatus"/> call
    ///     could not determine the working tree's clean/dirty state at all (e.g. the folder is
    ///     not a Git repository, or git could not be started), as opposed to a successful check
    ///     that found uncommitted changes.
    /// </summary>
    /// <remarks>
    ///     Both cases disable <see cref="PullCommand"/> (<see cref="CanPull"/> is
    ///     <see langword="false"/> either way), but they are distinct situations for the "Dirty
    ///     working tree" badge and <see cref="PullTooltip"/>: telling a user to "commit or discard
    ///     changes" when git status genuinely could not be read (e.g. not a git repo) would be
    ///     misleading.
    /// </remarks>
    public bool GitStatusUnavailable
    {
        get => _gitStatusUnavailable;
        private set
        {
            if (SetField(ref _gitStatusUnavailable, value))
            {
                OnPropertyChanged(nameof(IsWorkingTreeDirty));
                OnPropertyChanged(nameof(PullTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="RefreshGitStatus"/> has completed at least
    ///     once for this card (successfully or not), as opposed to <see cref="CanPull"/> and
    ///     <see cref="GitStatusUnavailable"/> simply holding their shared <see langword="false"/>
    ///     default because no check has run yet.
    /// </summary>
    /// <remarks>
    ///     Without this, a card would briefly report <see cref="IsWorkingTreeDirty"/> as
    ///     <see langword="true"/> (and <see cref="PullTooltip"/> would claim uncommitted changes)
    ///     between construction/startup and the first deferred <see cref="RefreshDirtyStatus"/>
    ///     call - a false "confirmed dirty" signal for a repo that simply hasn't been checked
    ///     yet. Gating on this flag instead gives an honest "not yet checked" neutral state.
    /// </remarks>
    public bool GitStatusChecked
    {
        get => _gitStatusChecked;
        private set
        {
            if (SetField(ref _gitStatusChecked, value))
            {
                OnPropertyChanged(nameof(IsWorkingTreeDirty));
                OnPropertyChanged(nameof(PullTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the repo's working tree currently has uncommitted
    ///     changes (the reason <see cref="PullCommand"/> is disabled), for the card's "Dirty
    ///     working tree" badge.
    /// </summary>
    /// <remarks>
    ///     Deliberately excludes <see cref="IsMissing"/> repos - those already get their own
    ///     dedicated "Missing" badge, and showing both for the same card would be redundant (a
    ///     missing repo's working tree can't meaningfully be "clean" or "dirty"). Also excludes
    ///     <see cref="GitStatusUnavailable"/> repos, since a failed status check is not the same
    ///     as a confirmed dirty working tree, and requires <see cref="GitStatusChecked"/> to be
    ///     <see langword="true"/>, so a repo whose status simply hasn't been checked yet (see
    ///     <see cref="RefreshDirtyStatus"/>'s lazy, deferred-by-design evaluation) is never
    ///     reported as dirty before a status check has actually completed.
    /// </remarks>
    public bool IsWorkingTreeDirty => GitStatusChecked && !CanPull && !IsMissing && !GitStatusUnavailable;

    /// <summary>
    ///     Gets the tooltip text for the card's "Pull" button, explaining why Pull is currently
    ///     disabled (if it is) instead of leaving the user to guess.
    /// </summary>
    public string PullTooltip => this switch
    {
        { IsMissing: true } => "This repo's folder could not be found on disk.",
        { CanPull: true } => "Pull the latest commits for this repo",
        { GitStatusChecked: false } => "This repo's Git status has not been checked yet.",
        { GitStatusUnavailable: true } => "This repo's Git status could not be determined (it may not be a " +
                                           "Git repository, or git could not be run), so Pull is disabled.",
        _ => "This repo has uncommitted changes, so Pull is disabled. Commit or discard them (e.g. the " +
             ".agentcontrol.json pin file after a Select Package/Upgrade) to re-enable Pull."
    };

    /// <summary>
    ///     Gets the most recent status/result message from a Launch/Pull/Upgrade action, for
    ///     display in the card (e.g. "Pull failed: ..."), or <see langword="null"/> if no action
    ///     has run yet.
    /// </summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether there is a <see cref="StatusMessage"/> to show on the card.
    /// </summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    /// <summary>
    ///     Gets the command bound to the card's "Launch" button.
    /// </summary>
    public AsyncRelayCommand LaunchCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's "Git Pull" button.
    /// </summary>
    public AsyncRelayCommand PullCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's upgrade chip and "Upgrade" menu item.
    /// </summary>
    public AsyncRelayCommand UpgradeCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's "No agent package" chip and "Select Package..."
    ///     menu item, offered instead of <see cref="UpgradeCommand"/> whenever
    ///     <see cref="IsPackageSelectionNeeded"/> is <see langword="true"/>.
    /// </summary>
    public RelayCommand SelectPackageCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's manual "Refresh" action, re-running the cheap
    ///     checks, the upgrade check and the (otherwise lazy) working-tree dirty check for just
    ///     this card.
    /// </summary>
    /// <remarks>
    ///     Does not bypass the shared <see cref="PackageVersionCache"/> - per architecture.md's
    ///     caching-strategy decision, the package-version scan is refreshed on app start or via
    ///     the Settings window (whenever a package source changes), not per-repo-card.
    /// </remarks>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    ///     Gets the label of the card menu's refresh item: <c>Refresh</c> normally, <c>Check again</c>
    ///     while the repo's folder is missing, because then there is nothing to refresh - the user is
    ///     waiting for the folder (a remounted drive, a restored clone) to come back and wants to
    ///     re-check.
    /// </summary>
    public string RefreshMenuText => IsMissing ? "Check again" : "Refresh";

    /// <summary>
    ///     Gets the tooltip of the card menu's refresh item, saying what it does in the current state.
    /// </summary>
    public string RefreshTooltip => IsMissing
        ? "Check whether the folder is available again"
        : "Re-check this repo's branch, chips, and upgrade status";

    /// <summary>
    ///     Gets the command bound to the card's "Remove from list" menu item.
    /// </summary>
    /// <remarks>
    ///     Only raises <see cref="RemoveRequested"/> - it never mutates any collection or
    ///     settings itself. The confirmation prompt and the actual removal are both view-layer
    ///     concerns handled by <c>MainWindow</c>'s code-behind and
    ///     <see cref="MainWindowViewModel.RemoveRepo"/>.
    /// </remarks>
    public RelayCommand RemoveCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's "Cancel" button, which asks the running package
    ///     operation to stop; only executable while <see cref="CanCancelOperation"/> is
    ///     <see langword="true"/>.
    /// </summary>
    public RelayCommand CancelOperationCommand { get; }

    /// <summary>
    ///     Raised after a successful <see cref="UpgradeAsync"/>, or package selection, with the
    ///     new package's release notes
    ///     text (or an empty string if the package had none), so the owning window can show a
    ///     <c>ReleaseNotesViewer</c>.
    /// </summary>
    public event EventHandler<string>? ReleaseNotesReady;

    /// <summary>
    ///     Raised when a Launch/Pull/Upgrade action fails with an error the user should be shown
    ///     in a message box, per architecture.md's "error message box is sufficient" decision.
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    ///     Raised when <see cref="IsFavorite"/> changes, so the owning
    ///     <see cref="MainWindowViewModel"/> can persist settings and re-sort
    ///     <see cref="MainWindowViewModel.DisplayedRepoCards"/>.
    /// </summary>
    public event EventHandler? FavoriteChanged;

    /// <summary>
    ///     Raised when <see cref="MoveToGroup"/> changes the repo's group, so the owning
    ///     <see cref="MainWindowViewModel"/> can persist settings and rebuild the sectioned list.
    /// </summary>
    public event EventHandler? GroupChanged;

    /// <summary>
    ///     Raised by <see cref="NewGroupCommand"/> to request the "New group" name prompt; the view
    ///     shows it and, if a name is chosen, calls <see cref="MoveToGroup"/>.
    /// </summary>
    public event EventHandler? NewGroupRequested;

    /// <summary>
    ///     Raised when <see cref="SetCardColor"/> changes the tile color, so the owning
    ///     <see cref="MainWindowViewModel"/> can persist settings.
    /// </summary>
    public event EventHandler? CardColorChanged;

    /// <summary>
    ///     Raised when a launch is recorded, so the owning <see cref="MainWindowViewModel"/> can
    ///     persist settings and re-sort <see cref="MainWindowViewModel.DisplayedRepoCards"/>.
    /// </summary>
    public event EventHandler? LaunchRecorded;

    /// <summary>
    ///     Raised by <see cref="RemoveCommand"/> to request this card's removal from the
    ///     recent-repos list. Does not itself mutate any collection - the view layer is
    ///     responsible for confirming with the user before calling
    ///     <see cref="MainWindowViewModel.RemoveRepo"/>.
    /// </summary>
    public event EventHandler? RemoveRequested;

    /// <summary>
    ///     Raised by <see cref="SelectPackageCommand"/> to request the "Select Package..." dialog
    ///     be shown. Only raised once at least one package source is configured - otherwise
    ///     <see cref="ErrorOccurred"/> is raised instead. The dialog lists every configured source
    ///     and lets the user choose among them. Does not itself show any dialog - that is a
    ///     view-layer concern handled by <c>MainWindow</c>'s code-behind, exactly like
    ///     <see cref="RemoveRequested"/>/<see cref="ConfirmationWindow"/>.
    /// </summary>
    public event EventHandler? SelectPackageRequested;

    /// <summary>
    ///     Raised after a successful extraction when the repo has no root-level <c>AGENTS.md</c>
    ///     file, the just-applied package includes an <c>AGENTS.md</c> template, and the user has
    ///     not previously declined this offer for this repo - carries the template's text content
    ///     so the view layer can show a modal Yes/No prompt. Does not itself show any dialog -
    ///     that is a view-layer concern handled by <c>MainWindow</c>'s code-behind via the
    ///     <see cref="ConfirmationWindow"/> pattern, exactly like <see cref="RemoveRequested"/>.
    ///     The user's choice is reported back via <see cref="AcceptAgentsMdTemplate"/> or
    ///     <see cref="DeclineAgentsMdTemplate"/>.
    /// </summary>
    public event EventHandler<string>? AgentsMdTemplateOfferRequested;

    /// <summary>
    ///     Re-reads the repo's pin file, git working-tree status, and upgrade availability from
    ///     the filesystem/git/package source, updating every bindable property.
    /// </summary>
    /// <remarks>
    ///     Equivalent to calling <see cref="RefreshCheap"/>, awaiting
    ///     <see cref="RefreshUpgradeStatusAsync"/> and calling <see cref="RefreshDirtyStatus"/> -
    ///     the single full-refresh entry point behind <see cref="RefreshCommand"/>, rather than
    ///     the split eager/lazy sequencing <see cref="MainWindowViewModel"/> uses at app launch.
    ///     It holds <see cref="IsBusy"/> while it runs so no other card action starts meanwhile.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the package-source part of the refresh.</param>
    /// <returns>A task that completes when every property is current; a failing source is
    ///     reported through the card's state, not thrown.</returns>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync("Refreshing...", cancellable: true, async token =>
        {
            RefreshCheap();

            // The user asked for this refresh, so a server that needs a sign-in may ask for it
            await RefreshUpgradeStatusAsync(token, allowPrompt: true);
            RefreshDirtyStatus();
        }, cancellationToken);

    /// <summary>
    ///     Runs every cheap per-repo check that does not need a package source: the missing-repo
    ///     check, the pin file read, whether the pin's source is configured, the current branch
    ///     name and the committed-agent-files badge (via the <c>HEAD</c>-hash cache).
    /// </summary>
    /// <remarks>
    ///     Deliberately excludes the working-tree dirty/clean check
    ///     (<see cref="RefreshDirtyStatus"/>), which is not cacheable and is instead deferred/lazy
    ///     per architecture.md's repo-fact caching strategy, and the upgrade-availability check
    ///     (<see cref="RefreshUpgradeStatusAsync"/>), which reads a package source and is
    ///     therefore asynchronous, so this method is safe to call eagerly for every recent repo
    ///     at app launch without slowing down startup. When
    ///     <see cref="IsMissing"/> is <see langword="true"/>, every other check short-circuits to
    ///     its "unavailable" default rather than attempting a git/filesystem operation against a
    ///     path that does not exist.
    /// </remarks>
    public void RefreshCheap()
    {
        IsMissing = !Directory.Exists(RepoPath);
        if (IsMissing)
        {
            CurrentBranch = null;
            HasCommittedAgentFiles = false;
            CanPull = false;
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            SetSourceProblem(null, needsSignIn: false);

            // The repo just disappeared (or was already missing): any previously-cached Git
            // status is no longer trustworthy, so invalidate it rather than let it linger and
            // potentially be reported (stale) once the repo reappears. Callers that need an
            // immediate fresh check after a missing-to-present transition (rather than waiting
            // for the next lazy/deferred trigger) should call RefreshDirtyStatus explicitly -
            // see MainWindowViewModel.ApplySettings.
            GitStatusChecked = false;
            GitStatusUnavailable = false;
            RaisePackageChipChanged();

            // A folder that does not exist has no picture: no lookup, just the generated tile
            _imageGeneration++;
            _loadedImageFile = null;
            RepoImage = null;
            return;
        }

        RefreshPin();
        RefreshBranchAndCommittedFiles();
        EvaluatePinnedSource();
        RaisePackageChipChanged();

        // The picture is looked for in the background, so the UI never waits for the disk or the
        // decoder; the tile shows the generated initials until (and unless) it arrives
        RepoImageRefreshTask = RefreshRepoImageAsync();
    }

    /// <summary>
    ///     Re-checks the repo's working-tree cleanliness via <see cref="GitClient"/>, updating
    ///     <see cref="CanPull"/>.
    /// </summary>
    /// <remarks>
    ///     Deliberately not called by <see cref="RefreshCheap"/> or the constructor - per
    ///     architecture.md's repo-fact caching strategy, working-tree dirty/clean state is not
    ///     cacheable by <c>HEAD</c> hash (it reflects uncommitted local edits), so it is computed
    ///     lazily instead: on demand via <see cref="RefreshCommand"/>, or once when a card first
    ///     becomes visible (see <c>MainWindow.axaml.cs</c>), rather than eagerly for every recent
    ///     repo at app launch.
    /// </remarks>
    public void RefreshDirtyStatus()
    {
        if (IsMissing)
        {
            CanPull = false;
            return;
        }

        RefreshGitStatus();
    }

    /// <summary>
    ///     Records that this repo was just successfully launched, stamping
    ///     <see cref="LastLaunchedUtc"/> with the current UTC time and raising
    ///     <see cref="LaunchRecorded"/> so the owning <see cref="MainWindowViewModel"/> can
    ///     persist settings and re-sort <see cref="MainWindowViewModel.DisplayedRepoCards"/>.
    /// </summary>
    private void RecordLaunched()
    {
        _recentRepo.LastLaunchedUtc = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(LastLaunchedUtc));
        LaunchRecorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Launches the configured agentic CLI tool in this repo's working directory, per
    ///     architecture.md's <c>AgentToolLauncher</c> subsystem.
    /// </summary>
    /// <remarks>
    ///     Per architecture.md's "Initial package selection and ensure-synced-before-launch"
    ///     decision (as amended - launch must never be blocked by agent-package sync state),
    ///     this first awaits <see cref="EnsureAgentFilesSyncedBeforeLaunchAsync"/> purely for its
    ///     best-effort sync side effect (it may extract missing managed folders, or surface an
    ///     informational status message/warning), then always proceeds to resolve the shell/agent
    ///     command and spawn the process regardless of that call's outcome - including when the
    ///     user cancels the wait for the sync. A user may want to
    ///     launch their agentic CLI tool to help with agent-package migration, or simply because
    ///     an agentic tool is useful even with zero agent files present - either way, sync state
    ///     must never stand in the way of launching.
    /// </remarks>
    /// <param name="cancellationToken">Signalled when the command itself is cancelled.</param>
    /// <returns>A task that completes once the agent tool has been started or the failure has
    ///     been reported.</returns>
    private Task LaunchAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("Preparing agent files...", cancellable: true, async token =>
        {
            // The pre-launch sync is best effort: whether it succeeds, fails or is cancelled by
            // the user, the agent tool is still started below
            try
            {
                await EnsureAgentFilesSyncedBeforeLaunchAsync(token);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Agent file sync was canceled; launching anyway.";
            }

            LaunchAgentTool();
        }, cancellationToken);

    /// <summary>
    ///     Resolves the configured agent tool and shell and starts the tool in this repo's
    ///     working directory, recording the launch on success and reporting any failure.
    /// </summary>
    private void LaunchAgentTool()
    {
        try
        {
            var settings = _getSettings();
            var command = ResolveAgentCommand(settings);
            if (string.IsNullOrWhiteSpace(command))
            {
                ErrorOccurred?.Invoke(this, "No agentic CLI tool command is configured. Set one in Settings.");
                return;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Launching agent tool for repo '{RepoName}' ('{RepoPath}') with command '{Command}'",
                    RepoName, RepoPath, command);
            }

            var shell = new AgentControl.AgentToolLauncher.ShellDetector().Detect(settings.ShellPreference);
            var startInfo = AgentLauncher.BuildProcessStartInfo(shell, command, RepoPath);
            AgentLauncher.Launch(startInfo);
            StatusMessage = "Launched.";
            RecordLaunched();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to launch the agent tool for repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to launch the agent tool: {ex.Message}");
        }
    }

    /// <summary>
    ///     Best-effort attempt to sync this repo's four managed agent folders with the currently
    ///     pinned package version, as an informational side action that runs before
    ///     <see cref="LaunchAsync"/> spawns the agent tool. Per architecture.md's
    ///     "ensure-synced-before-launch" decision (as amended), this <b>never</b> blocks the
    ///     launch - it only ever attempts to help keep the managed folders in sync, surfacing a
    ///     non-blocking status message or warning when it cannot.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading the source; once the copy into the repo
    ///     has started it is not interrupted, so a repo is never left half extracted.</param>
    /// <returns>
    ///     <see langword="true"/> if no sync action was needed or attempted (the repo has
    ///     committed agent files, has no pin, or the managed folders were already present), or a
    ///     missing set was silently re-extracted successfully; <see langword="false"/> if a sync
    ///     attempt was made and failed for an ordinary reason (source unreachable, pinned version
    ///     missing, an I/O failure), in which case <see cref="ErrorOccurred"/> has already been
    ///     raised as a non-blocking warning explaining why and <see cref="LaunchAsync"/> proceeds to
    ///     spawn the agent tool regardless of this return value.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while the source is being read; the launch still proceeds.</exception>
    /// <remarks>
    ///     <para>
    ///     If this repo has committed agent files (<see cref="HasCommittedAgentFiles"/> is
    ///     <see langword="true"/>), the four managed folders are never touched - no delete, no
    ///     extract - even if a pin exists and the folders are missing or stale. Blindly deleting
    ///     or overwriting files the user has deliberately committed to their repo would be far
    ///     worse than leaving them alone; this instead raises a non-blocking <see cref="StatusMessage"/>
    ///     noting that sync was skipped, and returns <see langword="true"/>.
    ///     </para>
    ///     <para>
    ///     Otherwise, if no pin exists at all (<see cref="PinnedPackageName"/> is
    ///     <see langword="null"/>), this is treated as an acceptable, unremarkable state - not an
    ///     error - since an agentic CLI tool remains useful even with zero managed agent files
    ///     present. A non-blocking <see cref="StatusMessage"/> notes that launch is proceeding
    ///     without managed agent files, and this returns <see langword="true"/>.
    ///     </para>
    ///     <para>
    ///     If a pin exists and <see cref="PackageZipExtractor.AllManagedFoldersExist"/> is
    ///     already <see langword="true"/>, this returns <see langword="true"/> immediately with no
    ///     re-extraction - the common "already synced" case must not pay any extra I/O cost on
    ///     every launch.
    ///     </para>
    ///     <para>
    ///     If a pin exists but one or more managed folders are missing (e.g. a freshly cloned
    ///     repo whose <c>.gitignore</c>'d agent folders were never unpacked on this machine), this
    ///     resolves the <em>currently pinned</em> package at the source the pin names (never the
    ///     latest - upgrading remains a deliberate, separate user action) and re-extracts it
    ///     directly via <see cref="PackageContentExtractor.Extract"/>, deliberately without going
    ///     through <see cref="ApplyPackageAndShowReleaseNotesAsync"/> - architecture.md's
    ///     ensure-synced-before-launch bullet never mentions showing release notes, unlike its
    ///     Select-Package bullet, so a silent background repair must not pop a release-notes
    ///     window on every launch. If this re-extraction attempt fails, <see cref="ErrorOccurred"/>
    ///     is raised as a non-blocking warning and this returns <see langword="false"/> - but the
    ///     launch still proceeds regardless.
    ///     </para>
    ///     <para>
    ///     Marked <see langword="internal"/> (not <see langword="private"/>) rather than tested
    ///     only through <see cref="LaunchAsync"/> itself, mirroring <c>AgentToolLauncher.BuildProcessStartInfo</c>'s
    ///     own precedent for keeping process-spawning code separated from its unit-testable
    ///     decision logic.
    /// </para>
    /// </remarks>
    internal async Task<bool> EnsureAgentFilesSyncedBeforeLaunchAsync(CancellationToken cancellationToken = default)
    {
        if (HasCommittedAgentFiles)
        {
            StatusMessage = "This repo has committed agent files; skipping agent-package sync.";
            return true;
        }

        var pinnedPackageName = PinnedPackageName;
        if (pinnedPackageName is null)
        {
            StatusMessage = "No agent package is pinned for this repo; launching without managed agent files.";
            return true;
        }

        if (PackageZipExtractor.AllManagedFoldersExist(RepoPath))
        {
            return true;
        }

        try
        {
            var registry = CreateSourceRegistry();
            if (registry.Definitions.Count == 0)
            {
                ErrorOccurred?.Invoke(this, "No package source is configured for this repo. Launching without syncing agent files.");
                return false;
            }

            // Resolve the pinned package at the source the pin names - and only that source. A
            // pin whose source is not configured fails here (UnknownSourceException) rather than
            // being served by any other source.
            var provider = registry.Resolve(PinnedSource ?? RepoPin.LegacySourceAlias);
            var pinnedVersion = PinnedPackageVersion;
            var pinnedPackage = (await provider.ListVersionsAsync(pinnedPackageName, cancellationToken))
                .FirstOrDefault(p => IsVersion(p, pinnedVersion));
            if (pinnedPackage is null)
            {
                ErrorOccurred?.Invoke(
                    this,
                    $"Pinned package '{pinnedPackageName} {pinnedVersion}' was not found at the configured source. Launching without syncing agent files.");
                return false;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Re-syncing missing managed agent folders for repo '{RepoName}' from pinned package '{PackageName} {Version}' (source '{Source}')",
                    RepoName, pinnedPackage.PackageName, pinnedPackage.Version, pinnedPackage.SourceAlias);
            }

            using var reader = await provider.AcquireAsync(pinnedPackage, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => PackageContentExtractor.Extract(reader, RepoPath), CancellationToken.None);
            MaybeOfferAgentsMdTemplate(reader);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to sync agent files for repo '{RepoName}' before launch", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to sync agent files: {SecretRedactor.SafeMessage(ex)} Launching anyway.");
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex, "Package source is unreachable while syncing repo '{RepoName}' before launch", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {SecretRedactor.SafeMessage(ex)} Launching anyway.");
            return false;
        }
    }

    /// <summary>
    ///     Runs <c>git pull</c> in this repo on a worker thread, then refreshes the working-tree
    ///     status.
    /// </summary>
    /// <param name="cancellationToken">Signalled when the command itself is cancelled; a pull that
    ///     has already started is not interrupted.</param>
    /// <returns>A task that completes when the pull and the status refresh have finished.</returns>
    private Task PullAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("Pulling...", cancellable: false, async _ => await PullCoreAsync(), cancellationToken);

    /// <summary>
    ///     Performs the pull for <see cref="PullAsync"/>.
    /// </summary>
    /// <returns>A task that completes when the pull and the status refresh have finished.</returns>
    private async Task PullCoreAsync()
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Pulling repo '{RepoName}' ('{RepoPath}')", RepoName, RepoPath);
        }

        try
        {
            var settings = _getSettings();
            var git = new GitClient(ResolveGitExecutablePath(settings));
            var result = await Task.Run(() => git.Pull(RepoPath), CancellationToken.None);
            StatusMessage = result.Succeeded
                ? "Pull succeeded."
                : $"Pull failed: {result.StandardError.Trim()}";
            LastActionFailed = !result.Succeeded;

            if (result.Succeeded)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Pull succeeded for repo '{RepoName}'", RepoName);
                }
            }
            else if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Pull failed for repo '{RepoName}': {StandardError}", RepoName, result.StandardError.Trim());
            }

            RefreshGitStatus();
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to pull repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to pull: {ex.Message}");
        }
    }

    /// <summary>
    ///     Runs the full upgrade sequence for this repo: finds the latest package at the source the
    ///     pin names, then applies it via <see cref="ApplyPackageAndShowReleaseNotesAsync"/>.
    /// </summary>
    /// <remarks>
    ///     The upgrade is resolved only at the pin's own source; if that source is not configured
    ///     the upgrade is refused with an explanatory message rather than offered from another
    ///     source.
    /// </remarks>
    /// <param name="cancellationToken">Signalled when the command itself is cancelled.</param>
    /// <returns>A task that completes when the upgrade has finished or failed.</returns>
    private Task UpgradeAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("Upgrading agent package...", cancellable: true, UpgradeCoreAsync, cancellationToken);

    /// <summary>
    ///     Performs the upgrade for <see cref="UpgradeAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels reading the source; a copy into the repo that has
    ///     started is not interrupted.</param>
    /// <returns>A task that completes when the upgrade has finished or failed.</returns>
    private async Task UpgradeCoreAsync(CancellationToken cancellationToken)
    {
        PackageSourceRegistry registry;
        try
        {
            registry = CreateSourceRegistry();
        }
        catch (InvalidOperationException ex)
        {
            ErrorOccurred?.Invoke(this, $"Upgrade failed: {SecretRedactor.SafeMessage(ex)}");
            return;
        }

        var packageName = PinnedPackageName;
        if (registry.Definitions.Count == 0 || string.IsNullOrWhiteSpace(packageName))
        {
            ErrorOccurred?.Invoke(this, "No package source or pinned package name is configured for this repo.");
            return;
        }

        var sourceAlias = PinnedSource ?? RepoPin.LegacySourceAlias;
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Checking for an upgrade to package '{PackageName}' for repo '{RepoName}' at source '{SourceAlias}'",
                packageName, RepoName, sourceAlias);
        }

        IPackageSourceProvider provider;
        DiscoveredPackage? latest;
        try
        {
            provider = registry.Resolve(sourceAlias);
            latest = (await provider.ListVersionsAsync(packageName, cancellationToken))
                .OrderByDescending(p => p.Version)
                .FirstOrDefault();
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Package source is unreachable while upgrading repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {SecretRedactor.SafeMessage(ex)}");
            return;
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Package source failed while upgrading repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Upgrade failed: {SecretRedactor.SafeMessage(ex)}");
            return;
        }

        if (latest is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "No package named '{PackageName}' was found at the configured source for repo '{RepoName}'",
                    packageName, RepoName);
            }

            ErrorOccurred?.Invoke(this, $"No package named '{packageName}' was found at the configured source.");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Upgrading repo '{RepoName}' to package '{PackageName} {Version}'",
                RepoName, latest.PackageName, latest.Version);
        }

        await ApplyPackageAndShowReleaseNotesAsync(
            provider, latest, $"Upgraded to {latest.Version}.", "Upgrade", cancellationToken);
    }

    /// <summary>
    ///     Applies a user-selected package to this repo: resolves the exact
    ///     <see cref="DiscoveredPackage"/> at the chosen source, then applies it via
    ///     <see cref="ApplyPackageAndShowReleaseNotesAsync"/>.
    /// </summary>
    /// <param name="selection">The package the user selected, naming the source it was selected
    ///     from; the source alias is recorded in the repo's pin. The version is matched against
    ///     the source's discovered versions.</param>
    /// <param name="cancellationToken">Cancels reading the source; a copy into the repo that has
    ///     started is not interrupted.</param>
    /// <returns>A task that completes when the package has been applied or the failure has been
    ///     reported.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection"/> is
    ///     <see langword="null"/>.</exception>
    public Task ApplySelectedPackageAsync(PackageReference selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return RunOperationAsync(
            "Applying agent package...",
            cancellable: true,
            token => ApplySelectedPackageCoreAsync(selection, token),
            cancellationToken);
    }

    /// <summary>
    ///     Performs the package application for <see cref="ApplySelectedPackageAsync"/>.
    /// </summary>
    /// <param name="selection">The package the user selected.</param>
    /// <param name="cancellationToken">Cancels reading the source.</param>
    /// <returns>A task that completes when the package has been applied or the failure has been
    ///     reported.</returns>
    private async Task ApplySelectedPackageCoreAsync(PackageReference selection, CancellationToken cancellationToken)
    {
        var packageName = selection.PackageId;
        var version = selection.Version;
        PackageSourceRegistry registry;
        try
        {
            registry = CreateSourceRegistry();
        }
        catch (InvalidOperationException ex)
        {
            ErrorOccurred?.Invoke(this, $"Select Package failed: {SecretRedactor.SafeMessage(ex)}");
            return;
        }

        if (registry.Definitions.Count == 0)
        {
            ErrorOccurred?.Invoke(this, "No package source is configured. Add one in Settings.");
            return;
        }

        IPackageSourceProvider provider;
        DiscoveredPackage? selected;
        try
        {
            provider = registry.Resolve(selection.SourceAlias);
            selected = (await provider.ListVersionsAsync(packageName, cancellationToken))
                .FirstOrDefault(p => IsVersion(p, version));
        }
        catch (DirectoryNotFoundException ex)
        {
            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {SecretRedactor.SafeMessage(ex)}");
            return;
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Package source failed while selecting a package for repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Select Package failed: {SecretRedactor.SafeMessage(ex)}");
            return;
        }

        if (selected is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Package '{PackageName} {Version}' was not found at the configured source for repo '{RepoName}'",
                    packageName, version, RepoName);
            }

            ErrorOccurred?.Invoke(
                this, $"Package '{packageName} {version}' was not found at the configured source.");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Selecting package '{PackageName} {Version}' from source '{SourceAlias}' for repo '{RepoName}'",
                packageName, version, selected.SourceAlias, RepoName);
        }

        await ApplyPackageAndShowReleaseNotesAsync(
            provider, selected, $"Package selected: {packageName} {version}.", "Select Package", cancellationToken);
    }

    /// <summary>
    ///     Shared extract-and-pin sequence reused by both <see cref="UpgradeAsync"/> (after resolving
    ///     the latest version) and <see cref="ApplySelectedPackageAsync"/> (after resolving the exact
    ///     user-selected version): acquires the package from its source, extracts it
    ///     (blind-delete-and-replace), proactively ensures the repo's <c>.gitignore</c> covers the
    ///     managed agent folders, rewrites the pin file (recording the package's source alias and
    ///     keeping any existing pin's other fields), refreshes this card's pin/upgrade-status
    ///     properties, offers the package's AGENTS.md template when it applies (see
    ///     <see cref="AgentsMdTemplateOfferRequested"/>) and then raises <see cref="ReleaseNotesReady"/>
    ///     with the new package's release notes.
    /// </summary>
    /// <remarks>
    ///     The existing pin file is read <em>before</em> anything is changed so a pin written by a
    ///     newer AgentControl (an unsupported schema) aborts the whole apply with its actionable
    ///     message while the repo is still untouched. Any other read failure (a corrupt or
    ///     unreadable file) is tolerated and defaults to "no persisted decline", since the
    ///     pre-feature behavior unconditionally wrote a fresh pin regardless of what (if anything)
    ///     existed before; the new pin is then still written via <see cref="RepoPinStore.Save"/>,
    ///     whose own failure handling is unchanged - a genuine write failure there is still
    ///     reported via the outer <see cref="ErrorOccurred"/> handler. Reusing the loaded pin keeps
    ///     <see cref="RepoPin.AgentsMdTemplateDeclined"/> and any unknown fields intact.
    ///     Cancellation is honored only while the package is being acquired; the copy into the
    ///     repo runs on a worker thread and, once started, is never interrupted, so a repo is
    ///     never left half extracted.
    /// </remarks>
    /// <param name="provider">The provider of the source the package was discovered at.</param>
    /// <param name="package">The resolved package to apply.</param>
    /// <param name="successMessage">The <see cref="StatusMessage"/> text to set on success.</param>
    /// <param name="failureVerb">A short verb phrase (e.g. <c>"Upgrade"</c>, <c>"Select Package"</c>)
    ///     used to prefix any <see cref="ErrorOccurred"/> message raised on failure.</param>
    /// <param name="cancellationToken">Cancels acquiring the package.</param>
    /// <returns>A task that completes when the package has been applied or the failure has been
    ///     reported.</returns>
    private async Task ApplyPackageAndShowReleaseNotesAsync(
        IPackageSourceProvider provider,
        DiscoveredPackage package,
        string successMessage,
        string failureVerb,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Fetching and applying package '{PackageName} {Version}' from source '{SourceAlias}' to repo '{RepoName}' ('{RepoPath}')",
                package.PackageName, package.Version, package.SourceAlias, RepoName, RepoPath);
        }

        try
        {
            // Read the existing pin first: a newer-schema pin must stop the apply before the repo
            // is touched; a corrupt one is simply replaced
            RepoPin? existingPin;
            try
            {
                existingPin = RepoPinStore.Load(RepoPath);
            }
            catch (InvalidOperationException ex) when (ex is not UnsupportedPinSchemaException)
            {
                existingPin = null;
            }

            using var reader = await provider.AcquireAsync(package, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => PackageContentExtractor.Extract(reader, RepoPath), CancellationToken.None);
            EnsureGitIgnoreCoversManagedFolders();

            // Preserve a prior AGENTS.md template decline (and any unknown fields) across
            // re-pinning by updating the loaded pin in place: those fields are orthogonal to the
            // selected package/version and must never be silently reset.
            var pin = existingPin ?? new RepoPin();
            pin.Source = package.SourceAlias;
            pin.PackageName = package.PackageName;
            pin.Version = package.Version.ToString();
            RepoPinStore.Save(RepoPath, pin);

            var releaseNotes = PackageContentExtractor.ReadReleaseNotes(reader) ?? string.Empty;

            // The package is applied; the follow-up status refresh is no longer cancellable so
            // the card never shows the old pin's upgrade state next to the new pin
            RefreshPin();
            EvaluatePinnedSource();
            await RefreshUpgradeStatusAsync(CancellationToken.None);
            RaisePackageChipChanged();
            StatusMessage = successMessage;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Applied package '{PackageName} {Version}' to repo '{RepoName}'",
                    package.PackageName, package.Version, RepoName);
            }

            // The AGENTS.md offer comes first, then the release notes: the view shows the offer (a
            // modal question) at once and holds the release notes back until it is answered, so the
            // question never covers the notes (and the notes window, being non-modal, is not lost
            // behind it); the notes then appear in front as the completion of the upgrade.
            MaybeOfferAgentsMdTemplate(reader);
            ReleaseNotesReady?.Invoke(this, releaseNotes);
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex,
                    "{FailureVerb} failed for package '{PackageName} {Version}' on repo '{RepoName}'",
                    failureVerb, package.PackageName, package.Version, RepoName);
            }

            ErrorOccurred?.Invoke(this, $"{failureVerb} failed: {SecretRedactor.SafeMessage(ex)}");
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex, "Package source is unreachable while applying a package to repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {SecretRedactor.SafeMessage(ex)}");
        }
    }

    /// <summary>
    ///     Decides whether to offer placing the just-applied package's optional root-level
    ///     <c>AGENTS.md</c> template into this repo's root, raising
    ///     <see cref="AgentsMdTemplateOfferRequested"/> when all three conditions hold: the repo
    ///     has no root-level <c>AGENTS.md</c> file yet, the package zip includes an
    ///     <c>AGENTS.md</c> template, and the user has not previously declined this offer for
    ///     this repo (<see cref="RepoPin.AgentsMdTemplateDeclined"/>).
    /// </summary>
    /// <param name="reader">The content of the package that was just successfully applied
    ///     (extracted) to this repo.</param>
    /// <remarks>
    ///     Called only from call sites that already have a <paramref name="reader"/> in hand
    ///     immediately after a successful <see cref="PackageContentExtractor.Extract"/> call
    ///     (<see cref="ApplyPackageAndShowReleaseNotesAsync"/> and the re-extraction branch of
    ///     <see cref="EnsureAgentFilesSyncedBeforeLaunchAsync"/>) - never as an independent, standing
    ///     per-launch check, so the offer only ever fires when a sync genuinely just happened.
    ///     Wrapped in its own non-blocking try/catch, mirroring
    ///     <see cref="EnsureGitIgnoreCoversManagedFolders"/>'s precedent, so a pin-read or
    ///     package-read failure here can never disrupt an already-successful sync.
    /// </remarks>
    private void MaybeOfferAgentsMdTemplate(IPackageContentReader reader)
    {
        try
        {
            if (File.Exists(PathHelpers.SafePathCombine(RepoPath, AgentsMdFileName)))
            {
                return;
            }

            var pin = RepoPinStore.Load(RepoPath);
            if (pin is { AgentsMdTemplateDeclined: true })
            {
                return;
            }

            var template = PackageContentExtractor.ReadAgentsMdTemplate(reader);
            if (template is null)
            {
                return;
            }

            AgentsMdTemplateOfferRequested?.Invoke(this, template);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    ///     Writes the package's offered <c>AGENTS.md</c> template to this repo's root, in
    ///     response to the user accepting the <see cref="AgentsMdTemplateOfferRequested"/> prompt.
    /// </summary>
    /// <param name="templateContent">The template's text content, as previously carried by
    ///     <see cref="AgentsMdTemplateOfferRequested"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="templateContent"/> is
    ///     <see langword="null"/>.</exception>
    /// <remarks>
    ///     <para>
    ///     Writes directly to the repo root - never into any of the four managed folders - since
    ///     a root-level <c>AGENTS.md</c> is a starting point the user is expected to customize
    ///     themselves, not a file blind-deleted/replaced on every subsequent sync.
    ///     </para>
    ///     <para>
    ///     Race-safe against the gap between <see cref="MaybeOfferAgentsMdTemplate"/>'s
    ///     <c>File.Exists</c> check (which runs before the modal offer is shown) and the user
    ///     actually accepting the offer: this writes via <see cref="FileMode.CreateNew"/> rather
    ///     than an unconditional overwrite, so if a user or another process creates
    ///     <c>AGENTS.md</c> while the dialog is open, this never silently overwrites it. That
    ///     outcome is treated as a distinct, non-destructive result - reported via
    ///     <see cref="StatusMessage"/>, not <see cref="ErrorOccurred"/> - rather than a generic
    ///     failure, mirroring how <see cref="EnsureAgentFilesSyncedBeforeLaunchAsync"/> distinguishes
    ///     informational outcomes from genuine errors.
    ///     </para>
    /// </remarks>
    public void AcceptAgentsMdTemplate(string templateContent)
    {
        ArgumentNullException.ThrowIfNull(templateContent);

        var agentsMdPath = PathHelpers.SafePathCombine(RepoPath, AgentsMdFileName);

        // Opening with FileMode.CreateNew is the atomic, race-safe check for "does AGENTS.md
        // already exist" - but only *this* step can mean "another process created it concurrently
        // between the offer being raised and now". It is deliberately isolated from the write
        // below: if CreateNew succeeds and a *later* write/flush step then fails (e.g. disk full),
        // AGENTS.md now exists only because this call just created it, and that failure must be
        // reported as a genuine error - not misreported as the benign "already exists" outcome,
        // which would otherwise happen if both steps shared one File.Exists-guarded catch.
        FileStream stream;
        try
        {
            stream = new FileStream(agentsMdPath, FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException ex) when (File.Exists(agentsMdPath))
        {
            // Another user/process created AGENTS.md after the offer was raised but before this
            // open ran - never overwrite it; this is a non-destructive, informational outcome,
            // not a failure.
            StatusMessage = "AGENTS.md already exists for this repo - nothing was written.";

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    ex,
                    "AGENTS.md was created concurrently for repo '{RepoName}'; the offered template was not written",
                    RepoName);
            }

            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to create AGENTS.md template for repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to write AGENTS.md: {ex.Message}");
            return;
        }

        try
        {
            using (stream)
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(templateContent);
            }

            StatusMessage = "Added a starting AGENTS.md template - please review and customize it for this repo.";

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Wrote a starting AGENTS.md template to repo '{RepoName}'", RepoName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to write AGENTS.md template for repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to write AGENTS.md: {ex.Message}");
        }
    }

    /// <summary>
    ///     Persists that the user declined the <c>AGENTS.md</c> template offer for this repo, so
    ///     <see cref="MaybeOfferAgentsMdTemplate"/> never raises
    ///     <see cref="AgentsMdTemplateOfferRequested"/> again for it.
    /// </summary>
    /// <remarks>
    ///     Loads the existing pin (if any) rather than constructing a fresh one, so the repo's
    ///     already-persisted package name/version are preserved alongside the new decline flag.
    ///     There is deliberately no corresponding "reset" method - the decision is sticky for this
    ///     repo, per the feature's explicit scope.
    /// </remarks>
    public void DeclineAgentsMdTemplate()
    {
        try
        {
            var pin = RepoPinStore.Load(RepoPath) ?? new RepoPin
            {
                Source = PinnedSource,
                PackageName = PinnedPackageName ?? string.Empty,
                Version = PinnedPackageVersion ?? string.Empty
            };
            pin.AgentsMdTemplateDeclined = true;
            RepoPinStore.Save(RepoPath, pin);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Recorded AGENTS.md template decline for repo '{RepoName}'", RepoName);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to record AGENTS.md template decline for repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to record your AGENTS.md decision: {ex.Message}");
        }
    }

    /// <summary>
    ///     Proactively ensures this repo's root <c>.gitignore</c> covers the four managed agent
    ///     folders immediately after a successful extraction, so they are far less likely to be
    ///     accidentally committed. Purely a non-blocking, best-effort side effect: deliberately
    ///     wrapped in its own try/catch, separate from <see cref="ApplyPackageAndShowReleaseNotesAsync"/>'s
    ///     outer catch, so a <c>.gitignore</c> I/O failure can never abort the pin write or
    ///     release-notes display that follow it.
    /// </summary>
    /// <remarks>
    ///     Never touches git tracking state itself and never invokes git - see
    ///     <see cref="GitIgnoreEnsurer"/>'s remarks for the marker-comment-only idempotency
    ///     rationale. This is a deliberate, narrow exception to the "Committed agent files"
    ///     badge's advisory-only stance (architecture.md), not a contradiction of it: the badge
    ///     remains purely advisory and unaffected by this method.
    /// </remarks>
    private void EnsureGitIgnoreCoversManagedFolders()
    {
        try
        {
            GitIgnoreEnsurer.Ensure(RepoPath);
        }
        catch (InvalidOperationException ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    ///     Raises <see cref="SelectPackageRequested"/> so the view layer can show the "Select
    ///     Package..." dialog.
    /// </summary>
    /// <remarks>
    ///     Reports an error instead when no package source is configured (or the configured
    ///     sources are invalid). The dialog lists every configured source and lets the user
    ///     choose, so the card passes nothing along.
    /// </remarks>
    private void SelectPackage()
    {
        PackageSourceRegistry registry;
        try
        {
            registry = CreateSourceRegistry();
        }
        catch (InvalidOperationException ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return;
        }

        if (registry.Definitions.Count == 0)
        {
            ErrorOccurred?.Invoke(this, "No package source is configured. Add one in Settings.");
            return;
        }

        SelectPackageRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Re-reads the repo's <c>.agentcontrol.json</c> pin file, updating
    ///     <see cref="PinnedPackageName"/>, <see cref="PinnedPackageVersion"/> and
    ///     <see cref="PinnedSource"/>.
    /// </summary>
    private void RefreshPin()
    {
        try
        {
            var pin = RepoPinStore.Load(RepoPath);
            PinnedPackageName = pin?.PackageName;
            PinnedPackageVersion = pin?.Version;
            PinnedSource = pin?.Source;
        }
        catch (InvalidOperationException ex)
        {
            // The pin file exists but could not be read/parsed (or was written by a newer
            // AgentControl) - clear any previously cached pin fields rather than leaving stale
            // data in place, so neither the UI nor the upgrade-gating logic in
            // RefreshUpgradeStatus() continues to treat this repo as pinned to a package that
            // can no longer be confirmed from disk.
            PinnedPackageName = null;
            PinnedPackageVersion = null;
            PinnedSource = null;
            StatusMessage = $"Failed to read pin file: {ex.Message}";
        }
    }

    /// <summary>
    ///     Re-checks the repo's working-tree cleanliness via <see cref="GitClient"/>, updating
    ///     <see cref="CanPull"/>, <see cref="GitStatusUnavailable"/>, and
    ///     <see cref="GitStatusChecked"/>. Any failure (not a git repo, git not installed, etc.)
    ///     is treated as "pull not offered" rather than propagated, but is distinguished from a
    ///     successful check that found a dirty working tree so the "Dirty working tree" badge
    ///     and <see cref="PullTooltip"/> aren't shown for repos whose status simply couldn't be
    ///     determined.
    /// </summary>
    private void RefreshGitStatus()
    {
        try
        {
            var settings = _getSettings();
            var git = new GitClient(ResolveGitExecutablePath(settings));
            CanPull = git.IsWorkingTreeClean(RepoPath);
            GitStatusUnavailable = false;
        }
        catch (InvalidOperationException)
        {
            CanPull = false;
            GitStatusUnavailable = true;
        }
        finally
        {
            GitStatusChecked = true;
        }
    }

    /// <summary>
    ///     Re-checks the repo's current branch name and "committed agent files" badge via
    ///     <see cref="GitClient"/>, consulting (and populating) the per-card
    ///     <see cref="CommittedAgentFilesCache"/> keyed by the repo's current <c>HEAD</c> commit
    ///     hash. Any failure (not a git repo, git not installed, etc.) is treated as "unknown"
    ///     rather than propagated.
    /// </summary>
    private void RefreshBranchAndCommittedFiles()
    {
        try
        {
            var settings = _getSettings();
            var git = new GitClient(ResolveGitExecutablePath(settings));
            CurrentBranch = git.GetCurrentBranch(RepoPath);

            var headHash = git.GetHeadCommitHash(RepoPath);
            if (!_committedAgentFilesCache.TryGetCached(RepoPath, headHash, out var cached))
            {
                cached = git.HasCommittedAgentFiles(RepoPath);
                _committedAgentFilesCache.Set(RepoPath, headHash, cached);
            }

            HasCommittedAgentFiles = cached;
        }
        catch (InvalidOperationException)
        {
            CurrentBranch = null;
            HasCommittedAgentFiles = false;
        }
    }

    /// <summary>
    ///     Re-checks whether a newer package version is available at the source the pin names (via
    ///     the shared <see cref="PackageVersionCache"/>), updating <see cref="IsUpgradeAvailable"/>,
    ///     <see cref="LatestAvailableVersion"/> and <see cref="SourceProblem"/>. Any failure
    ///     (source unreachable or failing) is treated as "no upgrade available" rather than
    ///     propagated.
    /// </summary>
    /// <remarks>
    ///     A pin naming a source that is not configured is an error state, not a reason to look
    ///     elsewhere: <see cref="SourceProblem"/> explains it and no upgrade is offered. Only the
    ///     pin's own source is ever consulted. The source is read asynchronously, so a slow or
    ///     unreachable source never freezes the window; <see cref="IsCheckingUpgrade"/> is set
    ///     meanwhile. When several checks overlap (for example after the sources were edited
    ///     while an earlier check was still waiting) only the most recently started one updates
    ///     the card.
    /// </remarks>
    /// <param name="cancellationToken">Cancels reading the source.</param>
    /// <param name="allowPrompt">Whether a server that needs a sign-in may open the sign-in
    ///     dialog. <see langword="false"/> (the default) for the background check at start-up and
    ///     after settings change, which must never interrupt the user with a dialog: it uses a
    ///     remembered credential if there is one and otherwise reports that a sign-in is needed.
    ///     <see langword="true"/> when the user asked for the check.</param>
    /// <returns>A task that completes when the card's upgrade state is current.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled; the card keeps its previous upgrade state.</exception>
    public async Task RefreshUpgradeStatusAsync(CancellationToken cancellationToken = default, bool allowPrompt = false)
    {
        var generation = ++_upgradeCheckGeneration;

        // A repo whose folder is gone offers no upgrade: its package chip is hidden, so an upgrade
        // chip on its own would be meaningless (and the upgrade command is disabled anyway)
        if (IsMissing)
        {
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            IsCheckingUpgrade = false;
            return;
        }

        if (!EvaluatePinnedSource())
        {
            IsCheckingUpgrade = false;
            return;
        }

        var pinnedPackageName = PinnedPackageName!;
        var pinnedVersion = PinnedPackageVersion ?? string.Empty;
        var sourceAlias = PinnedSource ?? RepoPin.LegacySourceAlias;
        IsCheckingUpgrade = true;
        try
        {
            var registry = CreateSourceRegistry(allowPrompt);
            var check = await _packageVersionCache.IsNewerVersionAvailableAsync(
                registry, sourceAlias, pinnedPackageName, pinnedVersion, cancellationToken);
            if (generation == _upgradeCheckGeneration)
            {
                LatestAvailableVersion = check.Latest?.Version.ToString();
                IsUpgradeAvailable = check.IsNewerAvailable;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // An unreachable or failing source (or invalid source configuration) means "no
            // upgrade can be offered", not a crash; the specific failure is surfaced by the
            // actions that need the source
            if (generation == _upgradeCheckGeneration)
            {
                IsUpgradeAvailable = false;
                LatestAvailableVersion = null;

                // A server that needs a sign-in is worth telling the user about, because the
                // background check must not ask: the card says what to do instead
                if (ex is PackageSourceAccessException { Kind: PackageSourceFailureKind.AuthenticationFailed } && !allowPrompt)
                {
                    SetSourceProblem(
                        $"{SecretRedactor.SafeMessage(ex)} Use the card's \"...\" menu > Refresh to sign in.",
                        needsSignIn: true);
                }
            }
        }
        finally
        {
            if (generation == _upgradeCheckGeneration)
            {
                IsCheckingUpgrade = false;
            }
        }
    }

    /// <summary>
    ///     Applies the parts of the upgrade check that need no package source: a repo with no pin
    ///     has no upgrade, and a pin naming a source that is not configured is an error state.
    /// </summary>
    /// <returns><see langword="true"/> when the pin's source is configured and must be consulted
    ///     to learn whether a newer version exists; <see langword="false"/> when the state was
    ///     fully decided here (no pin, unconfigured source, or invalid source configuration).</returns>
    private bool EvaluatePinnedSource()
    {
        SetSourceProblem(null, needsSignIn: false);
        if (string.IsNullOrWhiteSpace(PinnedPackageName))
        {
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            return false;
        }

        var sourceAlias = PinnedSource ?? RepoPin.LegacySourceAlias;
        try
        {
            if (!CreateSourceRegistry().Contains(sourceAlias))
            {
                // No fallback: the pin's source is simply not available, and the card says so
                SetSourceProblem(new UnknownSourceException(sourceAlias).Message, needsSignIn: false);
                IsUpgradeAvailable = false;
                LatestAvailableVersion = null;
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            // Invalid source configuration (for example a duplicated alias): nothing can be
            // offered; the actions that need the source report the specific reason
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Sets the source problem and whether it is a missing sign-in, notifying the chip's
    ///     bindings even when only the kind of problem changed.
    /// </summary>
    /// <param name="problem">The user-readable problem, or <see langword="null"/> for none.</param>
    /// <param name="needsSignIn">Whether the problem is that the server needs a sign-in.</param>
    private void SetSourceProblem(string? problem, bool needsSignIn)
    {
        var kindChanged = _sourceNeedsSignIn != needsSignIn;
        _sourceNeedsSignIn = needsSignIn;
        SourceProblem = problem;
        if (kindChanged)
        {
            OnPropertyChanged(nameof(SourceNeedsSignIn));
            OnPropertyChanged(nameof(SourceProblemChipText));
        }
    }

    /// <summary>
    ///     Runs a package operation for this card while holding <see cref="IsBusy"/>, so a second
    ///     operation cannot start until it finishes, and makes it cancellable through
    ///     <see cref="CancelOperationCommand"/> when <paramref name="cancellable"/> is set.
    /// </summary>
    /// <param name="busyMessage">The text shown while the operation runs.</param>
    /// <param name="cancellable">Whether the user may cancel the operation.</param>
    /// <param name="operation">The work to run; receives a token cancelled by either
    ///     <paramref name="cancellationToken"/> or the card's cancel command.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>A task that completes when the operation has finished; returns immediately
    ///     without running <paramref name="operation"/> when another operation is already
    ///     running. A cancelled operation sets a status message instead of failing.</returns>
    private async Task RunOperationAsync(
        string busyMessage,
        bool cancellable,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        // A new operation starts from a clean slate: the previous failure (if any) no longer
        // describes the card once the user tries again
        LastActionFailed = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;
        BusyMessage = busyMessage;
        CanCancelOperation = cancellable;
        IsBusy = true;
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "The operation was canceled.";
        }
        finally
        {
            _operationCancellation = null;
            IsBusy = false;
            CanCancelOperation = false;
            BusyMessage = null;
        }
    }

    /// <summary>
    ///     Asks the running package operation to stop; does nothing when none is running.
    /// </summary>
    private void CancelOperation()
    {
        try
        {
            _operationCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation finished between the click and the cancel; nothing is left to stop
        }
    }

    /// <summary>
    ///     Tells the user about an exception that no operation handled itself, so a failure never
    ///     disappears silently or tears down the application.
    /// </summary>
    /// <param name="exception">The unexpected exception thrown by a command's operation.</param>
    private void ReportUnexpectedFailure(Exception exception)
    {
        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError(exception, "An operation on repo '{RepoName}' failed unexpectedly", RepoName);
        }

        ErrorOccurred?.Invoke(this, $"The operation failed: {SecretRedactor.SafeMessage(exception)}");
    }

    /// <summary>
    ///     Resolves the git executable path to use: the per-user setting if configured,
    ///     otherwise the startup-time test override, otherwise <c>"git"</c> (PATH resolution).
    /// </summary>
    /// <param name="settings">The current application settings.</param>
    /// <returns>The git executable path or command name to invoke.</returns>
    private string ResolveGitExecutablePath(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.GitExecutablePath)
            ? settings.GitExecutablePath
            : _startupOptions?.GitExecutableOverride ?? "git";

    /// <summary>
    ///     Resolves the agent tool command line to launch: a well-known command for the built-in
    ///     <see cref="AgentToolKind"/> values, or the user-supplied custom command (falling back
    ///     to the startup-time test override) for <see cref="AgentToolKind.Custom"/>.
    /// </summary>
    /// <param name="settings">The current application settings.</param>
    /// <returns>The resolved command line, or an empty string if none could be resolved.</returns>
    private string ResolveAgentCommand(AppSettings settings)
    {
        if (settings.AgentTool == AgentToolKind.Custom)
        {
            return !string.IsNullOrWhiteSpace(settings.CustomAgentCommand)
                ? settings.CustomAgentCommand
                : _startupOptions?.AgentToolCommandOverride ?? string.Empty;
        }

        return WellKnownAgentCommands.GetValueOrDefault(settings.AgentTool, string.Empty);
    }
}
