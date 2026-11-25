package AgentControl_RepoGroupHierarchy {
    private import AgentControl_RepoGroupName::*;
    private import SoftwareItemMetadata::*;

    part def RepoGroupHierarchy {
        doc /* The rules for how the user's repo groups nest: which group is inside which, in what order the groups of one level are listed, which moves keep the tree a tree, what deleting a group with its contents kept does to the tree, and how stored parents are repaired. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Settings/RepoGroupHierarchy.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/RepoGroupHierarchyTests.cs");
            designFiles = ("docs/design/agent-control/settings/repo-group-hierarchy.md");
            verificationFiles = ("docs/verification/agent-control/settings/repo-group-hierarchy.md");
            requirementFiles = ("docs/reqstream/agent-control/settings/repo-group-hierarchy.yaml");
        }
    }

    // Uses the group name rules to clean up, spell and compare the names it reads
    dependency RepoGroupHierarchy to RepoGroupName;
}
