package AgentControl_SwitchBranchViewModel {
    private import SoftwareItemMetadata::*;

    part def SwitchBranchViewModel {
        doc /* View-model of the "Switch branch" dialog opened from a repo card's menu: lists the branches the repo already knows (local first, then remote-tracking, read without a fetch), marks the checked-out branch or names the commit of a detached HEAD, lets the user filter the list as they type and choose a branch other than the current one by button, Enter or double-click, reads the branches off the UI thread through the background-work runner and shows a failed read as text, and completes exactly once with the chosen branch or with none; its rows (BranchItemViewModel) and its answer (BranchChoice) are in the same file, and the SwitchBranchWindow view is a supporting type of the LauncherUI subsystem. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/SwitchBranchViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/SwitchBranchViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/switch-branch-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/switch-branch-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/switch-branch-view-model.yaml");
        }
    }
}
