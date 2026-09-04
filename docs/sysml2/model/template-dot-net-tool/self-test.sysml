package TemplateDotNetTool {
    part def SelfTestSubsystem {
        doc /* Self-validation framework exercising the tool's own capabilities on demand. */

        comment testRef /* Test: test/DemaConsulting.TemplateDotNetTool.Tests/SelfTest/SelfTestSubsystemTests.cs */
        comment designRef /* Design: docs/design/template-dot-net-tool/self-test.md */
        comment verificationRef /* Verification: docs/verification/template-dot-net-tool/self-test.md */
        comment reqRef /* Requirements: docs/reqstream/template-dot-net-tool/self-test.yaml */

        part validation : Validation;
    }
}
