package AgentControl_SettingsStore {
    private import AgentControl_BlockingCallGuard::*;
    private import SoftwareItemMetadata::*;

    part def SettingsStore {
        doc /* Loads/saves application-wide settings, including the user's own repo order (with its one-time arrangement of an older file), repo groups, card colors, collapsed sections, group colors and group order (repaired on load: unknown colors and entries of groups no repo is in are dropped); resolves the default configuration directory. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Settings/SettingsStore.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/SettingsStoreTests.cs", "test/DemaConsulting.AgentControl.Tests/Settings/SettingsStoreOrganizingTests.cs", "test/DemaConsulting.AgentControl.Tests/Settings/SettingsStoreRepoOrderTests.cs", "test/DemaConsulting.AgentControl.Tests/Settings/SettingsStoreLockTests.cs");
            designFiles = ("docs/design/agent-control/settings/settings-store.md");
            verificationFiles = ("docs/verification/agent-control/settings/settings-store.md");
            requirementFiles = ("docs/reqstream/agent-control/settings/settings-store.yaml");
        }
    }

    // Asks the blocking-call guard before every save, so the tests catch a settings save made on the UI thread
    dependency SettingsStore to BlockingCallGuard;
}
