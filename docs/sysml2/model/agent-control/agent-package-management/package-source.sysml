package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageSource {
        doc /* Scans a package source folder to enumerate packages/versions and detect upgrades. */

        metadata info : SoftwareInfo {
            description = "Scans a package source folder to enumerate packages/versions and detect upgrades.";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageSource.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageSourceTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-source.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-source.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-source.yaml");
        }
    }
}
