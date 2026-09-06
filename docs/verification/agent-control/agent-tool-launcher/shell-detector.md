### ShellDetector

#### Verification Approach

`ShellDetector` is verified with unit tests defined in `ShellDetectorTests.cs`. Platform and
filesystem dependencies (current OS, `PATH` entries, well-known install paths, and the
`SHELL` environment variable) are injected through fake delegate parameters rather than read
from the real operating system, so every branch of the detection logic can be exercised
deterministically on any host OS.

#### Test Environment

N/A - standard test environment; platform dependencies are faked via delegate injection (see
Verification Approach), so no specific host OS is required to exercise any branch.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The Windows fallback chain resolves in the documented preference order: PowerShell Core,
  then Windows PowerShell, then Command shell.
- The POSIX path honors the `SHELL` environment variable when set and falls back to a default
  shell otherwise.

#### Test Scenarios

**ShellDetector_DetectWindows_FollowsPwshWindowsPowerShellCmdFallbackChain**: On Windows,
detection returns PowerShell Core when it is on the path or at its well-known install
location; falls back to Windows PowerShell when only the legacy executable is found on the
path or at its well-known location; and falls back to the Command shell (with a default name)
when nothing is resolvable. This scenario is tested by
`ShellDetector_Detect_WindowsWithPwshOnPath_ReturnsPowerShellCore`,
`ShellDetector_Detect_WindowsWithPwshAtWellKnownPath_ReturnsPowerShellCore`,
`ShellDetector_Detect_WindowsWithOnlyLegacyPowerShellOnPath_ReturnsWindowsPowerShell`,
`ShellDetector_Detect_WindowsWithLegacyAtWellKnownPathOnly_ReturnsWindowsPowerShell`,
`ShellDetector_Detect_WindowsWithNoPowerShellAvailable_ReturnsCmd`, and
`ShellDetector_Detect_WindowsWithNothingResolvable_ReturnsCmdWithDefaultName`, covering
`AgentControl-ShellDetector-DetectWindows`.

**ShellDetector_DetectPosix_HonorsShellVariableOrDefaults**: On non-Windows platforms,
detection returns the shell named by the `SHELL` environment variable when set, and a default
POSIX shell when it is unset. This scenario is tested by
`ShellDetector_Detect_NonWindowsWithShellVariableSet_ReturnsPosixShell` and
`ShellDetector_Detect_NonWindowsWithShellVariableUnset_ReturnsDefaultPosixShell`, covering
`AgentControl-ShellDetector-DetectPosix`.
