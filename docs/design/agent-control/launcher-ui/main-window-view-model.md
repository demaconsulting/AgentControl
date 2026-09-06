### MainWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`MainWindowViewModel` backs the main recent-repos window. Its responsibility is to own the
collection of `RepoCardViewModel` instances, apply search filtering and sort ordering, and
mediate add/remove of tracked repos and settings changes. Window-management concerns (opening
`SettingsWindow`/`ReleaseNotesViewer`, showing folder-browse dialogs) are intentionally left
to `MainWindow`'s code-behind, since they require live Avalonia `Window`/`IStorageProvider`
instances that cannot be constructed in a headless unit test.

#### Data Model

**RepoCards**: `ObservableCollection<RepoCardViewModel>` — The recent-repos list,
most-recently-used first, backing this view model's underlying state (settings persistence,
additions/removals).

**DisplayedRepoCards**: `ObservableCollection<RepoCardViewModel>` — The filtered/sorted view
of `RepoCards` that the main window's list actually binds to.

**FilterText**: `string?` — The current filter text typed into the recent-repos search box;
setting it recomputes `DisplayedRepoCards`.

**Settings**: `AppSettings` (read-only property over an internal mutable field) — The current
application settings, exposed for building a `SettingsWindowViewModel` from the main window's
code-behind.

**_packageVersionCache**: `PackageVersionCache` — The shared, session-scoped package-version
cache passed to every `RepoCardViewModel`, per architecture.md's repo-fact caching strategy.

#### Key Methods

**Constructor**: Populates `RepoCards` from the loaded settings.

- *Parameters*: `AppSettings settings`, `StartupOptions? startupOptions = null`,
  `string? configDirectory = null`.
- *Preconditions*: `settings` is not null.
- *Postconditions*: One `RepoCardViewModel` exists per `RecentRepo` in `settings.RecentRepos`;
  each card's cheap refresh (`RefreshCheap`) has run; `DisplayedRepoCards` reflects the
  initial sort/filter (`AgentControl-MainWindowViewModel-PopulateRecentRepos`).

Only the cheap per-card checks run eagerly at construction — the working-tree dirty check is
deferred/lazy, per architecture.md's repo-fact caching strategy, so startup stays fast even
with many tracked repos.

**AddRepo**: Adds a repository to the recent-repos list.

- *Parameters*: `string repoPath`.
- *Returns*: `bool` — `true` if added.
- *Preconditions*: None beyond a non-null/non-whitespace path.
- *Postconditions*: On success, a new card is inserted at the most-recently-used position,
  settings are persisted, and `DisplayedRepoCards` is recomputed
  (`AgentControl-MainWindowViewModel-AddRepo`).

Returns `false` without persisting when `repoPath` does not exist on disk, or is already
present (case-insensitive comparison, matching Windows path semantics). Attempts to load an
existing pin via `RepoPinStore.Load`, tolerating (returning `null` for) a repo that has never
been synced.

**RemoveRepo**: Removes a repo card unconditionally.

- *Parameters*: `RepoCardViewModel card`.
- *Returns*: `void`.
- *Preconditions*: `card` is not null.
- *Postconditions*: The card is removed from `RepoCards`, settings are persisted, and
  `DisplayedRepoCards` is recomputed (`AgentControl-MainWindowViewModel-RemoveRepo`).

Performs no confirmation itself: `MainWindow`'s code-behind is responsible for showing a
`ConfirmationWindow` before calling this, in response to the card raising `RemoveRequested`.

**ApplySettings**: Applies updated settings from the settings window.

- *Parameters*: `AppSettings updated`.
- *Returns*: `void`.
- *Preconditions*: `updated` is not null.
- *Postconditions*: The live settings instance is replaced (preserving the existing recent-
  repos list, since `SettingsWindowViewModel` never owns it), persisted, the shared
  `PackageVersionCache` is invalidated, and every card's cheap refresh re-runs
  (`AgentControl-MainWindowViewModel-ApplySettings`).

**UpdateDisplayedRepoCards** (private): Recomputes `DisplayedRepoCards` from `RepoCards` — a
case-insensitive substring match against `FilterText` on repo name or path
(`AgentControl-MainWindowViewModel-SearchFilter`), sorted by favorite status descending then
by `LastLaunchedUtc` descending with nulls last, stable otherwise
(`AgentControl-MainWindowViewModel-SortOrder`).

#### Error Handling

`AddRepo` throws `ArgumentException` for a null, empty, or whitespace `repoPath`.
`RemoveRepo`/`ApplySettings` throw `ArgumentNullException` for a null argument. `SaveSettings`
propagates `InvalidOperationException` from `SettingsStore.Save` if the settings file cannot
be written. `TryLoadPin` (private) catches `InvalidOperationException` from `RepoPinStore.Load`
and returns `null`, so a corrupt or unreadable pin file never prevents `AddRepo` from
succeeding.

#### Dependencies

- **RepoCardViewModel** — one instance per tracked repo; `MainWindowViewModel` constructs,
  refreshes, and disposes of card membership in `RepoCards`.
- **RepoPinStore** (`RepoConfig` subsystem) — `AddRepo` attempts to load an existing pin for a
  newly-added repo.
- **SettingsStore** (`Settings` subsystem) — `SaveSettings` persists the live `AppSettings`.
- **PackageVersionCache** (`AgentPackageManagement` subsystem) — shared across all cards;
  invalidated by `ApplySettings` when the package source may have changed.

#### Callers

- **App** — constructs `MainWindowViewModel` in `OnFrameworkInitializationCompleted` and
  assigns it as `MainWindow.DataContext`.
- **MainWindow** (code-behind) — calls `AddRepo`, `RemoveRepo`, and reads `Settings` to open
  `SettingsWindow`.
