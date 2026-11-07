package AgentControl {
    part def NuGetSourceProvider {
        doc /* Provider over a NuGet package folder or NuGet V3 server using NuGet.Caching with an AgentControl-owned per-source cache; signs in to a server through the session credentials, the user's saved sign-in and the user, enforces per-source signed-package requirements with an optional author-certificate pin (failing closed where signatures cannot be verified), and never reads or writes a repo NuGet configuration. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/NuGetSourceProvider.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderTests.cs; test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderServerTests.cs; test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderSignatureTests.cs; test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderSignedPackageTests.cs; test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/SignedNuGetFixture.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/nuget-source-provider.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/nuget-source-provider.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/nuget-source-provider.yaml */
    }
}
