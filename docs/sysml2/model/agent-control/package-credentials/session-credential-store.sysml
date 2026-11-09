package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SessionCredentialStore {
        doc /* In-memory store of the credentials the user entered (or found saved in their own credential store) for package servers, keyed by server origin; never persisted by AgentControl, cleared at exit, forgets an exact rejected credential, and shares one look-up of the user's credential store per server per session. */

        metadata info : SoftwareInfo {
            description = "In-memory store of the credentials the user entered (or found saved in their own credential store) for package servers, keyed by server origin; never persisted by AgentControl, cleared at exit, forgets an exact rejected credential, and shares one look-up of the user's credential store per server per session.";
            sourceFiles = ("src/DemaConsulting.AgentControl/PackageCredentials/SessionCredentialStore.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/PackageCredentials/SessionCredentialStoreTests.cs");
            designFiles = ("docs/design/agent-control/package-credentials/session-credential-store.md");
            verificationFiles = ("docs/verification/agent-control/package-credentials/session-credential-store.md");
            requirementFiles = ("docs/reqstream/agent-control/package-credentials/session-credential-store.yaml");
        }
    }
}
