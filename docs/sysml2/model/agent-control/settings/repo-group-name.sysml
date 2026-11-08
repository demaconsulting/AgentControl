package AgentControl {
    part def RepoGroupName {
        doc /* The rules for the names of the user's own repo groups: how a typed or stored name is cleaned up, which names are acceptable, how a stored name is repaired, and how names that differ only in letter case are treated as one group. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/Settings/RepoGroupName.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Settings/RepoGroupNameTests.cs */
        comment designRef /* Design: docs/design/agent-control/settings/repo-group-name.md */
        comment verificationRef /* Verification: docs/verification/agent-control/settings/repo-group-name.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/settings/repo-group-name.yaml */
    }
}
