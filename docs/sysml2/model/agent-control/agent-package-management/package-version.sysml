package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageVersion {
        doc /* Parses and compares release/prerelease version strings. */

        metadata info : SoftwareInfo {
            description = "Parses and compares release/prerelease version strings.";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageVersion.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageVersionTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-version.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-version.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-version.yaml");
        }
    }
}
