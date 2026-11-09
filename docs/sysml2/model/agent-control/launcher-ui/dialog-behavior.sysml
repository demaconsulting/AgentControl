package AgentControl {
    private import SoftwareItemMetadata::*;

    part def DialogBehavior {
        doc /* Keyboard behavior every AgentControl dialog shares: Tab and Shift+Tab cycle inside the dialog, the first sensible control takes the visible keyboard focus when it opens, and Escape cancels a dialog that has a way to cancel. */

        metadata info : SoftwareInfo {
            description = "Keyboard behavior every AgentControl dialog shares: Tab and Shift+Tab cycle inside the dialog, the first sensible control takes the visible keyboard focus when it opens, and Escape cancels a dialog that has a way to cancel.";
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/DialogBehavior.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/DialogBehaviorTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/dialog-behavior.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/dialog-behavior.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/dialog-behavior.yaml");
        }
    }
}
