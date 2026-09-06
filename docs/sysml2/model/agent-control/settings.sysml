package AgentControl {
    part def SettingsSubsystem {
        doc
        /*
         * Loads/saves application-wide settings (package source path, git/agent-tool
         * overrides, recent repos, shell preference). AppSettings, RecentRepo, and
         * AgentToolKind have no dedicated test file and are folded into this subsystem-level
         * description.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Settings/ */
        comment designRef /* Design: docs/design/agent-control/settings.md */
        comment verificationRef /* Verification: docs/verification/agent-control/settings.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/settings.yaml */

        part settingsStore : SettingsStore;
    }
}
