### AgentToolLauncher

#### Verification Approach

`AgentToolLauncher` is verified with unit tests defined in `AgentToolLauncherTests.cs`. Every
test builds process-start information from in-memory `ShellKind`/command/working-directory
arguments and inspects the resulting `ProcessStartInfo`; no process is actually started.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Each supported shell kind produces the correct argument form for keeping the terminal open
  (or exiting cleanly, for PowerShell).
- A null shell, empty command, empty working directory, or unrecognized shell kind is
  rejected clearly rather than producing invalid process-start information.

#### Test Scenarios

**AgentToolLauncher_BuildCommand_ProducesCorrectArgumentsPerShellKind**: Building
process-start information produces no-exit command arguments for both PowerShell Core and
Windows PowerShell, keep-open arguments for the Command shell, and a `-c` argument for POSIX
shells. This scenario is tested by
`AgentToolLauncher_BuildProcessStartInfo_PowerShellCore_ProducesNoExitCommandArguments`,
`AgentToolLauncher_BuildProcessStartInfo_WindowsPowerShell_ProducesNoExitCommandArguments`,
`AgentToolLauncher_BuildProcessStartInfo_Cmd_ProducesKeepOpenArguments`, and
`AgentToolLauncher_BuildProcessStartInfo_Posix_ProducesDashCArgument`, covering
`AgentControl-AgentToolLauncher-BuildCommand`.

**AgentToolLauncher_Validation_RejectsInvalidInputs**: Building process-start information
rejects a null shell, an empty command, an empty working directory, and an unrecognized shell
kind, each with a clear exception type. This scenario is tested by
`AgentToolLauncher_BuildProcessStartInfo_NullShell_ThrowsArgumentNullException`,
`AgentToolLauncher_BuildProcessStartInfo_EmptyCommand_ThrowsArgumentException`,
`AgentToolLauncher_BuildProcessStartInfo_EmptyWorkingDirectory_ThrowsArgumentException`, and
`AgentToolLauncher_BuildProcessStartInfo_UnrecognizedShellKind_ThrowsArgumentOutOfRangeException`,
covering `AgentControl-AgentToolLauncher-Validation`.
