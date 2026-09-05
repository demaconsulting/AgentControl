package AgentControl {
    part def UtilitiesSubsystem {
        doc /* Shared, independently testable helper functions used by other subsystems. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Utilities/UtilitiesSubsystemTests.cs */
        comment designRef /* Design: docs/design/agent-control/utilities.md */
        comment verificationRef /* Verification: docs/verification/agent-control/utilities.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/utilities.yaml */

        part pathHelpers : PathHelpers;
    }
}
