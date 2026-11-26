package AgentControl_CloneRepositoryViewModel {
    private import AgentControl_GitClient::*;
    private import SoftwareItemMetadata::*;

    part def CloneRepositoryViewModel {
        doc /* View-model of the "Clone repository" dialog opened from the app bar's Clone button: collects the repository address, the parent folder and a folder name that follows the address until the user edits it, and the group of the new card (Ungrouped by default); refuses an address git would read as an option or a remote helper, a folder name that is not one valid Windows file name and a parent folder that is not absolute, as text with Clone disabled; checks the destination on disk off the UI thread through the background-work runner when Clone is pressed (reusing the rules of GitClient.CheckCloneUrl and GitClient.CheckCloneDestination); and completes exactly once with a request or with none; its group choice (CloneGroupChoice) and its answer (CloneRepositoryRequest) are in the same file, and the CloneRepositoryWindow view is a supporting type of the LauncherUI subsystem. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/CloneRepositoryViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/CloneRepositoryViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/clone-repository-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/clone-repository-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/clone-repository-view-model.yaml");
        }
    }

    // The dialog applies the same address and destination rules the clone itself applies, which
    // GitClient decides
    dependency CloneRepositoryViewModel to GitClient;
}
