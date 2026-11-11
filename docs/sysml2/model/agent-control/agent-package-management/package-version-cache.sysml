package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageVersionCache {
        doc /* Memoizes per-source, per-package version listings keyed by source alias and package id; invalidated per source. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageVersionCache.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageVersionCacheTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-version-cache.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-version-cache.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-version-cache.yaml");
        }
    }
}
