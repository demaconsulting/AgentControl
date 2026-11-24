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

The unit uses three independently documented `LauncherUI` units: `RepoGroupHeaderViewModel` (the header row that
starts a section, with `RepoGroupKind`, which kind of section a header introduces - see its own design),
`GroupNameViewModel` (the validating view model of the group-name prompt window `GroupNameWindow` - see its own
design) and `GlobalProgressViewModel` (the progress strip under the app bar, in which this view model shows its bulk
work - adding repositories, and reading the cards at start-up and after a settings save - see its own design). It
owns the one `RepoRefreshQueue` (see its own design) through which the cards' start-up reads, the folders of an add,
the cards' refresh after a settings save
and the cards' own reads are read off the UI thread, at most two at a time across all of them
(`AgentControl-MainWindowViewModel-SharedRepoReads`), and every add is saved once for the whole batch. It owns a
`SettingsWriter` (`Settings` subsystem, see its own design) through which every settings save is written in the
background, so a change the user makes is applied at once and the window never waits for the settings file.

#### Data Model

**RepoCards**: `ObservableCollection<RepoCardViewModel>` — The recent-repos list in the user's own order (the order of
`AppSettings.RecentRepos`; a repo added by `AddRepo` is appended at the end), backing this view model's underlying
state (settings persistence, additions/removals). Only `MoveCard`, `DropCard` (and the add paths, at the end) change
this order: it is **not** re-ordered when a repo is launched, refreshed, synced or marked as a favorite
(`AgentControl-MainWindowViewModel-StableOrder`). `DisplayedItems` and `DisplayedRepoCards` are derived from it
(favorites first, then this order).

**DisplayedItems**: `ObservableCollection<object>` — What the main window's list binds to: the visible repo cards
interleaved with the `RepoGroupHeaderViewModel` of each section. While no repo is in a group the list is flat (no
headers; favorites first, then the user's order from `RepoCards`,
`AgentControl-MainWindowViewModel-FlatListWithoutGroups`, `-SortOrder`). Once at least one repo is in a group
(`HasGroups`) it is sectioned: **Favorites** first (the favorite repos; a favorite is listed *only* there - not again
under its group, so every repo appears once - while it keeps its group, and its card shows a chip with it,
`-FavoriteListedOnce`), then **one section per group**, in the user's own group order - the groups with a position in
`AppSettings.GroupOrder` in that order, then the groups without one alphabetically (ordinal, ignoring letter case), so
the groups are alphabetical until the user orders them (`-GroupOrder`) - then **Ungrouped** last
(`AgentControl-MainWindowViewModel-GroupSections`). **Groups nest**: the groups form a tree (`AppSettings.GroupParents`,
see the *Settings* design), and a group's header is followed by the headers (and repos) of its **subgroups**, in their
own order, and only then by the group's **own repos** that are not favorites
(`AgentControl-MainWindowViewModel-NestedGroupTree`). A group's header counts the matching repos of its **whole
subtree**, and its tooltip says how many are directly in it; the headers and the cards under them know their depth
(`RepoGroupHeaderViewModel.Depth`, `RepoCardViewModel.Depth`) so the window can indent them. A group is listed when
something in its subtree matches or - with no filter - when it is *registered* (created inside another group or moved
within the hierarchy), so an empty group stays visible with a count of 0 until it is removed
(`-NestedGroupEmptyListed`); a group that is not registered is listed only while a repo is in it, as before. With no
hierarchy stored every group is top level and the list is the one that always was (`-NestedGroupFlatListUnchanged`).
Each section starts with a header that carries the section's name and count, and a section with no matching repo has no
header (`-GroupSectionHeaders`). Within a section repos appear in their relative order in `RepoCards`, the user's own
order (`-GroupSectionRepoOrder`). The cards of a collapsed section are left out, and so is its whole subtree; its header
stays with its count. While a filter is active every section is shown expanded and counts only the matches; sections
without a match have no header (so the groups that hold a match are listed, the others not), the headers' toggles are
disabled and the remembered collapsed names are left alone (`AgentControl-MainWindowViewModel-GroupCollapse`,
`-GroupFilter`, `-GroupFilterCounts`, `-GroupFilterOmitsEmpty`, `-GroupFilterToggleDisabled`,
`-GroupFilterKeepsCollapsedState`).

**DisplayedRepoCards**: `ObservableCollection<RepoCardViewModel>` — The filtered/sorted cards of
`DisplayedItems` (those inside a collapsed section are not included), in the same order.

**HasGroups**: `bool` (derived) — `true` when at least one repo is in a group or the hierarchy holds a group (one that
may have no repo); selects sectioned over flat. **GroupNames**: `IReadOnlyList<string>` (derived) — the names of the
groups that exist (`AppSettings.GetGroupNames`: those a repo is in and the registered ones), alphabetically (the card
menu's "Move to group" list, which so offers empty and nested groups too, `-GroupNamesIncludeEmptyGroups`; the sections
use the user's group order and the tree instead).

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
**GroupDropTarget**: `record` (`GroupName`, `InsertBefore`, `Inside`) — where a dragged group header lands: before (the
upper part of its header) or after (the lower part) another of the user's groups, as that group's sibling, or - with
`Inside` - inside it as its last subgroup (the middle part).
**RepoDropTarget**: `record` (`Kind`, `Name`, `Anchor`, `InsertBefore`) — a place a dragged card may land: a section
(its kind and display name) and either an anchor card of that section with "before/after it", or no anchor, which
means the end of the section (a drop on its header). `RepoDropTarget.ForHeader(header)` and
`RepoDropTarget.ForCard(anchor, insertBefore, kind, name)` build it; the window works out the anchor's section with
`GetSectionOf`.

**AddRepoResult**: `enum` — what became of a folder that was asked to be added: `Added` (a new entry, in the requested
group if one was given), `MovedExisting` (already listed, moved into the requested group), `MoveFailed` (that move
could not be saved; already reported through `SettingsSaveFailed`), `AlreadyListed` (already listed and no group was
requested), `Missing` (the folder does not exist), `NotAGitRepository` (not the top folder of a Git repository, or a
file rather than a folder) and `SaveFailed` (the batch could not be saved; used only by the multi-folder adds, which do
not throw). **AddRepoOutcome**: `record` (`Path`, `Result`, `Problem`) — one folder of a multi-folder add, with, for
`SaveFailed`, the reason the save failed, safe to show (secrets redacted by `SecretRedactor.SafeMessage`).

**RepoFolderKind**: `enum` (`Missing`, `NotAFolder`, `NotAGitRepositoryRoot`, `GitRoot`) — what a folder of an add
turned out to be on disk. **RepoFolderFacts**: immutable `record` (`Path`, `NormalizedPath`, `Kind`, `ListedAtStart`,
`Cheap`) — what the background part of an add read about one folder, handed from the worker thread to the UI thread;
`Cheap` is the `RepoCheapFacts` (pin, branch, `HEAD`, committed agent folders) of a Git root that was not listed when
the add was planned, otherwise `null`. **AddReposProgress**: `readonly record struct` (`Completed`, `Total`) — how
many folders of an add have been checked. **AddReposBatchResult**: `record` (`Outcomes`, `Canceled`) — what became of a
background add; `Outcomes` is empty and `Canceled` `true` when it was canceled before anything was saved or added.
**SettingsSaveRetryRequest**: `class` (`Message`, `Answer`, `Respond(bool retry)`) — the Retry/Discard question raised
by `AddSaveFailed`: a `TaskCompletionSource<bool>` with `RunContinuationsAsynchronously` (so answering from the dialog
never runs the add inside its close handling); the first answer wins; thread-safe.

**GlobalProgress**: `GlobalProgressViewModel` (get-only) — the state of the progress strip under the app bar, created
by the constructor; this view model is its only caller (`AgentControl-MainWindowViewModel-AddReposProgress`,
`-ApplySettingsInBackground`).

**PendingAddRepos**: `Task` (internal) — the chain of adds that are running or queued; completed when no add runs.
Never faults. Tests and the window's tests await it to let adds settle.

**_settingsWriter**: `SettingsWriter` (`Settings` subsystem) — writes every settings save of the view model in the
background, one write at a time on a worker thread, created by the constructor over the `saveSettings` seam
(`SettingsStore.Save` into the configuration directory by default; it must throw `InvalidOperationException` when the
file cannot be written; tests count, hold and fail writes) on `_timeProvider`, with the constructor's
`settingsWriteDelay` (`SettingsWriter.DefaultDelay`, 300 ms, by default). Its `WriteFailed` is handled by
`OnSettingsWriteFailed`. No save of the view model runs on the UI thread.

**_runningConfirmedSaves**, **_saveDeferred**: `List<RunningConfirmedSave>` and `bool` — the confirmed saves (an add,
the Settings window's Save) that are running, oldest first, each with its write (`Task`), a function that applies
its change to settings that are not the live ones (`ApplyTo`), a description of the change for the log
(`Description`: the repos an add adds and moves and its group, or the Settings window's settings) and a once-only
decision (`TryDecide`, see *Close*), and whether the live settings hold something to write once they have ended (see
*Background saving*). The list's count replaces a separate counter, so there is one source of truth.

**_lastWrittenSettings**: `AppSettings` — the last snapshot the writer wrote (set by the save delegate the constructor
wraps around `saveSettings`, on the writing thread; read with `Volatile`), or the settings as they were when the view
model was created while nothing was written yet: what the settings file is known to hold, against which the close logs
what it could not write.

**SettingsFlushTimeout**: `TimeSpan` (internal, five seconds) — the longest `Dispose` waits in all, for the confirmed
writes still running and the close's own write after them. A write normally takes milliseconds and `SettingsStore.Save`
gives a held file up after about a second, so the budget covers a running confirmed write and the close's write, each
on a held file; a write still running after it means the disk itself is not answering, which a longer wait would not
cure.

**CloseSettingsWrite**: `Task<bool>?` (internal) — the close's write of the settings (see *Close*), `null` before the
close; completes, never faulting, with whether everything was written. A test seam for a close that did not wait for it.

**_backgroundWork**: `IBackgroundWorkRunner` — constructor seam for how blocking reads run off the UI thread
(`ThreadPoolBackgroundWorkRunner.Instance` by default, the runner of the refresh queue; tests hold the reads at a
gate).

**_refreshQueue**, **_ownsRefreshQueue**: `RepoRefreshQueue` and `bool` — the queue every repo read goes through,
created here over `_backgroundWork` (two reads at a time) unless the constructor's optional `refreshQueue` supplied
one, and handed to every card; `Dispose` shuts down a queue created here. Its `ProgressChanged` is handled by
`OnRefreshQueueProgressChanged`.

**_upgradeCheckSlots**: `SemaphoreSlim` of `MaxConcurrentUpgradeChecks` (four) — the one limit every background
upgrade check shares (`CheckUpgradesAsync` for start-up, Settings, added repos and the re-check after Settings, and
`RecheckUpgradesAsync`), so overlapping checks never read more than four package sources at once between them. It is
never disposed: a check that is still unwinding when the window closes may release its slot.

**PendingStartupRefresh**: `Task` (internal) — the cards' start-up reads queued by the constructor; never faults.

**_refreshProgress**: `GlobalProgressOperation?` — the strip's operation for the queue's current wave of counted reads
("Refreshing repositories {completed} of {total}...", no cancel action); `null` while no wave runs.

**_gitTimeouts**: `GitClientTimeouts?` — the constructor's optional `gitTimeouts`, passed to every card and to the
add's folder checks (`null`: `GitClientTimeouts.Default`); tests pass a short limit against a git that hangs.

**_addSession**: `AddQueueSession?` (private class) — the adds that are running or queued and share one progress
operation (`GlobalProgressOperation`, begun with "Adding repositories..." and `CancelAddRepos` as its cancel action),
one `CancellationTokenSource` and a count of adds not yet ended; `null` when no add runs. Ending the last add of the
session disposes its progress operation and its cancellation.

**_settingsRefreshGeneration**: `int` — identifies the most recent card refresh started by `ApplySettingsAsync`, so an
older one that is still running neither rebuilds the list nor starts the upgrade check.

**AddSaveFailed**: `EventHandler<SettingsSaveRetryRequest>` (public event) — raised when the save at the end of a
background add failed; the window asks Retry or Discard and answers through the request
(`AgentControl-MainWindowViewModel-AddReposSaveFailure`). Without a subscriber the add is discarded.

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

**SettingsSaveFailed**: `EventHandler<string>` (public event) — raised on the UI thread with a message for the user when
the changes made in the window (favorite, group, card color, group color, group order, repo order, collapse, rename or
removal of a group, removal of a repo, launch stamp) could not be written in the background: once per failure streak,
saying that the changes stay in effect and are saved with the next change and when AgentControl closes; never after
the window closed. The window shows it in a message box it does not wait for
(`AgentControl-MainWindowViewModel-SaveFailureReported`). It is also raised, with its own message, when a synchronous
`AddRepoToGroup` could not save the move of a listed repo.

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

**Settings persistence model**: `_settingsWriter` is the only thing that writes the settings file while the window is
open and at its close. Its writes are one at a time, in the order asked, and every write is a full snapshot of the
intended state when it is queued: the confirmed state, the confirmed changes adopted so far and every ordinary change
made in the window (a confirmed save's candidate adds its own change). A confirmed save (an add, the Settings
window's Save) is a writer request whose task completes with success or failure; the caller then adopts or rolls back
exactly once, on the UI thread. The close always queues the full intended state as the final write (see *Close*).

**Background saving (apply at once, write in the background)**: every change the user makes in the main window goes
through the private `CommitSettingsChange(change)`:

1. *Apply*: `change` is applied to the live settings at once, followed by `AppSettings.RepairGroupSettings`, so a
   group that lost its last repo forgets its color and position.
2. *Schedule*: `ScheduleSettingsSave` hands `_settingsWriter` a snapshot of the live settings
   (`AppSettings.CreateSnapshot`, which shares nothing mutable with them). The writer writes the newest snapshot on a
   worker thread shortly after the last change; the UI thread never waits for the settings file.
3. *Refresh*: the callers then bring the cards and the displayed list up to date.

The change therefore never fails: a write that fails leaves the change in effect - the settings in memory, the cards and
the list - and the writer keeps the snapshot and writes it with the next change, the next flush or the close
(`AgentControl-MainWindowViewModel-ChangeAppliedWhenSaved`, `-FailedSaveChangesNothing`). The writer reports the first
failure of a streak through `WriteFailed`, which `OnSettingsWriteFailed` turns into `SettingsSaveFailed` with the reason
(redacted by `SecretRedactor.SafeMessage`) and the note that the changes stay in effect; after the view model was
disposed nothing is raised (`-SaveFailureReported`). The direct operations (`RemoveRepo`, `ToggleGroupCollapsed`,
`RenameGroup`, `RemoveGroup`, `CreateSubgroup`, `SetGroupColor`, `MoveGroup`, `DropGroup`, `MoveCard`, `DropCard`) no
longer throw or return `false` for a failed save. The changes that originate in a card are made through the private
`CommitRepoChange(repo, change)`, handed to each card as its `commitChange` callback when `CreateCard` builds it: it
applies the change to the repo's live entry through `CommitSettingsChange` (or directly, without saving, when the repo
is no longer in the list) and returns `true`; the card applies its own property and raises its change event (see the
`RepoCardViewModel` design).

*Confirmed saves*: an add and the Settings window's values must be on disk before they are applied, so their failure can
be answered (Retry/Discard, or the Settings window staying open) and Discard has nothing to undo. They go through the
private `SaveConfirmedAsync(applyTo, adopt, description)`: the candidate is `applyTo` applied to a snapshot of the live
settings (for an add `ApplyAddChange`, for the Settings window `WithOrganizingFrom`, which takes the new settings with
the repos and organizing choices of the snapshot), written by `_settingsWriter.SaveNowAsync` on a worker thread, and the
save is tracked in `_runningConfirmedSaves` until it has ended; `adopt` applies the change to the live settings, the
cards and the list on the UI thread once the write succeeded; a failure leaves everything as it was and reaches the
caller (`-SaveFailureRefusesOperation`). While a confirmed save runs, the candidate it writes does not hold a change
made meanwhile in the window, and a snapshot taken then would not hold the confirmed change: written after it, it would
take the add out of the file again. So `ScheduleSettingsSave` only records such a change (`_saveDeferred`) while
`_runningConfirmedSaves` is not empty, and when the last confirmed save has ended (and adopted its change) the live
settings - holding both - are scheduled. A confirmed save that ends while another one still runs sets `_saveDeferred`
too, because the other one's candidate was built without it. The writer itself keeps a snapshot scheduled before a
confirmed save when that save fails, so a change made before a discarded add is still written (`-AddKeepsOtherChanges`).
`_saveDeferred` only serves the open window (it keeps a confirmed save with no other change at one write); the close
never reads it, because it always writes the intended state last.

*Close*: `Dispose` writes every change made in the window, after any write still running, with one bounded wait
(`SettingsFlushTimeout`). It first takes the decision of every confirmed save still running (`TryDecide`): what becomes
of a confirmed save's change is decided exactly once, by the save's own completion when its write ends while the
window is open, otherwise by the close. A save the close decided adopts nothing when its write ends afterwards: the
closed view model's settings, cards and list are not changed and no refresh or upgrade check is started; its change
is on disk exactly when its write succeeded. The close then takes a snapshot of the live settings on the UI thread -
every change made in the window, those deferred while a confirmed save ran included, and no change of a confirmed save
that was not adopted - and starts its own write on a worker thread (`WriteSettingsAtCloseAsync`, exposed as
`CloseSettingsWrite`):

- It first awaits the write of every confirmed save the close decided, oldest first. Each one whose write succeeded has
  its change applied to the snapshot (`ApplyTo`); each one whose write failed is left out - nobody is left to offer
  Retry, so it is discarded, as Discard would, while everything else is kept - and a warning names it
  (`Description`) with the reason.
- The composed snapshot - the full intended state - is then always written, once, as the last write: with a
  confirmed save of the writer (`SaveNowAsync`), asked for only now that the confirmed writes it waited for have
  ended. The writer makes one write at a time in the order asked, so the snapshot is written after them (and after a
  scheduled write that was waiting for its turn), and drops an older waiting snapshot once written. Nothing decides
  whether the close needs to write: overlapping confirmed saves (an add and the Settings window's Save that run
  together each wrote a candidate built without the other's change, so the later write took the earlier change out of
  the file), a change held back while a confirmed save ran and a change still waiting for the debounce are all set
  right by the one write of the intended state. The close's write is therefore never written before an older
  confirmed write, and that older write can never follow it and take a change out of the file again.
- What could not be written is logged as the difference between `_lastWrittenSettings` and what should be on disk
  (repos added and removed, the changed fields of each repo, the repo order, the collapsed sections, the group colors
  and order, the Settings window's values). The writer is disposed when the close's write has ended.

The closing UI thread only waits for that write, at most `SettingsFlushTimeout`: the one wait for the disk left on the
UI thread, after the window has closed; the writes never need the UI thread, so it cannot deadlock. When the time runs
out the close returns anyway, so the process can exit (the write runs on a thread-pool thread, which does not keep the
process alive). The close's write stays queued behind the write that is still running and still happens if the process
lives long enough (an information entry then says so), so nothing is written out of order; the close logs at once the
confirmed saves still running and everything not on disk yet - which is lost if the process ends first - because
nobody can be told any more (`-SettingsFlushedOnClose`). Nothing is handed to the writer after the close
(`ScheduleSettingsSave` ignores a change, and a confirmed save or a synchronous add throws `ObjectDisposedException`),
so the close's write is the last one. The constructor's `settingsFlushTimeout` lets a test shorten the budget, so a
write still running after it is tested without waiting.

*Test seam*: `FlushSettingsAsync()` (internal) flushes the writer and completes when the writes accepted so far have
ended, so a test reads the settings file only once it holds every change.
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
  `IAgentToolProcessStarter? processStarter = null`, `ShellDetector? shellDetector = null`,
  `Action<AppSettings>? saveSettings = null`, `IBackgroundWorkRunner? backgroundWork = null`,
  `RepoRefreshQueue? refreshQueue = null`, `GitClientTimeouts? gitTimeouts = null` and
  `TimeSpan? settingsWriteDelay = null`. Must be called on the UI thread: the refresh queue it creates posts its
  progress, and the settings writer its failures, to the synchronization context current then.
- *Preconditions*: `settings` is not null; `upgradeRecheckInterval`, when given, is positive; `settingsWriteDelay`,
  when given, is not negative.
- *Postconditions*: One `RepoCardViewModel` exists per `RecentRepo` in `settings.RecentRepos`, built from the settings
  alone - no disk access and no git; `DisplayedItems` and `DisplayedRepoCards`
  reflect the initial sort/filter/grouping (`AgentControl-MainWindowViewModel-PopulateRecentRepos`); every card's
  cheap-facts read is queued (`PendingStartupRefresh`) and the strip shows the wave.
  No package source has been read (`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`) and no timer runs yet.

`StartStartupRefresh` queues each card's cheap facts (`RepoCardViewModel.RefreshRepoStateAsync(Background,
cheapFacts: true, workingTree: false, reportsProgress: true)`) in display order - `DisplayedRepoCards` first, then the
cards that are collapsed or filtered out - so the cards shown are read first and the strip shows "Refreshing
repositories {completed} of {total}..." without Cancel until the wave ends
(`AgentControl-MainWindowViewModel-StartupReadsQueued`). Each card shows that it is refreshing and its git-derived facts
as not known yet until its read lands (`RepoCardViewModel.HasRepoState`). The working-tree dirty check is not part of
start-up (deferred/lazy, per architecture.md's repo-fact caching strategy): the cards the window shows get it through
the queue when they are first attached. Even the pin file is read in the background, because a repo on an offline
network share can take tens of seconds to answer one file read; the pin the settings remember is shown meanwhile.

**WhenRepoStateSettledAsync** (internal): Waits until no card's cheap-facts or working-tree read waits or runs,
looping while new reads start (a card's working-tree check started when the window showed it); never faults. A test
seam for code that must act on settled cards.

**BeginUpgradeRefresh**: Starts checking every card's upgrade availability in the background.

- *Returns*: A task that completes when the checks have finished (the window ignores it; tests
  await it).
- *Postconditions*: Each card's `RefreshUpgradeStatusAsync` runs once the card's queued or running cheap-facts read was
  applied (`RepoCardViewModel.WhenCheapFactsSettledAsync`, awaited before the card takes a slot: applying cheap facts
  makes a check in flight irrelevant, so a check started earlier would be thrown away), at most four at a time across
  every background check (`_upgradeCheckSlots`), so a slow or unreachable source
  holds up only the cards' own checks, never the window, and a check still running for earlier settings (including a
  periodic round) is cancelled. The window calls it once it is shown; `AddRepo` checks the new card's upgrade status the
  same way, and `ApplySettingsAsync` starts a new round against the new sources
  (`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`); closing the Settings window without saving re-checks
  only the cards that show a problem (`RecheckAfterSettingsClosed`, below).
- *Busy cards*: `CheckUpgradesAsync` (the check behind `BeginUpgradeRefresh`, `ApplySettingsAsync`, an added repo and
  `RecheckAfterSettingsClosed`) skips a card that `IsBusy` with an action of its own (Refresh, Upgrade, Select Package,
  Launch, Pull) when its turn comes, as a periodic round does: a check beside the action would break the card's
  one-action-at-a-time rule and, being the card's newest check, could replace the action's result with its own. The
  card is not checked when it is queued but once it holds one of the few slots, immediately before its
  `RefreshUpgradeStatusAsync` starts, so an action the user started while the card waited for a slot is seen. The test
  of `IsBusy` and the synchronous start of `RefreshUpgradeStatusAsync` (which claims the card's newest check generation
  and sets `IsCheckingUpgrade`) run with no `await` between them, and the continuation after the slot wait resumes on
  the UI thread (the check is started there and its `await`s capture the UI synchronization context), the thread where
  the card's commands set `IsBusy`; so no action can start between the test and the start of the check. The wait for
  the card's state comes before the slot wait, never between the test and the start. An action started while the
  check waits for the card's state makes the card busy (its own promote-and-await sets `IsBusy` first), so the check
  skips it. A card whose
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
  has not finished, so a slow source never piles up work. Otherwise it takes the cards that `CanRecheckUpgrade` accepts:
  their state was read (`HasRepoState`), they have a pin, are not busy and
  their folder is not missing (a retired card is no longer in `RepoCards`, and a card that became busy while waiting its
  turn is skipped when its turn comes), **clears the cache** (the previous round's entries may still be inside their
  five minutes, and the round exists to see what the sources offer now) and checks them with
  `RefreshUpgradeStatusAsync(token)` - `allowPrompt` false, `relistSources` false, i.e. exactly the launch-time check:
  no sign-in dialog ever opens, and a failure - a server that needs a sign-in, an unreachable source - is shown in the
  card's own state (`IsUpgradeStatusUnknown`, `SourceProblem`) as at launch, never as a dialog. The cards are grouped by
  pinned package name: groups run in the shared slots (`_upgradeCheckSlots`, each group waiting for its cards' pending
  cheap-facts reads before it takes one), the cards of one group one after another, so the first card of a
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
  and the background upgrade check and card refresh tracked by `_refreshCancellation` (launch, settings change, added
  repo: it is cancelled, disposed and cleared the way `StartUpgradeRefresh` replaces it), so neither keeps waiting for
  a slow source or updates cards after the window closed, cancels the adds that have not saved yet
  (`CancelAddRepos`), shuts down a refresh queue it created (its waiting reads are canceled and a running git is
  stopped) and ends the strip's refresh line, and refuses further starts (a repo added afterwards starts no check, an
  add requested afterwards returns a canceled result); calling it twice is harmless.

**RecheckAfterSettingsClosed**: Checks again, in the background, the cards that show a package source problem or a
failed upgrade check, once the Settings window has closed
(`AgentControl-MainWindowViewModel-RecheckAfterSettingsClosed`).

- *Parameters*: `bool settingsSaved` — whether the Settings window saved the settings (its `Saved` event was raised)
  before it closed; `false` for Cancel, Escape or closing it without saving (including after a save that failed).
- *Returns*: A task that completes when the check it started has finished (cancellation is not an error); after a save
  it returns `PendingRefresh` (the background card refresh the save started and the upgrade check that follows it);
  already completed when the view model is disposed or no
  card needs checking. The window ignores it; tests await it.
- *Behavior*: While Settings is open the user may sign in with **Test connection** - whose provider
  (`CreateConnectionTestProvider`) uses `Credentials`, the same `SessionCredentialStore` the cards read without a prompt
  (`WithoutPrompt`) - or the network may come back, so without this the cards would keep showing "Sign-in needed" or a
  failed check until the next periodic round. A disposed view model starts nothing. After a save nothing is started,
  because `ApplySettingsAsync` always ends with `BeginUpgradeRefresh`, which checks every card: checking again would
  read every source twice. Otherwise it takes the cards that `CanRecheckUpgrade` accepts (state read, pinned, not busy,
  folder present) and that have `HasSourceProblem` or `IsUpgradeStatusUnknown`, or `IsCheckingUpgrade` (a background
  check is reading a source for the card at this moment: that read began before the sign-in, and if it fails it would
  put "Sign-in needed" on the card when it finishes, so a newer check is started and the card's generation counter
  ignores the older one's result), and checks them with `CheckUpgradesAsync` - the launch-style check (which, when a
  card's turn comes, skips it if the user has meanwhile started an action on it; see *Busy cards* above): `allowPrompt`
  false (no sign-in dialog), `relistSources` false (the cache is not cleared: a failure is never cached, so these cards
  read their sources again, while a listing cached for another card still serves), a shared `SourceFailureMemo`, a few
  at a time, counted in `_launchChecksInFlight` so no periodic round starts over it. Like the check of added repos
  (`BeginCardsUpgradeRefresh`) it runs with the token of `_refreshCancellation`, does not cancel a check that is
  running, is canceled by a later `StartUpgradeRefresh` or by `Dispose`, and is merged into `PendingRefresh`
  (`Task.WhenAll(PendingRefresh, check)`); the cards' generation counter keeps the newest result where it overlaps
  another check.
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
  `DisplayedRepoCards` is recomputed (`AgentControl-MainWindowViewModel-AddRepo`). It is a one-folder batch of the
  shared add steps (`AddRepoCore`): the folder is read inline, its entry is saved with a confirmed write of the settings
  writer (`ApplyAddCommitNow`, which waits for the write - made on a worker thread - only because this synchronous add,
  which no window code calls, must return its result), and only then is the card created; if the save fails the
  exception reaches the caller and the list, the cards and the settings are exactly as they were. The new card's
  working tree is not checked here: that runs in the background when its card is first shown.

Returns `false` without persisting when `repoPath` does not exist on disk, is already
present (`IsSameRepoPath`: the same folder ignoring letter case and a trailing directory separator), or is not the top
folder of a Git repository.
Loads an existing pin with the folder's cheap facts (`RepoCardViewModel.ReadCheapFacts`), tolerating a repo that has
never been synced (no pin) and a pin file that cannot be read (no pin).

**Add steps** (private, shared by every add path - `AddRepo`, `AddRepoToGroup`, `AddRepos` and `AddReposAsync`):

1. **PlanAddBatch** (UI thread, no file-system access): drops blank paths, keeps the first of the paths that name the
   same folder (`NormalizeRepoPath`, ignoring case), notes for each whether it is listed now (a card or a saved entry)
   and resolves the git executable.
2. **InspectFolder** (internal, static, any thread): reads one folder - a path that is not a folder is `Missing`, or
   `NotAFolder` when it is a file (a dropped file); a folder for which `GitClient.IsRepositoryRoot` is `false` is
   `NotAGitRepositoryRoot`; otherwise `GitRoot`, and only for a Git root that was not listed it also reads
   `RepoCardViewModel.ReadCheapFacts(path, git, cache: null, cancellationToken)` (pin, branch, `HEAD`, committed agent
   folders, with a `GitClient` of the configured executable and `_gitTimeouts`), so the new card needs no git call on
   the UI thread. The working tree is not checked. Returns an immutable `RepoFolderFacts`; the token (the refresh
   queue's shutdown) stops a running git.
3. **PrepareAddCommit** (UI thread, changes nothing): decides each folder's outcome against the list **as it is now**
   (so a repo listed while the folders were read is caught, and a folder added earlier in the same batch counts as
   listed for later ones), in the order: `Missing`; a file is `NotAGitRepository`; a listed repo is `AlreadyListed` for
   a plain add, or `MovedExisting` with a group (when it has a card) - a listed repo is never subject to the Git
   check, so the repos already in the list are left as they are; a folder that is not a Git root is `NotAGitRepository`
   (`AgentControl-MainWindowViewModel-AddGitReposOnly`); otherwise `Added`, with a new `RecentRepo` (path in its
   `NormalizeRepoPath` form, the group, the pin from the facts). It returns the outcomes and the steps (new entries
   and moves) in request order.
4. **ApplyAddCommitAsync** (UI thread; `ApplyAddCommitNow` for the synchronous adds): a batch with no step saves
   nothing. Otherwise `CreateAddCandidate` applies `ApplyAddSteps` to a snapshot of the live settings - a
   deterministic change that, in request order, appends each new entry and, for a moved repo, sets the group on its
   entry and moves the entry to the bottom of its section (`MoveToSectionEnd`: after the section's last other entry,
   or the end of the list; marks `HasUserRepoOrder` when the order changed), and removes the target group from the
   collapsed sections when anything joined it (so a target group collapsed while the add ran is opened) - and the
   candidate is written with one confirmed save (`SaveConfirmedAsync`, see *Background saving*), on a worker thread. A
   failed save throws `InvalidOperationException` with nothing changed. Only after the save, `AdoptAddCommit` applies
   the same steps to the live settings as they are then (so a change made while the add was saved is kept), and the
   new cards are created
   (`CreateCard` + `ApplyCheapFacts`, or a background `RefreshCheapInBackgroundAsync` through the shared queue for a
   card whose facts were not
   read - a repo that was listed when the add was planned but removed before the commit), their events attached,
   `RepoCards` put in the stored order (`SyncCollection`), the moved cards told their
   group
   changed, the list rebuilt **once** (`UpdateDisplayedRepoCards`), and the new cards' upgrade checks started in the
   background (`BeginCardsUpgradeRefresh`).

**NormalizeRepoPath** (internal, static): Gets the form of a repo path that identifies its folder.

- *Parameters*: `string path` (not null, `ArgumentNullException`).
- *Returns*: `string` - `Path.GetFullPath` of the path with any trailing directory separator removed by
  `Path.TrimEndingDirectorySeparator`, which keeps a root such as `C:\` or `/` valid; a path the platform cannot make
  full (`ArgumentException`, `NotSupportedException`, `PathTooLongException`) is returned as given, so the add reports
  it as missing instead of throwing.
- *Postconditions*: no file-system I/O. New entries are stored in this form; entries saved by earlier versions are
  never rewritten and are matched through `IsSameRepoPath`.

**IsSameRepoPath** (internal, static): Tells whether two repo paths name the same folder - their `NormalizeRepoPath`
forms are equal by ordinal comparison ignoring letter case - so a folder written with and without a trailing directory
separator, or in another letter case, is one repo. Used by the add's duplicate handling (through `NormalizeRepoPath`),
and
by `MainWindow` to find the card a card drag carries.

**AddRepoToGroup**: Adds a repository straight into one of the user's groups, or moves an already listed repo into it.

- *Parameters*: `string repoPath`, `string groupName`.
- *Returns*: `AddRepoResult` - `Added`, `MovedExisting`, `MoveFailed` (the card reported the failed save through
  `SettingsSaveFailed`), `Missing` (the folder does not exist) or `NotAGitRepository` (not the top folder of a Git
  repository); never `AlreadyListed` (a listed repo is moved) nor `SaveFailed` (a failed save throws).
- *Preconditions*: a non-blank path and a valid group name (`RepoGroupName.Validate`); a built-in section's name or a
  blank name throws `ArgumentException`.
- *Postconditions*: shares the add steps with `AddRepo` (the private `AddRepoCore`, a one-folder batch), with the
  group - spelled as an existing group of the same name ignoring case - set on the new entry before the single save, so
  the repo is never listed ungrouped in between; a repo already listed is moved into the group and placed at the bottom
  of its section in the same save instead of being added twice (a failed save of that move is reported through
  `SettingsSaveFailed` and gives `MoveFailed`, nothing changed); and a collapsed target group is expanded in the same
  save so the repo is visible
  (`AgentControl-MainWindowViewModel-AddRepoToGroup`, `-NewRepoAtEnd`). The group is stored in the user's settings
  only.

**AddRepos**: Adds several folders at once, synchronously - each as `AddRepo` (or, with a group, `AddRepoToGroup`)
would - and reports what became of each (`AgentControl-MainWindowViewModel-AddRepos`). The window adds through
`EnqueueAddReposAsync` instead; both share the add steps.

- *Parameters*: `IEnumerable<string> repoPaths` (in the order to add them), `string? groupName` (`null` for none).
- *Returns*: `IReadOnlyList<AddRepoOutcome>` — one outcome per distinct, non-blank path, in the order given.
- *Preconditions*: `repoPaths` is not null (`ArgumentNullException`); a given `groupName` is a valid group name, checked
  and spelled like an existing group once, before anything is added (`ArgumentException`, nothing added).
- *Postconditions*: Blank paths are ignored and a folder given more than once (`IsSameRepoPath`: ignoring letter case
  and a trailing directory separator) is handled once, under the first spelling given. The plan, an inline
  `InspectFolder` per folder and one commit: one folder that is missing, not a Git repository or already listed does
  not stop the others; the whole batch is saved **once** and the list rebuilt once; a collapsed target group is opened
  in that same save when anything was added to or moved into it. When the save fails (`InvalidOperationException`)
  nothing at all changed and every folder that would have been added or moved gets the `SaveFailed` outcome with the
  redacted reason (all-or-nothing); the other folders keep their outcome. Only the folders are checked here (the file
  system, no git: `InspectFolder` with `readCheapFacts: false`), so no git runs on the calling thread; the new cards'
  repo state is read through the shared queue (`Visible`), their upgrade checks follow it, and their working-tree
  checks run when their cards are first shown. No part of the application calls the synchronous adds.

**EnqueueAddReposAsync**: The add the window uses - folders from the picker or dropped from the file manager - run in
the background after any add that is already running or queued, with progress in `GlobalProgress`
(`AgentControl-MainWindowViewModel-AddReposInBackground`, `-AddReposQueued`, `-AddReposProgress`).

- *Parameters*: `IReadOnlyList<string> repoPaths`, `string? groupName`.
- *Returns*: `Task<AddReposBatchResult>` — what became of each folder, or a canceled result (nothing saved or added)
  when the user canceled or the window closed; it never faults for an expected failure (a failed save is part of the
  result).
- *Preconditions*: called on the UI thread; `repoPaths` not null (`ArgumentNullException`); a given `groupName` valid
  (`ArgumentException` at once, nothing queued).
- *Algorithm*: a disposed view model returns a canceled result. The request joins the running session (`_addSession`),
  or starts a new one when there is none or it was canceled (so an earlier Cancel never reaches an add asked for
  afterwards); a new session begins its progress operation ("Adding repositories...", cancelable). The request is
  chained after `PendingAddRepos` (`RunQueuedAddAsync` awaits the previous task, whatever became of it), so adds never
  run at the same time, and becomes the new `PendingAddRepos`. When its turn comes it shows "Adding repositories..."
  (Cancel allowed), reports "Adding {completed} of {total} repositories..." as folders are checked, and switches to
  "Saving..." with Cancel no longer allowed just before it commits. The last add of a session to end disposes the
  progress operation, so the strip goes idle (or shows an operation that is still running).
- *Postconditions*: the list reflects the batch, committed once; a repo listed while the folders were read is
  `AlreadyListed`, never added twice, including a folder named by two queued requests; a repo listed when the add was
  planned but removed before the commit is added again, its card's pin and branch read in the background; a target
  group removed while the add ran is created again by the commit (the user asked for that group), and a target group
  collapsed while it ran is opened.

**CancelAddRepos**: Cancels the running add and every add queued after it (the strip's Cancel)
(`AgentControl-MainWindowViewModel-AddReposCancel`). An add that is already saving completes; nothing else is saved or
added, and the canceled requests return a canceled result, so the window shows no message.

**AddReposAsync** (internal): The body of a queued add.

- *Parameters*: `IReadOnlyList<string> repoPaths`, `string? groupName`, `IProgress<AddReposProgress>? progress`,
  `CancellationToken cancellationToken`, `Action? onSaving = null`.
- *Algorithm*: canceled or disposed already: a canceled result. Plan on the UI thread and report `(0, total)`; read
  every folder through the shared queue (`_refreshQueue.EnqueueAsync` with the key (normalized path,
  `FolderInspection`), priority `Visible`, `InspectFolder` as the read and the add's token as the requester's), so the
  add shares the limit of two reads with every other repo read, keeping the results by index so the request order
  holds, and report `(completed, total)` as each completes (the continuations resume on the UI thread, so progress is
  reported there); a cancellation while reading gives a canceled result - the add's waiting checks leave the queue, and
  a check that already runs finishes without being used. Checked once more before the commit - the
  last moment to stop, nothing has been saved or added - then `onSaving` and `CommitAddBatchAsync`. The working tree
  of the new repos is not checked: their cards show the Git status as not checked yet until shown.

**CommitAddBatchAsync** (private): Commits a background add on the UI thread, asking the user when the save fails
(`AgentControl-MainWindowViewModel-AddReposSaveFailure`). Each attempt re-resolves the target group and prepares the
commit against the list as it is then (`PrepareAddCommit`, `ApplyAddCommitAsync`); on `InvalidOperationException` it
raises `AddSaveFailed` with a `SettingsSaveRetryRequest` whose message says the repositories could not be added because
the settings could not be saved, gives the redacted reason and says what Retry and Discard do, and awaits the answer:
Retry loops - the save is made again and, when it fails again, the question is asked again, until a save succeeds or
the user discards; Discard (or a disposed view model, or no subscriber, which answers Discard at once) returns the
outcomes with every folder that would have been added or moved as `SaveFailed`. Because the add is applied only once
saved, nothing needs undoing: memory, the cards and the list are as they were before the add, including the group,
the place and the collapsed state of repos the add would have moved. A change the user made in the window before or
during the add is kept and written as usual (`-AddKeepsOtherChanges`, see *Background saving*).

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
- *Postconditions*: The entry is removed from the live settings and the removal scheduled to be written
  (`CommitSettingsChange`); the card is removed from `RepoCards`, **retired** and `DisplayedRepoCards` recomputed
  (`AgentControl-MainWindowViewModel-RemoveRepo`). A failed write keeps the removal and is reported once
  (see *Background saving*).

Retiring (`RepoCardViewModel.Retire`, see its design) cancels what the card still has running and silences it, so a
removed repo's pending upgrade check or operation can never show an error, release notes or a prompt
(`AgentControl-MainWindowViewModel-RemoveRepoRetiresCard`).

Performs no confirmation itself: `MainWindow`'s code-behind is responsible for showing a
`ConfirmationWindow` before calling this, in response to the card raising `RemoveRequested`.

**ApplySettingsAsync**: Applies updated settings from the settings window.

- *Parameters*: `AppSettings updated`.
- *Returns*: `Task` — completes when the settings were saved and adopted; faults with `InvalidOperationException`
  when they could not be saved.
- *Preconditions*: `updated` is not null; a source location that embeds a credential is refused at once
  (`InvalidOperationException` thrown before anything is saved).
- *Algorithm*: the candidate is a snapshot of `updated` with the live repo list, `HasUserRepoOrder`, collapsed
  sections, group colors, group order and group hierarchy; it is written by a confirmed save of the settings writer
  (`SaveConfirmedAsync`) on a worker thread, so the window does not wait for the settings file
  (`AgentControl-MainWindowViewModel-ApplySettingsSaveInBackground`); `AdoptSettings` runs once it is on disk.
- *Postconditions*: The live settings instance is replaced (preserving the live recent-repos list in its order together
  with its `HasUserRepoOrder` marker, the collapsed-section names, and the group colors, group order and group
  hierarchy as they are at
  adoption - so a change made in the main window while the settings were saved is kept and written after them - since
  `SettingsWindowViewModel` never owns them - `AgentControl-MainWindowViewModel-OrderKeptOnApplySettings`,
  `-GroupSettingsKeptOnApplySettings`) only after the candidate has been persisted successfully — if the write fails,
  the previous instance stays live, so a later save (favorite, launch) can never persist a rejected candidate and the
  exception reaches the caller to show; the shared `PackageVersionCache` is invalidated for each package source that was
  edited or removed (the sources before and after are compared by alias and by `PackageSourceDefinition.IsEquivalentTo`;
  unchanged sources keep their cached results) (`AgentControl-MainWindowViewModel-InvalidateChangedSources`). The save
  and the adoption happen before the returned task completes; the cards are then refreshed in the background: the
  adoption cancels the upgrade check and the periodic round that were running (they were started for the previous
  settings) and sets `PendingRefresh` to `RefreshCardsAfterSettingsAsync`, which counts as a launch-style check while it
  runs (so no periodic round starts), queues every card's cheap facts through the shared refresh queue at background
  priority, in display order (the cards in `DisplayedRepoCards` first, then the collapsed and filtered-out ones),
  counted in the queue's progress wave (`RepoCardViewModel.RefreshRepoStateAsync` with `reportsProgress`), so
  `GlobalProgress` shows "Refreshing repositories {completed} of {total}..." (no Cancel; nothing for an empty list; see
  `OnRefreshQueueProgressChanged`), rebuilds the list once and then awaits `BeginUpgradeRefresh` against the new sources
  (`AgentControl-MainWindowViewModel-ApplySettingsInBackground`). A newer `ApplySettingsAsync` supersedes an older
  refresh (`_settingsRefreshGeneration`): the older one neither rebuilds the list nor starts the upgrade check (the
  cards' own generations already make the newest read win); `Dispose` cancels it. A present card is additionally given a
  fresh Git status check (through the queue, after its cheap facts) when either it reappeared (was missing, now present)
  during this refresh, so it is not left showing a "not yet checked" state indefinitely, or `GitExecutablePath` itself
  changed, so a clean/dirty result cached against the previously configured executable is not kept showing once a
  different executable is in effect; a present card that was already checked and whose executable path did not change
  only gets the cheap refresh, avoiding a `git status` call — which scans the whole working tree — for every tracked
  repo on every settings save (`AgentControl-MainWindowViewModel-ApplySettings`). Once the settings are saved, the saved
  `Theme` is handed to the injected theme applier (`Action<ThemePreference>`, supplied by `App`), so the choice takes
  effect without a restart (`AgentControl-MainWindowViewModel-ApplyTheme`); a refused save never reaches this step
  (`AgentControl-MainWindowViewModel-RefusedThemeNotApplied`).

**OnRefreshQueueProgressChanged** (private): Shows the shared refresh queue's wave of counted reads in
`GlobalProgress`: while it is active, one operation without a cancel action (begun on the first report) reporting
"Refreshing repositories {completed} of {total}..."; when it ends (or the view model was disposed), the operation is
disposed. The queue was created on the UI thread and posts its reports there, so the strip only changes on the UI
thread.

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

**UpdateDisplayedRepoCards** (private): Recomputes `DisplayedItems` and `DisplayedRepoCards` from `RepoCards` — a
case-insensitive substring match against `FilterText` on repo name or path
(`AgentControl-MainWindowViewModel-SearchFilter`), then either the flat order (favorite status descending, then the
order of `RepoCards` - a stable sort that never looks at `LastLaunchedUtc`:
`AgentControl-MainWindowViewModel-SortOrder`) or, when `HasGroups`, the sections described under *DisplayedItems*
(`AgentControl-MainWindowViewModel-GroupSections`), each filled in `RepoCards` order. The groups form a tree that is
walked depth first with an explicit stack (so any depth works): `BuildGroupPlan` builds a `RepoGroupHierarchy` from the
groups that exist, `GroupParents` and `GroupOrder` (a card's group the settings do not list yet is added, so a card is
never lost), and the groups that have a section (registered, or with a non-favorite repo anywhere in their subtree). The
siblings of each level are arranged by `RepoGroupHierarchy.GetChildren` - the names with a position in the stored order
(`AppSettings.GroupOrder`) in that order, then the others alphabetically by `RepoGroupName.Comparer`; a stored name that
matches no group is skipped and names keep their spelling (`-GroupOrder`). Each header is given its count (the matches
in its subtree) and the number directly in the group, its depth and its parent, its group's color
(`header.UpdateColor(_settings.FindGroupColor(name))`, `null` for *Favorites* and *Ungrouped*, `-GroupColorShown`) and,
for a user group, whether it can move up or down (`header.UpdateOrder` with the reasons of
`GetGroupMoveDisabledReason`); each card is given the depth of the group that lists it. A group is listed when its
subtree has a match or, with no filter, it is registered; a collapsed group lists nothing below its header
(`-NestedGroupTree`, `-NestedGroupEmptyListed`). A section is collapsed only if no filter is active and its name is in
`AppSettings.CollapsedGroups` (`AgentControl-MainWindowViewModel-GroupCollapse`, `-GroupFilter`). Both collections are
brought to the new state **in place** (remove what is gone, move and insert the rest) rather than cleared, so unchanged
rows keep their controls and focus. Also re-raises change notifications for `HasGroups`, `GroupNames`,
`HasNoDisplayedRepos` and `EmptyStateMessage`, since every path that can change the displayed set (add/remove, filter,
favorite-toggle, grouping) runs through this method.

**ToggleGroupCollapsed** / **IsGroupCollapsed**: Collapse or expand a section by name (a group, or
`Favorites` / `Ungrouped`; letter case ignored), add or remove the name in
`AppSettings.CollapsedGroups` (through `CommitSettingsChange`, so applied at once and written in the background), and
refresh the list. The collapsed names are written to the settings
file, are read back when the view model is built (`AgentControl-MainWindowViewModel-GroupCollapseRemembered`) and are
kept by `ApplySettingsAsync` (`-GroupCollapseKeptOnApplySettings`). The built-in names are reserved
(`RepoGroupName`), so a group can never collide with them. Damaged group or collapsed values in the settings
(a null list, blank, reserved or overlong names, names differing only in letter case) are read through
`RepoGroupName` so that the list is still shown (`-DamagedGroupSettings`).

**RenameGroup**: Renames a group — every member's group name and the group's collapsed entry, color, place in the group
order and place in the hierarchy (as the group and as the parent of other groups, `-NestedGroupRename`) move to the new
name (`AgentControl-MainWindowViewModel-GroupManagement`, `-GroupSettingsFollowRenameAndRemove`; the color and the order
are decided once, from the live state, before the change) — and returns `false` (changing nothing) for an unknown group,
an invalid name or another group's name; changing only the letter case of the group's own name is allowed
(`-GroupRenameRules`). **RemoveGroup**: Removes a group but keeps what is in it - the group's repos go to the group it
was inside (they become ungrouped for a top-level group, so the flat behavior is unchanged), its subgroups go there too
and take the group's place in the group order in their own order (`RepoGroupHierarchy.Pop`), and its collapsed entry,
color, place in the group order and place in the hierarchy are forgotten (`-GroupRemove`,
`-NestedGroupRemoveKeepsContents`, `-GroupSettingsFollowRenameAndRemove`). The repos keep their positions in the repo
list. Both are applied through `CommitSettingsChange`, persist to the settings file only (never to a repository,
`-GroupSettingsOnly`) and then refresh the cards' group properties (`FinishGroupEdit`, which calls
`RaiseGroupPropertiesChanged` on every card) and the list. A group whose last repo leaves it by any other path (a card
moved out, a repo removed) forgets its color and position the same way unless it is registered, through the repair that
`CommitSettingsChange` runs after every change. A header's `RenameCommand`, `RemoveGroupCommand` and
`NewSubgroupCommand` are forwarded as the `GroupRenameRequested`, `GroupRemoveRequested` and `GroupNewSubgroupRequested`
events, which change nothing by themselves (`-GroupHeaderRequests`); `MainWindow` shows the name prompt or a
confirmation and calls these methods.

**CreateSubgroup**: Creates a group inside another group (`AgentControl-MainWindowViewModel-NestedGroupCreate`).

- *Parameters*: `string parentName` (an existing group; letter case ignored), `string name`.
- *Returns*: `bool` — `true` when the group was created; `false`, changing nothing, for a parent that does not exist, a
  name that is invalid (`RepoGroupName.Validate`) or any group already has (names are unique across the whole tree, an
  empty group's name included), or Favorites/Ungrouped.
- *Postconditions*: The new group is registered as a child of the parent, and the parent and every group it is inside
  are registered too, so all of them exist until deleted; the parent and its ancestors are expanded so the new group is
  seen. Stored through `CommitSettingsChange` (applied at once, written in the background, only in the settings file).
  The new group has no stored place, so it is listed after the siblings that have one, alphabetically with the others.

**SetGroupColor**: Gives one of the user's groups a palette color, or takes it away
(`AgentControl-MainWindowViewModel-GroupColor`, `-GroupColorShown`).

- *Parameters*: `string groupName` (letter case ignored), `string? colorName` (a palette name, letter case ignored;
  `null` or blank for none).
- *Returns*: `bool` — `true` when the group now has the requested color (also when it already had it, in which case
  nothing is saved); `false`, changing nothing, for a color the palette does not know, a group that does not exist or a
  built-in section's name.
- *Postconditions*: The color is stored in `AppSettings.GroupColors` under the group's canonical spelling (any entry of
  the group in another letter case is removed) through `CommitSettingsChange`, so it is applied at once and written in
  the background; then `FinishGroupEdit` raises the cards' group properties (the tiles of the group's cards without a
  color of their own follow) and recomputes the list (the header's accent follows). The color lives only in the per-user
  settings file, never in a repository.

**GetGroupMoveDisabledReason** / **CanMoveGroup**: Say whether a group can be moved a `RepoGroupMove` now, and if not
why (`AgentControl-MainWindowViewModel-MoveGroupAvailability`).

- *Returns*: `null` when the move is allowed; otherwise the text the header's menu shows as the tooltip of the disabled
  item: `GroupReorderDisabledWhileFilteringMessage` while the filter has text; "Favorites always come first and
  Ungrouped last, so they cannot be moved." for a built-in name; "This group is no longer in the list." for a group
  that has no section of its own (all its repos are favorites, or it no longer exists); "This group is already the
  first group." for Up on the first and "This group is already the last group." for Down on the last of the **sibling
  groups** - the groups that have a section and are inside the same parent group (or at the top level), in the order
  they are shown; for a nested group the wording ends "... in its parent group." (`-NestedGroupReorder`).
  `CanMoveGroup` is
  `true` exactly when there is no reason.

**MoveGroup**: Moves a group one place up or down among its siblings and saves the new order
(`AgentControl-MainWindowViewModel-MoveGroup`, `-GroupOrderPersisted`).

- *Postconditions*: When the move is allowed the group swaps places with its neighbor among the sibling groups (it is
  placed before the previous one or after the next one) by `PlaceGroup`, without changing its parent; otherwise nothing
  changes and `false` is
  returned. A header's `MoveRequested` is wired to this method in `GetHeader`.

**CanDropGroup** / **DropGroup**: Whether a dragged group header may be dropped at a `GroupDropTarget`, and the drop
itself (`AgentControl-MainWindowViewModel-DropGroup`, `-NestedGroupDrop`).

- *Postconditions*: `CanDropGroup` is pure and refuses while the filter has text, a drop on the dragged group's own
  header, a dragged or target group that has no section of its own (unknown, or a built-in
  section), and a drop that would put the group inside itself or one of its subgroups - next to or inside any of them
  (`RepoGroupHierarchy.CanMove`). The group ends up inside the target when the drop is `Inside`, otherwise inside the
  target's parent (or at the top level), so dropping next to a nested header takes the group out of its old parent.
  `DropGroup` then places the group (as the last subgroup of the target for `Inside`, otherwise before or after the
  target) by `PlaceGroup`; a collapsed group
  stays collapsed. It returns `false`, changing nothing, when refused.

**PlaceGroup** (private): Puts a group inside a parent (or at the top level) and before or after another one, and saves.

- *Postconditions*: The new order and hierarchy are decided once, from the live state: the **full** arranged list of
  every group (`RepoGroupHierarchy.ArrangeGroups`, which includes groups whose repos are all favorites, so
  the stored order stays complete) without the group, with the group inserted next to the anchor (no anchor leaves the
  order alone), and the stored parents from `RepoGroupHierarchy.PutInside` (a group put inside another is registered
  with the groups it is inside; an ordinary top-level group that is only reordered is not, so a flat list is untouched).
  When neither differs from what is stored nothing is saved and `true` is returned. Otherwise both are stored in **one**
  `CommitSettingsChange` - so groups that had no stored position keep the place they were shown at - and the
  list is recomputed; `true` is returned. The saved order is what the next start reads (`-GroupOrderPersisted`).

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
- *Returns*: `true` when the card moved; `false` when the move was refused (nothing changed).

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
  if set and calls `MoveToGroup(null)`. If the card is then not in the requested section (the card refused the change)
  nothing further is attempted and `false` is returned. Then
  `PlaceCard` puts the card next to the anchor (before for the upper half of the row, after for the lower half) or,
  for a header drop (no anchor), at the **end of the section**. A **collapsed** target section stays collapsed - its
  header count shows the change; expanding it would silently change the collapsed state the user chose
  (`AgentControl-MainWindowViewModel-DropOnHeader`).
- *Two changes*: a drop that changes the section makes two changes - the section, then the position. Both are applied
  at once and scheduled; the settings writer folds them into one write, and a failure of that write is reported once.

**PlaceCard** (private): Puts a card next to an anchor card, or at the end of its (current) section, in the stored
order.

- *Postconditions*: The new order is decided once from the live state: `RepoCards` without the card, with the card
  inserted before/after the anchor or - with no anchor - after the last other member of its section (at the end of the
  list when it has none). When that equals the current order nothing is saved. Otherwise
  `CommitSettingsChange` applies and schedules the order (`ApplyRepoOrder` reorders `AppSettings.RecentRepos` in place,
  by path ignoring letter case, an unlisted entry keeping its relative place after the listed ones; it also sets
  `HasUserRepoOrder`), then `RepoCards` is brought to the new order (`ObservableCollection`
  `Move` operations, so the cards stay attached to the window) and `UpdateDisplayedRepoCards` runs. A saved order
  is what the next start reads (`AgentControl-MainWindowViewModel-OrderPersisted`), and `RemoveRepo` leaves the rest
  of the order as it was.

**AttachCardEvents** (private): Subscribes to each card's `FavoriteChanged` and `GroupChanged` events so the displayed
list is refreshed. It deliberately does **not** subscribe to `LaunchRecorded` (the card still raises it): nothing
displayed depends on a launch any more, so a launch cannot move a card - structurally, not just through the sort
(`AgentControl-MainWindowViewModel-StableOrder`). The change itself was already applied (and scheduled to be written)
by then: the card raises these events only after its `commitChange` callback (`CommitRepoChange`) accepted it. A color
change needs no re-ordering and so has no subscription here. A header's collapse toggle calls `ToggleGroupCollapsed`
where the header is created.

#### Error Handling

`AddRepo` throws `ArgumentException` for a null, empty, or whitespace `repoPath`.
`RemoveRepo`/`ApplySettingsAsync` throw `ArgumentNullException` for a null argument, and `ApplySettingsAsync`
throws `InvalidOperationException` before adopting anything when a source location embeds a
credential (synchronously) or when the settings file cannot be written (through its task), leaving the current settings
in place. The changes made in the window (`RemoveRepo`, `ToggleGroupCollapsed`, `RenameGroup`, `RemoveGroup`,
`MoveCard`, `DropCard`, `SetGroupColor`, `MoveGroup`, `DropGroup` and the card's own changes) never fail for a save:
they are applied at once, and a background write that fails is reported once per failure streak through
`SettingsSaveFailed` while the change stays in effect. `AddRepo` and `AddRepoToGroup` (for a new repo) throw
`InvalidOperationException` when the settings file cannot be written, having changed nothing; the move placement of
`AddRepoToGroup` reports it through `SettingsSaveFailed` and returns `MoveFailed`; `AddRepos` does not throw for a
failed save either: nothing of
the batch changed, every folder that would have been added or moved gets the `SaveFailed` outcome with the redacted
reason, and the window shows the reason in the summary of `DescribeAddOutcomes`; `EnqueueAddReposAsync` turns a failed
save into the Retry/Discard question (`AddSaveFailed`) and, on Discard, into the same `SaveFailed` outcomes, and its
task never faults for an expected failure (a queued add whose predecessor faulted still runs); a cancellation before
the commit is a canceled result, not an exception; `AddRepos` and `EnqueueAddReposAsync` throw `ArgumentNullException`
for a null list and `ArgumentException` for an invalid group name before adding (or queuing) anything;
`ToggleGroupCollapsed` throws
`ArgumentException` for a blank name, while `RenameGroup` and `RemoveGroup` report an unknown group or unusable name
by returning `false`, as `SetGroupColor` does for an unknown group or color. A folder that is not a Git repository is
an outcome (`NotAGitRepository`), not an error. Group names read from
settings are repaired (`RepoGroupName.Sanitize`) on the fly, a null collapsed list reads as empty, so
damaged values never throw. A failed background write never throws into the UI thread: the settings writer reports
it through `WriteFailed`, and `Dispose` logs what it could not write at the close. A pin file that cannot be
read while a folder is added is captured in its `RepoCheapFacts` (the card then shows the reason) and never prevents
the add from succeeding. A background card refresh after a settings save that is canceled (the window closed) ends
quietly.

#### Dependencies

- **RepoCardViewModel** — one instance per tracked repo; `MainWindowViewModel` constructs and refreshes each card,
  hands it the `commitChange` callback (`CommitRepoChange`), keeps the membership of `RepoCards` and retires a
  card when its repo is removed.
- **RepoGroupHeaderViewModel** (`LauncherUI`) — one header per section, updated in place (count, collapsed state,
  color, move availability); its toggle, rename, remove, add-repository and move requests are handled here.
- **GlobalProgressViewModel** (`LauncherUI`) — the progress strip's state (`GlobalProgress`), in which the adds (one
  cancelable operation per session) and the refresh queue's waves of counted reads (not cancelable) are shown.
- **RepoRefreshQueue** (`LauncherUI`) — the one queue (two reads at a time) through which the folder checks of an add
  and every card's reads run off the UI thread; created and disposed here and handed to every card.
- **IBackgroundWorkRunner** / **ThreadPoolBackgroundWorkRunner** (`LauncherUI` supporting type,
  `BackgroundWorkRunner.cs`) — the runner under the refresh queue.
- **CardColorPalette** (`LauncherUI` supporting type) — checks and spells a group color name in `SetGroupColor`.
- **GitClient** (`GitIntegration` subsystem) — the static `IsRepositoryRoot`, which every add path uses to accept only
  the top folder of a Git repository (no git process is started).
- **SecretRedactor** (`PackageCredentials` subsystem) — makes the reason of a failed save safe to show.
- **RepoCardViewModel.ReadCheapFacts** (`LauncherUI`) — reads, on a worker thread of the refresh queue, the pin
  (`RepoConfig`'s `RepoPinStore`), branch and committed agent folders of a folder being added, before it has a card,
  through a `GitClient` (`GitIntegration`) with `_gitTimeouts`.
- **SettingsWriter** (`Settings` subsystem) — writes every settings save in the background: the snapshots scheduled
  after the changes made in the window, the confirmed saves of an add and of `ApplySettingsAsync`, and the close's write
  (a confirmed save, or a flush, after the confirmed writes still running). **SettingsStore** (`Settings` subsystem) —
  the writer's default save delegate (`SettingsStore.Save` into the
  configuration directory);
  **AppLogging** (`Logging` subsystem) — logs, through a `SecretRedactingLogger` (`PackageCredentials`), the settings
  that could not be written at the close;
  **RepoGroupName** (`Settings` subsystem) — the group-name rules used for sorting, validation and
  repair; **RepoGroupHierarchy** (`Settings` subsystem) — the group tree: the listing order, the siblings of a group,
  which moves are allowed and what a drop, a creation or a removal stores; **AppSettings** (`Settings` subsystem data
  type) — `FindGroupColor`, `GetGroupNames` and `RepairGroupSettings` for the groups, their hierarchy, colors and
  order.
- **PackageVersionCache** (`AgentPackageManagement` subsystem) — shared across all cards; invalidated per source by
  `ApplySettingsAsync` when a package source was edited or removed, and entirely at the start of each periodic re-check
  round.
- **TimeProvider** (.NET) — the timer and the clock of the periodic re-check and of the cache's expiry.
- **PackageCredentialContext** (`PackageCredentials` subsystem) — the session credentials and sign-in
  prompt it holds and passes on; **NuGetSourceProvider**, **GitSourceProvider**, **GitSourceOptions**
  (`AgentPackageManagement`) — created by `CreateConnectionTestProvider`.

#### Callers

- **App** — constructs `MainWindowViewModel` in `OnFrameworkInitializationCompleted`, passing the
  process-wide `PackageCredentialContext`, and assigns it as `MainWindow.DataContext`.
- **MainWindow** (code-behind) — calls `EnqueueAddReposAsync` and `DescribeAddOutcomes` for the app bar's Add button,
  a header's "Add repository..." (both with several folders) and folders dropped from the file manager (the drop's
  add posted to the UI thread), binds the progress strip to `GlobalProgress`, answers `AddSaveFailed` with the
  Retry/Discard `ConfirmationWindow`, and calls `RemoveRepo`,
  `ToggleGroupCollapsed`, `RenameGroup` and
  `RemoveGroup` (a failed save is reported through `SettingsSaveFailed`, in a message box it does not wait for), asks
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
