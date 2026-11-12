package AgentControl {
    private import SoftwareItemMetadata::*;

    part def FeedOrigin {
        doc /* The scheme, host and port of a package server: the unit a credential is scoped to. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/PackageCredentials/FeedOrigin.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/PackageCredentials/FeedOriginTests.cs");
            designFiles = ("docs/design/agent-control/package-credentials/feed-origin.md");
            verificationFiles = ("docs/verification/agent-control/package-credentials/feed-origin.md");
            requirementFiles = ("docs/reqstream/agent-control/package-credentials/feed-origin.yaml");
        }
    }
}
