package TemplateDotNetTool {
    part def UtilitiesSubsystem {
        doc /* Shared, independently testable helper functions used by other subsystems. */

        comment testRef /* Test: test/DemaConsulting.TemplateDotNetTool.Tests/Utilities/UtilitiesSubsystemTests.cs */
        comment designRef /* Design: docs/design/template-dot-net-tool/utilities.md */
        comment verificationRef /* Verification: docs/verification/template-dot-net-tool/utilities.md */
        comment reqRef /* Requirements: docs/reqstream/template-dot-net-tool/utilities.yaml */

        part pathHelpers : PathHelpers;
    }
}
