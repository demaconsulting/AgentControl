package AgentControl {
    private import SoftwareItemMetadata::*;

    part def ReleaseNotesViewerViewModel {
        doc /* Release-notes dialog view-model shown after a successful sync. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/RepoSync/ReleaseNotesViewerViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoSync/ReleaseNotesViewerViewModelTests.cs");
            designFiles = ("docs/design/agent-control/repo-sync/release-notes-viewer-view-model.md");
            verificationFiles = ("docs/verification/agent-control/repo-sync/release-notes-viewer-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-sync/release-notes-viewer-view-model.yaml");
        }
    }
}
