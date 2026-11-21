package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SettingsSubsystem {
        doc
        /*
         * Loads/saves application-wide settings (package source path, git/agent-tool
         * overrides, recent repos in the user's own order, shell preference, whether the shell stays open after the
         * agent tool exits, and the user's own repo groups, card
         * colors, collapsed sections, group colors and group order - unknown colors and entries of
         * groups no repo is in are dropped when loaded). The data types AppSettings, RecentRepo, AgentToolKind
         * and ThemePreference are documented as supporting types of this subsystem rather than
         * as units of their own.
         */

        // Note - test/DemaConsulting.AgentControl.Tests/Settings/SettingsSubsystemTests.cs: the unit tests of SettingsStore and RepoGroupName are in the same folder
        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/SettingsSubsystemTests.cs");
            designFiles = ("docs/design/agent-control/settings.md");
            verificationFiles = ("docs/verification/agent-control/settings.md");
            requirementFiles = ("docs/reqstream/agent-control/settings.yaml");
        }

        part settingsStore : SettingsStore;
        part repoGroupName : RepoGroupName;
    }
}
