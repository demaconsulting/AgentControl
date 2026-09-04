package TemplateDotNetTool {
    part def CliSubsystem {
        doc /* Command-line argument parsing, output-channel management, and exit-code tracking. */

        comment testRef /* Test: test/DemaConsulting.TemplateDotNetTool.Tests/Cli/CliSubsystemTests.cs */
        comment designRef /* Design: docs/design/template-dot-net-tool/cli.md */
        comment verificationRef /* Verification: docs/verification/template-dot-net-tool/cli.md */
        comment reqRef /* Requirements: docs/reqstream/template-dot-net-tool/cli.yaml */

        part context : Context;
    }
}
