package AgentControl_AgentToolLauncherSubsystem {
    public import AgentControl_ShellDetector::*;
    public import AgentControl_AgentToolLauncher::*;
    public import AgentControl_AgentToolProcessStarter::*;
    private import SoftwareItemMetadata::*;

    part def AgentToolLauncherSubsystem {
        doc
        /*
         * Detects the appropriate shell for the current platform and launches the configured
         * agent tool command inside it, in a repo's working directory, with the shell kept
         * open after the tool exits or ended together with it (the user's keep-shell-open
         * choice). The process is started through a replaceable starter (the
         * IAgentToolProcessStarter interface, a supporting type implemented by
         * AgentToolProcessStarter) so the launch flow can be tested without starting a shell.
         * The ShellKind enum has no dedicated test file and is folded into this
         * subsystem-level description.
         *
         * NOTE: this subsystem shares its base name with its AgentToolLauncher unit; per
         * software-items.md's naming-collision convention the subsystem part def is suffixed
         * "Subsystem" to disambiguate.
         */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentToolLauncher/");
            designFiles = ("docs/design/agent-control/agent-tool-launcher.md");
            verificationFiles = ("docs/verification/agent-control/agent-tool-launcher.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-tool-launcher.yaml");
        }

        part shellDetector : ShellDetector;
        part agentToolLauncher : AgentToolLauncher;
        part agentToolProcessStarter : AgentToolProcessStarter;
    }
}
