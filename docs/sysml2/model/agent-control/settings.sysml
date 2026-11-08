package AgentControl {
    part def SettingsSubsystem {
        doc
        /*
         * Loads/saves application-wide settings (package source path, git/agent-tool
         * overrides, recent repos, shell preference, and the user's own repo groups, card
         * colors and collapsed sections). The data types AppSettings, RecentRepo, AgentToolKind
         * and ThemePreference, and the static group-name rules RepoGroupName (which has its own
         * RepoGroupNameTests test file but no behavior beyond validating and cleaning a name), are
         * documented as supporting types of this subsystem rather than as units of their own.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Settings/ */
        comment designRef /* Design: docs/design/agent-control/settings.md */
        comment verificationRef /* Verification: docs/verification/agent-control/settings.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/settings.yaml */

        part settingsStore : SettingsStore;
    }
}
