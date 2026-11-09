package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoGroupHeaderViewModel {
        doc /* Collapsible header row that starts a section of the main window's repo list (Favorites, a user group or Ungrouped): describes the section and its count and state, and reports toggle, rename and remove requests; RepoGroupKind identifies the kind of section. */

        metadata info : SoftwareInfo {
            description = "Collapsible header row that starts a section of the main window's repo list (Favorites, a user group or Ungrouped): describes the section and its count and state, and reports toggle, rename and remove requests; RepoGroupKind identifies the kind of section.";
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/RepoGroupHeaderViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupHeaderViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/repo-group-header-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/repo-group-header-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/repo-group-header-view-model.yaml");
        }
    }
}
