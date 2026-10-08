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
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Utilities;
using Microsoft.Extensions.Logging;
using AgentLauncher = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for a single card in the main window's recent-repos list: displays the repo's
///     pinned package version and upgrade-availability badge, and exposes Launch/Pull/Upgrade
///     actions.
/// </summary>
/// <remarks>
///     All business logic (upgrade detection, launch orchestration, pull-eligibility checks) is
///     implemented here or delegated to the Phase 1 subsystems (<see cref="GitClient"/>,
///     <see cref="PackageSource"/>, <see cref="PackageZipExtractor"/>,
///     <see cref="GitIgnoreEnsurer"/>, <see cref="RepoPinStore"/>, <see cref="AgentLauncher"/>)
///     rather than in any view
///     code-behind, so this class is fully unit-testable without an Avalonia window. Not
///     thread-safe; every member is expected to be called from the UI thread, consistent with
///     <see cref="ViewModelBase"/>.
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

    private bool _isMissing;
    private bool _isUpgradeAvailable;
    private string? _latestAvailableVersion;
    private string? _currentBranch;
    private bool _hasCommittedAgentFiles;
    private bool _canPull;
    private bool _gitStatusUnavailable;
    private bool _gitStatusChecked;
    private string? _statusMessage;

    /// <summary>
    ///     Logs the actual work this card performs on behalf of the user - fetching/applying
    ///     packages, pulling the repo, and launching the agent tool - so an operator can audit
    ///     what happened (and when) from the log file, not just from transient UI status text.
    /// </summary>
    private readonly ILogger<RepoCardViewModel> _logger = AppLogging.Factory.CreateLogger<RepoCardViewModel>();

    /// <summary>
    ///     Initializes a new <see cref="RepoCardViewModel"/> for a single recent repo.
    /// </summary>
    /// <param name="recentRepo">The shared recent-repos settings entry this card represents.</param>
    /// <param name="getSettings">Supplies the current <see cref="AppSettings"/> on demand.</param>
    /// <param name="packageVersionCache">The shared, session-scoped package-version cache.</param>
    /// <param name="startupOptions">Test-only startup overrides, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recentRepo"/>,
    ///     <paramref name="getSettings"/>, or <paramref name="packageVersionCache"/> is
    ///     <see langword="null"/>.</exception>
    public RepoCardViewModel(
        RecentRepo recentRepo,
        Func<AppSettings> getSettings,
        PackageVersionCache packageVersionCache,
        Startup.StartupOptions? startupOptions = null)
    {
        ArgumentNullException.ThrowIfNull(recentRepo);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(packageVersionCache);

        _recentRepo = recentRepo;
        _getSettings = getSettings;
        _packageVersionCache = packageVersionCache;
        _startupOptions = startupOptions;

        LaunchCommand = new RelayCommand(Launch, () => !IsMissing);
        PullCommand = new RelayCommand(Pull, () => CanPull && !IsMissing);
        UpgradeCommand = new RelayCommand(Upgrade, () => IsUpgradeAvailable && !IsMissing);
        SelectPackageCommand = new RelayCommand(SelectPackage, () => IsPackageSelectionNeeded && !IsMissing);
        RefreshCommand = new RelayCommand(() =>
        {
            RefreshCheap();
            RefreshDirtyStatus();
        }, () => !IsMissing);
        FavoriteToggleCommand = new RelayCommand(() => IsFavorite = !IsFavorite);
        RemoveCommand = new RelayCommand(() => RemoveRequested?.Invoke(this, EventArgs.Empty));
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
                RefreshCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsWorkingTreeDirty));
                OnPropertyChanged(nameof(PullTooltip));
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
            FavoriteChanged?.Invoke(this, EventArgs.Empty);
        }
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
        }
    }

    /// <summary>
    ///     Gets the repo's current git branch name, or <see langword="null"/> if it could not be
    ///     determined (not a git repository, missing repo, etc.).
    /// </summary>
    public string? CurrentBranch
    {
        get => _currentBranch;
        private set => SetField(ref _currentBranch, value);
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
        private set => SetField(ref _latestAvailableVersion, value);
    }

    /// <summary>
    ///     Gets a value indicating whether a newer package version than
    ///     <see cref="PinnedPackageVersion"/> was discovered at the configured package source.
    /// </summary>
    /// <remarks>
    ///     Drives both the upgrade badge's visibility in <c>MainWindow.axaml</c> and the
    ///     "Upgrade" menu item's visibility/enablement.
    /// </remarks>
    public bool IsUpgradeAvailable
    {
        get => _isUpgradeAvailable;
        private set
        {
            if (SetField(ref _isUpgradeAvailable, value))
            {
                UpgradeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the tooltip text shown on the upgrade-available badge.
    /// </summary>
    public string? UpgradeTooltip => IsUpgradeAvailable
        ? $"Version {LatestAvailableVersion} is available (currently pinned to {PinnedPackageVersion})."
        : null;

    /// <summary>
    ///     Gets a value indicating whether this repo has never had an agent package pinned (its
    ///     <c>.agentcontrol.json</c> pin file does not exist), i.e. whether the "Select
    ///     Package..." menu item should be offered instead of "Upgrade".
    /// </summary>
    /// <remarks>
    ///     Mutually exclusive with <see cref="IsUpgradeAvailable"/> being ever <see langword="true"/>
    ///     - <see cref="RefreshUpgradeStatus"/> already short-circuits to <see langword="false"/>
    ///     whenever <see cref="PinnedPackageName"/> is <see langword="null"/>, so a repo is either
    ///     never-pinned (this is <see langword="true"/>) or already-pinned (this is
    ///     <see langword="false"/>), never both.
    /// </remarks>
    public bool IsPackageSelectionNeeded => PinnedPackageName is null;

    /// <summary>
    ///     Gets the shared, session-scoped package-version cache passed to this card at
    ///     construction, exposed only so <c>MainWindow.axaml.cs</c> can construct a
    ///     <see cref="SelectPackageWindowViewModel"/> for this card's "Select Package..." dialog
    ///     without threading the cache through <see cref="MainWindowViewModel"/> a second way.
    /// </summary>
    internal PackageVersionCache PackageVersionCache => _packageVersionCache;

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
        private set => SetField(ref _statusMessage, value);
    }

    /// <summary>
    ///     Gets the command bound to the card's "Launch" button.
    /// </summary>
    public RelayCommand LaunchCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's "Pull" button.
    /// </summary>
    public RelayCommand PullCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's "Upgrade" menu item.
    /// </summary>
    public RelayCommand UpgradeCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's "Select Package..." menu item, offered instead of
    ///     <see cref="UpgradeCommand"/> whenever <see cref="IsPackageSelectionNeeded"/> is
    ///     <see langword="true"/>.
    /// </summary>
    public RelayCommand SelectPackageCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's manual "Refresh" action, re-running the cheap
    ///     checks plus the (otherwise lazy) working-tree dirty check for just this card.
    /// </summary>
    /// <remarks>
    ///     Does not bypass the shared <see cref="PackageVersionCache"/> - per architecture.md's
    ///     caching-strategy decision, the package-version scan is refreshed on app start or via
    ///     the Settings window (whenever the package-source path changes), not per-repo-card.
    /// </remarks>
    public RelayCommand RefreshCommand { get; }

    /// <summary>
    ///     Gets the command bound to the card's favorite/pin toggle button.
    /// </summary>
    public RelayCommand FavoriteToggleCommand { get; }

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
    ///     Raised after a successful <see cref="Upgrade"/> with the new package's release notes
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
    ///     be shown, carrying the resolved package-source directory. Only raised once
    ///     <c>settings.PackageSourcePath</c> has been validated as configured - a blank source
    ///     raises <see cref="ErrorOccurred"/> instead. Does not itself show any dialog - that is
    ///     a view-layer concern handled by <c>MainWindow</c>'s code-behind, exactly like
    ///     <see cref="RemoveRequested"/>/<see cref="ConfirmationWindow"/>.
    /// </summary>
    public event EventHandler<string>? SelectPackageRequested;

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
    ///     Equivalent to calling <see cref="RefreshCheap"/> followed by
    ///     <see cref="RefreshDirtyStatus"/> - kept as a single convenience entry point for
    ///     callers (e.g. adding a brand-new repo) that want an immediate full refresh rather than
    ///     the split eager/lazy sequencing <see cref="MainWindowViewModel"/> uses at app launch.
    /// </remarks>
    public void Refresh()
    {
        RefreshCheap();
        RefreshDirtyStatus();
    }

    /// <summary>
    ///     Runs every cheap per-repo check: the missing-repo check, the pin file read, the
    ///     current branch name, the committed-agent-files badge (via the <c>HEAD</c>-hash cache),
    ///     and upgrade availability (via the shared <see cref="PackageVersionCache"/>).
    /// </summary>
    /// <remarks>
    ///     Deliberately excludes the working-tree dirty/clean check
    ///     (<see cref="RefreshDirtyStatus"/>), which is not cacheable and is instead deferred/lazy
    ///     per architecture.md's repo-fact caching strategy, so this method is safe to call
    ///     eagerly for every recent repo at app launch without slowing down startup. When
    ///     <see cref="IsMissing"/> is <see langword="true"/>, every other check short-circuits to
    ///     its "unavailable" default rather than attempting a git/filesystem operation against a
    ///     path that does not exist.
    /// </remarks>
    public void RefreshCheap()
    {
        var wasMissing = IsMissing;
        IsMissing = !Directory.Exists(RepoPath);
        if (IsMissing)
        {
            CurrentBranch = null;
            HasCommittedAgentFiles = false;
            CanPull = false;
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;

            // The repo just disappeared (or was already missing): any previously-cached Git
            // status is no longer trustworthy, so invalidate it rather than let it linger and
            // potentially be reported (stale) once the repo reappears.
            GitStatusChecked = false;
            GitStatusUnavailable = false;
            return;
        }

        RefreshPin();
        RefreshBranchAndCommittedFiles();
        RefreshUpgradeStatus();

        // The repo just reappeared after being missing: its cached Git status was invalidated
        // above when it disappeared, so force an immediate fresh check here instead of relying
        // on the one-time-per-card lazy trigger in MainWindow.axaml.cs, which won't fire again
        // for an already-realized card.
        if (wasMissing)
        {
            RefreshDirtyStatus();
        }
    }

    /// <summary>
    ///     Re-checks the repo's working-tree cleanliness via <see cref="GitClient"/>, updating
    ///     <see cref="CanPull"/>.
    /// </summary>
    /// <remarks>
    ///     Not called by the constructor or by <see cref="RefreshCheap"/> for an
    ///     already-present repo - per architecture.md's repo-fact caching strategy, working-tree
    ///     dirty/clean state is not cacheable by <c>HEAD</c> hash (it reflects uncommitted local
    ///     edits), so it is computed lazily instead: on demand via <see cref="RefreshCommand"/>,
    ///     or once when a card first becomes visible (see <c>MainWindow.axaml.cs</c>), rather
    ///     than eagerly for every recent repo at app launch. The one exception is
    ///     <see cref="RefreshCheap"/>'s missing-to-present transition, which calls this method
    ///     immediately to replace the Git status it invalidated while the repo was missing,
    ///     since the one-time-per-card lazy trigger in <c>MainWindow.axaml.cs</c> would not fire
    ///     again for an already-realized card.
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
    ///     this first calls <see cref="EnsureAgentFilesSyncedBeforeLaunch"/> purely for its
    ///     best-effort sync side effect (it may extract missing managed folders, or surface an
    ///     informational status message/warning), then always proceeds to resolve the shell/agent
    ///     command and spawn the process regardless of that call's outcome. A user may want to
    ///     launch their agentic CLI tool to help with agent-package migration, or simply because
    ///     an agentic tool is useful even with zero agent files present - either way, sync state
    ///     must never stand in the way of launching.
    /// </remarks>
    private void Launch()
    {
        EnsureAgentFilesSyncedBeforeLaunch();

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
    ///     <see cref="Launch"/> spawns the agent tool. Per architecture.md's
    ///     "ensure-synced-before-launch" decision (as amended), this <b>never</b> blocks the
    ///     launch - it only ever attempts to help keep the managed folders in sync, surfacing a
    ///     non-blocking status message or warning when it cannot.
    /// </summary>
    /// <returns>
    ///     <see langword="true"/> if no sync action was needed or attempted (the repo has
    ///     committed agent files, has no pin, or the managed folders were already present), or a
    ///     missing set was silently re-extracted successfully; <see langword="false"/> if a sync
    ///     attempt was made and failed for an ordinary reason (source unreachable, pinned version
    ///     missing, an I/O failure), in which case <see cref="ErrorOccurred"/> has already been
    ///     raised as a non-blocking warning explaining why and <see cref="Launch"/> proceeds to
    ///     spawn the agent tool regardless of this return value.
    /// </returns>
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
    ///     resolves the <em>currently pinned</em> package at the configured source (never the
    ///     latest - upgrading remains a deliberate, separate user action) and re-extracts it
    ///     directly via <see cref="PackageZipExtractor.Extract"/>, deliberately without going
    ///     through <see cref="ApplyPackageAndShowReleaseNotes"/> - architecture.md's
    ///     ensure-synced-before-launch bullet never mentions showing release notes, unlike its
    ///     Select-Package bullet, so a silent background repair must not pop a release-notes
    ///     window on every launch. If this re-extraction attempt fails, <see cref="ErrorOccurred"/>
    ///     is raised as a non-blocking warning and this returns <see langword="false"/> - but the
    ///     launch still proceeds regardless.
    ///     </para>
    ///     <para>
    ///     Marked <see langword="internal"/> (not <see langword="private"/>) rather than tested
    ///     only through <see cref="Launch"/> itself, mirroring <c>AgentToolLauncher.BuildProcessStartInfo</c>'s
    ///     own precedent for keeping process-spawning code separated from its unit-testable
    ///     decision logic.
    /// </para>
    /// </remarks>
    internal bool EnsureAgentFilesSyncedBeforeLaunch()
    {
        if (HasCommittedAgentFiles)
        {
            StatusMessage = "This repo has committed agent files; skipping agent-package sync.";
            return true;
        }

        if (PinnedPackageName is null)
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
            var settings = _getSettings();
            if (string.IsNullOrWhiteSpace(settings.PackageSourcePath))
            {
                ErrorOccurred?.Invoke(this, "No package source is configured for this repo. Launching without syncing agent files.");
                return false;
            }

            var pinnedPackage = PackageSource.EnumeratePackages(settings.PackageSourcePath, PinnedPackageName)
                .FirstOrDefault(p => p.Version.ToString() == PinnedPackageVersion);
            if (pinnedPackage is null)
            {
                ErrorOccurred?.Invoke(
                    this,
                    $"Pinned package '{PinnedPackageName} {PinnedPackageVersion}' was not found at the configured source. Launching without syncing agent files.");
                return false;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Re-syncing missing managed agent folders for repo '{RepoName}' from pinned package '{PackageName} {Version}'",
                    RepoName, pinnedPackage.PackageName, pinnedPackage.Version);
            }

            PackageZipExtractor.Extract(pinnedPackage.FilePath, RepoPath);
            MaybeOfferAgentsMdTemplate(pinnedPackage.FilePath);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to sync agent files for repo '{RepoName}' before launch", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Failed to sync agent files: {ex.Message} Launching anyway.");
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex, "Package source is unreachable while syncing repo '{RepoName}' before launch", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {ex.Message} Launching anyway.");
            return false;
        }
    }

    /// <summary>
    ///     Runs <c>git pull</c> in this repo, then refreshes the working-tree status.
    /// </summary>
    private void Pull()
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Pulling repo '{RepoName}' ('{RepoPath}')", RepoName, RepoPath);
        }

        try
        {
            var settings = _getSettings();
            var git = new GitClient(ResolveGitExecutablePath(settings));
            var result = git.Pull(RepoPath);
            StatusMessage = result.Succeeded
                ? "Pull succeeded."
                : $"Pull failed: {result.StandardError.Trim()}";

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
    ///     Runs the full upgrade sequence for this repo: finds the latest package at the
    ///     configured source, then applies it via <see cref="ApplyPackageAndShowReleaseNotes"/>.
    /// </summary>
    private void Upgrade()
    {
        var settings = _getSettings();
        if (string.IsNullOrWhiteSpace(settings.PackageSourcePath) || string.IsNullOrWhiteSpace(PinnedPackageName))
        {
            ErrorOccurred?.Invoke(this, "No package source or pinned package name is configured for this repo.");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Checking for an upgrade to package '{PackageName}' for repo '{RepoName}' at source '{PackageSourcePath}'",
                PinnedPackageName, RepoName, settings.PackageSourcePath);
        }

        DiscoveredPackage? latest;
        try
        {
            latest = PackageSource.FindLatest(settings.PackageSourcePath, PinnedPackageName);
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Package source is unreachable while upgrading repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {ex.Message}");
            return;
        }

        if (latest is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "No package named '{PackageName}' was found at the configured source for repo '{RepoName}'",
                    PinnedPackageName, RepoName);
            }

            ErrorOccurred?.Invoke(this, $"No package named '{PinnedPackageName}' was found at the configured source.");
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Upgrading repo '{RepoName}' to package '{PackageName} {Version}'",
                RepoName, latest.PackageName, latest.Version);
        }

        ApplyPackageAndShowReleaseNotes(latest, $"Upgraded to {latest.Version}.", "Upgrade");
    }

    /// <summary>
    ///     Applies a user-selected package name/version to this repo: resolves the exact
    ///     <see cref="DiscoveredPackage"/> at the configured source, then applies it via
    ///     <see cref="ApplyPackageAndShowReleaseNotes"/>.
    /// </summary>
    /// <param name="packageName">The package base name the user selected.</param>
    /// <param name="version">The exact version string the user selected (matched against
    ///     <see cref="DiscoveredPackage.Version"/>'s <see cref="object.ToString"/>).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="packageName"/> or
    ///     <paramref name="version"/> is <see langword="null"/>.</exception>
    public void ApplySelectedPackage(string packageName, string version)
    {
        ArgumentNullException.ThrowIfNull(packageName);
        ArgumentNullException.ThrowIfNull(version);

        var settings = _getSettings();
        if (string.IsNullOrWhiteSpace(settings.PackageSourcePath))
        {
            ErrorOccurred?.Invoke(this, "No package source is configured. Set one in Settings.");
            return;
        }

        DiscoveredPackage? selected;
        try
        {
            selected = PackageSource.EnumeratePackages(settings.PackageSourcePath, packageName)
                .FirstOrDefault(p => p.Version.ToString() == version);
        }
        catch (DirectoryNotFoundException ex)
        {
            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {ex.Message}");
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
                "Selecting package '{PackageName} {Version}' for repo '{RepoName}'",
                packageName, version, RepoName);
        }

        ApplyPackageAndShowReleaseNotes(selected, $"Package selected: {packageName} {version}.", "Select Package");
    }

    /// <summary>
    ///     Shared extract-and-pin sequence reused by both <see cref="Upgrade"/> (after resolving
    ///     the latest version) and <see cref="ApplySelectedPackage"/> (after resolving the exact
    ///     user-selected version): extracts the package (blind-delete-and-replace), proactively
    ///     ensures the repo's <c>.gitignore</c> covers the managed agent folders, rewrites the
    ///     pin file (tolerating a corrupt/unreadable existing pin file when reading its prior
    ///     <see cref="RepoPin.AgentsMdTemplateDeclined"/> value - see remarks), refreshes this
    ///     card's pin/upgrade-status properties, and raises <see cref="ReleaseNotesReady"/> with
    ///     the new package's release notes.
    /// </summary>
    /// <remarks>
    ///     The read of any existing pin file's <see cref="RepoPin.AgentsMdTemplateDeclined"/>
    ///     value is wrapped in its own try/catch, separate from this method's outer catch: a
    ///     corrupt/unreadable existing <c>.agentcontrol.json</c> must not abort the apply after
    ///     extraction has already succeeded, since the pre-feature behavior unconditionally wrote
    ///     a fresh pin regardless of what (if anything) existed before. A read failure here
    ///     defaults to "no persisted decline" and the new pin is still written via
    ///     <see cref="RepoPinStore.Save"/> below; that <c>Save</c> call's own failure handling is
    ///     unchanged - a genuine write failure there is still reported via the outer
    ///     <see cref="ErrorOccurred"/> handler.
    /// </remarks>
    /// <param name="package">The resolved package to apply.</param>
    /// <param name="successMessage">The <see cref="StatusMessage"/> text to set on success.</param>
    /// <param name="failureVerb">A short verb phrase (e.g. <c>"Upgrade"</c>, <c>"Select Package"</c>)
    ///     used to prefix any <see cref="ErrorOccurred"/> message raised on failure.</param>
    private void ApplyPackageAndShowReleaseNotes(DiscoveredPackage package, string successMessage, string failureVerb)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Fetching and applying package '{PackageName} {Version}' from '{PackageFilePath}' to repo '{RepoName}' ('{RepoPath}')",
                package.PackageName, package.Version, package.FilePath, RepoName, RepoPath);
        }

        try
        {
            PackageZipExtractor.Extract(package.FilePath, RepoPath);
            EnsureGitIgnoreCoversManagedFolders();

            // Preserve a prior AGENTS.md template decline across re-pinning: this field is
            // orthogonal to the selected package/version and must never be silently reset.
            // A corrupt/unreadable existing pin file must not abort the apply - the extraction
            // above has already succeeded, so this defaults to "no persisted decline" and lets
            // the Save below still happen, rather than leaving the repo with newly-extracted
            // files but a stale pin.
            bool agentsMdTemplateDeclined;
            try
            {
                agentsMdTemplateDeclined = RepoPinStore.Load(RepoPath)?.AgentsMdTemplateDeclined ?? false;
            }
            catch (InvalidOperationException)
            {
                agentsMdTemplateDeclined = false;
            }

            RepoPinStore.Save(
                RepoPath,
                new RepoPin
                {
                    PackageName = package.PackageName,
                    Version = package.Version.ToString(),
                    AgentsMdTemplateDeclined = agentsMdTemplateDeclined,
                });

            var releaseNotes = PackageZipExtractor.ReadReleaseNotes(package.FilePath) ?? string.Empty;

            RefreshPin();
            RefreshUpgradeStatus();
            StatusMessage = successMessage;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Applied package '{PackageName} {Version}' to repo '{RepoName}'",
                    package.PackageName, package.Version, RepoName);
            }

            ReleaseNotesReady?.Invoke(this, releaseNotes);
            MaybeOfferAgentsMdTemplate(package.FilePath);
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

            ErrorOccurred?.Invoke(this, $"{failureVerb} failed: {ex.Message}");
        }
        catch (DirectoryNotFoundException ex)
        {
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex, "Package source is unreachable while applying a package to repo '{RepoName}'", RepoName);
            }

            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {ex.Message}");
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
    /// <param name="packageFilePath">Path to the package zip that was just successfully applied
    ///     (extracted) to this repo.</param>
    /// <remarks>
    ///     Called only from call sites that already have a <paramref name="packageFilePath"/> in
    ///     hand immediately after a successful <see cref="PackageZipExtractor.Extract"/> call
    ///     (<see cref="ApplyPackageAndShowReleaseNotes"/> and the re-extraction branch of
    ///     <see cref="EnsureAgentFilesSyncedBeforeLaunch"/>) - never as an independent, standing
    ///     per-launch check, so the offer only ever fires when a sync genuinely just happened.
    ///     Wrapped in its own non-blocking try/catch, mirroring
    ///     <see cref="EnsureGitIgnoreCoversManagedFolders"/>'s precedent, so a pin-read or
    ///     zip-read failure here can never disrupt an already-successful sync.
    /// </remarks>
    private void MaybeOfferAgentsMdTemplate(string packageFilePath)
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

            var template = PackageZipExtractor.ReadAgentsMdTemplate(packageFilePath);
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
    ///     failure, mirroring how <see cref="EnsureAgentFilesSyncedBeforeLaunch"/> distinguishes
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
            var pin = RepoPinStore.Load(RepoPath) ?? new RepoPin { PackageName = PinnedPackageName ?? string.Empty, Version = PinnedPackageVersion ?? string.Empty };
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
    ///     wrapped in its own try/catch, separate from <see cref="ApplyPackageAndShowReleaseNotes"/>'s
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
    ///     Validates a package source is configured, then raises <see cref="SelectPackageRequested"/>
    ///     so the view layer can show the "Select Package..." dialog.
    /// </summary>
    private void SelectPackage()
    {
        var settings = _getSettings();
        if (string.IsNullOrWhiteSpace(settings.PackageSourcePath))
        {
            ErrorOccurred?.Invoke(this, "No package source is configured. Set one in Settings.");
            return;
        }

        SelectPackageRequested?.Invoke(this, settings.PackageSourcePath);
    }

    /// <summary>
    ///     Re-reads the repo's <c>.agentcontrol.json</c> pin file, updating
    ///     <see cref="PinnedPackageName"/> and <see cref="PinnedPackageVersion"/>.
    /// </summary>
    private void RefreshPin()
    {
        try
        {
            var pin = RepoPinStore.Load(RepoPath);
            PinnedPackageName = pin?.PackageName;
            PinnedPackageVersion = pin?.Version;
        }
        catch (InvalidOperationException ex)
        {
            // The pin file exists but could not be read/parsed - clear any previously cached
            // pin fields rather than leaving stale data in place, so neither the UI nor the
            // upgrade-gating logic in RefreshUpgradeStatus() continues to treat this repo as
            // pinned to a package that can no longer be confirmed from disk.
            PinnedPackageName = null;
            PinnedPackageVersion = null;
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
    ///     Re-checks whether a newer package version is available at the configured source (via
    ///     the shared <see cref="PackageVersionCache"/>), updating <see cref="IsUpgradeAvailable"/>
    ///     and <see cref="LatestAvailableVersion"/>. Any failure (no source configured, source
    ///     unreachable) is treated as "no upgrade available" rather than propagated.
    /// </summary>
    private void RefreshUpgradeStatus()
    {
        var settings = _getSettings();
        if (string.IsNullOrWhiteSpace(settings.PackageSourcePath) || string.IsNullOrWhiteSpace(PinnedPackageName))
        {
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            return;
        }

        try
        {
            var isNewer = _packageVersionCache.IsNewerVersionAvailable(
                settings.PackageSourcePath, PinnedPackageName, PinnedPackageVersion ?? string.Empty, out var latest);
            LatestAvailableVersion = latest?.Version.ToString();
            IsUpgradeAvailable = isNewer;
        }
        catch (DirectoryNotFoundException)
        {
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
        }
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
