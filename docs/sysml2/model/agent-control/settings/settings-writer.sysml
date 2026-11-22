package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SettingsWriter {
        doc /* Writes settings snapshots in the background so a change made in the window never waits for the settings file: one write at a time on a worker thread, a trailing debounce that writes only the newest of a burst of snapshots, a confirmed write that tells its caller whether it reached the disk (dropping an older waiting snapshot only when it did), a failure of a background write reported once per failure streak with the snapshot kept for the next write, and a flush (also with a bounded wait, for the window's close). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/Settings/SettingsWriter.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/Settings/SettingsWriterTests.cs");
            designFiles = ("docs/design/agent-control/settings/settings-writer.md");
            verificationFiles = ("docs/verification/agent-control/settings/settings-writer.md");
            requirementFiles = ("docs/reqstream/agent-control/settings/settings-writer.yaml");
        }
    }
}
