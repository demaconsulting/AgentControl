package AgentControl_GitIntegrationSubsystem {
    public import AgentControl_GitClient::*;
    public import AgentControl_GitCredentialClient::*;
    public import AgentControl_GitRemoteClient::*;
    public import AgentControl_CommittedAgentFilesCache::*;
    private import SoftwareItemMetadata::*;

    part def GitIntegrationSubsystem {
        doc /* Invokes git for status/pull/branch/committed-files queries and stops git tracking managed agent folders; tells whether a folder is the top folder of a Git repository without running git; caches committed-agent-files results per HEAD hash; keeps, finds and removes package-feed sign-ins in the user's own Git credential helper, and runs hardened git against untrusted remote repositories (tag listing, one-tag shallow fetch, tree listing, raw blob reading). */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/");
            designFiles = ("docs/design/agent-control/git-integration.md");
            verificationFiles = ("docs/verification/agent-control/git-integration.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration.yaml");
        }

        part gitClient : GitClient;
        part gitCredentialClient : GitCredentialClient;
        part gitRemoteClient : GitRemoteClient;
        part committedAgentFilesCache : CommittedAgentFilesCache;
    }
}
