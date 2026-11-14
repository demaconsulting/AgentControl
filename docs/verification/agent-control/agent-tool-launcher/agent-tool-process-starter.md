### AgentToolProcessStarter

#### Verification Approach

`AgentToolProcessStarter` is verified with unit tests defined in
`AgentToolProcessStarterTests.cs`. The two failure paths (null start information, an executable
that does not exist) start nothing. The positive path starts one tiny, windowless operating-system
command (`cmd.exe /c mkdir` on Windows, `sh -c` elsewhere) whose only effect is to create a marker
folder in a temporary working directory; the test waits, with a bounded timeout, for the folder to
appear. No shell window is opened and nothing is left running.

#### Test Environment

Standard test environment. The positive scenario needs `cmd.exe` (Windows) or `sh` (other
platforms), which every supported host has; it does not use the user's desktop.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Null start information is rejected, and a process that cannot be started is reported as an
  `InvalidOperationException`.
- A process that can be started is started.

#### Test Scenarios

**AgentToolProcessStarter_Start_RejectsNullAndReportsProcessThatCannotStart**: Starting with null
start information throws `ArgumentNullException`, and starting an executable that does not exist
throws `InvalidOperationException`. This scenario is tested by
`AgentToolProcessStarter_Start_NullStartInfo_ThrowsArgumentNullException` and
`AgentToolProcessStarter_Start_MissingExecutable_ThrowsInvalidOperationException`, covering
`AgentControl-AgentToolProcessStarter-StartProcess`.

**AgentToolProcessStarter_Start_StartsTheDescribedProcess**: Starting a windowless command that
creates a marker folder in its working directory results in that folder existing shortly after,
proving the described process really ran; the starter does not wait for the process or keep a
handle to it. This scenario is tested by
`AgentToolProcessStarter_Start_ExistingExecutable_StartsProcess`, covering
`AgentControl-AgentToolProcessStarter-StartProcess`.
