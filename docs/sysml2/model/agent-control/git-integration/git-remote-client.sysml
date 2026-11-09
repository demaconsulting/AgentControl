package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitRemoteClient {
        doc /* Runs git against a remote repository whose content and answers are untrusted: lists tags without a clone, fetches exactly one tag shallow, lists the entries and reads the raw blobs of named paths without a checkout, with command-scoped hardening (no hooks, only the transport of the address, no submodules or filters, no trace output), no prompts for background work, bounded time and output, fixed failure messages and classified failures; uses only legal git ref names for tags and refuses a git older than the minimum supported version. */

        metadata info : SoftwareInfo {
            description = "Runs git against a remote repository whose content and answers are untrusted: lists tags without a clone, fetches exactly one tag shallow, lists the entries and reads the raw blobs of named paths without a checkout, with command-scoped hardening (no hooks, only the transport of the address, no submodules or filters, no trace output), no prompts for background work, bounded time and output, fixed failure messages and classified failures; uses only legal git ref names for tags and refuses a git older than the minimum supported version.";
            sourceFiles = ("src/DemaConsulting.AgentControl/GitIntegration/GitRemoteClient.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/GitRemoteClientTests.cs", "test/DemaConsulting.AgentControl.Tests/GitIntegration/GitRemoteClientIntegrationTests.cs", "test/DemaConsulting.AgentControl.Tests/GitIntegration/GitRemoteStub.cs");
            designFiles = ("docs/design/agent-control/git-integration/git-remote-client.md");
            verificationFiles = ("docs/verification/agent-control/git-integration/git-remote-client.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration/git-remote-client.yaml");
        }
    }
}
