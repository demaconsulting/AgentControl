package AgentControl {
    part def Program {
        doc
        /*
         * Entry point and bootstrap orchestrator. Parses startup arguments, initializes
         * diagnostic logging, and configures Avalonia's classic desktop lifetime (App) which
         * loads settings and shows the main window. App itself has no dedicated unit test
         * file, so its bootstrap responsibility is documented as part of this unit.
         */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/Program.cs, src/DemaConsulting.AgentControl/App.axaml.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/ProgramTests.cs */
        comment designRef /* Design: docs/design/agent-control/program.md */
        comment verificationRef /* Verification: docs/verification/agent-control/program.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/program.yaml */
    }
}
