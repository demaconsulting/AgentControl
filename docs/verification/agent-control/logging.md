## Logging

### Verification Approach

The `Logging` subsystem has a single dedicated unit test file,
`test/DemaConsulting.AgentControl.Tests/Logging/LoggingSetupTests.cs`, which directly proves
that a log entry written through the pipeline `LoggingSetup.Initialize` configures is actually
persisted to the documented `logs` subfolder location and content. Multi-day rolling and the
14-file retention limit are not independently re-verified here - they are Serilog's own
(`Serilog.Sinks.File`) tested behavior, configured but not reimplemented by this subsystem;
see `docs/design/ots/serilog.md` for that OTS dependency's own verification note.

**Test-evidence basis**: `LoggingSetupTests` reuses the assembly-wide
`test/DemaConsulting.AgentControl.Tests/TestLoggingFixture.cs` fixture (an xUnit v3 assembly
fixture that calls `LoggingSetup.Initialize` once before any test in the assembly runs) rather
than calling `LoggingSetup.Initialize` a second time with a valid directory, because doing so
would redirect the process-wide static `Serilog.Log.Logger` away from the fixture's location for
the remainder of the test run. `LoggingSetupTests` also directly asserts that an unusable log
directory path (one `Directory.CreateDirectory` rejects) is normalized to the documented
`InvalidOperationException` contract. Every other passing test in the assembly remains
additional *indirect* evidence that `LoggingSetup.Initialize` did not throw during test-assembly
startup, consistent with the disclosure previously made at the requirements and design level for
this subsystem.

### Test Environment

Standard `dotnet test` execution of `DemaConsulting.AgentControl.Tests`; no additional test
environment setup is required beyond the assembly-wide `TestLoggingFixture`.

### Acceptance Criteria

- `LoggingSetup.Initialize` completes without throwing during test-assembly startup.
- `LoggingSetup.Initialize` throws `InvalidOperationException` (not a raw path-validation
  exception) when the log directory cannot be created.
- A log entry written through the initialized pipeline appears in a rolling log file named
  `agentcontrol-*.log` under the configuration directory's `logs` subfolder, with the entry's
  content present verbatim in that file.
- An exception handed to the unhandled and unobserved exception safety-net entry points is logged without any secret
  - a registered credential, an authorization value, address user information or query string, in the message or an
  inner exception - and an unhandled object that is not an exception is tolerated.

### Test Scenarios

**LoggingSetup_Initialize_LogDirectoryPathInvalid_ThrowsInvalidOperationException**: an
unusable log-directory path (containing a NUL character, rejected by
`Directory.CreateDirectory` on every platform) causes `LoggingSetup.Initialize` to throw
`InvalidOperationException` rather than leak the raw exception type, covering
`AgentControl-Logging-DiagnosticFile`'s error-handling contract.

**LoggingSetup_Initialize_LogEntryWritten_AppearsInLogFileUnderLogsSubfolder**: a log entry
written through the fixture-initialized pipeline is found, verbatim, in the single
`agentcontrol-*.log` file under the configuration directory's `logs` subfolder, directly
covering `AgentControl-Logging-DiagnosticFile`'s rolling-file-location and captured-detail
behavior.

**Logging_DiagnosticFile_IndirectEvidenceOnlyViaAssemblyFixture**: `LoggingSetup.Initialize`
is invoked once by `TestLoggingFixture` before any test in
`DemaConsulting.AgentControl.Tests` runs; any passing test in that assembly is therefore
additional indirect evidence that initialization succeeded without throwing. This scenario is
evidenced indirectly by `GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue` (an arbitrary,
`AgentControl-Logging-DiagnosticFile`.

**LoggingSetup_UnhandledExceptionsRedacted_NoSecretReachesTheLog**: The two safety-net entry points
(`ReportUnhandledException` and `ReportUnobservedTaskException`) are called directly with a recording logger, because
raising a real process-wide unhandled exception would terminate the test process. An exception whose message, inner
exception, address and `Authorization` value carry a sentinel password, user information and query string is logged
at critical level without any of them - the recorded text keeps the exception type and the readable remainder of its
message - and the terminating flag is passed through; an unobserved task exception (an aggregate of such exceptions)
is redacted the same way; an unhandled object that is not an exception is tolerated. This scenario is tested by
`LoggingSetup_ReportUnhandledException_ExceptionCarryingSecrets_LogsOnlyASanitizedDescription` and
`LoggingSetup_ReportUnobservedAndNonExceptionObjects_AreRedactedAndTolerated`, covering
`AgentControl-Logging-UnhandledExceptionsRedacted`.
