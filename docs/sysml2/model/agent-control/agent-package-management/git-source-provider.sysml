package AgentControl {
    part def GitSourceProvider {
        doc /* Package source provider for one Git repository whose tags that match a pattern are the versions of a single package: lists versions without cloning, acquires one tag by a shallow fetch into a cache keyed by repository and commit, refuses links, submodules and case- or Unicode-colliding names in the package layout, never publishes a package whose .git folder could not be removed, optionally refuses a git that is too old, and reports the commit as the content identity. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/GitSourceProvider.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitSourceProviderTests.cs; test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitSourceProviderIntegrationTests.cs; test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitRepositoryFixture.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/git-source-provider.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/git-source-provider.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/git-source-provider.yaml */
    }
}
