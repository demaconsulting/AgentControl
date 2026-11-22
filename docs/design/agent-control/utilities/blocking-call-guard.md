### BlockingCallGuard

![Utilities Structure](UtilitiesView.svg)

#### Purpose

`BlockingCallGuard` lets the automated tests catch a blocking call - a git process, a disk read or write - made on a
thread that must never wait for one, such as the window's UI thread. Such a call still works; it only freezes the
window for a while, so no ordinary test notices it. The code that is about to make a blocking call asks the guard
first, and on a thread a test has marked as guarded the call is refused with an exception of the guard's own type and
recorded. Nothing is guarded unless a test asks for it, so the shipped application is unaffected. Its behavior is
specified by the unit requirements `AgentControl-BlockingCallGuard-RefusesGuardedThread`, `-ScopedPerThread` and
`-RecordsViolations`, which decompose `AgentControl-Utilities-BlockingCallGuard`. The
`BlockingCallOnGuardedThreadException` class in the same file is part of this unit.

#### Data Model

`BlockingCallGuard` is a static class whose state is per thread (`[ThreadStatic]`), so tests that run in parallel
on other threads never see each other's guard or refusals:

- **t_guardDepth** (`int`): how many guard scopes are open on the thread.
- **t_guardedForLifetime** (`bool`): whether the thread was guarded for its lifetime.
- **t_violations** (`List<string>?`): the descriptions of the calls refused on the thread and not yet taken.

**IsCurrentThreadGuarded** (`bool`, derived): `t_guardedForLifetime || t_guardDepth > 0`.

**BlockingCallOnGuardedThreadException** (sealed, public, derives directly from `Exception`): deliberately not an
`InvalidOperationException`, the type the git and file code catches to report ordinary failures ("git could not
tell"), so a refused call is never mistaken for one.

#### Key Methods

**GuardCurrentThread()**: Opens a guard scope on the calling thread and returns it as an `IDisposable`. Scopes nest;
the thread stays guarded until the outermost one is disposed. A scope disposed on another thread, or a second time,
does nothing (`AgentControl-BlockingCallGuard-ScopedPerThread`). Used by plain view-model tests around the synchronous
part of a gesture.

**MarkCurrentThreadGuarded()**: Guards the calling thread for the rest of its lifetime, for a thread a test harness
owns and keeps (the single UI thread of the headless test session). Never to be called on a thread-pool thread.

**ThrowIfGuarded(operation)**: Called by the code that makes a blocking call, before it does anything. Does nothing
on an unguarded thread. On a guarded thread it first records `operation` (a catch-all in the code under test cannot
hide the refusal) and then throws `BlockingCallOnGuardedThreadException` naming it
(`AgentControl-BlockingCallGuard-RefusesGuardedThread`). Throws `ArgumentNullException` for a null `operation`.

**TakeViolations()**: Returns the calls refused on the calling thread since the last call, in order, and empties the
record (`AgentControl-BlockingCallGuard-RecordsViolations`). The record is per thread, so a harness drains it on the
thread it guards, after the code under test ran there.

#### Error Handling

The only errors are the refusal itself (`BlockingCallOnGuardedThreadException`, which callers must not catch as an
ordinary failure) and `ArgumentNullException` for a null operation description. Disposing a scope on the wrong thread
or twice is ignored rather than thrown, because a `Dispose` must not throw.

Thread safety: every member acts on the calling thread's own state only, so it is safe to call from any thread
without locking.

#### Dependencies

- **.NET BCL** only (`ThreadStaticAttribute`, `Environment.CurrentManagedThreadId`).

#### Callers

The guard points - the code that asks before it blocks:

- **GitClient** (`GitIntegration`) - before every git process it starts, and before the `.git/HEAD` read of
  `GetCurrentBranch`.
- **SettingsStore** (`Settings`) - before every `Save`.
- **RepoPinStore** (`RepoConfig`) - before every pin read and write.
- **PackageContentIdentity** (`AgentPackageManagement`) - before every package or installed-files hash.
- **PackageContentExtractor** (`RepoSync`) - before every extraction.
- **GitRemoteClient** and **GitCredentialClient** (`GitIntegration`) - before every git process they start (both
  start it on a worker, so the guard catches a start that slips back onto the caller's thread).
- **ReleaseNotesViewer** (`RepoSync`) - before the shell start of a release-notes link (made on a worker).

The tests that enable it:

- **BlockingCallGuardTests**, **UtilitiesSubsystemTests** and **GitClientTests** - scopes around synchronous code and
  dedicated threads.
- **HeadlessTestApp** (the headless window tests' session) - marks the session's UI thread guarded for the life of the
  test process and, after every `Run`/`RunAsync`, drains the thread's record on that thread and fails the test when it
  holds anything, so a git command made on the UI thread by any headless test fails it.
- **RepoCardViewModelTests** - scopes around the synchronous part of a Refresh, a launch, an upgrade and the AGENTS.md
  answer, and headless runs of an upgrade check, an upgrade and a launch with a restore.
- The unit and subsystem tests of the guard points above, and the tests of the package source decorator, the source
  editor and the release-notes link - scopes around a call that must start its blocking work on another thread.

`SettingsStore.Load` is deliberately not a guard point: it runs once, before the window exists.
