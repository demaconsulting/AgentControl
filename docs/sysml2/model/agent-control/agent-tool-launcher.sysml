package AgentControl {
    part def AgentToolLauncherSubsystem {
        doc
        /*
         * Detects the appropriate shell for the current platform and launches the configured
         * agent tool command inside it, in a repo's working directory. The ShellKind enum has
         * no dedicated test file and is folded into this subsystem-level description.
         *
         * NOTE: this subsystem shares its base name with its AgentToolLauncher unit; per
         * software-items.md's naming-collision convention the subsystem part def is suffixed
         * "Subsystem" to disambiguate.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentToolLauncher/ */
        comment designRef /* Design: docs/design/agent-control/agent-tool-launcher.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-tool-launcher.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-tool-launcher.yaml */

        part shellDetector : ShellDetector;
        part agentToolLauncher : AgentToolLauncher;
    }
}
