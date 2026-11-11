package AgentControl {
    private import SoftwareItemMetadata::*;

    part def AgentToolLauncher {
        doc /* Builds process-start information to run the agent tool inside a detected shell. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentToolLauncher/AgentToolLauncher.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentToolLauncher/AgentToolLauncherTests.cs");
            designFiles = ("docs/design/agent-control/agent-tool-launcher/agent-tool-launcher.md");
            verificationFiles = ("docs/verification/agent-control/agent-tool-launcher/agent-tool-launcher.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-tool-launcher/agent-tool-launcher.yaml");
        }
    }
}
