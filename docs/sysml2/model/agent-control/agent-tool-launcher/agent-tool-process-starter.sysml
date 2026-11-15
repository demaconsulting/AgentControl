package AgentControl {
    private import SoftwareItemMetadata::*;

    part def AgentToolProcessStarter {
        doc /* Starts the agent-tool process described by a process-start information through the operating system; the production implementation of the IAgentToolProcessStarter seam that lets the launch flow be tested without starting a shell. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentToolLauncher/AgentToolProcessStarter.cs", "src/DemaConsulting.AgentControl/AgentToolLauncher/IAgentToolProcessStarter.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentToolLauncher/AgentToolProcessStarterTests.cs");
            designFiles = ("docs/design/agent-control/agent-tool-launcher/agent-tool-process-starter.md");
            verificationFiles = ("docs/verification/agent-control/agent-tool-launcher/agent-tool-process-starter.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-tool-launcher/agent-tool-process-starter.yaml");
        }
    }

    // The starter delegates the actual spawn (and its logging and error translation) to AgentToolLauncher.Launch
    dependency AgentToolProcessStarter to AgentToolLauncher;
}
