package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GlobalProgressViewModel {
        doc /* Progress strip directly under the main window's app bar, shown only while bulk work that is not tied to one repo card runs (adding repositories, refreshing the cards after the settings were saved): a thin bar (indeterminate until a count is known), one short status text that is also the bar's screen-reader name, and Cancel for an operation that can be canceled; shows the most recently begun operation still running and goes back to the previous one, or to idle, when it ends; GlobalProgressOperation is the handle an operation's owner reports through. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/GlobalProgressViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/GlobalProgressViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/global-progress-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/global-progress-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/global-progress-view-model.yaml");
        }
    }
}
