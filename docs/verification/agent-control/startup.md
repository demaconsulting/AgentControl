## Startup

### Verification Approach

The `Startup` subsystem is verified indirectly through its `StartupOptions` unit's test suite
(`StartupOptionsTests.cs`), documented in the `StartupOptions` unit-level verification
design. Every test operates purely on in-memory argument arrays; no filesystem or process
dependency is involved.

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `StartupOptions` pass with zero failures.
- Every supported override argument is captured accurately when supplied.
- An unsupported argument or a missing value for a recognized argument is rejected clearly.

### Test Scenarios

**Startup_ParseArguments_UnitTestsCoverCaptureAndRejection**: Startup-argument parsing is
verified by the `StartupOptions` unit tests (see the `StartupOptions` unit verification
design), with `StartupOptions_Parse_ConfigDirArgument_CapturesValue` and
`StartupOptions_Parse_UnsupportedArgument_ThrowsArgumentException` cited directly at the
subsystem level, covering `AgentControl-Startup-ParseArguments` (child:
`AgentControl-StartupOptions-Parse`).
