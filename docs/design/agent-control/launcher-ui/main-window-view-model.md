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

**HasNoDisplayedRepos**: `bool` (derived) — `true` when `DisplayedRepoCards` is empty, so the
view can swap the card list for an empty-state placeholder instead of rendering nothing;
recomputed (with its change notified, alongside `EmptyStateMessage`) every time
`UpdateDisplayedRepoCards` runs (`AgentControl-MainWindowViewModel-EmptyState`).

**EmptyStateMessage**: `string` (derived) — The message shown by the empty-state placeholder:
distinguishes "no repos tracked yet" (`RepoCards` itself is empty) from "no repos match the
current filter" (`RepoCards` has entries, but none passed the filter), so the user knows
whether to add a repo or adjust their filter text
(`AgentControl-MainWindowViewModel-EmptyState`).

**Settings**: `AppSettings` (read-only property over an internal mutable field) — The current
application settings, exposed for building a `SettingsWindowViewModel` from the main window's
code-behind.

**_packageVersionCache**: `PackageVersionCache` — The shared, session-scoped package-version
cache passed to every `RepoCardViewModel`, per architecture.md's repo-fact caching strategy.

**Credentials**: `PackageCredentialContext?` — The session credential store and sign-in prompt created by
`App`, passed to every `RepoCardViewModel` and to the connection-test provider so that a sign-in made in one
place is remembered for all of them; `null` (tests, or no sign-in facilities) leaves server sources with only
what NuGet itself supplies.

#### Key Methods

**Constructor**: Populates `RepoCards` from the loaded settings.

- *Parameters*: `AppSettings settings`, `StartupOptions? startupOptions = null`,
  `string? configDirectory = null`, `PackageCredentialContext? credentials = null`.
- *Preconditions*: `settings` is not null.
- *Postconditions*: One `RepoCardViewModel` exists per `RecentRepo` in `settings.RecentRepos`;
  each card's cheap refresh (`RefreshCheap`) has run; `DisplayedRepoCards` reflects the
  initial sort/filter (`AgentControl-MainWindowViewModel-PopulateRecentRepos`). No package source
  has been read (`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`).

Only the cheap per-card checks run eagerly at construction — the working-tree dirty check is
deferred/lazy, per architecture.md's repo-fact caching strategy, so startup stays fast even
with many tracked repos.

**BeginUpgradeRefresh**: Starts checking every card's upgrade availability in the background.

- *Returns*: A task that completes when the checks have finished (the window ignores it; tests
  await it).
- *Postconditions*: Each card's `RefreshUpgradeStatusAsync` runs a few cards at a time, so a slow or
  unreachable source delays only the cards pinned to it, and a check still running for earlier
  settings is cancelled. The window calls it once it is shown; `AddRepo` checks the new card's
  upgrade status the same way, and `ApplySettings` starts a new round against the new sources
  (`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`).

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
  repos list, since `SettingsWindowViewModel` never owns it) only after the candidate has been
  persisted successfully — if the write fails, the previous instance stays live, so a later save
  (favorite, launch) can never persist a rejected candidate and the exception reaches the caller to
  show; the shared
  `PackageVersionCache` is invalidated for each package source that was edited or removed (the
  sources before and after are compared by alias and by `PackageSourceDefinition.IsEquivalentTo`;
  unchanged sources keep their cached results)
  (`AgentControl-MainWindowViewModel-InvalidateChangedSources`), and every card's cheap refresh
  re-runs. A present card
  is additionally given a fresh Git status check (via `RefreshDirtyStatus`) when either it
  reappeared (was missing, now present) during this call, so it is not left showing a "not yet
  checked" state indefinitely, or `GitExecutablePath` itself changed, so a clean/dirty result
  cached against the previously configured executable is not kept showing once a different
  executable is in effect; a present card that was already checked and whose executable path
  did not change only gets the cheap refresh, avoiding a synchronous `git status` call — which
  `GitClient` runs with no timeout — for every tracked repo on every settings save
  (`AgentControl-MainWindowViewModel-ApplySettings`). Once the settings are saved, the saved
  `Theme` is handed to the injected theme applier (`Action<ThemePreference>`, supplied by `App`), so the
  choice takes effect without a restart; a refused save never reaches this step
  (`AgentControl-MainWindowViewModel-ApplyTheme`).

**CreateConnectionTestProvider**: Creates the `NuGetSourceProvider` that the settings window's
**Test connection** lists versions through for a source being edited, with the same credential facilities
and cache location the cards use, so a sign-in made during a test is remembered for the session and not
asked for again when a package is selected (`AgentControl-MainWindowViewModel-ConnectionTestProvider`).
`MainWindow` passes it to `SettingsWindowViewModel` as the provider factory.

**UpdateDisplayedRepoCards** (private): Recomputes `DisplayedRepoCards` from `RepoCards` — a
case-insensitive substring match against `FilterText` on repo name or path
(`AgentControl-MainWindowViewModel-SearchFilter`), sorted by favorite status descending then
by `LastLaunchedUtc` descending with nulls last, stable otherwise
(`AgentControl-MainWindowViewModel-SortOrder`). Also re-raises change notifications for
`HasNoDisplayedRepos`/`EmptyStateMessage`, since every path that can change the displayed set
(add/remove, filter, favorite-toggle re-sort) runs through this method.

#### Error Handling

`AddRepo` throws `ArgumentException` for a null, empty, or whitespace `repoPath`.
`RemoveRepo`/`ApplySettings` throw `ArgumentNullException` for a null argument, and `ApplySettings`
throws `InvalidOperationException` before adopting anything when a source location embeds a
credential or when the settings file cannot be written, leaving the current settings in place. `SaveSettings`
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
  invalidated per source by `ApplySettings` when a package source was edited or removed.
- **PackageCredentialContext** (`PackageCredentials` subsystem) — the session credentials and sign-in
  prompt it holds and passes on; **NuGetSourceProvider** (`AgentPackageManagement`) — created by
  `CreateConnectionTestProvider`.

#### Callers

- **App** — constructs `MainWindowViewModel` in `OnFrameworkInitializationCompleted`, passing the
  process-wide `PackageCredentialContext`, and assigns it as `MainWindow.DataContext`.
- **MainWindow** (code-behind) — calls `AddRepo`, `RemoveRepo`, and reads `Settings` to open
  `SettingsWindow`, passing `CreateConnectionTestProvider` as its provider factory.
