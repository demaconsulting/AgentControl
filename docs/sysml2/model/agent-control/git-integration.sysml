package AgentControl {
    part def GitIntegrationSubsystem {
        doc /* Invokes git for status/pull/branch/committed-files queries; caches committed-agent-files results per HEAD hash; keeps, finds and removes package-feed sign-ins in the user's own Git credential helper, and runs hardened git against untrusted remote repositories (tag listing, one-tag shallow fetch, tree listing, raw blob reading). */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/GitIntegration/ */
        comment designRef /* Design: docs/design/agent-control/git-integration.md */
        comment verificationRef /* Verification: docs/verification/agent-control/git-integration.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/git-integration.yaml */

        part gitClient : GitClient;
        part gitCredentialClient : GitCredentialClient;
        part gitRemoteClient : GitRemoteClient;
        part committedAgentFilesCache : CommittedAgentFilesCache;
    }
}
