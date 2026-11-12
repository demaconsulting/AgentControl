package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GroupNameViewModel {
        doc /* View-model of the prompt that asks for a repo group's name (new group or rename): validates as the user types, lets only a usable name through, lets a new group join an existing one of the same name, and completes exactly once. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/GroupNameViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/GroupNameViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/group-name-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/group-name-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/group-name-view-model.yaml");
        }
    }
}
