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
internal sealed class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     The most upgrade checks that read package sources at the same time.
    /// </summary>
    private const int MaxConcurrentUpgradeChecks = 4;

    /// <summary>
    ///     Cancels the background upgrade check started by the most recent
    ///     <see cref="BeginUpgradeRefresh()"/>, or <see langword="null"/> before the first one.
    /// </summary>
    private CancellationTokenSource? _refreshCancellation;

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
    ///     The shared, session-scoped package-version cache passed to every
    ///     <see cref="RepoCardViewModel"/>, per architecture.md's repo-fact caching strategy.
    /// </summary>
    private readonly PackageVersionCache _packageVersionCache = new();

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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is
    ///     <see langword="null"/>.</exception>
    public MainWindowViewModel(
        AppSettings settings,
        StartupOptions? startupOptions = null,
        string? configDirectory = null,
        PackageCredentialContext? credentials = null,
        Action<ThemePreference>? applyTheme = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _startupOptions = startupOptions;
        _configDirectory = configDirectory;
        _applyTheme = applyTheme;
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
    public bool AddRepo(string repoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        if (!Directory.Exists(repoPath))
        {
            return false;
        }

        if (_settings.RecentRepos.Any(r => string.Equals(r.Path, repoPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var pin = TryLoadPin(repoPath);
        var recentRepo = new RecentRepo
        {
            Path = repoPath,
            PinnedPackageName = pin?.PackageName,
            PinnedPackageVersion = pin?.Version,
            PinnedSource = pin?.Source
        };

        _settings.RecentRepos.Insert(0, recentRepo);
        var card = CreateCard(recentRepo);
        AttachCardEvents(card);
        card.RefreshCheap();
        card.RefreshDirtyStatus();
        RepoCards.Insert(0, card);

        SaveSettings();
        UpdateDisplayedRepoCards();

        // The new card's upgrade check reads its package source, so it runs in the background
        // rather than delaying the card's appearance
        BeginSingleCardUpgradeRefresh(card);
        return true;
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
    /// </remarks>
    /// <param name="card">The card to remove.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="card"/> is
    ///     <see langword="null"/>.</exception>
    public void RemoveRepo(RepoCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        _settings.RecentRepos.RemoveAll(r => string.Equals(r.Path, card.RepoPath, StringComparison.OrdinalIgnoreCase));
        RepoCards.Remove(card);

        SaveSettings();
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
    ///     package version is available for every repo card, cancelling any such check still
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
    ///     without cancelling the check of the other cards.
    /// </summary>
    /// <param name="card">The card to check.</param>
    private void BeginSingleCardUpgradeRefresh(RepoCardViewModel card)
    {
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

        PendingRefresh = CheckUpgradesAsync(cards, cancellation.Token);
        return PendingRefresh;
    }

    /// <summary>
    ///     Checks each card's upgrade availability, a few at a time.
    /// </summary>
    /// <param name="cards">The cards to check.</param>
    /// <param name="cancellationToken">Cancels the checks that have not completed.</param>
    /// <returns>A task that completes when every check has finished or been cancelled; a
    ///     cancelled check is not an error.</returns>
    private static async Task CheckUpgradesAsync(
        IReadOnlyList<RepoCardViewModel> cards, CancellationToken cancellationToken)
    {
        using var throttle = new SemaphoreSlim(MaxConcurrentUpgradeChecks);
        var checks = cards.Select(async card =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                await card.RefreshUpgradeStatusAsync(cancellationToken);
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

    /// <summary>
    ///     Persists the current settings to the configured directory.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be
    ///     written.</exception>
    public void SaveSettings() => SettingsStore.Save(_settings, _configDirectory);

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
        header.ToggleRequested += (_, _) => ToggleGroupCollapsed(header.Name);
        header.RenameRequested += (_, _) => GroupRenameRequested?.Invoke(this, header.Name);
        header.RemoveGroupRequested += (_, _) => GroupRemoveRequested?.Invoke(this, header.Name);
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
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written.</exception>
    public void ToggleGroupCollapsed(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _settings.CollapsedGroups ??= [];
        if (_settings.CollapsedGroups.RemoveAll(entry => RepoGroupName.Comparer.Equals(entry, name)) == 0)
        {
            _settings.CollapsedGroups.Add(name);
        }

        SaveSettings();
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
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written.</exception>
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
        foreach (var repo in _settings.RecentRepos.Where(
                     repo => RepoGroupName.Comparer.Equals(RepoGroupName.Sanitize(repo.GroupName) ?? string.Empty, oldName)))
        {
            repo.GroupName = target;
        }

        var wasCollapsed = IsGroupCollapsed(oldName);
        _settings.CollapsedGroups?.RemoveAll(entry => RepoGroupName.Comparer.Equals(entry, oldName));
        if (wasCollapsed)
        {
            (_settings.CollapsedGroups ??= []).Add(target);
        }

        FinishGroupEdit();
        return true;
    }

    /// <summary>
    ///     Removes a group: its repos become ungrouped (nothing on disk or in any repository changes)
    ///     and its remembered collapsed state is forgotten.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns><see langword="true"/> when the group existed and was removed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be written.</exception>
    public bool RemoveGroup(string name)
    {
        if (!GroupNames.Contains(name, RepoGroupName.Comparer))
        {
            return false;
        }

        foreach (var repo in _settings.RecentRepos.Where(
                     repo => RepoGroupName.Comparer.Equals(RepoGroupName.Sanitize(repo.GroupName) ?? string.Empty, name)))
        {
            repo.GroupName = null;
        }

        _settings.CollapsedGroups?.RemoveAll(entry => RepoGroupName.Comparer.Equals(entry, name));
        FinishGroupEdit();
        return true;
    }

    /// <summary>
    ///     Persists a group rename or removal and brings the cards and the list up to date.
    /// </summary>
    private void FinishGroupEdit()
    {
        SaveSettings();
        foreach (var card in RepoCards)
        {
            card.RaiseGroupPropertiesChanged();
        }

        UpdateDisplayedRepoCards();
    }

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
    ///     Subscribes to a card's <see cref="RepoCardViewModel.FavoriteChanged"/>,
    ///     <see cref="RepoCardViewModel.GroupChanged"/>, <see cref="RepoCardViewModel.CardColorChanged"/> and
    ///     <c>LaunchRecorded</c> events so settings are persisted and
    ///     <see cref="DisplayedRepoCards"/> is kept current. Deliberately does <em>not</em>
    ///     subscribe to <see cref="RepoCardViewModel.RemoveRequested"/> here - that event is a
    ///     view-layer wiring point only (the view shows a confirmation dialog and calls
    ///     <see cref="RemoveRepo"/> itself only if the user confirms); subscribing to it directly
    ///     from the view model would remove the card unconditionally, bypassing confirmation.
    /// </summary>
    /// <param name="card">The card to subscribe to.</param>
    private void AttachCardEvents(RepoCardViewModel card)
    {
        card.FavoriteChanged += (_, _) =>
        {
            SaveSettings();
            UpdateDisplayedRepoCards();
        };
        card.GroupChanged += (_, _) =>
        {
            SaveSettings();
            UpdateDisplayedRepoCards();
        };
        card.CardColorChanged += (_, _) => SaveSettings();
        card.LaunchRecorded += (_, _) =>
        {
            SaveSettings();
            UpdateDisplayedRepoCards();
        };
    }

    /// <summary>
    ///     Creates a <see cref="RepoCardViewModel"/> for a recent-repos entry, wired to always
    ///     read the live <see cref="_settings"/>/<see cref="_startupOptions"/> fields and share
    ///     this view model's <see cref="PackageVersionCache"/>.
    /// </summary>
    /// <param name="recentRepo">The recent-repos entry to create a card for.</param>
    /// <returns>The created card, not yet refreshed.</returns>
    private RepoCardViewModel CreateCard(RecentRepo recentRepo) =>
        new(recentRepo, () => _settings, _packageVersionCache, _startupOptions, credentials: Credentials);

    /// <summary>
    ///     Creates the provider that the settings window's "Test connection" lists versions
    ///     through for a source being edited, with the same credential facilities and cache
    ///     location the cards use.
    /// </summary>
    /// <param name="definition">The source as currently edited (not yet saved).</param>
    /// <returns>A provider that can sign in through the application's sign-in dialog.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the definition is not an
    ///     acceptable NuGet source.</exception>
    internal IPackageSourceProvider CreateConnectionTestProvider(PackageSourceDefinition definition) =>
        new NuGetSourceProvider(definition, RepoCardViewModel.GetNuGetRootBase(_startupOptions), Credentials);

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

