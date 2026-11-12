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
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     What became of a repo that was asked to be added to a group (see
///     <see cref="MainWindowViewModel.AddRepoToGroup"/>).
/// </summary>
internal enum AddRepoResult
{
    /// <summary>
    ///     The repo was not added: its folder does not exist, or (for the plain add) it is already listed.
    /// </summary>
    NotAdded,

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
    MoveFailed
}

/// <summary>
///     View model for the main launcher window: owns the recent-repos list and the settings
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
    ///     The most upgrade checks that read package sources at the same time.
    /// </summary>
    private const int MaxConcurrentUpgradeChecks = 4;

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
    ///     The configuration directory settings are persisted to/from, or <see langword="null"/>
    ///     to use <see cref="SettingsStore.GetDefaultConfigDirectory"/>.
    /// </summary>
    private readonly string? _configDirectory;

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
    ///     <see cref="ApplySettings"/> and persisted by <see cref="SaveSettings"/>.
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
    ///     by <see cref="ApplySettings"/> after the settings were saved. <see langword="null"/>
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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    ///     <paramref name="upgradeRecheckInterval"/> is zero or negative.</exception>
    public MainWindowViewModel(
        AppSettings settings,
        StartupOptions? startupOptions = null,
        string? configDirectory = null,
        PackageCredentialContext? credentials = null,
        Action<ThemePreference>? applyTheme = null,
        TimeProvider? timeProvider = null,
        TimeSpan? upgradeRecheckInterval = null,
        Func<AppSettings, PackageSourceRegistry>? registryFactory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            upgradeRecheckInterval ?? DefaultUpgradeRecheckInterval, TimeSpan.Zero);

        _settings = settings;
        _registryFactory = registryFactory;
        _startupOptions = startupOptions;
        _configDirectory = configDirectory;
        _applyTheme = applyTheme;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        // Per architecture.md's repo-fact caching strategy, only the cheap checks run eagerly at
        // app launch - the working-tree dirty check is deferred/lazy (see RefreshDirtyStatus).
        foreach (var card in RepoCards)
        {
            card.RefreshCheap();
        }

        UpdateDisplayedRepoCards();
    }

    /// <summary>
    ///     Gets the recent-repos list, most-recently-used first, backing this view model's
    ///     underlying state (settings persistence, additions/removals).
    /// </summary>
    public ObservableCollection<RepoCardViewModel> RepoCards { get; }

    /// <summary>
    ///     Gets the filtered/sorted cards that are currently shown: those of
    ///     <see cref="RepoCards"/> that match <see cref="FilterText"/> (a case-insensitive substring
    ///     match against <see cref="RepoCardViewModel.RepoName"/>/<see cref="RepoCardViewModel.RepoPath"/>),
    ///     in the order <see cref="DisplayedItems"/> shows them, without the cards hidden inside
    ///     collapsed sections.
    /// </summary>
    /// <remarks>
    ///     See <see cref="DisplayedItems"/> for the order. When no repo is in a group the list is
    ///     flat: favorites first, then the most recently launched, stable otherwise.
    /// </remarks>
    public ObservableCollection<RepoCardViewModel> DisplayedRepoCards { get; }

    /// <summary>
    ///     Gets what the main window's list binds to: the cards of <see cref="DisplayedRepoCards"/>
    ///     interleaved with the <see cref="RepoGroupHeaderViewModel"/> that starts each section.
    /// </summary>
    /// <remarks>
    ///     <b>Sections.</b> When at least one repo is in a group (<see cref="HasGroups"/>) the list is
    ///     sectioned: <c>Favorites</c> first (the favorite repos - a favorite is listed only there,
    ///     not again under its group, and its card shows a group chip), then the user's groups
    ///     alphabetically, then <c>Ungrouped</c> last; sections with no matching repo have no header.
    ///     When no repo is in a group there are no headers at all and the list looks as it always
    ///     did, favorites first. Within a section repos are ordered by most recent launch (nulls
    ///     last), stable otherwise. <b>Collapsing.</b> The cards of a collapsed section are left out
    ///     (its header stays, with the count of its matches); the state is remembered per user in
    ///     <see cref="AppSettings.CollapsedGroups"/>. <b>Filtering.</b> While a filter is active
    ///     every section is shown expanded and sections without a match disappear, so a match is
    ///     never hidden; the remembered collapsed state is not changed by filtering.
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
    public IReadOnlyList<string> GroupNames =>
        RepoGroupName.DistinctNames(_settings.RecentRepos.Select(repo => repo.GroupName));

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
                UpdateDisplayedRepoCards();
            }
        }
    }

    /// <summary>
    ///     Gets the current application settings, for building a
    ///     <see cref="SettingsWindowViewModel"/> from the main window's code-behind.
    /// </summary>
    public AppSettings Settings => _settings;

    /// <summary>
    ///     Gets the credential facilities that NuGet server sources use to sign in, or
    ///     <see langword="null"/> when there are none.
    /// </summary>
    internal PackageCredentialContext? Credentials { get; }

    /// <summary>
    ///     Adds a repository to the recent-repos list (most-recently-used position) and persists
    ///     the updated settings.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root to add.</param>
    /// <returns>
    ///     <see langword="true"/> if the repo was added; <see langword="false"/> if
    ///     <paramref name="repoPath"/> does not exist or is already present in the list (compared
    ///     case-insensitively, since Windows filesystem paths are case-insensitive).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repoPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     the repo was not added (the list, the cards and the file are unchanged).</exception>
    public bool AddRepo(string repoPath) => AddRepoCore(repoPath, null) == AddRepoResult.Added;

    /// <summary>
    ///     Adds a repository to the recent-repos list straight into one of the user's groups, or, when
    ///     the repository is already listed, moves its existing entry into that group.
    /// </summary>
    /// <remarks>
    ///     The same add as <see cref="AddRepo"/> (same folder check, same pin loading, one save), with
    ///     the group set on the new entry before it is saved, so the repo never shows up ungrouped in
    ///     between. A group that is collapsed is expanded afterwards so the repo that was just added
    ///     is visible. The group is a per-user setting and is never written to the repository.
    /// </remarks>
    /// <param name="repoPath">Absolute path to the repository root to add.</param>
    /// <param name="groupName">The group to put the repo in; it must be a valid group name (see
    ///     <see cref="RepoGroupName.Validate"/>). A name matching an existing group ignoring letter
    ///     case joins that group under its own spelling.</param>
    /// <returns>What became of the repo (see <see cref="AddRepoResult"/>).</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repoPath"/> is null, empty or
    ///     whitespace, or <paramref name="groupName"/> is not a valid group name.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     the repo was not added (the list, the cards and the file are unchanged).</exception>
    public AddRepoResult AddRepoToGroup(string repoPath, string groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var requested = RepoGroupName.Normalize(groupName);
        if (requested is null || RepoGroupName.Validate(requested) != RepoGroupNameProblem.None)
        {
            throw new ArgumentException("The group name is not valid.", nameof(groupName));
        }

        var target = RepoGroupName.Canonicalize(requested, GroupNames);
        var result = AddRepoCore(repoPath, target);

        // The repo that was just added or moved must be visible, so a collapsed group is opened
        if (result is AddRepoResult.Added or AddRepoResult.MovedExisting && IsGroupCollapsed(target))
        {
            try
            {
                ToggleGroupCollapsed(target);
            }
            catch (InvalidOperationException)
            {
                // The repo is in its group; only remembering that the group is open failed
            }
        }

        return result;
    }

    /// <summary>
    ///     The shared add: checks the folder, then either adds a new entry (in
    ///     <paramref name="groupName"/> when given) or, for a repo already listed, moves its entry
    ///     into <paramref name="groupName"/> when given.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root.</param>
    /// <param name="groupName">The already validated and canonical group to put the repo in, or
    ///     <see langword="null"/> for none (the top-bar add).</param>
    /// <returns>What became of the repo.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     nothing was added.</exception>
    private AddRepoResult AddRepoCore(string repoPath, string? groupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        if (!Directory.Exists(repoPath))
        {
            return AddRepoResult.NotAdded;
        }

        // A repo that is already listed is not added twice; the group flow moves it instead
        var existing = RepoCards.FirstOrDefault(
            card => string.Equals(card.RepoPath, repoPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null || _settings.RecentRepos.Any(r => string.Equals(r.Path, repoPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (groupName is null || existing is null)
            {
                return AddRepoResult.NotAdded;
            }

            // A failed save is reported through SettingsSaveFailed by the card, and nothing moved
            return existing.MoveToGroup(groupName) ? AddRepoResult.MovedExisting : AddRepoResult.MoveFailed;
        }

        var pin = TryLoadPin(repoPath);
        var recentRepo = new RecentRepo
        {
            Path = repoPath,
            GroupName = groupName,
            PinnedPackageName = pin?.PackageName,
            PinnedPackageVersion = pin?.Version
        };

        // Prepare the card first, then persist; only a successful save puts the repo in the live
        // list, so a failed save leaves the list, the cards and the file exactly as they were
        var card = CreateCard(recentRepo);
        card.RefreshCheap();
        card.RefreshDirtyStatus();
        try
        {
            CommitSettingsChange(settings => settings.RecentRepos.Insert(0, recentRepo));
        }
        catch (InvalidOperationException)
        {
            card.Retire();
            throw;
        }

        AttachCardEvents(card);
        RepoCards.Insert(0, card);
        UpdateDisplayedRepoCards();

        // The new card's upgrade check reads its package source, so it runs in the background
        // rather than delaying the card's appearance
        BeginSingleCardUpgradeRefresh(card);
        return AddRepoResult.Added;
    }

    /// <summary>
    ///     Removes a repo card from the recent-repos list, unconditionally, and persists the
    ///     updated settings.
    /// </summary>
    /// <remarks>
    ///     This method itself performs no confirmation - per the plan's event-based
    ///     view/view-model separation, <c>MainWindow</c>'s code-behind is responsible for showing
    ///     a confirmation dialog (via <c>ConfirmationWindow</c>) before calling this, in response
    ///     to <paramref name="card"/> raising <see cref="RepoCardViewModel.RemoveRequested"/>.
    ///     The updated settings are saved before the card is removed, so a failed save leaves the
    ///     list as it was; the removed card is then retired (see
    ///     <see cref="RepoCardViewModel.Retire"/>) so nothing it still has running can report back.
    /// </remarks>
    /// <param name="card">The card to remove.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="card"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     nothing was removed.</exception>
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
    ///     recent-repos list, which is only ever mutated via <see cref="AddRepo"/>/
    ///     <see cref="RemoveRepo"/>), persists them (adopting them only if that succeeds), invalidates the shared
    ///     <see cref="PackageVersionCache"/> for each package source that was changed or removed
    ///     (cached results for an edited source would otherwise be silently reused; sources that
    ///     did not change keep theirs), and
    ///     re-runs each card's cheap refresh (pin file, branch, committed-files badge, whether the
    ///     pin's source is configured) so those properties reflect any changed package-source/git-path
    ///     settings immediately, then starts the background upgrade-availability check
    ///     (<see cref="BeginUpgradeRefresh()"/>) against the new sources.
    /// </summary>
    /// <remarks>
    ///     Deliberately uses <see cref="RepoCardViewModel.RefreshCheap"/> rather than a full
    ///     <see cref="RepoCardViewModel.RefreshAsync"/> for every card: <c>GitClient</c>'s
    ///     synchronous, un-timed <c>git status</c> call means refreshing every tracked repo's
    ///     dirty status on the UI thread could freeze the window for a large or slow/networked
    ///     repo list. A fresh Git status check is instead requested only for the cards that
    ///     actually need one - those that reappeared (were missing, now present) since they were
    ///     last checked (unlike a newly-constructed card, an already-realized card's one-time
    ///     lazy <see cref="RepoCardViewModel.RefreshDirtyStatus"/> trigger in
    ///     <c>MainWindow.axaml.cs</c> will not fire again for it), or every present card when
    ///     <see cref="AppSettings.GitExecutablePath"/> itself changed - a previously-cached
    ///     <see cref="RepoCardViewModel.CanPull"/>/<see cref="RepoCardViewModel.PullTooltip"/>
    ///     computed against the old executable could otherwise keep being shown even though
    ///     subsequent Pull operations use the newly configured one.
    /// </remarks>
    /// <param name="updated">The updated settings, typically built by
    ///     <see cref="SettingsWindowViewModel.Save"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updated"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a source location embeds a
    ///     credential or the settings file cannot be written; the current settings are left
    ///     unchanged (the candidate is adopted only after it has been persisted).</exception>
    public void ApplySettings(AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        var gitExecutablePathChanged = !string.Equals(
            _settings.GitExecutablePath, updated.GitExecutablePath, StringComparison.Ordinal);

        // Note which sources were configured before the swap so cached results for any that were
        // edited or removed can be dropped afterwards
        var previousSources = _settings.GetEffectiveSources().ToList();

        // Refuse a credential-bearing source before adopting the settings, so the rejected values
        // never become the live in-memory settings either
        foreach (var source in updated.GetEffectiveSources())
        {
            var problem = source.GetCredentialProblem();
            if (problem is not null)
            {
                throw new InvalidOperationException($"Settings were not applied. {problem}");
            }
        }

        // Persist the candidate first and adopt it only once that succeeds: if the write fails,
        // the exception propagates to the caller (which shows it) while the live settings stay as
        // they were, so a later save (favorite, launch, ...) can never write the rejected values
        updated.RecentRepos = _settings.RecentRepos;
        updated.CollapsedGroups = _settings.CollapsedGroups;
        SettingsStore.Save(updated, _configDirectory);
        _settings = updated;
        OnPropertyChanged(nameof(Settings));

        // The new settings are saved: make the theme choice take effect straight away, without a
        // restart. A rejected save never reaches here, so a refused value is never applied.
        _applyTheme?.Invoke(updated.Theme);

        InvalidateChangedSources(previousSources, updated.GetEffectiveSources());
        foreach (var card in RepoCards)
        {
            var wasMissing = card.IsMissing;
            card.RefreshCheap();
            if ((wasMissing && !card.IsMissing) || (gitExecutablePathChanged && !card.IsMissing))
            {
                card.RefreshDirtyStatus();
            }
        }

        UpdateDisplayedRepoCards();

        // Re-check upgrades against the new sources in the background; any check still running
        // for the previous settings is cancelled
        BeginUpgradeRefresh();
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
    ///     cards pinned to it.
    /// </remarks>
    /// <returns>A task that completes when every card's check has finished (also available as
    ///     <see cref="PendingRefresh"/>).</returns>
    public Task BeginUpgradeRefresh() => StartUpgradeRefresh([.. RepoCards]);

    /// <summary>
    ///     Starts the background upgrade check for a single card (a repo that was just added),
    ///     without canceling the check of the other cards.
    /// </summary>
    /// <param name="card">The card to check.</param>
    private void BeginSingleCardUpgradeRefresh(RepoCardViewModel card)
    {
        // A disposed view model (the window closed) starts no check
        if (_disposed)
        {
            return;
        }

        var token = _refreshCancellation?.Token ?? CancellationToken.None;
        var previous = PendingRefresh;
        PendingRefresh = Task.WhenAll(previous, CheckUpgradesAsync([card], token));
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
            var failures = new SourceFailureMemo();
            using var throttle = new SemaphoreSlim(MaxConcurrentUpgradeChecks);
            var groups = cards
                .GroupBy(card => card.PinnedPackageName, StringComparer.OrdinalIgnoreCase)
                .Select(async group =>
                {
                    await throttle.WaitAsync(token);
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
                        throttle.Release();
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
            if (ReferenceEquals(_recheckCancellation, cancellation))
            {
                _recheckCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    ///     Determines whether a periodic round checks a card: one that is busy with an action of its
    ///     own, whose folder is missing, or that pins no package has nothing to re-check.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns><see langword="true"/> when the card's upgrade availability is re-checked.</returns>
    private static bool CanRecheckUpgrade(RepoCardViewModel card) =>
        !card.IsBusy && !card.IsMissing && card.PinnedPackageName is not null;

    /// <summary>
    ///     Stops the periodic upgrade re-check (see <see cref="StartPeriodicUpgradeRecheck"/>) and
    ///     cancels the round that is running and the background upgrade check (launch, settings
    ///     change, added repo) that is running; called when the main window closes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recheckTimer?.Dispose();
        _recheckTimer = null;
        CancelPeriodicRecheck();

        // The launch-time check must not keep waiting for a slow source (or publish to cards) after
        // the window closed
        var refresh = _refreshCancellation;
        _refreshCancellation = null;
        if (refresh is not null)
        {
            refresh.Cancel();
            refresh.Dispose();
        }
    }

    /// <summary>
    ///     Checks each card's upgrade availability, a few at a time.
    /// </summary>
    /// <param name="cards">The cards to check.</param>
    /// <param name="cancellationToken">Cancels the checks that have not completed.</param>
    /// <returns>A task that completes when every check has finished or been cancelled; a
    ///     cancelled check is not an error.</returns>
    /// <remarks>
    ///     Counted in <see cref="_launchChecksInFlight"/> from the call until it has unwound, so a
    ///     check that was superseded (and canceled) but is still inside a provider call that ignores
    ///     cancellation keeps holding off the periodic round, even though
    ///     <see cref="PendingRefresh"/> no longer refers to it.
    /// </remarks>
    private async Task CheckUpgradesAsync(
        IReadOnlyList<RepoCardViewModel> cards, CancellationToken cancellationToken)
    {
        // Counted before the first await, so the caller observes it as soon as this returns
        Interlocked.Increment(ref _launchChecksInFlight);
        try
        {
            using var throttle = new SemaphoreSlim(MaxConcurrentUpgradeChecks);

            // The failures of this check: once a source could not be read for a package, the cards that
            // follow get that failure instead of reading it again (cards that are already reading it
            // at the same moment still do, as they do for a listing that succeeds)
            var failures = new SourceFailureMemo();
            var checks = cards.Select(async card =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    await card.RefreshUpgradeStatusAsync(cancellationToken, failureMemo: failures);
                }
                finally
                {
                    throttle.Release();
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
            Interlocked.Decrement(ref _launchChecksInFlight);
        }
    }

    /// <summary>
    ///     Persists the current settings to the configured directory.
    /// </summary>
    /// <remarks>
    ///     Writes the live settings as they are. The changes the user makes in the main window do
    ///     not use this: they go through <see cref="CommitSettingsChange"/>, which saves a candidate
    ///     first and changes the live settings only when that succeeded.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be
    ///     written.</exception>
    public void SaveSettings() => SettingsStore.Save(_settings, _configDirectory);

    /// <summary>
    ///     Raised when a change the user made in the main window (marking a favorite, choosing a group
    ///     or a card color, collapsing a section, a launch being recorded) could not be saved. The
    ///     change was not applied, so the list still shows what is on disk; the argument is the
    ///     message for the user and the window shows it.
    /// </summary>
    public event EventHandler<string>? SettingsSaveFailed;

    /// <summary>
    ///     Makes a change to the settings all-or-nothing: the change is tried on a candidate copy,
    ///     the candidate is saved, and only when that succeeded is the same change applied to the
    ///     live settings.
    /// </summary>
    /// <remarks>
    ///     This is what keeps memory, the displayed list and the settings file in step. A failed save
    ///     leaves the live settings untouched, so a later, unrelated save can never write the change
    ///     that failed. The candidate copies the repos (so the change never reaches the live
    ///     entries) and the collapsed list; everything else is shared and must not be changed by
    ///     <paramref name="change"/>. The change runs twice (on the candidate, then on the live
    ///     settings), so it must give the same result both times and must not depend on state that the
    ///     first run changed.
    /// </remarks>
    /// <param name="change">The change, expressed on the settings it is given.</param>
    /// <exception cref="InvalidOperationException">Thrown when the candidate cannot be saved (the
    ///     file cannot be written, or a source embeds a credential); the live settings are unchanged.</exception>
    private void CommitSettingsChange(Action<AppSettings> change)
    {
        // Candidate copy: the user's repos and collapsed sections are copied, so the trial change
        // cannot reach the live objects
        var candidate = _settings.CreatePersistedCopy();
        candidate.RecentRepos = [.. _settings.RecentRepos.Select(repo => repo.Clone())];
        candidate.CollapsedGroups = [.. _settings.CollapsedGroups ?? []];
        change(candidate);

        // Save the candidate; an exception here leaves the live settings as they were
        SettingsStore.Save(candidate, _configDirectory);

        // Saved: adopt the change
        change(_settings);
    }

    /// <summary>
    ///     Applies a change to one repo's entry all-or-nothing, on behalf of its card (see
    ///     <see cref="CommitSettingsChange"/>); a failed save is reported to the user instead of
    ///     thrown, because the change comes from a button the user pressed.
    /// </summary>
    /// <param name="repo">The repo's entry; a repo that is no longer in the list (a card that was
    ///     removed) is changed without saving anything.</param>
    /// <param name="change">The change to the repo's entry.</param>
    /// <returns><see langword="true"/> when the change was saved and applied (or needed no saving);
    ///     <see langword="false"/> when the save failed, in which case nothing changed and
    ///     <see cref="SettingsSaveFailed"/> was raised.</returns>
    private bool CommitRepoChange(RecentRepo repo, Action<RecentRepo> change)
    {
        if (!_settings.RecentRepos.Contains(repo))
        {
            change(repo);
            return true;
        }

        try
        {
            CommitSettingsChange(settings =>
                change(settings.RecentRepos.First(
                    entry => string.Equals(entry.Path, repo.Path, StringComparison.OrdinalIgnoreCase))));
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ReportSaveFailure(ex);
            return false;
        }
    }

    /// <summary>
    ///     Tells the user (through <see cref="SettingsSaveFailed"/>) that a change was not saved and
    ///     so not applied.
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
    ///     <see cref="RepoCards"/>: filters by <see cref="FilterText"/>, orders by section, then by
    ///     most recent launch (see <see cref="DisplayedItems"/> for the section rules), and brings
    ///     both collections to that state with the fewest changes.
    /// </summary>
    /// <remarks>
    ///     The collections are updated in place (removing, moving and inserting only what differs)
    ///     rather than cleared and refilled, so the rows that stay keep their controls - and so the
    ///     keyboard focus - when, for example, a group is collapsed. The ordering uses
    ///     <see cref="Enumerable.OrderByDescending{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/>,
    ///     which is documented as a stable sort.
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
            // No group anywhere: a flat list, favorites first, exactly as before groups existed
            foreach (var card in filtered
                         .OrderByDescending(card => card.IsFavorite)
                         .ThenByDescending(card => card.LastLaunchedUtc ?? DateTimeOffset.MinValue))
            {
                items.Add(card);
                cards.Add(card);
            }
        }
        else
        {
            var matches = filtered
                .OrderByDescending(card => card.LastLaunchedUtc ?? DateTimeOffset.MinValue)
                .ToList();

            // A favorite is listed only under Favorites; every other repo under its group or, with
            // none, under Ungrouped
            AddSection(RepoGroupKind.Favorites, RepoGroupName.FavoritesName, matches.Where(card => card.IsFavorite));
            var groupNames = matches
                .Where(card => !card.IsFavorite && card.HasGroup)
                .Select(card => card.GroupName!)
                .Distinct(RepoGroupName.Comparer)
                .Order(RepoGroupName.Comparer);
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
        header.ToggleRequested += (_, _) => RunReportingSaveFailure(() => ToggleGroupCollapsed(header.Name));
        header.RenameRequested += (_, _) => GroupRenameRequested?.Invoke(this, header.Name);
        header.RemoveGroupRequested += (_, _) => GroupRemoveRequested?.Invoke(this, header.Name);
        header.AddRepoRequested += (_, _) => GroupAddRepoRequested?.Invoke(this, header.Name);
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
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     the section keeps its state.</exception>
    public void ToggleGroupCollapsed(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Decided once, from the live state, so the trial change and the real one agree
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
    ///     Renames a group: every repo in it moves to the new name, and its collapsed state moves
    ///     with it.
    /// </summary>
    /// <param name="oldName">The group's current name; letter case is ignored.</param>
    /// <param name="newName">The new name. It must be valid (see <see cref="RepoGroupName.Validate"/>)
    ///     and must not be another existing group's name; changing only the letter case of the
    ///     group's own name is allowed.</param>
    /// <returns><see langword="true"/> when the group was renamed (or the name was already so);
    ///     <see langword="false"/> when the group does not exist or the new name cannot be used, in
    ///     which case nothing changed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     the group is unchanged.</exception>
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

        // Every member takes the new spelling; the collapsed state follows the name
        var wasCollapsed = IsGroupCollapsed(oldName);
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
        });

        FinishGroupEdit();
        return true;
    }

    /// <summary>
    ///     Removes a group: its repos become ungrouped (nothing on disk or in any repository changes)
    ///     and its remembered collapsed state is forgotten.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns><see langword="true"/> when the group existed and was removed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written;
    ///     the group is unchanged.</exception>
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
        });

        FinishGroupEdit();
        return true;
    }

    /// <summary>
    ///     Brings the cards and the list up to date after a group rename or removal that was saved.
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
    ///     group's name is the argument); the window shows the folder picker and calls
    ///     <see cref="AddRepoToGroup"/>.
    /// </summary>
    public event EventHandler<string>? GroupAddRepoRequested;

    /// <summary>
    ///     Subscribes to a card's <see cref="RepoCardViewModel.FavoriteChanged"/>,
    ///     <see cref="RepoCardViewModel.GroupChanged"/> and <c>LaunchRecorded</c> events so
    ///     <see cref="DisplayedRepoCards"/> is kept current. The change itself has already been saved
    ///     by then: the card applies it through <see cref="CommitRepoChange"/> and raises its event
    ///     only when that succeeded. Deliberately does <em>not</em>
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
        card.LaunchRecorded += (_, _) => UpdateDisplayedRepoCards();
    }

    /// <summary>
    ///     Runs a change that saves settings from an event handler and, when the save fails, tells
    ///     the user instead of letting the exception escape into the UI framework.
    /// </summary>
    /// <param name="change">The change; it saves before applying, so a failure changed nothing.</param>
    private void RunReportingSaveFailure(Action change)
    {
        try
        {
            change();
        }
        catch (InvalidOperationException ex)
        {
            ReportSaveFailure(ex);
        }
    }

    /// <summary>
    ///     Creates a <see cref="RepoCardViewModel"/> for a recent-repos entry, wired to always
    ///     read the live <see cref="_settings"/>/<see cref="_startupOptions"/> fields, share
    ///     this view model's <see cref="PackageVersionCache"/> and save the card's own changes
    ///     all-or-nothing (see <see cref="CommitRepoChange"/>).
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
            registryFactory: _registryFactory);

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

    /// <summary>
    ///     Attempts to load the pin file for a newly-added repo, tolerating a repo that has never
    ///     been synced (no pin file yet).
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root.</param>
    /// <returns>The loaded pin, or <see langword="null"/> if none exists or it fails to load.</returns>
    private static RepoPin? TryLoadPin(string repoPath)
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
}

