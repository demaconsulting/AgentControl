package AgentControl_RepoGroupHeaderViewModel {
    private import SoftwareItemMetadata::*;

    part def RepoGroupHeaderViewModel {
        doc /* Collapsible header row that starts a section of the main window's repo list (Favorites, a user group or Ungrouped): describes the section and its count and state, says where a group is in the group tree (depth, parent, indent), shows the group's color (never for Favorites or Ungrouped), offers Move up/Move down and Sort by name as its owner allows, and reports toggle, add, new-subgroup, move, sort, rename and remove requests; RepoGroupKind identifies the kind of section. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/RepoGroupHeaderViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupHeaderViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/repo-group-header-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/repo-group-header-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/repo-group-header-view-model.yaml");
        }
    }
}
