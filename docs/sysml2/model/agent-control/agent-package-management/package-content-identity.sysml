package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageContentIdentity {
        doc /* Computes, formats and compares the content identity of an agent package (the SHA-256 of a zip or nupkg, or the commit of a Git tag), so a package version republished with different content can be told apart. */

        metadata info : SoftwareInfo {
            description = "Computes, formats and compares the content identity of an agent package (the SHA-256 of a zip or nupkg, or the commit of a Git tag), so a package version republished with different content can be told apart.";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/PackageContentIdentity.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageContentIdentityTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/package-content-identity.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/package-content-identity.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/package-content-identity.yaml");
        }
    }
}
