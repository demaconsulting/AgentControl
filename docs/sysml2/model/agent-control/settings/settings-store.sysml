package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SettingsStore {
        doc /* Loads/saves application-wide settings, including the user's own repo groups, card colors and collapsed sections; resolves the default configuration directory. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Settings/SettingsStore.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/SettingsStoreTests.cs", "test/DemaConsulting.AgentControl.Tests/Settings/SettingsStoreOrganizingTests.cs");
            designFiles = ("docs/design/agent-control/settings/settings-store.md");
            verificationFiles = ("docs/verification/agent-control/settings/settings-store.md");
            requirementFiles = ("docs/reqstream/agent-control/settings/settings-store.yaml");
        }
    }
}
