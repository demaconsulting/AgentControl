### SettingsWriter

![Settings Structure](SettingsView.svg)

#### Purpose

`SettingsWriter` writes settings snapshots to disk in the background, so that a change the user makes in the window
never waits for the settings file. Its single responsibility is to decide when each write runs: one at a time, on a
worker thread, the newest snapshot winning. It does not know what the snapshots hold or where they go: it calls a save
delegate it is given (`SettingsStore.Save` into the configuration directory, in the application). Its behavior is
specified by the unit requirements `AgentControl-SettingsWriter-Debounce`, `-Serialize`, `-ConfirmedSave`,
`-FailureReportedOnce` and `-Flush` (each prefixed `AgentControl-SettingsWriter`), and together with `SettingsStore` it
provides `AgentControl-Settings-BackgroundWriter`.

Writing the settings file can take a while: `SettingsStore.Save` retries for up to about 900 ms when another process (a
virus scanner, an indexer, a cloud-sync client) holds the file. Before this unit the window saved on the UI thread on
every favorite, color, group, order, collapse, removal and launch, and froze for that long whenever the file was held.

#### Data Model

**DefaultDelay** (`static readonly TimeSpan`, 300 ms): the debounce delay used when the caller gives none. Long enough
to fold a burst of clicks or a drag (several changes in quick succession) into one write, short enough that a change is
on disk well before a user could close the window and expect it kept; the close writes the intended state last anyway.

Read-only state set by the constructor:

- **_save** (`Action<AppSettings>`): writes one snapshot; throws when it could not be written.
- **_delay** (`TimeSpan`): the debounce delay.
- **_context** (`SynchronizationContext?`): the context current at construction, which `WriteFailed` is posted to (the
  UI thread in the application); `null` raises the event on the writing thread.
- **_timer** (`ITimer`): the debounce timer, created from the injected `TimeProvider` (the system clock by default, a
  `FakeTimeProvider` in tests); `Schedule` (re)starts it and it writes the waiting snapshot when it fires.
- **_writeTurn** (`SemaphoreSlim(1, 1)`): the turn to write. Every write - scheduled, confirmed or flushed - takes it
  before it starts and releases it when it has ended, so writes never overlap and run in the order they asked for the
  turn (`SemaphoreSlim` serves asynchronous waiters first in, first out). It is never disposed, because a write still
  running when the writer is disposed releases it.

Mutable state, all guarded by one lock (`_lock`):

- **_waiting** (`AppSettings?`) and **_waitingVersion** (`long`): the snapshot waiting to be written and the number
  `Schedule` gave it.
- **_version** (`long`): the number of the most recent `Schedule` call. A confirmed save remembers it when it is called,
  so it can tell a snapshot scheduled before it (older) from one scheduled while it ran (newer).
- **_writing** (`bool`): whether a write is running. It is set in the same lock that takes the snapshot, so
  `HasUnsavedChanges` never misses a snapshot between "taken" and "being written".
- **_failureReported** (`bool`): whether the current failure streak was reported; any write that succeeds clears it.
- **_disposed** (`bool`): after `Dispose`, `Schedule` is ignored and `SaveNowAsync` throws `ObjectDisposedException`.

Invariant: a snapshot handed to the writer is never changed by anyone again. The caller builds it with
`AppSettings.CreateSnapshot` - a full copy of the repos, sources, collapsed sections, group colors and group order -
so the window can keep changing its live settings while the snapshot is serialized on another thread.

#### Key Methods

**Constructor** `SettingsWriter(Action<AppSettings> save, TimeProvider? timeProvider = null, TimeSpan? delay = null)`:
throws `ArgumentNullException` for a null `save` and `ArgumentOutOfRangeException` for a negative `delay`; zero writes
at once. Captures `SynchronizationContext.Current`, so the owner creates the writer on the UI thread.

**Schedule(AppSettings snapshot)**: replaces the waiting snapshot (latest wins), gives it the next version and restarts
the timer with the delay (a trailing debounce: a write happens only once no newer snapshot came for the delay). It
returns at once and never waits for a write (`AgentControl-SettingsWriter-Debounce`). When the timer fires, the
waiting snapshot is written in its turn (*WriteWaitingAsync*, below).

**SaveNowAsync(AppSettings snapshot)** (`Task`): a confirmed write, for a change the caller must not apply unless it is
on disk (an add, the Settings window's Save). It notes `_version`, waits for its turn, runs the save on a worker
thread, and completes when the snapshot is written or faults with the exception the save threw
(`AgentControl-SettingsWriter-ConfirmedSave`):

- *Written*: a waiting snapshot whose version is not newer than the one noted is dropped and the timer stopped - the
  confirmed snapshot was built from settings that already held it, so writing it afterwards would put an older state
  back. A snapshot scheduled after the call is newer and stays waiting. A written confirmed snapshot also ends a
  failure streak.
- *Failed*: nothing waiting is dropped, so a change made before or during the confirmed save is still written by its
  timer, the next change or a flush. `WriteFailed` is not raised: the caller is told through the task.

**FlushAsync()** (`Task<bool>`): stops the timer and writes the waiting snapshot in its turn, so it completes, never
faulting, once every write accepted before the call has ended. It returns `true` when the waiting snapshot (if any) was
written and `false` when its write failed. A snapshot kept after a failed write is written by it as well
(`AgentControl-SettingsWriter-Flush`). The save never runs on the calling thread and never needs it (every `await` in
the writer uses `ConfigureAwait(false)`), so an owner may wait for the returned task on the UI thread without a
deadlock, and the blocking-call guard of `SettingsStore` is never tripped by it. A bounded wait is the owner's (the
window's close waits for its own write, see `MainWindowViewModel`).

**HasUnsavedChanges** (`bool`): whether a snapshot is waiting (scheduled, or kept after a failed write) or a write is
running.

**WriteFailed** (`event EventHandler<Exception>`): raised when a scheduled or flushed write fails after the write
before it succeeded - once per failure streak, not per failed write - with the failure (not redacted; the owner
redacts it for the user). Posted to the captured context, or raised on the writing thread when there was none
(`AgentControl-SettingsWriter-FailureReportedOnce`).

**Dispose()**: stops the timer and ignores later `Schedule` calls; a write in progress completes. The owner writes
what is left first (the window's close disposes the writer when its own write has ended).

*WriteWaitingAsync* (private, the body of every scheduled and flushed write): waits for the turn; takes the waiting
snapshot (if none, it returns `true`); runs the save on a worker thread (`Task.Run`); on failure puts the snapshot back
as waiting - unless a newer one was scheduled meanwhile - without restarting the timer (it is written with the next
change, the next flush or a confirmed save, never retried in a loop), and raises `WriteFailed` when the streak was not
reported yet. The report is made before the turn is released, so a flush that follows it sees it done.

#### Error Handling

Every exception the save throws counts as a failed write: a confirmed save rethrows it to its caller (with its original
stack, `ExceptionDispatchInfo`); a scheduled or flushed write keeps the snapshot and reports the first failure of the
streak through `WriteFailed`. No background write can fault an unobserved task or reach the UI thread's unhandled
exception path. `Schedule`, `SaveNowAsync` and the constructor validate their arguments
(`ArgumentNullException`, `ArgumentOutOfRangeException`); `SaveNowAsync` after `Dispose` throws
`ObjectDisposedException` (an `InvalidOperationException`, so the owner's failed-save path handles it).

Thread safety: every member may be called from any thread; the state is guarded by one lock, and no lock is held while
the save runs or an event handler is called.

#### Dependencies

- **AppSettings** (data type, same subsystem) — the snapshots written; built by its `CreateSnapshot`.
- **.NET BCL** — `TimeProvider`/`ITimer` (the debounce), `SemaphoreSlim` (the turn), `Task.Run` (the worker thread),
  `SynchronizationContext` (the failure report), `ExceptionDispatchInfo`.

The save delegate is injected; the writer has no dependency on `SettingsStore` itself.

#### Callers

- **MainWindowViewModel** (`LauncherUI` subsystem) — owns one writer over `SettingsStore.Save` into the configuration
  directory (or the test seam `saveSettings`), on its `TimeProvider`: it schedules a snapshot of the live settings after
  every change made in the window, makes the confirmed saves of an add and of the Settings window's values, shows
  `WriteFailed` to the user through its `SettingsSaveFailed` event, and offers `FlushAsync` to the tests
  (`FlushSettingsAsync`). When the window closes it relies on the turn's order: on a worker thread it first awaits the
  confirmed writes still running, and only then always makes its own write - a confirmed save of the full intended
  state it composed - so that write queues behind every write accepted before it (a scheduled write waiting for its
  turn included), drops an older waiting snapshot and is the last one; the owner waits for it with a bounded wait of
  its own and then disposes the writer. The writer is the only thing that writes the settings file.
