package AgentControl {
    part def NuGetSourceProvider {
        doc /* Provider over a NuGet package folder using NuGet.Caching with an AgentControl-owned per-source cache; never reads or writes a repo NuGet configuration. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/NuGetSourceProvider.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/NuGetSourceProviderTests.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/nuget-source-provider.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/nuget-source-provider.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/nuget-source-provider.yaml */
    }
}
