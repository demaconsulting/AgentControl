package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SettingsSubsystem {
        doc
        /*
         * Loads/saves application-wide settings (package source path, git/agent-tool
         * overrides, recent repos, shell preference, and the user's own repo groups, card
         * colors and collapsed sections). The data types AppSettings, RecentRepo, AgentToolKind
         * and ThemePreference are documented as supporting types of this subsystem rather than
         * as units of their own.
         */

        // Note - test/DemaConsulting.AgentControl.Tests/Settings/SettingsSubsystemTests.cs: the unit tests of SettingsStore and RepoGroupName are in the same folder
        metadata info : SoftwareInfo {
            description = "Loads/saves application-wide settings (package source path, git/agent-tool overrides, recent repos, shell preference, and the user's own repo groups, card colors and collapsed sections). The data types AppSettings, RecentRepo, AgentToolKind and ThemePreference are documented as supporting types of this subsystem rather than as units of their own.";
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/SettingsSubsystemTests.cs");
            designFiles = ("docs/design/agent-control/settings.md");
            verificationFiles = ("docs/verification/agent-control/settings.md");
            requirementFiles = ("docs/reqstream/agent-control/settings.yaml");
        }

        part settingsStore : SettingsStore;
        part repoGroupName : RepoGroupName;
    }
}
