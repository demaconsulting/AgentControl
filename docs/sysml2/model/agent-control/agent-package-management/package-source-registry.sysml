package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageSourceRegistry {
        doc /* Maps source aliases to providers; an alias that is not configured is an error and never falls back to another source. The built-in providers it creates are wrapped in a decorator (BackgroundPackageSourceProvider, a supporting type) that runs every call on a worker thread and computes an acquired package's content identity there, so a caller on the UI thread never waits for a source. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageSourceRegistry.cs", "src/DemaConsulting.AgentControl/AgentPackageManagement/BackgroundPackageSourceProvider.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageSourceRegistryTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/BackgroundPackageSourceProviderTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-source-registry.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-source-registry.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-source-registry.yaml");
        }
    }
}
