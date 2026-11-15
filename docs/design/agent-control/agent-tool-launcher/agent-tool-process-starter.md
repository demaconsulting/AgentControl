### AgentToolProcessStarter

![AgentToolLauncher Structure](AgentToolLauncherView.svg)

#### Purpose

`AgentToolProcessStarter` is the production implementation of the `IAgentToolProcessStarter`
interface, a supporting type of the `AgentToolLauncher` subsystem. It starts the operating-system
process that hosts a launched agent tool, from the `ProcessStartInfo` that the
`AgentToolLauncher` class built, and is the only place the launch flow reaches
`Process.Start`. Putting the spawn behind an interface lets the launch flow be tested with a
recording fake, so no test ever opens a shell on the developer's desktop.

#### Data Model

`AgentToolProcessStarter` holds no state; it is a sealed class implementing
`IAgentToolProcessStarter`, whose single method is `Start(ProcessStartInfo)`.

#### Key Methods

**Start**: Starts the process described by a `ProcessStartInfo`.

- *Parameters*: `ProcessStartInfo startInfo`.
- *Returns*: nothing; the caller does not own the process.
- *Postconditions*: The process is started through `AgentToolLauncher.Launch` (which logs the
  attempt and the result), and the `Process` handle it returns is disposed at once. Disposing a
  `Process` only releases the application's handle; it never stops the launched shell
  (`AgentControl-AgentToolProcessStarter-StartProcess`).

#### Error Handling

`Start` throws `ArgumentNullException` for null `startInfo` and `InvalidOperationException`
when the process cannot be started (`AgentToolLauncher.Launch` converts a `Win32Exception`, and
a `null` result of `Process.Start`, into `InvalidOperationException`).

#### Dependencies

- **AgentToolLauncher.Launch** — starts and logs the process.
- **.NET BCL** — `System.Diagnostics.Process` (disposed after the start).

#### Callers

- **RepoCardViewModel.Launch** — through the `IAgentToolProcessStarter` it receives (the
  application's `MainWindowViewModel` passes none, which selects this class).
