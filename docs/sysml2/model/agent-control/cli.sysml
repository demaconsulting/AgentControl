package AgentControl {
    part def CliSubsystem {
        doc /* Command-line argument parsing, output-channel management, and exit-code tracking. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Cli/CliSubsystemTests.cs */
        comment designRef /* Design: docs/design/agent-control/cli.md */
        comment verificationRef /* Verification: docs/verification/agent-control/cli.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/cli.yaml */

        part context : Context;
    }
}
