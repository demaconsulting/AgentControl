package AgentControl {
    part def SettingsSubsystem {
        doc
        /*
         * Loads/saves application-wide settings (package source path, git/agent-tool
         * overrides, recent repos, shell preference, and the user's own repo groups, card
         * colors and collapsed sections). The data types AppSettings, RecentRepo, AgentToolKind
         * and ThemePreference are documented as supporting types of this subsystem rather than
         * as units of their own.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/Settings/SettingsSubsystemTests.cs (the unit tests of SettingsStore and RepoGroupName are in the same folder) */
        comment designRef /* Design: docs/design/agent-control/settings.md */
        comment verificationRef /* Verification: docs/verification/agent-control/settings.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/settings.yaml */

        part settingsStore : SettingsStore;
        part repoGroupName : RepoGroupName;
    }
}
