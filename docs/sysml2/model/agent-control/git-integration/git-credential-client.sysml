package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitCredentialClient {
        doc /* Keeps, finds and removes a package feed's sign-in in the user's own Git credential helper by running the user's git executable (credential fill, approve and reject); never interactive, time-bounded, never logs a secret, and implements the PackageCredentials subsystem's IRememberedCredentialStore so AgentControl never stores a secret itself. */

        metadata info : SoftwareInfo {
            description = "Keeps, finds and removes a package feed's sign-in in the user's own Git credential helper by running the user's git executable (credential fill, approve and reject); never interactive, time-bounded, never logs a secret, and implements the PackageCredentials subsystem's IRememberedCredentialStore so AgentControl never stores a secret itself.";
            sourceFiles = ("src/DemaConsulting.AgentControl/GitIntegration/GitCredentialClient.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/GitCredentialClientTests.cs", "test/DemaConsulting.AgentControl.Tests/GitIntegration/GitCredentialClientIntegrationTests.cs", "test/DemaConsulting.AgentControl.Tests/GitIntegration/GitCredentialStub.cs");
            designFiles = ("docs/design/agent-control/git-integration/git-credential-client.md");
            verificationFiles = ("docs/verification/agent-control/git-integration/git-credential-client.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration/git-credential-client.yaml");
        }
    }
}
