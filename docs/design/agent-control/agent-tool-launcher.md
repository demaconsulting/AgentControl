## AgentToolLauncher

![AgentToolLauncher Structure](AgentToolLauncherView.svg)

### Overview

The `AgentToolLauncher` subsystem spans `ShellDetector.cs` (chooses the terminal/shell to
launch through) and `AgentToolLauncher.cs` (builds the process-start info to run the agent
tool inside that shell); the `ShellKind.cs` enum (and its companion `DetectedShell` record)
is folded into this subsystem-level description as it has no dedicated test file. It provides
the observable behavior of launching an agentic CLI tool in a repo's working directory. The
subsystem contains two units: `ShellDetector` and `AgentToolLauncher`.

> **Naming note**: this subsystem and one of its units share the name "AgentToolLauncher".
> Per this repo's software-item naming-collision convention, both retain the plain name at
> their own level; the folder/file path disambiguates them (this subsystem-level document is
> `docs/design/agent-control/agent-tool-launcher.md`, while the unit-level document is
> `docs/design/agent-control/agent-tool-launcher/agent-tool-launcher.md`). The SysML2 part
> definition backing this subsystem is likewise named `AgentToolLauncherSubsystem` to avoid a
> model-level name clash with the `AgentToolLauncher` unit part.

### Interfaces

**ShellDetector.Detect**: Chooses the appropriate shell for the current platform.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: On Windows, prefers PowerShell Core (`pwsh`) when available on `PATH` or at its
  well-known install location, falls back to Windows PowerShell, then to `cmd.exe`
  (`AgentControl-ShellDetector-DetectWindows`). On non-Windows platforms, uses the shell named
  by the `SHELL` environment variable, falling back to a default POSIX shell
  (`AgentControl-ShellDetector-DetectPosix`).
- *Constraints*: Always returns a `DetectedShell`; never returns no result, since a shell of
  some kind is always available to fall back to.

**AgentToolLauncher.BuildProcessStartInfo**: Builds process-start information for the
configured agent tool inside a detected shell.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Shapes shell-specific arguments so the resulting terminal window stays open (or
  exits cleanly, for PowerShell): no-exit arguments for PowerShell Core/Windows PowerShell,
  keep-open arguments for `cmd.exe`, and a `-c` argument for POSIX shells
  (`AgentControl-AgentToolLauncher-BuildCommand`).
- *Constraints*: Rejects a null shell, an empty command, an empty working directory, or an
  unrecognized `ShellKind` (`AgentControl-AgentToolLauncher-Validation`).

### Design

`ShellDetector` and `AgentToolLauncher` are both static classes with no persistent state; the
subsystem's only shared data type is the `DetectedShell` record (`ShellKind` plus the
resolved executable path) that `ShellDetector.Detect` returns and `AgentToolLauncher.
BuildProcessStartInfo` consumes. Together they realize architecture.md's `AgentToolLauncher`
software-structure entry: detect installed shells (preferring the highest available
PowerShell/`pwsh`, falling back to Windows PowerShell 5, falling back to `cmd`; using the
default shell on macOS/Linux) and launch the configured agentic CLI tool in the repo's
working directory (`AgentControl-AgentToolLauncherSubsystem-Launch`).

`RepoCardViewModel`'s `LaunchCommand` is the sole caller: it first ensures the repo's agent
files are synced (via the `RepoSync` subsystem), then calls `ShellDetector.Detect` followed
by `AgentToolLauncher.BuildProcessStartInfo`, and starts the resulting `ProcessStartInfo` via
the .NET BCL `Process` class. The subsystem has no dependency on other tool subsystems beyond
`Settings` (the configured agent-tool command and shell preference are read by the caller,
not by this subsystem itself, keeping `ShellDetector`/`AgentToolLauncher` pure functions of
their explicit parameters).
