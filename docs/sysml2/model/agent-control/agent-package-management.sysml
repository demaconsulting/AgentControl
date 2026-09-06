package AgentControl {
    part def AgentPackageManagementSubsystem {
        doc /* Package discovery, version parsing/comparison, and per-source scan caching. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/ */
        comment designRef /* Design: docs/design/agent-control/agent-package-management.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management.yaml */

        part packageSource : PackageSource;
        part packageVersion : PackageVersion;
        part packageVersionCache : PackageVersionCache;
    }
}
