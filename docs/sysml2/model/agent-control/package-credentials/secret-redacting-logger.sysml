package AgentControl {
    part def SecretRedactingLogger {
        doc /* Logger wrapper that removes URL user information, query strings, fragments and authorization header values from every message before it reaches the application log. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/PackageCredentials/SecretRedactingLogger.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/PackageCredentials/SecretRedactingLoggerTests.cs */
        comment designRef /* Design: docs/design/agent-control/package-credentials/secret-redacting-logger.md */
        comment verificationRef /* Verification: docs/verification/agent-control/package-credentials/secret-redacting-logger.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/package-credentials/secret-redacting-logger.yaml */
    }
}
