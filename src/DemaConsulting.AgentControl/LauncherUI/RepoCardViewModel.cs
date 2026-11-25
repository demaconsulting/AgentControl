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
using System.Runtime.ExceptionServices;
using Avalonia.Media.Imaging;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.AgentToolLauncher;
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
///     What a card's cheap refresh learns from the disk and git about a repo, read on any thread by
///     <see cref="RepoCardViewModel.ReadCheapFacts"/> and applied to the card on the UI thread by
///     <see cref="RepoCardViewModel.ApplyCheapFacts"/>.
/// </summary>
/// <remarks>
///     Immutable, so it can be handed from a worker thread to the UI thread without any locking. The
///     split lets the slow part (three git processes and a file read per repo) run off the UI thread
///     while every property change still happens on it.
/// </remarks>
/// <param name="IsMissing">Whether the repo's folder does not exist; every other value is then its
///     default.</param>
/// <param name="Pin">The pin file's content, or <see langword="null"/> when there is none or it could
///     not be read.</param>
/// <param name="PinLoadError">Why the pin file could not be read, or <see langword="null"/> when it was
///     read (or there is none).</param>
/// <param name="CurrentBranch">The checked-out branch, or <see langword="null"/> when git could not
///     tell.</param>
/// <param name="HeadHash">The <c>HEAD</c> commit hash the committed-files answer belongs to, or
///     <see langword="null"/> when git could not tell.</param>
/// <param name="HasCommittedAgentFiles">Whether managed agent folders are committed to git.</param>
/// <param name="GitQueryFailed">Whether git could not answer the branch, <c>HEAD</c> or committed-files
///     questions (not a repository, git missing, or git hung and was stopped).</param>
/// <param name="GitProblem">When git did not answer within its time limit and was stopped, what to tell
///     the user; <see langword="null"/> otherwise (an ordinary git failure is not shown as a
///     problem).</param>
internal sealed record RepoCheapFacts(
    bool IsMissing,
    RepoPin? Pin,
    string? PinLoadError,
    string? CurrentBranch,
    string? HeadHash,
    bool HasCommittedAgentFiles,
    bool GitQueryFailed,
    string? GitProblem = null);

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
    ///     What a user can do about a republished package version that was refused: ask for a new
    ///     version, or - when the change is trusted - choose the same version again with the card
    ///     menu's "Change package...", which asks for confirmation and then records the package's
    ///     current content.
    /// </summary>
    private const string ContentChangeAdvice =
        "Ask the package's maintainer to publish the changed files as a new version. If you trust the change, " +
        "you can instead accept it: use the card's \"...\" menu > Change package... and choose the same version again.";

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
    ///     The <c>files-sha256:</c> content identity the pin recorded for the pinned version, read
    ///     from the pin file; <see langword="null"/> when none (an older pin, or a source that
    ///     reported none). What a configured source's files are verified against.
    /// </summary>
    private string? _pinnedContentIdentity;

    /// <summary>
    ///     The <c>Source</c> an older version of AgentControl recorded in the pin file, read as a
    ///     hint only (never written back); <see langword="null"/> when the file names none. Used
    ///     only to choose among the user's own sources for a pin that has no files identity.
    /// </summary>
    private string? _pinLegacySource;

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

    /// <summary>
    ///     The number handed to the most recent cheap-facts request (a background read, a synchronous
    ///     refresh, a direct apply or a pin re-read); see <see cref="_appliedCheapGeneration"/>.
    /// </summary>
    private int _cheapRefreshGeneration;

    /// <summary>
    ///     The number of the newest cheap facts applied to the card. A background read's facts are
    ///     applied only when their number is higher, so the facts of an older read never replace newer
    ///     ones, while a newer request that was given up (and so applied nothing) never voids an older
    ///     read.
    /// </summary>
    private int _appliedCheapGeneration;

    /// <summary>
    ///     The number handed to the most recent working-tree request (synchronous or background); see
    ///     <see cref="_appliedGitStatusGeneration"/>.
    /// </summary>
    private int _gitStatusGeneration;

    /// <summary>
    ///     The number of the newest working-tree answer applied to the card. A background answer is
    ///     applied only when its number is higher, so an older answer never replaces a newer one, while
    ///     a newer request that was given up never voids an older check.
    /// </summary>
    private int _appliedGitStatusGeneration;


    /// <summary>
    ///     The queue every lazy read of this card's repo state goes through (shared by every card of
    ///     the window); see the constructor.
    /// </summary>
    private readonly RepoRefreshQueue _refreshQueue;

    /// <summary>
    ///     Whether <see cref="_refreshQueue"/> is the card's own (none was supplied), so the card shuts
    ///     it down when it is retired.
    /// </summary>
    private readonly bool _ownsRefreshQueue;

    /// <summary>
    ///     Signaled when the card is retired, so its reads that still wait in the queue are given up.
    /// </summary>
    private readonly CancellationTokenSource _retiredCancellation = new();

    /// <summary>
    ///     The token of <see cref="_retiredCancellation"/>, taken once so it stays usable after the
    ///     source was disposed by <see cref="Retire"/>.
    /// </summary>
    private readonly CancellationToken _retiredToken;

    /// <summary>
    ///     How many of the card's own repo-state reads wait in the queue or run; see
    ///     <see cref="IsRefreshing"/>.
    /// </summary>
    private int _refreshesInFlight;

    /// <summary>
    ///     The time limits of the card's git commands, or <see langword="null"/> for
    ///     <see cref="GitClientTimeouts.Default"/>; see the constructor.
    /// </summary>
    private readonly GitClientTimeouts? _gitTimeouts;

    /// <summary>
    ///     The git problem (a timeout) the card last showed as its status message, so a later read that
    ///     answers clears it again; <see langword="null"/> when none is shown.
    /// </summary>
    private string? _shownGitProblem;

    /// <summary>
    ///     Applies a change to this repo's settings entry on behalf of the card, saving it first; see
    ///     the constructor. <see langword="null"/> applies the change directly, without saving.
    /// </summary>
    private readonly Func<RecentRepo, Action<RecentRepo>, bool>? _commitChange;

    /// <summary>
    ///     Starts the agent-tool process when the card launches the tool; see the constructor.
    /// </summary>
    private readonly IAgentToolProcessStarter _processStarter;

    /// <summary>
    ///     Detects the shell the agent tool runs in; see the constructor.
    /// </summary>
    private readonly ShellDetector _shellDetector;

    /// <summary>
    ///     Runs the disk, hashing and process work of the card's actions (launch, upgrade, select
    ///     package, the AGENTS.md answer) off the UI thread; see the constructor.
    /// </summary>
    private readonly IBackgroundWorkRunner _backgroundWork;

    /// <summary>
    ///     Whether <see cref="Retire"/> was called: the card was removed from the list, so nothing it
    ///     still has running may report back or publish a result.
    /// </summary>
    private bool _isRetired;

    /// <summary>
    ///     Whether the AGENTS.md template offer that is currently open was raised by the re-sync
    ///     that runs before a launch, which never writes the pin.
    /// </summary>
    private bool _agentsMdOfferFromLaunchResync;

    private bool _isMissing;
    private bool _isUpgradeAvailable;
    private string? _latestAvailableVersion;
    private string? _sourceProblem;
    private bool _sourceNeedsSignIn;
    private string? _currentBranch;

    /// <summary>Backing field for <see cref="HasRepoState"/>.</summary>
    private bool _hasRepoState;
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

    /// <summary>Backing field for <see cref="IsUpgradeStatusUnknown"/>.</summary>
    private bool _isUpgradeStatusUnknown;

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
    /// <param name="commitChange">Applies a change to this repo's settings entry and has it saved,
    ///     returning whether the change was accepted (the owning <see cref="MainWindowViewModel"/>
    ///     applies it at once and saves it in the background, so it accepts every change; another
    ///     owner may refuse one); <see langword="null"/> applies the change to the entry directly and
    ///     saves nothing.</param>
    /// <param name="processStarter">Starts the agent-tool process; <see langword="null"/> uses the
    ///     real <see cref="AgentToolProcessStarter"/>. Supplied so tests can capture the launch
    ///     without starting a shell.</param>
    /// <param name="shellDetector">Detects the shell the agent tool runs in; <see langword="null"/>
    ///     uses a real <see cref="ShellDetector"/>. Supplied so tests get a deterministic shell.</param>
    /// <param name="backgroundWork">Runs the disk, hashing and process work of the card's actions
    ///     (the pin reads and writes, the content identity of an acquired package, the
    ///     <c>.gitignore</c> and release notes of an apply, the AGENTS.md check and answer, the shell
    ///     detection and the start of the agent tool) off the UI thread, and the card's repo-state
    ///     reads when the card has a refresh queue of its own (<paramref name="refreshQueue"/> is
    ///     <see langword="null"/>); <see langword="null"/> uses the thread pool. Supplied so tests can
    ///     hold the work at a gate.</param>
    /// <param name="refreshQueue">The queue the card's repo-state reads go through, shared by every
    ///     card of the window so only a few run at once; <see langword="null"/> gives the card a queue
    ///     of its own over <paramref name="backgroundWork"/> (a card used on its own, as in tests),
    ///     which it shuts down when it is retired.</param>
    /// <param name="gitTimeouts">The time limits of the card's git commands, or
    ///     <see langword="null"/> for <see cref="GitClientTimeouts.Default"/>. Supplied so tests can use a
    ///     short limit against a git that hangs.</param>
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
        Func<RepoImageFile, Bitmap?>? decodeImage = null,
        Func<RecentRepo, Action<RecentRepo>, bool>? commitChange = null,
        IAgentToolProcessStarter? processStarter = null,
        ShellDetector? shellDetector = null,
        IBackgroundWorkRunner? backgroundWork = null,
        RepoRefreshQueue? refreshQueue = null,
        GitClientTimeouts? gitTimeouts = null)
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
        _commitChange = commitChange;
        _processStarter = processStarter ?? new AgentToolProcessStarter();
        _shellDetector = shellDetector ?? new ShellDetector();
        _backgroundWork = backgroundWork ?? ThreadPoolBackgroundWorkRunner.Instance;
        _ownsRefreshQueue = refreshQueue is null;
        _refreshQueue = refreshQueue ?? new RepoRefreshQueue(backgroundWork);
        _retiredToken = _retiredCancellation.Token;
        _gitTimeouts = gitTimeouts;

        LaunchCommand = new AsyncRelayCommand(LaunchAsync, ReportUnexpectedFailure, () => !IsMissing && !IsBusy);
        PullCommand = new AsyncRelayCommand(PullAsync, ReportUnexpectedFailure, () => CanPull && !IsMissing && !IsBusy);
        UpgradeCommand = new AsyncRelayCommand(
            UpgradeAsync, ReportUnexpectedFailure, () => IsUpgradeAvailable && !IsMissing && !IsBusy);
        // Offered for a repo without a package ("Select Package...") and for a pinned one ("Change
        // package..."): the user's way to choose another package or version and to accept content
        // that changed behind a version they already use
        SelectPackageCommand = new RelayCommand(SelectPackage, () => !IsMissing && !IsBusy);
        // Switching branches needs the repo's state read (so the folder is known to exist and to be a
        // repository) and no other operation running
        SwitchBranchCommand = new RelayCommand(
            () => SwitchBranchRequested?.Invoke(this, EventArgs.Empty), () => !IsMissing && !IsBusy && HasRepoState);
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
                SwitchBranchCommand.RaiseCanExecuteChanged();
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
    ///     Changed through the owning <see cref="MainWindowViewModel"/> (see the constructor's
    ///     <c>commitChange</c>), which applies it to the shared <see cref="RecentRepo"/> and saves it in
    ///     the background; only once the owner accepted it does the card raise
    ///     <see cref="FavoriteChanged"/> so the owner can regroup
    ///     <see cref="MainWindowViewModel.DisplayedRepoCards"/>. When the owner refuses the change the
    ///     value is not changed and the star is told to show the real state again.
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

            if (!CommitChange(repo => repo.IsFavorite = value))
            {
                // Refused, so not changed: make the two-way bound star show the real state
                OnPropertyChanged();
                return;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGroupChip));
            FavoriteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///     Applies a change to this repo's settings entry through the owner (which has it saved), or
    ///     directly when the card has no owner.
    /// </summary>
    /// <param name="change">The change to the entry; it must be a plain assignment of fixed values,
    ///     because an owner may run it on a copy of the entry as well.</param>
    /// <returns><see langword="true"/> when the change was applied; <see langword="false"/> when the
    ///     owner refused it and so it was not applied.</returns>
    private bool CommitChange(Action<RecentRepo> change)
    {
        if (_commitChange is null)
        {
            change(_recentRepo);
            return true;
        }

        return _commitChange(_recentRepo, change);
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

    /// <summary>Backing field for <see cref="Depth"/>.</summary>
    private int _depth;

    /// <summary>
    ///     Gets how many groups the card is inside in the list: the depth of the group whose section
    ///     lists it, 0 for a card under Favorites, Ungrouped or a top-level group (and in a flat list).
    ///     Set by the owning <see cref="MainWindowViewModel"/> each time it builds the list.
    /// </summary>
    public int Depth
    {
        get => _depth;
        internal set
        {
            if (SetField(ref _depth, value))
            {
                OnPropertyChanged(nameof(IndentWidth));
            }
        }
    }

    /// <summary>
    ///     Gets how far the card is indented from the left edge of the list, in device-independent
    ///     pixels; the same indent as the header of the group that lists it.
    /// </summary>
    public double IndentWidth => RepoGroupHeaderViewModel.GetIndentWidth(Depth);

    /// <summary>
    ///     Gets the names of the groups that exist now - the groups the repos in the settings are in plus the
    ///     registered groups of the hierarchy (a group created empty or moved within the tree), see
    ///     <see cref="AppSettings.GetGroupNames"/> - sorted, for the "Move to group" submenu.
    /// </summary>
    /// <returns>The distinct group names, alphabetically.</returns>
    public IReadOnlyList<string> GetExistingGroupNames() => _getSettings().GetGroupNames();

    /// <summary>
    ///     Moves this repo into a group, or out of every group.
    /// </summary>
    /// <remarks>
    ///     A name that matches an existing group ignoring letter case joins that group under its own
    ///     spelling, so look-alike groups cannot arise. The choice is stored only in the per-user
    ///     settings (on the shared <see cref="RecentRepo"/>), never in the repository, and goes through
    ///     the owner (see <see cref="IsFavorite"/>). Raises
    ///     <see cref="GroupChanged"/> when the group actually changed, so the owning
    ///     <see cref="MainWindowViewModel"/> rebuilds the list.
    /// </remarks>
    /// <param name="groupName">The group's name, or <see langword="null"/> (or blank) to take the
    ///     repo out of its group.</param>
    /// <returns><see langword="true"/> when the repo is now in the requested place (including when
    ///     it already was); <see langword="false"/> when <paramref name="groupName"/> is not a valid
    ///     group name (too long, or a built-in section's name), or the owner refused the change,
    ///     and nothing changed.</returns>
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

        if (!CommitChange(repo => repo.GroupName = target))
        {
            return false;
        }

        RaiseGroupPropertiesChanged();
        GroupChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    ///     Tells the view that this card's favorite mark and group may have changed because the owning
    ///     <see cref="MainWindowViewModel"/> changed them for several cards at once (a drop of a selection).
    /// </summary>
    internal void RaiseOrganizationChanged()
    {
        OnPropertyChanged(nameof(IsFavorite));
        RaiseGroupPropertiesChanged();
    }

    /// <summary>Backing field for <see cref="SelectionSize"/>.</summary>
    private int _selectionSize;

    /// <summary>
    ///     Gets how many cards are selected together with this one, or 0 when this card is not selected;
    ///     set by the owning <see cref="MainWindowViewModel"/> when the selection changes.
    /// </summary>
    public int SelectionSize
    {
        get => _selectionSize;
        private set
        {
            if (SetField(ref _selectionSize, value))
            {
                OnPropertyChanged(nameof(RemoveMenuText));
                OnPropertyChanged(nameof(RemoveMenuTooltip));
                OnPropertyChanged(nameof(MoveToGroupMenuText));
                OnPropertyChanged(nameof(MoveToGroupMenuTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets the text of the "Move to group" menu item: it says how many repos it moves when this card is
    ///     one of several selected, because the item (and the "New group..." and "Remove from group" items in
    ///     its submenu) then acts on all of them.
    /// </summary>
    public string MoveToGroupMenuText => _selectionSize > 1 ? $"Move {_selectionSize} selected repos to group" : "Move to group";

    /// <summary>
    ///     Gets the tooltip of the "Move to group" menu item.
    /// </summary>
    public string MoveToGroupMenuTooltip => _selectionSize > 1
        ? $"Put the {_selectionSize} selected repos in one of your groups, or start a new group (stored only in your AgentControl settings, never in the repository)"
        : "Put this repo in one of your groups, or start a new group (stored only in your AgentControl settings, never in the repository)";

    /// <summary>
    ///     Gets the text of the "Remove from list" menu item: it says how many repos it removes when this
    ///     card is one of several selected, because the item then acts on all of them.
    /// </summary>
    public string RemoveMenuText => _selectionSize > 1 ? $"Remove {_selectionSize} selected repos from list" : "Remove from list";

    /// <summary>
    ///     Gets the tooltip of the "Remove from list" menu item.
    /// </summary>
    public string RemoveMenuTooltip => _selectionSize > 1
        ? $"Remove the {_selectionSize} selected repos from AgentControl's recent-repos list (does not delete them from disk)"
        : "Remove this repo from AgentControl's recent-repos list (does not delete it from disk)";

    /// <summary>
    ///     Records how many cards are selected together with this one.
    /// </summary>
    /// <param name="size">The number of selected cards when this card is selected, otherwise 0.</param>
    internal void UpdateSelection(int size) => SelectionSize = size;

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

        // The tile follows the group's color when the card has none of its own
        OnPropertyChanged(nameof(GroupColorName));
        OnPropertyChanged(nameof(IsTileColorChosen));
        OnPropertyChanged(nameof(CardColorHeading));
        OnPropertyChanged(nameof(AvatarColorIndex));
        OnPropertyChanged(nameof(AvatarStyleClass));
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
    /// <remarks>
    ///     "Automatic" means the card has no color of its own: the tile then takes its group's color
    ///     (<see cref="GroupColorName"/>) when the group has one, otherwise the color derived from the
    ///     repo path.
    /// </remarks>
    public bool IsCardColorAutomatic => !HasCustomCardColor;

    /// <summary>
    ///     Gets the color the user chose for this repo's group, or <see langword="null"/> when the
    ///     repo is in no group or the group has no color.
    /// </summary>
    /// <remarks>
    ///     Read from the live settings each time (see <see cref="AppSettings.FindGroupColor"/>), so a
    ///     group color chosen, changed or cleared elsewhere is seen once the owning view model calls
    ///     <see cref="RaiseGroupPropertiesChanged"/>. A favorite keeps its group's color: starring a repo
    ///     lists it under Favorites but does not repaint its tile.
    /// </remarks>
    public string? GroupColorName => _getSettings().FindGroupColor(GroupName);

    /// <summary>
    ///     Gets a value indicating whether the tile's color was chosen by the user - for this card, or
    ///     for its group - rather than derived from the repo path; it shows as a ring around a repo's
    ///     picture so the chosen color stays visible.
    /// </summary>
    public bool IsTileColorChosen => HasCustomCardColor || GroupColorName is not null;

    /// <summary>
    ///     Gets the palette heading's text: <c>Card color: Teal</c> for a card color of its own,
    ///     <c>Card color: Automatic (group color Teal)</c> when the tile follows its group's color, or
    ///     <c>Card color: Automatic</c>, so the color in effect is named in words in the palette itself
    ///     and not only by a swatch's tooltip, accessible name and check mark.
    /// </summary>
    public string CardColorHeading
    {
        get
        {
            if (CardColorName is { } own)
            {
                return $"Card color: {own}";
            }

            return GroupColorName is { } group ? $"Card color: Automatic (group color {group})" : "Card color: Automatic";
        }
    }

    /// <summary>
    ///     Chooses the color of this repo's tile.
    /// </summary>
    /// <remarks>
    ///     Only the tile changes: the status accent bar keeps its fixed, meaningful colors. The choice
    ///     is stored by name in the per-user settings only, goes through the owner (see
    ///     <see cref="IsFavorite"/>), and raises <see cref="CardColorChanged"/> once applied.
    /// </remarks>
    /// <param name="colorName">A palette color's name (letter case ignored), or
    ///     <see langword="null"/> (or blank) for the automatic color.</param>
    /// <returns><see langword="true"/> when the card now has the requested color (including when it
    ///     already had); <see langword="false"/> when <paramref name="colorName"/> names no palette
    ///     color or the owner refused the change, and nothing changed.</returns>
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

        var name = requested?.Name;
        if (!CommitChange(repo => repo.CardColor = name))
        {
            return false;
        }

        OnPropertyChanged(nameof(CardColorName));
        OnPropertyChanged(nameof(HasCustomCardColor));
        OnPropertyChanged(nameof(IsCardColorAutomatic));
        OnPropertyChanged(nameof(IsTileColorChosen));
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
    ///     Gets the alias of the configured source that supplies the pinned package on this
    ///     machine, or <see langword="null"/> when none is known (not resolved yet, or no
    ///     configured source delivers it).
    /// </summary>
    /// <remarks>
    ///     A per-user cache in the recent-repos settings (<see cref="RecentRepo.PinnedSource"/>),
    ///     never part of the repo's pin file: the pin names no source, because sources are each
    ///     user's own configuration. It is a hint that saves re-reading every source on each check
    ///     and what Select Package / Change package start on; it is set whenever a source is
    ///     resolved or a package is applied, and cleared when the source is no longer configured.
    /// </remarks>
    public string? SupplyingSource
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
    ///     Gets a user-readable description of why the pinned package cannot be obtained (for example
    ///     no configured source delivers it), or <see langword="null"/> when there is no such
    ///     problem.
    /// </summary>
    /// <remarks>
    ///     This is the card's error state for a pin that no configured source can supply. The card
    ///     never uses a source whose files are not verified to be the ones the pin recorded: every
    ///     action that needs the package reports this problem instead.
    /// </remarks>
    public string? SourceProblem
    {
        get => _sourceProblem;
        private set
        {
            if (SetField(ref _sourceProblem, value))
            {
                // Every chip binding depends on whether a problem is set, so all of them are told
                OnPropertyChanged(nameof(HasSourceProblem));
                OnPropertyChanged(nameof(SourceNeedsSignIn));
                OnPropertyChanged(nameof(ShowSourceProblemChip));
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
    ///     needs a sign-in (which the background check never asks for, or which the user did not
    ///     give when a check they started asked), so the card can say so on the chip itself instead
    ///     of the generic "Package source problem".
    /// </summary>
    /// <remarks>
    ///     Also decides whether the card shows its clickable "Sign-in needed" chip, which runs
    ///     <see cref="RefreshCommand"/> - the user-started check that may ask for the sign-in. It is
    ///     never <see langword="true"/> together with <see cref="ShowSourceProblemChip"/>, so the
    ///     card shows at most one source-problem chip.
    /// </remarks>
    public bool SourceNeedsSignIn => _sourceProblem is not null && _sourceNeedsSignIn;

    /// <summary>
    ///     Gets a value indicating whether the card shows its plain (tooltip-only) red
    ///     source-problem chip: there is a source problem and it is not a missing sign-in.
    /// </summary>
    /// <remarks>
    ///     A missing sign-in has its own clickable chip (see <see cref="SourceNeedsSignIn"/>)
    ///     because a refresh the user starts can fix it; every other source problem (no source
    ///     offers the pin, access denied, invalid source configuration, Git too old) needs the
    ///     user to change something outside the card, so its chip only explains the problem in
    ///     its tooltip. The two are mutually exclusive.
    /// </remarks>
    public bool ShowSourceProblemChip => _sourceProblem is not null && !_sourceNeedsSignIn;

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
                RaiseBranchChipChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the current branch is known, i.e. whether git named a
    ///     branch for the repo.
    /// </summary>
    public bool HasBranch => !string.IsNullOrEmpty(_currentBranch);

    /// <summary>
    ///     Gets a value indicating whether the branch chip is shown: the branch is known, or the card's
    ///     repo state has not been read yet (the chip then shows that the branch is not known yet,
    ///     rather than nothing, which would read as "no branch").
    /// </summary>
    public bool ShowBranchChip => HasBranch || !HasRepoState;

    /// <summary>
    ///     Gets a value indicating whether the branch chip stands for a branch that has not been read
    ///     yet, so the view dims it.
    /// </summary>
    public bool IsBranchUnknown => !HasRepoState;

    /// <summary>
    ///     Gets the text of the branch chip: the current branch, or <c>...</c> while the card's repo
    ///     state has not been read yet.
    /// </summary>
    public string? BranchChipText => HasRepoState ? CurrentBranch : "...";

    /// <summary>
    ///     Gets the tooltip of the branch chip, which says what the chip shows.
    /// </summary>
    public string BranchTooltip => HasRepoState
        ? $"Current git branch: {CurrentBranch}"
        : "Reading the current Git branch...";

    /// <summary>
    ///     Gets a value indicating whether the card's repo state (the folder, the pin file, the branch
    ///     and the committed agent folders) has been read at least once.
    /// </summary>
    /// <remarks>
    ///     A card is shown at once, from the settings alone, and its repo state is read in the
    ///     background (see <see cref="RefreshRepoStateAsync"/>). Until the first read was applied the
    ///     card does not know whether its folder exists, which branch is checked out or whether agent
    ///     folders are committed, so it shows them as unknown (<see cref="RepoCardStatus.Refreshing"/>,
    ///     a dimmed <c>...</c> branch chip, no "No agent package" chip, no committed-files warning)
    ///     rather than as facts. It never turns back to <see langword="false"/>: a later re-read shows
    ///     the known values with <see cref="IsRefreshing"/> next to them.
    /// </remarks>
    public bool HasRepoState
    {
        get => _hasRepoState;
        private set
        {
            if (SetField(ref _hasRepoState, value))
            {
                RaiseBranchChipChanged();
                SwitchBranchCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ShowNoPackageChip));
                RaiseStatusChanged();
            }
        }
    }

    /// <summary>
    ///     Raises the change notifications of every property the branch chip binds to.
    /// </summary>
    private void RaiseBranchChipChanged()
    {
        OnPropertyChanged(nameof(HasBranch));
        OnPropertyChanged(nameof(ShowBranchChip));
        OnPropertyChanged(nameof(IsBranchUnknown));
        OnPropertyChanged(nameof(BranchChipText));
        OnPropertyChanged(nameof(BranchTooltip));
    }

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
    ///     Before the card's repo state was first read (see <see cref="HasRepoState"/>) the card is
    ///     <see cref="RepoCardStatus.Refreshing"/>: whether the folder exists, the pin and so whether an
    ///     upgrade or a problem exists are not known yet. After that a problem (missing folder, package
    ///     source problem or sign-in needed, failed last action)
    ///     outranks everything else, then an available upgrade, then the absence of a package;
    ///     a pinned package whose upgrade check is still running (or whose last check failed) is
    ///     neutral, and only a pinned package whose check succeeded with no newer version reports
    ///     <see cref="RepoCardStatus.UpToDate"/>.
    /// </remarks>
    public RepoCardStatus Status => this switch
    {
        { HasRepoState: false } => RepoCardStatus.Refreshing,
        { IsMissing: true } or { HasSourceProblem: true } or { LastActionFailed: true } => RepoCardStatus.Problem,
        { IsUpgradeAvailable: true } => RepoCardStatus.UpgradeAvailable,
        { IsPackageSelectionNeeded: true } => RepoCardStatus.NoPackage,
        { IsCheckingUpgrade: true } => RepoCardStatus.Checking,
        { IsUpgradeStatusUnknown: true } => RepoCardStatus.Unknown,
        _ => RepoCardStatus.UpToDate
    };

    /// <summary>
    ///     Gets a value indicating whether the card menu's "Agent files" group has anything to offer:
    ///     "Select Package..." (no package pinned yet), "Change package..." (a package is pinned) or
    ///     "Upgrade to ..." (a newer version exists). A repo whose folder is missing has none, so the
    ///     group, its header and its separator are hidden rather than shown empty.
    /// </summary>
    public bool HasAgentFileActions => IsPackageSelectionNeeded || IsUpgradeAvailable || HasPinnedPackage;

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
        RepoCardStatus.Unknown => "Update check failed",
        RepoCardStatus.Refreshing => "Refreshing",
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
        RepoCardStatus.Unknown => "Update check failed: the package source could not be read, so it is not known whether a newer agent package version is available. Use Refresh in the card menu to try again.",
        RepoCardStatus.Refreshing => "Refreshing: reading this repo's folder, pin file and Git state...",
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
    ///     accepted. <b>Ownership:</b> the card owns the bitmap it publishes here. When the value is
    ///     replaced or cleared the superseded bitmap is disposed - right after the change was
    ///     announced, so the tile (bound to this property) no longer shows it - and a bitmap that
    ///     never gets published (a newer refresh took over) is disposed by the refresh that decoded
    ///     it. Consumers must therefore not keep the value beyond the moment they read it.
    /// </remarks>
    public Bitmap? RepoImage
    {
        get => _repoImage;
        private set
        {
            var superseded = _repoImage;
            if (SetField(ref _repoImage, value))
            {
                OnPropertyChanged(nameof(HasRepoImage));
                OnPropertyChanged(nameof(ShowAvatarInitials));
                OnPropertyChanged(nameof(ShowAvatarFolderGlyph));

                // The view has dropped the old bitmap by now (its binding was updated by the
                // notifications above), so the native image can be released
                superseded?.Dispose();
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
        // Numbered here, synchronously when the refresh starts, so a failure can tell whether a
        // newer refresh has taken over since
        var generation = ++_imageGeneration;
        try
        {
            await LoadRepoImageAsync(generation);
        }
        catch (Exception)
        {
            // Whatever went wrong with a repo's picture file - the locator, the decoder or an
            // injected decoder - the card just keeps its generated tile, and the background task
            // never faults. Only the latest refresh may say so: an older one failing late must
            // not erase the picture a newer refresh has already published.
            if (generation == _imageGeneration)
            {
                _loadedImageFile = null;
                RepoImage = null;
            }
        }
    }

    /// <summary>
    ///     The body of <see cref="RefreshRepoImageAsync"/>, which only adds the "any failure means
    ///     no picture" guard around it.
    /// </summary>
    /// <param name="generation">The number of this refresh; a newer refresh supersedes it.</param>
    /// <returns>A task that completes when the picture state is current.</returns>
    private async Task LoadRepoImageAsync(int generation)
    {
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

        // A newer refresh started meanwhile (or the card was removed): let it have the last word.
        // The bitmap this refresh decoded will never be shown, so release it here.
        if (generation != _imageGeneration)
        {
            bitmap?.Dispose();
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
    ///     repo's tile: the color the user chose for this card (<see cref="CardColorName"/>), otherwise
    ///     the color of its group (<see cref="GroupColorName"/>), otherwise the automatic color,
    ///     derived from a stable hash of the repo path so the same repo always has the same color in
    ///     every session and on every machine.
    /// </summary>
    public int AvatarColorIndex =>
        CardColorPalette.Find(_recentRepo.CardColor)?.Index
        ?? CardColorPalette.Find(GroupColorName)?.Index
        ?? GetAvatarColorIndex(RepoPath);

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

        // Both separators are trimmed on every OS (not Path.DirectorySeparatorChar) so a repo
        // keeps the same color on every machine, however its path was typed
        var normalized = repoPath
            .TrimEnd('\\', '/')
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
    ///     Gets the tag the pinned version has in the Git source that supplies it (the source's tag
    ///     pattern applied to the pinned version), or <see langword="null"/> when the supplying
    ///     source is not a configured Git source.
    /// </summary>
    public string? PinnedTagName
    {
        get
        {
            if (SupplyingSourceDefinition is not { Type: PackageSourceType.Git } source ||
                !PackageVersion.TryParseStrict(PinnedPackageVersion, out var version) || version is null ||
                !GitTagPattern.TryCreate(source.TagPattern, out var pattern, out _) || pattern is null)
            {
                return null;
            }

            return pattern.Format(version);
        }
    }

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
    ///     exists but has never had an agent package pinned. Hidden until the card's repo state was
    ///     read (see <see cref="HasRepoState"/>), so a pin that is only not known yet is never shown as
    ///     missing.
    /// </summary>
    public bool ShowNoPackageChip => IsPackageSelectionNeeded && !IsMissing && HasRepoState;

    /// <summary>
    ///     Gets the text of the agent package chip: the pinned package's name and version.
    /// </summary>
    public string PackageChipText => $"{PinnedPackageName} {PinnedPackageVersion}".TrimEnd();

    /// <summary>
    ///     Gets the text of the upgrade chip: the version an upgrade would move the repo to.
    /// </summary>
    public string UpgradeChipText => LatestAvailableVersion ?? string.Empty;

    /// <summary>
    ///     Gets the definition of the source that supplies the pinned package on this machine (see
    ///     <see cref="SupplyingSource"/>), or <see langword="null"/> when none is known or it is no
    ///     longer configured.
    /// </summary>
    private PackageSourceDefinition? SupplyingSourceDefinition
    {
        get
        {
            var alias = SupplyingSource;
            return alias is null
                ? null
                : _getSettings().GetEffectiveSources().FirstOrDefault(
                    source => string.Equals(source.Alias, alias, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the card knows which configured source supplies the
    ///     pinned package, so the agent package chip can name it.
    /// </summary>
    public bool HasSupplyingSource => SupplyingSourceDefinition is not null;

    /// <summary>
    ///     Gets the icon of the source that supplies the pinned package, shown inside the agent
    ///     package chip.
    /// </summary>
    public MaterialIconKind PinnedSourceIcon => PackageSourceIcons.GetIcon(SupplyingSourceDefinition);

    /// <summary>
    ///     Gets the alias of the source that supplies the pinned package, shown as muted text beside
    ///     the source icon in the agent package chip so the source is recognizable at a glance
    ///     without hovering; empty when no configured source is known to supply it.
    /// </summary>
    public string PinnedSourceLabel => SupplyingSourceDefinition?.Alias ?? string.Empty;

    /// <summary>
    ///     Gets a value indicating whether the source that supplies the pinned package requires
    ///     signed packages, so the agent package chip shows a small shield. A zip source, a source
    ///     that is not configured and a source without the requirement show none.
    /// </summary>
    public bool RequiresSignature =>
        SupplyingSourceDefinition is { Type: PackageSourceType.NuGet, RequireSignedPackages: true };

    /// <summary>
    ///     Gets the tooltip of the signature shield: that signed packages are required and, when
    ///     the source is pinned to a publisher, the start of that certificate's fingerprint.
    /// </summary>
    public string SignatureTooltip
    {
        get
        {
            var source = SupplyingSourceDefinition;
            if (source is null || !PackageSourceDefinition.TryNormalizeFingerprint(source.SignerFingerprint, out var fingerprint))
            {
                return "Signature required (any trusted signer)";
            }

            return $"Signature required (signer pinned: {NuGetSourceProvider.SummarizeFingerprint(fingerprint)})";
        }
    }

    /// <summary>
    ///     Gets the tooltip of the agent package chip: which package and version the agent files
    ///     came from and, when a configured source is known to supply them, which source (by alias
    ///     and kind) - plus, for a Git source, the tag the version has under the source's pattern.
    /// </summary>
    public string PackageChipTooltip
    {
        get
        {
            var source = SupplyingSourceDefinition;
            if (source is null)
            {
                return $"Agent files: {PackageChipText}.";
            }

            var tooltip = $"Agent files: {PackageChipText} from source '{source.Alias}' " +
                          $"({PackageSourceIcons.GetShortName(source)})";

            // A Git source is told apart by the tag the version has
            var tag = PinnedTagName;
            if (tag is not null)
            {
                tooltip += $", tag {tag}";
            }

            return tooltip + ".";
        }
    }

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
                SwitchBranchCommand.RaiseCanExecuteChanged();
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
    ///     Gets a value indicating whether the last completed upgrade check failed (the pinned
    ///     package's source could not be read), so nothing is known about whether a newer version
    ///     exists.
    /// </summary>
    /// <remarks>
    ///     Kept until a later check succeeds or the card no longer has anything to check (no pin,
    ///     missing folder, unusable source), so a failed check is never shown as "up to date".
    /// </remarks>
    public bool IsUpgradeStatusUnknown
    {
        get => _isUpgradeStatusUnknown;
        private set
        {
            if (SetField(ref _isUpgradeStatusUnknown, value))
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

        // A Git source follows the same rule: only work the user started may let git show its
        // credential UI; the background check never prompts and reports "sign-in needed" instead
        var git = new GitSourceOptions(
            () => ResolveGitExecutablePath(settings), GetGitCacheRoot(_startupOptions), Interactive: allowPrompt,
            EnforceMinimumGitVersion: true);
        return PackageSourceRegistry.Create(
            settings.GetEffectiveSources(), GetNuGetRootBase(_startupOptions), credentials, git);
    }

    /// <summary>
    ///     Gets the folder under which Git sources keep the packages they fetch: a <c>git-cache</c>
    ///     folder inside the test-isolation configuration directory when one was supplied at
    ///     startup, otherwise <see langword="null"/> for the per-user default.
    /// </summary>
    /// <param name="startupOptions">The startup overrides, or <see langword="null"/>.</param>
    /// <returns>The override cache folder, or <see langword="null"/> for the default. Never a
    ///     repo.</returns>
    internal static string? GetGitCacheRoot(Startup.StartupOptions? startupOptions) =>
        string.IsNullOrWhiteSpace(startupOptions?.ConfigDirectory)
            ? null
            : Path.Combine(Path.GetFullPath(startupOptions.ConfigDirectory), "git-cache");

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
        OnPropertyChanged(nameof(PinnedTagName));
        OnPropertyChanged(nameof(HasSupplyingSource));
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
    ///     Gets the command bound to the card's "Switch branch..." menu item: raises
    ///     <see cref="SwitchBranchRequested"/> so the view layer can show the branch dialog. Available
    ///     while the card's repo state has been read, its folder exists and no other operation of the
    ///     card runs.
    /// </summary>
    public RelayCommand SwitchBranchCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's manual "Refresh" action, re-running the cheap
    ///     checks, the upgrade check and the (otherwise lazy) working-tree dirty check for just
    ///     this card.
    /// </summary>
    /// <remarks>
    ///     Re-reads every configured package source (rather than serving the cache), so the card
    ///     shows what the sources offer now; the cache still serves automatic checks for at most five
    ///     minutes, see <see cref="PackageVersionCache"/>. It is also the command of the card's
    ///     clickable "Sign-in needed" chip (see <see cref="SourceNeedsSignIn"/>): as a check the
    ///     user started it may ask for the sign-in the background check could not, and it is
    ///     unavailable while another operation of the card runs.
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
    ///     Raised after <see cref="IsFavorite"/> changed (accepted by the owner), so the owning
    ///     <see cref="MainWindowViewModel"/> can regroup <see cref="MainWindowViewModel.DisplayedRepoCards"/>.
    /// </summary>
    public event EventHandler? FavoriteChanged;

    /// <summary>
    ///     Raised after <see cref="MoveToGroup"/> changed the repo's group (accepted by the owner), so the
    ///     owning <see cref="MainWindowViewModel"/> can rebuild the sectioned list.
    /// </summary>
    public event EventHandler? GroupChanged;

    /// <summary>
    ///     Raised by <see cref="NewGroupCommand"/> to request the "New group" name prompt; the view
    ///     shows it and, if a name is chosen, calls <see cref="MoveToGroup"/>.
    /// </summary>
    public event EventHandler? NewGroupRequested;

    /// <summary>
    ///     Raised after <see cref="SetCardColor"/> changed the tile color (accepted by the owner).
    /// </summary>
    public event EventHandler? CardColorChanged;

    /// <summary>
    ///     Raised after a launch was recorded (accepted by the owner). The owning
    ///     <see cref="MainWindowViewModel"/> does not listen to it: a launch never moves a card,
    ///     because the list shows the user's own order.
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
    ///     Stops this card for good because it was removed from the recent-repos list: whatever it
    ///     still has running is cancelled or its result ignored, nothing it does afterwards can
    ///     report to the user (no error box, no release notes, no prompt), and the picture it holds is
    ///     released.
    /// </summary>
    /// <remarks>
    ///     Called by <see cref="MainWindowViewModel"/> once the repo was removed. Without it a card
    ///     the user removed - kept alive by handlers the window attached, or by an operation that is
    ///     still running - could still raise <see cref="ErrorOccurred"/> or
    ///     <see cref="ReleaseNotesReady"/> for a repo that is no longer listed. A retired card is never
    ///     reused; adding the repo again creates a new card. Idempotent.
    /// </remarks>
    internal void Retire()
    {
        _isRetired = true;

        // Results of checks, background reads and picture loads that are still on their way are dropped
        _upgradeCheckGeneration++;
        _appliedCheapGeneration = ++_cheapRefreshGeneration;
        _appliedGitStatusGeneration = ++_gitStatusGeneration;
        _imageGeneration++;
        _loadedImageFile = null;
        IsCheckingUpgrade = false;
        CancelOperation();

        // The card's reads that still wait in the queue are given up, so they take no slot; a queue of
        // the card's own is shut down (its running read's git is stopped)
        if (!_retiredToken.IsCancellationRequested)
        {
            _retiredCancellation.Cancel();
            _retiredCancellation.Dispose();
        }

        if (_ownsRefreshQueue)
        {
            _refreshQueue.Dispose();
        }

        // Nobody is listening any more: release the window's handlers and anything they reference
        ReleaseNotesReady = null;
        ErrorOccurred = null;
        FavoriteChanged = null;
        GroupChanged = null;
        NewGroupRequested = null;
        CardColorChanged = null;
        LaunchRecorded = null;
        RemoveRequested = null;
        SelectPackageRequested = null;
        SwitchBranchRequested = null;
        AgentsMdTemplateOfferRequested = null;
        ContentChangeConfirmationRequested = null;
        CommittedFilesConversionRequested = null;
        AgentFilesChoiceRequested = null;

        // The picture is disposed here, once the view has been told it is gone
        RepoImage = null;
    }

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
    ///     Raised by <see cref="SwitchBranchCommand"/> to request the "Switch branch" dialog be shown.
    ///     The dialog is built with <see cref="CreateSwitchBranchViewModel"/>, and the branch the user
    ///     chose is switched to with <see cref="SwitchBranchAsync"/>. Does not itself show any dialog -
    ///     that is a view-layer concern handled by <c>MainWindow</c>'s code-behind, exactly like
    ///     <see cref="SelectPackageRequested"/>.
    /// </summary>
    public event EventHandler? SwitchBranchRequested;

    /// <summary>
    ///     Raised after a successful extraction when the repo has no root-level <c>AGENTS.md</c>
    ///     file, the just-applied package includes an <c>AGENTS.md</c> template, and the user has
    ///     not previously declined this offer for this repo - carries the template's text content
    ///     so the view layer can show a modal Yes/No prompt. Does not itself show any dialog -
    ///     that is a view-layer concern handled by <c>MainWindow</c>'s code-behind via the
    ///     <see cref="ConfirmationWindow"/> pattern, exactly like <see cref="RemoveRequested"/>.
    ///     The user's choice is reported back via <see cref="AcceptAgentsMdTemplateAsync"/> or
    ///     <see cref="DeclineAgentsMdTemplateAsync"/>.
    /// </summary>
    public event EventHandler<string>? AgentsMdTemplateOfferRequested;

    /// <summary>
    ///     Raised by <see cref="ApplySelectedPackageAsync"/> when the user selects the version the
    ///     repo is already pinned to but the source now delivers different content for it (a
    ///     republished package). Nothing in the repo has been changed at that point; the view layer
    ///     shows the request's message in a modal <see cref="ConfirmationWindow"/> and, if the user
    ///     agrees, calls <see cref="ApplySelectedPackageAsync"/> again with the request's selection
    ///     and <c>acceptContentChange: true</c>. Does not itself show any dialog - that is a
    ///     view-layer concern, like <see cref="AgentsMdTemplateOfferRequested"/>.
    /// </summary>
    public event EventHandler<ContentChangeConfirmationRequest>? ContentChangeConfirmationRequested;

    /// <summary>
    ///     Raised when a package is about to be applied (Select Package, Change package or Upgrade)
    ///     to a repo that has managed agent folders tracked by git: asks the user, before anything is
    ///     downloaded or changed, whether AgentControl may stop git tracking those folders so the
    ///     repo can be managed from the package. The view layer shows the request's message in a
    ///     modal <see cref="ConfirmationWindow"/> and answers through
    ///     <see cref="CommittedFilesConversionRequest.Respond"/>; the package operation waits for the
    ///     answer while it holds the card busy. Does not itself show any dialog - that is a
    ///     view-layer concern, like <see cref="ContentChangeConfirmationRequested"/>. Without a
    ///     subscriber nobody can be asked, so the operation stops and changes nothing.
    /// </summary>
    public event EventHandler<CommittedFilesConversionRequest>? CommittedFilesConversionRequested;

    /// <summary>
    ///     Raised by <see cref="LaunchCommand"/> when the agent files on disk differ from the pinned
    ///     package (keep them or update them from the package?) and when a needed restore of the
    ///     agent files failed (launch anyway or cancel?). The view layer shows the request's message
    ///     in a modal <see cref="ConfirmationWindow"/> with the request's two button labels and
    ///     answers through <see cref="AgentFilesChoiceRequest.Respond"/>; the launch waits for the
    ///     answer while it holds the card busy. Does not itself show any dialog - that is a
    ///     view-layer concern, like <see cref="CommittedFilesConversionRequested"/>. Without a
    ///     subscriber nobody can be asked, so the files are kept and a failure is reported
    ///     passively, and the launch proceeds.
    /// </summary>
    public event EventHandler<AgentFilesChoiceRequest>? AgentFilesChoiceRequested;

    /// <summary>
    ///     What to do when a package version that the repo is already pinned to is delivered with
    ///     different content than the pin recorded.
    /// </summary>
    private enum ContentChangeHandling
    {
        /// <summary>
        ///     Stop and tell the user; used by Upgrade, which never replaces a version with a changed
        ///     copy of itself.
        /// </summary>
        Refuse,

        /// <summary>
        ///     Stop and ask the user to confirm through <see cref="ContentChangeConfirmationRequested"/>;
        ///     used by Select Package, the deliberate way to accept a republished version.
        /// </summary>
        Ask,

        /// <summary>
        ///     Go ahead: the user has already confirmed.
        /// </summary>
        Accept
    }

    /// <summary>
    ///     Re-reads the repo's pin file, git working-tree status, and upgrade availability from
    ///     the filesystem/git/package source, updating every bindable property.
    /// </summary>
    /// <remarks>
    ///     The single full-refresh entry point behind <see cref="RefreshCommand"/> (the card menu's
    ///     Refresh and the "Sign-in needed" chip). The repo state - the cheap facts and the working
    ///     tree - is read through the shared refresh queue at the priority of a user action, ahead of
    ///     the background reads and joined with any read of the card that already waits there, so no
    ///     git runs on the UI thread and the card shows <see cref="IsRefreshing"/> meanwhile; then
    ///     <see cref="RefreshUpgradeStatusAsync"/> runs as before.
    ///     It holds <see cref="IsBusy"/> while it runs so no other card action starts meanwhile.
    ///     Because the user asked for it, every configured source (NuGet, zip folder and Git) is read
    ///     again rather than answered from the cache, so a package published since the last listing
    ///     (a new version) is offered at once, and a server that needs a sign-in may ask for it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the refresh: a repo read that has not started yet is
    ///     given up, and the package-source part stops.</param>
    /// <returns>A task that completes when every property is current; a failing source is
    ///     reported through the card's state, not thrown.</returns>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RunOperationAsync("Refreshing...", cancelable: true, RefreshCoreAsync, cancellationToken);

    /// <summary>
    ///     Performs the full refresh for <see cref="RefreshAsync"/> and for the refresh that follows a
    ///     pull (<see cref="PullCoreAsync"/>): the repo state through the shared queue at user-action
    ///     priority, then the upgrade check. It does not take <see cref="IsBusy"/> itself, so it can run
    ///     inside the operation that is already holding it.
    /// </summary>
    /// <param name="token">Cancels the refresh (see <see cref="RefreshAsync"/>).</param>
    /// <returns>A task that completes when every property is current.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is
    ///     cancelled; the card keeps whatever was already applied.</exception>
    private async Task RefreshCoreAsync(CancellationToken token)
    {
        // The repo state is read through the queue, ahead of the background reads; the upgrade check
        // that follows needs the pin it reads
        await RefreshRepoStateForUserAsync(token);
        token.ThrowIfCancellationRequested();
        if (_isRetired)
        {
            return;
        }

        // The user asked for this refresh (or for the action that triggered it), so a server that
        // needs a sign-in may ask for it
        await RefreshUpgradeStatusAsync(token, allowPrompt: true, relistSources: true);
    }

    /// <summary>
    ///     Reads the card's whole repo state - its cheap facts and its working tree - through the shared
    ///     refresh queue at the priority of a user action and waits until it was applied.
    /// </summary>
    /// <remarks>
    ///     The working-tree check is skipped for a folder that is missing, so for a card whose folder
    ///     was missing the cheap facts are read first: when the folder is back, its working tree is then
    ///     checked too (the "Check again" case). Otherwise both are requested together and run side by
    ///     side.
    /// </remarks>
    /// <param name="cancellationToken">Gives up the reads that have not started.</param>
    /// <returns>A task that completes when the reads were applied, dropped or given up; never
    ///     faults.</returns>
    private async Task RefreshRepoStateForUserAsync(CancellationToken cancellationToken)
    {
        if (!IsMissing)
        {
            await RefreshRepoStateAsync(
                RepoRefreshPriority.UserAction, cheapFacts: true, workingTree: true, cancellationToken: cancellationToken);
            return;
        }

        await RefreshRepoStateAsync(
            RepoRefreshPriority.UserAction, cheapFacts: true, workingTree: false, cancellationToken: cancellationToken);
        if (!IsMissing && !_isRetired && !cancellationToken.IsCancellationRequested)
        {
            await RefreshRepoStateAsync(
                RepoRefreshPriority.UserAction, cheapFacts: false, workingTree: true, cancellationToken: cancellationToken);
        }
    }

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
    ///     therefore asynchronous. When
    ///     <see cref="IsMissing"/> is <see langword="true"/>, every other check short-circuits to
    ///     its "unavailable" default rather than attempting a git/filesystem operation against a
    ///     path that does not exist. Runs synchronously on the calling thread, so the application
    ///     never calls it: startup, Refresh, adds and Settings use <see cref="RefreshRepoStateAsync"/>
    ///     (through the shared queue, off the UI thread). It remains for callers that are already off
    ///     the UI thread (tests). Supersedes any background cheap refresh that is still reading.
    /// </remarks>
    public void RefreshCheap()
    {
        // A card that was removed from the list checks nothing any more
        if (_isRetired)
        {
            return;
        }

        ApplyCheapFacts(ReadCheapFacts(
            RepoPath, CreateGitClient(_getSettings()), _committedAgentFilesCache, CancellationToken.None));
    }

    /// <summary>
    ///     Gets a value indicating whether one of the card's repo-state reads (its cheap facts or its
    ///     working-tree check) waits in the shared refresh queue or runs, so the card can show that its
    ///     state is being refreshed.
    /// </summary>
    public bool IsRefreshing => Volatile.Read(ref _refreshesInFlight) > 0;

    /// <summary>
    ///     Gets the task of the most recent <see cref="RefreshRepoStateAsync"/>, so a caller (a test) can
    ///     wait for the card's state to settle. Never faults.
    /// </summary>
    internal Task RefreshStateTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Gets the task of the most recent cheap-facts read through the queue (see
    ///     <see cref="RefreshRepoStateAsync"/> and <see cref="RefreshCheapInBackgroundAsync"/>), so a
    ///     caller (a test) can wait for the card to settle. Never faults.
    /// </summary>
    internal Task CheapRefreshTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Gets the task of the most recent working-tree check through the queue (see
    ///     <see cref="RefreshRepoStateAsync"/> and <see cref="RefreshDirtyStatusInBackgroundAsync"/>), so
    ///     a caller (a test) can wait for the check to settle. Never faults.
    /// </summary>
    internal Task GitStatusRefreshTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Re-reads the card's repo state - its cheap facts, its working tree, or both - through the
    ///     shared refresh queue, off the UI thread, and applies what was found on the caller's (UI)
    ///     thread.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Every lazy refresh of a card goes through here, so however many cards and callers there are,
    ///     only a few git processes run at once (see <see cref="RepoRefreshQueue"/>). A read of the same
    ///     kind that already waits in the queue for this repo is joined rather than queued again, at the
    ///     higher of the two priorities. <see cref="IsRefreshing"/> is true while any of the card's reads
    ///     waits or runs; the card keeps showing its previous state meanwhile.
    ///     </para>
    ///     <para>
    ///     The answer is dropped when the card was retired meanwhile or the answer of a newer read of
    ///     the same kind (through the queue, synchronous, or a direct apply) has already been applied,
    ///     so the newest answer always has the last word. A newer request that was given up applies
    ///     nothing and so never voids an older read; for one repo and kind the queue starts reads in
    ///     request order (a waiting read is joined, a running one is never overtaken by itself). A
    ///     git command that hung and was stopped is shown as the card's status message; an ordinary git
    ///     failure (not a repository, git missing) is shown as unknown state, as before. Must be called
    ///     on the UI thread; the returned task completes there too.
    ///     </para>
    /// </remarks>
    /// <param name="priority">How urgently the state is wanted: background, a card that is shown, or
    ///     a user action.</param>
    /// <param name="cheapFacts">Whether to read the cheap facts (folder, pin file, branch,
    ///     <c>HEAD</c> and committed agent folders).</param>
    /// <param name="workingTree">Whether to check the working tree (<c>git status</c>). Skipped for a
    ///     card whose folder is missing (Pull is then simply not offered).</param>
    /// <param name="reportsProgress">Whether the cheap-facts read counts in the queue's progress wave
    ///     (shown in the global progress strip by the window).</param>
    /// <param name="cancellationToken">Gives up the reads that have not started; nothing is applied
    ///     then.</param>
    /// <returns>A task that completes when every requested read was applied, dropped or given up;
    ///     never faults. Also available as <see cref="RefreshStateTask"/>.</returns>
    internal Task RefreshRepoStateAsync(
        RepoRefreshPriority priority,
        bool cheapFacts,
        bool workingTree,
        bool reportsProgress = false,
        CancellationToken cancellationToken = default)
    {
        var reads = new List<Task>(2);
        if (cheapFacts)
        {
            CheapRefreshTask = ReadCheapFactsThroughQueueAsync(
                priority, reportsProgress, gitFactsOnly: false, cancellationToken);
            reads.Add(CheapRefreshTask);
        }

        if (workingTree)
        {
            GitStatusRefreshTask = ReadWorkingTreeThroughQueueAsync(priority, cancellationToken);
            reads.Add(GitStatusRefreshTask);
        }

        RefreshStateTask = reads.Count switch
        {
            0 => Task.CompletedTask,
            1 => reads[0],
            _ => Task.WhenAll(reads)
        };
        return RefreshStateTask;
    }

    /// <summary>
    ///     Raises the priority of the card's repo-state reads that still wait in the shared queue, for
    ///     example when the card is shown in the window or the user acts on it.
    /// </summary>
    /// <param name="priority">The priority the reads should have at least.</param>
    internal void PrioritizeRepoState(RepoRefreshPriority priority)
    {
        _refreshQueue.Promote(new RepoRefreshKey(RepoPath, RepoRefreshKind.CheapFacts), priority);
        _refreshQueue.Promote(new RepoRefreshKey(RepoPath, RepoRefreshKind.WorkingTree), priority);
    }

    /// <summary>
    ///     Makes sure an action on the card acts on current repo state: moves the card's reads that
    ///     still wait in the shared queue to the front and waits for them, or - when the card's state
    ///     was never read and nothing is pending - reads it now, at the priority of a user action.
    /// </summary>
    /// <remarks>
    ///     The promote-and-await step of Launch, Pull, Upgrade and Select Package (see
    ///     <see cref="RunOperationAsync"/>). A card whose state is known and has nothing pending
    ///     returns at once: an action adds no git round trip of its own, so it acts on the same state
    ///     the card shows. The window is never blocked: this only awaits, on the UI thread, reads that
    ///     run in the background; the wait is bounded by the reads already running in the queue and
    ///     git's time limit. Must be called on the UI thread.
    /// </remarks>
    /// <param name="cancellationToken">Stops waiting (the reads go on and are applied when they
    ///     land).</param>
    /// <returns>A task that completes when the card's pending reads were applied (or dropped).</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while waiting.</exception>
    internal async Task EnsureCurrentRepoStateAsync(CancellationToken cancellationToken = default)
    {
        // A read that waits is moved ahead of everything else; one that runs is simply awaited
        PrioritizeRepoState(RepoRefreshPriority.UserAction);
        await WhenRepoStateSettledAsync().WaitAsync(cancellationToken);

        // Never read, and nothing pending any more (the first read was given up, for example by a newer
        // Settings refresh): read it now
        if (!HasRepoState && !_isRetired)
        {
            _ = RefreshRepoStateAsync(
                RepoRefreshPriority.UserAction, cheapFacts: true, workingTree: false, cancellationToken: CancellationToken.None);
            await WhenRepoStateSettledAsync().WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    ///     Waits, without changing their priority, until none of the card's repo-state reads (its
    ///     cheap facts and its working tree) waits in the shared queue or runs, including reads that
    ///     start while it waits.
    /// </summary>
    /// <returns>A task that completes when the card's state has settled; never faults.</returns>
    internal async Task WhenRepoStateSettledAsync()
    {
        while (!CheapRefreshTask.IsCompleted || !GitStatusRefreshTask.IsCompleted)
        {
            await Task.WhenAll(CheapRefreshTask, GitStatusRefreshTask);
        }
    }

    /// <summary>
    ///     Waits, without changing its priority, until the card's cheap facts (the folder, the pin
    ///     file, the branch and the committed agent folders) are no longer being read, including a read
    ///     that starts while it waits.
    /// </summary>
    /// <remarks>
    ///     A background upgrade check waits for this before it starts: applying cheap facts makes any
    ///     upgrade check in flight irrelevant (it was started against the old pin), so a check started
    ///     before the card's pending read landed would be thrown away, and the card would be left
    ///     without an upgrade answer. The working-tree check does not affect the upgrade check and is
    ///     not waited for.
    /// </remarks>
    /// <returns>A task that completes when the cheap facts have settled; never faults.</returns>
    internal async Task WhenCheapFactsSettledAsync()
    {
        while (!CheapRefreshTask.IsCompleted)
        {
            await CheapRefreshTask;
        }
    }

    /// <summary>
    ///     Runs the same checks as <see cref="RefreshCheap"/> through the shared refresh queue, at
    ///     background priority, reading the disk and git off the UI thread and applying what was found
    ///     on the caller's (UI) thread.
    /// </summary>
    /// <remarks>
    ///     The background form of <see cref="RefreshRepoStateAsync"/> for the cheap facts alone (see
    ///     there for coalescing and for which answers are dropped). Also available as
    ///     <see cref="CheapRefreshTask"/>.
    /// </remarks>
    /// <param name="cancellationToken">Gives up the read before it starts; nothing is applied then.</param>
    /// <returns>A task that completes when the facts were applied (or dropped). Cancellation is not
    ///     an error: the task completes without throwing.</returns>
    internal Task RefreshCheapInBackgroundAsync(CancellationToken cancellationToken = default) =>
        RefreshRepoStateAsync(RepoRefreshPriority.Background, cheapFacts: true, workingTree: false,
            cancellationToken: cancellationToken);

    /// <summary>
    ///     Reads the card's cheap facts through the shared refresh queue and applies them.
    /// </summary>
    /// <param name="priority">The read's priority.</param>
    /// <param name="reportsProgress">Whether the read counts in the queue's progress wave.</param>
    /// <param name="gitFactsOnly">Whether only git's answers (the branch and the committed agent
    ///     folders) are applied, leaving the pin and the upgrade state as they are; used after git's
    ///     index was changed by the card itself (see <see cref="RefreshGitStateAfterUntrackAsync"/>).</param>
    /// <param name="cancellationToken">Gives up the read before it starts.</param>
    /// <returns>A task that completes when the facts were applied (or dropped); never faults.</returns>
    private async Task ReadCheapFactsThroughQueueAsync(
        RepoRefreshPriority priority, bool reportsProgress, bool gitFactsOnly, CancellationToken cancellationToken)
    {
        if (_isRetired)
        {
            return;
        }

        // Numbered and prepared here, on the UI thread, before anything runs in the background
        var generation = ++_cheapRefreshGeneration;
        var repoPath = RepoPath;
        var git = CreateGitClient(_getSettings());
        var cache = _committedAgentFilesCache;

        var facts = await ReadThroughQueueAsync(
            RepoRefreshKind.CheapFacts,
            priority,
            token => ReadCheapFacts(repoPath, git, cache, token),
            reportsProgress,
            cancellationToken);

        // Given up, retired meanwhile, or a newer result has already been applied: that one has the
        // last word
        if (facts is null || _isRetired || generation <= _appliedCheapGeneration)
        {
            return;
        }

        if (gitFactsOnly)
        {
            // Only what git says about the branch and the committed agent folders; a folder that is
            // gone meanwhile is left to the next full read. Numbered as applied either way, so an older
            // full read that lands later cannot put back the state from before the untrack.
            _appliedCheapGeneration = generation;
            if (!facts.IsMissing)
            {
                ShowGitProblem(facts.GitProblem);
                ApplyBranchFacts(facts.CurrentBranch, facts.HeadHash, facts.HasCommittedAgentFiles, facts.GitQueryFailed);
            }

            return;
        }

        ApplyNumberedCheapFacts(facts, generation);
    }

    /// <summary>
    ///     Re-reads, through the shared refresh queue at the priority of a user action, what git says
    ///     about the card's repo after the card itself took the agent folders out of git's index: the
    ///     branch, the committed agent folders and the working tree, and waits until it was applied.
    /// </summary>
    /// <remarks>
    ///     The cached committed-files answer is keyed by the commit and would otherwise keep showing the
    ///     badge, so it is dropped first. Only git's answers are applied (not the pin or the upgrade
    ///     state, which the apply manages itself), as the synchronous re-read this replaces did.
    /// </remarks>
    /// <returns>A task that completes when the answers were applied (or dropped); never faults.</returns>
    private Task RefreshGitStateAfterUntrackAsync()
    {
        _committedAgentFilesCache.Invalidate(RepoPath);
        CheapRefreshTask = ReadCheapFactsThroughQueueAsync(
            RepoRefreshPriority.UserAction, reportsProgress: false, gitFactsOnly: true, CancellationToken.None);
        GitStatusRefreshTask = ReadWorkingTreeThroughQueueAsync(RepoRefreshPriority.UserAction, CancellationToken.None);
        RefreshStateTask = Task.WhenAll(CheapRefreshTask, GitStatusRefreshTask);
        return RefreshStateTask;
    }

    /// <summary>
    ///     Requests one read of the card's repo through the shared queue on behalf of the card, keeping
    ///     <see cref="IsRefreshing"/> up while it waits or runs.
    /// </summary>
    /// <typeparam name="T">The read's result type.</typeparam>
    /// <param name="kind">What is read.</param>
    /// <param name="priority">The read's priority.</param>
    /// <param name="read">The read; runs on a worker thread.</param>
    /// <param name="reportsProgress">Whether the read counts in the queue's progress wave.</param>
    /// <param name="cancellationToken">Gives up the read before it starts.</param>
    /// <returns>The read's result, or <see langword="null"/> when it was given up, the card was
    ///     retired, or the queue was shut down.</returns>
    private async Task<T?> ReadThroughQueueAsync<T>(
        RepoRefreshKind kind,
        RepoRefreshPriority priority,
        Func<CancellationToken, T> read,
        bool reportsProgress,
        CancellationToken cancellationToken)
        where T : class
    {
        // The card's own retirement gives the read up as well as the caller's token
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _retiredToken);
        BeginRefreshing();
        try
        {
            return await _refreshQueue.EnqueueAsync(
                new RepoRefreshKey(RepoPath, kind), priority, read, reportsProgress, giveUp.Token);
        }
        catch (OperationCanceledException)
        {
            // Given up, retired, or the window closed: nothing to apply
            return null;
        }
        finally
        {
            EndRefreshing();
        }
    }

    /// <summary>
    ///     Counts one more of the card's reads as waiting or running, announcing
    ///     <see cref="IsRefreshing"/> when it turns on.
    /// </summary>
    private void BeginRefreshing()
    {
        if (Interlocked.Increment(ref _refreshesInFlight) == 1)
        {
            OnPropertyChanged(nameof(IsRefreshing));
        }
    }

    /// <summary>
    ///     Counts one of the card's reads as ended, announcing <see cref="IsRefreshing"/> when it turns
    ///     off.
    /// </summary>
    private void EndRefreshing()
    {
        if (Interlocked.Decrement(ref _refreshesInFlight) == 0)
        {
            OnPropertyChanged(nameof(IsRefreshing));
        }
    }

    /// <summary>
    ///     Reads what a cheap refresh needs to know about a repo from the disk and git: whether the
    ///     folder exists, the pin file, the branch, <c>HEAD</c> and whether agent folders are
    ///     committed.
    /// </summary>
    /// <remarks>
    ///     Pure I/O with no view-model state, so it is safe on any thread; the shared refresh queue
    ///     runs it on worker threads, and the main window's add runs it for folders that are not cards
    ///     yet. A failure to read the pin file or to run git is captured in the result rather than
    ///     thrown; a git command that hung past its time limit and was stopped is captured as
    ///     <see cref="RepoCheapFacts.GitProblem"/> so the card can say so.
    /// </remarks>
    /// <param name="repoPath">The repository's root folder.</param>
    /// <param name="git">The git client to ask (its executable and time limits).</param>
    /// <param name="cache">The committed-files cache to consult and fill, or <see langword="null"/>
    ///     to always ask git (a repo that has no card yet). The cache is safe for concurrent use.</param>
    /// <param name="cancellationToken">Stops a running git when signaled (the window closed).</param>
    /// <returns>The facts.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    internal static RepoCheapFacts ReadCheapFacts(
        string repoPath, GitClient git, CommittedAgentFilesCache? cache, CancellationToken cancellationToken)
    {
        // A folder that does not exist has nothing else to read
        if (!Directory.Exists(repoPath))
        {
            return new RepoCheapFacts(true, null, null, null, null, false, false);
        }

        // The pin file: a file that cannot be read is reported on the card, not thrown
        RepoPin? pin = null;
        string? pinLoadError = null;
        try
        {
            pin = RepoPinStore.Load(repoPath);
        }
        catch (InvalidOperationException ex)
        {
            pinLoadError = ex.Message;
        }

        // The branch and the committed-files answer, the latter cached by HEAD commit
        try
        {
            var branch = git.GetCurrentBranch(repoPath, cancellationToken);
            var headHash = git.GetHeadCommitHash(repoPath, cancellationToken);
            if (cache is null || !cache.TryGetCached(repoPath, headHash, out var hasCommittedFiles))
            {
                hasCommittedFiles = git.HasCommittedAgentFiles(repoPath, cancellationToken);
                cache?.Set(repoPath, headHash, hasCommittedFiles);
            }

            return new RepoCheapFacts(false, pin, pinLoadError, branch, headHash, hasCommittedFiles, false);
        }
        catch (GitCommandTimeoutException ex)
        {
            return new RepoCheapFacts(false, pin, pinLoadError, null, null, false, true, DescribeGitTimeout(ex));
        }
        catch (InvalidOperationException)
        {
            return new RepoCheapFacts(false, pin, pinLoadError, null, null, false, true);
        }
    }

    /// <summary>
    ///     Describes, for the card's status message, a git command that did not answer in time.
    /// </summary>
    /// <param name="timeout">The timeout.</param>
    /// <returns>What happened and what the user can do.</returns>
    private static string DescribeGitTimeout(GitCommandTimeoutException timeout) =>
        $"Git did not answer within {timeout.Timeout.TotalSeconds:0.###} seconds and was stopped; " +
        "the repo's Git state is unknown. Use Refresh to try again.";

    /// <summary>
    ///     Applies what a cheap refresh read (see <see cref="ReadCheapFacts"/>) to the card's
    ///     properties.
    /// </summary>
    /// <remarks>
    ///     Called on the UI thread, by <see cref="RefreshCheap"/>, by the queued reads of
    ///     <see cref="RefreshRepoStateAsync"/> and by the owning <see cref="MainWindowViewModel"/> for a
    ///     card it creates from facts read in the background while repos were added. A direct apply is
    ///     newer than everything in flight: any upgrade check still in flight is made irrelevant (it was
    ///     started against the old pin), and any background cheap read still in flight is superseded
    ///     (its facts are dropped when they arrive). A git timeout is shown as the card's status message
    ///     (and cleared again by a later read that got an answer). Starts the background picture
    ///     refresh. A retired card is left as it is.
    /// </remarks>
    /// <param name="facts">The facts to apply. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="facts"/> is
    ///     <see langword="null"/>.</exception>
    internal void ApplyCheapFacts(RepoCheapFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ApplyNumberedCheapFacts(facts, ++_cheapRefreshGeneration);
    }

    /// <summary>
    ///     Applies cheap facts with the number of the request that read them, recording it as the
    ///     newest applied number.
    /// </summary>
    /// <param name="facts">The facts to apply.</param>
    /// <param name="generation">The number of the request that read <paramref name="facts"/>;
    ///     higher than every number applied so far.</param>
    private void ApplyNumberedCheapFacts(RepoCheapFacts facts, int generation)
    {
        if (_isRetired)
        {
            return;
        }

        // These facts are now the newest applied: an older background read must not replace them
        _appliedCheapGeneration = generation;

        // The card's repo state is known from now on; the values below replace "not read yet"
        HasRepoState = true;

        // Whatever upgrade check is still in flight was started against the repo, pin and sources as
        // they were: its result must not be published over what this refresh finds (the caller starts
        // a new check when one is wanted)
        InvalidateUpgradeCheck();

        ShowGitProblem(facts.GitProblem);
        IsMissing = facts.IsMissing;
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
            // potentially be reported (stale) once the repo reappears; a working-tree check still
            // running is ignored. Callers that need an immediate fresh check after a
            // missing-to-present transition (rather than waiting for the next lazy/deferred
            // trigger) request one explicitly - see MainWindowViewModel.ApplySettings.
            _appliedGitStatusGeneration = ++_gitStatusGeneration;
            GitStatusChecked = false;
            GitStatusUnavailable = false;
            RaisePackageChipChanged();

            // A folder that does not exist has no picture: no lookup, just the generated tile
            _imageGeneration++;
            _loadedImageFile = null;
            RepoImage = null;
            return;
        }

        ApplyPin(facts.Pin, facts.PinLoadError);
        ApplyBranchFacts(facts.CurrentBranch, facts.HeadHash, facts.HasCommittedAgentFiles, facts.GitQueryFailed);
        EvaluatePinnedSource();
        RaisePackageChipChanged();

        // The picture is looked for in the background, so the UI never waits for the disk or the
        // decoder; the tile shows the generated initials until (and unless) it arrives
        RepoImageRefreshTask = RefreshRepoImageAsync();
    }

    /// <summary>
    ///     Shows a git problem (a command that hung and was stopped) as the card's status message, or
    ///     clears the one shown before once git answered again; a status message from anything else (an
    ///     action's outcome) is left alone.
    /// </summary>
    /// <param name="problem">The problem, or <see langword="null"/> when git answered.</param>
    private void ShowGitProblem(string? problem)
    {
        if (problem is not null)
        {
            _shownGitProblem = problem;
            StatusMessage = problem;
        }
        else if (_shownGitProblem is not null)
        {
            if (StatusMessage == _shownGitProblem)
            {
                StatusMessage = null;
            }

            _shownGitProblem = null;
        }
    }

    /// <summary>
    ///     Re-checks the repo's working-tree cleanliness via <see cref="GitClient"/>, updating
    ///     <see cref="CanPull"/>.
    /// </summary>
    /// <remarks>
    ///     Deliberately not called by <see cref="RefreshCheap"/> or the constructor - per
    ///     architecture.md's repo-fact caching strategy, working-tree dirty/clean state is not
    ///     cacheable by <c>HEAD</c> hash (it reflects uncommitted local edits), so it is computed
    ///     lazily instead. The window uses <see cref="RefreshRepoStateAsync"/> (through the shared
    ///     queue, off the UI thread); this synchronous form remains for callers that already run off
    ///     the hot path. Supersedes any background check still running.
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
    ///     Re-checks the repo's working-tree cleanliness like <see cref="RefreshDirtyStatus"/>, through
    ///     the shared refresh queue at background priority, running <c>git status</c> off the UI thread
    ///     and applying the answer on the caller's (UI) thread.
    /// </summary>
    /// <remarks>
    ///     The background form of <see cref="RefreshRepoStateAsync"/> for the working tree alone. The
    ///     working-tree check is the slowest git call (it scans the whole working tree), so it never
    ///     runs on the UI thread from the window. Until the answer arrives the card keeps its previous
    ///     state; a card never checked stays "not checked yet" (<see cref="GitStatusChecked"/> is
    ///     <see langword="false"/>), so it is never shown as dirty before git has answered. Also
    ///     available as <see cref="GitStatusRefreshTask"/>.
    /// </remarks>
    /// <returns>A task that completes when the answer was applied (or dropped); never faults.</returns>
    internal Task RefreshDirtyStatusInBackgroundAsync() =>
        RefreshRepoStateAsync(
            RepoRefreshPriority.Background, cheapFacts: false, workingTree: true, cancellationToken: CancellationToken.None);

    /// <summary>
    ///     Checks the card's working tree through the shared refresh queue and applies the answer.
    /// </summary>
    /// <param name="priority">The check's priority.</param>
    /// <param name="cancellationToken">Gives up the check before it starts.</param>
    /// <returns>A task that completes when the answer was applied (or dropped); never faults.</returns>
    private async Task ReadWorkingTreeThroughQueueAsync(RepoRefreshPriority priority, CancellationToken cancellationToken)
    {
        if (_isRetired)
        {
            return;
        }

        if (IsMissing)
        {
            CanPull = false;
            return;
        }

        // Numbered and prepared here, on the UI thread; only git runs in the background
        var generation = ++_gitStatusGeneration;
        var repoPath = RepoPath;
        var git = CreateGitClient(_getSettings());

        var answer = await ReadThroughQueueAsync(
            RepoRefreshKind.WorkingTree,
            priority,
            token => ReadWorkingTreeClean(repoPath, git, token),
            reportsProgress: false,
            cancellationToken);

        // Given up, retired meanwhile, or a newer answer has already been applied: that one has the
        // last word
        if (answer is null || _isRetired || generation <= _appliedGitStatusGeneration)
        {
            return;
        }

        _appliedGitStatusGeneration = generation;
        ApplyGitStatus(answer);
    }

    /// <summary>
    ///     Asks git whether a repo's working tree is clean; safe on any thread.
    /// </summary>
    /// <param name="repoPath">The repository's root folder.</param>
    /// <param name="git">The git client to ask.</param>
    /// <param name="cancellationToken">Stops a running git when signaled.</param>
    /// <returns>The answer: clean, dirty, or unknown (not a repository, git missing, or git hung and was
    ///     stopped - the last with the problem to show).</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    private static WorkingTreeAnswer ReadWorkingTreeClean(string repoPath, GitClient git, CancellationToken cancellationToken)
    {
        try
        {
            return new WorkingTreeAnswer(git.IsWorkingTreeClean(repoPath, cancellationToken), null);
        }
        catch (GitCommandTimeoutException ex)
        {
            return new WorkingTreeAnswer(null, DescribeGitTimeout(ex));
        }
        catch (InvalidOperationException)
        {
            return new WorkingTreeAnswer(null, null);
        }
    }

    /// <summary>
    ///     Applies a working-tree answer to <see cref="CanPull"/>, <see cref="GitStatusUnavailable"/>
    ///     and <see cref="GitStatusChecked"/>, showing a git timeout as the card's status message.
    /// </summary>
    /// <param name="answer">The answer (see <see cref="ReadWorkingTreeClean"/>).</param>
    private void ApplyGitStatus(WorkingTreeAnswer answer)
    {
        ShowGitProblem(answer.GitProblem);
        CanPull = answer.Clean ?? false;
        GitStatusUnavailable = answer.Clean is null;
        GitStatusChecked = true;
    }

    /// <summary>
    ///     What a working-tree check found; immutable, so it can be handed from a worker thread to the
    ///     UI thread.
    /// </summary>
    /// <param name="Clean"><see langword="true"/> when clean, <see langword="false"/> when it has
    ///     uncommitted changes, <see langword="null"/> when git could not tell.</param>
    /// <param name="GitProblem">For a git that hung and was stopped, what to tell the user; otherwise
    ///     <see langword="null"/>.</param>
    private sealed record WorkingTreeAnswer(bool? Clean, string? GitProblem);

    /// <summary>
    ///     Records that this repo was just successfully launched, stamping
    ///     <see cref="LastLaunchedUtc"/> with the current UTC time (through the owner, which saves it in
    ///     the background, see <see cref="IsFavorite"/>) and raising <see cref="LaunchRecorded"/>. The
    ///     stamp only records the launch; it does not move the card in the list.
    ///     The launch itself has already happened; when the owner refuses the stamp it is simply not
    ///     recorded.
    /// </summary>
    private void RecordLaunched()
    {
        var now = DateTimeOffset.UtcNow;
        if (!CommitChange(repo => repo.LastLaunchedUtc = now))
        {
            return;
        }

        OnPropertyChanged(nameof(LastLaunchedUtc));
        LaunchRecorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Launches the configured agentic CLI tool in this repo's working directory, per
    ///     architecture.md's <c>AgentToolLauncher</c> subsystem.
    /// </summary>
    /// <remarks>
    ///     This first awaits <see cref="EnsureAgentFilesSyncedBeforeLaunchAsync"/>, which verifies
    ///     the agent files on disk against the pinned package (installing them silently when none
    ///     exist, and asking the user to keep or update them when they differ), then resolves the
    ///     shell/agent command and spawns the process. Per architecture.md's "Verify installed agent
    ///     files before launch" decision (which amends "launch must never be blocked by
    ///     agent-package sync state"), sync state stands in the way of a launch in exactly one case:
    ///     the agent files were needed, could not be restored, and the user chose to cancel the
    ///     launch. Everything else launches - including when the wait is canceled, when the user
    ///     keeps files that differ, when there is no pin or the files are committed, and when nobody
    ///     can be asked - because an agentic tool is useful for migrating agent packages, or with
    ///     zero agent files at all.
    /// </remarks>
    /// <param name="cancellationToken">Signaled when the command itself is cancelled.</param>
    /// <returns>A task that completes once the agent tool has been started, the launch was canceled
    ///     by the user, or the failure has been reported.</returns>
    private Task LaunchAsync(CancellationToken cancellationToken) =>
        RunOperationAsync("Preparing agent files...", cancelable: true, async token =>
        {
            // Canceling the wait for the sync never stops the launch; only an explicit "Cancel"
            // answered to a failed restore does
            var outcome = AgentFilesSyncOutcome.Skipped;
            try
            {
                outcome = await EnsureAgentFilesSyncedBeforeLaunchAsync(token);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Agent file sync was canceled; launching anyway.";
            }

            if (outcome == AgentFilesSyncOutcome.LaunchCanceled)
            {
                StatusMessage = "Launch canceled; the agent files were not restored.";
                return;
            }

            await LaunchAgentToolAsync();
        }, cancellationToken, NoFurtherStateCheck);

    /// <summary>
    ///     Resolves the configured agent tool and shell and starts the tool in this repo's
    ///     working directory, recording the launch on success and reporting any failure.
    /// </summary>
    /// <remarks>
    ///     The settings and the command are read on the UI thread; finding the shell (a probe of
    ///     every <c>PATH</c> folder and the well-known install folders) and starting the process run
    ///     on a worker, so a slow or offline <c>PATH</c> entry never freezes the window. Once handed
    ///     over the start is not canceled. The launch is recorded, and a failure reported, back on
    ///     the UI thread.
    /// </remarks>
    /// <returns>A task that completes when the tool has been started or the failure reported.</returns>
    private async Task LaunchAgentToolAsync()
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

            var shellPreference = settings.ShellPreference;
            var keepShellOpen = settings.KeepShellOpen;
            var repoPath = RepoPath;
            await _backgroundWork.RunAsync(
                () =>
                {
                    var shell = _shellDetector.Detect(shellPreference);
                    var startInfo = AgentLauncher.BuildProcessStartInfo(shell, command, repoPath, keepShellOpen);
                    _processStarter.Start(startInfo);
                    return true;
                },
                CancellationToken.None);
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
    ///     Verifies, before <see cref="LaunchAsync"/> spawns the agent tool, that the agent files
    ///     physically in this repo are the ones the pinned package delivers, and repairs or asks
    ///     when they are not.
    /// </summary>
    /// <param name="cancellationToken">Cancels hashing the installed files, reading the sources and
    ///     waiting for an answer; once the copy into the repo has started it is not interrupted, so
    ///     a repo is never left half extracted.</param>
    /// <returns>The outcome; <see cref="AgentFilesSyncOutcome.LaunchCanceled"/> is the only one
    ///     that stops the launch (see <see cref="AgentFilesSyncOutcome"/> for the others).</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while the files are being hashed, the source is being read or a question is
    ///     open; the launch then proceeds (see <see cref="LaunchAsync"/>).</exception>
    /// <remarks>
    ///     <para>
    ///     Why not "the folders exist": the previous check only looked for the four managed folders,
    ///     so a repo whose folders were present but about half of whose files were missing (an
    ///     interrupted extraction has no rollback) looked synced and was launched forever without a
    ///     restore. The decision now rests on the files themselves. The checks run in this order and
    ///     the first that applies wins:
    ///     </para>
    ///     <list type="number">
    ///         <item>Committed agent files (<see cref="HasCommittedAgentFiles"/>): never touched; sync
    ///         is skipped (<see cref="AgentFilesSyncOutcome.Skipped"/>).</item>
    ///         <item>No pin: nothing to sync (<see cref="AgentFilesSyncOutcome.Skipped"/>); an agentic
    ///         tool is useful with zero managed files.</item>
    ///         <item>No agent file on disk (<see cref="PackageContentExtractor.HasInstalledFiles"/> is
    ///         <see langword="false"/>): the pinned package is installed silently - nothing can be
    ///         lost (<see cref="AgentFilesSyncOutcome.Restored"/>).</item>
    ///         <item>The pin records no files identity (an older pin): there is nothing to compare
    ///         the files with and obtaining the package on every launch would cost a source listing or
    ///         download, so the weaker folder-presence rule applies - all four folders exist: launch
    ///         (<see cref="AgentFilesSyncOutcome.Skipped"/>); a folder is missing: silent restore.</item>
    ///         <item>Otherwise the files are hashed with
    ///         <see cref="PackageContentIdentity.ComputeInstalledFilesIdentity"/> - the algorithm that
    ///         produced the pin's identity - on a worker thread. Equal: launch, with no prompt and
    ///         <b>no access to any package source</b> (<see cref="AgentFilesSyncOutcome.InSync"/>).
    ///         Different (a file missing, changed or added) or not readable (a link, an I/O error): the
    ///         user is asked on every launch to keep the current files (the default) or to update them
    ///         from the package, which deletes the four folders and extracts the pinned package.</item>
    ///     </list>
    ///     <para>
    ///     The package is only obtained when files must be restored or updated, resolved as the
    ///     <em>currently pinned</em> package and version (never the latest - upgrading remains a
    ///     deliberate, separate user action) through the existing caches, with no release-notes
    ///     window; see <see cref="RestorePinnedFilesAsync"/>. A restore that fails raises a question
    ///     (<see cref="AgentFilesChoiceRequested"/>): launch anyway, or cancel (the default). With no
    ///     subscriber nobody can be asked, so a mismatch keeps the files and a failure is reported
    ///     passively through <see cref="ErrorOccurred"/> and the launch proceeds. The pin is never
    ///     written here.
    ///     </para>
    ///     <para>
    ///     Marked <see langword="internal"/> (not <see langword="private"/>) rather than tested
    ///     only through <see cref="LaunchAsync"/> itself, mirroring <c>AgentToolLauncher.BuildProcessStartInfo</c>'s
    ///     own precedent for keeping process-spawning code separated from its unit-testable
    ///     decision logic.
    ///     </para>
    /// </remarks>
    internal async Task<AgentFilesSyncOutcome> EnsureAgentFilesSyncedBeforeLaunchAsync(
        CancellationToken cancellationToken = default)
    {
        // Check 1: committed files are the user's own work and are never touched
        if (HasCommittedAgentFiles)
        {
            StatusMessage = "This repo has committed agent files; skipping agent-package sync.";
            return AgentFilesSyncOutcome.Skipped;
        }

        // Check 2: without a pin there is no package to compare with
        var pinnedPackageName = PinnedPackageName;
        if (pinnedPackageName is null)
        {
            StatusMessage = "No agent package is pinned for this repo; launching without managed agent files.";
            return AgentFilesSyncOutcome.Skipped;
        }

        // Check 3: no agent file at all - install the pinned package silently. The scan reads the
        // disk, so it runs on a worker to keep the UI responsive.
        var hasInstalledFiles = await Task.Run(() => PackageContentExtractor.HasInstalledFiles(RepoPath), cancellationToken);
        if (!hasInstalledFiles)
        {
            return await RestoreOrAskAsync(
                pinnedPackageName, outcomeOnSuccess: AgentFilesSyncOutcome.Restored, updateInstalledFiles: false, cancellationToken);
        }

        // Check 4: a pin without a files identity cannot be verified against the disk
        var recordedIdentity = (await LoadPinForComparisonAsync(cancellationToken))?.ContentIdentity;
        if (!PackageContentIdentity.IsFilesIdentity(recordedIdentity))
        {
            return await EnsureUnverifiablePinFoldersAsync(pinnedPackageName, cancellationToken);
        }

        // Check 5: hash what is installed with the pin's own algorithm; this reads only the repo,
        // so an in-sync repo launches without touching any package source
        string? installedIdentity = null;
        string? unreadableReason = null;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            installedIdentity = await Task.Run(
                () => PackageContentIdentity.ComputeInstalledFilesIdentity(RepoPath, cancellationToken), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // A link, an unreadable file or a case collision: the files cannot be vouched for, which
            // is a reason to ask rather than to launch silently or to overwrite
            unreadableReason = ex.Message;
        }

        stopwatch.Stop();
        if (installedIdentity is not null &&
            PackageContentIdentity.Compare(recordedIdentity, installedIdentity) == ContentIdentityComparison.Same)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Agent files of repo '{RepoName}' match the pin ({Identity}); verified in {ElapsedMs} ms",
                    RepoName, PackageContentIdentity.Describe(installedIdentity), stopwatch.ElapsedMilliseconds);
            }

            return AgentFilesSyncOutcome.InSync;
        }

        // Check 6: the files differ from the pin (or cannot be read) - ask, defaulting to Keep
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "Agent files of repo '{RepoName}' do not match the pin: pinned {Pinned}, installed {Installed}{Reason}",
                RepoName,
                PackageContentIdentity.Describe(recordedIdentity),
                PackageContentIdentity.Describe(installedIdentity),
                unreadableReason is null ? string.Empty : $" ({unreadableReason})");
        }

        return await AskKeepOrUpdateAsync(pinnedPackageName, unreadableReason, cancellationToken);
    }

    /// <summary>
    ///     Applies the weaker launch-time rule to a pin that records no files identity: all four
    ///     managed folders present means launch (nothing can be compared, and fetching the package
    ///     on every launch would be costly); a missing folder means a silent restore.
    /// </summary>
    /// <param name="pinnedPackageName">The pinned package name.</param>
    /// <param name="cancellationToken">Cancels the restore's source access.</param>
    /// <returns>The outcome; see <see cref="EnsureAgentFilesSyncedBeforeLaunchAsync"/>.</returns>
    private async Task<AgentFilesSyncOutcome> EnsureUnverifiablePinFoldersAsync(
        string pinnedPackageName, CancellationToken cancellationToken)
    {
        // Folder presence is a metadata-only check, but on a network repo even that can take a
        // while, so it runs on a worker like every other look at the disk
        var repoPath = RepoPath;
        if (await _backgroundWork.RunAsync(() => PackageZipExtractor.AllManagedFoldersExist(repoPath), cancellationToken))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "The pin of repo '{RepoName}' records no content identity; its agent files cannot be verified, and all managed folders are present",
                    RepoName);
            }

            StatusMessage = "The agent files cannot be verified against this repo's older pin; launching with them.";
            return AgentFilesSyncOutcome.Skipped;
        }

        return await RestoreOrAskAsync(
            pinnedPackageName, outcomeOnSuccess: AgentFilesSyncOutcome.Restored, updateInstalledFiles: false, cancellationToken);
    }

    /// <summary>
    ///     Asks whether to keep the installed agent files that differ from the pin or to replace them
    ///     with the pinned package's files, and acts on the answer.
    /// </summary>
    /// <param name="pinnedPackageName">The pinned package name.</param>
    /// <param name="unreadableReason">Why the installed files could not be read, or
    ///     <see langword="null"/> when they were read and simply differ.</param>
    /// <param name="cancellationToken">Cancels waiting for the answer and the restore's source
    ///     access.</param>
    /// <returns><see cref="AgentFilesSyncOutcome.KeptCurrentFiles"/> when the user keeps the files
    ///     (the default, Escape and the no-subscriber case); otherwise the result of the update (see
    ///     <see cref="RestoreOrAskAsync"/>).</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while the question is open.</exception>
    private async Task<AgentFilesSyncOutcome> AskKeepOrUpdateAsync(
        string pinnedPackageName, string? unreadableReason, CancellationToken cancellationToken)
    {
        var unreadableNote = unreadableReason is null
            ? string.Empty
            : $" Some of them could not be read ({unreadableReason}).";
        var answer = await AskAgentFilesChoiceAsync(
            $"The agent files in '{RepoName}' do not match the package this repo is pinned to " +
            $"({pinnedPackageName} {PinnedPackageVersion}). Some files may be missing, changed or added - for " +
            $"example you may be trying out new agent files.{unreadableNote}\n\n" +
            "Update from package replaces the agent folders (.github/agents, .github/standards, .github/templates, " +
            ".github/skills) with the pinned package's files and discards any changes there. " +
            "Keep current files launches with the files exactly as they are.",
            confirmText: "Update from package",
            declineText: "Keep current files",
            cancellationToken);

        // Keep is the default; with nobody to ask, the files are also left alone
        if (answer != true)
        {
            StatusMessage = "Agent files differ from the pinned package; keeping your current files.";
            return AgentFilesSyncOutcome.KeptCurrentFiles;
        }

        return await RestoreOrAskAsync(
            pinnedPackageName, outcomeOnSuccess: AgentFilesSyncOutcome.Updated, updateInstalledFiles: true, cancellationToken);
    }

    /// <summary>
    ///     Restores (or updates) the agent files from the pinned package and, when that fails, lets
    ///     the user decide whether to launch anyway.
    /// </summary>
    /// <param name="pinnedPackageName">The pinned package name.</param>
    /// <param name="outcomeOnSuccess">The outcome to report when the files were installed.</param>
    /// <param name="updateInstalledFiles"><see langword="true"/> when the files being replaced are
    ///     the user's current files (an explicit update), which only changes the wording of the
    ///     failure question: a failed update may have left the files partially updated or
    ///     incomplete, so the question says so instead of promising the earlier files.</param>
    /// <param name="cancellationToken">Cancels reading the source and waiting for an answer.</param>
    /// <returns><paramref name="outcomeOnSuccess"/> on success. On failure with a subscriber:
    ///     <see cref="AgentFilesSyncOutcome.FailedLaunchAnyway"/> when the user chooses to launch
    ///     anyway, otherwise (the default, Escape) <see cref="AgentFilesSyncOutcome.LaunchCanceled"/>.
    ///     On failure without a subscriber the failure is reported passively through
    ///     <see cref="ErrorOccurred"/> and <see cref="AgentFilesSyncOutcome.FailedLaunchAnyway"/> is
    ///     returned, so a host that cannot ask is never blocked.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while the source is being read or a question is open.</exception>
    private async Task<AgentFilesSyncOutcome> RestoreOrAskAsync(
        string pinnedPackageName,
        AgentFilesSyncOutcome outcomeOnSuccess,
        bool updateInstalledFiles,
        CancellationToken cancellationToken)
    {
        var result = await RestorePinnedFilesAsync(pinnedPackageName, cancellationToken);
        if (result.Succeeded)
        {
            if (outcomeOnSuccess == AgentFilesSyncOutcome.Updated)
            {
                StatusMessage = $"Agent files updated from {pinnedPackageName} {PinnedPackageVersion}.";
            }

            return outcomeOnSuccess;
        }

        // A failed restore: the user decides whether the tool starts without (all of) its files. A failed
        // update may already have deleted the managed folders and extracted only part of the package, so
        // the files on disk can no longer be promised to be the user's earlier ones
        var consequence = updateInstalledFiles
            ? "The agent files could not be updated from the package, so the files in the repo may be partially " +
              "updated or incomplete. Launching anyway starts the agent tool with the files as they are now."
            : "The agent files could not be restored, so the agent tool would start without (all of) them.";
        var answer = await AskAgentFilesChoiceAsync(
            $"{result.Reason}\n\n{consequence} Launch anyway?",
            confirmText: "Launch anyway",
            declineText: "Cancel",
            cancellationToken);
        if (answer is null)
        {
            // Nobody can be asked: report it the old, passive way and never block the launch
            ErrorOccurred?.Invoke(this, $"{result.Reason} {result.PassiveLaunchNote}");
            return AgentFilesSyncOutcome.FailedLaunchAnyway;
        }

        return answer.Value ? AgentFilesSyncOutcome.FailedLaunchAnyway : AgentFilesSyncOutcome.LaunchCanceled;
    }

    /// <summary>
    ///     Raises <see cref="AgentFilesChoiceRequested"/> and waits for the user's answer without
    ///     blocking a thread.
    /// </summary>
    /// <param name="message">The complete question.</param>
    /// <param name="confirmText">The label of the non-default button.</param>
    /// <param name="declineText">The label of the safe, default button.</param>
    /// <param name="cancellationToken">Cancels waiting for the answer.</param>
    /// <returns><see langword="true"/> when the user chose <paramref name="confirmText"/>;
    ///     <see langword="false"/> when the user chose <paramref name="declineText"/>;
    ///     <see langword="null"/> when nobody is subscribed to ask.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while the question is open.</exception>
    private async Task<bool?> AskAgentFilesChoiceAsync(
        string message, string confirmText, string declineText, CancellationToken cancellationToken)
    {
        var handler = AgentFilesChoiceRequested;
        if (handler is null)
        {
            return null;
        }

        StatusMessage = "Waiting for your decision about the agent files.";
        var request = new AgentFilesChoiceRequest(message, confirmText, declineText);
        handler(this, request);
        return await request.Answer.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     What <see cref="RestorePinnedFilesAsync"/> achieved.
    /// </summary>
    /// <param name="Succeeded">Whether the pinned package's files were installed.</param>
    /// <param name="Reason">When it failed, the plain-language cause (without the trailing
    ///     "launching" sentence); otherwise <see langword="null"/>.</param>
    /// <param name="PassiveLaunchNote">When it failed, the sentence a host that cannot ask the user
    ///     appends to <paramref name="Reason"/> before reporting it passively.</param>
    private sealed record RestoreResult(bool Succeeded, string? Reason = null, string? PassiveLaunchNote = null);

    /// <summary>
    ///     Installs the pinned package's agent files into the repo: resolves the <em>currently
    ///     pinned</em> package among the user's configured sources, deletes the four managed folders
    ///     and extracts the package, as the "restore" of a repo whose files are missing and as the
    ///     "update" of one whose files differ.
    /// </summary>
    /// <param name="pinnedPackageName">The pinned package name.</param>
    /// <param name="cancellationToken">Cancels reading the source; the copy into the repo, once
    ///     started, is not interrupted.</param>
    /// <returns>A successful result, or a failed one carrying the reason; never throws for an
    ///     ordinary failure (source unreachable, pinned version missing, a republished package, an
    ///     I/O failure).</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while the source is being read.</exception>
    /// <remarks>
    ///     <para>
    ///     The package is resolved among the configured sources in Settings order: the first source
    ///     whose files have exactly the pin's recorded files identity supplies it; a pin without one
    ///     takes the source it was last supplied from (else the first that offers it). A package
    ///     version that was republished with other files is refused and nothing is extracted. The
    ///     files are extracted directly via <see cref="PackageContentExtractor.Extract"/>,
    ///     deliberately without going through <see cref="ApplyPackageAndShowReleaseNotesAsync"/>: a
    ///     repair at launch must not pop a release-notes window on every launch, and it never writes
    ///     the pin (recording an identity here would dirty the working tree). Existing caches are
    ///     reused; nothing is forced to download again.
    ///     </para>
    /// </remarks>
    private async Task<RestoreResult> RestorePinnedFilesAsync(string pinnedPackageName, CancellationToken cancellationToken)
    {
        try
        {
            var registry = CreateSourceRegistry();
            if (registry.Definitions.Count == 0)
            {
                return new RestoreResult(
                    false, "No package source is configured for this repo.", "Launching without syncing agent files.");
            }

            // The pin names no source (sources are per-user configuration), so the configured
            // sources are asked what they offer of the pinned package and version, in Settings
            // order. A pin that recorded a files identity gets the first source whose acquired
            // files have exactly that identity - a package name and version is never enough, and a
            // source whose files differ is never used. A pin without one (an older pin) has
            // nothing to verify against: it takes the source it was last supplied from, else the
            // source an older version recorded in the file, else the first that offers it, and is
            // never refused.
            var pinnedVersion = PinnedPackageVersion;
            var recordedPin = await LoadPinForComparisonAsync(cancellationToken);
            var (offering, firstFailure, listingAccessDenied) = await FindOfferingSourcesAsync(
                registry, pinnedPackageName, pinnedVersion, cancellationToken);
            if (offering.Count == 0)
            {
                // A source that could not be read is not proof that no source offers the package:
                // report why it could not be read instead
                var reason = firstFailure is not null
                    ? $"A package source could not be read: {SecretRedactor.SafeMessage(firstFailure)}"
                    : DescribeNoSourceOffers(pinnedPackageName, pinnedVersion);
                return new RestoreResult(false, reason, "Launching without syncing agent files.");
            }

            DiscoveredPackage pinnedPackage;
            IPackageContentReader reader;
            if (PackageContentIdentity.IsFilesIdentity(recordedPin?.ContentIdentity))
            {
                var verified = await FindVerifiedSourceAsync(
                    offering, recordedPin!.ContentIdentity, SupplyingSource, registry, listingAccessDenied, cancellationToken);
                if (verified is null)
                {
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning(
                            "Not re-syncing repo '{RepoName}' before launch: no configured source delivers '{PackageName} {Version}' with the files the pin recorded ({Pinned})",
                            RepoName, pinnedPackageName, pinnedVersion,
                            PackageContentIdentity.Describe(recordedPin.ContentIdentity));
                    }

                    return new RestoreResult(
                        false,
                        $"The agent files were not synced. {DescribeNoMatchingFiles(pinnedPackageName, pinnedVersion)}",
                        $"Launching without them. {ContentChangeAdvice}");
                }

                pinnedPackage = verified.Package;
                reader = verified.Reader;
            }
            else
            {
                pinnedPackage = ChooseUnverifiedOffer(offering);
                reader = await registry.Resolve(pinnedPackage.SourceAlias).AcquireAsync(pinnedPackage, cancellationToken);
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Restoring managed agent files for repo '{RepoName}' from package '{PackageName} {Version}' of source '{Source}'",
                    RepoName, pinnedPackage.PackageName, pinnedPackage.Version, pinnedPackage.SourceAlias);
            }

            using var ownedReader = reader;
            cancellationToken.ThrowIfCancellationRequested();

            // Only the check is done at launch: the pin is never written (recording an identity
            // here would dirty the working tree on every launch). The source that supplied the
            // files is remembered in the user's own settings, never in the pin.
            await Task.Run(() => PackageContentExtractor.Extract(reader, RepoPath), CancellationToken.None);
            SupplyingSource = pinnedPackage.SourceAlias;
            await MaybeOfferAgentsMdTemplateAsync(reader, fromLaunchResync: true);
            return new RestoreResult(true);
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to sync agent files for repo '{RepoName}' before launch", RepoName);
            }

            return new RestoreResult(false, $"Failed to sync agent files: {SecretRedactor.SafeMessage(ex)}", "Launching anyway.");
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex, "Package source is unreachable while syncing repo '{RepoName}' before launch", RepoName);
            }

            return new RestoreResult(false, $"Package source is unreachable: {SecretRedactor.SafeMessage(ex)}", "Launching anyway.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Every configured source failing (listing rethrows the first failure) or the copy into
            // the repo failing can surface as a plain I/O or access error; it is an ordinary
            // restore failure, not an unhandled one
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "An I/O failure stopped the agent files of repo '{RepoName}' being restored", RepoName);
            }

            return new RestoreResult(false, $"Failed to sync agent files: {SecretRedactor.SafeMessage(ex)}", "Launching anyway.");
        }
    }

    /// <summary>
    ///     Runs <c>git pull</c> in this repo on a worker thread, then refreshes the whole card the way
    ///     <see cref="RefreshAsync"/> does.
    /// </summary>
    /// <param name="cancellationToken">Signaled when the command itself is cancelled; a pull that
    ///     has already started is not interrupted.</param>
    /// <returns>A task that completes when the pull and the refresh have finished.</returns>
    private Task PullAsync(CancellationToken cancellationToken) =>
        RunOperationAsync(
            "Pulling...",
            cancelable: false,
            PullCoreAsync,
            cancellationToken,
            // Pull is offered only for a working tree git reported clean; when the check that was
            // pending says otherwise, the pull is not made
            () => !GitStatusChecked || CanPull
                ? null
                : "Pull is not available: the working tree has uncommitted changes or its Git status could not be read.");

    /// <summary>
    ///     Performs the pull for <see cref="PullAsync"/>.
    /// </summary>
    /// <remarks>
    ///     A pull can change anything the card shows: the pin file (a new pinned package or version),
    ///     which agent files are committed, the branch and the working tree. So after every completed
    ///     pull - a refused one too, since a merge that stopped on a conflict has already changed the
    ///     working tree and can have written conflict markers into the pin file - the card is refreshed
    ///     in full, inside the pull's own busy operation. The pull's outcome stays the card's status:
    ///     the refresh does not replace it, and only a failure to read the pin file is added to it.
    ///     A pull that has completed cannot be undone, so its outcome also survives the refresh being
    ///     cancelled (the card retired, or the caller's token signaled): the cancellation is absorbed
    ///     here instead of reaching <see cref="RunOperationAsync"/>, which would replace the outcome
    ///     with "The operation was canceled."
    /// </remarks>
    /// <param name="token">Cancels the package-source part of the refresh that follows the pull (the
    ///     pull itself is never interrupted).</param>
    /// <returns>A task that completes when the pull and the refresh have finished.</returns>
    private async Task PullCoreAsync(CancellationToken token)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Pulling repo '{RepoName}' ('{RepoPath}')", RepoName, RepoPath);
        }

        try
        {
            var settings = _getSettings();
            var git = CreateGitClient(settings);
            var result = await Task.Run(() => git.Pull(RepoPath, CancellationToken.None), CancellationToken.None);
            var pullMessage = result.Succeeded
                ? "Pull succeeded."
                : $"Pull failed: {result.StandardError.Trim()}";
            StatusMessage = pullMessage;
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

            // What the pull changed (the pin, the branch, the committed agent files, the working tree)
            // is picked up by the same full refresh the Refresh action runs
            try
            {
                await RefreshCoreAsync(token);
            }
            catch (OperationCanceledException)
            {
                // The pull has already happened and cannot be undone, so canceling the package-source
                // part of the refresh (the card was retired, or the caller gave up) must not turn its
                // outcome into "The operation was canceled." The working tree is what the pull most
                // directly affects, so it is still checked (through the queue, ahead of background
                // reads) - except for a retired card, which checks nothing any more. The upgrade
                // state is left as it was, as when a Refresh is cancelled.
                if (!_isRetired)
                {
                    await RefreshRepoStateAsync(
                        RepoRefreshPriority.UserAction, cheapFacts: false, workingTree: true, cancellationToken: CancellationToken.None);
                }
            }

            // The refresh must not replace the pull's outcome. The only status text it can set is a
            // failure to read the pin file the pull just changed; that is kept after the outcome
            if (StatusMessage != pullMessage && !string.IsNullOrEmpty(StatusMessage))
            {
                StatusMessage = $"{pullMessage} {StatusMessage}";
            }
            else
            {
                StatusMessage = pullMessage;
            }

            LastActionFailed = !result.Succeeded;
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
    ///     Creates the view model of the "Switch branch" dialog for this repo: it lists the branches
    ///     git already knows, read through this card's git client and background work runner.
    /// </summary>
    /// <remarks>
    ///     Exists so the window never builds a git client: which git executable and which time limits
    ///     apply is the card's business. The read runs off the UI thread (through the card's
    ///     <see cref="IBackgroundWorkRunner"/>) when the dialog loads.
    /// </remarks>
    /// <returns>The view model; the dialog loads it when it opens.</returns>
    internal SwitchBranchViewModel CreateSwitchBranchViewModel()
    {
        var git = CreateGitClient(_getSettings());
        var path = RepoPath;
        return new SwitchBranchViewModel(RepoName, token => git.GetBranches(path, token), _backgroundWork);
    }

    /// <summary>
    ///     Switches this repo to the branch the user chose in the "Switch branch" dialog, on a worker
    ///     thread, then refreshes the whole card the way <see cref="RefreshAsync"/> does.
    /// </summary>
    /// <param name="choice">The branch to switch to. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Signaled when the caller gives up; a switch that has already
    ///     started is not interrupted.</param>
    /// <returns>A task that completes when the switch and the refresh have finished (or the switch was
    ///     refused).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="choice"/> is
    ///     <see langword="null"/>.</exception>
    public async Task SwitchBranchAsync(BranchChoice choice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choice);

        try
        {
            await RunOperationAsync(
                $"Switching to {choice.Name}...",
                cancelable: false,
                token => SwitchBranchCoreAsync(choice, token),
                cancellationToken,
                NoFurtherStateCheck);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Called from the window's fire-and-forget handler: an unexpected failure is reported to
            // the user rather than left to tear the application down
            ReportUnexpectedFailure(ex);
        }
    }

    /// <summary>
    ///     Performs the switch for <see cref="SwitchBranchAsync"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Mirrors <see cref="PullCoreAsync"/>: git runs on a worker thread, the outcome becomes the
    ///     card's status, and after a switch that was made the whole card is refreshed, because a
    ///     branch can change everything the card shows - the branch chip, the pin file (each branch can
    ///     pin another package or version, and the launch re-sync rules read the pin at launch, so they
    ///     follow without further work), which agent files are committed and the working tree. A
    ///     switch that has been made cannot be undone, so its outcome survives the refresh being
    ///     cancelled.
    ///     </para>
    ///     <para>
    ///     A switch git refuses (local changes that would be overwritten) changes nothing, so nothing
    ///     is refreshed: the card says so in git's words, and the same text is shown in a message box.
    ///     Nothing is ever forced, stashed or discarded (see <see cref="GitClient.SwitchBranch"/>).
    ///     </para>
    /// </remarks>
    /// <param name="choice">The branch to switch to.</param>
    /// <param name="token">Cancels the package-source part of the refresh that follows the switch
    ///     (the switch itself is never interrupted).</param>
    /// <returns>A task that completes when the switch and the refresh have finished.</returns>
    private async Task SwitchBranchCoreAsync(BranchChoice choice, CancellationToken token)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Switching repo '{RepoName}' ('{RepoPath}') to branch '{Branch}'", RepoName, RepoPath, choice.FullName);
        }

        try
        {
            var git = CreateGitClient(_getSettings());
            var result = await Task.Run(
                () => git.SwitchBranch(RepoPath, choice.FullName, choice.IsRemote, CancellationToken.None),
                CancellationToken.None);

            if (!result.Succeeded)
            {
                // Git refused and changed nothing; say why in git's own words
                var reason = SecretRedactor.Redact(
                        string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError)
                    .Trim();
                if (reason.Length == 0)
                {
                    reason = $"git reported an error (exit code {result.ExitCode}).";
                }

                var refusal = $"Could not switch to '{choice.Name}': {reason} Nothing was changed.";
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(
                        "Switching repo '{RepoName}' to '{Branch}' was refused: {Reason}", RepoName, choice.FullName, reason);
                }

                StatusMessage = refusal;
                ErrorOccurred?.Invoke(this, refusal);
                return;
            }

            var switchMessage = $"Switched to branch '{choice.Name}'.";
            StatusMessage = switchMessage;
            LastActionFailed = false;
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Switched repo '{RepoName}' to branch '{Branch}'", RepoName, choice.FullName);
            }

            // What the switch changed (the branch, the pin, the committed agent files, the working
            // tree) is picked up by the same full refresh the Refresh action runs
            try
            {
                await RefreshCoreAsync(token);
            }
            catch (OperationCanceledException)
            {
                // The switch has already happened and cannot be undone, so canceling the
                // package-source part of the refresh (the card was retired, or the caller gave up)
                // must not turn its outcome into "The operation was canceled." The branch and the
                // working tree are what the switch most directly affects, so they are still read
                // (except for a retired card, which reads nothing any more)
                if (!_isRetired)
                {
                    await RefreshRepoStateAsync(
                        RepoRefreshPriority.UserAction, cheapFacts: true, workingTree: true, cancellationToken: CancellationToken.None);
                }
            }

            // The refresh must not replace the switch's outcome; the only status text it can set is a
            // failure to read the pin file of the branch just switched to, which is kept after it
            StatusMessage = StatusMessage != switchMessage && !string.IsNullOrEmpty(StatusMessage)
                ? $"{switchMessage} {StatusMessage}"
                : switchMessage;
            LastActionFailed = false;
        }
        catch (InvalidOperationException ex)
        {
            // Git could not be started, or did not finish within the switch limit
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to switch repo '{RepoName}' to '{Branch}'", RepoName, choice.FullName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to switch branch: {SecretRedactor.Redact(ex.Message)}");
        }
    }

    /// <summary>
    ///     Runs the full upgrade sequence for this repo: finds the latest package across the
    ///     configured sources, then applies it via <see cref="ApplyPackageAndShowReleaseNotesAsync"/>.
    /// </summary>
    /// <remarks>
    ///     The pin names no source, so the upgrade is resolved among all the user's configured
    ///     sources that offer the package: the newest version wins and a tie keeps Settings order.
    /// </remarks>
    /// <param name="cancellationToken">Signaled when the command itself is cancelled.</param>
    /// <returns>A task that completes when the upgrade has finished or failed.</returns>
    private Task UpgradeAsync(CancellationToken cancellationToken)
    {
        // The upgrade the user asked for is the one from the pin the card showed; when the card's
        // pending read finds another pin, that upgrade is no longer what was offered
        var pinShown = (PinnedPackageName, PinnedPackageVersion);
        return RunOperationAsync(
            "Upgrading agent package...",
            cancelable: true,
            UpgradeCoreAsync,
            cancellationToken,
            () => (PinnedPackageName, PinnedPackageVersion) == pinShown
                ? null
                : "The pinned package changed; check for an upgrade again.");
    }

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

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Checking for an upgrade to package '{PackageName}' for repo '{RepoName}' across the configured sources",
                packageName, RepoName);
        }

        IPackageSourceProvider? provider;
        DiscoveredPackage? latest;
        try
        {
            // The user started the upgrade, so the sources are listed afresh: a version published
            // since the last (cached) background check must be the one that is applied
            foreach (var definition in registry.Definitions)
            {
                _packageVersionCache.Invalidate(definition.Alias);
            }

            // The pin names no source: every configured source that offers the package is asked,
            // the newest version wins and a tie keeps Settings order. Upgrading is a deliberate
            // action, so the source that was actually used is the one the card remembers.
            var check = await _packageVersionCache.IsNewerVersionAvailableAcrossSourcesAsync(
                registry, packageName, PinnedPackageVersion ?? string.Empty, cancellationToken);
            latest = check.Latest;
            provider = latest is null ? null : registry.Resolve(latest.SourceAlias);
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
            provider!, latest, $"Upgraded to {latest.Version}.", "Upgrade", ContentChangeHandling.Refuse, cancellationToken);
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
    /// <param name="acceptContentChange">When <see langword="true"/>, the user has already confirmed
    ///     (through <see cref="ContentChangeConfirmationRequested"/>) that the selected version may
    ///     replace a copy of the same version whose content differs; otherwise such a change asks
    ///     for that confirmation first.</param>
    /// <returns>A task that completes when the package has been applied or the failure has been
    ///     reported.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection"/> is
    ///     <see langword="null"/>.</exception>
    public Task ApplySelectedPackageAsync(
        PackageReference selection, CancellationToken cancellationToken = default, bool acceptContentChange = false)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return RunOperationAsync(
            "Applying agent package...",
            cancelable: true,
            token => ApplySelectedPackageCoreAsync(selection, acceptContentChange, token),
            cancellationToken,
            NoFurtherStateCheck);
    }

    /// <summary>
    ///     Performs the package application for <see cref="ApplySelectedPackageAsync"/>.
    /// </summary>
    /// <param name="selection">The package the user selected.</param>
    /// <param name="acceptContentChange">Whether a changed copy of the pinned version is already
    ///     accepted.</param>
    /// <param name="cancellationToken">Cancels reading the source.</param>
    /// <returns>A task that completes when the package has been applied or the failure has been
    ///     reported.</returns>
    private async Task ApplySelectedPackageCoreAsync(
        PackageReference selection, bool acceptContentChange, CancellationToken cancellationToken)
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
            provider,
            selected,
            $"Package selected: {packageName} {version}.",
            "Select Package",
            acceptContentChange ? ContentChangeHandling.Accept : ContentChangeHandling.Ask,
            cancellationToken);
    }

    /// <summary>
    ///     Shared extract-and-pin sequence reused by both <see cref="UpgradeAsync"/> (after resolving
    ///     the latest version) and <see cref="ApplySelectedPackageAsync"/> (after resolving the exact
    ///     user-selected version): acquires the package from its source, extracts it
    ///     (blind-delete-and-replace), proactively ensures the repo's <c>.gitignore</c> covers the
    ///     managed agent folders, rewrites the pin file (recording the package's source alias and
    ///     content identity and keeping any existing pin's other fields), refreshes this card's pin/upgrade-status
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
    /// <para>
    ///     A repo whose managed agent folders are tracked by git cannot be managed from the package:
    ///     <c>.gitignore</c> has no effect on tracked files, so replacing them would leave git showing
    ///     modified and deleted files while the new files stayed invisible. Before anything is
    ///     acquired this therefore re-checks the tracking with git (never trusting the cached badge)
    ///     and, when some folders are tracked, asks the user through
    ///     <see cref="CommittedFilesConversionRequested"/>; declining changes nothing at all. After
    ///     the user agrees, and the package has passed the content checks, only the folders that are
    ///     tracked are removed from git's index just before the copy, so nothing has changed if the
    ///     package turns out to be unusable, and a failure to do so stops the apply before any file is
    ///     replaced. The user commits the result afterwards.
    ///     </para>
    /// </remarks>
    /// <param name="provider">The provider of the source the package was discovered at.</param>
    /// <param name="package">The resolved package to apply.</param>
    /// <param name="successMessage">The <see cref="StatusMessage"/> text to set on success.</param>
    /// <param name="failureVerb">A short verb phrase (e.g. <c>"Upgrade"</c>, <c>"Select Package"</c>)
    ///     used to prefix any <see cref="ErrorOccurred"/> message raised on failure.</param>
    /// <param name="contentChange">What to do when the repo is already pinned to this very package
    ///     version but the source now delivers different content for it (a republished package);
    ///     see <see cref="ContentChangeHandling"/>. A pin that recorded no content identity is
    ///     never a change: the identity is simply recorded.</param>
    /// <param name="cancellationToken">Cancels acquiring the package.</param>
    /// <returns>A task that completes when the package has been applied or the failure has been
    ///     reported.</returns>
    private async Task ApplyPackageAndShowReleaseNotesAsync(
        IPackageSourceProvider provider,
        DiscoveredPackage package,
        string successMessage,
        string failureVerb,
        ContentChangeHandling contentChange,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Fetching and applying package '{PackageName} {Version}' from source '{SourceAlias}' to repo '{RepoName}' ('{RepoPath}')",
                package.PackageName, package.Version, package.SourceAlias, RepoName, RepoPath);
        }

        // Set once the agent folders have been removed from git's index, so every way out of the
        // apply (success or failure) leaves the card's git chips showing what git now reports
        var untracked = false;
        try
        {
            // Read the existing pin first (on a worker, like all of the apply's disk work): a
            // newer-schema pin must stop the apply before the repo is touched; a corrupt one is
            // simply replaced
            var repoPath = RepoPath;
            var existingPin = await _backgroundWork.RunAsync(() => LoadExistingPinForApply(repoPath), cancellationToken);

            // Managed folders tracked by git must be converted (taken out of git) for the package to
            // manage them, and only with the user's consent: ask before anything is downloaded, and
            // change nothing at all when the user declines. The check is made now, with git, rather
            // than trusting the possibly stale "Committed agent files" badge.
            var trackedFolders = await FindTrackedAgentFoldersAsync();
            if (trackedFolders.Count > 0 &&
                !await ConfirmCommittedFilesConversionAsync(package, trackedFolders, failureVerb, cancellationToken))
            {
                return;
            }

            using var reader = await provider.AcquireAsync(package, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // The identity of the acquired files is a hash of every file: computed on a worker (the
            // configured sources already did it there, and the reader remembers it)
            var contentIdentity = await _backgroundWork.RunAsync(() => reader.ContentIdentity, cancellationToken);

            // A republished version must be noticed before anything in the repo is touched
            if (ContentChangedSincePinned(existingPin, package, contentIdentity) &&
                !ResolveContentChange(existingPin, package, contentIdentity, contentChange, failureVerb))
            {
                return;
            }

            // The package is acquired and accepted: only now is git told to stop tracking the
            // folders, immediately before the files are replaced, so a package that could not be
            // fetched or was refused leaves git exactly as it was. A failure stops here, before any
            // file is replaced.
            if (trackedFolders.Count > 0)
            {
                untracked = await UntrackAgentFoldersAsync(trackedFolders);
            }

            try
            {
                await Task.Run(() => PackageContentExtractor.Extract(reader, RepoPath), CancellationToken.None);
            }
            catch when (untracked)
            {
                // The package could not be applied: put the folders back in git's index so a failed
                // apply never leaves their removal from git staged. The flag stays set so the
                // finally block reports what git says either way, even if restoring failed.
                await RestoreAgentFolderTrackingAsync(trackedFolders);
                throw;
            }

            // Preserve a prior AGENTS.md template decline (and any unknown fields) across
            // re-pinning by updating the loaded pin in place: those fields are orthogonal to the
            // selected package/version and must never be silently reset. The content identity is
            // recorded here (and only on Select Package / Change package / Upgrade, never on the
            // launch re-sync), replacing whatever the previous pin recorded - including nothing, and
            // including the older kinds of identity. The pin names no source and no commit: the
            // identity of the files is all that is needed to prove they were not tampered with.
            var pin = existingPin ?? new RepoPin();
            pin.PackageName = package.PackageName;
            pin.Version = package.Version.ToString();
            pin.ContentIdentity = contentIdentity;

            // The .gitignore, the pin, the release notes and the re-read of the pin all touch the
            // disk: one step on a worker, whose outcome is applied here
            var written = await _backgroundWork.RunAsync(() => WriteAppliedPackage(repoPath, pin, reader), CancellationToken.None);
            if (written.GitIgnoreError is not null)
            {
                ErrorOccurred?.Invoke(this, written.GitIgnoreError);
            }

            if (written.Failure is not null)
            {
                ExceptionDispatchInfo.Capture(written.Failure).Throw();
            }

            // The package is applied; the follow-up status refresh is no longer cancelable so
            // the card never shows the old pin's upgrade state next to the new pin. The source it
            // came from is remembered in the user's own settings (never in the pin). The pin just
            // read is newer than any background cheap read still in flight.
            _appliedCheapGeneration = ++_cheapRefreshGeneration;
            ApplyPin(written.Pin, written.PinLoadError);
            SupplyingSource = package.SourceAlias;
            EvaluatePinnedSource();
            await RefreshUpgradeStatusAsync(CancellationToken.None);
            RaisePackageChipChanged();

            // A converted repo needs one more step from the user: the removal from git is only
            // staged, and committing it is what finishes the change
            StatusMessage = untracked
                ? $"{successMessage} The agent folders are no longer tracked by git - commit the change " +
                  "(deletions, .gitignore and .agentcontrol.json) to finish."
                : successMessage;

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
            await MaybeOfferAgentsMdTemplateAsync(reader);
            ReleaseNotesReady?.Invoke(this, written.ReleaseNotes);
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
        finally
        {
            // Git's index changed, so whatever the apply went on to do (or failed to do), the card
            // must report what git now says: re-read through the queue, ahead of background reads
            // and off the UI thread, while the card is still busy
            if (untracked)
            {
                await RefreshGitStateAfterUntrackAsync();
            }
        }
    }

    /// <summary>
    ///     Reads the pin an apply updates; reads the disk, so it runs on a worker.
    /// </summary>
    /// <param name="repoPath">The repo's root folder.</param>
    /// <returns>The pin, or <see langword="null"/> when there is none or it cannot be read (a
    ///     corrupt pin is simply replaced).</returns>
    /// <exception cref="UnsupportedPinSchemaException">Thrown when the pin was written by a newer
    ///     AgentControl; the apply must then stop before the repo is touched.</exception>
    private static RepoPin? LoadExistingPinForApply(string repoPath)
    {
        try
        {
            return RepoPinStore.Load(repoPath);
        }
        catch (InvalidOperationException ex) when (ex is not UnsupportedPinSchemaException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Does the disk work that follows a successful extraction: makes the <c>.gitignore</c> cover
    ///     the managed folders, writes the new pin, reads the package's release notes and reads the
    ///     pin back for the card. Runs on a worker; touches no view-model state.
    /// </summary>
    /// <param name="repoPath">The repo's root folder.</param>
    /// <param name="pin">The pin to write.</param>
    /// <param name="reader">The applied package.</param>
    /// <returns>What happened; a <c>.gitignore</c> failure, and a pin that cannot be written or release
    ///     notes that cannot be read, are carried in the result rather than thrown, so the caller reports
    ///     them in order.</returns>
    private static AppliedPackageWrite WriteAppliedPackage(string repoPath, RepoPin pin, IPackageContentReader reader)
    {
        // A .gitignore failure never aborts the pin write or the release notes that follow it
        string? gitIgnoreError = null;
        try
        {
            GitIgnoreEnsurer.Ensure(repoPath);
        }
        catch (InvalidOperationException ex)
        {
            gitIgnoreError = ex.Message;
        }

        try
        {
            RepoPinStore.Save(repoPath, pin);
        }
        catch (InvalidOperationException ex)
        {
            return new AppliedPackageWrite(gitIgnoreError, ex, null, null, string.Empty);
        }

        string releaseNotes;
        try
        {
            releaseNotes = PackageContentExtractor.ReadReleaseNotes(reader) ?? string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            return new AppliedPackageWrite(gitIgnoreError, ex, null, null, string.Empty);
        }

        RepoPin? written = null;
        string? loadError = null;
        try
        {
            written = RepoPinStore.Load(repoPath);
        }
        catch (InvalidOperationException ex)
        {
            loadError = ex.Message;
        }

        return new AppliedPackageWrite(gitIgnoreError, null, written, loadError, releaseNotes);
    }

    /// <summary>
    ///     The outcome of <see cref="WriteAppliedPackage"/>; immutable, so it can be handed from the
    ///     worker to the UI thread.
    /// </summary>
    /// <param name="GitIgnoreError">Why the <c>.gitignore</c> could not be updated, or
    ///     <see langword="null"/>.</param>
    /// <param name="Failure">Why the pin could not be written or the release notes could not be read,
    ///     or <see langword="null"/>; when set, nothing after the failed step was done.</param>
    /// <param name="Pin">The pin read back after the write, or <see langword="null"/>.</param>
    /// <param name="PinLoadError">Why the pin could not be read back, or <see langword="null"/>.</param>
    /// <param name="ReleaseNotes">The package's release notes; empty when it has none.</param>
    private sealed record AppliedPackageWrite(
        string? GitIgnoreError, InvalidOperationException? Failure, RepoPin? Pin, string? PinLoadError, string ReleaseNotes);

    /// <summary>
    ///     Asks git, right now, which of the managed agent folders have files tracked by git.
    /// </summary>
    /// <returns>The tracked folders as repo-relative paths (for example <c>.github/agents</c>), or an
    ///     empty list when none is tracked or git cannot tell.</returns>
    /// <remarks>
    ///     Deliberately does not use the cached "Committed agent files" badge: the conversion removes
    ///     tracked files from the repo's history-facing state, so the decision must rest on what git
    ///     reports at this moment, not on what it reported for an earlier commit. A failing git (not
    ///     installed, not a repository) is treated as "nothing tracked", exactly as the badge itself
    ///     treats it, so such a repo is applied as it always was.
    /// </remarks>
    private async Task<IReadOnlyList<string>> FindTrackedAgentFoldersAsync()
    {
        try
        {
            var git = CreateGitClient(_getSettings());
            return await Task.Run(() => git.GetTrackedAgentFolders(RepoPath), CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Could not determine which agent folders git tracks for repo '{RepoName}'", RepoName);
            }

            return [];
        }
    }

    /// <summary>
    ///     Asks the user, through <see cref="CommittedFilesConversionRequested"/>, whether AgentControl
    ///     may stop git tracking the repo's tracked agent folders so the package can manage them.
    /// </summary>
    /// <param name="package">The package that will be applied if the user agrees.</param>
    /// <param name="trackedFolders">The folders that have tracked files; never empty.</param>
    /// <param name="failureVerb">The action name used in messages.</param>
    /// <param name="cancellationToken">Cancels waiting for the answer.</param>
    /// <returns><see langword="true"/> when the user agreed; <see langword="false"/> when the user
    ///     declined or nobody could be asked, in which case nothing has been changed and the user has
    ///     been told so.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while the question is open.</exception>
    private async Task<bool> ConfirmCommittedFilesConversionAsync(
        DiscoveredPackage package,
        IReadOnlyList<string> trackedFolders,
        string failureVerb,
        CancellationToken cancellationToken)
    {
        var folders = string.Join(", ", trackedFolders);
        var handler = CommittedFilesConversionRequested;
        if (handler is null)
        {
            // Changing what git tracks is never done without an explicit yes
            ErrorOccurred?.Invoke(
                this,
                $"{failureVerb} stopped. '{RepoName}' has agent files committed to git in {folders}, and " +
                "AgentControl needs your confirmation to stop tracking them. Nothing was changed.");
            return false;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Repo '{RepoName}' has agent files tracked by git in {Folders}; asking whether to stop tracking them",
                RepoName, folders);
        }

        StatusMessage = "Waiting for your confirmation to stop git tracking the agent folders.";
        var request = new CommittedFilesConversionRequest(
            $"'{RepoName}' has agent files committed to git in {folders}. " +
            "AgentControl manages these folders from the package and keeps them out of git. " +
            "To continue, AgentControl will stop tracking them in git (the files stay on disk until the " +
            $"package replaces them), add them to .gitignore, and apply {package.PackageName} {package.Version}. " +
            "Commit the result afterwards to finish the change.\n\nContinue?");
        handler(this, request);

        if (await request.Answer.WaitAsync(cancellationToken))
        {
            return true;
        }

        StatusMessage = $"{failureVerb} canceled. The agent files are still committed to git; nothing was changed.";
        return false;
    }

    /// <summary>
    ///     Stops git tracking the given agent folders (index only; the files stay on disk).
    /// </summary>
    /// <param name="trackedFolders">The folders that have tracked files; never empty.</param>
    /// <returns><see langword="true"/> once git has removed the folders from its index.</returns>
    /// <exception cref="InvalidOperationException">Thrown when git could not do it; the message
    ///     explains that nothing was changed and carries git's own explanation, so the apply stops
    ///     before any file is replaced.</exception>
    private async Task<bool> UntrackAgentFoldersAsync(IReadOnlyList<string> trackedFolders)
    {
        var git = CreateGitClient(_getSettings());
        try
        {
            await Task.Run(() => git.UntrackAgentFolders(RepoPath, trackedFolders), CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"AgentControl could not stop git tracking the agent folders, so nothing was changed. {ex.Message}", ex);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Stopped git tracking {Folders} for repo '{RepoName}'", string.Join(", ", trackedFolders), RepoName);
        }

        return true;
    }

    /// <summary>
    ///     Rolls back <see cref="UntrackAgentFoldersAsync"/> after the package could not be applied:
    ///     restores the folders' entries in git's index (the working tree is untouched). Best effort -
    ///     a failure is logged, never thrown, so the original apply error is the one reported.
    /// </summary>
    /// <param name="trackedFolders">The folders that were untracked; never empty.</param>
    private async Task RestoreAgentFolderTrackingAsync(IReadOnlyList<string> trackedFolders)
    {
        var git = CreateGitClient(_getSettings());
        try
        {
            await Task.Run(() => git.RestoreAgentFolderTracking(RepoPath, trackedFolders), CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(
                ex, "Could not restore git tracking of {Folders} for repo '{RepoName}'",
                string.Join(", ", trackedFolders), RepoName);
        }
    }

    /// <summary>
    ///     A package from a configured source that was verified to deliver the identical files the
    ///     repo's pin recorded, with its content opened for reading.
    /// </summary>
    /// <param name="Package">The package as that source lists it.</param>
    /// <param name="Reader">The opened content; disposed with this value.</param>
    private sealed record VerifiedSource(DiscoveredPackage Package, IPackageContentReader Reader) : IDisposable
    {
        /// <summary>
        ///     Releases the opened content.
        /// </summary>
        public void Dispose() => Reader.Dispose();
    }

    /// <summary>
    ///     Asks every configured source, in Settings order, which of them offer the pinned package
    ///     and version.
    /// </summary>
    /// <param name="registry">The configured sources.</param>
    /// <param name="packageName">The pinned package name.</param>
    /// <param name="version">The pinned version text.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <param name="failureMemo">The failures recorded so far by the upgrade check the listing
    ///     belongs to: the current background check round's (the periodic round or the launch-time
    ///     check), or the memo a repo card's check the user started (Refresh, the "Sign-in needed"
    ///     chip) keeps of its own for its length - <see cref="RefreshUpgradeStatusAsync"/> always
    ///     passes one; or <see langword="null"/> for a listing outside any such check (restoring the
    ///     pinned files when the agent tool is launched); see
    ///     <see cref="PackageVersionCache.GetVersionsDescendingAsync"/>.</param>
    /// <returns>The offers of the package at exactly that version in Settings order (a source
    ///     with several spellings of the version lists each), the failure to report for a source
    ///     that could not be read (and was skipped) - a refusal of access (HTTP 403) when there was one,
    ///     since that is the one a person can act on, otherwise the first failure - or
    ///     <see langword="null"/>, and the first refusal of access, or <see langword="null"/>.</returns>
    /// <exception cref="IOException">Thrown when every source failed to list; see
    ///     <see cref="PackageVersionCache.GetVersionsAcrossSourcesAsync"/>.</exception>
    private async Task<(List<DiscoveredPackage> Offering, Exception? FirstFailure, PackageSourceAccessException? FirstAccessDenied)>
        FindOfferingSourcesAsync(
            PackageSourceRegistry registry,
            string packageName,
            string? version,
            CancellationToken cancellationToken,
            SourceFailureMemo? failureMemo = null)
    {
        Exception? firstFailure = null;
        PackageSourceAccessException? firstAccessDenied = null;
        var offers = await _packageVersionCache.GetVersionsAcrossSourcesAsync(
            registry, packageName, excludeAlias: null, cancellationToken,
            failure =>
            {
                firstFailure ??= failure;
                if (failure is PackageSourceAccessException { Kind: PackageSourceFailureKind.AccessDenied } denied)
                {
                    firstAccessDenied ??= denied;
                }
            },
            failureMemo);
        return ([.. offers.Where(candidate => IsVersion(candidate, version))], firstAccessDenied ?? firstFailure, firstAccessDenied);
    }

    /// <summary>
    ///     Chooses, for a pin that recorded no files identity (so nothing can be verified), which of
    ///     the sources that offer the pinned package and version supplies it.
    /// </summary>
    /// <param name="offering">The offers in Settings order; not empty.</param>
    /// <returns>The offer of the source this card was last supplied from, else of the source an
    ///     older version recorded in the pin file (a hint only), else the first offer.</returns>
    private DiscoveredPackage ChooseUnverifiedOffer(IReadOnlyList<DiscoveredPackage> offering) =>
        new[] { SupplyingSource, _pinLegacySource }
            .Where(hint => hint is not null)
            .Select(hint => offering.FirstOrDefault(
                offer => string.Equals(offer.SourceAlias, hint, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(offer => offer is not null) ?? offering[0];

    /// <summary>
    ///     Looks, among the sources that offer the pinned package and version, for the first one that
    ///     delivers the identical files the repo's pin recorded.
    /// </summary>
    /// <param name="offering">The offers of the package at the pinned version in Settings order.</param>
    /// <param name="pinnedIdentity">The files identity the pin recorded; without one there is
    ///     nothing to verify against and no source is accepted.</param>
    /// <param name="preferredAlias">The source this card was last supplied from, tried first (the
    ///     files it delivers are checked like any other's), or <see langword="null"/>.</param>
    /// <param name="registry">The configured sources.</param>
    /// <param name="listingAccessDenied">The first refusal of access (HTTP 403) a source gave while it
    ///     was listed for the offers, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The first source whose acquired files have exactly the recorded identity;
    ///     <see langword="null"/> when there is none. The caller disposes the result.</returns>
    /// <remarks>
    ///     A candidate is acquired only because it lists the package and version, and the sources are
    ///     tried in Settings order (the last supplier first, which is only a saving: identical files
    ///     are identical whichever source delivers them). A candidate that cannot be read or acquired
    ///     is skipped (the next one may still match), and one that delivers other files is
    ///     rejected - a package name and version is never enough. The exception is a refusal of
    ///     access (the server answered 403: <see cref="PackageSourceFailureKind.AccessDenied"/>) that
    ///     is remembered, not lost: when no source verifies, the first such refusal is rethrown, so
    ///     the caller reports "access denied, and why" instead of claiming that no source has the
    ///     files (the source that refused may be the one that has them). When another source does
    ///     verify, the refusal is only logged. A refusal that a source gave while it was
    ///     <em>listed</em> (it is then not among the offers, so it is never acquired) counts the same: it is
    ///     passed in as <paramref name="listingAccessDenied"/> and is the first refusal. Credentials are whatever the
    ///     registry was built with: a background check never prompts.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is canceled.</exception>
    /// <exception cref="PackageSourceAccessException">Thrown, with kind
    ///     <see cref="PackageSourceFailureKind.AccessDenied"/>, when no source verified and a source
    ///     refused access, while it was listed or while it was read.</exception>
    private async Task<VerifiedSource?> FindVerifiedSourceAsync(
        IReadOnlyList<DiscoveredPackage> offering,
        string? pinnedIdentity,
        string? preferredAlias,
        PackageSourceRegistry registry,
        PackageSourceAccessException? listingAccessDenied,
        CancellationToken cancellationToken)
    {
        // A pin without a files identity (an older pin, or a source that named none) has nothing
        // to verify a source against: trust nothing rather than guess
        if (!PackageContentIdentity.IsFilesIdentity(pinnedIdentity))
        {
            return null;
        }

        PackageSourceAccessException? firstAccessDenied = listingAccessDenied;
        foreach (var offer in offering.OrderBy(
                     candidate => string.Equals(candidate.SourceAlias, preferredAlias, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            IPackageContentReader? reader = null;
            try
            {
                var provider = registry.Resolve(offer.SourceAlias);
                reader = await provider.AcquireAsync(offer, cancellationToken);

                // The identity is a hash of every file: computed on a worker (the configured
                // sources already did it there, and the reader remembers it)
                var acquired = reader;
                var identity = await _backgroundWork.RunAsync(() => acquired.ContentIdentity, cancellationToken);
                if (PackageContentIdentity.Compare(pinnedIdentity, identity) == ContentIdentityComparison.Same)
                {
                    var verified = new VerifiedSource(offer, reader);
                    reader = null;
                    return verified;
                }

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Source '{Source}' offers '{PackageName} {Version}' but its files differ from the pin of repo '{RepoName}' ({Pinned} vs {Actual}); not used",
                        offer.SourceAlias, offer.PackageName, offer.Version, RepoName,
                        PackageContentIdentity.Describe(pinnedIdentity), PackageContentIdentity.Describe(identity));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                           or ArgumentException)
            {
                // A source that cannot deliver the package says nothing about the others; a refusal
                // of access is remembered so that it is reported when no source verifies
                if (ex is PackageSourceAccessException { Kind: PackageSourceFailureKind.AccessDenied } denied)
                {
                    firstAccessDenied ??= denied;
                }

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        ex, "Source '{Source}' could not deliver '{PackageName} {Version}' for repo '{RepoName}'; trying the next source",
                        offer.SourceAlias, offer.PackageName, offer.Version, RepoName);
                }
            }
            finally
            {
                reader?.Dispose();
            }
        }

        if (firstAccessDenied is not null)
        {
            ExceptionDispatchInfo.Capture(firstAccessDenied).Throw();
        }

        return null;
    }

    /// <summary>
    ///     Explains, for the user, that no configured source offers the pinned package and version.
    /// </summary>
    /// <param name="packageName">The pinned package name.</param>
    /// <param name="version">The pinned version text.</param>
    /// <returns>A plain-language sentence or two; it contains no hashes.</returns>
    private static string DescribeNoSourceOffers(string packageName, string? version) =>
        $"No configured source offers {packageName} {version}. Add a source that delivers it in Settings, or use " +
        "Change package... to select the package from a configured source.";

    /// <summary>
    ///     Explains, for the user, that sources offer the pinned package and version but none of
    ///     them delivers the files the repo was set up with.
    /// </summary>
    /// <param name="packageName">The pinned package name.</param>
    /// <param name="version">The pinned version text.</param>
    /// <returns>A plain-language sentence or two; it contains no hashes.</returns>
    private static string DescribeNoMatchingFiles(string packageName, string? version) =>
        $"No configured source delivers {packageName} {version} with the files this repo was set up with: it now " +
        "contains different files than the copy this repo was set up with, although the version number is the " +
        "same. The package may have been republished.";

    /// <summary>
    ///     Reads the repo's pin for a content-identity comparison on a worker thread, tolerating an
    ///     unreadable file.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read before it starts.</param>
    /// <returns>The pin, or <see langword="null"/> when there is none or it cannot be read (in
    ///     which case there is no recorded identity to compare against).</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     was canceled before the read started.</exception>
    private Task<RepoPin?> LoadPinForComparisonAsync(CancellationToken cancellationToken)
    {
        var repoPath = RepoPath;
        return _backgroundWork.RunAsync(() => LoadPinForComparison(repoPath), cancellationToken);
    }

    /// <summary>
    ///     Reads a repo's pin for a content-identity comparison, tolerating an unreadable file;
    ///     reads the disk, so it runs on a worker.
    /// </summary>
    /// <param name="repoPath">The repo's root folder.</param>
    /// <returns>The pin, or <see langword="null"/> when there is none or it cannot be read.</returns>
    private static RepoPin? LoadPinForComparison(string repoPath)
    {
        try
        {
            return RepoPinStore.Load(repoPath);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Determines whether a package that is about to be applied or re-synced is the very version
    ///     the repo is pinned to, yet its content is no longer what the pin recorded - that is,
    ///     whether the version was republished.
    /// </summary>
    /// <param name="pin">The repo's current pin, or <see langword="null"/> when there is none.</param>
    /// <param name="package">The package that was acquired.</param>
    /// <param name="contentIdentity">The identity of the acquired package's files.</param>
    /// <returns><see langword="true"/> only when the pin names the same package and version (from any
    ///     source: the identity describes the delivered files, not the source) and both identities are
    ///     known and differ. A different version, a pin without a files identity (a pin made before
    ///     files identities were recorded) and a source that reports no identity are never a change.</returns>
    private static bool ContentChangedSincePinned(RepoPin? pin, DiscoveredPackage package, string? contentIdentity) =>
        pin is not null &&
        string.Equals(pin.PackageName, package.PackageName, StringComparison.OrdinalIgnoreCase) &&
        IsVersion(package, pin.Version) &&
        PackageContentIdentity.Compare(pin.ContentIdentity, contentIdentity) == ContentIdentityComparison.Changed;

    /// <summary>
    ///     Describes, for the user, that a package version now delivers different content than the
    ///     copy the repo was set up with.
    /// </summary>
    /// <param name="package">The package whose content changed.</param>
    /// <returns>A plain-language sentence naming the package and its source; it deliberately
    ///     contains no hashes.</returns>
    private static string DescribeContentChange(DiscoveredPackage package) =>
        $"{package.PackageName} {package.Version} from source '{package.SourceAlias}' now contains different files " +
        "than the copy this repo was set up with, although the version number is the same. " +
        "The package may have been republished.";

    /// <summary>
    ///     Applies the policy for a package version whose content changed since the pin was made:
    ///     refuses it, asks the user to confirm, or lets it through when the user already did.
    /// </summary>
    /// <param name="pin">The repo's current pin (carries the identity that was recorded).</param>
    /// <param name="package">The package that was acquired.</param>
    /// <param name="contentIdentity">The identity of the acquired package's files.</param>
    /// <param name="contentChange">The policy requested by the caller.</param>
    /// <param name="failureVerb">The action name used in the refusal message.</param>
    /// <returns><see langword="true"/> when the package may be applied; <see langword="false"/>
    ///     when it was refused or the user was asked (nothing in the repo has been changed).</returns>
    /// <remarks>
    ///     The refusal is both logged and reported to the user, because a republished version can
    ///     be an attack on the supply chain as well as an honest mistake and the user needs to
    ///     know either way. Asking needs somebody to ask: without a subscriber to
    ///     <see cref="ContentChangeConfirmationRequested"/> the change is refused instead.
    /// </remarks>
    private bool ResolveContentChange(
        RepoPin? pin,
        DiscoveredPackage package,
        string? contentIdentity,
        ContentChangeHandling contentChange,
        string failureVerb)
    {
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "Package '{PackageName} {Version}' from source '{SourceAlias}' changed content for repo '{RepoName}' ({Pinned} -> {Actual}); handling: {Handling}",
                package.PackageName, package.Version, package.SourceAlias, RepoName,
                PackageContentIdentity.Describe(pin?.ContentIdentity),
                PackageContentIdentity.Describe(contentIdentity),
                contentChange);
        }

        if (contentChange == ContentChangeHandling.Accept)
        {
            return true;
        }

        var handler = ContentChangeConfirmationRequested;
        if (contentChange == ContentChangeHandling.Ask && handler is not null)
        {
            StatusMessage = "Waiting for your confirmation of the changed package.";
            var selection = new PackageReference(package.SourceAlias, package.PackageName, package.Version.ToString());
            handler(
                this,
                new ContentChangeConfirmationRequest(
                    selection,
                    $"{DescribeContentChange(package)}\n\n" +
                    $"Replace the agent files in '{RepoName}' with the changed package?\n\n" +
                    "Choose Yes only if you trust the change."));
            return false;
        }

        ErrorOccurred?.Invoke(
            this,
            $"{failureVerb} stopped. {DescribeContentChange(package)} Nothing was changed. {ContentChangeAdvice}");
        return false;
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
    /// <param name="fromLaunchResync"><see langword="true"/> when the offer follows the re-sync
    ///     that runs before a launch, which never writes the pin: a decline of such an offer is
    ///     then not persisted (see <see cref="DeclineAgentsMdTemplateAsync"/>).</param>
    /// <returns>A task that completes once the offer was raised or found not to apply.</returns>
    /// <remarks>
    ///     Called only from call sites that already have a <paramref name="reader"/> in hand
    ///     immediately after a successful <see cref="PackageContentExtractor.Extract"/> call
    ///     (<see cref="ApplyPackageAndShowReleaseNotesAsync"/> and the re-extraction branch of
    ///     <see cref="EnsureAgentFilesSyncedBeforeLaunchAsync"/>) - never as an independent, standing
    ///     per-launch check, so the offer only ever fires when a sync genuinely just happened.
    ///     The three checks read the disk and the package, so they run on a worker; the offer is
    ///     raised back on the UI thread. A pin-read or package-read failure is reported and never
    ///     disrupts the already-successful sync, as a <c>.gitignore</c> failure does not.
    /// </remarks>
    private async Task MaybeOfferAgentsMdTemplateAsync(IPackageContentReader reader, bool fromLaunchResync = false)
    {
        string? template;
        try
        {
            var repoPath = RepoPath;
            template = await _backgroundWork.RunAsync(
                () => FindAgentsMdTemplateToOffer(repoPath, reader), CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
            return;
        }

        if (template is null)
        {
            return;
        }

        // Remembered so that a decline of this offer does not write the pin (the launch re-sync
        // never does); set just before the handlers run, replaced by every later offer
        _agentsMdOfferFromLaunchResync = fromLaunchResync;
        AgentsMdTemplateOfferRequested?.Invoke(this, template);
    }

    /// <summary>
    ///     Finds the <c>AGENTS.md</c> template to offer: none when the repo has an <c>AGENTS.md</c>,
    ///     the pin records that the offer was declined, or the package has no template. Reads the
    ///     disk and the package, so it runs on a worker; touches no view-model state.
    /// </summary>
    /// <param name="repoPath">The repo's root folder.</param>
    /// <param name="reader">The package that was just applied.</param>
    /// <returns>The template's text, or <see langword="null"/> when nothing is to be offered.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the pin or the package cannot be read.</exception>
    private static string? FindAgentsMdTemplateToOffer(string repoPath, IPackageContentReader reader)
    {
        if (File.Exists(PathHelpers.SafePathCombine(repoPath, AgentsMdFileName)))
        {
            return null;
        }

        var pin = RepoPinStore.Load(repoPath);
        if (pin is { AgentsMdTemplateDeclined: true })
        {
            return null;
        }

        return PackageContentExtractor.ReadAgentsMdTemplate(reader);
    }

    /// <summary>
    ///     Writes the package's offered <c>AGENTS.md</c> template to this repo's root, in
    ///     response to the user accepting the <see cref="AgentsMdTemplateOfferRequested"/> prompt.
    /// </summary>
    /// <param name="templateContent">The template's text content, as previously carried by
    ///     <see cref="AgentsMdTemplateOfferRequested"/>.</param>
    /// <returns>A task that completes once the file was written, found to exist already, or the
    ///     failure was reported.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="templateContent"/> is
    ///     <see langword="null"/>.</exception>
    /// <remarks>
    ///     <para>
    ///     Writes directly to the repo root - never into any of the four managed folders - since
    ///     a root-level <c>AGENTS.md</c> is a starting point the user is expected to customize
    ///     themselves, not a file blind-deleted/replaced on every subsequent sync. The write runs on
    ///     a worker; the outcome is reported back on the UI thread.
    ///     </para>
    ///     <para>
    ///     Race-safe against the gap between <see cref="MaybeOfferAgentsMdTemplateAsync"/>'s
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
    public async Task AcceptAgentsMdTemplateAsync(string templateContent)
    {
        ArgumentNullException.ThrowIfNull(templateContent);

        var repoPath = RepoPath;
        var (outcome, failure) = await _backgroundWork.RunAsync(
            () => WriteAgentsMdTemplate(repoPath, templateContent), CancellationToken.None);
        switch (outcome)
        {
            case AgentsMdWriteOutcome.Written:
                StatusMessage = "Added a starting AGENTS.md template - please review and customize it for this repo.";
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Wrote a starting AGENTS.md template to repo '{RepoName}'", RepoName);
                }

                break;

            case AgentsMdWriteOutcome.AlreadyExists:
                // Another user/process created AGENTS.md after the offer was raised but before the
                // write ran - it was never overwritten; this is a non-destructive, informational
                // outcome, not a failure.
                StatusMessage = "AGENTS.md already exists for this repo - nothing was written.";
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        failure,
                        "AGENTS.md was created concurrently for repo '{RepoName}'; the offered template was not written",
                        RepoName);
                }

                break;

            default:
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(
                        failure,
                        outcome == AgentsMdWriteOutcome.CreateFailed
                            ? "Failed to create AGENTS.md template for repo '{RepoName}'"
                            : "Failed to write AGENTS.md template for repo '{RepoName}'",
                        RepoName);
                }

                ErrorOccurred?.Invoke(this, $"Failed to write AGENTS.md: {failure?.Message}");
                break;
        }
    }

    /// <summary>
    ///     Creates the repo's <c>AGENTS.md</c> with the template's text, never overwriting one that
    ///     exists. Writes the disk, so it runs on a worker; touches no view-model state.
    /// </summary>
    /// <param name="repoPath">The repo's root folder.</param>
    /// <param name="templateContent">The template's text.</param>
    /// <returns>The outcome, with the exception behind any outcome other than
    ///     <see cref="AgentsMdWriteOutcome.Written"/>.</returns>
    private static (AgentsMdWriteOutcome Outcome, Exception? Failure) WriteAgentsMdTemplate(
        string repoPath, string templateContent)
    {
        // Opening with FileMode.CreateNew is the atomic, race-safe check for "does AGENTS.md
        // already exist" - but only *this* step can mean "another process created it concurrently
        // between the offer being raised and now". It is deliberately isolated from the write
        // below: if CreateNew succeeds and a *later* write/flush step then fails (e.g. disk full),
        // AGENTS.md now exists only because this call just created it, and that failure must be
        // reported as a genuine error - not misreported as the benign "already exists" outcome,
        // which would otherwise happen if both steps shared one File.Exists-guarded catch.
        var agentsMdPath = PathHelpers.SafePathCombine(repoPath, AgentsMdFileName);
        FileStream stream;
        try
        {
            stream = new FileStream(agentsMdPath, FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException ex) when (File.Exists(agentsMdPath))
        {
            return (AgentsMdWriteOutcome.AlreadyExists, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (AgentsMdWriteOutcome.CreateFailed, ex);
        }

        try
        {
            using (stream)
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(templateContent);
            }

            return (AgentsMdWriteOutcome.Written, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (AgentsMdWriteOutcome.WriteFailed, ex);
        }
    }

    /// <summary>
    ///     What writing the offered <c>AGENTS.md</c> template did.
    /// </summary>
    private enum AgentsMdWriteOutcome
    {
        /// <summary>The template was written.</summary>
        Written,

        /// <summary>An <c>AGENTS.md</c> appeared after the offer; nothing was written.</summary>
        AlreadyExists,

        /// <summary>The file could not be created.</summary>
        CreateFailed,

        /// <summary>The file was created but the text could not be written.</summary>
        WriteFailed
    }

    /// <summary>
    ///     Persists that the user declined the <c>AGENTS.md</c> template offer for this repo, so
    ///     <see cref="MaybeOfferAgentsMdTemplateAsync"/> never raises
    ///     <see cref="AgentsMdTemplateOfferRequested"/> again for it.
    /// </summary>
    /// <returns>A task that completes once the decline was recorded (or not needed), or the
    ///     failure was reported.</returns>
    /// <remarks>
    ///     Loads the existing pin (if any) rather than constructing a fresh one, so the repo's
    ///     already-persisted package name/version are preserved alongside the new decline flag.
    ///     The pin is read and written on a worker. There is deliberately no corresponding "reset"
    ///     method - the decision is sticky for this repo, per the feature's explicit scope.
    /// </remarks>
    public async Task DeclineAgentsMdTemplateAsync()
    {
        // The launch re-sync never writes the pin (a write would dirty the working tree on a
        // launch), so a decline of the offer that re-sync raised is not remembered; the offer
        // can only return when a later re-sync happens, which needs the managed folders gone
        if (_agentsMdOfferFromLaunchResync)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "AGENTS.md template offered during a launch re-sync was declined for repo '{RepoName}'; the pin was not written",
                    RepoName);
            }

            return;
        }

        var repoPath = RepoPath;
        var packageName = PinnedPackageName ?? string.Empty;
        var version = PinnedPackageVersion ?? string.Empty;
        try
        {
            await _backgroundWork.RunAsync(
                () =>
                {
                    var pin = RepoPinStore.Load(repoPath) ?? new RepoPin { PackageName = packageName, Version = version };
                    pin.AgentsMdTemplateDeclined = true;
                    RepoPinStore.Save(repoPath, pin);
                    return true;
                },
                CancellationToken.None);

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
    ///     Applies a pin file's content (or the failure to read it) to <see cref="PinnedPackageName"/>
    ///     and <see cref="PinnedPackageVersion"/> (and the recorded content identity and the legacy
    ///     source hint the card keeps).
    /// </summary>
    /// <param name="pin">The pin read from the repo, or <see langword="null"/> when there is none or it
    ///     could not be read.</param>
    /// <param name="loadError">Why the pin file could not be read, or <see langword="null"/>.</param>
    private void ApplyPin(RepoPin? pin, string? loadError)
    {
        var previous = (PinnedPackageName, PinnedPackageVersion, _pinnedContentIdentity);
        if (loadError is null)
        {
            PinnedPackageName = pin?.PackageName;
            PinnedPackageVersion = pin?.Version;
            _pinnedContentIdentity = pin?.ContentIdentity;
            _pinLegacySource = pin?.LegacySource;
        }
        else
        {
            // The pin file exists but could not be read/parsed (or was written by a newer
            // AgentControl) - clear any previously cached pin fields rather than leaving stale
            // data in place, so neither the UI nor the upgrade-gating logic in
            // RefreshUpgradeStatus() continues to treat this repo as pinned to a package that
            // can no longer be confirmed from disk.
            PinnedPackageName = null;
            PinnedPackageVersion = null;
            _pinnedContentIdentity = null;
            _pinLegacySource = null;
            StatusMessage = $"Failed to read pin file: {loadError}";
        }

        // A different pin makes everything learned about upgrades for the old one meaningless: drop
        // it and ignore any check still running for it (the caller starts a new one). The source
        // that supplied the old package is forgotten only when the package or version changed (the
        // card's remembered supplier survives an app restart, when only the identity is read for
        // the first time).
        if ((PinnedPackageName, PinnedPackageVersion, _pinnedContentIdentity) != previous)
        {
            InvalidateUpgradeCheck();
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            if (previous.PinnedPackageName != PinnedPackageName || previous.PinnedPackageVersion != PinnedPackageVersion)
            {
                SupplyingSource = null;
            }

            RaisePackageChipChanged();
        }
    }

    /// <summary>
    ///     Makes any upgrade check that is still in flight irrelevant: its result will not be
    ///     published, and the card stops showing "checking" and "check failed" for it.
    /// </summary>
    private void InvalidateUpgradeCheck()
    {
        _upgradeCheckGeneration++;
        IsCheckingUpgrade = false;
        IsUpgradeStatusUnknown = false;
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
        // A direct check is newer than any background check still in flight
        _appliedGitStatusGeneration = ++_gitStatusGeneration;
        var settings = _getSettings();
        ApplyGitStatus(ReadWorkingTreeClean(RepoPath, CreateGitClient(settings), CancellationToken.None));
    }

    /// <summary>
    ///     Applies git's answers about the branch and the committed agent folders to
    ///     <see cref="CurrentBranch"/> and <see cref="HasCommittedAgentFiles"/>, remembering the
    ///     committed-files answer in the per-card cache under its <c>HEAD</c> commit.
    /// </summary>
    /// <param name="branch">The checked-out branch.</param>
    /// <param name="headHash">The <c>HEAD</c> commit the answer belongs to.</param>
    /// <param name="hasCommittedAgentFiles">Whether managed agent folders are committed.</param>
    /// <param name="gitQueryFailed">Whether git could not answer; the card then shows no branch and
    ///     no committed-files badge.</param>
    private void ApplyBranchFacts(string? branch, string? headHash, bool hasCommittedAgentFiles, bool gitQueryFailed)
    {
        if (gitQueryFailed)
        {
            CurrentBranch = null;
            HasCommittedAgentFiles = false;
            return;
        }

        CurrentBranch = branch;
        if (headHash is not null)
        {
            _committedAgentFilesCache.Set(RepoPath, headHash, hasCommittedAgentFiles);
        }

        HasCommittedAgentFiles = hasCommittedAgentFiles;
    }

    /// <summary>
    ///     Re-checks, in the background, whether a configured source supplies the pinned package
    ///     (and a newer version than the pinned one is available), via the shared
    ///     <see cref="PackageVersionCache"/>, updating <see cref="IsUpgradeAvailable"/>,
    ///     <see cref="LatestAvailableVersion"/>, <see cref="SupplyingSource"/> and
    ///     <see cref="SourceProblem"/>. Any failure
    ///     (source unreachable or failing) is treated as "no upgrade available" rather than
    ///     propagated.
    /// </summary>
    /// <remarks>
    ///     The pin names no source, so the user's configured sources are asked, in Settings order,
    ///     which of them offer the pinned package and version. For a pin that recorded a files
    ///     identity, the source this card was last supplied from is accepted as it is, and otherwise
    ///     the first source whose files have exactly that identity supplies it; a pin without one
    ///     takes the source it was last supplied from, else the one an older version recorded, else
    ///     the first that offers it. The newest version across all the sources that offer the
    ///     package (a tie keeps Settings order) decides whether an upgrade is offered. A pin that no
    ///     source can supply is an error state: <see cref="SourceProblem"/> explains it and the card
    ///     is never "up to date", yet a newer version a source offers is still offered as an upgrade
    ///     (the way out of the problem). The sources are read
    ///     asynchronously, so a slow or unreachable source never freezes the window;
    ///     <see cref="IsCheckingUpgrade"/> is set meanwhile. When several checks overlap (for
    ///     example after the sources were edited while an earlier check was still waiting) only the
    ///     most recently started one updates the card.
    /// </remarks>
    /// <param name="cancellationToken">Cancels reading the source.</param>
    /// <param name="allowPrompt">Whether a server that needs a sign-in may open the sign-in
    ///     dialog. <see langword="false"/> (the default) for the background check at start-up and
    ///     after settings change, which must never interrupt the user with a dialog: it uses a
    ///     remembered credential if there is one and otherwise reports that a sign-in is needed.
    ///     <see langword="true"/> when the user asked for the check; when the user then cancels or
    ///     closes that dialog, or the server rejects the sign-in given, the card reports the same
    ///     sign-in-needed problem, in the same words, as the background check.</param>
    /// <param name="relistSources">Whether every configured source is read again instead of
    ///     answered from the cache. <see langword="true"/> when the user asked for the check (so a
    ///     package published since the last listing - a new NuGet version, a zip added to a folder,
    ///     a pushed tag - is offered at once); <see langword="false"/> (the default) for the
    ///     background check, which may reuse cached listings no older than five minutes.</param>
    /// <param name="failureMemo">The failures of the background check round this check belongs to,
    ///     or <see langword="null"/> (the default; a check the user asked for, or one on its own),
    ///     in which case the check keeps a memo of its own for its length, so a source that failed -
    ///     a sign-in the user canceled included - is read, and the user asked, at most once per
    ///     check. A source that already failed in the round is not read again - this card ends in
    ///     the same state the card that hit the failure did - and a failure here is recorded for
    ///     the cards that follow. Nothing in it outlives the round.</param>
    /// <returns>A task that completes when the card's upgrade state is current.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled; the card keeps its previous upgrade state.</exception>
    public async Task RefreshUpgradeStatusAsync(
        CancellationToken cancellationToken = default,
        bool allowPrompt = false,
        bool relistSources = false,
        SourceFailureMemo? failureMemo = null)
    {
        // A card that was removed from the list checks nothing any more
        if (_isRetired)
        {
            return;
        }

        var generation = ++_upgradeCheckGeneration;

        // A repo whose folder is gone offers no upgrade: its package chip is hidden, so an upgrade
        // chip on its own would be meaningless (and the upgrade command is disabled anyway)
        if (IsMissing)
        {
            IsUpgradeAvailable = false;
            IsUpgradeStatusUnknown = false;
            LatestAvailableVersion = null;
            IsCheckingUpgrade = false;
            return;
        }

        if (!EvaluatePinnedSource())
        {
            IsUpgradeStatusUnknown = false;
            IsCheckingUpgrade = false;
            return;
        }

        var pinnedPackageName = PinnedPackageName!;
        var pinnedVersion = PinnedPackageVersion ?? string.Empty;

        // Within one check a source that could not be read - a sign-in the user canceled or the
        // server rejected included - is read once: a check given no round's memo (one the user
        // started) keeps one of its own for its length, so the user is never asked twice
        var failures = failureMemo ?? new SourceFailureMemo();
        IsCheckingUpgrade = true;
        try
        {
            var registry = CreateSourceRegistry(allowPrompt);

            // A package published since the last listing is only visible in a fresh listing: when
            // the user asked, drop what the cache learned about every configured source so the
            // check below reads them again (NuGet servers, zip folders and Git tags alike). The
            // cache discards the entries of just those sources, and a read that was already running
            // for one cannot put its older answer back.
            if (relistSources)
            {
                foreach (var definition in registry.Definitions)
                {
                    _packageVersionCache.Invalidate(definition.Alias);
                }
            }

            // The pin names no source: the user's configured sources, in Settings order, are asked
            // what they offer of the pinned package and version
            var (offering, firstFailure, listingAccessDenied) = await FindOfferingSourcesAsync(
                registry, pinnedPackageName, pinnedVersion, cancellationToken, failures);

            // Nothing offers it and a source could not be read: nothing is known (a sign-in that is
            // needed, a share that is down), which is not "no source offers it"
            if (offering.Count == 0 && firstFailure is not null)
            {
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
            }

            string? supplier = null;
            string? problem = null;
            if (offering.Count == 0)
            {
                // (A pin whose version does not parse as a version is left to the normal result:
                // "not listed" says nothing about it.)
                if (PackageVersion.TryParse(pinnedVersion, out _))
                {
                    problem = DescribeNoSourceOffers(pinnedPackageName, pinnedVersion);
                }
            }
            else if (PackageContentIdentity.IsFilesIdentity(_pinnedContentIdentity))
            {
                // The source this card was last supplied from is accepted as it is (the files are
                // checked whenever they are applied); otherwise the sources are asked for their
                // files, in Settings order, and the first with exactly the recorded identity wins
                supplier = offering.FirstOrDefault(
                    offer => string.Equals(offer.SourceAlias, SupplyingSource, StringComparison.OrdinalIgnoreCase))?.SourceAlias;
                if (supplier is null)
                {
                    using var verified = await FindVerifiedSourceAsync(
                        offering, _pinnedContentIdentity, SupplyingSource, registry, listingAccessDenied, cancellationToken);
                    supplier = verified?.Package.SourceAlias;
                    if (supplier is null)
                    {
                        problem = DescribeNoMatchingFiles(pinnedPackageName, pinnedVersion);
                    }
                }
            }
            else
            {
                // Nothing to verify against (an older pin): the source it was last supplied from,
                // else the one an older version recorded in the file, else the first that offers it
                supplier = ChooseUnverifiedOffer(offering).SourceAlias;
            }

            // The newest version across all the configured sources that offer the package (a tie
            // keeps Settings order). With a problem it is still offered when newer: upgrading is the
            // natural way out of it.
            var check = await _packageVersionCache.IsNewerVersionAvailableAcrossSourcesAsync(
                registry, pinnedPackageName, pinnedVersion, cancellationToken, failures);

            if (generation == _upgradeCheckGeneration)
            {
                SupplyingSource = supplier;
                if (problem is not null)
                {
                    SetSourceProblem(
                        check.IsNewerAvailable
                            ? $"{problem} Upgrade to {check.Latest?.Version} to move to a version a configured source offers."
                            : problem,
                        needsSignIn: false);
                }

                LatestAvailableVersion = check.Latest?.Version.ToString();
                IsUpgradeAvailable = check.IsNewerAvailable;
                IsUpgradeStatusUnknown = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // An unreachable or failing source (or invalid source configuration) means "no
            // upgrade can be offered", not a crash; the specific failure is surfaced by the
            // actions that need the source. It is also not "up to date": the card says the
            // check failed (a neutral status) rather than claiming currency nothing established.
            if (generation == _upgradeCheckGeneration)
            {
                IsUpgradeAvailable = false;
                IsUpgradeStatusUnknown = true;
                LatestAvailableVersion = null;

                // A server that needs a sign-in is worth telling the user about: the background
                // check must not ask, and a check the user started whose sign-in dialog was
                // canceled or closed, or whose sign-in the server rejected, still has none. Either
                // way the source still needs a sign-in, so the card shows the same problem in the
                // same words and says what to do - click its "Sign-in needed" chip or choose
                // Refresh, both of which run the check that may ask
                if (ex is PackageSourceAccessException
                    {
                        Kind: PackageSourceFailureKind.AuthenticationFailed or PackageSourceFailureKind.SignInCanceled
                    } signIn)
                {
                    SetSourceProblem(DescribeSignInNeeded(signIn), needsSignIn: true);
                }
                else if (ex is PackageSourceAccessException { Kind: PackageSourceFailureKind.AccessDenied })
                {
                    // Signed in, but the account may not read the feed: no sign-in can fix it, so the card
                    // says what the server said and does not offer one
                    SetSourceProblem(SecretRedactor.SafeMessage(ex), needsSignIn: false);
                }
                else if (ex is GitRemoteException { Kind: GitFailureKind.GitTooOld })
                {
                    // Nothing can be checked until Git is updated: say so on the card
                    SetSourceProblem(SecretRedactor.SafeMessage(ex), needsSignIn: false);
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
    ///     has no upgrade, and a pin that can never be supplied because no source is configured
    ///     (or the source configuration is invalid) is an error state.
    /// </summary>
    /// <returns><see langword="true"/> when the package sources must be consulted to learn which
    ///     one supplies the pinned package and whether a newer version exists;
    ///     <see langword="false"/> when the state was fully decided here (no pin, no source at all,
    ///     or invalid source configuration).</returns>
    private bool EvaluatePinnedSource()
    {
        SetSourceProblem(null, needsSignIn: false);
        if (string.IsNullOrWhiteSpace(PinnedPackageName))
        {
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            SupplyingSource = null;
            return false;
        }

        try
        {
            var registry = CreateSourceRegistry();

            // A remembered supplier that is no longer configured supplies nothing
            if (SupplyingSource is not null && !registry.Contains(SupplyingSource))
            {
                SupplyingSource = null;
            }

            if (registry.Definitions.Count == 0)
            {
                SupplyingSource = null;
                SetSourceProblem(DescribeNoSourceOffers(PinnedPackageName, PinnedPackageVersion), needsSignIn: false);
                IsUpgradeAvailable = false;
                LatestAvailableVersion = null;
                return false;
            }
        }
        catch (InvalidOperationException ex)
        {
            // Invalid source configuration (for example a duplicated alias): no upgrade can be
            // offered and the card must not claim it is up to date, so it is a problem the card
            // shows; the actions that need the source report the specific reason again
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            SetSourceProblem(
                $"The package sources are not configured correctly: {SecretRedactor.SafeMessage(ex)}",
                needsSignIn: false);
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Describes a source that still needs a sign-in, for the card's "Sign-in needed" chip.
    /// </summary>
    /// <param name="failure">The failure of the read: the server needs a sign-in, rejected the
    ///     one given, or the user canceled the sign-in.</param>
    /// <returns>The source's sign-in-needed message - the same words whether or not the user was
    ///     asked - followed by what to do.</returns>
    private static string DescribeSignInNeeded(PackageSourceAccessException failure) =>
        $"{SecretRedactor.Redact(failure.SignInNeededMessage ?? failure.Message)} " +
        "Click \"Sign-in needed\" (or use the card's \"...\" menu > Refresh) to sign in.";

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
            OnPropertyChanged(nameof(ShowSourceProblemChip));
            OnPropertyChanged(nameof(SourceProblemChipText));
        }
    }

    /// <summary>
    ///     Runs a package operation for this card while holding <see cref="IsBusy"/>, so a second
    ///     operation cannot start until it finishes, and makes it cancelable through
    ///     <see cref="CancelOperationCommand"/> when <paramref name="cancelable"/> is set.
    /// </summary>
    /// <remarks>
    ///     An operation that acts on the card's repo state (Launch, Pull, Upgrade, Select Package)
    ///     passes <paramref name="checkCurrentRepoState"/>: the card first shows its busy row, then
    ///     waits - without blocking the window - for its repo-state reads that are still queued or
    ///     running, moved to the front of the shared queue (see <see cref="EnsureCurrentRepoStateAsync"/>),
    ///     and decides again on what they found: a folder that turned out to be missing, or the check's
    ///     own refusal, is told on the card and nothing runs. So no action uses state that is unknown or
    ///     about to change. The background upgrade checks skip a busy card, so they do not run beside
    ///     the action meanwhile.
    /// </remarks>
    /// <param name="busyMessage">The text shown while the operation runs.</param>
    /// <param name="cancelable">Whether the user may cancel the operation.</param>
    /// <param name="operation">The work to run; receives a token cancelled by either
    ///     <paramref name="cancellationToken"/> or the card's cancel command.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <param name="checkCurrentRepoState">When supplied, the operation needs current repo state: it
    ///     waits for the card's pending reads first, then this is asked whether the operation can still
    ///     run on what was found, returning why not (shown as the card's status message) or
    ///     <see langword="null"/> to go ahead. <see langword="null"/> (the default) runs the operation
    ///     at once.</param>
    /// <returns>A task that completes when the operation has finished; returns immediately
    ///     without running <paramref name="operation"/> when another operation is already
    ///     running. A cancelled operation sets a status message instead of failing.</returns>
    private async Task RunOperationAsync(
        string busyMessage,
        bool cancelable,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        Func<string?>? checkCurrentRepoState = null)
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
        CanCancelOperation = cancelable;
        IsBusy = true;
        try
        {
            // An action never acts on state that is unknown or about to change: the card's pending
            // reads go first, then the action is decided again on what they found
            if (checkCurrentRepoState is not null)
            {
                await EnsureCurrentRepoStateAsync(cancellation.Token);
                var refusal = DescribeStateRefusal(checkCurrentRepoState);
                if (refusal is not null)
                {
                    StatusMessage = refusal;
                    return;
                }
            }

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
    ///     Decides, once the card's repo state is current, whether an action can still run.
    /// </summary>
    /// <param name="checkCurrentRepoState">The action's own check.</param>
    /// <returns>Why the action does not run, or <see langword="null"/> to go ahead.</returns>
    private string? DescribeStateRefusal(Func<string?> checkCurrentRepoState)
    {
        if (_isRetired)
        {
            return "This repo was removed from the list; nothing was done.";
        }

        if (!HasRepoState)
        {
            return "This repo's state could not be read; nothing was done. Use Refresh to try again.";
        }

        if (IsMissing)
        {
            return "This repo's folder could not be found; nothing was done.";
        }

        return checkCurrentRepoState();
    }

    /// <summary>
    ///     The <see cref="RunOperationAsync"/> state check of an action that needs nothing beyond
    ///     current repo state and an existing folder.
    /// </summary>
    /// <returns>Always <see langword="null"/>: go ahead.</returns>
    private static string? NoFurtherStateCheck() => null;

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
    ///     Creates the git client the card runs its git commands through: the configured git
    ///     executable (see <see cref="ResolveGitExecutablePath(AppSettings)"/>) with the card's time
    ///     limits.
    /// </summary>
    /// <param name="settings">The current application settings.</param>
    /// <returns>The client.</returns>
    private GitClient CreateGitClient(AppSettings settings) =>
        new(ResolveGitExecutablePath(settings), timeouts: _gitTimeouts);

    /// <summary>
    ///     Resolves the git executable path to use: the per-user setting if configured,
    ///     otherwise the startup-time test override, otherwise <c>"git"</c> (PATH resolution).
    /// </summary>
    /// <param name="settings">The current application settings.</param>
    /// <returns>The git executable path or command name to invoke.</returns>
    private string ResolveGitExecutablePath(AppSettings settings) => ResolveGitExecutablePath(settings, _startupOptions);

    /// <summary>
    ///     Resolves the git executable path to use: the per-user setting if configured,
    ///     otherwise the startup-time test override, otherwise <c>"git"</c> (PATH resolution).
    /// </summary>
    /// <param name="settings">The application settings.</param>
    /// <param name="startupOptions">The startup overrides, or <see langword="null"/>.</param>
    /// <returns>The git executable path or command name to invoke.</returns>
    internal static string ResolveGitExecutablePath(AppSettings settings, Startup.StartupOptions? startupOptions) =>
        !string.IsNullOrWhiteSpace(settings.GitExecutablePath)
            ? settings.GitExecutablePath
            : startupOptions?.GitExecutableOverride ?? "git";

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
