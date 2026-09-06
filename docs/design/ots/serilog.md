## Serilog

This document describes the integration and usage design for the `Serilog` OTS software item.

### Purpose

Serilog (via `Serilog.Extensions.Logging` and `Serilog.Sinks.File`) backs AgentControl's
`Logging` subsystem, writing the rolling diagnostic log file used to troubleshoot
process-launch and git-integration issues (`AgentControl-OTS-Serilog-WriteLogFile`). Unlike
the project's build/CI/quality tooling, Serilog ships as part of the AgentControl application
itself, so it also has a corresponding SysML2 part (`docs/sysml2/model/ots.sysml`).

### Features Used

- Rolling file sink (`Serilog.Sinks.File`) with a size/day-based rolling policy.
- `Microsoft.Extensions.Logging` integration (`Serilog.Extensions.Logging`), so the rest of the
  codebase depends only on the portable `ILogger`/`ILoggerFactory` abstractions, never a
  concrete Serilog type.

### Integration Pattern

Serilog is consumed as a set of NuGet package references in the main project.
`LoggingSetup.Initialize` configures the global Serilog logger and wraps it in an
`ILoggerFactory` exposed via `AppLogging.Factory`, called once at application startup by
`Program.Main` before any other subsystem runs. `LoggingSetupTests` (see
`docs/design/agent-control/logging.md`'s test-evidence note) directly exercises the diagnostic
log file's location and content by writing a log entry through the assembly-wide
`TestLoggingFixture`-initialized pipeline and asserting it appears in the resulting rolling log
file; multi-day rolling and the 14-file retention limit remain Serilog's own tested behavior
and are not independently re-verified here. Any other passing test in the assembly (e.g.
`GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue`) remains additional indirect evidence that
Serilog initialized without throwing.
