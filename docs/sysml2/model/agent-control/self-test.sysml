package AgentControl {
    part def SelfTestSubsystem {
        doc /* Self-validation framework exercising the tool's own capabilities on demand. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/SelfTest/SelfTestSubsystemTests.cs */
        comment designRef /* Design: docs/design/agent-control/self-test.md */
        comment verificationRef /* Verification: docs/verification/agent-control/self-test.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/self-test.yaml */

        part validation : Validation;
    }
}
