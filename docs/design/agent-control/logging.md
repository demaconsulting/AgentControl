## Logging

![Logging Structure](LoggingView.svg)

### Overview

The `Logging` subsystem spans `AppLogging.cs` (a process-wide `ILoggerFactory` holder) and
`LoggingSetup.cs` (configures Serilog to write a rolling diagnostic log file at startup). This
subsystem has a single dedicated unit test file,
`test/DemaConsulting.AgentControl.Tests/Logging/LoggingSetupTests.cs`, and is otherwise
documented at subsystem level only, with no separate unit files — both classes are described
here.

> **Test-evidence basis**: `LoggingSetupTests` directly proves both the documented
> `InvalidOperationException` error-handling contract (invalid log-directory path) and that a
> log entry written through the pipeline is actually persisted to the documented `logs`
> subfolder location and content. It reuses the already-initialized
> `test/DemaConsulting.AgentControl.Tests/TestLoggingFixture.cs` assembly fixture rather than
> calling `LoggingSetup.Initialize` a second time with a valid directory, because doing so
> would redirect the process-wide static `Serilog.Log.Logger` away from the fixture's location
> for the remainder of the test run. Multi-day rolling and the 14-file retention limit are not
> independently re-verified here - they are Serilog's own tested behavior, only configured (not
> reimplemented) by this subsystem; see `docs/design/ots/serilog.md` for that OTS dependency's
> own note. Every other passing test in the assembly remains additional *indirect* evidence
> that `LoggingSetup.Initialize` did not throw, since `TestLoggingFixture` runs it once before
> any test executes; see `docs/reqstream/agent-control/logging.yaml`'s matching test-evidence
> note.

### Interfaces

**LoggingSetup.Initialize**: Configures the Serilog-backed logging pipeline.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Called once from `Program.Main`, before the Avalonia app builder runs, so
  startup-time failures are also captured on disk. Resolves the log directory from the same
  configuration-directory override (or default) used by `SettingsStore`, creates a
  `logs\` subfolder, and configures Serilog with daily rolling files and a 14-file retention
  limit (no hand-rolled cleanup code) (`AgentControl-Logging-DiagnosticFile`,
  `AgentControl-OTS-Serilog-WriteLogFile`). Also installs process-wide safety-net handlers for
  `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` so exceptions
  that would otherwise crash the process or silently vanish are always recorded before the
  process potentially terminates.
- *Constraints*: Throws `InvalidOperationException` when the `logs` directory cannot be
  created (e.g. permissions, invalid path). Not safe to call more than once concurrently; in
  practice it is called exactly once, synchronously, from `Program.Main`.

**AppLogging.Factory**: Process-wide access point for the shared `ILoggerFactory`.

- *Type*: In-process .NET static property.
- *Role*: Provider.
- *Contract*: Defaults to `NullLoggerFactory` (a safe no-op) so unit tests and any code path
  that runs before `LoggingSetup.Initialize` never null-reference; `LoggingSetup.Initialize`
  overwrites it with the real Serilog-backed factory during application startup. Subsystem
  classes (e.g. `GitClient`, `AgentToolLauncher`) that were not given an explicit logger fall
  back to this factory, keeping them coupled only to the portable
  `Microsoft.Extensions.Logging` abstraction, never to Serilog directly.
- *Constraints*: Set exactly once, early in `Program.Main`, before any subsystem is
  constructed; not safe to mutate concurrently with reads that expect a stable factory
  instance for a long-lived logger.

### Design

`LoggingSetup.Initialize` is the subsystem's sole entry point, realizing architecture.md's
"Diagnostic file logging via Serilog" decision: message boxes remain the primary in-the-moment
error surface for users, but a rolling diagnostic log under `<config-dir>\logs\` additionally
captures process-launch and git-integration detail (exit codes, timing, stderr, Win32 error
codes) that a message box alone cannot show. This decision superseded the original "no
application logging" scope exclusion after real-world flaky-process-launch troubleshooting
proved a message box alone was insufficient to root-cause intermittent failures.

`AppLogging` exists purely so production call sites with no convenient path to thread a
logger through from `Program.Main` (e.g. view models constructed deep inside the Avalonia UI
tree) can obtain a working `ILogger<T>` without hard-coding Serilog anywhere outside this
subsystem. Neither class holds any other state; `LoggingSetup.Initialize` is idempotent in
the sense that it is only ever invoked once per process, and its Serilog pipeline is flushed
via `Log.CloseAndFlush()` in `Program.Main`'s `finally` block regardless of how the process
exits.
