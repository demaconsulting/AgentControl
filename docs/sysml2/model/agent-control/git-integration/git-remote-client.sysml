package AgentControl {
    part def GitRemoteClient {
        doc /* Runs git against a remote repository whose content and answers are untrusted: lists tags without a clone, fetches exactly one tag shallow, reads and checks out named paths, with command-scoped hardening (no hooks, only the transport of the address, no submodules or filters, no trace output), no prompts for background work, bounded time and output, redacted messages and classified failures. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/GitIntegration/GitRemoteClient.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/GitIntegration/GitRemoteClientTests.cs; test/DemaConsulting.AgentControl.Tests/GitIntegration/GitRemoteClientIntegrationTests.cs; test/DemaConsulting.AgentControl.Tests/GitIntegration/GitRemoteStub.cs */
        comment designRef /* Design: docs/design/agent-control/git-integration/git-remote-client.md */
        comment verificationRef /* Verification: docs/verification/agent-control/git-integration/git-remote-client.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/git-integration/git-remote-client.yaml */
    }
}
