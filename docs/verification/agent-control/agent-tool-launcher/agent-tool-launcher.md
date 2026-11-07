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
- A custom command beginning with a quoted executable path is invoked (not echoed) under
  PowerShell, and is left unchanged for every other shell kind and command form.
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

**AgentToolLauncher_QuotedPathInvocation_AddsCallOperatorOnlyForQuotedPowerShellCommands**:
For PowerShell Core and Windows PowerShell, a command that begins (after leading whitespace)
with a double or single quote - with or without trailing arguments - is passed to `-Command`
prefixed with `&` so the quoted path is executed rather than echoed; a command that already
begins with `&` or `.`, or that does not begin with a quote, is passed through verbatim; and
the Command shell and POSIX shells never receive the prefix. This scenario is tested by
`AgentToolLauncher_BuildProcessStartInfo_PowerShellCoreQuotedPathWithArguments_PrefixesCallOperator`,
`AgentToolLauncher_BuildProcessStartInfo_WindowsPowerShellQuotedPathWithArguments_PrefixesCallOperator`,
`AgentToolLauncher_BuildProcessStartInfo_PowerShellQuotedPathOnly_PrefixesCallOperator`,
`AgentToolLauncher_BuildProcessStartInfo_PowerShellSingleQuotedPath_PrefixesCallOperator`,
`AgentToolLauncher_BuildProcessStartInfo_PowerShellLeadingWhitespaceBeforeQuote_PrefixesCallOperator`,
`AgentToolLauncher_BuildProcessStartInfo_PowerShellCommandAlreadyHasOperator_IsNotDoublePrefixed`,
`AgentToolLauncher_BuildProcessStartInfo_PowerShellUnquotedCommand_IsUnchanged`,
`AgentToolLauncher_BuildProcessStartInfo_CmdQuotedPath_IsUnchanged`, and
`AgentToolLauncher_BuildProcessStartInfo_PosixQuotedPath_IsUnchanged`, covering
`AgentControl-AgentToolLauncher-QuotedPathInvocation`. The end-to-end effect (the configured
quoted stub path really runs) is additionally verified by the FlaUI Launch tests at the
subsystem level.

**AgentToolLauncher_Validation_RejectsInvalidInputs**: Building process-start information
rejects a null shell, an empty command, an empty working directory, and an unrecognized shell
kind, each with a clear exception type. This scenario is tested by
`AgentToolLauncher_BuildProcessStartInfo_NullShell_ThrowsArgumentNullException`,
`AgentToolLauncher_BuildProcessStartInfo_EmptyCommand_ThrowsArgumentException`,
`AgentToolLauncher_BuildProcessStartInfo_EmptyWorkingDirectory_ThrowsArgumentException`, and
`AgentToolLauncher_BuildProcessStartInfo_UnrecognizedShellKind_ThrowsArgumentOutOfRangeException`,
covering `AgentControl-AgentToolLauncher-Validation`.
