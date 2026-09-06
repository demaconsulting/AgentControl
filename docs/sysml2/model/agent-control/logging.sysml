package AgentControl {
    part def LoggingSubsystem {
        doc
        /*
         * Process-wide ILoggerFactory holder (AppLogging) and Serilog-backed rolling
         * diagnostic log file setup (LoggingSetup). No dedicated unit test file exists for
         * this subsystem; see docs/reqstream/agent-control/logging.yaml's test-evidence
         * disclosure for the indirect-only coverage this implies (via
         * TestLoggingFixture.cs's one-time LoggingSetup.Initialize call).
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/TestLoggingFixture.cs (indirect only) */
        comment designRef /* Design: docs/design/agent-control/logging.md */
        comment verificationRef /* Verification: docs/verification/agent-control/logging.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/logging.yaml */

        part serilog : Serilog;
    }
}
