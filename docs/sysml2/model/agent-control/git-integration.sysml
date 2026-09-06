package AgentControl {
    part def GitIntegrationSubsystem {
        doc /* Invokes git for status/pull/branch/committed-files queries; caches committed-agent-files results per HEAD hash. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/GitIntegration/ */
        comment designRef /* Design: docs/design/agent-control/git-integration.md */
        comment verificationRef /* Verification: docs/verification/agent-control/git-integration.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/git-integration.yaml */

        part gitClient : GitClient;
        part committedAgentFilesCache : CommittedAgentFilesCache;
    }
}
