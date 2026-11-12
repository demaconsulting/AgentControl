package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageVersionCache {
        doc /* Caches per-source, per-package version listings keyed by source alias and package id; entries expire after five minutes and can be invalidated per source. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageVersionCache.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageVersionCacheTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-version-cache.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-version-cache.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-version-cache.yaml");
        }
    }
}
