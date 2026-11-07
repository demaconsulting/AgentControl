### AgentToolLauncher

![AgentToolLauncher Structure](AgentToolLauncherView.svg)

> **Naming note**: this unit's class name, `AgentToolLauncher`, collides with its parent
> subsystem's name. The SysML2 model disambiguates the subsystem part as
> `AgentToolLauncherSubsystem`; this document (like the reqstream unit ID prefix
> `AgentControl-AgentToolLauncher-*`) refers to the class alone.

#### Purpose

`AgentToolLauncher` builds the `ProcessStartInfo` needed to launch an arbitrary agent tool
command string inside a detected (or user-overridden) shell, and isolates the actual process
spawn so it can be substituted in tests. Splitting `BuildProcessStartInfo` from `Launch` lets
tests assert on the constructed `ProcessStartInfo` (file name, arguments, working directory)
without ever spawning a real terminal process, per architecture.md's testability requirements.

#### Data Model

`AgentToolLauncher` holds no instance state; it is a static class.

**LoggerCategoryName**: `const string` — a fixed string logger category (rather than the
generic `ILogger<AgentToolLauncher>` pattern used by non-static classes like `GitClient`),
since C# does not permit a static class to be used as a generic type argument.

#### Key Methods

**BuildProcessStartInfo**: Builds a `ProcessStartInfo` that launches a command inside a
detected shell.

- *Parameters*: `DetectedShell shell`, `string command`, `string workingDirectory`.
- *Returns*: `ProcessStartInfo`.
- *Postconditions*: The shell is started with `-NoExit`/`-NoLogo` (PowerShell), `/K` (cmd), or
  `-c` (POSIX) so the window stays open for the user to interact with the launched tool
  (`AgentControl-AgentToolLauncher-BuildCommand`).
- *Command adaptation (PowerShell only)*: when the shell kind is `PowerShellCore` or
  `WindowsPowerShell` and the command, after trimming leading whitespace, begins with a `"` or
  `'` character, the value passed to `-Command` is `&` followed by the trimmed command. This
  is required because PowerShell treats a leading quoted string as an expression and only
  prints it; the call operator makes it invoke the quoted executable path (for example
  `& "C:\Program Files\Tool\tool.exe" --flag`). A command already beginning with `&` (call
  operator) or `.` (dot-source operator), or beginning with anything other than a quote, is
  passed through verbatim, so it is never double-prefixed. cmd (`/K`) and POSIX (`-c`) commands
  are never modified because those shells execute a quoted path directly
  (`AgentControl-AgentToolLauncher-QuotedPathInvocation`). The adaptation is implemented by
  the private helper `ApplyPowerShellCallOperator`.

**Launch**: Starts a process from a previously built `ProcessStartInfo`.

- *Parameters*: `ProcessStartInfo startInfo`, `ILogger? logger = null`.
- *Returns*: `Process` — the caller owns its lifetime.
- *Postconditions*: Logs the launch at `Debug` before starting and `Information` after a
  successful start, including the elapsed time and PID. Isolated from
  `BuildProcessStartInfo` specifically so unit tests can verify the constructed
  `ProcessStartInfo` without invoking this method, which spawns a real OS process.

#### Error Handling

`BuildProcessStartInfo` throws `ArgumentNullException` for a null `shell`,
`ArgumentException` for a null/empty/whitespace `command` or `workingDirectory`, and
`ArgumentOutOfRangeException` for an unrecognized `ShellKind` value. `Launch` throws
`ArgumentNullException` for a null `startInfo` and `InvalidOperationException` when the
process fails to start (either `Process.Start` returning `null`, or a caught `Win32Exception`,
whose `NativeErrorCode` is logged at `Error` level — captured for the same class of
intermittent-launch-failure diagnostics as `GitClient.RunGit`).

#### Dependencies

- **ShellKind**, **DetectedShell** — the shell descriptor produced by `ShellDetector`.
- **AppLogging** (`Logging` subsystem) — supplies the fallback `ILoggerFactory` when no
  `logger` is passed.
- **.NET BCL** — `System.Diagnostics.Process`/`ProcessStartInfo`, `System.Diagnostics.Stopwatch`.

#### Callers

- **RepoCardViewModel.Launch** — calls `EnsureAgentFilesSyncedBeforeLaunch` first purely for
  its best-effort agent-package sync side effect (never gating on its outcome), then always
  calls `ShellDetector.Detect` followed by `BuildProcessStartInfo`/`Launch`.
