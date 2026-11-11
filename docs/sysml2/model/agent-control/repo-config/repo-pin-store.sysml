package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoPinStore {
        doc /* Loads/saves a repo's `.agentcontrol.json` pin file (pinned package name, version and the content identity of the files - no package source and no Git commit; older schemas are read, with an older Source kept only as a read-time hint). */

        metadata info : SoftwareInfo {
            description = "Loads/saves a repo's `.agentcontrol.json` pin file (pinned package name, version and the content identity of the files - no package source and no Git commit; older schemas are read, with an older Source kept only as a read-time hint).";
            sourceFiles = ("src/DemaConsulting.AgentControl/RepoConfig/RepoPinStore.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoConfig/RepoPinStoreTests.cs");
            designFiles = ("docs/design/agent-control/repo-config/repo-pin-store.md");
            verificationFiles = ("docs/verification/agent-control/repo-config/repo-pin-store.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-config/repo-pin-store.yaml");
        }
    }
}
