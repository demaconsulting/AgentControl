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

- **RepoCardViewModel.Launch** — calls `BuildProcessStartInfo` then `Launch` after
  `ShellDetector.Detect` and `EnsureAgentFilesSyncedBeforeLaunch` both succeed.
