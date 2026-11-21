package AgentControl {
    private import SoftwareItemMetadata::*;

    part def CommittedAgentFilesCache {
        doc /* Memoizes the committed-agent-files-changed result per repo path, keyed by HEAD commit hash; safe for concurrent use (a private lock guards it), because cards read git and consult it on worker threads while the UI thread may consult or invalidate it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/GitIntegration/CommittedAgentFilesCache.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/CommittedAgentFilesCacheTests.cs");
            designFiles = ("docs/design/agent-control/git-integration/committed-agent-files-cache.md");
            verificationFiles = ("docs/verification/agent-control/git-integration/committed-agent-files-cache.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration/committed-agent-files-cache.yaml");
        }
    }
}
