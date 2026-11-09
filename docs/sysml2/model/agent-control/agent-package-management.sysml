package AgentControl {
    part def AgentPackageManagementSubsystem {
        doc /* Package source configuration model, per-source providers (zip folder, NuGet folder and server), alias-to-provider resolution, version parsing/comparison, per-source version caching, per-source signed-package enforcement with an optional publisher pin, the content identity of delivered packages, and the platform check for signature verification. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/ */
        comment designRef /* Design: docs/design/agent-control/agent-package-management.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management.yaml */

        part packageSource : PackageSource;
        part packageVersion : PackageVersion;
        part packageVersionCache : PackageVersionCache;
        part packageSourceRegistry : PackageSourceRegistry;
        part zipFolderSourceProvider : ZipFolderSourceProvider;
        part nuGetSourceProvider : NuGetSourceProvider;
        part signatureVerificationSupport : SignatureVerificationSupport;
        part packageContentIdentity : PackageContentIdentity;
        part nuGetCaching : NuGetCaching;
    }
}
