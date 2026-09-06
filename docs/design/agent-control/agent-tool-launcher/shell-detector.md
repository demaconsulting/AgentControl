### ShellDetector

![AgentToolLauncher Structure](AgentToolLauncherView.svg)

#### Purpose

`ShellDetector` detects the best available shell/terminal to launch the configured agentic CLI
tool in, preferring the highest installed PowerShell. Per architecture.md's `AgentToolLauncher`
subsystem description, detection order on Windows is: highest installed PowerShell (`pwsh` on
`PATH` or well-known install locations) > Windows PowerShell 5.x > `cmd.exe`. On macOS/Linux,
the user's default shell (`$SHELL`, falling back to `/bin/sh`) is used directly since
PowerShell is not assumed to be installed there.

#### Data Model

**WellKnownPwshPaths**, **WellKnownWindowsPowerShellPaths**: `static readonly string[]` —
fixed, well-known Windows install locations for PowerShell 7+ and Windows PowerShell 5.x,
consulted only after `PATH`-based resolution fails.

**\_resolveOnPath**, **\_fileExists**, **\_getShellEnvironmentVariable**, **\_isWindows**:
constructor-injected delegates/flags for every piece of environment probing (`PATH`
resolution, file existence, environment variables, and current OS), so tests can exercise
every detection branch deterministically without depending on what is actually installed on
the test machine.

#### Key Methods

**Constructor**: Accepts optional overrides for every injected delegate, defaulting to real
`PATH`-scanning, `File.Exists`, the `$SHELL` environment variable, and
`OperatingSystem.IsWindows()` respectively.

**Detect**: Detects the best available shell.

- *Parameters*: None.
- *Returns*: `DetectedShell` — the detected `ShellKind` and executable path to launch.
- *Postconditions*: On Windows, follows the PowerShell 7+ → PowerShell 5.x → `cmd.exe`
  fallback chain (`AgentControl-ShellDetector-DetectWindows`); elsewhere, returns the `$SHELL`
  environment variable's value or `/bin/sh` if unset
  (`AgentControl-ShellDetector-DetectPosix`).

#### Error Handling

`Detect` never throws — every branch has a guaranteed final fallback (`cmd.exe` on Windows,
`/bin/sh` on POSIX), so shell detection always produces an answer. The default `PATH`-scanning
implementation silently skips malformed `PATH` entries (e.g. containing invalid path
characters) rather than treating them as a detection failure.

#### Dependencies

- **ShellKind**, **DetectedShell** (subsystem-level types, folded into
  `docs/design/agent-control/agent-tool-launcher.md`) — the detection result type.
- **.NET BCL** — `Environment.GetEnvironmentVariable`, `File.Exists`, `OperatingSystem.IsWindows`.

#### Callers

- **RepoCardViewModel.Launch** — constructs a `ShellDetector` and calls `Detect` immediately
  before building the process start info for a launch.
