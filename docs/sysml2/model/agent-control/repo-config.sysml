package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoConfigSubsystem {
        doc
        /*
         * Persists each repo's pinned agent package name, version and content identity (of the agent files) in a
         * per-repo `.agentcontrol.json` pin file; the pin records no package source and no Git commit. The RepoPin data record has no
         * dedicated test file and is folded into this subsystem-level description.
         */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoConfig/");
            designFiles = ("docs/design/agent-control/repo-config.md");
            verificationFiles = ("docs/verification/agent-control/repo-config.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-config.yaml");
        }

        part repoPinStore : RepoPinStore;
    }
}
