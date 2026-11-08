package AgentControl {
    part def LoggingSubsystem {
        doc
        /*
         * Process-wide ILoggerFactory holder (AppLogging) and Serilog-backed rolling
         * diagnostic log file setup (LoggingSetup), including process-wide handlers for
         * unhandled and unobserved exceptions that log only a secret-redacted description.
         * The only dedicated unit test file is LoggingSetupTests.cs; see
         * docs/reqstream/agent-control/logging.yaml's test-evidence
         * disclosure for the indirect-only coverage of the rest (via
         * TestLoggingFixture.cs's one-time LoggingSetup.Initialize call).
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Logging/LoggingSetupTests.cs; test/DemaConsulting.AgentControl.Tests/TestLoggingFixture.cs (indirect only) */
        comment designRef /* Design: docs/design/agent-control/logging.md */
        comment verificationRef /* Verification: docs/verification/agent-control/logging.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/logging.yaml */

        part serilog : Serilog;
    }
}
