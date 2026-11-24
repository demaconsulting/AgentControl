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

using System.Collections.ObjectModel;
using System.Text.Json;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.AgentToolLauncher;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     What became of a folder that was asked to be added to the repo list (see
///     <see cref="MainWindowViewModel.AddRepoToGroup"/> and <see cref="MainWindowViewModel.AddRepos"/>).
/// </summary>
internal enum AddRepoResult
{
    /// <summary>
    ///     The repo was added as a new entry (in the requested group, if one was given).
    /// </summary>
    Added,

    /// <summary>
    ///     The repo was already listed, and its existing entry was moved into the requested group.
    /// </summary>
    MovedExisting,

    /// <summary>
    ///     The repo was already listed but could not be moved because the settings could not be
    ///     saved; the failure has already been reported through
    ///     <see cref="MainWindowViewModel.SettingsSaveFailed"/>.
    /// </summary>
    MoveFailed,

    /// <summary>
    ///     The repo is already listed and no group was requested, so nothing changed.
    /// </summary>
    AlreadyListed,

    /// <summary>
    ///     The folder does not exist.
    /// </summary>
    Missing,

    /// <summary>
    ///     The path is not the top folder of a Git repository (it has no <c>.git</c> folder or file
    ///     directly in it), or it is a file rather than a folder; nothing was added.
    /// </summary>
    NotAGitRepository,

    /// <summary>
    ///     The repo could not be added because the settings could not be saved; nothing changed.
    /// </summary>
    SaveFailed
}

/// <summary>
///     What became of one folder of a multi-folder add (see <see cref="MainWindowViewModel.AddRepos"/>).
/// </summary>
/// <param name="Path">The folder's path, as it was given.</param>
/// <param name="Result">What became of it.</param>
/// <param name="Problem">For <see cref="AddRepoResult.SaveFailed"/>, the reason the settings could not
///     be saved, safe to show (secrets redacted); otherwise <see langword="null"/>.</param>
internal sealed record AddRepoOutcome(string Path, AddRepoResult Result, string? Problem = null);

/// <summary>
///     What a folder that was asked to be added turned out to be, as read from the file system (see
///     <see cref="MainWindowViewModel.InspectFolder"/>).
/// </summary>
internal enum RepoFolderKind
{
    /// <summary>
    ///     Nothing exists at the path.
    /// </summary>
    Missing,

    /// <summary>
    ///     A file exists at the path (a dropped file), not a folder.
    /// </summary>
    NotAFolder,

    /// <summary>
    ///     A folder that is not the top folder of a Git repository.
    /// </summary>
    NotAGitRepositoryRoot,

    /// <summary>
    ///     The top folder of a Git repository.
    /// </summary>
    GitRoot
}

/// <summary>
///     What the background part of an add learned about one folder; immutable, so it can be handed
///     from a worker thread to the UI thread, where the add is committed.
/// </summary>
/// <param name="Path">The folder's path, as it was given.</param>
/// <param name="NormalizedPath">The folder's path in its normalized form (see
///     <see cref="MainWindowViewModel.NormalizeRepoPath"/>), the form a new entry is stored in.</param>
/// <param name="Kind">What the folder is.</param>
/// <param name="ListedAtStart">Whether the repo was already listed when the add started; such a repo
///     is never subject to the Git check's facts, so its cheap facts were not read.</param>
/// <param name="Cheap">The cheap-refresh facts of a Git repository root that was not listed (its pin,
///     branch and committed-files answer), or <see langword="null"/> when not read.</param>
internal sealed record RepoFolderFacts(
    string Path, string NormalizedPath, RepoFolderKind Kind, bool ListedAtStart, RepoCheapFacts? Cheap);

/// <summary>
///     How far a background add has got: how many of its folders have been checked.
/// </summary>
/// <param name="Completed">How many folders have been checked so far.</param>
/// <param name="Total">How many folders the add checks.</param>
internal readonly record struct AddReposProgress(int Completed, int Total);

/// <summary>
///     What became of a background add (see <see cref="MainWindowViewModel.AddReposAsync"/>).
/// </summary>
/// <param name="Outcomes">One outcome per distinct, non-blank folder, in the order given; empty when
///     the add was canceled.</param>
/// <param name="Canceled">Whether the add was canceled before anything was saved or added.</param>
internal sealed record AddReposBatchResult(IReadOnlyList<AddRepoOutcome> Outcomes, bool Canceled);

/// <summary>
///     Carries the question the view asks when the settings could not be saved while repos were being
///     added - try saving again, or discard the add - and carries the answer back.
/// </summary>
/// <remarks>
///     Raised by <see cref="MainWindowViewModel.AddSaveFailed"/>. The add waits for
///     <see cref="Answer"/>; nothing of the add has been applied at that point, so Discard needs no
///     undo. The message is written for the user. The first answer wins; later calls to
///     <see cref="Respond"/> are ignored. Thread-safe.
/// </remarks>
internal sealed class SettingsSaveRetryRequest
{
    /// <summary>
    ///     Completes with the user's answer; runs continuations asynchronously so answering from the
    ///     view never runs the add inside the dialog's close handling.
    /// </summary>
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Initializes a request.
    /// </summary>
    /// <param name="message">The complete question to show: what failed, why, and what Retry and
    ///     Discard do. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is
    ///     <see langword="null"/>.</exception>
    public SettingsSaveRetryRequest(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
    }

    /// <summary>
    ///     Gets the complete question to show the user.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the task that completes with <see langword="true"/> for Retry and
    ///     <see langword="false"/> for Discard (or when the question could not be shown).
    /// </summary>
    public Task<bool> Answer => _answer.Task;

    /// <summary>
    ///     Records the user's answer.
    /// </summary>
    /// <param name="retry"><see langword="true"/> to try saving again; <see langword="false"/> to
    ///     discard the add.</param>
    public void Respond(bool retry) => _answer.TrySetResult(retry);
}

/// <summary>
///     The two ways the "Move" items of a group header's menu change the group's place in the list.
/// </summary>
internal enum RepoGroupMove
{
    /// <summary>
    ///     One place towards the top of the list (never above Favorites).
    /// </summary>
    Up,

    /// <summary>
    ///     One place towards the bottom of the list (never below Ungrouped).
    /// </summary>
    Down
}

/// <summary>
///     Where a dragged group header is dropped: next to another of the user's groups.
/// </summary>
/// <param name="GroupName">The group whose header the dragged one is dropped on.</param>
/// <param name="InsertBefore"><see langword="true"/> to put the dragged group before
///     <paramref name="GroupName"/> (the upper half of its header), <see langword="false"/> after it.</param>
internal sealed record GroupDropTarget(string GroupName, bool InsertBefore);

/// <summary>
///     The four ways the "Move" items of a card's menu change a repo's place within its section.
/// </summary>
internal enum RepoMove
{
    /// <summary>
    ///     To the first place of the repo's section.
    /// </summary>
    Top,

    /// <summary>
    ///     One place towards the top of the repo's section.
    /// </summary>
    Up,

    /// <summary>
    ///     One place towards the bottom of the repo's section.
    /// </summary>
    Down,

    /// <summary>
    ///     To the last place of the repo's section.
    /// </summary>
    Bottom
}

/// <summary>
///     Where a dragged card is dropped: a section (a group, Favorites or Ungrouped) and a place in
///     it, given as a card of that section to insert next to or, for a section header, "the end".
/// </summary>
/// <param name="Kind">The kind of the target section.</param>
/// <param name="Name">The target section's display name (a group's name, or the built-in
///     <see cref="RepoGroupName.FavoritesName"/>/<see cref="RepoGroupName.UngroupedName"/>).</param>
/// <param name="Anchor">The card of the target section to insert next to, or <see langword="null"/>
///     for the end of the section (a drop on its header).</param>
/// <param name="InsertBefore"><see langword="true"/> to insert before <paramref name="Anchor"/>,
///     <see langword="false"/> to insert after it; ignored when there is no anchor.</param>
internal sealed record RepoDropTarget(RepoGroupKind Kind, string Name, RepoCardViewModel? Anchor, bool InsertBefore)
{
    /// <summary>
    ///     Creates the target for a drop on a section's header: the end of that section.
    /// </summary>
    /// <param name="header">The header the card was dropped on.</param>
    /// <returns>The target.</returns>
    public static RepoDropTarget ForHeader(RepoGroupHeaderViewModel header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return new RepoDropTarget(header.Kind, header.Name, null, false);
    }

    /// <summary>
    ///     Creates the target for a drop on a card: just before or after it, in its section.
    /// </summary>
    /// <param name="anchor">The card that was dropped on.</param>
    /// <param name="insertBefore"><see langword="true"/> for the upper half of the card,
    ///     <see langword="false"/> for the lower half.</param>
    /// <param name="kind">The kind of the anchor's section.</param>
    /// <param name="name">The anchor's section's display name.</param>
    /// <returns>The target.</returns>
    public static RepoDropTarget ForCard(RepoCardViewModel anchor, bool insertBefore, RepoGroupKind kind, string name)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        return new RepoDropTarget(kind, name, anchor, insertBefore);
    }
}

/// <summary>
///     View model for the main launcher window: owns the user-ordered repo list and the settings
///     persistence lifecycle.
/// </summary>
/// <remarks>
///     Window-management concerns (opening <c>SettingsWindow</c>/<c>ReleaseNotesViewer</c>,
///     showing folder-browse dialogs) are intentionally left to <c>MainWindow</c>'s code-behind,
///     since they require live Avalonia <c>Window</c>/<c>IStorageProvider</c> instances that
///     cannot be constructed in a headless unit test; every other behavior (recent-repo list
///     population, upgrade-badge/pull-eligibility state, settings round-tripping) lives here so
///     it can be tested directly. Not thread-safe; every member is expected to be called from
///     the UI thread.
/// </remarks>
internal sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    ///     The most upgrade checks that read package sources at the same time, across every check that
    ///     runs (startup, added repos, after Settings, the periodic round); see
    ///     <see cref="_upgradeCheckSlots"/>.
    /// </summary>
    private const int MaxConcurrentUpgradeChecks = 4;

    /// <summary>
    ///     The one limit every background upgrade check shares, so overlapping checks (the startup
    ///     check, an added repo's, the one after Settings closed and a periodic round) never read more
    ///     than <see cref="MaxConcurrentUpgradeChecks"/> package sources at once between them. Never
    ///     disposed: a check that is still unwinding when the window closes may release its slot.
    /// </summary>
    private readonly SemaphoreSlim _upgradeCheckSlots = new(MaxConcurrentUpgradeChecks);

    /// <summary>
    ///     The longest the window's closing waits for the settings that are not written yet (see
    ///     <see cref="Dispose"/>).
    /// </summary>
    /// <remarks>
    ///     A write normally takes milliseconds; a file locked by another process is given up after
    ///     about a second (see <see cref="SettingsStore.ReplaceMaxAttempts"/>). Five seconds covers a
    ///     confirmed write that is still running at the close and the close's own write after it, each
    ///     on a held file, without keeping a closed window's process alive for long. A write still
    ///     running after it means the disk itself is not answering, which waiting longer does not
    ///     cure.
    /// </remarks>
    internal static readonly TimeSpan SettingsFlushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The longest the window's closing waits for the settings that are not written yet:
    ///     <see cref="SettingsFlushTimeout"/> unless a test supplied a shorter one (see the
    ///     constructor).
    /// </summary>
    private readonly TimeSpan _settingsFlushTimeout;

    /// <summary>
    ///     Writes the settings in the background, one write at a time, the latest state winning; every
    ///     settings save of the window goes through it, so none runs on the UI thread. See the
    ///     constructor.
    /// </summary>
    private readonly SettingsWriter _settingsWriter;

    /// <summary>
    ///     The last settings snapshot <see cref="_settingsWriter"/> wrote, or the settings as they were
    ///     when the view model was created while nothing was written yet: what the settings file is
    ///     known to hold, against which the close logs what it could not write (see
    ///     <see cref="Dispose"/>). Set on the writing thread, read with <see cref="Volatile"/>.
    /// </summary>
    private AppSettings _lastWrittenSettings;

    /// <summary>
    ///     The confirmed saves (an add, the Settings window's Save) that are running, oldest first; while
    ///     any is, a change made in the window is not handed to <see cref="_settingsWriter"/> but
    ///     remembered in <see cref="_saveDeferred"/> (see <see cref="ScheduleSettingsSave"/>). The close
    ///     (see <see cref="Dispose"/>) writes the changes made meanwhile after their writes have ended,
    ///     together with the change of each one whose write succeeded.
    /// </summary>
    private readonly List<RunningConfirmedSave> _runningConfirmedSaves = [];

    /// <summary>
    ///     Whether the live settings hold something that has to be written once the confirmed saves
    ///     that are running have ended: a change made while they ran, or a confirmed save that ended
    ///     while another one still ran (see <see cref="SaveConfirmedAsync"/>). Used only while the
    ///     window is open, so that a confirmed save with no other change costs one write; the close
    ///     never reads it, because it always writes the intended state last (see
    ///     <see cref="Dispose"/>).
    /// </summary>
    private bool _saveDeferred;

    /// <summary>
    ///     Logs what cannot be shown once the window has closed (settings that could not be written
    ///     at the close), through a <see cref="SecretRedactingLogger"/>, so a failure logged with its
    ///     exception carries no secret.
    /// </summary>
    private static readonly ILogger Logger = new SecretRedactingLogger(AppLogging.Factory.CreateLogger<MainWindowViewModel>());

    /// <summary>
    ///     Runs the blocking reads of adds and refreshes off the UI thread; see the constructor.
    /// </summary>
    private readonly IBackgroundWorkRunner _backgroundWork;

    /// <summary>
    ///     The one queue every lazy repo-state read goes through - the folder checks of adds, the
    ///     cards' refresh after a settings save and the working-tree checks of the cards shown - so only
    ///     a few git processes run at once; see the constructor.
    /// </summary>
    private readonly RepoRefreshQueue _refreshQueue;

    /// <summary>
    ///     Whether <see cref="_refreshQueue"/> was created here (none was supplied), so
    ///     <see cref="Dispose"/> shuts it down.
    /// </summary>
    private readonly bool _ownsRefreshQueue;

    /// <summary>
    ///     The time limits of every git command the cards and adds run, or <see langword="null"/> for
    ///     <see cref="GitClientTimeouts.Default"/>; see the constructor.
    /// </summary>
    private readonly GitClientTimeouts? _gitTimeouts;

    /// <summary>
    ///     The global progress operation that shows the refresh queue's current wave, or
    ///     <see langword="null"/> while no wave runs.
    /// </summary>
    private GlobalProgressOperation? _refreshProgress;

    /// <summary>
    ///     The adds that are running or queued and share one Cancel and one progress operation, or
    ///     <see langword="null"/> when none is (see <see cref="EnqueueAddReposAsync"/>).
    /// </summary>
    private AddQueueSession? _addSession;

    /// <summary>
    ///     Identifies the most recent card refresh started by <see cref="ApplySettingsAsync"/>, so an older
    ///     one that is still running does not update the list or start the upgrade check after it.
    /// </summary>
    private int _settingsRefreshGeneration;

    /// <summary>
    ///     How often the periodic background upgrade re-check runs by default: the lifetime of a
    ///     cached listing, so no card's upgrade badge is older than the listing it was derived from
    ///     is allowed to be.
    /// </summary>
    internal static readonly TimeSpan DefaultUpgradeRecheckInterval = PackageVersionCache.EntryLifetime;

    /// <summary>
    ///     Builds the registry of configured sources for every card, or <see langword="null"/> for
    ///     the real providers; see the constructor.
    /// </summary>
    private readonly Func<AppSettings, PackageSourceRegistry>? _registryFactory;

    /// <summary>
    ///     Starts the agent-tool process for every card, or <see langword="null"/> for the real
    ///     one; see the constructor.
    /// </summary>
    private readonly IAgentToolProcessStarter? _processStarter;

    /// <summary>
    ///     Detects the shell for every card's launch, or <see langword="null"/> for a real
    ///     detector; see the constructor.
    /// </summary>
    private readonly ShellDetector? _shellDetector;

    /// <summary>
    ///     The clock that drives the periodic upgrade re-check (and dates the package-version
    ///     cache's entries).
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     How often the periodic upgrade re-check runs.
    /// </summary>
    private readonly TimeSpan _upgradeRecheckInterval;

    /// <summary>
    ///     The timer that triggers the periodic upgrade re-check, or <see langword="null"/> when it
    ///     is not running (not started yet, or this view model was disposed).
    /// </summary>
    private ITimer? _recheckTimer;

    /// <summary>
    ///     Cancels the periodic re-check round that is running, or <see langword="null"/> when none is.
    /// </summary>
    private CancellationTokenSource? _recheckCancellation;

    /// <summary>
    ///     Runs an action on the UI thread; the periodic timer fires on a thread-pool thread, and
    ///     every property this view model's cards expose is updated on the UI thread.
    /// </summary>
    private Action<Action>? _runOnUiThread;

    /// <summary>
    ///     Whether this view model was disposed, after which no re-check starts.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Cancels the background upgrade check started by the most recent
    ///     <see cref="BeginUpgradeRefresh()"/>, or <see langword="null"/> before the first one.
    /// </summary>
    private CancellationTokenSource? _refreshCancellation;

    /// <summary>
    ///     How many launch-time (or settings-change, or added-repo) upgrade checks are still running,
    ///     superseded and canceled ones included until they have unwound. A periodic round does not
    ///     start while this is above zero.
    /// </summary>
    private int _launchChecksInFlight;

    /// <summary>
    ///     The failure memos of the background checks that are running (periodic rounds and
    ///     launch-style checks, superseded ones included until they have unwound), so that
    ///     <see cref="RecheckAfterSettingsClosed"/> can make them forget the failures they recorded
    ///     before a sign-in or a returned connection. Guarded by <see cref="_activeFailureMemosLock"/>.
    /// </summary>
    private readonly List<SourceFailureMemo> _activeFailureMemos = [];

    /// <summary>
    ///     Guards <see cref="_activeFailureMemos"/>: a check may register or drop its memo from a
    ///     thread-pool continuation when no UI thread runner is in use (tests).
    /// </summary>
    private readonly Lock _activeFailureMemosLock = new();


    /// <summary>
    ///     Test-only startup overrides passed through to every <see cref="RepoCardViewModel"/>.
    /// </summary>
    private readonly StartupOptions? _startupOptions;

    /// <summary>
    ///     Applies the user's theme choice to the running application, or <see langword="null"/>
    ///     for none; see the constructor.
    /// </summary>
    private readonly Action<ThemePreference>? _applyTheme;
    /// <summary>
    ///     The live application settings instance; mutated in place by
    ///     <see cref="CommitSettingsChange"/>, replaced by <see cref="ApplySettingsAsync"/> and written
    ///     in the background by <see cref="_settingsWriter"/>.
    /// </summary>
    private AppSettings _settings;

    /// <summary>
    ///     The package-version cache passed to every <see cref="RepoCardViewModel"/>, per
    ///     architecture.md's repo-fact caching strategy; its entries expire after five minutes.
    /// </summary>
    private readonly PackageVersionCache _packageVersionCache;

    /// <summary>
    ///     The current filter text typed into the recent-repos search box, or
    ///     <see langword="null"/>/empty for no filter.
    /// </summary>
    private string? _filterText;

    /// <summary>
    ///     The section headers in use, one instance per section (keyed by kind and name) so a header
    ///     is updated in place rather than replaced and keeps keyboard focus.
    /// </summary>
    private readonly Dictionary<string, RepoGroupHeaderViewModel> _headers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Initializes a new <see cref="MainWindowViewModel"/>, populating
    ///     <see cref="RepoCards"/> from <paramref name="settings"/>'s recent-repos list.
    /// </summary>
    /// <param name="settings">The loaded application settings.</param>
    /// <param name="startupOptions">Test-only startup overrides, or <see langword="null"/>.</param>
    /// <param name="configDirectory">The configuration directory settings are persisted to, or
    ///     <see langword="null"/> to use the default.</param>
    /// <param name="credentials">The credential facilities (session store and sign-in prompt) that
    ///     NuGet server sources use, shared by every card and by the settings window's connection
    ///     test; <see langword="null"/> for none.</param>
    /// <param name="applyTheme">Applies the user's theme choice to the running application; called
    ///     by <see cref="ApplySettingsAsync"/> after the settings were saved. <see langword="null"/>
    ///     applies nothing (the view model then has no dependency on the UI framework, so tests
    ///     can observe the call with a fake).</param>
    /// <param name="timeProvider">The clock behind the package-version cache's expiry and the
    ///     periodic upgrade re-check, or <see langword="null"/> for the system clock. Supplied so
    ///     tests can move time without waiting.</param>
    /// <param name="upgradeRecheckInterval">How often the periodic upgrade re-check runs once
    ///     <see cref="StartPeriodicUpgradeRecheck"/> was called, or <see langword="null"/> for
    ///     <see cref="DefaultUpgradeRecheckInterval"/> (five minutes).</param>
    /// <param name="registryFactory">Builds the registry of configured sources for every card from
    ///     the settings; <see langword="null"/> uses the real providers. Supplied so tests can
    ///     substitute in-memory sources whose reads they count.</param>
    /// <param name="processStarter">Starts the agent-tool process for every card;
    ///     <see langword="null"/> uses the real one. Supplied so tests can capture launches without
    ///     starting a shell.</param>
    /// <param name="shellDetector">Detects the shell for every card's launch;
    ///     <see langword="null"/> uses a real <see cref="ShellDetector"/>. Supplied so tests get a
    ///     deterministic shell.</param>
    /// <param name="saveSettings">Writes one settings snapshot to disk; <see langword="null"/> uses
    ///     <see cref="SettingsStore.Save"/> into <paramref name="configDirectory"/>. Supplied so tests
    ///     can count saves, hold them and make them fail. It is called by the background settings
    ///     writer on a thread-pool thread, one call at a time, and must throw
    ///     <see cref="InvalidOperationException"/> when the settings could not be written.</param>
    /// <param name="backgroundWork">Runs the blocking reads of adds and card refreshes off the UI
    ///     thread, for this view model and every card; <see langword="null"/> uses the thread pool.
    ///     Supplied so tests can hold the reads at a gate.</param>
    /// <param name="refreshQueue">The queue every lazy repo-state read goes through, shared with every
    ///     card; <see langword="null"/> creates one over <paramref name="backgroundWork"/> (two reads at
    ///     a time), which <see cref="Dispose"/> shuts down. Create the view model on the UI thread: the
    ///     queue reports its progress to the synchronization context current then.</param>
    /// <param name="gitTimeouts">The time limits of every git command the cards and adds run, or
    ///     <see langword="null"/> for <see cref="GitClientTimeouts.Default"/>. Supplied so tests can use a
    ///     short limit against a git that hangs.</param>
    /// <param name="settingsWriteDelay">How long a change waits for the next one before the settings
    ///     are written in the background, or <see langword="null"/> for
    ///     <see cref="SettingsWriter.DefaultDelay"/>; measured on <paramref name="timeProvider"/>. Create
    ///     the view model on the UI thread: a failed background write is reported there.</param>
    /// <param name="settingsFlushTimeout">The longest the window's closing waits for the settings
    ///     that are not written yet (see <see cref="Dispose"/>), or <see langword="null"/> for
    ///     <see cref="SettingsFlushTimeout"/>. Supplied so tests can close without waiting for a write
    ///     they hold.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    ///     <paramref name="upgradeRecheckInterval"/> is zero or negative, or
    ///     <paramref name="settingsWriteDelay"/> or <paramref name="settingsFlushTimeout"/> is
    ///     negative.</exception>
    public MainWindowViewModel(
        AppSettings settings,
        StartupOptions? startupOptions = null,
        string? configDirectory = null,
        PackageCredentialContext? credentials = null,
        Action<ThemePreference>? applyTheme = null,
        TimeProvider? timeProvider = null,
        TimeSpan? upgradeRecheckInterval = null,
        Func<AppSettings, PackageSourceRegistry>? registryFactory = null,
        IAgentToolProcessStarter? processStarter = null,
        ShellDetector? shellDetector = null,
        Action<AppSettings>? saveSettings = null,
        IBackgroundWorkRunner? backgroundWork = null,
        RepoRefreshQueue? refreshQueue = null,
        GitClientTimeouts? gitTimeouts = null,
        TimeSpan? settingsWriteDelay = null,
        TimeSpan? settingsFlushTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            upgradeRecheckInterval ?? DefaultUpgradeRecheckInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(settingsWriteDelay ?? TimeSpan.Zero, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(settingsFlushTimeout ?? TimeSpan.Zero, TimeSpan.Zero);

        _settings = settings;
        _settingsFlushTimeout = settingsFlushTimeout ?? SettingsFlushTimeout;

        // A first run, or settings built in memory, still have to get the order older versions
        // displayed; a stored user order is left alone
        settings.EnsureRepoOrder();

        // Settings built in memory have not been through the load's repair: the group colors and
        // order are made to match the groups that exist, as a load would leave them
        settings.RepairGroupSettings();
        _registryFactory = registryFactory;
        _processStarter = processStarter;
        _shellDetector = shellDetector;
        _startupOptions = startupOptions;
        _backgroundWork = backgroundWork ?? ThreadPoolBackgroundWorkRunner.Instance;
        GlobalProgress = new GlobalProgressViewModel();
        _ownsRefreshQueue = refreshQueue is null;
        _refreshQueue = refreshQueue ?? new RepoRefreshQueue(_backgroundWork);
        _refreshQueue.ProgressChanged += OnRefreshQueueProgressChanged;
        _gitTimeouts = gitTimeouts;
        _applyTheme = applyTheme;
        _timeProvider = timeProvider ?? TimeProvider.System;
        var save = saveSettings ?? (snapshot => SettingsStore.Save(snapshot, configDirectory));
        _lastWrittenSettings = settings.CreateSnapshot();
        _settingsWriter = new SettingsWriter(
            snapshot =>
            {
                // The writer makes one write at a time, so the last one that succeeded is the file
                save(snapshot);
                Volatile.Write(ref _lastWrittenSettings, snapshot);
            },
            _timeProvider,
            settingsWriteDelay);
        _settingsWriter.WriteFailed += OnSettingsWriteFailed;
        _upgradeRecheckInterval = upgradeRecheckInterval ?? DefaultUpgradeRecheckInterval;
        _packageVersionCache = new PackageVersionCache(_timeProvider);
        Credentials = credentials;

        RepoCards = [];
        DisplayedRepoCards = [];
        DisplayedItems = [];
        foreach (var card in settings.RecentRepos.Select(CreateCard))
        {
            AttachCardEvents(card);
            RepoCards.Add(card);
        }

        UpdateDisplayedRepoCards();

        // Nothing is read from the disk or git here: the cards are shown at once from the settings
        // and read in the background through the shared queue (see StartStartupRefresh)
        PendingStartupRefresh = StartStartupRefresh();
    }

    /// <summary>
    ///     Gets the task of the cards' startup read (see <see cref="StartStartupRefresh"/>), so a caller
    ///     (a test) can wait for it; never faults.
    /// </summary>
    internal Task PendingStartupRefresh { get; }

    /// <summary>
    ///     Queues the startup read of every card's repo state - the folder, the pin file, the branch and
    ///     the committed agent folders - in the shared refresh queue at background priority, in display
    ///     order, counted in the progress wave.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The window is shown at once, from the settings alone: no disk access and no git on the UI
    ///     thread before or after it appears. Each card shows that its state is being read
    ///     (<see cref="RepoCardViewModel.HasRepoState"/>, <see cref="RepoCardViewModel.IsRefreshing"/>)
    ///     until its read lands, and the global strip shows "Refreshing repositories {n} of
    ///     {total}..." meanwhile (see <see cref="OnRefreshQueueProgressChanged"/>). Even the pin file is
    ///     read in the background: a repo on an offline network share can take tens of seconds to
    ///     answer a single file read. The pinned package the settings remember is shown until then.
    ///     </para>
    ///     <para>
    ///     The cards shown come first (top of the list first), then the collapsed and filtered-out
    ///     ones; a card the window realizes moves its read ahead of the background ones (see
    ///     <c>MainWindow.RepoCard_AttachedToVisualTree</c>), and a user action on a card moves it to
    ///     the front (see <see cref="RepoCardViewModel.EnsureCurrentRepoStateAsync"/>). The working-tree
    ///     check is not part of startup; it is made for the cards the window shows.
    ///     </para>
    /// </remarks>
    /// <returns>A task that completes when every card's startup read was applied or dropped.</returns>
    private Task StartStartupRefresh()
    {
        var reads = DisplayedRepoCards.Concat(RepoCards).Distinct()
            .Select(card => card.RefreshRepoStateAsync(
                RepoRefreshPriority.Background, cheapFacts: true, workingTree: false, reportsProgress: true, CancellationToken.None))
            .ToList();
        return Task.WhenAll(reads);
    }

    /// <summary>
    ///     Waits until no card's repo-state read waits in the shared queue or runs, including reads that
    ///     start while it waits (for example a card's working-tree check started when the window showed
    ///     it); a test seam for code that must see the cards' settled state.
    /// </summary>
    /// <returns>A task that completes when every card's state has settled; never faults.</returns>
    internal async Task WhenRepoStateSettledAsync()
    {
        while (true)
        {
            var cards = RepoCards.ToList();
            await Task.WhenAll(cards.Select(card => card.WhenRepoStateSettledAsync()));
            if (RepoCards.All(card => card.CheapRefreshTask.IsCompleted && card.GitStatusRefreshTask.IsCompleted))
            {
                return;
            }
        }
    }

    /// <summary>
    ///     Gets the repo list in the user's own order, backing this view model's underlying state
    ///     (settings persistence, additions/removals, reordering).
    /// </summary>
    /// <remarks>
    ///     The order is the one stored in <see cref="AppSettings.RecentRepos"/>: a new repo is added
    ///     at the end, <see cref="MoveCard"/> and <see cref="DropCard"/> reorder it, and nothing
    ///     else does (launching, refreshing and filtering never move a card). Each section of
    ///     <see cref="DisplayedItems"/> shows its cards in this relative order.
    /// </remarks>
    public ObservableCollection<RepoCardViewModel> RepoCards { get; }

    /// <summary>
    ///     Gets the filtered cards that are currently shown: those of
    ///     <see cref="RepoCards"/> that match <see cref="FilterText"/> (a case-insensitive substring
    ///     match against <see cref="RepoCardViewModel.RepoName"/>/<see cref="RepoCardViewModel.RepoPath"/>),
    ///     in the order <see cref="DisplayedItems"/> shows them, without the cards hidden inside
    ///     collapsed sections.
    /// </summary>
    /// <remarks>
    ///     See <see cref="DisplayedItems"/> for the order. When no repo is in a group the list is
    ///     flat: favorites first, then the user's own order.
    /// </remarks>
    public ObservableCollection<RepoCardViewModel> DisplayedRepoCards { get; }

    /// <summary>
    ///     Gets what the main window's list binds to: the cards of <see cref="DisplayedRepoCards"/>
    ///     interleaved with the <see cref="RepoGroupHeaderViewModel"/> that starts each section.
    /// </summary>
    /// <remarks>
    ///     <b>Sections.</b> When at least one repo is in a group (<see cref="HasGroups"/>) the list is
    ///     sectioned: <c>Favorites</c> first (the favorite repos - a favorite is listed only there,
    ///     not again under its group, and its card shows a group chip), then the user's groups in the
    ///     user's own order (<see cref="AppSettings.GroupOrder"/>; groups without a stored position
    ///     follow alphabetically), then <c>Ungrouped</c> last; sections with no matching repo have no header.
    ///     When no repo is in a group there are no headers at all and the list looks as it always
    ///     did, favorites first. Within a section repos are in the user's own order (the relative
    ///     order of <see cref="RepoCards"/>); the time of a launch plays no part. <b>Collapsing.</b>
    ///     The cards of a collapsed section are left out
    ///     (its header stays, with the count of its matches); the state is remembered per user in
    ///     <see cref="AppSettings.CollapsedGroups"/>. <b>Filtering.</b> While a filter is active
    ///     every section is shown expanded and sections without a match disappear, so a match is
    ///     never hidden; the remembered collapsed state and the order are not changed by filtering.
    /// </remarks>
    public ObservableCollection<object> DisplayedItems { get; }

    /// <summary>
    ///     Gets a value indicating whether at least one repo is in a group of the user's own, which
    ///     is what switches the list from flat to sectioned (with headers).
    /// </summary>
    public bool HasGroups => RepoCards.Any(card => card.HasGroup);

    /// <summary>
    ///     Gets the names of the groups that exist now, alphabetically.
    /// </summary>
    public IReadOnlyList<string> GroupNames => _settings.GetGroupNames();

    /// <summary>
    ///     Gets a value indicating whether <see cref="DisplayedItems"/> is currently empty (no repo
    ///     matches, or there is none), so the view can swap the repo list for an explanatory
    ///     empty-state placeholder instead of just rendering nothing.
    /// </summary>
    /// <remarks>
    ///     Recomputed (and its change notified) every time <see cref="UpdateDisplayedRepoCards"/>
    ///     runs, which covers every path that can change the displayed set: adding/removing a
    ///     repo, filtering, favorite-toggling and grouping. A list whose sections are all collapsed
    ///     is not empty: the headers are still shown.
    /// </remarks>
    public bool HasNoDisplayedRepos => DisplayedItems.Count == 0;

    /// <summary>
    ///     Gets the message shown by the empty-state placeholder when
    ///     <see cref="HasNoDisplayedRepos"/> is <see langword="true"/>, distinguishing "no repos
    ///     added yet" from "no repos match the current filter" so the user knows which action
    ///     (add a repo, or clear the filter) would resolve it.
    /// </summary>
    public string EmptyStateMessage =>
        RepoCards.Count == 0
            ? "No repos yet. Click the folder-with-a-plus button above to add your first repository. " +
              "Use the gear button to set up where your agent packages come from."
            : $"No repos match \"{FilterText}\". Try a different filter.";

    /// <summary>
    ///     Gets or sets the current filter text typed into the recent-repos search box.
    /// </summary>
    public string? FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                OnPropertyChanged(nameof(IsReorderEnabled));
                UpdateDisplayedRepoCards();
            }
        }
    }

    /// <summary>
    ///     The explanation shown (as a tooltip on the disabled Move items) while the filter has text
    ///     and so the order cannot be changed.
    /// </summary>
    public const string ReorderDisabledWhileFilteringMessage =
        "Clear the filter to change the order: while the filter has text only some repos are shown, " +
        "so their positions cannot be changed.";

    /// <summary>
    ///     Gets a value indicating whether the user may change the order of the repos now: only while
    ///     the filter is empty, because a filtered list shows just some of the repos.
    /// </summary>
    /// <remarks>
    ///     Governs the Move menu items and the start of a drag; <see cref="MoveCard"/> and
    ///     <see cref="CanDrop"/> refuse too, so no path changes the order while the filter has text.
    /// </remarks>
    public bool IsReorderEnabled => string.IsNullOrWhiteSpace(_filterText);

    /// <summary>
    ///     Gets the current application settings, for building a
    ///     <see cref="SettingsWindowViewModel"/> from the main window's code-behind.
    /// </summary>
    public AppSettings Settings => _settings;

    /// <summary>
    ///     Gets the state of the global progress strip under the app bar, which shows the bulk
    ///     operations of this view model (adding repositories, refreshing the cards after Settings were
    ///     saved) and lets the user cancel an add.
    /// </summary>
    public GlobalProgressViewModel GlobalProgress { get; }

    /// <summary>
    ///     Gets the credential facilities that NuGet server sources use to sign in, or
    ///     <see langword="null"/> when there are none.
    /// </summary>
    internal PackageCredentialContext? Credentials { get; }

    /// <summary>
    ///     Adds a repository to the end of the repo list (the bottom of its section) and persists
    ///     the updated settings.
    /// </summary>
    /// <remarks>
    ///     The synchronous single add, kept for callers that add one known folder (the tests; no window
    ///     code calls it): it waits for the folder check and for the confirmed save, which the
    ///     background settings writer makes on a worker thread. The window adds through
    ///     <see cref="EnqueueAddReposAsync"/>, which waits for neither. Both use the same checks and
    ///     the same all-or-nothing commit. Only the folder is checked here
    ///     (the file system, no git): the new card's repo state is read in the background through the
    ///     shared queue, as at startup, and its working-tree check runs when the card is first shown.
    /// </remarks>
    /// <param name="repoPath">Absolute path to the repository root to add.</param>
    /// <returns>
    ///     <see langword="true"/> if the repo was added; <see langword="false"/> if
    ///     <paramref name="repoPath"/> does not exist, is already present in the list (see
    ///     <see cref="IsSameRepoPath"/>), or is not the top
    ///     folder of a Git repository (see <see cref="GitClient.IsRepositoryRoot"/>).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repoPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     the repo was not added (the list, the cards and the file are unchanged).</exception>
    public bool AddRepo(string repoPath) => AddRepoCore(repoPath, null) == AddRepoResult.Added;

    /// <summary>
    ///     Adds a repository to the repo list straight into one of the user's groups (at the bottom
    ///     of it), or, when the repository is already listed, moves its existing entry to the bottom
    ///     of that group.
    /// </summary>
    /// <remarks>
    ///     The same add as <see cref="AddRepo"/> (same folder and Git repository checks, same pin
    ///     loading, one save), with the group set on the new entry before it is saved, so the repo
    ///     never shows up ungrouped in between. A group that is collapsed is expanded in the same save
    ///     so the repo that was just added is visible. The group is a per-user setting and is never
    ///     written to the repository.
    /// </remarks>
    /// <param name="repoPath">Absolute path to the repository root to add.</param>
    /// <param name="groupName">The group to put the repo in; it must be a valid group name (see
    ///     <see cref="RepoGroupName.Validate"/>). A name matching an existing group ignoring letter
    ///     case joins that group under its own spelling.</param>
    /// <returns>What became of the repo (see <see cref="AddRepoResult"/>); never
    ///     <see cref="AddRepoResult.AlreadyListed"/> for a repo that has a card (it is moved), nor
    ///     <see cref="AddRepoResult.SaveFailed"/> (a failed save of an add throws; a failed save of a
    ///     move is reported through <see cref="SettingsSaveFailed"/> and gives
    ///     <see cref="AddRepoResult.MoveFailed"/>).</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repoPath"/> is null, empty or
    ///     whitespace, or <paramref name="groupName"/> is not a valid group name.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written for
    ///     a new repo; the repo was not added (the list, the cards and the file are unchanged).</exception>
    public AddRepoResult AddRepoToGroup(string repoPath, string groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var target = ResolveAddTargetGroup(groupName);
        return AddRepoCore(repoPath, target);
    }

    /// <summary>
    ///     Adds several folders at once, synchronously, each as <see cref="AddRepo"/> (or, with a group,
    ///     <see cref="AddRepoToGroup"/>) would, and reports what became of each.
    /// </summary>
    /// <remarks>
    ///     The synchronous core of the add, kept for callers that add known folders (the tests; no
    ///     window code calls it): it waits for the folder checks and for the confirmed save, which the
    ///     background settings writer makes on a worker thread. The window adds through
    ///     <see cref="EnqueueAddReposAsync"/>, which reads the folders off the UI thread, shows
    ///     progress and waits for nothing. Both share the same checks and the same commit: one that is missing, not a
    ///     Git repository root or already listed does not stop the others; a folder that appears more
    ///     than once (see <see cref="IsSameRepoPath"/>) is handled once; blank entries are ignored. With
    ///     a group, a repo that is already listed is moved into it, and a collapsed group is opened
    ///     when anything was added to or moved into it. Everything the batch changes is saved
    ///     <b>once</b> and the list is rebuilt once; when that save fails nothing at all changed and
    ///     every folder that would have been added or moved is reported as
    ///     <see cref="AddRepoResult.SaveFailed"/> with the reason. Only the folders are checked here (the
    ///     file system, no git): the new cards' repo state is read in the background through the shared
    ///     queue, their upgrade checks follow it, and their working-tree checks run when their cards are
    ///     first shown.
    /// </remarks>
    /// <param name="repoPaths">The folders' absolute paths, in the order to add them.</param>
    /// <param name="groupName">The group to put the repos in, or <see langword="null"/> for none;
    ///     when given it must be a valid group name (see <see cref="RepoGroupName.Validate"/>), and a
    ///     name matching an existing group ignoring letter case joins that group.</param>
    /// <returns>One outcome per distinct, non-blank path, in the order given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoPaths"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="groupName"/> is given but is
    ///     not a valid group name; nothing was added.</exception>
    public IReadOnlyList<AddRepoOutcome> AddRepos(IEnumerable<string> repoPaths, string? groupName)
    {
        ArgumentNullException.ThrowIfNull(repoPaths);

        var target = groupName is null ? null : ResolveAddTargetGroup(groupName);

        // The same steps as the background add, inline: plan, read every folder, commit once
        var plan = PlanAddBatch(repoPaths);
        var facts = plan.Folders
            .Select(folder => InspectFolder(folder.Path, folder.Listed, plan.GitExecutablePath, _gitTimeouts, CancellationToken.None, readCheapFacts: false))
            .ToList();
        var commit = PrepareAddCommit(facts, target);
        try
        {
            ApplyAddCommitNow(commit);
            return commit.Outcomes;
        }
        catch (InvalidOperationException ex)
        {
            return commit.ToSaveFailedOutcomes(SecretRedactor.SafeMessage(ex));
        }
    }

    /// <summary>
    ///     Checks the group an add was asked to put repos in and finds how it is spelled.
    /// </summary>
    /// <param name="groupName">The requested group name.</param>
    /// <returns>The canonical name: the existing group's spelling when one matches ignoring letter
    ///     case, otherwise the normalized requested name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="groupName"/> is not a valid
    ///     group name.</exception>
    private string ResolveAddTargetGroup(string groupName)
    {
        var requested = RepoGroupName.Normalize(groupName);
        if (requested is null || RepoGroupName.Validate(requested) != RepoGroupNameProblem.None)
        {
            throw new ArgumentException("The group name is not valid.", nameof(groupName));
        }

        return RepoGroupName.Canonicalize(requested, GroupNames);
    }

    /// <summary>
    ///     Gets the task of the adds that are running or queued (see
    ///     <see cref="EnqueueAddReposAsync"/>), so a caller (the window's tests) can wait for them to
    ///     settle; completed when no add is running. Never faults.
    /// </summary>
    internal Task PendingAddRepos { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Raised when the settings could not be saved at the end of a background add: the view asks
    ///     the user whether to try saving again (Retry) or to drop the add (Discard), and answers
    ///     through <see cref="SettingsSaveRetryRequest.Respond"/>.
    /// </summary>
    /// <remarks>
    ///     Nothing of the add has been applied when this is raised, so the list still shows it as it was
    ///     before the add; Retry saves the add again (re-checked against the list as it is then) and
    ///     raises this again if that save fails too, and Discard leaves everything as it is. Without a
    ///     subscriber nobody can be asked, so the add is discarded and its folders are reported as not
    ///     saved.
    /// </remarks>
    public event EventHandler<SettingsSaveRetryRequest>? AddSaveFailed;

    /// <summary>
    ///     Adds folders (from the folder picker or dropped from the file manager) in the background,
    ///     after any add that is already running or queued, showing progress in
    ///     <see cref="GlobalProgress"/>; the add the window uses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Adding many folders used to freeze the window, because every folder was checked with git on
    ///     the UI thread and saved on its own. Here each folder is read off the UI thread (see
    ///     <see cref="AddReposAsync"/>) and the whole batch is committed on the UI thread with one save
    ///     and one list rebuild, so the window stays usable: the filter, collapsing, renaming and
    ///     removing keep working while an add runs. The commit re-checks every folder against the list
    ///     as it is then, so a repo listed meanwhile is never added twice, and a target group removed
    ///     meanwhile is created again by the add (the user asked for that group).
    ///     </para>
    ///     <para>
    ///     A further request while an add runs waits for it and then runs; adds never run at the same
    ///     time. The running add and the queued ones share one progress operation with one Cancel
    ///     (<see cref="CancelAddRepos"/>), which stops them all before they save anything. When the
    ///     save fails the user is asked through <see cref="AddSaveFailed"/>. Must be called on the UI
    ///     thread.
    ///     </para>
    /// </remarks>
    /// <param name="repoPaths">The folders' absolute paths, in the order to add them. Must not be
    ///     <see langword="null"/>.</param>
    /// <param name="groupName">The group to put the repos in, or <see langword="null"/> for none;
    ///     when given it must be a valid group name.</param>
    /// <returns>A task that completes with what became of each folder, or with a canceled result
    ///     (nothing saved or added) when the user canceled or the window closed. It never faults for
    ///     an expected failure (a failed save is part of the result).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoPaths"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="groupName"/> is given but is
    ///     not a valid group name; nothing was queued.</exception>
    public Task<AddReposBatchResult> EnqueueAddReposAsync(IReadOnlyList<string> repoPaths, string? groupName)
    {
        ArgumentNullException.ThrowIfNull(repoPaths);
        if (groupName is not null)
        {
            ResolveAddTargetGroup(groupName);
        }

        // A closed window adds nothing
        if (_disposed)
        {
            return Task.FromResult(new AddReposBatchResult([], Canceled: true));
        }

        // Join the adds that are running, unless there are none or they were canceled: a canceled
        // session's Cancel must not reach an add the user asked for afterwards
        if (_addSession is null || _addSession.Cancellation.IsCancellationRequested)
        {
            _addSession = new AddQueueSession(this);
        }

        var session = _addSession;
        session.Pending++;
        var batch = RunQueuedAddAsync(PendingAddRepos, session, repoPaths, groupName);
        PendingAddRepos = batch;
        return batch;
    }

    /// <summary>
    ///     Cancels the add that is running and every add queued after it (the global progress strip's
    ///     Cancel). An add that is already saving completes; nothing else is saved or added. No
    ///     message is shown afterwards.
    /// </summary>
    public void CancelAddRepos() => _addSession?.Cancel();

    /// <summary>
    ///     Runs one queued add once the adds before it have ended, and ends the shared progress
    ///     operation after the last add of its session.
    /// </summary>
    /// <param name="previous">The adds queued before this one; may have faulted, which does not stop
    ///     this one.</param>
    /// <param name="session">The session this add belongs to.</param>
    /// <param name="repoPaths">The folders to add.</param>
    /// <param name="groupName">The group to put them in, or <see langword="null"/>.</param>
    /// <returns>What became of each folder.</returns>
    private async Task<AddReposBatchResult> RunQueuedAddAsync(
        Task previous, AddQueueSession session, IReadOnlyList<string> repoPaths, string? groupName)
    {
        try
        {
            // Adds never run at the same time: wait for the ones before, whatever became of them
            await Task.WhenAny(previous);

            var operation = session.Progress;
            operation.AllowCancel = true;
            operation.SetIndeterminate("Adding repositories...");
            var progress = new ImmediateProgress<AddReposProgress>(report =>
                operation.Report(
                    report.Completed,
                    report.Total,
                    $"Adding {report.Completed} of {report.Total} repositories..."));
            return await AddReposAsync(
                repoPaths,
                groupName,
                progress,
                session.Cancellation.Token,
                onSaving: () =>
                {
                    operation.AllowCancel = false;
                    operation.SetIndeterminate("Saving...");
                });
        }
        finally
        {
            session.Pending--;
            if (session.Pending == 0)
            {
                session.End();
                if (ReferenceEquals(_addSession, session))
                {
                    _addSession = null;
                }
            }
        }
    }

    /// <summary>
    ///     Adds folders the way <see cref="AddRepos"/> does, but reads each folder off the UI thread and
    ///     can be canceled until it saves; the body of every queued add.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Steps: plan on the UI thread (blank and duplicate paths dropped, which folders are already
    ///     listed, which git to run); read each folder through the shared refresh queue
    ///     (<see cref="RepoRefreshQueue"/>, a couple at a time across every repo read of the window, at
    ///     the priority of a visible card) (whether it exists, whether it is the
    ///     top folder of a Git repository and, for a repo not yet listed, its pin, branch and
    ///     committed agent folders - the slow part), reporting progress as each completes; then, on
    ///     the UI thread, commit the batch: one save and one list rebuild. The working-tree check of
    ///     the new repos is not made: it runs in the background when a card is first shown, and until
    ///     then the card shows its status as not checked yet.
    ///     </para>
    ///     <para>
    ///     Canceled (or the view model disposed) before the commit, nothing is saved or added. A
    ///     failed save asks the user through <see cref="AddSaveFailed"/> (see
    ///     <see cref="CommitAddBatchAsync"/>). The awaits resume on the caller's synchronization
    ///     context, which is the UI thread in the application, so every change to the list happens
    ///     there.
    ///     </para>
    /// </remarks>
    /// <param name="repoPaths">The folders' absolute paths, in the order to add them. Must not be
    ///     <see langword="null"/>.</param>
    /// <param name="groupName">The group to put the repos in, or <see langword="null"/>; when given it
    ///     must be a valid group name.</param>
    /// <param name="progress">Told how many folders have been checked, from 0 up to the total, or
    ///     <see langword="null"/>. Called on the thread the awaits resume on.</param>
    /// <param name="cancellationToken">Cancels the add before it saves.</param>
    /// <param name="onSaving">Called once the folders have been checked and the add is about to save,
    ///     or <see langword="null"/>; the strip shows "Saving..." from then on.</param>
    /// <returns>What became of each folder, or a canceled result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoPaths"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="groupName"/> is given but is
    ///     not a valid group name.</exception>
    internal async Task<AddReposBatchResult> AddReposAsync(
        IReadOnlyList<string> repoPaths,
        string? groupName,
        IProgress<AddReposProgress>? progress,
        CancellationToken cancellationToken,
        Action? onSaving = null)
    {
        ArgumentNullException.ThrowIfNull(repoPaths);
        if (groupName is not null)
        {
            ResolveAddTargetGroup(groupName);
        }

        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return new AddReposBatchResult([], Canceled: true);
        }

        // Plan on the UI thread: cheap, no file-system access
        var plan = PlanAddBatch(repoPaths);
        var total = plan.Folders.Count;
        progress?.Report(new AddReposProgress(0, total));

        // Read every folder in the background through the shared refresh queue (a couple at a time,
        // across everything else that reads repos); the results keep the request order
        var facts = new RepoFolderFacts[total];
        var completed = 0;
        var inspections = plan.Folders.Select(async (folder, index) =>
        {
            facts[index] = await _refreshQueue.EnqueueAsync(
                new RepoRefreshKey(NormalizeRepoPath(folder.Path), RepoRefreshKind.FolderInspection),
                RepoRefreshPriority.Visible,
                token => InspectFolder(folder.Path, folder.Listed, plan.GitExecutablePath, _gitTimeouts, token),
                reportsProgress: false,
                cancellationToken);
            progress?.Report(new AddReposProgress(Interlocked.Increment(ref completed), total));
        }).ToList();

        try
        {
            await Task.WhenAll(inspections);
        }
        catch (OperationCanceledException)
        {
            return new AddReposBatchResult([], Canceled: true);
        }

        // The last moment to stop: nothing has been saved or added yet
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return new AddReposBatchResult([], Canceled: true);
        }

        onSaving?.Invoke();
        return await CommitAddBatchAsync(facts, groupName);
    }

    /// <summary>
    ///     Commits a background add on the UI thread, asking the user what to do when the save fails.
    /// </summary>
    /// <remarks>
    ///     Each attempt re-checks the folders against the list as it is at that moment and saves
    ///     everything in one confirmed write (see <see cref="ApplyAddCommitAsync"/>), applying the add
    ///     only once it is on disk, so a failed save changed nothing. The user is then asked through
    ///     <see cref="AddSaveFailed"/>: Retry tries again and asks again each time that save fails (the
    ///     loop), Discard (or no one to ask, or a closed window) gives up and reports the folders that
    ///     would have been added or moved as not saved, with the reason. Memory, the cards and the
    ///     displayed list are then as they were before the add, including the groups, order and
    ///     collapsed state of repos the add would have moved; a change the user made meanwhile is kept
    ///     and written in the background as usual.
    /// </remarks>
    /// <param name="facts">What was read about each folder, in request order.</param>
    /// <param name="groupName">The requested group, or <see langword="null"/>.</param>
    /// <returns>What became of each folder.</returns>
    private async Task<AddReposBatchResult> CommitAddBatchAsync(IReadOnlyList<RepoFolderFacts> facts, string? groupName)
    {
        while (true)
        {
            // Re-resolved on every attempt: the groups may have changed while the user was asked
            var commit = PrepareAddCommit(facts, groupName is null ? null : ResolveAddTargetGroup(groupName));
            string problem;
            try
            {
                await ApplyAddCommitAsync(commit);
                return new AddReposBatchResult(commit.Outcomes, Canceled: false);
            }
            catch (InvalidOperationException ex)
            {
                problem = SecretRedactor.SafeMessage(ex);
            }

            // Ask whether to try again; nobody to ask (or a closed window) means Discard
            var request = new SettingsSaveRetryRequest(
                $"The repositories could not be added because the settings could not be saved. {problem}\n\n" +
                "Retry to save again, or Discard to leave the list as it is.");
            if (_disposed || AddSaveFailed is null)
            {
                request.Respond(false);
            }
            else
            {
                AddSaveFailed.Invoke(this, request);
            }

            if (!await request.Answer || _disposed)
            {
                return new AddReposBatchResult(commit.ToSaveFailedOutcomes(problem), Canceled: false);
            }
        }
    }

    /// <summary>
    ///     The shared single add behind <see cref="AddRepo"/> and <see cref="AddRepoToGroup"/>: one
    ///     folder through the batch steps, read inline.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root.</param>
    /// <param name="groupName">The already validated and canonical group to put the repo in, or
    ///     <see langword="null"/> for none (the top-bar add).</param>
    /// <returns>What became of the repo; never <see cref="AddRepoResult.SaveFailed"/> (a failed save of
    ///     an add throws; a failed save of a move is reported and gives
    ///     <see cref="AddRepoResult.MoveFailed"/>).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written for
    ///     a new repo; nothing was added.</exception>
    private AddRepoResult AddRepoCore(string repoPath, string? groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var plan = PlanAddBatch([repoPath]);
        var folder = plan.Folders[0];
        var commit = PrepareAddCommit(
            [InspectFolder(folder.Path, folder.Listed, plan.GitExecutablePath, _gitTimeouts, CancellationToken.None, readCheapFacts: false)], groupName);
        var result = commit.Outcomes[0].Result;
        if (result != AddRepoResult.MovedExisting)
        {
            // A failed save of a new repo throws, as a single add always did
            ApplyAddCommitNow(commit);
            return result;
        }

        // A repo that is already listed moved from a button the user pressed: a failed save is told
        try
        {
            ApplyAddCommitNow(commit);
            return result;
        }
        catch (InvalidOperationException ex)
        {
            ReportSaveFailure(ex);
            return AddRepoResult.MoveFailed;
        }
    }

    /// <summary>
    ///     Plans an add on the UI thread: drops blank and repeated paths, notes which folders are
    ///     already listed and which git executable to run, without touching the file system.
    /// </summary>
    /// <param name="repoPaths">The requested paths, in order.</param>
    /// <returns>The distinct, non-blank folders in request order, and the git executable.</returns>
    private AddBatchPlan PlanAddBatch(IEnumerable<string> repoPaths)
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in RepoCards.Select(card => card.RepoPath).Concat(_settings.RecentRepos.Select(repo => repo.Path)))
        {
            listed.Add(NormalizeRepoPath(path));
        }

        var folders = repoPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .DistinctBy(NormalizeRepoPath, StringComparer.OrdinalIgnoreCase)
            .Select(path => new PlannedFolder(path, listed.Contains(NormalizeRepoPath(path))))
            .ToList();
        return new AddBatchPlan(folders, RepoCardViewModel.ResolveGitExecutablePath(_settings, _startupOptions));
    }

    /// <summary>
    ///     Reads what an add needs to know about one folder from the file system and git; safe on any
    ///     thread (it touches no view-model state).
    /// </summary>
    /// <remarks>
    ///     A path that is not a folder is <see cref="RepoFolderKind.Missing"/>, or
    ///     <see cref="RepoFolderKind.NotAFolder"/> when it is a file; a folder is checked for being the
    ///     top folder of a Git repository (see <see cref="GitClient.IsRepositoryRoot"/>, file system
    ///     only). Only for a Git repository root that is not listed are the card's cheap facts read
    ///     (its pin, branch, <c>HEAD</c> and committed agent folders, see
    ///     <see cref="RepoCardViewModel.ReadCheapFacts"/>), so the new card needs no git call on the UI
    ///     thread. The working tree is not checked.
    /// </remarks>
    /// <param name="path">The folder's path, as given.</param>
    /// <param name="listed">Whether the repo was listed when the add was planned.</param>
    /// <param name="gitExecutablePath">The git executable to run.</param>
    /// <param name="gitTimeouts">The time limits of the git commands, or <see langword="null"/> for
    ///     <see cref="GitClientTimeouts.Default"/>.</param>
    /// <param name="cancellationToken">Stops a running git when signaled (the window closed).</param>
    /// <param name="readCheapFacts">Whether the new card's cheap facts are read here (with git);
    ///     <see langword="false"/> checks the folder only and leaves the facts to the card's own read
    ///     through the shared queue, so a caller on the UI thread runs no git.</param>
    /// <returns>The facts.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    internal static RepoFolderFacts InspectFolder(
        string path,
        bool listed,
        string gitExecutablePath,
        GitClientTimeouts? gitTimeouts = null,
        CancellationToken cancellationToken = default,
        bool readCheapFacts = true)
    {
        var normalized = NormalizeRepoPath(path);
        if (!Directory.Exists(path))
        {
            return new RepoFolderFacts(
                path, normalized, File.Exists(path) ? RepoFolderKind.NotAFolder : RepoFolderKind.Missing, listed, null);
        }

        if (!GitClient.IsRepositoryRoot(path))
        {
            return new RepoFolderFacts(path, normalized, RepoFolderKind.NotAGitRepositoryRoot, listed, null);
        }

        var cheap = listed || !readCheapFacts ? null : RepoCardViewModel.ReadCheapFacts(
            normalized, new GitClient(gitExecutablePath, timeouts: gitTimeouts), cache: null, cancellationToken);
        return new RepoFolderFacts(path, normalized, RepoFolderKind.GitRoot, listed, cheap);
    }

    /// <summary>
    ///     Decides, on the UI thread and against the list as it is now, what becomes of each folder of
    ///     an add, without changing anything.
    /// </summary>
    /// <remarks>
    ///     The checks, in order: a path that is not a folder is <see cref="AddRepoResult.Missing"/>, or
    ///     <see cref="AddRepoResult.NotAGitRepository"/> when it is a file (a dropped file); a repo that
    ///     is listed now (see <see cref="IsSameRepoPath"/>) is <see cref="AddRepoResult.AlreadyListed"/>
    ///     or, with a group and a card, moved into the group - it is never subject to the Git check, so
    ///     the repos already in the list are left as they are; a folder that is not the top folder of a
    ///     Git repository is <see cref="AddRepoResult.NotAGitRepository"/>; anything else is added,
    ///     stored in its normalized form (see <see cref="NormalizeRepoPath"/>). A folder added earlier
    ///     in the same batch counts as listed for the folders after it.
    /// </remarks>
    /// <param name="facts">What was read about each folder, in request order.</param>
    /// <param name="target">The canonical group to put the repos in, or <see langword="null"/>.</param>
    /// <returns>The outcomes and the steps that carry them out.</returns>
    private AddBatchCommit PrepareAddCommit(IReadOnlyList<RepoFolderFacts> facts, string? target)
    {
        // The list as it is now: the cards by normalized path, and every listed path
        var cards = new Dictionary<string, RepoCardViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in RepoCards)
        {
            cards.TryAdd(NormalizeRepoPath(card.RepoPath), card);
        }

        var listed = new HashSet<string>(cards.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var repo in _settings.RecentRepos)
        {
            listed.Add(NormalizeRepoPath(repo.Path));
        }

        var outcomes = new List<AddRepoOutcome>();
        var steps = new List<AddStep>();
        foreach (var folder in facts)
        {
            AddRepoResult result;
            if (folder.Kind == RepoFolderKind.Missing)
            {
                result = AddRepoResult.Missing;
            }
            else if (folder.Kind == RepoFolderKind.NotAFolder)
            {
                result = AddRepoResult.NotAGitRepository;
            }
            else if (listed.Contains(folder.NormalizedPath))
            {
                // Listed: moved into the group when there is one (and a card to move), else refused
                if (target is not null && cards.TryGetValue(folder.NormalizedPath, out var existing))
                {
                    steps.Add(new AddStep(null, null, existing));
                    result = AddRepoResult.MovedExisting;
                }
                else
                {
                    result = AddRepoResult.AlreadyListed;
                }
            }
            else if (folder.Kind != RepoFolderKind.GitRoot)
            {
                result = AddRepoResult.NotAGitRepository;
            }
            else
            {
                var pin = folder.Cheap?.Pin;
                var recentRepo = new RecentRepo
                {
                    Path = folder.NormalizedPath,
                    GroupName = target,
                    PinnedPackageName = pin?.PackageName,
                    PinnedPackageVersion = pin?.Version
                };
                steps.Add(new AddStep(recentRepo, folder.Cheap, null));
                listed.Add(folder.NormalizedPath);
                result = AddRepoResult.Added;
            }

            outcomes.Add(new AddRepoOutcome(folder.Path, result));
        }

        return new AddBatchCommit(target, outcomes, steps);
    }

    /// <summary>
    ///     Carries out a prepared add in the background: saves everything it changes in one confirmed
    ///     write and, only once that succeeded, applies it (see <see cref="AdoptAddCommit"/>).
    /// </summary>
    /// <remarks>
    ///     The candidate is a snapshot of the live settings with the add's change (see
    ///     <see cref="ApplyAddSteps"/>) applied, written by the background settings writer (see
    ///     <see cref="SaveConfirmedAsync"/>), so the window stays usable while it is written. A failed
    ///     save leaves memory, the cards and the displayed list exactly as they were. A batch that
    ///     changes nothing saves nothing.
    /// </remarks>
    /// <param name="commit">The prepared add.</param>
    /// <returns>A task that completes when the add was saved and applied.</returns>
    /// <exception cref="InvalidOperationException">Thrown (through the task) when the settings file
    ///     cannot be written; nothing changed.</exception>
    private Task ApplyAddCommitAsync(AddBatchCommit commit) =>
        commit.Steps.Count == 0
            ? Task.CompletedTask
            : SaveConfirmedAsync(
                settings => ApplyAddChange(settings, commit), () => AdoptAddCommit(commit), commit.Describe());

    /// <summary>
    ///     Carries out a prepared add synchronously, for the synchronous adds (<see cref="AddRepos"/>,
    ///     <see cref="AddRepo"/>, <see cref="AddRepoToGroup"/>): waits for the confirmed write, then
    ///     applies it (see <see cref="AdoptAddCommit"/>).
    /// </summary>
    /// <remarks>
    ///     The write still runs on a worker thread (the settings writer never needs the calling
    ///     thread, so waiting for it cannot deadlock); only these synchronous adds, which no window code
    ///     calls, wait for it. A failed save changed nothing.
    /// </remarks>
    /// <param name="commit">The prepared add.</param>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     nothing changed.</exception>
    private void ApplyAddCommitNow(AddBatchCommit commit)
    {
        if (commit.Steps.Count == 0)
        {
            return;
        }

        // A closed window saves nothing more: a write now could follow the close's own write
        ObjectDisposedException.ThrowIf(_disposed, this);
        _settingsWriter.SaveNowAsync(CreateAddCandidate(commit)).GetAwaiter().GetResult();
        AdoptAddCommit(commit);

        // A confirmed save that is still running was built without this add: write the result after it
        _saveDeferred |= _runningConfirmedSaves.Count > 0;
    }

    /// <summary>
    ///     Builds what an add saves: a snapshot of the live settings with the add's change applied and
    ///     the group colors and order repaired, leaving the live settings untouched.
    /// </summary>
    /// <param name="commit">The prepared add.</param>
    /// <returns>The candidate settings.</returns>
    private AppSettings CreateAddCandidate(AddBatchCommit commit) =>
        ApplyAddChange(_settings.CreateSnapshot(), commit);

    /// <summary>
    ///     Applies an add's change to settings that are not the live ones (a candidate or the snapshot
    ///     written at the close) and repairs their group colors and order.
    /// </summary>
    /// <param name="target">The settings to change.</param>
    /// <param name="commit">The prepared add.</param>
    /// <returns><paramref name="target"/>, changed.</returns>
    private static AppSettings ApplyAddChange(AppSettings target, AddBatchCommit commit)
    {
        ApplyAddSteps(target, commit);
        target.RepairGroupSettings();
        return target;
    }

    /// <summary>
    ///     Applies an add that was saved: changes the live settings the same way, creates the new
    ///     cards, puts the cards in the stored order, rebuilds the list once and starts the new repos'
    ///     upgrade checks.
    /// </summary>
    /// <remarks>
    ///     The settings change (see <see cref="ApplyAddSteps"/>) appends the new entries, moves the
    ///     listed repos into the group at the bottom of their section, and opens the group when
    ///     anything joined it. It runs against the live settings as they are now, so a change the user
    ///     made while the add was saved (a repo removed, a group renamed) is kept.
    /// </remarks>
    /// <param name="commit">The prepared add.</param>
    private void AdoptAddCommit(AddBatchCommit commit)
    {
        ApplyAddSteps(_settings, commit);
        _settings.RepairGroupSettings();

        // Saved: create the new cards from what was read in the background (a folder read before it
        // was listed has facts; one whose facts are missing is refreshed in the background)
        var newCards = new List<RepoCardViewModel>();
        foreach (var step in commit.Steps.Where(step => step.NewRepo is not null))
        {
            var card = CreateCard(step.NewRepo!);
            if (step.Cheap is { } cheap)
            {
                card.ApplyCheapFacts(cheap);
            }
            else
            {
                _ = card.RefreshRepoStateAsync(
                    RepoRefreshPriority.Visible, cheapFacts: true, workingTree: false, cancellationToken: CancellationToken.None);
            }

            AttachCardEvents(card);
            newCards.Add(card);
        }

        // The cards follow the stored order, which the change set
        var byPath = new Dictionary<string, RepoCardViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in RepoCards.Concat(newCards))
        {
            byPath.TryAdd(card.RepoPath, card);
        }

        var ordered = _settings.RecentRepos
            .Select(repo => byPath.GetValueOrDefault(repo.Path))
            .OfType<RepoCardViewModel>()
            .Distinct()
            .ToList();
        SyncCollection(RepoCards, ordered);

        // The moved cards' group changed on their shared entries: tell their views, then rebuild once
        foreach (var step in commit.Steps.Where(step => step.MovedCard is not null))
        {
            step.MovedCard!.RaiseGroupPropertiesChanged();
        }

        UpdateDisplayedRepoCards();

        // The new cards' upgrade checks read package sources, so they run in the background
        BeginCardsUpgradeRefresh(newCards);
    }

    /// <summary>
    ///     The settings change of an add, run by <see cref="CommitSettingsChange"/> on the candidate and
    ///     then on the live settings; deterministic, as that requires.
    /// </summary>
    /// <remarks>
    ///     In request order: a new repo's entry is appended; a listed repo's entry gets the group and is
    ///     moved to the bottom of its section (after the section's last other entry, or the end of the
    ///     list when the section has no other entry), which makes the order the user's own. When anything
    ///     joined the group, the group is removed from the collapsed sections so the repos are visible.
    /// </remarks>
    /// <param name="settings">The settings to change.</param>
    /// <param name="commit">The prepared add.</param>
    private static void ApplyAddSteps(AppSettings settings, AddBatchCommit commit)
    {
        var joinedGroup = false;
        foreach (var step in commit.Steps)
        {
            if (step.NewRepo is { } newRepo)
            {
                settings.RecentRepos.Add(newRepo);
                joinedGroup |= commit.Target is not null;
                continue;
            }

            var entry = settings.RecentRepos.FirstOrDefault(
                repo => string.Equals(repo.Path, step.MovedCard!.RepoPath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                continue;
            }

            entry.GroupName = commit.Target;
            if (MoveToSectionEnd(settings.RecentRepos, entry))
            {
                settings.HasUserRepoOrder = true;
            }

            joinedGroup = true;
        }

        if (joinedGroup && commit.Target is { } target)
        {
            settings.CollapsedGroups?.RemoveAll(name => RepoGroupName.Comparer.Equals(name, target));
        }
    }

    /// <summary>
    ///     Moves an entry to the bottom of its section in a repo list: after the last other entry of the
    ///     same section, or to the end of the list when the section has no other entry. The list
    ///     instance is kept (the settings share it by reference).
    /// </summary>
    /// <param name="repos">The repo list.</param>
    /// <param name="entry">The entry to move; it is in <paramref name="repos"/>.</param>
    /// <returns><see langword="true"/> when the order changed.</returns>
    private static bool MoveToSectionEnd(List<RecentRepo> repos, RecentRepo entry)
    {
        var section = GetSectionOf(entry);
        var ordered = repos.Where(other => !ReferenceEquals(other, entry)).ToList();
        var index = ordered.FindLastIndex(other =>
            GetSectionOf(other) is var otherSection &&
            otherSection.Kind == section.Kind &&
            RepoGroupName.Comparer.Equals(otherSection.Name, section.Name)) + 1;
        if (index == 0)
        {
            index = ordered.Count;
        }

        ordered.Insert(index, entry);
        if (ordered.SequenceEqual(repos, ReferenceEqualityComparer.Instance))
        {
            return false;
        }

        repos.Clear();
        repos.AddRange(ordered);
        return true;
    }

    /// <summary>
    ///     Gets the section a repo entry is listed in, by the same rule as
    ///     <see cref="GetSectionOf(RepoCardViewModel)"/> for its card.
    /// </summary>
    /// <param name="repo">The entry.</param>
    /// <returns>The section's kind and display name.</returns>
    private static (RepoGroupKind Kind, string Name) GetSectionOf(RecentRepo repo)
    {
        if (repo.IsFavorite)
        {
            return (RepoGroupKind.Favorites, RepoGroupName.FavoritesName);
        }

        return RepoGroupName.Sanitize(repo.GroupName) is { } group
            ? (RepoGroupKind.Group, group)
            : (RepoGroupKind.Ungrouped, RepoGroupName.UngroupedName);
    }

    /// <summary>
    ///     One folder of a planned add.
    /// </summary>
    /// <param name="Path">The folder's path, as given.</param>
    /// <param name="Listed">Whether the repo was listed when the add was planned.</param>
    private sealed record PlannedFolder(string Path, bool Listed);

    /// <summary>
    ///     A planned add: its folders and the git executable to read them with.
    /// </summary>
    /// <param name="Folders">The distinct, non-blank folders in request order.</param>
    /// <param name="GitExecutablePath">The git executable.</param>
    private sealed record AddBatchPlan(IReadOnlyList<PlannedFolder> Folders, string GitExecutablePath);

    /// <summary>
    ///     One change a commit makes: a new repo (with the facts read for its card) or a listed repo
    ///     moved into the group.
    /// </summary>
    /// <param name="NewRepo">The new entry, or <see langword="null"/> for a move.</param>
    /// <param name="Cheap">The new card's facts, or <see langword="null"/> when not read.</param>
    /// <param name="MovedCard">The card of the listed repo to move, or <see langword="null"/> for an
    ///     add.</param>
    private sealed record AddStep(RecentRepo? NewRepo, RepoCheapFacts? Cheap, RepoCardViewModel? MovedCard);

    /// <summary>
    ///     A prepared add: the outcomes it gives and the steps that carry them out.
    /// </summary>
    /// <param name="Target">The canonical group, or <see langword="null"/>.</param>
    /// <param name="Outcomes">One outcome per folder, in request order.</param>
    /// <param name="Steps">The changes, in request order.</param>
    private sealed record AddBatchCommit(string? Target, IReadOnlyList<AddRepoOutcome> Outcomes, IReadOnlyList<AddStep> Steps)
    {
        /// <summary>
        ///     Gets the outcomes when the save failed: every folder that would have been added or moved
        ///     is <see cref="AddRepoResult.SaveFailed"/> with the reason; the others keep their outcome.
        /// </summary>
        /// <param name="problem">Why the settings could not be saved, safe to show.</param>
        /// <returns>The outcomes.</returns>
        public IReadOnlyList<AddRepoOutcome> ToSaveFailedOutcomes(string problem) =>
        [
            .. Outcomes.Select(outcome => outcome.Result is AddRepoResult.Added or AddRepoResult.MovedExisting
                ? new AddRepoOutcome(outcome.Path, AddRepoResult.SaveFailed, problem)
                : outcome)
        ];

        /// <summary>
        ///     Describes what the add changes, for the log of a close that could not save it.
        /// </summary>
        /// <remarks>
        ///     Reads the moved cards' paths, so it is called on the UI thread.
        /// </remarks>
        /// <returns>The repos it adds and moves, and the group it puts them in.</returns>
        public string Describe()
        {
            var added = Steps.Where(step => step.NewRepo is not null).Select(step => step.NewRepo!.Path).ToList();
            var moved = Steps.Where(step => step.MovedCard is not null).Select(step => step.MovedCard!.RepoPath).ToList();
            var parts = new List<string>();
            if (added.Count > 0)
            {
                parts.Add("adding " + string.Join(", ", added));
            }

            if (moved.Count > 0)
            {
                parts.Add("moving " + string.Join(", ", moved));
            }

            var group = Target is null ? string.Empty : $" into group '{Target}'";
            return $"the add ({string.Join(" and ", parts)}){group}";
        }
    }

    /// <summary>
    ///     The adds that run one after the other under one progress operation and one Cancel.
    /// </summary>
    private sealed class AddQueueSession
    {
        /// <summary>
        ///     Whether the session has ended.
        /// </summary>
        private bool _ended;

        /// <summary>
        ///     Initializes a session and shows its progress operation.
        /// </summary>
        /// <param name="owner">The view model whose strip shows it.</param>
        public AddQueueSession(MainWindowViewModel owner)
        {
            Progress = owner.GlobalProgress.Begin("Adding repositories...", Cancel);
        }

        /// <summary>
        ///     Gets the cancellation shared by the session's adds.
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>
        ///     Gets the session's progress operation.
        /// </summary>
        public GlobalProgressOperation Progress { get; }

        /// <summary>
        ///     Gets or sets how many of the session's adds have not ended yet.
        /// </summary>
        public int Pending { get; set; }

        /// <summary>
        ///     Cancels the session's adds (once it has ended, nothing is left to cancel).
        /// </summary>
        public void Cancel()
        {
            if (!_ended)
            {
                Cancellation.Cancel();
            }
        }

        /// <summary>
        ///     Ends the session: removes its progress operation and releases its cancellation.
        /// </summary>
        public void End()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            Progress.Dispose();
            Cancellation.Dispose();
        }
    }

    /// <summary>
    ///     An <see cref="IProgress{T}"/> that reports on the calling thread at once, unlike
    ///     <see cref="Progress{T}"/>, which posts; the add reports from its UI-thread continuations, so
    ///     the strip is updated in step with the work.
    /// </summary>
    /// <typeparam name="T">The report type.</typeparam>
    /// <param name="report">What to do with each report.</param>
    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        /// <inheritdoc />
        public void Report(T value) => report(value);
    }

    /// <summary>
    ///     The most folder names one category of <see cref="DescribeAddOutcomes"/>'s summary lists
    ///     before it says how many more there are, so the message box stays a manageable size.
    /// </summary>
    private const int MaxNamesPerSummaryLine = 5;

    /// <summary>
    ///     Words what the user needs to know about an add: nothing when everything was added, the
    ///     single-add message for one folder, or one combined summary for several.
    /// </summary>
    /// <remarks>
    ///     A single folder gets the same messages a single add always gave, with the reason made
    ///     specific (already listed, missing, not a Git repository, could not be saved, moved into the
    ///     group); an added folder, or one whose move failed (already reported), needs no message.
    ///     Several folders get one message, so a drop of many folders never opens a box per folder:
    ///     <c>Added N of M folders.</c> followed by one line per kind of outcome that occurred, each
    ///     naming the folders (at most five, then how many more). Pure.
    /// </remarks>
    /// <param name="outcomes">What became of each folder (see <see cref="AddRepos"/>).</param>
    /// <param name="groupName">The group the folders were added to, or <see langword="null"/>.</param>
    /// <returns>The message, or <see langword="null"/> when there is nothing to tell.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcomes"/> is
    ///     <see langword="null"/>.</exception>
    internal static string? DescribeAddOutcomes(IReadOnlyList<AddRepoOutcome> outcomes, string? groupName)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        // One folder: the single-add messages
        if (outcomes.Count == 1)
        {
            var (path, result, saveProblem) = outcomes[0];
            return result switch
            {
                AddRepoResult.MovedExisting =>
                    $"'{path}' is already in the list, so it was moved into the group \"{groupName}\".",
                AddRepoResult.AlreadyListed => $"'{path}' could not be added: it is already in the list.",
                AddRepoResult.Missing => $"'{path}' could not be added: it does not exist.",
                AddRepoResult.NotAGitRepository =>
                    $"'{path}' could not be added: it is not the top folder of a Git repository " +
                    "(it has no .git folder or file).",
                AddRepoResult.SaveFailed => $"'{path}' could not be added. {saveProblem}",
                _ => null
            };
        }

        // Several folders: silent when all were added, otherwise one summary by kind of outcome
        if (outcomes.All(outcome => outcome.Result == AddRepoResult.Added))
        {
            return null;
        }

        var added = outcomes.Count(outcome => outcome.Result == AddRepoResult.Added);
        var lines = new List<string> { $"Added {added} of {outcomes.Count} folders." };
        AddSummaryLine(lines, outcomes, $"Already in the list, moved into \"{groupName}\"", AddRepoResult.MovedExisting);
        AddSummaryLine(lines, outcomes, "Already in the list", AddRepoResult.AlreadyListed);
        AddSummaryLine(
            lines, outcomes, "Not the top folder of a Git repository (no .git folder or file)", AddRepoResult.NotAGitRepository);
        AddSummaryLine(lines, outcomes, "Not found", AddRepoResult.Missing);
        AddSummaryLine(lines, outcomes, "Not saved", AddRepoResult.SaveFailed, AddRepoResult.MoveFailed);
        var problem = outcomes.FirstOrDefault(outcome => outcome.Problem is not null)?.Problem;
        if (problem is not null)
        {
            lines.Add(problem);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    ///     Adds one line of the multi-folder summary, naming the folders that had one of the given
    ///     outcomes, when there are any.
    /// </summary>
    /// <param name="lines">The summary's lines so far.</param>
    /// <param name="outcomes">Every folder's outcome.</param>
    /// <param name="caption">The line's caption, for example <c>Not found</c>.</param>
    /// <param name="results">The outcomes the line covers.</param>
    private static void AddSummaryLine(
        List<string> lines, IReadOnlyList<AddRepoOutcome> outcomes, string caption, params AddRepoResult[] results)
    {
        var names = outcomes
            .Where(outcome => results.Contains(outcome.Result))
            .Select(outcome => GetFolderDisplayName(outcome.Path))
            .ToList();
        if (names.Count == 0)
        {
            return;
        }

        var shown = string.Join(", ", names.Take(MaxNamesPerSummaryLine));
        var more = names.Count > MaxNamesPerSummaryLine ? $" and {names.Count - MaxNamesPerSummaryLine} more" : string.Empty;
        lines.Add($"{caption}: {shown}{more}.");
    }

    /// <summary>
    ///     Gets the short name a summary uses for a folder: its last path segment, or the whole path
    ///     when it has none (a drive root).
    /// </summary>
    /// <param name="path">The folder's path.</param>
    /// <returns>The display name.</returns>
    private static string GetFolderDisplayName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>
    ///     Gets the form of a repo path that identifies its folder: the full path without a trailing
    ///     directory separator, while a root (such as <c>C:\</c> or <c>/</c>) keeps the separator it
    ///     needs.
    /// </summary>
    /// <remarks>
    ///     New entries are stored in this form; entries saved before are left as they were and are
    ///     matched through <see cref="IsSameRepoPath"/>. Performs no file-system I/O. A path the
    ///     platform cannot make full (an invalid path) is returned as given, so it is still compared
    ///     and later reported as missing rather than throwing.
    /// </remarks>
    /// <param name="path">The repo path.</param>
    /// <returns>The normalized path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is
    ///     <see langword="null"/>.</exception>
    internal static string NormalizeRepoPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>
    ///     Tells whether two repo paths name the same folder: their normalized forms (see
    ///     <see cref="NormalizeRepoPath"/>) are equal ignoring letter case, so a folder written with
    ///     and without a trailing directory separator, or in another letter case, is one repo.
    /// </summary>
    /// <param name="first">One repo path.</param>
    /// <param name="second">The other repo path.</param>
    /// <returns><see langword="true"/> when both paths name the same folder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either path is
    ///     <see langword="null"/>.</exception>
    internal static bool IsSameRepoPath(string first, string second) =>
        string.Equals(NormalizeRepoPath(first), NormalizeRepoPath(second), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Removes a repo card from the recent-repos list, unconditionally, and persists the
    ///     updated settings.
    /// </summary>
    /// <remarks>
    ///     This method itself performs no confirmation - per the plan's event-based
    ///     view/view-model separation, <c>MainWindow</c>'s code-behind is responsible for showing
    ///     a confirmation dialog (via <c>ConfirmationWindow</c>) before calling this, in response
    ///     to <paramref name="card"/> raising <see cref="RepoCardViewModel.RemoveRequested"/>.
    ///     The removal is applied at once and saved in the background (see
    ///     <see cref="CommitSettingsChange"/>); the removed card is retired (see
    ///     <see cref="RepoCardViewModel.Retire"/>) so nothing it still has running can report back.
    /// </remarks>
    /// <param name="card">The card to remove.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="card"/> is
    ///     <see langword="null"/>.</exception>
    public void RemoveRepo(RepoCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        CommitSettingsChange(settings => settings.RecentRepos.RemoveAll(
            r => string.Equals(r.Path, card.RepoPath, StringComparison.OrdinalIgnoreCase)));
        RepoCards.Remove(card);
        card.Retire();

        UpdateDisplayedRepoCards();
    }

    /// <summary>
    ///     Replaces the current settings with <paramref name="updated"/> (preserving the
    ///     repo list and its order, the collapsed sections and the group colors and order, which are
    ///     only ever changed from the main window, e.g. via <see cref="AddRepo"/>/
    ///     <see cref="RemoveRepo"/>/<see cref="MoveCard"/>/<see cref="DropCard"/>/<see cref="SetGroupColor"/>/<see cref="MoveGroup"/>),
    ///     persists them in the background (adopting them only once that succeeded), invalidates the shared
    ///     <see cref="PackageVersionCache"/> for each package source that was changed or removed
    ///     (cached results for an edited source would otherwise be silently reused; sources that
    ///     did not change keep theirs), and
    ///     starts re-running each card's cheap refresh (pin file, branch, committed-files badge,
    ///     whether the pin's source is configured) in the background so those properties reflect any
    ///     changed package-source/git-path settings, then the background upgrade-availability check
    ///     (<see cref="BeginUpgradeRefresh()"/>) against the new sources.
    /// </summary>
    /// <remarks>
    ///     The save is a confirmed write made by the background settings writer on a worker thread (see
    ///     <see cref="SaveConfirmedAsync"/>), so the window is not frozen while the file is written; the
    ///     returned task completes once it was saved and adopted. A change made in the main window
    ///     meanwhile is kept and written after it. The card refresh runs in the
    ///     background through the shared refresh queue (see <see cref="RefreshCardsAfterSettingsAsync"/>),
    ///     reading git off the UI thread and showing "Refreshing repositories {n} of {total}..." in
    ///     <see cref="GlobalProgress"/>, so the
    ///     window stays usable however many repos are listed. <see cref="PendingRefresh"/> (and
    ///     <see cref="RecheckAfterSettingsClosed"/> after a save) completes when the refresh and the
    ///     upgrade check that follows it have finished.
    ///     Deliberately refreshes only the cards' cheap facts rather than running a full
    ///     <see cref="RepoCardViewModel.RefreshAsync"/> for every card: the
    ///     <c>git status</c> call scans the whole working tree, so refreshing every tracked repo's
    ///     dirty status could take long for a large or slow/networked
    ///     repo list. A fresh Git status check is instead requested only for the cards that
    ///     actually need one - those that reappeared (were missing, now present) since they were
    ///     last checked (unlike a newly-constructed card, an already-realized card's one-time
    ///     lazy <see cref="RepoCardViewModel.RefreshDirtyStatusInBackgroundAsync"/> trigger in
    ///     <c>MainWindow.axaml.cs</c> will not fire again for it), or every present card when
    ///     <see cref="AppSettings.GitExecutablePath"/> itself changed - a previously-cached
    ///     <see cref="RepoCardViewModel.CanPull"/>/<see cref="RepoCardViewModel.PullTooltip"/>
    ///     computed against the old executable could otherwise keep being shown even though
    ///     subsequent Pull operations use the newly configured one.
    /// </remarks>
    /// <param name="updated">The updated settings, typically built by
    ///     <see cref="SettingsWindowViewModel.SaveCommand"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updated"/> is
    ///     <see langword="null"/>.</exception>
    /// <returns>A task that completes when the settings were saved and adopted.</returns>
    /// <exception cref="InvalidOperationException">Thrown, before anything is saved, when a source
    ///     location embeds a credential; thrown through the task when the settings file cannot be
    ///     written. Either way the current settings are left unchanged (the candidate is adopted only
    ///     after it has been persisted).</exception>
    public Task ApplySettingsAsync(AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        // Refuse a credential-bearing source before anything is saved or adopted, so the rejected
        // values never reach the file or the live in-memory settings
        foreach (var source in updated.GetEffectiveSources())
        {
            var problem = source.GetCredentialProblem();
            if (problem is not null)
            {
                throw new InvalidOperationException($"Settings were not applied. {problem}");
            }
        }

        // The candidate: the new settings with the user's repos and organizing choices as they are
        // now, as a snapshot the writer can write while the window keeps running. Written in the
        // background; adopted only once it is on disk, so a failed write (thrown through the task)
        // leaves the live settings as they were and a later save can never write the rejected values
        return SaveConfirmedAsync(
            settings => WithOrganizingFrom(updated, settings), () => AdoptSettings(updated), "the Settings window's settings");
    }

    /// <summary>
    ///     Builds what a Settings save writes: a snapshot of the new settings with the repos and
    ///     organizing choices of other settings (the live ones, or the snapshot written at the close).
    /// </summary>
    /// <param name="updated">The new settings from the Settings window; left untouched.</param>
    /// <param name="organizing">A snapshot holding the repos and organizing choices to keep; nobody
    ///     changes it any more.</param>
    /// <returns>The settings to write.</returns>
    private static AppSettings WithOrganizingFrom(AppSettings updated, AppSettings organizing)
    {
        var candidate = updated.CreateSnapshot();
        candidate.RecentRepos = organizing.RecentRepos;
        candidate.HasUserRepoOrder = organizing.HasUserRepoOrder;
        candidate.CollapsedGroups = organizing.CollapsedGroups;
        candidate.GroupColors = organizing.GroupColors;
        candidate.GroupOrder = organizing.GroupOrder;
        return candidate;
    }

    /// <summary>
    ///     Adopts settings from the Settings window once they were saved: keeps the live repo list and
    ///     organizing choices, applies the theme, drops the cached results of changed sources and
    ///     starts the cards' refresh and the upgrade check (see <see cref="ApplySettingsAsync"/>).
    /// </summary>
    /// <param name="updated">The settings that were saved.</param>
    private void AdoptSettings(AppSettings updated)
    {
        var gitExecutablePathChanged = !string.Equals(
            _settings.GitExecutablePath, updated.GitExecutablePath, StringComparison.Ordinal);

        // Note which sources were configured before the swap so cached results for any that were
        // edited or removed can be dropped afterwards
        var previousSources = _settings.GetEffectiveSources().ToList();

        // The live repo list and organizing choices are kept, including a change made while the
        // settings were saved (it is written by the save scheduled after this, see SaveConfirmedAsync)
        updated.RecentRepos = _settings.RecentRepos;
        updated.HasUserRepoOrder = _settings.HasUserRepoOrder;
        updated.CollapsedGroups = _settings.CollapsedGroups;
        updated.GroupColors = _settings.GroupColors;
        updated.GroupOrder = _settings.GroupOrder;
        _settings = updated;
        OnPropertyChanged(nameof(Settings));

        // The new settings are saved: make the theme choice take effect straight away, without a
        // restart. A rejected save never reaches here, so a refused value is never applied.
        _applyTheme?.Invoke(updated.Theme);

        InvalidateChangedSources(previousSources, updated.GetEffectiveSources());

        // Every check still running was started for the previous settings: this refresh, and the
        // upgrade check it starts when it is done, supersede them
        var previousRefresh = _refreshCancellation;
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        if (previousRefresh is not null)
        {
            previousRefresh.Cancel();
            previousRefresh.Dispose();
        }

        CancelPeriodicRecheck();

        // The cards are refreshed in the background (git runs for each), showing progress; the
        // upgrade check against the new sources follows
        var generation = ++_settingsRefreshGeneration;
        PendingRefresh = RefreshCardsAfterSettingsAsync(generation, gitExecutablePathChanged, cancellation.Token);
    }
    /// <summary>
    ///     Refreshes every card after the settings were saved - its cheap checks and, where needed, its
    ///     working-tree check - through the shared refresh queue, then rebuilds the list once and starts
    ///     the upgrade check against the new sources.
    /// </summary>
    /// <remarks>
    ///     Each card's cheap facts are queued at background priority in display order (the cards shown
    ///     first, then the collapsed and filtered-out ones) and counted in the queue's progress wave, so
    ///     the strip shows "Refreshing repositories {n} of {total}..." without Cancel (the refresh only
    ///     reads) and the window stays usable however many repos are listed; the queue runs only a
    ///     couple of reads at a time across everything that reads repos. A card whose folder came back,
    ///     or every present card when the git executable changed, also gets a fresh working-tree check
    ///     (see <see cref="ApplySettingsAsync"/>). Counted as a launch-style check while it runs, so the
    ///     periodic round does not start meanwhile. A newer <see cref="ApplySettingsAsync"/> supersedes it:
    ///     the superseded one neither rebuilds the list nor starts the upgrade check (the card-level
    ///     generations already make the newest read win). <see cref="Dispose"/> cancels it.
    /// </remarks>
    /// <param name="generation">This refresh's number (see <see cref="_settingsRefreshGeneration"/>).</param>
    /// <param name="gitExecutablePathChanged">Whether the git executable setting changed.</param>
    /// <param name="cancellationToken">Gives up the reads that have not started.</param>
    /// <returns>A task that completes when the cards were refreshed and the upgrade check that
    ///     follows has finished; cancellation is not an error.</returns>
    private async Task RefreshCardsAfterSettingsAsync(
        int generation, bool gitExecutablePathChanged, CancellationToken cancellationToken)
    {
        // Display order: the cards on screen first, then the rest
        var cards = DisplayedRepoCards.Concat(RepoCards).Distinct().ToList();
        Interlocked.Increment(ref _launchChecksInFlight);
        try
        {
            var refreshes = cards.Select(async card =>
            {
                var wasMissing = card.IsMissing;
                await card.RefreshRepoStateAsync(
                    RepoRefreshPriority.Background,
                    cheapFacts: true,
                    workingTree: false,
                    reportsProgress: true,
                    cancellationToken);
                if (!cancellationToken.IsCancellationRequested && !card.IsMissing && (wasMissing || gitExecutablePathChanged))
                {
                    await card.RefreshRepoStateAsync(
                        RepoRefreshPriority.Background,
                        cheapFacts: false,
                        workingTree: true,
                        cancellationToken: cancellationToken);
                }
            }).ToList();

            await Task.WhenAll(refreshes);
        }
        finally
        {
            Interlocked.Decrement(ref _launchChecksInFlight);
        }

        // A newer save took over (it rebuilds and checks itself), or the window closed
        if (_disposed || cancellationToken.IsCancellationRequested || generation != _settingsRefreshGeneration)
        {
            return;
        }

        UpdateDisplayedRepoCards();

        // Re-check upgrades against the new sources in the background
        await BeginUpgradeRefresh();
    }

    /// <summary>
    ///     Shows the shared refresh queue's current wave of counted reads in
    ///     <see cref="GlobalProgress"/>: "Refreshing repositories {n} of {total}..." without Cancel while
    ///     the wave runs, and nothing once it ended.
    /// </summary>
    /// <remarks>
    ///     The queue posts its progress to the UI thread's synchronization context in the application
    ///     (it was created there), so the global progress is only changed on the UI thread. A closed
    ///     window shows nothing more.
    /// </remarks>
    /// <param name="sender">The queue.</param>
    /// <param name="progress">The wave's progress.</param>
    private void OnRefreshQueueProgressChanged(object? sender, RepoRefreshProgress progress)
    {
        if (!progress.IsActive || _disposed)
        {
            _refreshProgress?.Dispose();
            _refreshProgress = null;
            return;
        }

        var text = $"Refreshing repositories {progress.Completed} of {progress.Total}...";
        _refreshProgress ??= GlobalProgress.Begin(text);
        _refreshProgress.Report(progress.Completed, progress.Total, text);
    }

    /// <summary>
    ///     Gets the task of the most recently started background upgrade check, so a caller (a
    ///     test, or code that must wait for the cards to settle) can await it; completed when no
    ///     check has been started.
    /// </summary>
    internal Task PendingRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Starts checking, in the background and without blocking the UI thread, whether a newer
    ///     package version is available for every repo card, canceling any such check still
    ///     running.
    /// </summary>
    /// <remarks>
    ///     Called by the main window once it is shown, and after settings change. Each card reads
    ///     only the source its pin names; at most a few cards read sources at the same time so a
    ///     long recent-repos list cannot flood a slow source. A failing source only affects the
    ///     cards pinned to it. A card busy with an action of its own when its turn comes is skipped
    ///     (see <see cref="CheckUpgradesAsync"/>).
    /// </remarks>
    /// <returns>A task that completes when every card's check has finished (also available as
    ///     <see cref="PendingRefresh"/>).</returns>
    public Task BeginUpgradeRefresh() => StartUpgradeRefresh([.. RepoCards]);

    /// <summary>
    ///     Starts the background upgrade check for the cards of repos that were just added, without
    ///     canceling the check of the other cards.
    /// </summary>
    /// <param name="cards">The cards to check; nothing starts for none.</param>
    private void BeginCardsUpgradeRefresh(IReadOnlyList<RepoCardViewModel> cards)
    {
        // A disposed view model (the window closed) starts no check
        if (_disposed || cards.Count == 0)
        {
            return;
        }

        var token = _refreshCancellation?.Token ?? CancellationToken.None;
        var previous = PendingRefresh;
        PendingRefresh = Task.WhenAll(previous, CheckUpgradesAsync(cards, token));
    }

    /// <summary>
    ///     Checks again, in the background, the repo cards that show a package source problem or a
    ///     failed upgrade check, once the Settings window has closed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     While the Settings window is open the user may sign in to a source with
    ///     <b>Test connection</b>, or the network may have come back; without this the cards would
    ///     keep showing "Sign-in needed" (or a failed check) until the next periodic round. When the
    ///     settings were saved, <see cref="ApplySettingsAsync"/> has already started the check of every
    ///     card, so nothing more is started and the pending check is returned - the cards are never
    ///     checked twice for one Settings session.
    ///     </para>
    ///     <para>
    ///     Otherwise only the cards that a periodic round would check (not busy, folder present, a
    ///     pin) and that show a source problem or a failed check, or whose background check is
    ///     reading a source at this moment, are checked; a card whose state is already known reads
    ///     no source. A card in the middle of a background check is included because its read began
    ///     before the sign-in: if that read fails it would put "Sign-in needed" on the card when it
    ///     finishes, so a newer check of the card is started and, the newest check of a card being
    ///     the one whose result is shown, the older one's result is ignored. A card that becomes busy
    ///     with an action of its own while it waits for its turn is skipped when the turn comes (see
    ///     <see cref="CheckUpgradesAsync"/>). The check is the
    ///     background one: it never asks the user to sign in, and it uses the credentials of this
    ///     session's <see cref="PackageCredentialContext"/> - the same store the connection test
    ///     signs in to - so a sign-in typed during the test is used. Failures are never cached, so
    ///     each of these cards reads its source again. Like the check of an added repo it does not
    ///     cancel a check that is running, it is canceled by a later full check or by
    ///     <see cref="Dispose"/>, and it holds off the periodic round while it runs. Must be called
    ///     on the UI thread.
    ///     </para>
    ///     <para>
    ///     A check that is running (a periodic round, or a launch-style check) may have recorded a
    ///     source failure in its <see cref="SourceFailureMemo"/> before the sign-in; it is made to
    ///     forget it (see <see cref="ForgetRecordedSourceFailures"/>), even when no card needs this
    ///     re-check, so the cards it checks from now on read the source again instead of being given
    ///     the old failure - which, as the newer check of the card, would replace what this re-check
    ///     shows. A read that is still in flight cannot record its failure afterwards either (the
    ///     memo moves to a new generation when it is cleared). The package-version cache is not
    ///     cleared: it never holds a failure, so the source behind a problem has no listing to
    ///     serve, and the listings it holds for sources that answered are within their five
    ///     minutes, as for any background check.
    ///     </para>
    /// </remarks>
    /// <param name="settingsSaved"><see langword="true"/> when the Settings window saved the
    ///     settings (its <see cref="SettingsWindowViewModel.Saved"/> event was raised) before it
    ///     closed; <see langword="false"/> when it was canceled or closed without saving.</param>
    /// <returns>A task that completes when the checks it started (or, after a save, the check the
    ///     save started) have finished; already completed when this view model was disposed or no
    ///     card needs checking. Cancellation is not an error. The check is also merged into
    ///     <see cref="PendingRefresh"/>.</returns>
    public Task RecheckAfterSettingsClosed(bool settingsSaved)
    {
        // A disposed view model (the window closed) starts no check
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        // Saving already re-checked every card against the saved settings: do not do it twice
        if (settingsSaved)
        {
            return PendingRefresh;
        }

        // A check still running may have recorded a failure before the sign-in (or before the
        // connection came back): it must read those sources again rather than put that failure
        // back on a card this re-check clears
        ForgetRecordedSourceFailures();

        // Only the cards whose shown state a sign-in or a returned connection could change: those
        // showing a problem or a failed check, and those a running background check is reading
        // for - such a read began before the sign-in and its failure must not land on the card, so
        // a newer check of the card is started and the older one's result is ignored
        var cards = RepoCards
            .Where(card => CanRecheckUpgrade(card)
                && (card.HasSourceProblem || card.IsUpgradeStatusUnknown || card.IsCheckingUpgrade))
            .ToList();
        if (cards.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Run alongside any check that is running, as the check of an added repo does
        var token = _refreshCancellation?.Token ?? CancellationToken.None;
        var check = CheckUpgradesAsync(cards, token);
        PendingRefresh = Task.WhenAll(PendingRefresh, check);
        return check;
    }

    /// <summary>
    ///     Replaces the running background upgrade check with a new one for the given cards.
    /// </summary>
    /// <param name="cards">The cards to check.</param>
    /// <returns>A task that completes when the new check has finished.</returns>
    private Task StartUpgradeRefresh(IReadOnlyList<RepoCardViewModel> cards)
    {
        var previous = _refreshCancellation;
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        // A periodic re-check still running was started for the previous settings: this check
        // supersedes it
        CancelPeriodicRecheck();

        PendingRefresh = CheckUpgradesAsync(cards, cancellation.Token);
        return PendingRefresh;
    }

    /// <summary>
    ///     Gets the task of the most recently started periodic upgrade re-check round, so a caller
    ///     (a test) can await it; completed when none has been started.
    /// </summary>
    internal Task PendingPeriodicRecheck { get; private set; } = Task.CompletedTask;

    /// <summary>
    ///     Starts re-checking every card's upgrade availability each
    ///     <see cref="DefaultUpgradeRecheckInterval"/> (or the interval given to the constructor) for
    ///     as long as the application runs, until <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Users leave the application open all day, and packages are published meanwhile, so the
    ///     upgrade badges would otherwise show only what existed when the window opened. Each round
    ///     is the same check as the one at launch (<see cref="RepoCardViewModel.RefreshUpgradeStatusAsync"/>
    ///     without a sign-in prompt): a server that needs a sign-in is reported on the card, never by
    ///     a dialog, and any other failure is shown the way the launch-time check shows it.
    ///     </para>
    ///     <para>
    ///     A round re-reads every source once: it clears the package-version cache first, so it is
    ///     not answered by a listing from the previous round that has not expired yet, and then the
    ///     cards that pin the same package are checked one after the other, so only the first of
    ///     them reads the sources and the rest are answered from what it just fetched. That holds
    ///     for a source that cannot be read too: the failure (unreachable, sign-in needed) is not
    ///     cached, but the round remembers it per source and package (a
    ///     <see cref="SourceFailureMemo"/>), so the later cards show the same failure without
    ///     reading the source again, and the next round reads it afresh. A card that
    ///     is busy, missing, or has no pin is skipped. A tick that finds the previous round still
    ///     running (or the launch-time check still running) does nothing, so a slow source never
    ///     piles up rounds.
    ///     </para>
    ///     <para>
    ///     The timer fires on a thread-pool thread; <paramref name="runOnUiThread"/> brings each tick
    ///     back to the UI thread, where the round runs and where its awaits resume, so every card
    ///     property is updated there, like the launch-time check.
    ///     </para>
    /// </remarks>
    /// <param name="runOnUiThread">Runs an action on the UI thread (the main window passes the
    ///     dispatcher's <c>Post</c>), or <see langword="null"/> to run each tick on the timer's own
    ///     thread (for tests with a fake clock, which fire the timer on the caller's thread).</param>
    /// <exception cref="ObjectDisposedException">Thrown when this view model was disposed.</exception>
    public void StartPeriodicUpgradeRecheck(Action<Action>? runOnUiThread = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recheckTimer is not null)
        {
            return;
        }

        _runOnUiThread = runOnUiThread;
        _recheckTimer = _timeProvider.CreateTimer(
            _ => OnRecheckTimer(), null, _upgradeRecheckInterval, _upgradeRecheckInterval);
    }

    /// <summary>
    ///     Handles the periodic timer firing (on any thread): moves to the UI thread and starts a round.
    /// </summary>
    private void OnRecheckTimer()
    {
        if (_runOnUiThread is { } runOnUiThread)
        {
            runOnUiThread(BeginPeriodicRecheck);
        }
        else
        {
            BeginPeriodicRecheck();
        }
    }

    /// <summary>
    ///     Starts one periodic re-check round unless this view model was disposed, the previous round
    ///     is still running, or any launch-time check (including one that was superseded but has not
    ///     unwound) is still running.
    /// </summary>
    private void BeginPeriodicRecheck()
    {
        if (_disposed || _recheckCancellation is not null || Volatile.Read(ref _launchChecksInFlight) > 0)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _recheckCancellation = cancellation;
        PendingPeriodicRecheck = RecheckUpgradesAsync(cancellation);
    }

    /// <summary>
    ///     Requests cancellation of the periodic re-check round that is running, if any. The round
    ///     stays registered in <see cref="_recheckCancellation"/> until it has finished unwinding, so
    ///     no new round starts while it is still running.
    /// </summary>
    private void CancelPeriodicRecheck()
    {
        // Only request the cancellation: the field keeps referring to the round until that round has
        // actually unwound (its finally clears it), so a tick that comes while the canceled round's
        // provider call is still in flight does not start a second round on top of it
        _recheckCancellation?.Cancel();
    }

    /// <summary>
    ///     Runs one periodic re-check round; see <see cref="StartPeriodicUpgradeRecheck"/>.
    /// </summary>
    /// <param name="cancellation">The round's cancellation source, which this round disposes.</param>
    /// <returns>A task that completes when every card has been re-checked or the round was
    ///     cancelled; cancellation is not an error.</returns>
    private async Task RecheckUpgradesAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;

        // The round's failures; registered so that closing Settings can make it forget them
        var failures = RegisterFailureMemo();
        try
        {
            var cards = RepoCards.Where(CanRecheckUpgrade).ToList();
            if (cards.Count == 0)
            {
                return;
            }

            // What the previous round (or any earlier action) cached may still be inside its five
            // minutes: this round exists to see what the sources offer now
            _packageVersionCache.Invalidate();

            // Cards that pin the same package are checked one after the other, so only the first
            // reads the sources and the others are answered from what it just fetched; groups run
            // a few at a time. A source that cannot be read is not cached (a later round, and the
            // user's own actions, try it again), so the round remembers its failures: the cards after
            // the first get the same failure without reading that source again.
            var groups = cards
                .GroupBy(card => card.PinnedPackageName, StringComparer.OrdinalIgnoreCase)
                .Select(async group =>
                {
                    // A card whose repo state is being read again must have it applied before its
                    // check starts (applying it drops a check in flight); waited for before taking a
                    // slot, so a slow read never holds one
                    await Task.WhenAll(group.Select(card => card.WhenCheapFactsSettledAsync()));
                    await _upgradeCheckSlots.WaitAsync(token);
                    try
                    {
                        foreach (var card in group)
                        {
                            token.ThrowIfCancellationRequested();
                            if (CanRecheckUpgrade(card))
                            {
                                await card.RefreshUpgradeStatusAsync(token, failureMemo: failures);
                            }
                        }
                    }
                    finally
                    {
                        _upgradeCheckSlots.Release();
                    }
                })
                .ToList();
            await Task.WhenAll(groups);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer check, or the window is closing: the cards keep their state
        }
        finally
        {
            UnregisterFailureMemo(failures);
            if (ReferenceEquals(_recheckCancellation, cancellation))
            {
                _recheckCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    ///     Determines whether a periodic round checks a card: one that is busy with an action of its
    ///     own, whose repo state has not been read yet (its startup check follows the read), whose
    ///     folder is missing, or that pins no package has nothing to re-check.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns><see langword="true"/> when the card's upgrade availability is re-checked.</returns>
    private static bool CanRecheckUpgrade(RepoCardViewModel card) =>
        !card.IsBusy && card.HasRepoState && !card.IsMissing && card.PinnedPackageName is not null;

    /// <summary>
    ///     Stops the periodic upgrade re-check (see <see cref="StartPeriodicUpgradeRecheck"/>) and
    ///     cancels the round that is running, the background upgrade check (launch, settings
    ///     change, added repo) and card refresh that are running, the adds that have not saved
    ///     yet, and every repo read of the shared refresh queue (a running git is stopped), then
    ///     writes the settings that are not written yet; called when the main window closes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The settings are written on a worker thread, after every write that is still running, by
    ///     the close's own write (see <see cref="WriteSettingsAtCloseAsync"/>): it first waits for the
    ///     confirmed saves (an add, the Settings window's Save) still running, then writes the live
    ///     settings - holding every change made in the window, those made while a confirmed save ran
    ///     included - with the change of each confirmed save whose write succeeded; one whose write
    ///     failed is left out (there is nobody left to offer Retry, so it is discarded, as Discard
    ///     would) and logged. That snapshot - the full intended state - is always written, once, as
    ///     the last write, so two overlapping confirmed saves (an add and the Settings window's Save)
    ///     both end up on disk although each one's candidate lacked the other's change, and a change
    ///     held back or still waiting is never left behind. Writes stay one at a time and in order,
    ///     so the close's snapshot is never written before a confirmed write it waits for, and that
    ///     older write can never follow it and take a change out of the file again.
    ///     </para>
    ///     <para>
    ///     The calling thread waits for that write at most <see cref="SettingsFlushTimeout"/> in all
    ///     (the one wait for the disk left on the UI thread; it comes after the window has closed, and
    ///     the writes never need the UI thread, so it cannot deadlock). When the time runs out the
    ///     close returns anyway, so the process can exit: the close's write stays queued behind the
    ///     write that is still running and still happens if the process lives that long, and what is
    ///     not on disk yet - and is lost if the process ends first - is logged, because nobody can be
    ///     told any more; so is a close's write that failed.
    ///     </para>
    ///     <para>
    ///     Every confirmed save still running at the close is decided by the close, once (see
    ///     <see cref="SaveConfirmedAsync"/>): when it ends afterwards it adopts nothing, so the closed
    ///     view model's settings, cards and list are not changed and no refresh is started; its
    ///     change is on disk exactly when its write succeeded.
    ///     </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Every confirmed save still running is decided by the close, here and once: its own completion
        // then adopts nothing into the closed view model (see SaveConfirmedAsync). The close's write
        // adds the change of those whose write succeeds and leaves out those whose write fails.
        var pending = _runningConfirmedSaves.Where(save => save.TryDecide()).ToArray();
        _runningConfirmedSaves.Clear();

        _recheckTimer?.Dispose();
        _recheckTimer = null;
        CancelPeriodicRecheck();

        // Adds that have not saved yet stop: nothing more is added after the window closed
        CancelAddRepos();

        // The launch-time check must not keep waiting for a slow source (or publish to cards) after
        // the window closed
        var refresh = _refreshCancellation;
        _refreshCancellation = null;
        if (refresh is not null)
        {
            refresh.Cancel();
            refresh.Dispose();
        }

        // No repo is read any more: the waiting reads are dropped, a running git is stopped, and the
        // strip's refresh line goes
        _refreshQueue.ProgressChanged -= OnRefreshQueueProgressChanged;
        if (_ownsRefreshQueue)
        {
            _refreshQueue.Dispose();
        }

        _refreshProgress?.Dispose();
        _refreshProgress = null;

        // The live settings hold every change made in the window and no change of a confirmed save that
        // was not adopted; taken here, on the UI thread, and always written by the worker as the last
        // write, after the confirmed writes still running have ended, with the change of each one that
        // succeeded
        var atClose = _settings.CreateSnapshot();
        var closeWrite = Task.Run(() => WriteSettingsAtCloseAsync(pending, atClose), CancellationToken.None);
        CloseSettingsWrite = closeWrite;
        if (WaitQuietly(closeWrite, _settingsFlushTimeout))
        {
            return;
        }

        // Out of time: the process may end before the close's write, which still waits behind a write
        // that is running, so say what is not on disk yet; and say so again if it is written after all
        LogSettingsNotWrittenInTime(pending);
        _ = closeWrite.ContinueWith(
            write =>
            {
                if (write.Result)
                {
                    Logger.LogInformation("The settings were written after the window closed, after all");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    /// <summary>
    ///     Gets the close's write of the settings (see <see cref="Dispose"/>), or
    ///     <see langword="null"/> before the close; a test seam for a close that did not wait for it.
    /// </summary>
    /// <remarks>
    ///     Completes, never faulting, when the write has ended: <see langword="true"/> when everything
    ///     was written.
    /// </remarks>
    internal Task<bool>? CloseSettingsWrite { get; private set; }

    /// <summary>
    ///     The close's write of the settings, on a worker thread: waits for the confirmed writes still
    ///     running, then always writes the settings with the change of each one that succeeded (see
    ///     <see cref="Dispose"/>), and finally disposes the settings writer.
    /// </summary>
    /// <remarks>
    ///     The composed snapshot is the full intended state: every change made in the window (those
    ///     held back while a confirmed save ran included) and every confirmed change that reached the
    ///     disk. It is written with a confirmed save of the writer, asked for only once every confirmed
    ///     write it waits for has ended, so it is always the last write: after them (and after a
    ///     scheduled write that was waiting for its turn), with an older waiting snapshot dropped.
    ///     Nothing decides whether it is needed: overlapping confirmed saves each wrote a candidate
    ///     without the other's change, and a change may be waiting or held back, so writing the
    ///     intended state once, last, is what leaves the file right in every case. What could not be
    ///     written is logged.
    /// </remarks>
    /// <param name="pending">The confirmed saves the close decided, oldest first.</param>
    /// <param name="atClose">A snapshot of the live settings at the close; changed here.</param>
    /// <returns><see langword="true"/> when everything was written. Never faults.</returns>
    private async Task<bool> WriteSettingsAtCloseAsync(RunningConfirmedSave[] pending, AppSettings atClose)
    {
        try
        {
            // The confirmed writes end first: the settings are written after them, never before
            var intended = atClose;
            foreach (var save in pending)
            {
                try
                {
                    await save.Write.ConfigureAwait(false);
                    intended = save.ApplyTo(intended);
                }
                catch (Exception ex)
                {
                    // Whatever the write threw, the change is not on disk; nobody can be asked any more
                    Logger.LogWarning(
                        ex,
                        "The settings could not be saved for {Change}, which is discarded because the window closed",
                        save.Description);
                }
            }

            // The intended state, always, as the last write
            var written = true;
            try
            {
                await _settingsWriter.SaveNowAsync(intended).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Whatever the save threw, the snapshot is not on disk
                Logger.LogWarning(ex, "The settings could not be written when the window closed");
                written = false;
            }

            if (!written)
            {
                Logger.LogWarning(
                    "Settings changes lost when the window closed: {Lost}",
                    DescribeSettingsDifference(Volatile.Read(ref _lastWrittenSettings), intended));
            }

            return written;
        }
        finally
        {
            _settingsWriter.Dispose();
        }
    }

    /// <summary>
    ///     Logs, when the close's wait ran out, what is not on disk yet: the confirmed saves whose
    ///     write is still running and every change the file does not hold yet.
    /// </summary>
    /// <remarks>
    ///     Runs on the UI thread, which owns the live settings; the close's write works on its own
    ///     snapshot. The settings described assume the running confirmed writes succeed, as the
    ///     close's write would.
    /// </remarks>
    /// <param name="pending">The confirmed saves the close decided.</param>
    private void LogSettingsNotWrittenInTime(RunningConfirmedSave[] pending)
    {
        var intended = _settings.CreateSnapshot();
        var running = new List<string>();
        foreach (var save in pending.Where(save => !save.Write.IsFaulted && !save.Write.IsCanceled))
        {
            intended = save.ApplyTo(intended);
            if (!save.Write.IsCompleted)
            {
                running.Add(save.Description);
            }
        }

        Logger.LogWarning(
            "The settings were not written within {Timeout} when the window closed; the close's write waits for " +
            "the save still running ({Running}) and is lost if the application exits first. Not on disk yet: {Lost}",
            _settingsFlushTimeout,
            running.Count == 0 ? "none" : string.Join("; ", running),
            DescribeSettingsDifference(Volatile.Read(ref _lastWrittenSettings), intended));
    }

    /// <summary>
    ///     Describes how one settings state differs from another, for the log of settings that could
    ///     not be written.
    /// </summary>
    /// <param name="onDisk">What the settings file holds.</param>
    /// <param name="intended">What it should hold.</param>
    /// <returns>Every difference - repos added and removed, the changed fields of each repo, the repo
    ///     order, the collapsed sections, the group colors and order, and the Settings window's
    ///     values - or "nothing".</returns>
    private static string DescribeSettingsDifference(AppSettings onDisk, AppSettings intended)
    {
        var changes = new List<string>();
        var before = new Dictionary<string, RecentRepo>(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in onDisk.RecentRepos)
        {
            before.TryAdd(repo.Path, repo);
        }

        var after = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in intended.RecentRepos)
        {
            after.Add(repo.Path);
            if (!before.TryGetValue(repo.Path, out var old))
            {
                changes.Add($"repo {repo.Path} added");
                continue;
            }

            var fields = new List<string>();
            AddIfChanged(fields, "favorite", old.IsFavorite, repo.IsFavorite);
            AddIfChanged(fields, "group", old.GroupName, repo.GroupName);
            AddIfChanged(fields, "card color", old.CardColor, repo.CardColor);
            AddIfChanged(fields, "last launch", old.LastLaunchedUtc, repo.LastLaunchedUtc);
            AddIfChanged(fields, "pinned package", old.PinnedPackageName, repo.PinnedPackageName);
            AddIfChanged(fields, "pinned version", old.PinnedPackageVersion, repo.PinnedPackageVersion);
            AddIfChanged(fields, "package source", old.PinnedSource, repo.PinnedSource);
            if (fields.Count > 0)
            {
                changes.Add($"repo {repo.Path}: {string.Join(", ", fields)}");
            }
        }

        changes.AddRange(onDisk.RecentRepos.Where(repo => !after.Contains(repo.Path)).Select(repo => $"repo {repo.Path} removed"));

        // The order of the repos both hold
        var orderBefore = onDisk.RecentRepos.Select(repo => repo.Path).Where(after.Contains);
        var orderAfter = intended.RecentRepos.Select(repo => repo.Path).Where(before.ContainsKey);
        if (!orderBefore.SequenceEqual(orderAfter, StringComparer.OrdinalIgnoreCase))
        {
            changes.Add("repo order: " + string.Join(", ", orderAfter));
        }

        if (!onDisk.CollapsedGroups.Order(StringComparer.Ordinal).SequenceEqual(intended.CollapsedGroups.Order(StringComparer.Ordinal)))
        {
            changes.Add($"collapsed sections: [{string.Join(", ", intended.CollapsedGroups)}]");
        }

        if (!DescribeGroupColors(onDisk).SequenceEqual(DescribeGroupColors(intended)))
        {
            changes.Add($"group colors: [{string.Join(", ", DescribeGroupColors(intended))}]");
        }

        if (!onDisk.GroupOrder.SequenceEqual(intended.GroupOrder))
        {
            changes.Add($"group order: [{string.Join(", ", intended.GroupOrder)}]");
        }

        if (!string.Equals(DescribeOtherSettings(onDisk), DescribeOtherSettings(intended), StringComparison.Ordinal))
        {
            changes.Add("the Settings window's settings");
        }

        return changes.Count == 0 ? "nothing" : string.Join("; ", changes);
    }

    /// <summary>
    ///     Adds a field's change to a description when its value changed.
    /// </summary>
    /// <typeparam name="T">The field's type.</typeparam>
    /// <param name="fields">The description of the changed fields.</param>
    /// <param name="name">The field's name.</param>
    /// <param name="before">The value on disk.</param>
    /// <param name="after">The value that should be on disk.</param>
    private static void AddIfChanged<T>(List<string> fields, string name, T before, T after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after))
        {
            fields.Add($"{name} {before?.ToString() ?? "none"} -> {after?.ToString() ?? "none"}");
        }
    }

    /// <summary>
    ///     Lists the group colors of settings in a stable order, for comparing and logging them.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>One "group=color" entry per group, ordered by group.</returns>
    private static List<string> DescribeGroupColors(AppSettings settings) =>
        [.. settings.GroupColors.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Key}={entry.Value}")];

    /// <summary>
    ///     Serializes the settings the Settings window edits (everything but the repos and how they
    ///     are organized), for telling whether they differ.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The JSON of those settings.</returns>
    private static string DescribeOtherSettings(AppSettings settings)
    {
        var others = settings.CreateSnapshot();
        others.RecentRepos = [];
        others.HasUserRepoOrder = false;
        others.CollapsedGroups = [];
        others.GroupColors = new Dictionary<string, string>();
        others.GroupOrder = [];
        return JsonSerializer.Serialize(others);
    }

    /// <summary>
    ///     Checks each card's upgrade availability, a few at a time.
    /// </summary>
    /// <param name="cards">The cards to check.</param>
    /// <param name="cancellationToken">Cancels the checks that have not completed.</param>
    /// <returns>A task that completes when every check has finished or been cancelled; a
    ///     cancelled check is not an error.</returns>
    /// <remarks>
    ///     <para>
    ///     A card that is busy with an action of its own (<see cref="RepoCardViewModel.IsBusy"/>)
    ///     when its turn comes is skipped, as a periodic round skips it: a background check beside
    ///     the action would break the card's one-action-at-a-time rule and could replace the
    ///     action's result with its own (the newest check of a card is the one shown). Refresh,
    ///     Upgrade, Select Package and Pull check the card themselves; any card is checked again by
    ///     the next periodic round. Whether the card is busy is read once the card holds its slot,
    ///     right before its check starts, so an action started while it waited for a slot is seen.
    ///     A card whose own background check is running is not busy and is checked: the newer
    ///     check's result replaces the older one's.
    ///     </para>
    ///     <para>
    ///     A card's check waits until the card's repo-state read that is queued or running (at
    ///     startup, every card's) was applied, because applying it drops an upgrade check in flight;
    ///     the wait happens before the card takes one of the slots, which every background check
    ///     shares (<see cref="_upgradeCheckSlots"/>), so overlapping checks never read more than
    ///     <see cref="MaxConcurrentUpgradeChecks"/> sources at once between them.
    ///     </para>
    ///     <para>
    ///     Counted in <see cref="_launchChecksInFlight"/> from the call until it has unwound, so a
    ///     check that was superseded (and canceled) but is still inside a provider call that ignores
    ///     cancellation keeps holding off the periodic round, even though
    ///     <see cref="PendingRefresh"/> no longer refers to it.
    ///     </para>
    /// </remarks>
    private async Task CheckUpgradesAsync(
        IReadOnlyList<RepoCardViewModel> cards, CancellationToken cancellationToken)
    {
        // Counted before the first await, so the caller observes it as soon as this returns
        Interlocked.Increment(ref _launchChecksInFlight);

        // The failures of this check: once a source could not be read for a package, the cards that
        // follow get that failure instead of reading it again (cards that are already reading it
        // at the same moment still do, as they do for a listing that succeeds). Registered so that
        // closing Settings can make it forget them.
        var failures = RegisterFailureMemo();
        try
        {
            var checks = cards.Select(async card =>
            {
                // The card's repo state is read first (at startup it is still queued): applying it
                // drops any upgrade check in flight, so the check starts only once the read landed.
                // Waited for before taking a slot, so a slow read never holds one.
                await card.WhenCheapFactsSettledAsync();
                await _upgradeCheckSlots.WaitAsync(cancellationToken);
                try
                {
                    // Decided when the card's turn comes, not when it was queued: the user may have
                    // started an action on it (Refresh, Upgrade, Select Package, Launch, Pull) while it
                    // waited, and a check started now would run beside that action and could replace
                    // its result. The check and the start of the card's check run in one go, with no
                    // await between them, on the thread the action's busy flag is set on (the UI
                    // thread), so no action can start in between.
                    if (!card.IsBusy)
                    {
                        await card.RefreshUpgradeStatusAsync(cancellationToken, failureMemo: failures);
                    }
                }
                finally
                {
                    _upgradeCheckSlots.Release();
                }
            });

            try
            {
                await Task.WhenAll(checks);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer check (or the window closing): the cards keep their state
            }
        }
        finally
        {
            UnregisterFailureMemo(failures);
            Interlocked.Decrement(ref _launchChecksInFlight);
        }
    }

    /// <summary>
    ///     Creates the failure memo of a background check that is starting and registers it as
    ///     running, so <see cref="ForgetRecordedSourceFailures"/> reaches it.
    /// </summary>
    /// <returns>The new, empty memo; the check passes it to <see cref="UnregisterFailureMemo"/>
    ///     when it has unwound.</returns>
    private SourceFailureMemo RegisterFailureMemo()
    {
        var memo = new SourceFailureMemo();
        lock (_activeFailureMemosLock)
        {
            _activeFailureMemos.Add(memo);
        }

        return memo;
    }

    /// <summary>
    ///     Drops the failure memo of a background check that has unwound from the running ones.
    /// </summary>
    /// <param name="memo">The memo <see cref="RegisterFailureMemo"/> returned to the check.</param>
    private void UnregisterFailureMemo(SourceFailureMemo memo)
    {
        lock (_activeFailureMemosLock)
        {
            _activeFailureMemos.Remove(memo);
        }
    }

    /// <summary>
    ///     Makes every background check that is running forget the source failures it has recorded,
    ///     so the cards it checks from now on read those sources again.
    /// </summary>
    /// <remarks>
    ///     A failure recorded before the user signed in (or before the connection came back) no
    ///     longer holds; a round that kept replaying it would put "Sign-in needed" back on a card
    ///     after a newer check had cleared it, because the newest check of a card is the one whose
    ///     result is shown. The memos are cleared whole: every card reads the same configured
    ///     sources, and a forgotten failure only costs the round one more read of that source.
    /// </remarks>
    private void ForgetRecordedSourceFailures()
    {
        lock (_activeFailureMemosLock)
        {
            foreach (var memo in _activeFailureMemos)
            {
                memo.Clear();
            }
        }
    }

    /// <summary>
    ///     Raised when the user's latest changes in the main window (marking a favorite, choosing a
    ///     group or a card color, coloring, renaming, removing or moving a group, collapsing a section,
    ///     moving or removing a repo, a launch being recorded) could not be written to the settings
    ///     file in the background; the argument is the message for the user and the window shows it.
    /// </summary>
    /// <remarks>
    ///     The changes stay applied: they are in effect while the application runs and are written
    ///     again with the next change and when the window closes. Raised on the UI thread, once per
    ///     failure streak (the first failure after a write that succeeded), so a disk that keeps
    ///     failing does not interrupt every click; not raised after the window closed. Also raised,
    ///     with its own message, when a synchronous <see cref="AddRepoToGroup"/> could not save the
    ///     move of a listed repo.
    /// </remarks>
    public event EventHandler<string>? SettingsSaveFailed;

    /// <summary>
    ///     Writes the settings that are waiting to be written now and waits for it; a test seam for
    ///     code that reads the settings file right after a change.
    /// </summary>
    /// <returns>A task that completes, never faulting, when every write accepted so far has ended:
    ///     <see langword="true"/> when what was waiting was written.</returns>
    internal Task<bool> FlushSettingsAsync() => _settingsWriter.FlushAsync();

    /// <summary>
    ///     Applies a change to the live settings at once and schedules them to be written in the
    ///     background.
    /// </summary>
    /// <remarks>
    ///     The change takes effect immediately, so the window never waits for the settings file; the
    ///     group colors and order are repaired afterwards (see
    ///     <see cref="AppSettings.RepairGroupSettings"/>), so a group that lost its last repo forgets
    ///     its color and position. The background writer (see <see cref="ScheduleSettingsSave"/>)
    ///     writes the newest state shortly after the last change. When a write fails the user is told
    ///     once (see <see cref="SettingsSaveFailed"/>) and the change stays in effect; it is written with
    ///     the next change or when the window closes.
    /// </remarks>
    /// <param name="change">The change, expressed on the settings it is given.</param>
    private void CommitSettingsChange(Action<AppSettings> change)
    {
        change(_settings);
        _settings.RepairGroupSettings();
        ScheduleSettingsSave();
    }

    /// <summary>
    ///     Hands a snapshot of the live settings to the background writer, or, while a confirmed save
    ///     runs (an add, the Settings window's Save), remembers that one is due once it has ended.
    /// </summary>
    /// <remarks>
    ///     A confirmed save was built from the settings as they were when it started, without the
    ///     change it has not adopted yet. A snapshot written while it runs would therefore not hold that
    ///     change and, written after it, would take it out of the file again until the next write. So
    ///     the snapshot is taken only after every confirmed save has ended and adopted its change (see
    ///     <see cref="SaveConfirmedAsync"/>). Nothing is handed to the writer after the close: the
    ///     close's own write (see <see cref="Dispose"/>) is the last one, and a later snapshot would
    ///     not hold a confirmed save the close decided.
    /// </remarks>
    private void ScheduleSettingsSave()
    {
        if (_disposed)
        {
            return;
        }

        if (_runningConfirmedSaves.Count > 0)
        {
            _saveDeferred = true;
            return;
        }

        _settingsWriter.Schedule(_settings.CreateSnapshot());
    }

    /// <summary>
    ///     Saves a candidate with a confirmed write of the background settings writer and, once it is on
    ///     disk, adopts it; for the changes that must not be applied unless they were saved (an add,
    ///     the Settings window's Save).
    /// </summary>
    /// <remarks>
    ///     The write runs on a worker thread, so the window stays usable; the adoption runs on the
    ///     caller's thread (the UI thread) when the write has succeeded. A change made in the window
    ///     while the write ran is applied at once but written only afterwards (see
    ///     <see cref="ScheduleSettingsSave"/>): when the last confirmed save has ended, the live
    ///     settings - holding both - are scheduled to be written. A confirmed save that ends while
    ///     another one still runs schedules that write too, because the other one's candidate does not
    ///     hold it. The save is tracked in <see cref="_runningConfirmedSaves"/> until it has ended, so
    ///     the close can write its change with the changes made meanwhile once its write succeeded, or
    ///     leave it out once it failed (see <see cref="Dispose"/>).
    ///     Whether the change is adopted is decided once (<see cref="RunningConfirmedSave.TryDecide"/>):
    ///     by this method when the write ends while the window is open, otherwise by the close, after
    ///     which this method adopts nothing and changes nothing of the closed view model.
    /// </remarks>
    /// <param name="applyTo">Applies the change to settings that are not the live ones and returns
    ///     the result: applied to a snapshot of the live settings it builds the candidate to write, and
    ///     at the close it is applied, on a worker thread, to the snapshot written then.</param>
    /// <param name="adopt">Applies the saved change to the live settings, the cards and the list.</param>
    /// <param name="description">What the change is, for the log of a close that could not save
    ///     it.</param>
    /// <returns>A task that completes when the candidate was written and adopted (or, when the window
    ///     closed while it was written, when it was written).</returns>
    /// <exception cref="InvalidOperationException">Thrown (through the task) when the settings file
    ///     cannot be written; nothing was adopted.</exception>
    /// <exception cref="ObjectDisposedException">Thrown (through the task) when the window has
    ///     closed; nothing was written.</exception>
    private async Task SaveConfirmedAsync(Func<AppSettings, AppSettings> applyTo, Action adopt, string description)
    {
        // A closed window saves nothing more: a write now could follow the close's own write
        ObjectDisposedException.ThrowIf(_disposed, this);
        var candidate = applyTo(_settings.CreateSnapshot());
        var write = _settingsWriter.SaveNowAsync(candidate);
        var running = new RunningConfirmedSave(write, applyTo, description);
        _runningConfirmedSaves.Add(running);
        try
        {
            await write;
        }
        catch
        {
            // Failed: nothing to adopt. Ended here unless the close took the decision already.
            if (running.TryDecide())
            {
                EndConfirmedSave(running, adopted: false);
            }

            throw;
        }

        // The decision is made once: here, or by the close when the window closed while this save ran
        // (see Dispose). Then the closed view model is left as it is: nothing is adopted, no card is
        // built and no refresh is started after the close.
        if (!running.TryDecide())
        {
            return;
        }

        var adopted = false;
        try
        {
            adopt();
            adopted = true;
        }
        finally
        {
            EndConfirmedSave(running, adopted);
        }
    }

    /// <summary>
    ///     Ends a confirmed save that decided for itself (see <see cref="SaveConfirmedAsync"/>): takes it
    ///     out of the running ones and schedules the deferred write when it was the last.
    /// </summary>
    /// <param name="running">The save that ended.</param>
    /// <param name="adopted">Whether its change was adopted.</param>
    private void EndConfirmedSave(RunningConfirmedSave running, bool adopted)
    {
        _runningConfirmedSaves.Remove(running);
        if (_runningConfirmedSaves.Count > 0)
        {
            // Another confirmed save is still running, built without this one's change
            _saveDeferred |= adopted;
        }
        else if (_saveDeferred)
        {
            _saveDeferred = false;
            ScheduleSettingsSave();
        }
    }

    /// <summary>
    ///     Waits a bounded time for a task, ignoring how it ended.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="timeout">How long to wait at most.</param>
    /// <returns><see langword="true"/> when the task ended in time.</returns>
    private static bool WaitQuietly(Task task, TimeSpan timeout)
    {
        try
        {
            return task.Wait(timeout);
        }
        catch (AggregateException)
        {
            // Ended, by failing: the task logged what it could not do
            return true;
        }
    }

    /// <summary>
    ///     A confirmed save that is running (see <see cref="SaveConfirmedAsync"/>).
    /// </summary>
    /// <remarks>
    ///     What becomes of the save's change - adopted into the live settings, written with the close's
    ///     snapshot, or left out - is decided exactly once, by whichever comes first: the save's own
    ///     completion or the close (see <see cref="Dispose"/>). <see cref="TryDecide"/> hands that
    ///     decision to one caller only.
    /// </remarks>
    /// <param name="write">The confirmed write of the background settings writer.</param>
    /// <param name="applyTo">Applies the save's change to settings that are not the live ones.</param>
    /// <param name="description">What the change is, for the log.</param>
    private sealed class RunningConfirmedSave(Task write, Func<AppSettings, AppSettings> applyTo, string description)
    {
        /// <summary>
        ///     One once the decision was taken.
        /// </summary>
        private int _decided;

        /// <summary>
        ///     Gets the confirmed write of the background settings writer.
        /// </summary>
        public Task Write { get; } = write;

        /// <summary>
        ///     Gets the function that applies the save's change to settings that are not the live ones.
        /// </summary>
        public Func<AppSettings, AppSettings> ApplyTo { get; } = applyTo;

        /// <summary>
        ///     Gets what the change is (the repos an add adds and moves, or the Settings window's
        ///     settings), for the log of a close that could not save it.
        /// </summary>
        public string Description { get; } = description;

        /// <summary>
        ///     Takes the decision about the save's change, unless it was taken already.
        /// </summary>
        /// <returns><see langword="true"/> for the first caller only.</returns>
        public bool TryDecide() => Interlocked.Exchange(ref _decided, 1) == 0;
    }

    /// <summary>
    ///     Applies a change to one repo's entry on behalf of its card and saves it in the background
    ///     (see <see cref="CommitSettingsChange"/>).
    /// </summary>
    /// <param name="repo">The repo's entry; a repo that is no longer in the list (a card that was
    ///     removed) is changed without saving anything.</param>
    /// <param name="change">The change to the repo's entry.</param>
    /// <returns><see langword="true"/>: the change is applied at once (a failed background write is
    ///     reported through <see cref="SettingsSaveFailed"/> and leaves it applied).</returns>
    private bool CommitRepoChange(RecentRepo repo, Action<RecentRepo> change)
    {
        if (!_settings.RecentRepos.Contains(repo))
        {
            change(repo);
            return true;
        }

        CommitSettingsChange(_ => change(repo));
        return true;
    }

    /// <summary>
    ///     Tells the user (through <see cref="SettingsSaveFailed"/>) that the background writer could
    ///     not write the latest changes; they stay in effect.
    /// </summary>
    /// <param name="sender">The settings writer.</param>
    /// <param name="failure">Why the write failed.</param>
    private void OnSettingsWriteFailed(object? sender, Exception failure)
    {
        // A closed window has nobody to tell; the close logged what it could not write
        if (_disposed)
        {
            return;
        }

        SettingsSaveFailed?.Invoke(
            this,
            $"Your latest changes could not be saved to the settings file. {SecretRedactor.SafeMessage(failure)}\n\n" +
            "They stay in effect while AgentControl runs, and are saved again with your next change and when " +
            "AgentControl closes.");
    }

    /// <summary>
    ///     Tells the user (through <see cref="SettingsSaveFailed"/>) that the move of a listed repo by a
    ///     synchronous <see cref="AddRepoToGroup"/> was not saved and so not applied.
    /// </summary>
    /// <param name="exception">The failure.</param>
    private void ReportSaveFailure(InvalidOperationException exception) =>
        SettingsSaveFailed?.Invoke(
            this, $"The change was not applied because it could not be saved. {SecretRedactor.SafeMessage(exception)}");
    /// <summary>
    ///     Drops the shared cache's entries for every source that existed before a settings change
    ///     and is now removed or different, so a stale result is never reused for an edited source
    ///     while untouched sources keep their cached results.
    /// </summary>
    /// <param name="previous">The sources in effect before the change.</param>
    /// <param name="current">The sources in effect after the change.</param>
    private void InvalidateChangedSources(
        IReadOnlyList<PackageSourceDefinition> previous, IReadOnlyList<PackageSourceDefinition> current)
    {
        foreach (var before in previous)
        {
            var after = current.FirstOrDefault(
                source => string.Equals(source.Alias, before.Alias, StringComparison.OrdinalIgnoreCase));
            if (after is null || !after.IsEquivalentTo(before))
            {
                _packageVersionCache.Invalidate(before.Alias);
            }
        }
    }

    /// <summary>
    ///     Recomputes <see cref="DisplayedItems"/> and <see cref="DisplayedRepoCards"/> from
    ///     <see cref="RepoCards"/>: filters by <see cref="FilterText"/>, arranges the cards by section
    ///     and keeps the user's own order within each (see <see cref="DisplayedItems"/> for the
    ///     section rules), and brings both collections to that state with the fewest changes.
    /// </summary>
    /// <remarks>
    ///     The collections are updated in place (removing, moving and inserting only what differs)
    ///     rather than cleared and refilled, so the rows that stay keep their controls - and so the
    ///     keyboard focus - when, for example, a group is collapsed. The flat list's favorites-first
    ///     ordering uses
    ///     <see cref="Enumerable.OrderByDescending{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/>,
    ///     which is documented as a stable sort, so the user's order is kept within each tier.
    /// </remarks>
    private void UpdateDisplayedRepoCards()
    {
        var filter = _filterText;
        var isFiltering = !string.IsNullOrWhiteSpace(filter);
        IEnumerable<RepoCardViewModel> filtered = RepoCards;
        if (isFiltering)
        {
            filtered = filtered.Where(card =>
                card.RepoName.Contains(filter!, StringComparison.OrdinalIgnoreCase) ||
                card.RepoPath.Contains(filter!, StringComparison.OrdinalIgnoreCase));
        }

        var items = new List<object>();
        var cards = new List<RepoCardViewModel>();
        var usedHeaders = new HashSet<RepoGroupHeaderViewModel>();

        if (!HasGroups)
        {
            // No group anywhere: a flat list, favorites first, then the user's own order
            foreach (var card in filtered.OrderByDescending(card => card.IsFavorite))
            {
                items.Add(card);
                cards.Add(card);
            }
        }
        else
        {
            var matches = filtered.ToList();

            // A favorite is listed only under Favorites; every other repo under its group or, with
            // none, under Ungrouped
            AddSection(RepoGroupKind.Favorites, RepoGroupName.FavoritesName, matches.Where(card => card.IsFavorite));
            var groupNames = RepoGroupHierarchy.ArrangeGroups(
                matches.Where(card => !card.IsFavorite && card.HasGroup).Select(card => card.GroupName!),
                _settings.GroupOrder);
            foreach (var groupName in groupNames)
            {
                AddSection(
                    RepoGroupKind.Group,
                    groupName,
                    matches.Where(card =>
                        !card.IsFavorite && card.HasGroup && RepoGroupName.Comparer.Equals(card.GroupName!, groupName)));
            }

            AddSection(
                RepoGroupKind.Ungrouped, RepoGroupName.UngroupedName, matches.Where(card => !card.IsFavorite && !card.HasGroup));
        }

        // Forget the headers of sections that no longer have a match
        foreach (var stale in _headers.Where(pair => !usedHeaders.Contains(pair.Value)).Select(pair => pair.Key).ToList())
        {
            _headers.Remove(stale);
        }

        SyncCollection(DisplayedItems, items);
        SyncCollection(DisplayedRepoCards, cards);

        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(GroupNames));
        OnPropertyChanged(nameof(HasNoDisplayedRepos));
        OnPropertyChanged(nameof(EmptyStateMessage));

        // Adds one section: its header (when it has any match) and, unless collapsed, its cards. A
        // filter shows everything, whatever the remembered collapsed state.
        void AddSection(RepoGroupKind kind, string name, IEnumerable<RepoCardViewModel> sectionCards)
        {
            var members = sectionCards.ToList();
            if (members.Count == 0)
            {
                return;
            }

            var collapsed = !isFiltering && IsGroupCollapsed(name);
            var header = GetHeader(kind, name);
            header.Update(members.Count, collapsed, isToggleEnabled: !isFiltering);
            header.UpdateColor(kind == RepoGroupKind.Group ? _settings.FindGroupColor(name) : null);
            if (kind == RepoGroupKind.Group)
            {
                header.UpdateOrder(
                    GetGroupMoveDisabledReason(name, RepoGroupMove.Up),
                    GetGroupMoveDisabledReason(name, RepoGroupMove.Down));
            }

            usedHeaders.Add(header);
            items.Add(header);
            if (!collapsed)
            {
                items.AddRange(members);
                cards.AddRange(members);
            }
        }
    }

    /// <summary>
    ///     Makes <paramref name="target"/> contain exactly <paramref name="desired"/>, in order, by
    ///     removing, moving and inserting only what differs.
    /// </summary>
    /// <typeparam name="T">The element type; compared by reference.</typeparam>
    /// <param name="target">The collection the view is bound to.</param>
    /// <param name="desired">The wanted contents, without duplicates.</param>
    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        // First drop what is no longer wanted, so the passes below only move or add
        var wanted = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        // Then walk the wanted order: everything before position i already matches
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
            {
                continue;
            }

            var existing = target.IndexOf(desired[i]);
            if (existing > i)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }
    }

    /// <summary>
    ///     Gets the header for a section, creating it (and wiring its requests) the first time it is
    ///     needed so the same instance is reused while the section exists.
    /// </summary>
    /// <param name="kind">The kind of section.</param>
    /// <param name="name">The section's display name.</param>
    /// <returns>The section's header.</returns>
    private RepoGroupHeaderViewModel GetHeader(RepoGroupKind kind, string name)
    {
        var key = $"{kind}:{name}";
        if (_headers.TryGetValue(key, out var existing) && string.Equals(existing.Name, name, StringComparison.Ordinal))
        {
            return existing;
        }

        var header = new RepoGroupHeaderViewModel(kind, name);
        header.ToggleRequested += (_, _) => ToggleGroupCollapsed(header.Name);
        header.RenameRequested += (_, _) => GroupRenameRequested?.Invoke(this, header.Name);
        header.RemoveGroupRequested += (_, _) => GroupRemoveRequested?.Invoke(this, header.Name);
        header.AddRepoRequested += (_, _) => GroupAddRepoRequested?.Invoke(this, header.Name);
        header.MoveRequested += (_, move) => MoveGroup(header.Name, move);
        _headers[key] = header;
        return header;
    }

    /// <summary>
    ///     Gets whether a section (a group, or the built-in Favorites or Ungrouped section) is
    ///     remembered as collapsed.
    /// </summary>
    /// <param name="name">The section's name; letter case is ignored.</param>
    /// <returns><see langword="true"/> when the user collapsed the section.</returns>
    public bool IsGroupCollapsed(string name) =>
        (_settings.CollapsedGroups ?? []).Contains(name, RepoGroupName.Comparer);

    /// <summary>
    ///     Collapses an expanded section, or expands a collapsed one, remembers that in the settings
    ///     and refreshes the list.
    /// </summary>
    /// <remarks>
    ///     Allowed while a filter is active (the remembered state changes, the filtered list keeps
    ///     showing every match), though the header's toggle is disabled then.
    /// </remarks>
    /// <param name="name">The section's name (a group name, or <c>Favorites</c> or
    ///     <c>Ungrouped</c>); letter case is ignored.</param>
    public void ToggleGroupCollapsed(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var collapse = !IsGroupCollapsed(name);
        CommitSettingsChange(settings =>
        {
            settings.CollapsedGroups ??= [];
            settings.CollapsedGroups.RemoveAll(entry => RepoGroupName.Comparer.Equals(entry, name));
            if (collapse)
            {
                settings.CollapsedGroups.Add(name);
            }
        });

        UpdateDisplayedRepoCards();
    }

    /// <summary>
    ///     Renames a group: every repo in it moves to the new name, and its collapsed state, its color
    ///     and its place in the user's group order move with it.
    /// </summary>
    /// <param name="oldName">The group's current name; letter case is ignored.</param>
    /// <param name="newName">The new name. It must be valid (see <see cref="RepoGroupName.Validate"/>)
    ///     and must not be another existing group's name; changing only the letter case of the
    ///     group's own name is allowed.</param>
    /// <returns><see langword="true"/> when the group was renamed (or the name was already so);
    ///     <see langword="false"/> when the group does not exist or the new name cannot be used, in
    ///     which case nothing changed.</returns>
    public bool RenameGroup(string oldName, string newName)
    {
        var target = RepoGroupName.Normalize(newName);
        if (target is null ||
            RepoGroupName.Validate(target) != RepoGroupNameProblem.None ||
            !GroupNames.Contains(oldName, RepoGroupName.Comparer) ||
            GroupNames.Any(other =>
                RepoGroupName.Comparer.Equals(other, target) && !RepoGroupName.Comparer.Equals(other, oldName)))
        {
            return false;
        }

        // Every member takes the new spelling; the collapsed state, the color and the place in the
        // user's group order follow the name
        var wasCollapsed = IsGroupCollapsed(oldName);
        var color = _settings.FindGroupColor(oldName);
        var order = (_settings.GroupOrder ?? [])
            .Select(entry => RepoGroupName.Comparer.Equals(entry, oldName) ? target : entry)
            .ToList();
        CommitSettingsChange(settings =>
        {
            foreach (var repo in settings.RecentRepos.Where(repo => IsInGroup(repo, oldName)))
            {
                repo.GroupName = target;
            }

            settings.CollapsedGroups?.RemoveAll(entry => RepoGroupName.Comparer.Equals(entry, oldName));
            if (wasCollapsed)
            {
                (settings.CollapsedGroups ??= []).Add(target);
            }

            RemoveGroupColor(settings, oldName);
            if (color is not null)
            {
                settings.GroupColors[target] = color;
            }

            settings.GroupOrder = [.. order];
        });

        FinishGroupEdit();
        return true;
    }

    /// <summary>
    ///     Removes every color entry of a group (letter case ignored) from some settings.
    /// </summary>
    /// <param name="settings">The settings to change.</param>
    /// <param name="groupName">The group's name.</param>
    private static void RemoveGroupColor(AppSettings settings, string groupName)
    {
        settings.GroupColors ??= new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var key in settings.GroupColors.Keys.Where(key => RepoGroupName.Comparer.Equals(key, groupName)).ToList())
        {
            settings.GroupColors.Remove(key);
        }
    }

    /// <summary>
    ///     Removes a group: its repos become ungrouped (nothing on disk or in any repository changes)
    ///     and its remembered collapsed state, color and place in the group order are forgotten.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns><see langword="true"/> when the group existed and was removed.</returns>
    public bool RemoveGroup(string name)
    {
        if (!GroupNames.Contains(name, RepoGroupName.Comparer))
        {
            return false;
        }

        CommitSettingsChange(settings =>
        {
            foreach (var repo in settings.RecentRepos.Where(repo => IsInGroup(repo, name)))
            {
                repo.GroupName = null;
            }

            settings.CollapsedGroups?.RemoveAll(entry => RepoGroupName.Comparer.Equals(entry, name));
            RemoveGroupColor(settings, name);
            settings.GroupOrder?.RemoveAll(entry => RepoGroupName.Comparer.Equals(entry, name));
        });

        FinishGroupEdit();
        return true;
    }

    /// <summary>
    ///     Brings the cards and the list up to date after a group rename, removal or color change.
    /// </summary>
    private void FinishGroupEdit()
    {
        foreach (var card in RepoCards)
        {
            card.RaiseGroupPropertiesChanged();
        }

        UpdateDisplayedRepoCards();
    }

    /// <summary>
    ///     Gives one of the user's groups a color from the card color palette, or takes its color
    ///     away, and saves the choice.
    /// </summary>
    /// <remarks>
    ///     The color shows as an accent on the group's header and is the tile color of the group's
    ///     repos that have no card color of their own (a card's own color wins). It is stored only in
    ///     the per-user settings (<see cref="AppSettings.GroupColors"/>), never in a repository; it is
    ///     applied at once and saved in the background (see <see cref="CommitSettingsChange"/>).
    ///     Favorites and Ungrouped are not groups of the user's own and cannot be colored.
    /// </remarks>
    /// <param name="groupName">The group's name; letter case is ignored.</param>
    /// <param name="colorName">A palette color's name (letter case ignored), or
    ///     <see langword="null"/> (or blank) for no color.</param>
    /// <returns><see langword="true"/> when the group now has the requested color (including when it
    ///     already had); <see langword="false"/> when the group does not exist (or is a built-in
    ///     section) or the color is not in the palette - nothing changed then.</returns>
    public bool SetGroupColor(string groupName, string? colorName)
    {
        var name = RepoGroupName.Sanitize(groupName);
        var requested = string.IsNullOrWhiteSpace(colorName) ? null : CardColorPalette.Find(colorName);
        if (name is null ||
            !GroupNames.Contains(name, RepoGroupName.Comparer) ||
            (requested is null && !string.IsNullOrWhiteSpace(colorName)))
        {
            return false;
        }

        // Stored under the group's own spelling; nothing to save when the color is already so
        var group = RepoGroupName.Canonicalize(name, GroupNames);
        var color = requested?.Name;
        if (string.Equals(_settings.FindGroupColor(group), color, StringComparison.Ordinal))
        {
            return true;
        }

        CommitSettingsChange(settings =>
        {
            RemoveGroupColor(settings, group);
            if (color is not null)
            {
                settings.GroupColors[group] = color;
            }
        });

        FinishGroupEdit();
        return true;
    }

    /// <summary>
    ///     The explanation shown (as a tooltip on the disabled Move items of a group header's menu)
    ///     while the filter has text and so the order of the groups cannot be changed.
    /// </summary>
    public const string GroupReorderDisabledWhileFilteringMessage =
        "Clear the filter to change the order of the groups: while the filter has text only some groups " +
        "are shown, so their positions cannot be changed.";

    /// <summary>
    ///     Gets the groups that have a section of their own (at least one repo that is not a
    ///     favorite), in the order the list shows them - the groups a Move or a drag can reorder.
    ///     (The pure arranging of names is <see cref="RepoGroupHierarchy.ArrangeGroups"/>.)
    /// </summary>
    /// <returns>The groups' names, first to last.</returns>
    private List<string> GetMovableGroups() =>
        RepoGroupHierarchy.ArrangeGroups(
            RepoCards.Where(card => !card.IsFavorite && card.HasGroup).Select(card => card.GroupName!),
            _settings.GroupOrder);

    /// <summary>
    ///     Gets why a group cannot be moved up or down now.
    /// </summary>
    /// <remarks>
    ///     The wording is what the header's menu shows as the tooltip of the disabled item. A group is
    ///     moved among the groups that have a section, so a group whose repos are all favorites (it has
    ///     no header) cannot be moved; Favorites stays first and Ungrouped last.
    /// </remarks>
    /// <param name="groupName">The group's name; letter case is ignored.</param>
    /// <param name="move">The move.</param>
    /// <returns>The reason, or <see langword="null"/> when the move is allowed: the filter has no
    ///     text, the group has a section, and it is not already at that end of the groups.</returns>
    public string? GetGroupMoveDisabledReason(string groupName, RepoGroupMove move)
    {
        ArgumentNullException.ThrowIfNull(groupName);

        if (!IsReorderEnabled)
        {
            return GroupReorderDisabledWhileFilteringMessage;
        }

        if (RepoGroupName.IsReserved(groupName))
        {
            return "Favorites always come first and Ungrouped last, so they cannot be moved.";
        }

        var groups = GetMovableGroups();
        var index = groups.FindIndex(group => RepoGroupName.Comparer.Equals(group, groupName));
        return index switch
        {
            < 0 => "This group is no longer in the list.",
            0 when move == RepoGroupMove.Up => "This group is already the first group.",
            _ when move == RepoGroupMove.Down && index == groups.Count - 1 => "This group is already the last group.",
            _ => null
        };
    }

    /// <summary>
    ///     Gets whether a group can be moved the given way now.
    /// </summary>
    /// <param name="groupName">The group's name; letter case is ignored.</param>
    /// <param name="move">The move.</param>
    /// <returns><see langword="true"/> when <see cref="GetGroupMoveDisabledReason"/> has no reason.</returns>
    public bool CanMoveGroup(string groupName, RepoGroupMove move) => GetGroupMoveDisabledReason(groupName, move) is null;

    /// <summary>
    ///     Moves a group one place up or down among the groups and saves the new order.
    /// </summary>
    /// <remarks>
    ///     The group swaps places with the group before (or after) it in the list. The whole order of
    ///     the groups as shown is then stored (<see cref="AppSettings.GroupOrder"/>), so groups that had
    ///     no stored position yet keep the place they were shown at. Nothing changes (and nothing is
    ///     saved) when the move is not allowed.
    /// </remarks>
    /// <param name="groupName">The group's name; letter case is ignored.</param>
    /// <param name="move">The move.</param>
    /// <returns><see langword="true"/> when the group was moved; <see langword="false"/> when the move
    ///     is not allowed (see <see cref="GetGroupMoveDisabledReason"/>); nothing changed then.</returns>
    public bool MoveGroup(string groupName, RepoGroupMove move)
    {
        if (!CanMoveGroup(groupName, move))
        {
            return false;
        }

        var groups = GetMovableGroups();
        var index = groups.FindIndex(group => RepoGroupName.Comparer.Equals(group, groupName));
        var neighbor = groups[move == RepoGroupMove.Up ? index - 1 : index + 1];
        return PlaceGroup(groups[index], neighbor, insertBefore: move == RepoGroupMove.Up);
    }

    /// <summary>
    ///     Gets whether a dragged group header may be dropped on another group's header.
    /// </summary>
    /// <remarks>
    ///     Pure: nothing is changed. Refused while the filter has text, for a group (dragged or
    ///     target) that has no section of its own, for Favorites and Ungrouped, and for a drop on the
    ///     dragged group's own header.
    /// </remarks>
    /// <param name="groupName">The dragged group's name; letter case is ignored.</param>
    /// <param name="target">The place.</param>
    /// <returns><see langword="true"/> when <see cref="DropGroup"/> would accept the drop.</returns>
    public bool CanDropGroup(string groupName, GroupDropTarget target)
    {
        ArgumentNullException.ThrowIfNull(groupName);
        ArgumentNullException.ThrowIfNull(target);

        if (!IsReorderEnabled || RepoGroupName.Comparer.Equals(groupName, target.GroupName))
        {
            return false;
        }

        var groups = GetMovableGroups();
        return groups.Contains(groupName, RepoGroupName.Comparer) && groups.Contains(target.GroupName, RepoGroupName.Comparer);
    }

    /// <summary>
    ///     Drops a dragged group header before or after another group's header, and saves the new
    ///     order of the groups.
    /// </summary>
    /// <remarks>
    ///     Stores the whole order of the groups as shown (see <see cref="MoveGroup"/>). A collapsed
    ///     group stays collapsed. A drop that leaves the order as it was saves nothing.
    /// </remarks>
    /// <param name="groupName">The dragged group's name; letter case is ignored.</param>
    /// <param name="target">The place; see <see cref="CanDropGroup"/> for what is refused.</param>
    /// <returns><see langword="true"/> when the group is now at the place; <see langword="false"/> when
    ///     the drop was refused.</returns>
    public bool DropGroup(string groupName, GroupDropTarget target)
    {
        if (!CanDropGroup(groupName, target))
        {
            return false;
        }

        var groups = GetMovableGroups();
        var dragged = groups.First(group => RepoGroupName.Comparer.Equals(group, groupName));
        var anchor = groups.First(group => RepoGroupName.Comparer.Equals(group, target.GroupName));
        return PlaceGroup(dragged, anchor, target.InsertBefore);
    }

    /// <summary>
    ///     Puts a group before or after another one in the order of all groups, saves that order and
    ///     updates the list.
    /// </summary>
    /// <param name="groupName">The group to place, as spelled.</param>
    /// <param name="anchor">The group to place it next to, as spelled.</param>
    /// <param name="insertBefore"><see langword="true"/> to place it before <paramref name="anchor"/>,
    ///     <see langword="false"/> after it.</param>
    /// <returns><see langword="true"/>: the group is at the place (including when it already was, in
    ///     which case nothing is saved).</returns>
    private bool PlaceGroup(string groupName, string anchor, bool insertBefore)
    {
        // Every group (also those whose repos are all favorites) keeps a place, so the order stays
        // complete
        var current = RepoGroupHierarchy.ArrangeGroups(GroupNames, _settings.GroupOrder);
        var order = current.Where(group => !RepoGroupName.Comparer.Equals(group, groupName)).ToList();
        var index = order.FindIndex(group => RepoGroupName.Comparer.Equals(group, anchor));
        order.Insert(insertBefore ? index : index + 1, groupName);
        if (order.SequenceEqual(current, RepoGroupName.Comparer))
        {
            return true;
        }

        CommitSettingsChange(settings => settings.GroupOrder = [.. order]);
        UpdateDisplayedRepoCards();
        return true;
    }

    /// <summary>
    ///     Gets whether a repo's entry is in the named group (letter case ignored; whatever the entry
    ///     holds is read through <see cref="RepoGroupName.Sanitize"/>).
    /// </summary>
    /// <param name="repo">The repo's entry.</param>
    /// <param name="groupName">The group's name.</param>
    /// <returns><see langword="true"/> when the repo is in that group.</returns>
    private static bool IsInGroup(RecentRepo repo, string groupName) =>
        RepoGroupName.Comparer.Equals(RepoGroupName.Sanitize(repo.GroupName) ?? string.Empty, groupName);

    /// <summary>
    ///     Raised when the user asks to rename a group (its name is the argument); the window shows
    ///     the name prompt and calls <see cref="RenameGroup"/>.
    /// </summary>
    public event EventHandler<string>? GroupRenameRequested;

    /// <summary>
    ///     Raised when the user asks to remove a group (its name is the argument); the window
    ///     confirms and calls <see cref="RemoveGroup"/>.
    /// </summary>
    public event EventHandler<string>? GroupRemoveRequested;

    /// <summary>
    ///     Raised when the user asks to add a repository to a group from its header's menu (the
    ///     group's name is the argument); the window shows the folder picker (several folders may be
    ///     chosen) and calls <see cref="AddRepos"/> with the group.
    /// </summary>
    public event EventHandler<string>? GroupAddRepoRequested;

    /// <summary>
    ///     Gets the section a card is listed in: Favorites for a favorite, otherwise its group, otherwise
    ///     Ungrouped.
    /// </summary>
    /// <remarks>
    ///     The same rule that <see cref="DisplayedItems"/> uses to build the sections. When no repo is in
    ///     a group the list has no headers, and this still yields two implicit tiers (Favorites, then
    ///     everything else as Ungrouped) that the favorites-first flat list is made of.
    /// </remarks>
    /// <param name="card">The card.</param>
    /// <returns>The section's kind and display name.</returns>
    internal static (RepoGroupKind Kind, string Name) GetSectionOf(RepoCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (card.IsFavorite)
        {
            return (RepoGroupKind.Favorites, RepoGroupName.FavoritesName);
        }

        return card.GroupName is { } group
            ? (RepoGroupKind.Group, group)
            : (RepoGroupKind.Ungrouped, RepoGroupName.UngroupedName);
    }

    /// <summary>
    ///     Gets whether a card is listed in the given section.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <param name="kind">The section's kind.</param>
    /// <param name="name">The section's display name (compared ignoring letter case).</param>
    /// <returns><see langword="true"/> when the card is in that section.</returns>
    private static bool IsInSection(RepoCardViewModel card, RepoGroupKind kind, string name)
    {
        var section = GetSectionOf(card);
        return section.Kind == kind && RepoGroupName.Comparer.Equals(section.Name, name);
    }

    /// <summary>
    ///     Gets why a card cannot be moved the given way now.
    /// </summary>
    /// <remarks>
    ///     The wording is what the menu shows as the tooltip of the disabled item, so the filter
    ///     explanation is the same everywhere (see <see cref="ReorderDisabledWhileFilteringMessage"/>).
    /// </remarks>
    /// <param name="card">The card.</param>
    /// <param name="move">The move.</param>
    /// <returns>The reason, or <see langword="null"/> when the move is allowed: the filter has no text,
    ///     the card is in the list, and it is not already at the place the move leads to.</returns>
    public string? GetMoveDisabledReason(RepoCardViewModel card, RepoMove move)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (!IsReorderEnabled)
        {
            return ReorderDisabledWhileFilteringMessage;
        }

        if (!RepoCards.Contains(card))
        {
            return "This repo is no longer in the list.";
        }

        var members = GetSectionMembers(card);
        var index = members.IndexOf(card);
        var atTop = index == 0;
        var atBottom = index == members.Count - 1;
        return move switch
        {
            RepoMove.Top or RepoMove.Up when atTop => "This repo is already the first one in its section.",
            RepoMove.Down or RepoMove.Bottom when atBottom => "This repo is already the last one in its section.",
            _ => null
        };
    }

    /// <summary>
    ///     Gets why a card's whole Reorder submenu is unavailable now.
    /// </summary>
    /// <remarks>
    ///     The submenu is unavailable while the filter has text (with the same explanation as the
    ///     individual Move items), when the card is not in the list, and when none of the four moves
    ///     applies, which is the case for the only repo of its section.
    /// </remarks>
    /// <param name="card">The card.</param>
    /// <returns>The reason, or <see langword="null"/> when at least one move applies.</returns>
    public string? GetReorderDisabledReason(RepoCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var reasons = new[] { RepoMove.Top, RepoMove.Up, RepoMove.Down, RepoMove.Bottom }
            .Select(move => GetMoveDisabledReason(card, move))
            .ToList();
        if (reasons.Contains(null))
        {
            return null;
        }

        // The filter and "not in the list" reasons are the same for every move; otherwise no move
        // leads anywhere because the card is the only one of its section
        return reasons[0] is ReorderDisabledWhileFilteringMessage or "This repo is no longer in the list."
            ? reasons[0]
            : "This repo is the only one in its section.";
    }

    /// <summary>
    ///     Gets whether a card can be moved the given way now.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <param name="move">The move.</param>
    /// <returns><see langword="true"/> when <see cref="GetMoveDisabledReason"/> has no reason.</returns>
    public bool CanMoveCard(RepoCardViewModel card, RepoMove move) => GetMoveDisabledReason(card, move) is null;

    /// <summary>
    ///     Moves a card to the top, one up, one down or to the bottom of its section, and saves the
    ///     new order.
    /// </summary>
    /// <remarks>
    ///     The section is the one the card is listed in (see <see cref="GetSectionOf(RepoCardViewModel)"/>); other
    ///     sections' cards, which may be interleaved with it in the stored list, keep their relative
    ///     order. Nothing changes (and nothing is saved) when the move is not allowed.
    /// </remarks>
    /// <param name="card">The card to move.</param>
    /// <param name="move">The move.</param>
    /// <returns><see langword="true"/> when the card was moved; <see langword="false"/> when the move
    ///     is not allowed (see <see cref="GetMoveDisabledReason"/>); nothing changed then.</returns>
    public bool MoveCard(RepoCardViewModel card, RepoMove move)
    {
        if (!CanMoveCard(card, move))
        {
            return false;
        }

        var members = GetSectionMembers(card);
        var index = members.IndexOf(card);
        return move switch
        {
            RepoMove.Top => PlaceCard(card, members[0], insertBefore: true),
            RepoMove.Up => PlaceCard(card, members[index - 1], insertBefore: true),
            RepoMove.Down => PlaceCard(card, members[index + 1], insertBefore: false),
            _ => PlaceCard(card, members[^1], insertBefore: false)
        };
    }

    /// <summary>
    ///     Gets whether a dragged card may be dropped at a place.
    /// </summary>
    /// <remarks>
    ///     Pure: nothing is changed. Refused while the filter has text, for a card that is not in the
    ///     list, for an anchor that is not (or no longer) in the target section, for a header of a
    ///     group that no longer exists, and in the flat list (no groups) for a place in the other
    ///     tier - without headers a drop there could not explain the favorite change it would imply,
    ///     so the flat list only reorders. Dropping a card on itself is allowed and changes nothing.
    /// </remarks>
    /// <param name="card">The dragged card.</param>
    /// <param name="target">The place.</param>
    /// <returns><see langword="true"/> when <see cref="DropCard"/> would accept the drop.</returns>
    public bool CanDrop(RepoCardViewModel card, RepoDropTarget target)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(target);

        if (!IsReorderEnabled || !RepoCards.Contains(card))
        {
            return false;
        }

        if (target.Anchor is { } anchor &&
            (!RepoCards.Contains(anchor) || !IsInSection(anchor, target.Kind, target.Name)))
        {
            // A stale target: the anchor was removed or has moved to another section
            return false;
        }

        if (target.Anchor is null && target.Kind == RepoGroupKind.Group &&
            !GroupNames.Contains(target.Name, RepoGroupName.Comparer))
        {
            return false;
        }

        // The flat list has no headers to explain a favorite or group change, so it only reorders
        return HasGroups || IsInSection(card, target.Kind, target.Name);
    }

    /// <summary>
    ///     Drops a dragged card at a place: moves it into the target section when it is in another one,
    ///     then puts it at the drop position, and saves.
    /// </summary>
    /// <remarks>
    ///     Changing the section goes through the same paths as the menu and the star:
    ///     <see cref="RepoCardViewModel.MoveToGroup"/> for a group (or none, for Ungrouped) and
    ///     <see cref="RepoCardViewModel.IsFavorite"/> for Favorites; dropping into Favorites marks the
    ///     repo as a favorite and keeps its group, and leaving Favorites un-favorites it (and sets the
    ///     group the target section implies). The position is then set as a second change; both are
    ///     applied at once and saved in the background (the writer folds them into one write). A
    ///     collapsed target section stays collapsed (its header count shows the change). A drop on a
    ///     header (no anchor) puts the card at the end of that section.
    /// </remarks>
    /// <param name="card">The dragged card.</param>
    /// <param name="target">The place; see <see cref="CanDrop"/> for what is refused.</param>
    /// <returns><see langword="true"/> when the card is now at the place; <see langword="false"/> when
    ///     the drop was refused.</returns>
    public bool DropCard(RepoCardViewModel card, RepoDropTarget target)
    {
        if (!CanDrop(card, target))
        {
            return false;
        }

        if (!IsInSection(card, target.Kind, target.Name))
        {
            switch (target.Kind)
            {
                case RepoGroupKind.Favorites:
                    card.IsFavorite = true;
                    break;

                case RepoGroupKind.Group:
                    card.IsFavorite = false;
                    card.MoveToGroup(target.Name);
                    break;

                default:
                    card.IsFavorite = false;
                    card.MoveToGroup(null);
                    break;
            }

            // A change the card refused leaves it where it was: stop
            if (!IsInSection(card, target.Kind, target.Name))
            {
                return false;
            }
        }

        return PlaceCard(card, target.Anchor, target.InsertBefore);
    }

    /// <summary>
    ///     Gets the cards of the section a card is listed in, in the stored order.
    /// </summary>
    /// <param name="card">A card of the section.</param>
    /// <returns>The section's cards, including <paramref name="card"/> when it is in the list.</returns>
    private List<RepoCardViewModel> GetSectionMembers(RepoCardViewModel card)
    {
        var section = GetSectionOf(card);
        return [.. RepoCards.Where(other => IsInSection(other, section.Kind, section.Name))];
    }

    /// <summary>
    ///     Puts a card next to an anchor card or at the end of its section in the stored order, saves
    ///     that order and updates the list.
    /// </summary>
    /// <param name="card">The card to place.</param>
    /// <param name="anchor">The card to place it next to, or <see langword="null"/> for the end of the
    ///     card's current section (after the section's last other member, or the end of the list when
    ///     it has none).</param>
    /// <param name="insertBefore"><see langword="true"/> to place it before <paramref name="anchor"/>,
    ///     <see langword="false"/> after it.</param>
    /// <returns><see langword="true"/>: the card is at the place (including when it already was, in
    ///     which case nothing is saved).</returns>
    private bool PlaceCard(RepoCardViewModel card, RepoCardViewModel? anchor, bool insertBefore)
    {
        // Placing a card next to itself is the identity
        if (ReferenceEquals(anchor, card))
        {
            return true;
        }

        var ordered = RepoCards.Where(other => !ReferenceEquals(other, card)).ToList();
        int index;
        if (anchor is not null)
        {
            index = ordered.IndexOf(anchor) + (insertBefore ? 0 : 1);
        }
        else
        {
            var section = GetSectionOf(card);
            index = ordered.FindLastIndex(other => IsInSection(other, section.Kind, section.Name)) + 1;
            if (index == 0)
            {
                index = ordered.Count;
            }
        }

        ordered.Insert(index, card);
        if (ordered.SequenceEqual(RepoCards, ReferenceEqualityComparer.Instance))
        {
            return true;
        }

        var paths = ordered.Select(other => other.RepoPath).ToList();
        CommitSettingsChange(settings =>
        {
            ApplyRepoOrder(settings.RecentRepos, paths);
            settings.HasUserRepoOrder = true;
        });

        SyncCollection(RepoCards, ordered);
        UpdateDisplayedRepoCards();
        return true;
    }

    /// <summary>
    ///     Reorders a repo list in place to follow a list of paths.
    /// </summary>
    /// <remarks>
    ///     Paths are compared ignoring letter case. An entry whose path is not listed keeps its current
    ///     relative order after the listed ones (so nothing is ever dropped). The same list instance is
    ///     kept, because the settings share it by reference.
    /// </remarks>
    /// <param name="repos">The list to reorder.</param>
    /// <param name="orderedPaths">The wanted order of the repo paths.</param>
    private static void ApplyRepoOrder(List<RecentRepo> repos, IReadOnlyList<string> orderedPaths)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < orderedPaths.Count; i++)
        {
            rank.TryAdd(orderedPaths[i], i);
        }

        // OrderBy is stable: unlisted entries keep their relative order at the end
        var sorted = repos
            .OrderBy(repo => rank.TryGetValue(repo.Path, out var position) ? position : int.MaxValue)
            .ToList();
        repos.Clear();
        repos.AddRange(sorted);
    }

    /// <summary>
    ///     Subscribes to a card's <see cref="RepoCardViewModel.FavoriteChanged"/> and
    ///     <see cref="RepoCardViewModel.GroupChanged"/> events so
    ///     <see cref="DisplayedRepoCards"/> is kept current. The change itself has already been applied
    ///     by then (and scheduled to be saved): the card applies it through
    ///     <see cref="CommitRepoChange"/> and raises its event only when that accepted it. Deliberately
    ///     does <em>not</em> subscribe to
    ///     <c>LaunchRecorded</c>: nothing displayed depends on a launch, so a launch can never move
    ///     a card. Also deliberately does <em>not</em>
    ///     subscribe to <see cref="RepoCardViewModel.RemoveRequested"/> here - that event is a
    ///     view-layer wiring point only (the view shows a confirmation dialog and calls
    ///     <see cref="RemoveRepo"/> itself only if the user confirms); subscribing to it directly
    ///     from the view model would remove the card unconditionally, bypassing confirmation.
    /// </summary>
    /// <param name="card">The card to subscribe to.</param>
    private void AttachCardEvents(RepoCardViewModel card)
    {
        card.FavoriteChanged += (_, _) => UpdateDisplayedRepoCards();
        card.GroupChanged += (_, _) => UpdateDisplayedRepoCards();
    }


    /// <summary>
    ///     Creates a <see cref="RepoCardViewModel"/> for a recent-repos entry, wired to always
    ///     read the live <see cref="_settings"/>/<see cref="_startupOptions"/> fields, share
    ///     this view model's <see cref="PackageVersionCache"/> and save the card's own changes in the
    ///     background (see <see cref="CommitRepoChange"/>).
    /// </summary>
    /// <param name="recentRepo">The recent-repos entry to create a card for.</param>
    /// <returns>The created card, not yet refreshed.</returns>
    private RepoCardViewModel CreateCard(RecentRepo recentRepo) =>
        new(
            recentRepo,
            () => _settings,
            _packageVersionCache,
            _startupOptions,
            credentials: Credentials,
            commitChange: CommitRepoChange,
            registryFactory: _registryFactory,
            processStarter: _processStarter,
            shellDetector: _shellDetector,
            backgroundWork: _backgroundWork,
            refreshQueue: _refreshQueue,
            gitTimeouts: _gitTimeouts);

    /// <summary>
    ///     Creates the provider that the settings window's "Test connection" lists versions
    ///     through for a source being edited. A NuGet source gets a NuGet provider with the same
    ///     credential facilities and cache location the cards use; a Git source gets a Git
    ///     provider that runs the configured git executable, may show Git's own sign-in (the user
    ///     started the test) and refuses a git older than the supported version before listing
    ///     any tag.
    /// </summary>
    /// <param name="definition">The source as currently edited (not yet saved).</param>
    /// <returns>A provider for the source's kind: NuGet that can sign in through the
    ///     application's sign-in dialog, or Git as described above.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the definition is not an
    ///     acceptable NuGet source or, for a Git source, is not an acceptable Git source (an
    ///     unusable address, tag pattern, folder or package name).</exception>
    /// <exception cref="ArgumentException">Thrown when the definition is a Git source whose
    ///     settings the Git provider refuses.</exception>
    internal IPackageSourceProvider CreateConnectionTestProvider(PackageSourceDefinition definition) =>
        definition.Type == PackageSourceType.Git
            ? new GitSourceProvider(
                definition,
                new GitSourceOptions(
                    () => RepoCardViewModel.ResolveGitExecutablePath(_settings, _startupOptions),
                    RepoCardViewModel.GetGitCacheRoot(_startupOptions),
                    Interactive: true,
                    EnforceMinimumGitVersion: true))
            : new NuGetSourceProvider(definition, RepoCardViewModel.GetNuGetRootBase(_startupOptions), Credentials);
}

