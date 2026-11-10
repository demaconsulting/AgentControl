package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoPinStore {
        doc /* Loads/saves a repo's `.agentcontrol.json` pin file (pinned package name, version, preferred source alias, content identity of the files and Git source revision; migrates older schemas). */

        metadata info : SoftwareInfo {
            description = "Loads/saves a repo's `.agentcontrol.json` pin file (pinned package name, version, preferred source alias, content identity of the files and Git source revision; migrates older schemas).";
            sourceFiles = ("src/DemaConsulting.AgentControl/RepoConfig/RepoPinStore.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoConfig/RepoPinStoreTests.cs");
            designFiles = ("docs/design/agent-control/repo-config/repo-pin-store.md");
            verificationFiles = ("docs/verification/agent-control/repo-config/repo-pin-store.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-config/repo-pin-store.yaml");
        }
    }
}
