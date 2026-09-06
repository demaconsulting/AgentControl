package AgentControl {
    part def RepoConfigSubsystem {
        doc
        /*
         * Persists each repo's pinned agent package name/version in a per-repo
         * `.agentcontrol.json` pin file. The RepoPin data record has no dedicated test file
         * and is folded into this subsystem-level description.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/RepoConfig/ */
        comment designRef /* Design: docs/design/agent-control/repo-config.md */
        comment verificationRef /* Verification: docs/verification/agent-control/repo-config.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/repo-config.yaml */

        part repoPinStore : RepoPinStore;
    }
}
