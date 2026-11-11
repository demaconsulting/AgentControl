package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PathHelpers {
        doc /* Safe path-combination utility guarding against path-traversal. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Utilities/PathHelpers.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Utilities/PathHelpersTests.cs");
            designFiles = ("docs/design/agent-control/utilities/path-helpers.md");
            verificationFiles = ("docs/verification/agent-control/utilities/path-helpers.md");
            requirementFiles = ("docs/reqstream/agent-control/utilities/path-helpers.yaml");
        }
    }
}
