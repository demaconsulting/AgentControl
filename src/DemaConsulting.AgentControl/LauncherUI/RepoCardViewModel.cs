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
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
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
///     <see cref="RepoPinStore"/>, <see cref="AgentLauncher"/>) rather than in any view
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
    private string? _statusMessage;

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
            }
        }
    }

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
        IsMissing = !Directory.Exists(RepoPath);
        if (IsMissing)
        {
            CurrentBranch = null;
            HasCommittedAgentFiles = false;
            CanPull = false;
            IsUpgradeAvailable = false;
            LatestAvailableVersion = null;
            return;
        }

        RefreshPin();
        RefreshBranchAndCommittedFiles();
        RefreshUpgradeStatus();
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
    ///     decision, this first calls <see cref="EnsureAgentFilesSyncedBeforeLaunch"/> and aborts
    ///     the launch entirely (without resolving the shell/agent command or spawning any
    ///     process) if that check reports the launch should not proceed.
    /// </remarks>
    private void Launch()
    {
        if (!EnsureAgentFilesSyncedBeforeLaunch())
        {
            return;
        }

        try
        {
            var settings = _getSettings();
            var command = ResolveAgentCommand(settings);
            if (string.IsNullOrWhiteSpace(command))
            {
                ErrorOccurred?.Invoke(this, "No agentic CLI tool command is configured. Set one in Settings.");
                return;
            }

            var shell = new AgentControl.AgentToolLauncher.ShellDetector().Detect();
            var startInfo = AgentLauncher.BuildProcessStartInfo(shell, command, RepoPath);
            AgentLauncher.Launch(startInfo);
            StatusMessage = "Launched.";
            RecordLaunched();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ErrorOccurred?.Invoke(this, $"Failed to launch the agent tool: {ex.Message}");
        }
    }

    /// <summary>
    ///     Ensures this repo's four managed agent folders match the currently pinned package
    ///     version before <see cref="Launch"/> is allowed to proceed, per architecture.md's
    ///     "ensure-synced-before-launch" decision.
    /// </summary>
    /// <returns>
    ///     <see langword="true"/> if the launch may proceed (either the managed folders were
    ///     already present, or a missing set was silently re-extracted successfully);
    ///     <see langword="false"/> if the launch must be aborted, in which case
    ///     <see cref="ErrorOccurred"/> has already been raised with a message explaining why.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///     If no pin exists at all (<see cref="PinnedPackageName"/> is <see langword="null"/>),
    ///     this raises <see cref="ErrorOccurred"/> directing the user to "Select Package..." first
    ///     and returns <see langword="false"/> - launching the agent tool with no agent files
    ///     present at all would be worse than blocking with a clear message.
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
    ///     window on every launch.
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
        if (PinnedPackageName is null)
        {
            ErrorOccurred?.Invoke(this, "This repo has no agent package selected yet. Use \"Select Package...\" first.");
            return false;
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
                ErrorOccurred?.Invoke(this, "No package source is configured for this repo.");
                return false;
            }

            var pinnedPackage = PackageSource.EnumeratePackages(settings.PackageSourcePath, PinnedPackageName)
                .FirstOrDefault(p => p.Version.ToString() == PinnedPackageVersion);
            if (pinnedPackage is null)
            {
                ErrorOccurred?.Invoke(
                    this,
                    $"Pinned package '{PinnedPackageName} {PinnedPackageVersion}' was not found at the configured source.");
                return false;
            }

            PackageZipExtractor.Extract(pinnedPackage.FilePath, RepoPath);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to sync agent files: {ex.Message}");
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Runs <c>git pull</c> in this repo, then refreshes the working-tree status.
    /// </summary>
    private void Pull()
    {
        try
        {
            var settings = _getSettings();
            var git = new GitClient(ResolveGitExecutablePath(settings));
            var result = git.Pull(RepoPath);
            StatusMessage = result.Succeeded
                ? "Pull succeeded."
                : $"Pull failed: {result.StandardError.Trim()}";
            RefreshGitStatus();
        }
        catch (InvalidOperationException ex)
        {
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

        DiscoveredPackage? latest;
        try
        {
            latest = PackageSource.FindLatest(settings.PackageSourcePath, PinnedPackageName);
        }
        catch (DirectoryNotFoundException ex)
        {
            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {ex.Message}");
            return;
        }

        if (latest is null)
        {
            ErrorOccurred?.Invoke(this, $"No package named '{PinnedPackageName}' was found at the configured source.");
            return;
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
            ErrorOccurred?.Invoke(
                this, $"Package '{packageName} {version}' was not found at the configured source.");
            return;
        }

        ApplyPackageAndShowReleaseNotes(selected, $"Package selected: {packageName} {version}.", "Select Package");
    }

    /// <summary>
    ///     Shared extract-and-pin sequence reused by both <see cref="Upgrade"/> (after resolving
    ///     the latest version) and <see cref="ApplySelectedPackage"/> (after resolving the exact
    ///     user-selected version): extracts the package (blind-delete-and-replace), rewrites the
    ///     pin file, refreshes this card's pin/upgrade-status properties, and raises
    ///     <see cref="ReleaseNotesReady"/> with the new package's release notes.
    /// </summary>
    /// <param name="package">The resolved package to apply.</param>
    /// <param name="successMessage">The <see cref="StatusMessage"/> text to set on success.</param>
    /// <param name="failureVerb">A short verb phrase (e.g. <c>"Upgrade"</c>, <c>"Select Package"</c>)
    ///     used to prefix any <see cref="ErrorOccurred"/> message raised on failure.</param>
    private void ApplyPackageAndShowReleaseNotes(DiscoveredPackage package, string successMessage, string failureVerb)
    {
        try
        {
            PackageZipExtractor.Extract(package.FilePath, RepoPath);
            RepoPinStore.Save(RepoPath, new RepoPin { PackageName = package.PackageName, Version = package.Version.ToString() });

            var releaseNotes = PackageZipExtractor.ReadReleaseNotes(package.FilePath) ?? string.Empty;

            RefreshPin();
            RefreshUpgradeStatus();
            StatusMessage = successMessage;
            ReleaseNotesReady?.Invoke(this, releaseNotes);
        }
        catch (InvalidOperationException ex)
        {
            ErrorOccurred?.Invoke(this, $"{failureVerb} failed: {ex.Message}");
        }
        catch (DirectoryNotFoundException ex)
        {
            ErrorOccurred?.Invoke(this, $"Package source is unreachable: {ex.Message}");
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
    ///     <see cref="CanPull"/>. Any failure (not a git repo, git not installed, etc.) is treated
    ///     as "pull not offered" rather than propagated.
    /// </summary>
    private void RefreshGitStatus()
    {
        try
        {
            var settings = _getSettings();
            var git = new GitClient(ResolveGitExecutablePath(settings));
            CanPull = git.IsWorkingTreeClean(RepoPath);
        }
        catch (InvalidOperationException)
        {
            CanPull = false;
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
