package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageContentIdentity {
        doc /* Computes, formats and compares the content identity of an agent package: files-sha256 over the agent files the package delivers (independent of zip, nupkg or Git packaging), so a package version republished with different files (or a Git tag moved to other files) can be told apart and the same files from another kind of source recognized. */

        metadata info : SoftwareInfo {
            description = "Computes, formats and compares the content identity of an agent package: files-sha256 over the agent files the package delivers (independent of zip, nupkg or Git packaging), so a package version republished with different files (or a Git tag moved to other files) can be told apart and the same files from another kind of source recognized.";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageContentIdentity.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageContentIdentityTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-content-identity.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-content-identity.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-content-identity.yaml");
        }
    }
}
