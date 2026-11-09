package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SecretRedactingLogger {
        doc /* Logger wrapper (with its redaction engine and secret-free exception stand-in) that removes URL user information, query strings, fragments, authorization header values and registered secret values from every message, property, scope and exception before it reaches the application log or the user interface. */

        metadata info : SoftwareInfo {
            description = "Logger wrapper (with its redaction engine and secret-free exception stand-in) that removes URL user information, query strings, fragments, authorization header values and registered secret values from every message, property, scope and exception before it reaches the application log or the user interface.";
            sourceFiles = ("src/DemaConsulting.AgentControl/PackageCredentials/SecretRedactingLogger.cs", "src/DemaConsulting.AgentControl/PackageCredentials/SecretRedactor.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/PackageCredentials/SecretRedactingLoggerTests.cs", "test/DemaConsulting.AgentControl.Tests/PackageCredentials/SecretRedactorTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/PackageSourceErrorRedactionTests.cs");
            designFiles = ("docs/design/agent-control/package-credentials/secret-redacting-logger.md");
            verificationFiles = ("docs/verification/agent-control/package-credentials/secret-redacting-logger.md");
            requirementFiles = ("docs/reqstream/agent-control/package-credentials/secret-redacting-logger.yaml");
        }
    }
}
