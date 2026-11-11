package AgentControl {
    private import SoftwareItemMetadata::*;

    part def UtilitiesSubsystem {
        doc /* Shared, independently testable helper functions used by other subsystems. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Utilities/UtilitiesSubsystemTests.cs");
            designFiles = ("docs/design/agent-control/utilities.md");
            verificationFiles = ("docs/verification/agent-control/utilities.md");
            requirementFiles = ("docs/reqstream/agent-control/utilities.yaml");
        }

        part pathHelpers : PathHelpers;
    }
}
