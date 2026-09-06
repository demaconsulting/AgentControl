package AgentControl {
    part def CommittedAgentFilesCache {
        doc /* Memoizes the committed-agent-files-changed result per repo path, keyed by HEAD commit hash. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/GitIntegration/CommittedAgentFilesCache.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/GitIntegration/CommittedAgentFilesCacheTests.cs */
        comment designRef /* Design: docs/design/agent-control/git-integration/committed-agent-files-cache.md */
        comment verificationRef /* Verification: docs/verification/agent-control/git-integration/committed-agent-files-cache.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/git-integration/committed-agent-files-cache.yaml */
    }
}
