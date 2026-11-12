package AgentControl {
    private import SoftwareItemMetadata::*;

    part def StartupSubsystem {
        doc /* Parses startup arguments (config-dir/git/agent-tool overrides) for hermetic testability. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Startup/");
            designFiles = ("docs/design/agent-control/startup.md");
            verificationFiles = ("docs/verification/agent-control/startup.md");
            requirementFiles = ("docs/reqstream/agent-control/startup.yaml");
        }

        part startupOptions : StartupOptions;
    }
}
