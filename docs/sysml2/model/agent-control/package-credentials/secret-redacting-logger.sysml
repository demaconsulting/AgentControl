package AgentControl {
    part def SecretRedactingLogger {
        doc /* Logger wrapper (with its redaction engine and secret-free exception stand-in) that removes URL user information, query strings, fragments, authorization header values and registered secret values from every message, property, scope and exception before it reaches the application log or the user interface. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/PackageCredentials/SecretRedactingLogger.cs; src/DemaConsulting.AgentControl/PackageCredentials/SecretRedactor.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/PackageCredentials/SecretRedactingLoggerTests.cs; test/DemaConsulting.AgentControl.Tests/PackageCredentials/SecretRedactorTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/PackageSourceErrorRedactionTests.cs */
        comment designRef /* Design: docs/design/agent-control/package-credentials/secret-redacting-logger.md */
        comment verificationRef /* Verification: docs/verification/agent-control/package-credentials/secret-redacting-logger.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/package-credentials/secret-redacting-logger.yaml */
    }
}
