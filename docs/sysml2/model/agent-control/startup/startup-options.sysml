package AgentControl {
    private import SoftwareItemMetadata::*;

    part def StartupOptions {
        doc /* Parses config-dir/git/agent-tool startup argument overrides. */

        metadata info : SoftwareInfo {
            description = "Parses config-dir/git/agent-tool startup argument overrides.";
            sourceFiles = ("src/DemaConsulting.AgentControl/Startup/StartupOptions.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Startup/StartupOptionsTests.cs");
            designFiles = ("docs/design/agent-control/startup/startup-options.md");
            verificationFiles = ("docs/verification/agent-control/startup/startup-options.md");
            requirementFiles = ("docs/reqstream/agent-control/startup/startup-options.yaml");
        }
    }
}
