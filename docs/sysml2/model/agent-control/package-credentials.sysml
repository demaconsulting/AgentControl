package AgentControl {
    part def PackageCredentialsSubsystem {
        doc
        /*
         * Handles the credentials for package servers: the server a credential belongs to,
         * the in-memory session store, the per-operation answer to a server's request for
         * credentials, and the log redaction that keeps secrets out of the application log.
         * ICredentialPrompt, CredentialPromptRequest and PackageCredentialContext have no
         * dedicated unit files and are folded into this subsystem-level description.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/PackageCredentials/PackageCredentialsSubsystemTests.cs */
        comment designRef /* Design: docs/design/agent-control/package-credentials.md */
        comment verificationRef /* Verification: docs/verification/agent-control/package-credentials.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/package-credentials.yaml */

        part feedOrigin : FeedOrigin;
        part sessionCredentialStore : SessionCredentialStore;
        part packageCredentialCallback : PackageCredentialCallback;
        part secretRedactingLogger : SecretRedactingLogger;
        part nuGetCaching : NuGetCaching;
    }
}
