### BlockingCallGuard

#### Verification Approach

`BlockingCallGuard` (with `BlockingCallOnGuardedThreadException` in its file) is verified with unit tests in
`BlockingCallGuardTests.cs`. The tests call the guard directly; nothing is mocked. Because the guard's state belongs to
the calling thread, every test guards its own thread only inside a `using` scope around synchronous code, or guards a
dedicated `Thread` it creates and joins (the lifetime mark is never put on a thread-pool thread), so no guard outlives
a test or reaches a thread another test uses. A worker thread synchronized with a `ManualResetEventSlim` checks that
the guard does not reach other threads; no timing is involved. That the code which blocks consults the guard is
verified at its own level (`GitClientTests.cs`). That the headless test harness really guards its UI thread, so every
headless window test also catches a git call made there, is verified by `HeadlessTestAppTests.cs`
(`HeadlessTestApp_UiThread_GitCallIsRefusedAndRecorded`: inside `HeadlessTestApp.Run` the thread is guarded, a
`GitClient` working-tree query is refused with `BlockingCallOnGuardedThreadException` before git starts, and the record
holds `git status` once; the test drains the record so the harness does not fail it). It covers
`Quality-UiThreadRegressionGuard`.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- On an unguarded thread a blocking call goes through and nothing is recorded.
- Inside a guard scope, and on a thread guarded for its lifetime, a blocking call is refused with
  `BlockingCallOnGuardedThreadException` (exactly that type) naming the call, and is recorded.
- The thread is guarded until the outermost of nested scopes is disposed; disposing a scope twice does not end an outer
  one; another thread is never guarded by this thread's scope.
- Taking the record returns every refusal in order and empties it.

#### Test Scenarios

**BlockingCallGuard_RefusesGuardedThread**: A call on an unguarded thread is let through and not recorded
(`BlockingCallGuard_ThrowIfGuarded_UnguardedThread_DoesNothing`); inside a scope it is refused with the guard's own
exception type naming `git status`, and recorded (`BlockingCallGuard_ThrowIfGuarded_GuardedScope_ThrowsAndRecords`); on
a dedicated thread guarded for its lifetime it is refused and recorded while the test's own thread stays unguarded
(`BlockingCallGuard_MarkCurrentThreadGuarded_DedicatedThread_StaysGuarded`). These cover
`AgentControl-BlockingCallGuard-RefusesGuardedThread`.

**BlockingCallGuard_ScopedPerThread**: Disposing the only scope ends the guard
(`BlockingCallGuard_GuardCurrentThread_ScopeDisposed_ThreadNoLongerGuarded`); with two nested scopes the thread stays
guarded after the inner one is disposed (twice) and is free after the outer one
(`BlockingCallGuard_GuardCurrentThread_NestedScopes_GuardUntilOutermostDisposed`); a worker thread checking itself while
the test's thread is guarded is neither guarded nor refused
(`BlockingCallGuard_GuardCurrentThread_OtherThread_NotGuarded`); and the lifetime mark guards with no scope open
(`BlockingCallGuard_MarkCurrentThreadGuarded_DedicatedThread_StaysGuarded`). These cover
`AgentControl-BlockingCallGuard-ScopedPerThread`.

**BlockingCallGuard_RecordsViolations**: Two refusals are taken in order (`git status`, then `read pin file`) and a
second take is empty (`BlockingCallGuard_TakeViolations_DrainsRecord`); a refusal is in the record even though the test
caught the exception (`BlockingCallGuard_ThrowIfGuarded_GuardedScope_ThrowsAndRecords`). These cover
`AgentControl-BlockingCallGuard-RecordsViolations`.
