package AgentControl {
    part def StartupSubsystem {
        doc /* Parses startup arguments (config-dir/git/agent-tool overrides) for hermetic testability. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Startup/ */
        comment designRef /* Design: docs/design/agent-control/startup.md */
        comment verificationRef /* Verification: docs/verification/agent-control/startup.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/startup.yaml */

        part startupOptions : StartupOptions;
    }
}
