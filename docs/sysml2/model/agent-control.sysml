package AgentControl {
    doc
    /*
     * AgentControl is a template .NET command-line application demonstrating
     * DemaConsulting's compliance/documentation tooling conventions.
     */

    part def AgentControlSystem {
        doc /* The AgentControl system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/IntegrationTests.cs */
        comment designRef /* Design: docs/design/agent-control.md */
        comment verificationRef /* Verification: docs/verification/agent-control.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control.yaml */

        part program : Program;
        part cli : CliSubsystem;
        part selfTest : SelfTestSubsystem;
        part utilities : UtilitiesSubsystem;
    }
}
