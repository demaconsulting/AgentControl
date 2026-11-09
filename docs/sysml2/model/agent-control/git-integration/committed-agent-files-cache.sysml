package AgentControl {
    private import SoftwareItemMetadata::*;

    part def CommittedAgentFilesCache {
        doc /* Memoizes the committed-agent-files-changed result per repo path, keyed by HEAD commit hash. */

        metadata info : SoftwareInfo {
            description = "Memoizes the committed-agent-files-changed result per repo path, keyed by HEAD commit hash.";
            sourceFiles = ("src/DemaConsulting.AgentControl/GitIntegration/CommittedAgentFilesCache.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/CommittedAgentFilesCacheTests.cs");
            designFiles = ("docs/design/agent-control/git-integration/committed-agent-files-cache.md");
            verificationFiles = ("docs/verification/agent-control/git-integration/committed-agent-files-cache.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration/committed-agent-files-cache.yaml");
        }
    }
}
