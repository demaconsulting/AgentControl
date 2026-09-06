## Startup

![Startup Structure](StartupView.svg)

### Overview

The `Startup` subsystem spans `StartupOptions.cs`, which parses the fixed set of startup
arguments (configuration-directory override, git executable override, agent-tool command
override) used to support hermetic, isolated end-to-end testing. It provides the observable
startup-argument behavior. The subsystem contains one unit: `StartupOptions`.

### Interfaces

**StartupOptions.Parse**: Parses AgentControl's startup command-line arguments.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Accepts `--config-dir <path>`, `--git-path <path>`, and
  `--agent-tool-command <command>`, all optional; leaves any not supplied as `null` on the
  returned `StartupOptions` (`AgentControl-Startup-ParseArguments`).
- *Constraints*: Throws `ArgumentNullException` for a null `args` array; throws
  `ArgumentException` for an unsupported argument or a recognized argument missing its
  required value.

### Design

`StartupOptions` is an immutable, thread-safe-once-constructed class instantiated exclusively
via `Parse`; it performs no I/O itself. AgentControl is a GUI application, so this parser
intentionally supports only the small set of flags needed for test isolation and
troubleshooting, per architecture.md's "Testability via config-dir override and stub
executables" decision: `--config-dir` lets FlaUI-driven integration tests point the app at an
isolated settings folder instead of the real `%APPDATA%\AgentControl\`, and `--git-path`/
`--agent-tool-command` let those same tests substitute an arg-logging stub executable for git
and the agentic CLI tool, giving fully hermetic, repeatable end-to-end automation
(`AgentControl-System-TestIsolation`). Each override affects only the startup-time default —
the per-user `Settings` subsystem's stored git executable path or agent tool command, once
settings are loaded, takes precedence over these overrides where both apply.

`Program.Main` is the sole caller: it calls `StartupOptions.Parse(args)` before any other
startup work, so a parse failure (an unsupported argument, or an option missing its value) is
reported to stderr and causes a non-zero exit code without initializing logging or starting
the Avalonia UI (`AgentControl-Program-ErrorHandling`). On success, the parsed
`ConfigDirectory` is passed to `LoggingSetup.Initialize` and attached to `App.Options` for
`App.OnFrameworkInitializationCompleted` to pass to `SettingsStore.Load`.
