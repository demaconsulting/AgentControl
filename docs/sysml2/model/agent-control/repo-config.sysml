package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoConfigSubsystem {
        doc
        /*
         * Persists each repo's pinned agent package name, version, source alias and content
         * identity in a per-repo `.agentcontrol.json` pin file. The RepoPin data record has no
         * dedicated test file and is folded into this subsystem-level description.
         */

        metadata info : SoftwareInfo {
            description = "Persists each repo's pinned agent package name, version, source alias and content identity in a per-repo `.agentcontrol.json` pin file. The RepoPin data record has no dedicated test file and is folded into this subsystem-level description.";
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoConfig/");
            designFiles = ("docs/design/agent-control/repo-config.md");
            verificationFiles = ("docs/verification/agent-control/repo-config.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-config.yaml");
        }

        part repoPinStore : RepoPinStore;
    }
}
