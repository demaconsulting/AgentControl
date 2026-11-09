package AgentControl {
    private import SoftwareItemMetadata::*;

    part def ShellDetector {
        doc /* Chooses the shell/terminal to launch the agent tool through, per platform. */

        metadata info : SoftwareInfo {
            description = "Chooses the shell/terminal to launch the agent tool through, per platform.";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentToolLauncher/ShellDetector.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentToolLauncher/ShellDetectorTests.cs");
            designFiles = ("docs/design/agent-control/agent-tool-launcher/shell-detector.md");
            verificationFiles = ("docs/verification/agent-control/agent-tool-launcher/shell-detector.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-tool-launcher/shell-detector.yaml");
        }
    }
}
