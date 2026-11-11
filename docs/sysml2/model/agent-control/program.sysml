package AgentControl {
    private import SoftwareItemMetadata::*;

    part def Program {
        doc
        /*
         * Entry point and bootstrap orchestrator. Parses startup arguments, initializes
         * diagnostic logging, and configures Avalonia's classic desktop lifetime (App) which
         * loads settings and shows the main window. App itself has no dedicated unit test
         * file, so its bootstrap responsibility is documented as part of this unit.
         */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Program.cs", "src/DemaConsulting.AgentControl/App.axaml.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/ProgramTests.cs");
            designFiles = ("docs/design/agent-control/program.md");
            verificationFiles = ("docs/verification/agent-control/program.md");
            requirementFiles = ("docs/reqstream/agent-control/program.yaml");
        }
    }
}
