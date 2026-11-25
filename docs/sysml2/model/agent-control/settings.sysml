package AgentControl_SettingsSubsystem {
    public import AgentControl_SettingsStore::*;
    public import AgentControl_SettingsWriter::*;
    public import AgentControl_RepoGroupName::*;
    public import AgentControl_RepoGroupHierarchy::*;
    private import SoftwareItemMetadata::*;

    part def SettingsSubsystem {
        doc
        /*
         * Loads/saves application-wide settings (package source path, git/agent-tool
         * overrides, recent repos in the user's own order, shell preference, whether the shell stays open after the
         * agent tool exits, and the user's own repo groups, card
         * colors, collapsed sections, group colors, group order and the hierarchy of nested groups - unknown colors and
         * entries of groups that no repo is in and that are not part of the hierarchy are dropped, and a damaged
         * hierarchy is repaired, when loaded), and writes them in the background (one write at a
         * time, the newest state of a burst of changes, a failure reported once per failure streak) so the
         * window never waits for the settings file. The data types AppSettings, RecentRepo, AgentToolKind
         * and ThemePreference are documented as supporting types of this subsystem rather than
         * as units of their own.
         */

        // Note - test/DemaConsulting.AgentControl.Tests/Settings/SettingsSubsystemTests.cs: the unit tests of SettingsStore, RepoGroupName and RepoGroupHierarchy are in the same folder
        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/SettingsSubsystemTests.cs");
            designFiles = ("docs/design/agent-control/settings.md");
            verificationFiles = ("docs/verification/agent-control/settings.md");
            requirementFiles = ("docs/reqstream/agent-control/settings.yaml");
        }

        part settingsStore : SettingsStore;
        part settingsWriter : SettingsWriter;
        part repoGroupName : RepoGroupName;
        part repoGroupHierarchy : RepoGroupHierarchy;
    }
}
