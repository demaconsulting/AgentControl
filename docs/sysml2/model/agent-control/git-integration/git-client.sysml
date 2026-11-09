package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitClient {
        doc /* Invokes the configured git executable for status, pull, branch, and committed-files queries. */

        metadata info : SoftwareInfo {
            description = "Invokes the configured git executable for status, pull, branch, and committed-files queries.";
            sourceFiles = ("src/DemaConsulting.AgentControl/GitIntegration/GitClient.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/GitClientTests.cs");
            designFiles = ("docs/design/agent-control/git-integration/git-client.md");
            verificationFiles = ("docs/verification/agent-control/git-integration/git-client.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration/git-client.yaml");
        }
    }
}
