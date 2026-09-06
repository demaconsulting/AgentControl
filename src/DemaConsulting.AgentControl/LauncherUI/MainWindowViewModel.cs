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
    ///     The configuration directory settings are persisted to/from, or <see langword="null"/>
    ///     to use <see cref="SettingsStore.GetDefaultConfigDirectory"/>.
    /// </summary>
    private readonly string? _configDirectory;

    /// <summary>
    ///     Test-only startup overrides passed through to every <see cref="RepoCardViewModel"/>.
    /// </summary>
    private readonly StartupOptions? _startupOptions;

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
    ///     Initializes a new <see cref="MainWindowViewModel"/>, populating
    ///     <see cref="RepoCards"/> from <paramref name="settings"/>'s recent-repos list.
    /// </summary>
    /// <param name="settings">The loaded application settings.</param>
    /// <param name="startupOptions">Test-only startup overrides, or <see langword="null"/>.</param>
    /// <param name="configDirectory">The configuration directory settings are persisted to, or
    ///     <see langword="null"/> to use the default.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is
    ///     <see langword="null"/>.</exception>
    public MainWindowViewModel(AppSettings settings, StartupOptions? startupOptions = null, string? configDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _startupOptions = startupOptions;
        _configDirectory = configDirectory;

        RepoCards = [];
        DisplayedRepoCards = [];
        foreach (var recentRepo in settings.RecentRepos)
        {
            var card = CreateCard(recentRepo);
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
    ///     Gets the filtered/sorted view of <see cref="RepoCards"/> that the main window's list
    ///     actually binds to: a case-insensitive substring match against
    ///     <see cref="RepoCardViewModel.RepoName"/>/<see cref="RepoCardViewModel.RepoPath"/>
    ///     against <see cref="FilterText"/>, sorted by favorite status (descending) then by
    ///     <see cref="RepoCardViewModel.LastLaunchedUtc"/> (descending, nulls last), stable
    ///     otherwise.
    /// </summary>
    public ObservableCollection<RepoCardViewModel> DisplayedRepoCards { get; }

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
            PinnedPackageVersion = pin?.Version
        };

        _settings.RecentRepos.Insert(0, recentRepo);
        var card = CreateCard(recentRepo);
        AttachCardEvents(card);
        card.Refresh();
        RepoCards.Insert(0, card);

        SaveSettings();
        UpdateDisplayedRepoCards();
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
    ///     <see cref="RemoveRepo"/>), persists them, invalidates the shared
    ///     <see cref="PackageVersionCache"/> (the package-source path may have changed), and
    ///     re-runs each card's cheap refresh so upgrade badges and pull eligibility reflect any
    ///     changed package-source/git-path settings immediately.
    /// </summary>
    /// <param name="updated">The updated settings, typically built by
    ///     <see cref="SettingsWindowViewModel.Save"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updated"/> is
    ///     <see langword="null"/>.</exception>
    public void ApplySettings(AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        updated.RecentRepos = _settings.RecentRepos;
        _settings = updated;
        OnPropertyChanged(nameof(Settings));

        SaveSettings();

        _packageVersionCache.Invalidate();
        foreach (var card in RepoCards)
        {
            card.RefreshCheap();
        }

        UpdateDisplayedRepoCards();
    }

    /// <summary>
    ///     Persists the current settings to the configured directory.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the settings file cannot be
    ///     written.</exception>
    public void SaveSettings() => SettingsStore.Save(_settings, _configDirectory);

    /// <summary>
    ///     Recomputes <see cref="DisplayedRepoCards"/> from <see cref="RepoCards"/>: a
    ///     case-insensitive substring match against <see cref="FilterText"/>, sorted by favorite
    ///     status (descending) then by <see cref="RepoCardViewModel.LastLaunchedUtc"/>
    ///     (descending, nulls last), stable otherwise (<see cref="Enumerable.OrderBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/>
    ///     and <see cref="Enumerable.ThenBy{TSource,TKey}(IOrderedEnumerable{TSource},Func{TSource,TKey})"/>
    ///     are documented as stable sorts).
    /// </summary>
    private void UpdateDisplayedRepoCards()
    {
        var filter = _filterText;
        IEnumerable<RepoCardViewModel> filtered = RepoCards;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(card =>
                card.RepoName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                card.RepoPath.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var sorted = filtered
            .OrderByDescending(card => card.IsFavorite)
            .ThenByDescending(card => card.LastLaunchedUtc ?? DateTimeOffset.MinValue)
            .ToList();

        DisplayedRepoCards.Clear();
        foreach (var card in sorted)
        {
            DisplayedRepoCards.Add(card);
        }
    }

    /// <summary>
    ///     Subscribes to a card's <see cref="RepoCardViewModel.FavoriteChanged"/> and
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
        new(recentRepo, () => _settings, _packageVersionCache, _startupOptions);

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

