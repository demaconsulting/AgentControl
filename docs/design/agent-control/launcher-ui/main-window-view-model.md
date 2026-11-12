### MainWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`MainWindowViewModel` backs the main recent-repos window. Its responsibility is to own the
collection of `RepoCardViewModel` instances, apply search filtering, sort ordering and the
user's own grouping (collapsible sections), and mediate add/remove of tracked repos and settings
changes. Window-management concerns (opening
`SettingsWindow`/`ReleaseNotesViewer`, showing folder-browse dialogs and the group-name prompt) are
intentionally left to `MainWindow`'s code-behind, since they require live Avalonia
`Window`/`IStorageProvider` instances that cannot be constructed in a headless unit test.

The unit uses two independently documented `LauncherUI` units: `RepoGroupHeaderViewModel` (the header row that
starts a section, with `RepoGroupKind`, which kind of section a header introduces - see its own design) and
`GroupNameViewModel` (the validating view model of the group-name prompt window `GroupNameWindow` - see its own
design).

#### Data Model

**RepoCards**: `ObservableCollection<RepoCardViewModel>` — The recent-repos list in storage order (the order of
`AppSettings.RecentRepos`; a repo added by `AddRepo` is inserted at the front), backing this view model's underlying
state (settings persistence, additions/removals). It is **not** re-ordered when a repo is launched or marked as a
favorite: only `DisplayedItems` and `DisplayedRepoCards` are ordered (favorites, then most recently launched).

**DisplayedItems**: `ObservableCollection<object>` — What the main window's list binds to: the
visible repo cards interleaved with the `RepoGroupHeaderViewModel` of each section. While no repo
is in a group the list is flat (no headers; favorites first, then most recently launched,
`AgentControl-MainWindowViewModel-FlatListWithoutGroups`). Once
at least one repo is in a group (`HasGroups`) it is sectioned: **Favorites** first (the favorite
repos; a favorite is listed *only* there - not again under its group, so every repo appears once - while it keeps
its group, and its card shows a chip with it, `-FavoriteListedOnce`), then **one section per group**, alphabetically
(ordinal, ignoring letter case), then **Ungrouped** last (`AgentControl-MainWindowViewModel-GroupSections`). Each
section starts with a header that carries the section's name and count, and a section with no matching repo has no
header (`-GroupSectionHeaders`). Within a section repos are ordered by most recent launch, nulls last, stable
otherwise (`-GroupSectionRepoOrder`).
The cards of a collapsed section are left out; its header stays with its count. While a filter is
active every section is shown expanded and counts only the matches; sections without a match have no header, the
headers' toggles are disabled and the remembered collapsed names are left alone
(`AgentControl-MainWindowViewModel-GroupCollapse`, `-GroupFilter`, `-GroupFilterCounts`, `-GroupFilterOmitsEmpty`,
`-GroupFilterToggleDisabled`, `-GroupFilterKeepsCollapsedState`).

**DisplayedRepoCards**: `ObservableCollection<RepoCardViewModel>` — The filtered/sorted cards of
`DisplayedItems` (those inside a collapsed section are not included), in the same order.

**HasGroups**: `bool` (derived) — `true` when at least one repo is in a group; selects sectioned
over flat. **GroupNames**: `IReadOnlyList<string>` (derived) — the distinct group names,
alphabetically.

**FilterText**: `string?` — The current filter text typed into the recent-repos search box;
setting it recomputes `DisplayedItems` and `DisplayedRepoCards`.

**HasNoDisplayedRepos**: `bool` (derived) — `true` when `DisplayedItems` is empty (no repo matches,
or there is none; a list whose sections are all collapsed still has its headers), so the
view can swap the list for an empty-state placeholder instead of rendering nothing;
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

**_headers**: `Dictionary<string, RepoGroupHeaderViewModel>` — one header instance per section
(keyed by kind and name, ignoring letter case), reused while the section exists so a header keeps
keyboard focus when it is toggled.

**RepoGroupHeaderViewModel**: the unit that represents a section's header (`Kind`, `Name`, `Count`,
`IsCollapsed`, `IsToggleEnabled`, `AccessibleName` and the request-only commands; see its own design). This view
model updates a header in place with `Update(count, isCollapsed, isToggleEnabled)` and handles its three request
events (toggle, rename, remove).

**GroupNameViewModel**: the unit behind the group-name prompt (see its own design). The window shows it for a new
group (from a card's menu) and for a rename (from a header), and this view model applies the name it completes with
(`AgentControl-MainWindowViewModel-GroupManagement`, `AgentControl-RepoCardViewModel-MoveToGroup`).

**SettingsSaveFailed**: `EventHandler<string>` (public event) — raised with a message for the user when a change that
comes from a card or a header button (favorite, group, card color, launch stamp, collapse) could not be saved; the
window shows it in a message box (`AgentControl-MainWindowViewModel-SaveFailureReported`).

**_packageVersionCache**: `PackageVersionCache` — The package-version cache passed to every
`RepoCardViewModel`, per architecture.md's repo-fact caching strategy; created with the view model's `TimeProvider`, so
its entries expire five minutes after they were fetched.

**_timeProvider**, **_upgradeRecheckInterval**: `TimeProvider` and `TimeSpan` — the clock behind the cache's expiry and
the periodic re-check's timer, and the interval of that timer (`DefaultUpgradeRecheckInterval`, five minutes, the
lifetime of a cache entry). Both are constructor parameters so tests move time by hand and shorten the interval.

**_registryFactory**: `Func<AppSettings, PackageSourceRegistry>?` — handed to every card; `null` uses the real providers.
Supplied by tests that need sources whose reads they count.

**_recheckTimer**, **_recheckCancellation**, **_runOnUiThread**, **_disposed**: the periodic re-check's timer, the
cancellation source of the round that is running (non-null exactly from the moment a round starts until that round has
finished unwinding - canceling a round only requests the cancellation and leaves the field set, so a round that was
superseded or disposed but whose provider call is still in flight still counts as running, which is what stops rounds
from overlapping), the function that brings a tick to the UI thread, and whether the view model was disposed.
`RecheckUpgradesAsync`'s `finally` clears the field only when it still holds that round's own source.

**_launchChecksInFlight**: `int` — how many background upgrade checks of the launch kind (launch, settings change,
added repo) are running: incremented when `CheckUpgradesAsync` is called and decremented in its `finally`, so a check
that a newer one superseded counts until it has unwound, although `PendingRefresh` no longer refers to it. A periodic
tick starts no round while it is above zero or `_recheckCancellation` is set, so a superseded check still inside a
provider call that ignores cancellation never overlaps a round.

**Credentials**: `PackageCredentialContext?` — The session credential store and sign-in prompt created by
`App`, passed to every `RepoCardViewModel` and to the connection-test provider so that a sign-in made in one
place is remembered for all of them; `null` (tests, or no sign-in facilities) leaves server sources with only
what NuGet itself supplies.

#### Key Methods

**Transactional changes (candidate copy, save, commit, refresh)**: every change the user makes in the main window goes
through the private `CommitSettingsChange(change)`, so memory, the displayed list and the settings file cannot drift
apart:

1. *Candidate copy*: a copy of the settings is made (`CreatePersistedCopy`, with each `RecentRepo` cloned by
   `RecentRepo.Clone()` and the collapsed list copied), and `change` is applied to the **candidate** only.
2. *Save*: the candidate is written with `SettingsStore.Save`. If that throws (a file that cannot be written, a source
   location that embeds a credential), the exception leaves the method before anything live was touched.
3. *Commit*: only after a successful save is the same `change` applied to the live settings. Because the change runs
   twice, it must give the same result both times; callers decide anything that depends on live state (for example
   whether a section is being collapsed or expanded) before the call.
4. *Refresh*: the callers then bring the cards and the displayed list up to date.

A failed save therefore changes nothing - not the live settings, the cards, the displayed list or the file - and a
later, unrelated save can never write the failed change because the live settings never held it
(`AgentControl-MainWindowViewModel-FailedSaveChangesNothing`, `-FailedSaveNotPersistedLater`,
`-ChangeAppliedWhenSaved`). The operations that the window calls directly (`AddRepo`, `RemoveRepo`,
`ToggleGroupCollapsed`, `RenameGroup`, `RemoveGroup`) let the `InvalidOperationException` of the failed save reach
their caller, which shows a message (`-SaveFailureRefusesOperation`). The changes that originate in a card are made
through the private `CommitRepoChange(repo, change)`, handed to each card as its `commitChange` callback when
`CreateCard` builds it: the callback applies the change to the repo's entry through `CommitSettingsChange`, returns
`true` when it was saved (or when the repo is no longer in the list, which needs no saving), and on a failed save
raises `SettingsSaveFailed` through `ReportSaveFailure`, returns `false` and changes nothing. The card applies its own
property, and raises its change event, only when the callback returned `true` (see the `RepoCardViewModel` design). A
header's collapse toggle is forwarded through `RunReportingSaveFailure`, which reports the failure the same way
instead of letting the exception escape into the UI framework (`-SaveFailureReported`). `ApplySettings` keeps its own
candidate-first flow, described below.

**Constructor**: Populates `RepoCards` from the loaded settings.

- *Parameters*: `AppSettings settings`, `StartupOptions? startupOptions = null`,
  `string? configDirectory = null`, `PackageCredentialContext? credentials = null`,
  `Action<ThemePreference>? applyTheme = null`, `TimeProvider? timeProvider = null`,
  `TimeSpan? upgradeRecheckInterval = null` and `Func<AppSettings, PackageSourceRegistry>? registryFactory = null`.
- *Preconditions*: `settings` is not null; `upgradeRecheckInterval`, when given, is positive.
- *Postconditions*: One `RepoCardViewModel` exists per `RecentRepo` in `settings.RecentRepos`;
  each card's cheap refresh (`RefreshCheap`) has run; `DisplayedItems` and `DisplayedRepoCards`
  reflect the initial sort/filter/grouping (`AgentControl-MainWindowViewModel-PopulateRecentRepos`).
  No package source has been read (`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`) and no timer runs yet.

Only the cheap per-card checks run eagerly at construction — the working-tree dirty check is
deferred/lazy, per architecture.md's repo-fact caching strategy, so startup stays fast even
with many tracked repos.

**BeginUpgradeRefresh**: Starts checking every card's upgrade availability in the background.

- *Returns*: A task that completes when the checks have finished (the window ignores it; tests
  await it).
- *Postconditions*: Each card's `RefreshUpgradeStatusAsync` runs a few cards at a time, so a slow or
  unreachable source holds up only the cards' own checks, never the window, and a check still running for earlier
  settings (including a periodic round) is cancelled. The window calls it once it is shown; `AddRepo` checks the new card's
  upgrade status the same way, and `ApplySettings` starts a new round against the new sources
  (`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`).

**StartPeriodicUpgradeRecheck**: Starts a timer that checks every card's upgrade availability again every
`_upgradeRecheckInterval` (five minutes) until the view model is disposed
(`AgentControl-MainWindowViewModel-PeriodicUpgradeRecheck`, `-PeriodicRecheckLifecycle`).

- *Parameters*: `Action<Action>? runOnUiThread` — runs an action on the UI thread; the main window passes
  `Dispatcher.UIThread.Post`. `null` runs each tick where the timer fired (tests, whose fake clock fires timers on the
  calling thread).
- *Preconditions*: The view model is not disposed (otherwise `ObjectDisposedException`). Calling it again is ignored,
  so there is only ever one timer. The window calls it where it calls `BeginUpgradeRefresh`; the constructor does not
  start it, so a view model in a unit test never runs a timer unasked.
- *Threading*: `TimeProvider.CreateTimer` fires on a thread-pool thread. The tick only hands `BeginPeriodicRecheck` to
  `runOnUiThread`; the round, its `await`s and therefore every card property it updates run on the UI thread, like the
  launch-time check ("Not thread-safe; every member is expected to be called from the UI thread"). A
  `DispatcherTimer` was not used because a `TimeProvider` timer can be driven by `FakeTimeProvider` in a test, so the
  interval and the expiry are tested without waiting; the cache and the timer share the one clock.
- *Round* (`BeginPeriodicRecheck` / `RecheckUpgradesAsync`): does nothing when the view model is disposed, when the
  previous round has not finished (`_recheckCancellation` is non-null) or when the launch-time check
  (`PendingRefresh`) has not finished, so a slow source never piles up work. Otherwise it takes the cards that have a pin,
  are not busy and whose folder is not missing (a retired card is no longer in `RepoCards`, and a card that became
  busy while waiting its turn is skipped when its turn comes), **clears the cache** (the previous round's entries may
  still be inside their five minutes, and the round exists to see what the sources offer now) and checks them with
  `RefreshUpgradeStatusAsync(token)` - `allowPrompt` false, `relistSources` false, i.e. exactly the launch-time check:
  no sign-in dialog ever opens, and a failure - a server that needs a sign-in, an unreachable source - is shown in the
  card's own state (`IsUpgradeStatusUnknown`, `SourceProblem`) as at launch, never as a dialog. The cards are grouped by
  pinned package name: groups run a few at a time, the cards of one group one after another, so the first card of a
  package reads each source and the others are answered from what it just fetched (each source is read once per package
  per round, not once per card). That also has to hold when a source cannot be read: `PackageVersionCache` never caches
  a failure, so the round creates a `SourceFailureMemo` and passes it through `RefreshUpgradeStatusAsync(token,
  failureMemo: failures)` to the cache's reads. The first card whose read of a source fails records the failure in the
  memo under `(source alias, package ID)`; every later card of the round (in the same group, and a card checked after
  the failure in another) gets that same failure for that source without reading it, so a source is read at most once
  per round per package and a card ends in the same state as the first one (`IsUpgradeStatusUnknown`, plus the
  sign-in or Git-too-old `SourceProblem`); a source that failed while another answered is skipped for the later cards
  too, which still show the upgrade the working source offers. The memo belongs to the round: it is dropped when the
  round ends, so the next round - like every action the user starts, which passes no memo - reads the source again.
  The launch-time check (`CheckUpgradesAsync`, which does not group cards and reads concurrently) creates a memo of its
  own for the same reason; cards that are already reading the same source at the moment it fails still read it, as
  they do for a listing that succeeds. The round's token is cancelled by `Dispose` and by `StartUpgradeRefresh` (a settings
  change starts a new launch-style round that supersedes it); a cancelled round is not an error. Canceling only
  requests the cancellation: `_recheckCancellation` stays set until `RecheckUpgradesAsync` has actually finished
  (its `finally` clears it, and only if it still holds that round's own source), so a tick that arrives after the
  superseding check completed, while the cancelled round's provider call is still in flight, finds the round still
  registered and starts nothing - two rounds never overlap on the shared cache and card state.
- *`Dispose`* (`IDisposable`; the main window calls it when it closes): disposes the timer, cancels the running round
  and the background upgrade check tracked by `_refreshCancellation` (launch, settings change, added repo: it is
  cancelled, disposed and cleared the way `StartUpgradeRefresh` replaces it), so neither keeps waiting for a slow
  source or updates cards after the window closed, and refuses further starts (a repo added afterwards starts no
  check); calling it twice is harmless.

**AddRepo**: Adds a repository to the recent-repos list.

- *Parameters*: `string repoPath`.
- *Returns*: `bool` — `true` if added.
- *Preconditions*: None beyond a non-null/non-whitespace path.
- *Postconditions*: On success, a new card is inserted at the front of `RepoCards`, settings are persisted, and
  `DisplayedRepoCards` is recomputed (`AgentControl-MainWindowViewModel-AddRepo`). The new card is prepared first and
  its entry added through `CommitSettingsChange`; if the save fails the prepared card is retired, the exception
  reaches the caller and the list, the cards and the file are exactly as they were.

Returns `false` without persisting when `repoPath` does not exist on disk, or is already
present (case-insensitive comparison, matching Windows path semantics). Attempts to load an
existing pin via `RepoPinStore.Load`, tolerating (returning `null` for) a repo that has never
been synced.

**AddRepoToGroup**: Adds a repository straight into one of the user's groups, or moves an already listed repo into it.

- *Parameters*: `string repoPath`, `string groupName`.
- *Returns*: `AddRepoResult` - `Added`, `MovedExisting`, `MoveFailed` (the card reported the failed save through
  `SettingsSaveFailed`) or `NotAdded` (the folder does not exist).
- *Preconditions*: a non-blank path and a valid group name (`RepoGroupName.Validate`); a built-in section's name or a
  blank name throws `ArgumentException`.
- *Postconditions*: shares the add with `AddRepo` (the private `AddRepoCore`), with the group - spelled as an existing
  group of the same name ignoring case - set on the new entry before the single save, so the repo is never listed
  ungrouped in between; a repo already listed is moved with `RepoCardViewModel.MoveToGroup` instead of being added
  twice; and a collapsed target group is expanded so the repo is visible
  (`AgentControl-MainWindowViewModel-AddRepoToGroup`). The group is stored in the user's settings only.

**RemoveRepo**: Removes a repo card unconditionally.

- *Parameters*: `RepoCardViewModel card`.
- *Returns*: `void`.
- *Preconditions*: `card` is not null.
- *Postconditions*: The removal is first saved (`CommitSettingsChange`); only then is the card removed from
  `RepoCards`, **retired** and `DisplayedRepoCards` recomputed (`AgentControl-MainWindowViewModel-RemoveRepo`). A
  failed save throws and leaves the repo listed and its card working.

Retiring (`RepoCardViewModel.Retire`, see its design) cancels what the card still has running and silences it, so a
removed repo's pending upgrade check or operation can never show an error, release notes or a prompt
(`AgentControl-MainWindowViewModel-RemoveRepoRetiresCard`).

Performs no confirmation itself: `MainWindow`'s code-behind is responsible for showing a
`ConfirmationWindow` before calling this, in response to the card raising `RemoveRequested`.

**ApplySettings**: Applies updated settings from the settings window.

- *Parameters*: `AppSettings updated`.
- *Returns*: `void`.
- *Preconditions*: `updated` is not null.
- *Postconditions*: The live settings instance is replaced (preserving the existing recent-
  repos list and the collapsed-section names, since `SettingsWindowViewModel` never owns them) only after
  the candidate has been
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
  choice takes effect without a restart (`AgentControl-MainWindowViewModel-ApplyTheme`); a refused save never reaches
  this step (`AgentControl-MainWindowViewModel-RefusedThemeNotApplied`).

**CreateConnectionTestProvider**: Creates the provider that the settings window's
**Test connection** lists through for a source being edited: for a NuGet source the `NuGetSourceProvider`, with the
same credential facilities
and cache location the cards use, so a sign-in made during a test is remembered for the session and not
asked for again when a package is selected (`AgentControl-MainWindowViewModel-ConnectionTestProvider`); for a Git
source a `GitSourceProvider` that runs git with the configured git executable (the setting, else the startup
override, else `git`, as the cards do), keeps its cache in the same `git-cache` folder, is **interactive** (git may
show its own sign-in, because the user started the test) and enforces the minimum git version, so an old git is
reported as such - before any tag is listed - instead of failing obscurely
(`AgentControl-MainWindowViewModel-GitConnectionTestProvider`, `-GitConnectionTestInteractive`,
`-GitConnectionTestRefusesOldGit`). It throws `InvalidOperationException` or `ArgumentException` for a definition the
provider refuses (an unacceptable NuGet source, or a Git source whose address, tag pattern, folder or package name is
unusable).
`MainWindow` passes it to `SettingsWindowViewModel` as the provider factory.

**UpdateDisplayedRepoCards** (private): Recomputes `DisplayedItems` and `DisplayedRepoCards` from
`RepoCards` — a case-insensitive substring match against `FilterText` on repo name or path
(`AgentControl-MainWindowViewModel-SearchFilter`), then either the flat order (favorite status
descending, then `LastLaunchedUtc` descending with nulls last, stable otherwise -
`AgentControl-MainWindowViewModel-SortOrder`) or, when `HasGroups`, the sections described under
*DisplayedItems* (`AgentControl-MainWindowViewModel-GroupSections`). A section is collapsed only if
no filter is active and its name is in `AppSettings.CollapsedGroups`
(`AgentControl-MainWindowViewModel-GroupCollapse`, `-GroupFilter`). Both collections are brought to
the new state **in place** (remove what is gone, move and insert the rest) rather than cleared, so
unchanged rows keep their controls and focus. Also re-raises change notifications for
`HasGroups`, `GroupNames`, `HasNoDisplayedRepos` and `EmptyStateMessage`, since every path that can
change the displayed set (add/remove, filter, favorite-toggle, grouping) runs through this method.

**ToggleGroupCollapsed** / **IsGroupCollapsed**: Collapse or expand a section by name (a group, or
`Favorites` / `Ungrouped`; letter case ignored), add or remove the name in
`AppSettings.CollapsedGroups` (through `CommitSettingsChange`; whether it is being collapsed or expanded is decided
once, from the live state, before the call), and refresh the list. The collapsed names are written to the settings
file, are read back when the view model is built (`AgentControl-MainWindowViewModel-GroupCollapseRemembered`) and are
kept by `ApplySettings` (`-GroupCollapseKeptOnApplySettings`). The built-in names are reserved
(`RepoGroupName`), so a group can never collide with them. Damaged group or collapsed values in the settings
(a null list, blank, reserved or overlong names, names differing only in letter case) are read through
`RepoGroupName` so that the list is still shown (`-DamagedGroupSettings`).

**RenameGroup**: Renames a group — every member's group name and the group's collapsed entry move to
the new name (`AgentControl-MainWindowViewModel-GroupManagement`) — and returns `false` (changing nothing) for an
unknown group, an invalid name or another group's name; changing only the letter case of the group's own name is
allowed (`-GroupRenameRules`).
**RemoveGroup**: Removes a group — its repos become ungrouped, its collapsed entry is forgotten
(`-GroupRemove`). Both are applied through `CommitSettingsChange`, persist to the settings file only (never to a
repository, `-GroupSettingsOnly`) and, once saved, refresh the cards' group properties and the list. A header's
`RenameCommand` and `RemoveGroupCommand` are forwarded as the `GroupRenameRequested` and `GroupRemoveRequested`
events, which change nothing by themselves (`-GroupHeaderRequests`); `MainWindow` shows the name prompt or a
confirmation and calls these methods.

**AttachCardEvents** (private): Subscribes to each card's `FavoriteChanged`, `GroupChanged` and `LaunchRecorded`
events so the displayed list is refreshed. The change itself was already saved by then: the card raises these events
only after its `commitChange` callback (`CommitRepoChange`) reported success, so a failed save raises no event and
refreshes nothing. A color change needs no re-ordering and so has no subscription here. A header's collapse toggle is
run through `RunReportingSaveFailure` where the header is created.

#### Error Handling

`AddRepo` throws `ArgumentException` for a null, empty, or whitespace `repoPath`.
`RemoveRepo`/`ApplySettings` throw `ArgumentNullException` for a null argument, and `ApplySettings`
throws `InvalidOperationException` before adopting anything when a source location embeds a
credential or when the settings file cannot be written, leaving the current settings in place. The
operations the window calls (`AddRepo`, `RemoveRepo`, `ToggleGroupCollapsed`, `RenameGroup`, `RemoveGroup`)
throw `InvalidOperationException` when the settings file cannot be written, having changed nothing (the window
catches it and tells the user); a change that comes from a card or a header button is not thrown but reported through
`SettingsSaveFailed` (the callback returns `false`, the card keeps its value); `ToggleGroupCollapsed` throws
`ArgumentException` for a blank name, while `RenameGroup` and `RemoveGroup` report an unknown group or unusable name
by returning `false`. Group names read from
settings are repaired (`RepoGroupName.Sanitize`) on the fly, a null collapsed list reads as empty, so
damaged values never throw. `SaveSettings`
propagates `InvalidOperationException` from `SettingsStore.Save` if the settings file cannot
be written; it writes the live settings as they are and is not used for the user's changes. `TryLoadPin` (private)
catches `InvalidOperationException` from `RepoPinStore.Load`
and returns `null`, so a corrupt or unreadable pin file never prevents `AddRepo` from
succeeding.

#### Dependencies

- **RepoCardViewModel** — one instance per tracked repo; `MainWindowViewModel` constructs and refreshes each card,
  hands it the `commitChange` callback (`CommitRepoChange`), keeps the membership of `RepoCards` and retires a
  card when its repo is removed.
- **RepoGroupHeaderViewModel** (`LauncherUI`) — one header per section, updated in place; its
  toggle, rename and remove requests are handled here.
- **RepoPinStore** (`RepoConfig` subsystem) — `AddRepo` attempts to load an existing pin for a
  newly-added repo.
- **SettingsStore** (`Settings` subsystem) — saves the candidate settings of every change and of `ApplySettings`
  (`SaveSettings` persists the live `AppSettings` as they are);
  **RepoGroupName** (`Settings` subsystem) — the group-name rules used for sorting, validation and
  repair.
- **PackageVersionCache** (`AgentPackageManagement` subsystem) — shared across all cards;
  invalidated per source by `ApplySettings` when a package source was edited or removed, and entirely at the start of each
  periodic re-check round.
- **TimeProvider** (.NET) — the timer and the clock of the periodic re-check and of the cache's expiry.
- **PackageCredentialContext** (`PackageCredentials` subsystem) — the session credentials and sign-in
  prompt it holds and passes on; **NuGetSourceProvider**, **GitSourceProvider**, **GitSourceOptions**
  (`AgentPackageManagement`) — created by `CreateConnectionTestProvider`.

#### Callers

- **App** — constructs `MainWindowViewModel` in `OnFrameworkInitializationCompleted`, passing the
  process-wide `PackageCredentialContext`, and assigns it as `MainWindow.DataContext`.
- **MainWindow** (code-behind) — calls `AddRepo`, `RemoveRepo`, `ToggleGroupCollapsed`, `RenameGroup` and
  `RemoveGroup` (catching the `InvalidOperationException` of a failed save and showing a message), starts the
  upgrade check and the periodic re-check (`BeginUpgradeRefresh`, `StartPeriodicUpgradeRecheck`) when the view
  model is attached, disposes the view model when it closes, subscribes to
  `SettingsSaveFailed` to show a message for the changes that come from cards and headers, and reads `Settings` to open
  `SettingsWindow`, passing `CreateConnectionTestProvider` as its provider factory; fills each card
  menu's "Move to group" submenu when the menu opens, shows the `GroupNameWindow` prompt and the
  removal confirmation for the `GroupRenameRequested`/`GroupRemoveRequested`/`NewGroupRequested`
  requests, opens the card color palette pop-up, and calls `RenameGroup`/`RemoveGroup`.
