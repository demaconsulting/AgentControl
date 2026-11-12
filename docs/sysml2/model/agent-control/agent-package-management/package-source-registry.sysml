package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageSourceRegistry {
        doc /* Maps source aliases to providers; an alias that is not configured is an error and never falls back to another source. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageSourceRegistry.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageSourceRegistryTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-source-registry.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-source-registry.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-source-registry.yaml");
        }
    }
}
