package AgentControl {
    part def PackageVersionCache {
        doc /* Memoizes per-source, per-package version listings keyed by source alias and package id; invalidated per source. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/PackageVersionCache.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageVersionCacheTests.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/package-version-cache.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/package-version-cache.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/package-version-cache.yaml */
    }
}
