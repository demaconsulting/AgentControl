package AgentControl_RepoGroupName {
    private import SoftwareItemMetadata::*;

    part def RepoGroupName {
        doc /* The rules for the names of the user's own repo groups: how a typed or stored name is cleaned up, which names are acceptable, how a stored name is repaired, and how names that differ only in letter case are treated as one group. A group exists while a repo names it or while it is registered in the stored group tree, and a name is unique across the whole tree. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Settings/RepoGroupName.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/RepoGroupNameTests.cs");
            designFiles = ("docs/design/agent-control/settings/repo-group-name.md");
            verificationFiles = ("docs/verification/agent-control/settings/repo-group-name.md");
            requirementFiles = ("docs/reqstream/agent-control/settings/repo-group-name.yaml");
        }
    }
}
