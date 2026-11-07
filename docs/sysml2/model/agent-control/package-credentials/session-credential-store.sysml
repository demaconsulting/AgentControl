package AgentControl {
    part def SessionCredentialStore {
        doc /* In-memory store of the credentials the user entered (or found saved in their own credential store) for package servers, keyed by server origin; never persisted by AgentControl, cleared at exit, forgets an exact rejected credential, and shares one look-up of the user's credential store per server per session. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/PackageCredentials/SessionCredentialStore.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/PackageCredentials/SessionCredentialStoreTests.cs */
        comment designRef /* Design: docs/design/agent-control/package-credentials/session-credential-store.md */
        comment verificationRef /* Verification: docs/verification/agent-control/package-credentials/session-credential-store.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/package-credentials/session-credential-store.yaml */
    }
}
