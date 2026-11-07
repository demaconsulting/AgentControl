package AgentControl {
    part def SessionCredentialStore {
        doc /* In-memory store of the credentials the user entered for package servers, keyed by server origin; never persisted and cleared at exit. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/PackageCredentials/SessionCredentialStore.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/PackageCredentials/SessionCredentialStoreTests.cs */
        comment designRef /* Design: docs/design/agent-control/package-credentials/session-credential-store.md */
        comment verificationRef /* Verification: docs/verification/agent-control/package-credentials/session-credential-store.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/package-credentials/session-credential-store.yaml */
    }
}
