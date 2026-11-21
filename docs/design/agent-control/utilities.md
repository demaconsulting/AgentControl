## Utilities

![Utilities Structure](UtilitiesView.svg)

### Overview

The `Utilities` subsystem provides shared utility functions for the Agent Control. It
supplies reusable, independently testable helpers consumed by other subsystems. Its primary
responsibility is safe file-path manipulation, protecting callers from path-traversal
vulnerabilities when constructing paths from caller-supplied inputs. It also provides the
blocking-call guard the automated tests use to catch a git or disk call made on the window's UI
thread (`AgentControl-Utilities-BlockingCallGuard`, which supports `Quality-UiThreadRegressionGuard`).
The `Utilities` subsystem contains two units: `PathHelpers` and `BlockingCallGuard`.

### Interfaces

**PathHelpers.SafePathCombine**: Combines a base path and a relative path, rejecting any result
that escapes the base directory.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts `string basePath` and `string relativePath`. Returns the combined path
  produced by `Path.Combine(basePath, relativePath)` after verifying that the resolved result
  remains within `basePath`. Preserves the caller's relative/absolute style in the return value.
- *Constraints*: Throws `ArgumentNullException` for null inputs; throws `ArgumentException`
  when the combined path escapes the base directory; may propagate `NotSupportedException` or
  `PathTooLongException` from underlying BCL path operations.

**BlockingCallGuard**: Lets the tests mark a thread on which blocking git and disk calls are refused.

- *Type*: In-process .NET static methods: `GuardCurrentThread` (a disposable scope), `MarkCurrentThreadGuarded`,
  `IsCurrentThreadGuarded`, `ThrowIfGuarded(operation)` and `TakeViolations`.
- *Role*: Provider to two sides: the code that makes a blocking call asks `ThrowIfGuarded` first (in this phase
  `GitClient`, `GitIntegration`), and the tests guard a thread and read the record.
- *Contract*: On a guarded thread `ThrowIfGuarded` records the call and throws `BlockingCallOnGuardedThreadException`
  (not an `InvalidOperationException`); on any other thread it does nothing. The guard and the record are per thread
  (`AgentControl-BlockingCallGuard-RefusesGuardedThread`, `-ScopedPerThread`, `-RecordsViolations`).
- *Constraints*: Nothing is guarded unless a test asks for it, so the shipped application is unaffected; safe to call
  from any thread.

### Design

The `Utilities` subsystem contains the `PathHelpers` and `BlockingCallGuard` units, which do not use each other. Neither
has dependencies on other tool units or subsystems; they use only .NET BCL types (`Path`, `ArgumentNullException`,
`ThreadStaticAttribute`).

`BlockingCallGuard` keeps its state in thread-static fields: the depth of nested guard scopes, a lifetime flag and the
list of refused calls. Because the state belongs to the calling thread, tests that run in parallel never see each
other's guard, and the guard costs an unguarded thread two field reads. Its guard points live in the code that blocks
(`GitClient.RunGit` and the `.git/HEAD` read of `GitClient.GetCurrentBranch`), so a new caller of that code on the UI
thread is caught without any change to the guard.

`PathHelpers.SafePathCombine` is a pure utility method: it performs no file-system I/O, holds
no state, and throws immediately on invalid input. All calls to `SafePathCombine` in the
codebase originate from the `RepoConfig` subsystem (`RepoPinStore`, resolving the per-repo
`.agentcontrol.json` pin file path), the `RepoSync` subsystem (`PackageZipExtractor`,
resolving managed-folder and zip-entry destination paths, and `GitIgnoreEnsurer`, resolving
the repo-root `.gitignore` path), and the `LauncherUI` subsystem (`RepoCardViewModel`,
resolving the repo-root `AGENTS.md` path), each using it to keep a caller-supplied repo root
from being escaped by a malformed relative path.
