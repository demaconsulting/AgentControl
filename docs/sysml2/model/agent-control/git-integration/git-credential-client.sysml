package AgentControl {
    part def GitCredentialClient {
        doc /* Keeps, finds and removes a package feed's sign-in in the user's own Git credential helper by running the user's git executable (credential fill, approve and reject); never interactive, time-bounded, never logs a secret, and implements the PackageCredentials subsystem's IRememberedCredentialStore so AgentControl never stores a secret itself. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/GitIntegration/GitCredentialClient.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/GitIntegration/GitCredentialClientTests.cs; test/DemaConsulting.AgentControl.Tests/GitIntegration/GitCredentialClientIntegrationTests.cs; test/DemaConsulting.AgentControl.Tests/GitIntegration/GitCredentialStub.cs */
        comment designRef /* Design: docs/design/agent-control/git-integration/git-credential-client.md */
        comment verificationRef /* Verification: docs/verification/agent-control/git-integration/git-credential-client.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/git-integration/git-credential-client.yaml */
    }
}
