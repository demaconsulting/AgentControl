### RepoRefreshQueue

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`RepoRefreshQueue` is the one queue every lazy read of a repo's state goes through: the cards' cheap facts (folder,
pin file, branch, `HEAD`, committed agent folders) and working-tree checks, and the folder checks of an add. Its
single responsibility is to decide when each read runs - a fixed, small number at a time, the most urgent first,
each wanted read once - so that however many repos and callers there are, the machine never runs more than a couple
of git processes for them at once. It knows nothing of cards, settings or the window; a read is an opaque delegate
that returns an immutable result. Its behavior is specified by the unit requirements
`AgentControl-RepoRefreshQueue-Limit`, `-Coalesce`, `-Priority`, `-Promote`, `-WaveProgress`, `-Cancel`, `-Shutdown`
and `-FailureIsolated` (each prefixed `AgentControl-RepoRefreshQueue`), and together with `MainWindowViewModel` and
`RepoCardViewModel` it provides `AgentControl-LauncherUI-BoundedRepoReads`. The small types in the same file
(`RepoRefreshKind`, `RepoRefreshPriority`, `RepoRefreshKey`, `RepoRefreshProgress`) are part of this unit.

#### Data Model

**RepoRefreshKind** (enum): what a read finds out - `CheapFacts`, `WorkingTree` or `FolderInspection`.

**RepoRefreshPriority** (enum, ordered): `Background` (0, work nobody looks at yet), `Visible` (1, a card on screen
or a folder being added), `UserAction` (2, a read a user action waits for).

**RepoRefreshKey** (record struct): `RepoPath` and `Kind`. Two keys are equal when the kinds match and the paths
match ignoring letter case, like every repo path in the application; callers pass the path in the form the card or the
add stores. Equal keys name the same read.

**RepoRefreshProgress** (record struct): `Completed`, `Total` and `IsActive` of the current wave of counted reads.

**DefaultMaxConcurrentReads** (`const int`, 2): the limit. One read is at most one git process (or one burst of
file-system reads). Two, not one, so a single slow repo - on a network share, with a huge working tree, or with a git
that hangs until its time limit (see *GitClient*) - cannot stall every other read, including a user action promoted
behind it. Two, not more, because the reads compete for the same disk and are scanned by antivirus at process start:
more parallel `git status` runs give little and make the machine sluggish. The application's other process budgets
are separate: the upgrade checks read package sources under their own limit (`MainWindowViewModel`), and user actions
run one at a time per card.

Private state, all guarded by one lock:

- **_waiting** (`List`) and **_waitingByKey** (`Dictionary<RepoRefreshKey, ...>`): the reads waiting for a slot, in
  request order, and the same reads by key so a request can join one. A read is in both or in neither.
- **_running** (`int`): how many reads run; never above the limit.
- **_nextSequence** (`long`): the request order number of the next new read.
- **_countedPending** (`int`), **_progress** (`RepoRefreshProgress`) and **_progressVersion**: the counted reads that
  wait or run, the current wave and a change counter for its reports.
- **_idle** (`TaskCompletionSource?`): completed when nothing waits or runs.
- **_disposed** (`bool`) and **_shutdown** (`CancellationTokenSource`, its token kept in **_shutdownToken**): set and
  signaled by `Dispose`.

Each queued read (private `QueuedRead<T>`) holds its key, request order, priority (raised by joins and promotions),
whether it is counted, how many requesters still wait for it, the read delegate (replaced by the newest joining
request) and a `TaskCompletionSource<T>` created with `RunContinuationsAsynchronously`.

Invariants: `_running <= limit`; a key is waiting at most once; `_progress.IsActive` exactly when a counted read
waits or runs and the queue was not disposed.

#### Key Methods

**EnqueueAsync&lt;T&gt;(key, priority, read, reportsProgress = false, cancellationToken = default)**: Queues a read,
or joins the waiting read with the same key, and returns at once.

- *Parameters*: the key (its path not null), the priority, the read (`Func<CancellationToken, T>`, not null; runs on
  a worker thread, touches no view-model state, receives the queue's shutdown token), whether it counts in the
  progress wave, and the requester's token.
- *Algorithm*: under the lock - refuse with a canceled task when disposed; join a waiting read with the key (the read
  gets the newest delegate, the higher priority, one more requester, and is counted when either request asked; a
  waiting read with another result type is a programming error, `InvalidOperationException`), or append a new read
  (`AgentControl-RepoRefreshQueue-Coalesce`). A read with the key that already runs is not joined: the running read
  may predate the change the requester reacts to, so a new read is queued, which later requests can join. A newly
  counted read starts a new wave when none is pending and adds one to its total (a join that was already counted adds
  nothing). Then the progress is reported and slots are filled (`StartReads`).
- *Returns*: the read's task for a requester that cannot cancel, otherwise a task that is canceled when the requester
  gives up. It completes with the read's result, faults with the read's exception, or is canceled when the request
  was given up, the queue was disposed before the read ran, or the read was stopped by the disposal.

**StartReads** (private): while a slot is free, takes the waiting read with the highest priority - the earliest
requested among equals - out of the waiting lists and starts it outside the lock
(`AgentControl-RepoRefreshQueue-Limit`, `-Priority`).

**RunReadAsync** (private): runs the read through the `IBackgroundWorkRunner` with the shutdown token, then, under the
lock, frees the slot, counts a counted read as completed (ending the wave when it was the last) and signals idle;
reports the progress; and only then completes the requesters' task, so a requester that resumes sees the bookkeeping
already done. Then it fills the free slot. A read that ended after the queue shut down is delivered as canceled; an
exception of the read faults that read's task only (`AgentControl-RepoRefreshQueue-FailureIsolated`).

**Promote(key, priority)**: Raises the priority of the waiting read with the key, if lower; returns whether such a
read was waiting. A running read cannot be promoted (`AgentControl-RepoRefreshQueue-Promote`).

**ProgressChanged** (event) and **Progress**: the current wave of counted reads
(`AgentControl-RepoRefreshQueue-WaveProgress`). The constructor captures the current `SynchronizationContext`. With one
(the UI thread in the application), every change is posted to it under the lock, so the posts arrive in the order of the
changes, each with its own value. With none, the change is raised on the thread that made it, one raise at a time under
a dedicated event lock, always with the newest value and never twice for one change.

**WhenIdleAsync()**: a task that completes once nothing waits or runs (for tests and callers that must let the reads
settle).

**Dispose()**: idempotent. Marks the queue disposed, takes every waiting read out (and out of the wave), ends the
progress, signals the shutdown token - a running read's git process is stopped through it (see *GitClient*) - cancels
the waiting reads' tasks and disposes the token source. Later requests are refused with a canceled task
(`AgentControl-RepoRefreshQueue-Shutdown`).

**Giving up** (private `AwaitCancelableRequestAsync`, `OnRequesterGaveUp`): a requester whose token is signaled gets
a canceled task; before that task completes, the requester is removed from the read, and a waiting read with no
requester left is taken out of the queue and out of the wave without running
(`AgentControl-RepoRefreshQueue-Cancel`). A running read is never interrupted by a requester.

#### Error Handling

`EnqueueAsync` throws `ArgumentNullException` for a null read or key path and `InvalidOperationException` for a join
with another result type; the constructor throws `ArgumentOutOfRangeException` for a limit below one. Every other
outcome is reported through the requester's task: a read's exception faults only that read's task; an
`OperationCanceledException` of a read after the shutdown, and a read that ends after it, are reported as canceled.
The callers (`RepoCardViewModel`, `MainWindowViewModel`) treat a canceled read as "apply nothing"; card reads capture
their own git and disk failures in their results, so they do not throw.

Thread safety: every member is safe to call from any thread. The bookkeeping runs under one lock and does no I/O, so
requesting a read on the UI thread costs a few in-memory operations; no requester continuation ever runs inside the
lock, because the tasks complete asynchronously.

#### Dependencies

- **IBackgroundWorkRunner** / **ThreadPoolBackgroundWorkRunner** (`LauncherUI` supporting type) - runs each read off
  the calling thread; tests substitute a gate.

#### Callers

- **MainWindowViewModel** (`LauncherUI`) - owns the queue (created on the UI thread), shares it with every card,
  queues the folder checks of an add (`FolderInspection`, `Visible`, the add's cancellation), shows the counted wave in
  the global progress strip (`Refreshing repositories {n} of {total}...`) and disposes the queue when the window
  closes.
- **RepoCardViewModel** (`LauncherUI`) - queues its cheap facts and working-tree checks (`RefreshRepoStateAsync`),
  promotes them when shown (`PrioritizeRepoState`) and gives up its waiting reads when it is retired. A card created
  on its own (in tests) creates a private queue.

Which work goes through the queue, and at which priority:

- *Settings saved*: every card, in display order - `CheapFacts` at `Background`, counted; then `WorkingTree` at
  `Background` when the folder came back or the git executable changed.
- *A card first shown in the window*: `WorkingTree` at `Visible`, and the card's waiting reads promoted to `Visible`.
- *Folders being added*: `FolderInspection` at `Visible`, not counted (the add shows its own count).
- *A re-added repo whose facts were not read*: `CheapFacts` at `Background`.
- *Refresh, and after a pull*: their working-tree check, `WorkingTree` at `Background`.
