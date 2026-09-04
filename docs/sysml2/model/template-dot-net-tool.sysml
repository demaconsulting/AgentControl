package TemplateDotNetTool {
    doc
    /*
     * TemplateDotNetTool is a template .NET command-line application demonstrating
     * DemaConsulting's compliance/documentation tooling conventions.
     */

    part def TemplateDotNetToolSystem {
        doc /* The TemplateDotNetTool system as a whole. */

        comment testRef /* Test: test/DemaConsulting.TemplateDotNetTool.Tests/IntegrationTests.cs */
        comment designRef /* Design: docs/design/template-dot-net-tool.md */
        comment verificationRef /* Verification: docs/verification/template-dot-net-tool.md */
        comment reqRef /* Requirements: docs/reqstream/template-dot-net-tool.yaml */

        part program : Program;
        part cli : CliSubsystem;
        part selfTest : SelfTestSubsystem;
        part utilities : UtilitiesSubsystem;
    }
}
