package AgentControl {
    part def PackageSourceRegistry {
        doc /* Maps source aliases to providers; an alias that is not configured is an error and never falls back to another source. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/PackageSourceRegistry.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/PackageSourceRegistryTests.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/package-source-registry.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/package-source-registry.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/package-source-registry.yaml */
    }
}
