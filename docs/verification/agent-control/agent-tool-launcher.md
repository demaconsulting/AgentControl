## AgentToolLauncher

### Verification Approach

The `AgentToolLauncher` subsystem is verified indirectly through its constituent units' test
suites (`ShellDetectorTests.cs` and `AgentToolLauncherTests.cs`), each documented in its own
unit-level verification design, plus one FlaUI end-to-end test at the system level that
exercises the full launch path against a real Windows shell. The `ShellKind` enum has no
dedicated test file and is exercised only as a value returned by `ShellDetector` and consumed
by `AgentToolLauncher`.

Per the naming-collision convention shared with `docs/reqstream/agent-control/agent-tool-
launcher.yaml`, this subsystem and one of its units share the plain name "AgentToolLauncher";
the folder/file path disambiguates them (this file lives at `agent-control/agent-tool-
launcher.md` for the subsystem, versus `agent-control/agent-tool-launcher/agent-tool-
launcher.md` for the unit).

### Test Environment

The FlaUI end-to-end scenario requires the Windows-only FlaUI test environment described in
the `AgentControl` system-level verification design (Windows CI runner, application launched
against a real repo fixture). The unit tests below have no such requirement.

### Acceptance Criteria

- All unit tests for `ShellDetector` and `AgentToolLauncher` pass with zero failures.
- The FlaUI end-to-end launch scenario passes on the Windows CI runner.
- The correct shell and process-start arguments are produced for every supported platform and
  shell kind.

### Test Scenarios

**AgentToolLauncher_Launch_UnitTestsCoverDetectionAndBuildCommandPlusEndToEnd**: Shell
detection and process-start-info construction are verified by the `ShellDetector` and
`AgentToolLauncher` unit tests (see their respective unit verification designs); the full
launch path is additionally verified end-to-end by the FlaUI test
`windows@LaunchButton_Click_InvokesConfiguredAgentToolInRepoWorkingDirectory`, cited directly
at the subsystem level, covering `AgentControl-AgentToolLauncherSubsystem-Launch` (children:
`AgentControl-ShellDetector-DetectWindows`, `AgentControl-ShellDetector-DetectPosix`,
`AgentControl-AgentToolLauncher-BuildCommand`, `AgentControl-AgentToolLauncher-Validation`).
