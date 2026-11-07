package AgentControl {
    part def AgentPackageManagementSubsystem {
        doc /* Package source configuration model, per-source providers (zip folder, NuGet folder and server), alias-to-provider resolution, version parsing/comparison, and per-source version caching. */

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
        part nuGetCaching : NuGetCaching;
    }
}
