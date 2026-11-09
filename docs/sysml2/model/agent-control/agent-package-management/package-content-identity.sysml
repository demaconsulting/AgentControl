package AgentControl {
    part def PackageContentIdentity {
        doc /* Computes, formats and compares the content identity of an agent package (the SHA-256 of a zip or nupkg, or the commit of a Git tag), so a package version republished with different content can be told apart. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/PackageContentIdentity.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageContentIdentityTests.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/package-content-identity.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/package-content-identity.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/package-content-identity.yaml */
    }
}
