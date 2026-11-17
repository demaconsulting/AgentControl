## AgentToolLauncher

### Verification Approach

The `AgentToolLauncher` subsystem is verified indirectly through its constituent units' test
suites (`ShellDetectorTests.cs`, `AgentToolLauncherTests.cs` and
`AgentToolProcessStarterTests.cs`), each documented in its own
unit-level verification design, plus the subsystem tests in `AgentToolLauncherSubsystemTests.cs`
that run detection, building and starting together through a recording fake starter, plus
FlaUI end-to-end tests at the system level that exercise the full launch path against a real
Windows shell. The `ShellKind` enum and the `IAgentToolProcessStarter` interface have no
dedicated test file and are exercised as values and a seam used by the units and the subsystem
tests.

Per the naming-collision convention shared with `docs/reqstream/agent-control/agent-tool-launcher.yaml`,
this subsystem and one of its units share the plain name "AgentToolLauncher";
the folder/file path disambiguates them (this file lives at `agent-control/agent-tool-launcher.md`
for the subsystem, versus `agent-control/agent-tool-launcher/agent-tool-launcher.md`
for the unit).

### Test Environment

The FlaUI end-to-end scenario requires the Windows-only FlaUI test environment described in
the `AgentControl` system-level verification design (Windows CI runner, application launched
against a real repo fixture). The unit and subsystem tests below have no such requirement:
they start no shell (the subsystem tests use a recording fake starter).

### Acceptance Criteria

- All unit tests for `ShellDetector`, `AgentToolLauncher` and `AgentToolProcessStarter`, and the
  subsystem tests, pass with zero failures.
- The FlaUI end-to-end launch scenario passes on the Windows CI runner.
- The correct shell and process-start arguments are produced for every supported platform and
  shell kind, with the shell kept open or not.

### Test Scenarios

**AgentToolLauncher_Launch_UnitTestsCoverDetectionAndBuildCommandPlusEndToEnd**: Shell
detection and process-start-info construction are verified by the `ShellDetector` and
`AgentToolLauncher` unit tests (see their respective unit verification designs); the full
launch path is additionally verified end-to-end by the FlaUI test
`windows@LaunchButton_Click_InvokesConfiguredAgentToolInRepoWorkingDirectory`, cited directly
at the subsystem level, covering `AgentControl-AgentToolLauncherSubsystem-Launch` (children:
`AgentControl-ShellDetector-DetectWindows`, `AgentControl-ShellDetector-DetectPosix`,
`AgentControl-AgentToolLauncher-BuildCommand`, `AgentControl-AgentToolLauncher-QuotedPathInvocation`,
`AgentControl-AgentToolLauncher-Validation`). The FlaUI test configures a custom command made
of the quoted arg-logger stub path plus a marker argument and asserts that the stub recorded
an invocation with exactly that marker (which git invocations never carry) in the repo
working directory, proving the quoted command was executed by the shell rather than echoed.

**AgentToolLauncher_KeepShellOpen_DetectBuildAndStartThroughFakeStarter**: Detection, building
and starting work together for the keep-shell-open choice: with a deterministic Windows shell
detector, PowerShell (`pwsh`, `powershell`) and the Command shell receive the no-exit / `/K`
arguments when the shell is kept open and the arguments that end the shell with the tool
(`-NoLogo -Command`, `/C`) otherwise, a POSIX shell is started the same way either way, and the
fake starter records exactly one request with the repo as working directory and starts no
process. This scenario is tested by
`AgentToolLauncherSubsystem_KeepShellOpen_PowerShellAndCmd_OnKeepsOpenOffEndsWithTool` and
`AgentToolLauncherSubsystem_KeepShellOpen_PosixShell_IsUnaffected`, covering
`AgentControl-AgentToolLauncherSubsystem-KeepShellOpen` (children:
`AgentControl-AgentToolLauncher-BuildCommand`, `AgentControl-AgentToolLauncher-ShellEndsWithTool`,
`AgentControl-AgentToolProcessStarter-StartProcess`).
