package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageCredentialCallback {
        doc /* Answers the package client's requests for credentials during one operation against one server: scoped to the server's origin, asked once, session store then the user's own credential store before the user; hands a credential to the user's store only when the user chose to remember it; forgets exactly the credential a server rejected. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/PackageCredentials/PackageCredentialCallback.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/PackageCredentials/PackageCredentialCallbackTests.cs");
            designFiles = ("docs/design/agent-control/package-credentials/package-credential-callback.md");
            verificationFiles = ("docs/verification/agent-control/package-credentials/package-credential-callback.md");
            requirementFiles = ("docs/reqstream/agent-control/package-credentials/package-credential-callback.yaml");
        }
    }
}
