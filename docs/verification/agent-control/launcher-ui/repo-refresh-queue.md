### RepoRefreshQueue

#### Verification Approach

`RepoRefreshQueue` (with the `RepoRefreshKind`, `RepoRefreshPriority`, `RepoRefreshKey` and `RepoRefreshProgress` types
in its file) is verified with unit tests in `RepoRefreshQueueTests.cs`. Each test constructs a real queue over the
thread-pool runner (one shutdown test uses the gated runner of the `LauncherUI` tests instead, to hold a read before it
begins) and requests fake reads: each read records that it started, counts how many reads run at that
moment and blocks on a gate of its own (a `TaskCompletionSource`) until the test opens it, so the test decides exactly
when each read ends and needs no timing assumption. Nothing else is mocked. The queues are created with no
synchronization context current, so the progress is raised on the changing thread; one test creates its queue under a
collecting synchronization context instead, to verify that the progress is posted to it. Which reads the cards and
the main view model queue, at which priority, is verified by those units (`RepoCardViewModelTests.cs`,
`MainWindowViewModelTests.cs`) and by the headless window tests of the `LauncherUI` subsystem.

#### Test Environment

N/A - standard test environment (no window, no files and no processes are involved).

#### Acceptance Criteria

- However many reads are requested, never more than two run at the same time, and every read runs once and delivers its
  result.
- A request for a read that is waiting joins it: the read runs once, with the newest delegate, and every requester gets
  its result; a request for a read that runs queues one new read that later requests join.
- Waiting reads start by priority (user action, visible, background), in request order within a priority, and a
  promoted waiting read starts before the reads of its former priority; a running read cannot be promoted.
- The progress counts the current wave of counted reads (a join adds nothing), ends when the last one finishes, starts
  a new wave with the next counted read, and is posted to the synchronization context captured at creation.
- A waiting read whose only requester gives up is removed without running and leaves the wave.
- Disposing the queue signals the running read's token, cancels the waiting read without running it, ends the
  progress and refuses later requests; a read the runner held before it began never begins and is reported as
  canceled, and a read still running can keep using its token until it ends and is reported as canceled.
- A read that throws faults only its own task.

#### Test Scenarios

**RepoRefreshQueue_Limit**: Ten reads that all block on one gate are requested; while the gate is closed exactly two
run; once it opens all ten deliver their results and at most two ever ran at once. This scenario is tested by
`RepoRefreshQueue_ManyReads_NeverMoreThanLimitAtOnce`, covering `AgentControl-RepoRefreshQueue-Limit`.

**RepoRefreshQueue_Coalesce**: With the one slot held, two requests for the same repo (the path spelled with another
letter case) and a newer delegate both get the newer delegate's result from a single read
(`RepoRefreshQueue_SameKeyWhileWaiting_JoinsAndReadsOnce`); while a repo's read runs, two more requests for it get one
new read, which both share (`RepoRefreshQueue_SameKeyWhileRunning_QueuesOneNewRead`). These cover
`AgentControl-RepoRefreshQueue-Coalesce`.

**RepoRefreshQueue_Priority**: With the one slot held, two background reads, a user-action read and a visible read are
requested in that order; they start user action, visible, then the background reads in request order. This scenario is
tested by `RepoRefreshQueue_HigherPriority_StartsBeforeEarlierBackground`, covering
`AgentControl-RepoRefreshQueue-Priority`.

**RepoRefreshQueue_Promote**: With the one slot held, the last of three background reads is promoted to visible and
starts first; promoting the running read reports that it was not waiting. This scenario is tested by
`RepoRefreshQueue_Promote_MovesWaitingItemAhead`, covering `AgentControl-RepoRefreshQueue-Promote`.

**RepoRefreshQueue_WaveProgress**: Three counted reads give 0 of 3, active; the first finishing gives 1 of 3; a request
that joins a waiting read leaves 1 of 3; two more give 1 of 5; when all finish the wave is 5 of 5 and inactive; a new
counted read starts a new wave at 0 of 1; the reports raised follow the same values
(`RepoRefreshQueue_Progress_CountsCurrentWave`). A queue created under a collecting synchronization context does not
raise its progress on the requesting thread but posts it, and running the posts gives 0 of 1 active and later 1 of 1
inactive (`RepoRefreshQueue_ProgressChanged_PostedToCapturedContext`). These cover
`AgentControl-RepoRefreshQueue-WaveProgress`.

**RepoRefreshQueue_Cancel**: With the one slot held, a counted read whose requester then gives up is canceled, the
progress goes from 0 of 1 active to 0 of 0 inactive, and the read never runs. This scenario is tested by
`RepoRefreshQueue_RequesterCanceledWhileWaiting_RemovedFromQueueAndWave`, covering
`AgentControl-RepoRefreshQueue-Cancel`.

**RepoRefreshQueue_Shutdown**: A running read that stands in for git (it stops when its token is signaled) and a counted
read waiting behind it; disposing the queue cancels both, a request made afterwards is canceled, no other read ran and
the progress is inactive (`RepoRefreshQueue_Dispose_CancelsWaitingAndSignalsRunningAndRefusesNew`). Over a gated
runner (`GatedBackgroundWorkRunner`) that holds a read before it begins, the queue is disposed and the runner then
lets the read go: the read is canceled and never began - had it begun, reading its token's wait handle would have met
a disposed source (`RepoRefreshQueue_Dispose_ReadHeldBeforeItBegins_NeverBeginsAndIsCanceled`). A read held while it
runs is let go after the disposal and then reads its token's wait handle and registers on it, as git does: it is
canceled, not failed with `ObjectDisposedException`
(`RepoRefreshQueue_Dispose_ReadStillRunning_KeepsWorkingTokenAndIsCanceled`). These cover
`AgentControl-RepoRefreshQueue-Shutdown`.

**RepoRefreshQueue_FailureIsolated**: A read that throws `InvalidOperationException` faults its task with that
exception, while another read delivers its result. This scenario is tested by
`RepoRefreshQueue_ReadThrows_FaultsOnlyThatTask`, covering `AgentControl-RepoRefreshQueue-FailureIsolated`.
