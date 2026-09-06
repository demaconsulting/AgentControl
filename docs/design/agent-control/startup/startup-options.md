### StartupOptions

![Startup Structure](StartupView.svg)

#### Purpose

`StartupOptions` is an immutable representation of the fixed set of AgentControl command-line
startup arguments. AgentControl is a GUI application, so this parser intentionally supports
only the small set of flags needed for test isolation and troubleshooting, per architecture.md's
"Testability via config-dir override and stub executables" decision: a configuration directory
override so FlaUI-driven integration tests can point the app at an isolated settings folder
instead of the real `%APPDATA%\AgentControl\`, and git/agent-tool command overrides so those
same tests can substitute an arg-logging stub executable for git and the agentic CLI tool.

#### Data Model

**ConfigDirectory**: `string?` — configuration directory override, or `null` to use the
default `%APPDATA%\AgentControl\`.

**GitExecutableOverride**: `string?` — git executable path override supplied for test
isolation, or `null`. Overrides only the startup-time default; the per-user `Settings`
subsystem's stored git executable path (if any) takes precedence once settings are loaded.

**AgentToolCommandOverride**: `string?` — agent tool command override supplied for test
isolation, or `null`. Same precedence rule as `GitExecutableOverride`.

Instances are created exclusively via `Parse`; the private constructor performs no I/O.
Immutable and thread-safe once constructed.

#### Key Methods

**Parse**: Parses AgentControl's startup command-line arguments.

- *Parameters*: `string[] args`.
- *Returns*: `StartupOptions`.
- *Postconditions*: Walks the argument list, recognizing `--config-dir`, `--git-path`, and
  `--agent-tool-command`, each consuming the value that follows it
  (`AgentControl-StartupOptions-Parse`).

#### Error Handling

`Parse` throws `ArgumentNullException` for a null `args`, and `ArgumentException` when an
unsupported argument is encountered or when a recognized option is the last argument (missing
its required value).

#### Dependencies

- **.NET BCL** — none beyond `System.Array`/`string` handling; no filesystem or process access.

#### Callers

- **Program.Main** — calls `Parse` on the raw `args` passed to `Main`, before any other startup
  step, and threads the resulting `StartupOptions` through to `SettingsStore.Load`/`GitClient`/
  `AgentToolLauncher` for configuration-directory and command overrides.
- **RepoCardViewModel** — consults `GitExecutableOverride`/`AgentToolCommandOverride` as a
  first-run fallback when no per-user setting is configured.
