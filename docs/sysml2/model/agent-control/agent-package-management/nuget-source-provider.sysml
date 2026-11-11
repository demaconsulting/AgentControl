package AgentControl {
    private import SoftwareItemMetadata::*;

    part def NuGetSourceProvider {
        doc /* Provider over a NuGet package folder or NuGet V3 server using NuGet.Caching with an AgentControl-owned per-source cache; signs in to a server through the session credentials, the user's saved sign-in and the user, enforces per-source signed-package requirements with an optional author-certificate pin (failing closed where signatures cannot be verified), and never reads or writes a repo NuGet configuration. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/NuGetSourceProvider.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderServerTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderSignatureTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderSignedPackageTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/SignedNuGetFixture.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/nuget-source-provider.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/nuget-source-provider.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/nuget-source-provider.yaml");
        }
    }
}
