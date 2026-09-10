package AgentControl {
    part def GitIgnoreEnsurer {
        doc /* Proactively ensures a repo's .gitignore covers the four managed agent folders via a marker-comment scan; additive-only, no git dependency. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/RepoSync/GitIgnoreEnsurer.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/RepoSync/GitIgnoreEnsurerTests.cs */
        comment designRef /* Design: docs/design/agent-control/repo-sync/git-ignore-ensurer.md */
        comment verificationRef /* Verification: docs/verification/agent-control/repo-sync/git-ignore-ensurer.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/repo-sync/git-ignore-ensurer.yaml */
    }
}
