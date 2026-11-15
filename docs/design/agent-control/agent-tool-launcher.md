## AgentToolLauncher

![AgentToolLauncher Structure](AgentToolLauncherView.svg)

### Overview

The `AgentToolLauncher` subsystem spans `ShellDetector.cs` (chooses the terminal/shell to
launch through), `AgentToolLauncher.cs` (builds the process-start info to run the agent
tool inside that shell, for a shell that stays open after the tool or ends with it) and
`AgentToolProcessStarter.cs` (starts the process); the `ShellKind.cs` enum (and its companion
`DetectedShell` record) and the `IAgentToolProcessStarter.cs` interface (which
`AgentToolProcessStarter` implements, and which lets the launch flow be tested without starting
a shell) are folded into this subsystem-level description as supporting types with no
dedicated test file of their own. It provides the observable behavior of launching an agentic
CLI tool in a repo's working directory. The subsystem contains three units: `ShellDetector`,
`AgentToolLauncher` and `AgentToolProcessStarter`.

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
- *Contract*: Shapes shell-specific arguments. With `keepShellOpen` true (the default) the
  resulting terminal window stays open after the tool exits: no-exit arguments for PowerShell
  Core/Windows PowerShell and keep-open arguments for `cmd.exe`
  (`AgentControl-AgentToolLauncher-BuildCommand`). With `keepShellOpen` false the shell ends
  together with the tool: PowerShell without the no-exit argument and `cmd.exe` with `/C`
  (`AgentControl-AgentToolLauncher-ShellEndsWithTool`). POSIX shells use a `-c` argument either
  way.
- *Constraints*: Rejects a null shell, an empty command, an empty working directory, or an
  unrecognized `ShellKind` (`AgentControl-AgentToolLauncher-Validation`).

**AgentToolProcessStarter.Start** (through the `IAgentToolProcessStarter` interface): Starts the
process described by a `ProcessStartInfo`.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Starts the process and releases the application's handle to it without stopping
  it; the caller does not own the process (`AgentControl-AgentToolProcessStarter-StartProcess`).
- *Constraints*: Rejects null start information; reports a process that cannot be started as an
  `InvalidOperationException`.

### Design

`ShellDetector` is a sealed class whose environment lookups are injected through its
constructor, `AgentToolLauncher` is a static class, and `AgentToolProcessStarter` is a sealed
class; none keeps persistent state. The subsystem's data types are the `DetectedShell` record
(`ShellKind` plus the resolved executable path) that `ShellDetector.Detect` returns and
`AgentToolLauncher.BuildProcessStartInfo` consumes, and the `IAgentToolProcessStarter`
interface. Together they realize architecture.md's `AgentToolLauncher`
software-structure entry: detect installed shells (preferring the highest available
PowerShell/`pwsh`, falling back to Windows PowerShell 5, falling back to `cmd`; using the
default shell on macOS/Linux) and launch the configured agentic CLI tool in the repo's
working directory (`AgentControl-AgentToolLauncherSubsystem-Launch`).

The user's "keep the shell open after the agent tool exits" setting only changes how the shell
is invoked: PowerShell gets `-NoExit` and `cmd.exe` gets `/K` when the shell is kept open;
PowerShell omits `-NoExit` and `cmd.exe` gets `/C` when it is not, so the shell ends together
with the tool (`AgentControl-AgentToolLauncherSubsystem-KeepShellOpen`). On non-Windows
platforms no terminal window is opened at all: the shell is run with `-c` and the setting has
no effect. That is an existing limitation, unchanged; terminal-emulator support for macOS and
Linux is deferred.

`RepoCardViewModel`'s `LaunchCommand` is the sole caller: it first verifies the repo's agent
files against the pinned package (via the `RepoSync` and `AgentPackageManagement` subsystems),
then calls `ShellDetector.Detect` followed by `AgentToolLauncher.BuildProcessStartInfo`, and
hands the resulting `ProcessStartInfo` to an `IAgentToolProcessStarter`. The application uses
`AgentToolProcessStarter`, which starts the process through the .NET BCL `Process` class; the
card receives both the starter and the `ShellDetector` through optional constructor parameters
(default: the real ones), so tests substitute a recording starter and a deterministic detector
and never open a shell. The subsystem has no dependency on other tool subsystems beyond
`Settings` (the configured agent-tool command, shell preference and keep-shell-open choice
are read by the caller, not by this subsystem itself, and passed in as explicit parameters,
keeping `ShellDetector`/`AgentToolLauncher` functions of their explicit parameters).
