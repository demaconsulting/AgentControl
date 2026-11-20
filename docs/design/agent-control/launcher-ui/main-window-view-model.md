### MainWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`MainWindowViewModel` backs the main recent-repos window. Its responsibility is to own the
collection of `RepoCardViewModel` instances, apply search filtering, the user's own repo order (favorites first)
and the user's own grouping (collapsible sections, each group with an optional color, the groups in the user's own
order), let the user change those orders (Move menu items and drag and drop, for cards and for groups), and mediate
add/remove of tracked repos (one folder or several at once, only top folders of Git repositories) and settings
changes. Window-management concerns (opening
`SettingsWindow`/`ReleaseNotesViewer`, showing folder-browse dialogs and the group-name prompt) are
intentionally left to `MainWindow`'s code-behind, since they require live Avalonia
`Window`/`IStorageProvider` instances that cannot be constructed in a headless unit test.

The unit uses two independently documented `LauncherUI` units: `RepoGroupHeaderViewModel` (the header row that
starts a section, with `RepoGroupKind`, which kind of section a header introduces - see its own design) and
`GroupNameViewModel` (the validating view model of the group-name prompt window `GroupNameWindow` - see its own
design).

#### Data Model

**RepoCards**: `ObservableCollection<RepoCardViewModel>` — The recent-repos list in the user's own order (the order of
`AppSettings.RecentRepos`; a repo added by `AddRepo` is appended at the end), backing this view model's underlying
state (settings persistence, additions/removals). Only `MoveCard`, `DropCard` (and the add paths, at the end) change
this order: it is **not** re-ordered when a repo is launched, refreshed, synced or marked as a favorite
(`AgentControl-MainWindowViewModel-StableOrder`). `DisplayedItems` and `DisplayedRepoCards` are derived from it
(favorites first, then this order).

**DisplayedItems**: `ObservableCollection<object>` — What the main window's list binds to: the
visible repo cards interleaved with the `RepoGroupHeaderViewModel` of each section. While no repo
is in a group the list is flat (no headers; favorites first, then the user's order from `RepoCards`,
`AgentControl-MainWindowViewModel-FlatListWithoutGroups`, `-SortOrder`). Once
at least one repo is in a group (`HasGroups`) it is sectioned: **Favorites** first (the favorite
repos; a favorite is listed *only* there - not again under its group, so every repo appears once - while it keeps
its group, and its card shows a chip with it, `-FavoriteListedOnce`), then **one section per group**, in the user's
own group order - the groups with a position in `AppSettings.GroupOrder` in that order, then the groups without one
alphabetically (ordinal, ignoring letter case), so the groups are alphabetical until the user orders them
(`-GroupOrder`) - then **Ungrouped** last (`AgentControl-MainWindowViewModel-GroupSections`). Each
section starts with a header that carries the section's name and count, and a section with no matching repo has no
header (`-GroupSectionHeaders`). Within a section repos appear in their relative order in `RepoCards`, the user's own
order (`-GroupSectionRepoOrder`).
The cards of a collapsed section are left out; its header stays with its count. While a filter is
active every section is shown expanded and counts only the matches; sections without a match have no header, the
headers' toggles are disabled and the remembered collapsed names are left alone
(`AgentControl-MainWindowViewModel-GroupCollapse`, `-GroupFilter`, `-GroupFilterCounts`, `-GroupFilterOmitsEmpty`,
`-GroupFilterToggleDisabled`, `-GroupFilterKeepsCollapsedState`).

**DisplayedRepoCards**: `ObservableCollection<RepoCardViewModel>` — The filtered/sorted cards of
`DisplayedItems` (those inside a collapsed section are not included), in the same order.

**HasGroups**: `bool` (derived) — `true` when at least one repo is in a group; selects sectioned
over flat. **GroupNames**: `IReadOnlyList<string>` (derived) — the distinct group names,
alphabetically (the card menu's "Move to group" list; the sections use the user's group order instead).

**FilterText**: `string?` — The current filter text typed into the recent-repos search box;
setting it recomputes `DisplayedItems` and `DisplayedRepoCards` and raises `IsReorderEnabled`.

**IsReorderEnabled**: `bool` (derived) — `true` when `FilterText` is null, empty or whitespace. While the filter has
text only some repos are shown, so their positions cannot be changed: the window starts no drag and disables the
Move menu items, and `MoveCard`, `CanDrop` and `DropCard` refuse as well
(`AgentControl-MainWindowViewModel-ReorderOffWhileFiltering`). **ReorderDisabledWhileFilteringMessage**: `string`
(public constant) — the explanation ("Clear the filter to change the order: ...") shown as the tooltip of a disabled
Move item, so the wording is the same everywhere. **GroupReorderDisabledWhileFilteringMessage**: `string` (public
constant) — the same explanation for the groups ("Clear the filter to change the order of the groups: ..."), shown as
the tooltip of a header's disabled Move up / Move down item; the window also starts no group drag while the filter has
text.

**RepoMove**: `enum` (`Top`, `Up`, `Down`, `Bottom`) — the four moves of the card menu.
**RepoGroupMove**: `enum` (`Up`, `Down`) — the two moves of a group header's menu.
**GroupDropTarget**: `record` (`GroupName`, `InsertBefore`) — where a dragged group header lands: before (the upper
half of its header) or after (the lower half) another of the user's groups.
**RepoDropTarget**: `record` (`Kind`, `Name`, `Anchor`, `InsertBefore`) — a place a dragged card may land: a section
(its kind and display name) and either an anchor card of that section with "before/after it", or no anchor, which
means the end of the section (a drop on its header). `RepoDropTarget.ForHeader(header)` and
`RepoDropTarget.ForCard(anchor, insertBefore, kind, name)` build it; the window works out the anchor's section with
`GetSectionOf`.

**AddRepoResult**: `enum` — what became of a folder that was asked to be added: `Added` (a new entry, in the requested
group if one was given), `MovedExisting` (already listed, moved into the requested group), `MoveFailed` (that move
could not be saved; already reported through `SettingsSaveFailed`), `AlreadyListed` (already listed and no group was
requested), `Missing` (the folder does not exist), `NotAGitRepository` (not the top folder of a Git repository, or a
file rather than a folder) and `SaveFailed` (the new entry could not be saved; used only by `AddRepos`, which does not
throw). **AddRepoOutcome**: `record` (`Path`, `Result`, `Problem`) — one folder of a multi-folder add, with, for
`SaveFailed`, the reason the save failed, safe to show (secrets redacted by `SecretRedactor.SafeMessage`).

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
`IsCollapsed`, `IsToggleEnabled`, `AccessibleName`, the group's color, the move availability and the request-only
commands; see its own design). This view model updates a header in place with
`Update(count, isCollapsed, isToggleEnabled)`, `UpdateColor(color)` (the group's color, `null` for *Favorites* and
*Ungrouped*) and, for a user group, `UpdateOrder(upReason, downReason)` (from `GetGroupMoveDisabledReason`), and handles
its request events (toggle, rename, remove, add repository, move).

**GroupNameViewModel**: the unit behind the group-name prompt (see its own design). The window shows it for a new
group (from a card's menu) and for a rename (from a header), and this view model applies the name it completes with
(`AgentControl-MainWindowViewModel-GroupManagement`, `AgentControl-RepoCardViewModel-MoveToGroup`).

**SettingsSaveFailed**: `EventHandler<string>` (public event) — raised with a message for the user when a change that
comes from a card or a header button (favorite, group, card color, group color, group order, launch stamp, collapse)
could not be saved; the
window shows it in a message box (`AgentControl-MainWindowViewModel-SaveFailureReported`).

**_packageVersionCache**: `PackageVersionCache` — The package-version cache passed to every
`RepoCardViewModel`, per architecture.md's repo-fact caching strategy; created with the view model's `TimeProvider`, so
its entries expire five minutes after they were fetched.

**_timeProvider**, **_upgradeRecheckInterval**: `TimeProvider` and `TimeSpan` — the clock behind the cache's expiry and
the periodic re-check's timer, and the interval of that timer (`DefaultUpgradeRecheckInterval`, five minutes, the
lifetime of a cache entry). Both are constructor parameters so tests move time by hand and shorten the interval.

**_registryFactory**: `Func<AppSettings, PackageSourceRegistry>?` — handed to every card; `null` uses the real
providers. Supplied by tests that need sources whose reads they count.

**_processStarter**, **_shellDetector**: `IAgentToolProcessStarter?`, `ShellDetector?` — handed to every card;
`null` uses the real starter and a real detector (the application's choice). Supplied by tests so a launch is
captured without a shell being started.

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

**_activeFailureMemos** / **_activeFailureMemosLock**: `List<SourceFailureMemo>` behind a `Lock` — the failure memos of
the background checks that are running: `RecheckUpgradesAsync` and `CheckUpgradesAsync` register the memo they create
(`RegisterFailureMemo`) and drop it in their `finally` (`UnregisterFailureMemo`), so a superseded check stays listed
until it has unwound. `ForgetRecordedSourceFailures` clears every listed memo; `RecheckAfterSettingsClosed` calls it.
The lock is there because without a UI-thread runner (tests) a check may finish on a thread-pool continuation.

**Credentials**: `PackageCredentialContext?` — The session credential store and sign-in prompt created by
`App`, passed to every `RepoCardViewModel` and to the connection-test provider so that a sign-in made in one
place is remembered for all of them; `null` (tests, or no sign-in facilities) leaves server sources with only
what NuGet itself supplies.

#### Key Methods

**Transactional changes (candidate copy, save, commit, refresh)**: every change the user makes in the main window goes
through the private `CommitSettingsChange(change)`, so memory, the displayed list and the settings file cannot drift
apart:

1. *Candidate copy*: a copy of the settings is made (`CreatePersistedCopy`, with each `RecentRepo` cloned by
   `RecentRepo.Clone()` and the collapsed list, the group colors and the group order copied), and `change` is applied
   to the **candidate** only; then `AppSettings.RepairGroupSettings` runs on the candidate, so a group that lost its
   last repo forgets its color and position in the file.
2. *Save*: the candidate is written with `SettingsStore.Save`. If that throws (a file that cannot be written, a source
   location that embeds a credential), the exception leaves the method before anything live was touched.
3. *Commit*: only after a successful save is the same `change` applied to the live settings, followed by the same
   repair, so memory and the file agree. Because the change runs
   twice, it must give the same result both times; callers decide anything that depends on live state (for example
   whether a section is being collapsed or expanded) before the call.
4. *Refresh*: the callers then bring the cards and the displayed list up to date.

A failed save therefore changes nothing - not the live settings, the cards, the displayed list or the file - and a
later, unrelated save can never write the failed change because the live settings never held it
(`AgentControl-MainWindowViewModel-FailedSaveChangesNothing`, `-FailedSaveNotPersistedLater`,
`-ChangeAppliedWhenSaved`). The direct operations (`AddRepo`, `AddRepoToGroup`, `RemoveRepo`,
`ToggleGroupCollapsed`, `RenameGroup`, `RemoveGroup`) let the `InvalidOperationException` of the failed save reach
their caller, which shows a message (`-SaveFailureRefusesOperation`); `AddRepos` catches it per folder instead (see
below). The changes that originate in a card are made
through the private `CommitRepoChange(repo, change)`, handed to each card as its `commitChange` callback when
`CreateCard` builds it: the callback applies the change to the repo's entry through `CommitSettingsChange`, returns
`true` when it was saved (or when the repo is no longer in the list, which needs no saving), and on a failed save
raises `SettingsSaveFailed` through `ReportSaveFailure`, returns `false` and changes nothing. The card applies its own
property, and raises its change event, only when the callback returned `true` (see the `RepoCardViewModel` design). A
header's collapse toggle is forwarded through `RunReportingSaveFailure`, which reports the failure the same way
instead of letting the exception escape into the UI framework (`-SaveFailureReported`); `SetGroupColor`, `MoveGroup`
and `DropGroup` report a failed save the same way and return `false`. `ApplySettings` keeps its own
candidate-first flow, described below.

**Constructor**: Populates `RepoCards` from the loaded settings, in the stored order. Before building the cards it
calls `AppSettings.EnsureRepoOrder`, so settings built directly (a first run, a test) are seeded exactly like a loaded
file, once: a list that has no user order yet is arranged favorites first, then most recently launched, and the
arrangement is written by the next save of any kind (`AgentControl-MainWindowViewModel-LegacyOrderSeeded`); a list
that already has a user order is kept whatever the launch times are (`-OrderKept`). It then calls
`AppSettings.RepairGroupSettings`, so settings built in memory have the group colors and order a load would leave
(only known colors, only groups some repo is in).

- *Parameters*: `AppSettings settings`, `StartupOptions? startupOptions = null`,
  `string? configDirectory = null`, `PackageCredentialContext? credentials = null`,
  `Action<ThemePreference>? applyTheme = null`, `TimeProvider? timeProvider = null`,
  `TimeSpan? upgradeRecheckInterval = null`, `Func<AppSettings, PackageSourceRegistry>? registryFactory = null`,
  `IAgentToolProcessStarter? processStarter = null` and `ShellDetector? shellDetector = null`.
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
- *Postconditions*: Each card's `RefreshUpgradeStatusAsync` runs a few cards at a time, so a slow or unreachable source
  holds up only the cards' own checks, never the window, and a check still running for earlier settings (including a
  periodic round) is cancelled. The window calls it once it is shown; `AddRepo` checks the new card's upgrade status the
  same way, and `ApplySettings` starts a new round against the new sources
  (`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`); closing the Settings window without saving re-checks
  only the cards that show a problem (`RecheckAfterSettingsClosed`, below).
- *Busy cards*: `CheckUpgradesAsync` (the check behind `BeginUpgradeRefresh`, `ApplySettings`, an added repo and
  `RecheckAfterSettingsClosed`) skips a card that `IsBusy` with an action of its own (Refresh, Upgrade, Select Package,
  Launch, Pull) when its turn comes, as a periodic round does: a check beside the action would break the card's
  one-action-at-a-time rule and, being the card's newest check, could replace the action's result with its own. The
  card is not checked when it is queued but once it holds one of the few slots, immediately before its
  `RefreshUpgradeStatusAsync` starts, so an action the user started while the card waited for a slot is seen. The test
  of `IsBusy` and the synchronous start of `RefreshUpgradeStatusAsync` (which claims the card's newest check generation
  and sets `IsCheckingUpgrade`) run with no `await` between them, and the continuation after the slot wait resumes on
  the UI thread (the check is started there and its `await`s capture the UI synchronization context), the thread where
  the card's commands set `IsBusy`; so no action can start between the test and the start of the check. A card whose
  background check is running is not busy and is checked: the newer check's result replaces the older one's, which
  `RecheckAfterSettingsClosed` relies on. A skipped card keeps its state until the action checks it (Refresh, Upgrade,
  Select Package and Pull do) or until the next periodic round.

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
  previous round has not finished (`_recheckCancellation` is non-null) or when the launch-time check (`PendingRefresh`)
  has not finished, so a slow source never piles up work. Otherwise it takes the cards that have a pin, are not busy and
  whose folder is not missing (a retired card is no longer in `RepoCards`, and a card that became busy while waiting its
  turn is skipped when its turn comes), **clears the cache** (the previous round's entries may still be inside their
  five minutes, and the round exists to see what the sources offer now) and checks them with
  `RefreshUpgradeStatusAsync(token)` - `allowPrompt` false, `relistSources` false, i.e. exactly the launch-time check:
  no sign-in dialog ever opens, and a failure - a server that needs a sign-in, an unreachable source - is shown in the
  card's own state (`IsUpgradeStatusUnknown`, `SourceProblem`) as at launch, never as a dialog. The cards are grouped by
  pinned package name: groups run a few at a time, the cards of one group one after another, so the first card of a
  package reads each source and the others are answered from what it just fetched (each source is read once per package
  per round, not once per card). That also has to hold when a source cannot be read: `PackageVersionCache` never caches
  a failure, so the round creates a `SourceFailureMemo` and passes it through
  `RefreshUpgradeStatusAsync(token, failureMemo: failures)` to the cache's reads. The first card whose read of a source
  fails records the failure in the memo under `(source alias, package ID)`; every later card of the round (in the same
  group, and a card checked after the failure in another) gets that same failure for that source without reading it, so
  a source is read at most once per round per package and a card ends in the same state as the first one
  (`IsUpgradeStatusUnknown`, plus the sign-in or Git-too-old `SourceProblem`); a source that failed while another
  answered is skipped for the later cards too, which still show the upgrade the working source offers. The memo belongs
  to the round: it is dropped when the round ends, so the next round - like every action the user starts, which passes
  no memo - reads the source again. The launch-time check (`CheckUpgradesAsync`, which does not group cards and reads
  concurrently) creates a memo of its own for the same reason; cards that are already reading the same source at the
  moment it fails still read it, as they do for a listing that succeeds. The round's token is cancelled by `Dispose` and
  by `StartUpgradeRefresh` (a settings change starts a new launch-style round that supersedes it); a cancelled round is
  not an error. Canceling only requests the cancellation: `_recheckCancellation` stays set until `RecheckUpgradesAsync`
  has actually finished (its `finally` clears it, and only if it still holds that round's own source), so a tick that
  arrives after the superseding check completed, while the cancelled round's provider call is still in flight, finds the
  round still registered and starts nothing - two rounds never overlap on the shared cache and card state.
- *`Dispose`* (`IDisposable`; the main window calls it when it closes): disposes the timer, cancels the running round
  and the background upgrade check tracked by `_refreshCancellation` (launch, settings change, added repo: it is
  cancelled, disposed and cleared the way `StartUpgradeRefresh` replaces it), so neither keeps waiting for a slow
  source or updates cards after the window closed, and refuses further starts (a repo added afterwards starts no
  check); calling it twice is harmless.

**RecheckAfterSettingsClosed**: Checks again, in the background, the cards that show a package source problem or a
failed upgrade check, once the Settings window has closed
(`AgentControl-MainWindowViewModel-RecheckAfterSettingsClosed`).

- *Parameters*: `bool settingsSaved` — whether the Settings window saved the settings (its `Saved` event was raised)
  before it closed; `false` for Cancel, Escape or closing it without saving (including after a save that failed).
- *Returns*: A task that completes when the check it started has finished (cancellation is not an error); after a save
  it returns `PendingRefresh` (the check the save started); already completed when the view model is disposed or no
  card needs checking. The window ignores it; tests await it.
- *Behavior*: While Settings is open the user may sign in with **Test connection** - whose provider
  (`CreateConnectionTestProvider`) uses `Credentials`, the same `SessionCredentialStore` the cards read without a prompt
  (`WithoutPrompt`) - or the network may come back, so without this the cards would keep showing "Sign-in needed" or a
  failed check until the next periodic round. A disposed view model starts nothing. After a save nothing is started,
  because `ApplySettings` always ends with `BeginUpgradeRefresh`, which checks every card: checking again would read
  every source twice. Otherwise it takes the cards that `CanRecheckUpgrade` accepts (pinned, not busy, folder present)
  and that have `HasSourceProblem` or `IsUpgradeStatusUnknown`, or `IsCheckingUpgrade` (a background check is reading
  a source for the card at this moment: that read began before the sign-in, and if it fails it would put "Sign-in
  needed" on the card when it finishes, so a newer check is started and the card's generation counter ignores the
  older one's result), and checks them with `CheckUpgradesAsync` - the launch-style check (which, when a card's turn
  comes, skips it if the user has meanwhile started an action on it; see *Busy cards* above): `allowPrompt` false (no
  sign-in dialog), `relistSources` false (the cache is not cleared: a
  failure is never cached, so these cards read their sources again, while a listing cached for another card still
  serves), a shared `SourceFailureMemo`, a few at a time, counted in `_launchChecksInFlight` so no periodic round starts
  over it. Like the check of an added repo (`BeginSingleCardUpgradeRefresh`) it runs with the token of
  `_refreshCancellation`, does not cancel a check that is running, is canceled by a later `StartUpgradeRefresh` or by
  `Dispose`, and is merged into `PendingRefresh` (`Task.WhenAll(PendingRefresh, check)`); the cards' generation
  counter keeps the newest result where it overlaps another check.
- *Failures recorded before the sign-in*: before choosing the cards (so also when none needs checking) it calls
  `ForgetRecordedSourceFailures`, which clears the `SourceFailureMemo` of every check that is running - a periodic round
  or a launch-style check started before Settings closed. Such a check may have recorded "sign-in needed" or
  "unreachable" for a source before the user signed in or the connection came back; without this, a card that check
  reaches afterwards would be given the old failure without the source being read, and because that check of the card
  started after this re-check's, the card would show the old failure in place of the re-check's result. After the
  clear, those cards read the source again (with the session's sign-in). A read that is still in flight cannot record
  its failure after the clear either: the memo moves to a new generation and drops a failure read in an earlier one
  (see `SourceFailureMemo` in the package-version cache's design). The memos are cleared whole rather than per
  source: every card reads the same configured sources, and a forgotten failure costs one more read. After a save
  nothing is cleared: `StartUpgradeRefresh` cancels the running checks and starts one with a new memo. The
  package-version cache needs no clearing on this path: it never holds a failure, so the source behind a problem has
  no listing to serve, and the listings of sources that answered are within their five minutes, as for any background
  check (the chip's Refresh relists every source).

**AddRepo**: Adds a repository to the recent-repos list.

- *Parameters*: `string repoPath`.
- *Returns*: `bool` — `true` if added.
- *Preconditions*: None beyond a non-null/non-whitespace path.
- *Postconditions*: On success, a new card is appended at the end of `RepoCards` (and its entry at the end of
  `AppSettings.RecentRepos`), so it is at the bottom of its section (`-NewRepoAtEnd`), settings are persisted, and
  `DisplayedRepoCards` is recomputed (`AgentControl-MainWindowViewModel-AddRepo`). The new card is prepared first and
  its entry added through `CommitSettingsChange`; if the save fails the prepared card is retired, the exception
  reaches the caller and the list, the cards and the file are exactly as they were.

Returns `false` without persisting when `repoPath` does not exist on disk, is already
present (case-insensitive comparison, matching Windows path semantics), or is not the top folder of a Git repository.
Attempts to load an
existing pin via `RepoPinStore.Load`, tolerating (returning `null` for) a repo that has never
been synced.

**AddRepoCore** (private, shared by every add path): the checks, in order:

1. A path that is not a folder is `Missing`, or `NotAGitRepository` when it is an existing file (a dropped file).
2. A repo that is already listed (path compared ignoring case) is `AlreadyListed` for a plain add, or is moved into the
   requested group (`MovedExisting` / `MoveFailed`). A listed repo is never subject to the Git check, so the repos
   already in the list are left as they are, whatever their folders hold.
3. A folder for which `GitClient.IsRepositoryRoot` is `false` - no `.git` folder or file directly in it, for example a
   subfolder of a repository - is `NotAGitRepository` and nothing is added
   (`AgentControl-MainWindowViewModel-AddGitReposOnly`).
4. Otherwise the new card is prepared and its entry saved through `CommitSettingsChange` (`Added`); a failed save
   retires the prepared card and throws `InvalidOperationException`.

**AddRepoToGroup**: Adds a repository straight into one of the user's groups, or moves an already listed repo into it.

- *Parameters*: `string repoPath`, `string groupName`.
- *Returns*: `AddRepoResult` - `Added`, `MovedExisting`, `MoveFailed` (the card reported the failed save through
  `SettingsSaveFailed`), `Missing` (the folder does not exist) or `NotAGitRepository` (not the top folder of a Git
  repository); never `AlreadyListed` (a listed repo is moved) nor `SaveFailed` (a failed save throws).
- *Preconditions*: a non-blank path and a valid group name (`RepoGroupName.Validate`); a built-in section's name or a
  blank name throws `ArgumentException`.
- *Postconditions*: shares the add with `AddRepo` (the private `AddRepoCore`), with the group - spelled as an existing
  group of the same name ignoring case - set on the new entry before the single save, so the repo is never listed
  ungrouped in between; a repo already listed is moved with `RepoCardViewModel.MoveToGroup` instead of being added
  twice, and then placed at the bottom of the group like a newly added one (a failure to save that place is reported
  through `SettingsSaveFailed`; the repo stays in the group, where it was, and the result stays `MovedExisting`);
  and a collapsed target group is expanded so the repo is visible
  (`AgentControl-MainWindowViewModel-AddRepoToGroup`, `-NewRepoAtEnd`). The group is stored in the user's settings
  only.

**AddRepos**: Adds several folders at once - several folders chosen in the folder picker, or folders dropped from the
file manager - each as `AddRepo` (or, with a group, `AddRepoToGroup`) would, and reports what became of each
(`AgentControl-MainWindowViewModel-AddRepos`).

- *Parameters*: `IEnumerable<string> repoPaths` (in the order to add them), `string? groupName` (`null` for none).
- *Returns*: `IReadOnlyList<AddRepoOutcome>` — one outcome per distinct, non-blank path, in the order given.
- *Preconditions*: `repoPaths` is not null (`ArgumentNullException`); a given `groupName` is a valid group name, checked
  and spelled like an existing group once, before anything is added (`ArgumentException`, nothing added).
- *Postconditions*: Blank paths are ignored and a path given more than once (ignoring letter case) is handled once.
  Every folder goes through `AddRepoCore` on its own and is saved as it is added, so the list, the cards and the file
  stay in step whatever fails; one folder that is missing, not a Git repository, already listed or cannot be saved does
  not stop the others - a failed save (`InvalidOperationException`) becomes that folder's `SaveFailed` outcome with
  the redacted reason, and the next folder is tried. With a group, a collapsed target group is opened **once** at the
  end when anything was added to or moved into it (`OpenGroupAfterAdd`, which `AddRepoToGroup` uses too; a failure to
  remember the open state is ignored, the repos are in their group). Each new card's upgrade check starts in the
  background as for a single add.

**DescribeAddOutcomes** (internal, static, pure): Words what the user needs to know about an add
(`AgentControl-MainWindowViewModel-AddReposSummary`).

- *Parameters*: `IReadOnlyList<AddRepoOutcome> outcomes` (not null), `string? groupName`.
- *Returns*: `string?` — `null` when there is nothing to tell (no outcome, or every folder `Added`). For **one**
  outcome the single-add messages: `MovedExisting` - `'{path}' is already in the list, so it was moved into the group
  "{group}".`; `AlreadyListed` - `'{path}' could not be added: it is already in the list.`; `Missing` - `... it does
  not exist.`; `NotAGitRepository` - `... it is not the top folder of a Git repository (it has no .git folder or
  file).`; `SaveFailed` - `'{path}' could not be added. {problem}`; `Added` and `MoveFailed` (already reported) -
  `null`. For **several** outcomes one message, so a drop of many folders never opens a box per folder: `Added N of M
  folders.` followed by one line per kind of outcome that occurred, in this order - `Already in the list, moved into
  "{group}": ...`, `Already in the list: ...`, `Not the top folder of a Git repository (no .git folder or file): ...`,
  `Not found: ...`, `Not saved: ...` (`SaveFailed` and `MoveFailed`) - each naming at most five folders by their last
  path segment (the whole path for a drive root) and then `and K more`, so the fixed-width message box stays a
  manageable size; the first save problem's text, if any, ends the message.

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
- *Postconditions*: The live settings instance is replaced (preserving the existing recent-repos
  list in its order together with its `HasUserRepoOrder` marker, the collapsed-section names, and the group colors and
  group order, since
  `SettingsWindowViewModel` never owns them - `AgentControl-MainWindowViewModel-OrderKeptOnApplySettings`,
  `-GroupSettingsKeptOnApplySettings`) only after
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
descending, then the order of `RepoCards` - a stable sort that never looks at `LastLaunchedUtc`:
`AgentControl-MainWindowViewModel-SortOrder`) or, when `HasGroups`, the sections described under
*DisplayedItems* (`AgentControl-MainWindowViewModel-GroupSections`), each filled in `RepoCards` order. The groups'
sections are arranged by the private, pure `ArrangeGroups(names, order)`: the names with a position in the stored
order (`AppSettings.GroupOrder`) in that order, then the others alphabetically by `RepoGroupName.Comparer`; a stored
name that matches no group is skipped and names keep their spelling (`-GroupOrder`). Each header is given its group's
color (`header.UpdateColor(_settings.FindGroupColor(name))`, `null` for *Favorites* and *Ungrouped*,
`-GroupColorShown`) and, for a user group, whether it can move up or down (`header.UpdateOrder` with the reasons of
`GetGroupMoveDisabledReason`). A section is
collapsed only if
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

**RenameGroup**: Renames a group — every member's group name and the group's collapsed entry, color and place in the
group order move to
the new name (`AgentControl-MainWindowViewModel-GroupManagement`, `-GroupSettingsFollowRenameAndRemove`; the color and
the order are decided once, from the live state, before the change) — and returns `false` (changing nothing) for an
unknown group, an invalid name or another group's name; changing only the letter case of the group's own name is
allowed (`-GroupRenameRules`).
**RemoveGroup**: Removes a group — its repos become ungrouped, its collapsed entry, color and place in the group order
are forgotten
(`-GroupRemove`, `-GroupSettingsFollowRenameAndRemove`). Both are applied through `CommitSettingsChange`, persist to
the settings file only (never to a
repository, `-GroupSettingsOnly`) and, once saved, refresh the cards' group properties (`FinishGroupEdit`, which calls
`RaiseGroupPropertiesChanged` on every card) and the list. A group whose last repo leaves it by any other path (a
card moved out, a repo removed) forgets its color and position the same way, through the repair that
`CommitSettingsChange` runs after every change. A header's
`RenameCommand` and `RemoveGroupCommand` are forwarded as the `GroupRenameRequested` and `GroupRemoveRequested`
events, which change nothing by themselves (`-GroupHeaderRequests`); `MainWindow` shows the name prompt or a
confirmation and calls these methods.

**SetGroupColor**: Gives one of the user's groups a palette color, or takes it away
(`AgentControl-MainWindowViewModel-GroupColor`, `-GroupColorShown`).

- *Parameters*: `string groupName` (letter case ignored), `string? colorName` (a palette name, letter case ignored;
  `null` or blank for none).
- *Returns*: `bool` — `true` when the group now has the requested color (also when it already had it, in which case
  nothing is saved); `false`, changing nothing, for a color the palette does not know, a group that does not exist, a
  built-in section's name, or a failed save (reported through `SettingsSaveFailed`).
- *Postconditions*: The color is stored in `AppSettings.GroupColors` under the group's canonical spelling (any entry of
  the group in another letter case is removed) through `CommitSettingsChange`, so it is saved first and only then
  applied; then `FinishGroupEdit` raises the cards' group properties (the tiles of the group's cards without a color of
  their own follow) and recomputes the list (the header's accent follows). The color lives only in the per-user
  settings file, never in a repository.

**GetGroupMoveDisabledReason** / **CanMoveGroup**: Say whether a group can be moved a `RepoGroupMove` now, and if not
why (`AgentControl-MainWindowViewModel-MoveGroupAvailability`).

- *Returns*: `null` when the move is allowed; otherwise the text the header's menu shows as the tooltip of the disabled
  item: `GroupReorderDisabledWhileFilteringMessage` while the filter has text; "Favorites always come first and
  Ungrouped last, so they cannot be moved." for a built-in name; "This group is no longer in the list." for a group
  that has no section of its own (all its repos are favorites, or it no longer exists); "This group is already the
  first group." for Up on the first and "This group is already the last group." for Down on the last of the **movable
  groups** - the groups that have a section, in the order they are shown (`GetMovableGroups`). `CanMoveGroup` is
  `true` exactly when there is no reason.

**MoveGroup**: Moves a group one place up or down and saves the new order (`AgentControl-MainWindowViewModel-MoveGroup`,
`-GroupOrderPersisted`).

- *Postconditions*: When the move is allowed the group swaps places with its neighbor among the movable groups (it is
  placed before the previous one or after the next one) by `PlaceGroup`; otherwise nothing changes and `false` is
  returned. A header's `MoveRequested` is wired to this method in `GetHeader`.

**CanDropGroup** / **DropGroup**: Whether a dragged group header may be dropped at a `GroupDropTarget`, and the drop
itself (`AgentControl-MainWindowViewModel-DropGroup`).

- *Postconditions*: `CanDropGroup` is pure and refuses while the filter has text, a drop on the dragged group's own
  header, and a dragged or target group that is not a movable group (no section of its own, unknown, or a built-in
  section). `DropGroup` places the dragged group before or after the target group by `PlaceGroup`; a collapsed group
  stays collapsed. It returns `false`, changing nothing, when refused or when the save failed.

**PlaceGroup** (private): Puts a group before or after another one and saves the order.

- *Postconditions*: The new order is decided once, from the live state: the **full** arranged list of every group
  (`ArrangeGroups(GroupNames, GroupOrder)`, which includes groups whose repos are all favorites, so the stored order
  stays complete) without the group, with the group inserted next to the anchor. When that equals the current order
  nothing is saved and `true` is returned. Otherwise the whole list is stored as `AppSettings.GroupOrder` through
  `CommitSettingsChange` - so groups that had no stored position keep the place they were shown at - and the list is
  recomputed; a failed save is reported through `SettingsSaveFailed`, `false` is returned and nothing changed. The
  saved order is what the next start reads (`-GroupOrderPersisted`).

**GetSectionOf** (internal, static): The section a card is listed in: **Favorites** for a favorite, otherwise its group,
otherwise **Ungrouped** - the rule `DisplayedItems` builds its sections with. With no repo in a group the list has no
headers, and the same rule still yields two implicit tiers (Favorites, then everything else as Ungrouped) that the
flat, favorites-first list is made of. A private `IsInSection(card, kind, name)` compares a card's section with a
given one (names ignoring letter case) and `GetSectionMembers(card)` returns the cards of its section in `RepoCards`
order.

**GetMoveDisabledReason** / **CanMoveCard**: Say whether a card can be moved a given `RepoMove` now, and if not why.

- *Returns*: `GetMoveDisabledReason` returns `null` when the move is allowed, otherwise the text the menu shows as the
  tooltip of the disabled item: `ReorderDisabledWhileFilteringMessage` while the filter has text, "no longer in the
  list" for a card that is not in `RepoCards`, and "already the first/last one in its section" for `Top`/`Up` on the
  first and `Down`/`Bottom` on the last member of the section. `CanMoveCard` is `true` exactly when there is no reason
  (`AgentControl-MainWindowViewModel-MoveAvailability`, `-ReorderOffWhileFiltering`).

**GetReorderDisabledReason**: Says whether the whole **Reorder** submenu of a card is unavailable now, and if so why.

- *Returns*: `null` when at least one of the four moves applies; otherwise the filter explanation or "no longer in the
  list" reason when that is why every move is refused, and "the only one in its section" when the card is alone in its
  section so that no move leads anywhere (`AgentControl-MainWindowViewModel-MoveAvailability`).

**MoveCard**: Moves a card to the top, one up, one down or to the bottom of **its section** and saves the new order.

- *Postconditions*: The target is computed among the card's section members in `RepoCards` order (the first member,
  the previous one, the next one, the last one) and the card is placed before or after it by `PlaceCard` (below).
  Cards of other sections, which may be interleaved with the section's cards in the stored list, keep their relative
  order. When the move is not allowed, nothing changes and nothing is saved (`-MoveCard`).
- *Returns*: `true` when the card moved; `false` when the move was refused or the order could not be saved - that
  failure is reported through `SettingsSaveFailed` and nothing changed.

**CanDrop**: Pure check of whether a dragged card may be dropped at a `RepoDropTarget`.

- *Postconditions*: Refused (`false`) while the filter has text, for a card that is not in `RepoCards`, for a target
  whose anchor is no longer in `RepoCards` or no longer in the target section (a stale target), for a header of a group
  that no longer exists, and - when no repo is in a group - for a place in the other tier: the flat list has no
  headers that could explain the favorite change a drop there would imply, so in the flat list a drag only reorders
  **within** its tier (`AgentControl-MainWindowViewModel-DropReorder`). Dropping a card next to itself is allowed and
  changes nothing.

**DropCard**: Drops a dragged card at a place (`AgentControl-MainWindowViewModel-DropReorder`, `-DropChangesSection`,
`-DropOnHeader`).

- *Preconditions*: `CanDrop` is `true`; otherwise `false` is returned and nothing changes.
- *Postconditions*: When the card is not yet in the target section its section is changed first, through the paths
  the menu and the star use: dropping into **Favorites** sets `IsFavorite` (the card keeps its group); dropping into a
  **group** clears `IsFavorite` if set and calls `MoveToGroup(name)`; dropping into **Ungrouped** clears `IsFavorite`
  if set and calls `MoveToGroup(null)`. If the card is then not in the requested section (a failed save, already
  reported by the card through `SettingsSaveFailed`) nothing further is attempted and `false` is returned. Then
  `PlaceCard` puts the card next to the anchor (before for the upper half of the row, after for the lower half) or,
  for a header drop (no anchor), at the **end of the section**. A **collapsed** target section stays collapsed - its
  header count shows the change; expanding it would silently change the collapsed state the user chose
  (`AgentControl-MainWindowViewModel-DropOnHeader`).
- *Two saves*: a drop that changes the section saves twice - the section change, then the new position. If only the
  second fails, the card is in its new section at its old relative position and the failure is reported through
  `SettingsSaveFailed`; the result is `false`.

**PlaceCard** (private): Puts a card next to an anchor card, or at the end of its (current) section, in the stored
order.

- *Postconditions*: The new order is decided once from the live state: `RepoCards` without the card, with the card
  inserted before/after the anchor or - with no anchor - after the last other member of its section (at the end of the
  list when it has none). When that equals the current order nothing is saved. Otherwise
  `CommitSettingsChange` writes the order (`ApplyRepoOrder` reorders `AppSettings.RecentRepos` in place, by path
  ignoring letter case, an unlisted entry keeping its relative place after the listed ones; it also sets
  `HasUserRepoOrder`), and only after a successful save `RepoCards` is brought to the new order (`ObservableCollection`
  `Move` operations, so the cards stay attached to the window) and `UpdateDisplayedRepoCards` runs. A failed save is
  reported through `SettingsSaveFailed` (`ReportSaveFailure`), `false` is returned and nothing changed. A saved order
  is what the next start reads (`AgentControl-MainWindowViewModel-OrderPersisted`), and `RemoveRepo` leaves the rest
  of the order as it was.

**AttachCardEvents** (private): Subscribes to each card's `FavoriteChanged` and `GroupChanged` events so the displayed
list is refreshed. It deliberately does **not** subscribe to `LaunchRecorded` (the card still raises it): nothing
displayed depends on a launch any more, so a launch cannot move a card - structurally, not just through the sort
(`AgentControl-MainWindowViewModel-StableOrder`). The change itself was already saved by then: the card raises these
events only after its `commitChange` callback (`CommitRepoChange`) reported success, so a failed save raises no event
and refreshes nothing. A color change needs no re-ordering and so has no subscription here. A header's collapse toggle
is run through `RunReportingSaveFailure` where the header is created.

#### Error Handling

`AddRepo` throws `ArgumentException` for a null, empty, or whitespace `repoPath`.
`RemoveRepo`/`ApplySettings` throw `ArgumentNullException` for a null argument, and `ApplySettings`
throws `InvalidOperationException` before adopting anything when a source location embeds a
credential or when the settings file cannot be written, leaving the current settings in place. The
operations `AddRepo`, `AddRepoToGroup`, `RemoveRepo`, `ToggleGroupCollapsed`, `RenameGroup` and `RemoveGroup`
throw `InvalidOperationException` when the settings file cannot be written, having changed nothing (the window
catches it and tells the user); a change that comes from a card or a header button is not thrown but reported through
`SettingsSaveFailed` (the callback returns `false`, the card keeps its value); `MoveCard`, `DropCard`, `SetGroupColor`,
`MoveGroup`, `DropGroup` and the order
placement of `AddRepoToGroup` do not throw for a failed save either: they report it through `SettingsSaveFailed`,
return `false` (or keep `MovedExisting`) and change nothing; `AddRepos` does not throw for a failed save of one folder
either: that folder's outcome is `SaveFailed` with the redacted reason, the next folders are still tried, and the
window shows the reason in the summary of `DescribeAddOutcomes`; `AddRepos` throws `ArgumentNullException` for a null
list and `ArgumentException` for an invalid group name before adding anything; `ToggleGroupCollapsed` throws
`ArgumentException` for a blank name, while `RenameGroup` and `RemoveGroup` report an unknown group or unusable name
by returning `false`, as `SetGroupColor` does for an unknown group or color. A folder that is not a Git repository is
an outcome (`NotAGitRepository`), not an error. Group names read from
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
- **RepoGroupHeaderViewModel** (`LauncherUI`) — one header per section, updated in place (count, collapsed state,
  color, move availability); its toggle, rename, remove, add-repository and move requests are handled here.
- **CardColorPalette** (`LauncherUI` supporting type) — checks and spells a group color name in `SetGroupColor`.
- **GitClient** (`GitIntegration` subsystem) — the static `IsRepositoryRoot`, which every add path uses to accept only
  the top folder of a Git repository (no git process is started).
- **SecretRedactor** (`PackageCredentials` subsystem) — makes the reason of a failed save of `AddRepos` safe to show.
- **RepoPinStore** (`RepoConfig` subsystem) — `AddRepo` attempts to load an existing pin for a
  newly-added repo.
- **SettingsStore** (`Settings` subsystem) — saves the candidate settings of every change and of `ApplySettings`
  (`SaveSettings` persists the live `AppSettings` as they are);
  **RepoGroupName** (`Settings` subsystem) — the group-name rules used for sorting, validation and
  repair; **AppSettings** (`Settings` subsystem data type) — `FindGroupColor` and `RepairGroupSettings` for the group
  colors and group order.
- **PackageVersionCache** (`AgentPackageManagement` subsystem) — shared across all cards; invalidated per source by
  `ApplySettings` when a package source was edited or removed, and entirely at the start of each periodic re-check
  round.
- **TimeProvider** (.NET) — the timer and the clock of the periodic re-check and of the cache's expiry.
- **PackageCredentialContext** (`PackageCredentials` subsystem) — the session credentials and sign-in
  prompt it holds and passes on; **NuGetSourceProvider**, **GitSourceProvider**, **GitSourceOptions**
  (`AgentPackageManagement`) — created by `CreateConnectionTestProvider`.

#### Callers

- **App** — constructs `MainWindowViewModel` in `OnFrameworkInitializationCompleted`, passing the
  process-wide `PackageCredentialContext`, and assigns it as `MainWindow.DataContext`.
- **MainWindow** (code-behind) — calls `AddRepos` and `DescribeAddOutcomes` for the app bar's Add button, a header's
  "Add repository..." (both with several folders) and folders dropped from the file manager, and calls `RemoveRepo`,
  `ToggleGroupCollapsed`, `RenameGroup` and
  `RemoveGroup` (catching the `InvalidOperationException` of a failed save and showing a message), asks
  `IsReorderEnabled`, `CanMoveCard`/`GetMoveDisabledReason`, `GetReorderDisabledReason`, `CanDrop` and `CanDropGroup`
  and calls
  `MoveCard`, `DropCard` and `DropGroup` for the
  Move menu items and for drag and drop of cards and group headers (the failures are reported through
  `SettingsSaveFailed`), calls `SetGroupColor` from the group color palette, starts the
  upgrade check and the periodic re-check (`BeginUpgradeRefresh`, `StartPeriodicUpgradeRecheck`) when the view
  model is attached, disposes the view model when it closes, subscribes to
  `SettingsSaveFailed` to show a message for the changes that come from cards and headers, and reads `Settings` to open
  `SettingsWindow`, passing `CreateConnectionTestProvider` as its provider factory, and once that window closes calls
  `RecheckAfterSettingsClosed` with whether its `Saved` event was raised (not awaited); fills each card
  menu's "Move to group" submenu when the menu opens, shows the `GroupNameWindow` prompt and the
  removal confirmation for the `GroupRenameRequested`/`GroupRemoveRequested`/`NewGroupRequested`
  requests, opens the card and group color palette pop-ups, and calls `RenameGroup`/`RemoveGroup`.
