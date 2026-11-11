package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitSourceProvider {
        doc /* Package source provider for one Git repository whose tags that match a pattern are the versions of a single package: lists versions without cloning, acquires one tag by a shallow fetch into a cache keyed by repository and commit, refuses links, submodules and case- or Unicode-colliding names in the package layout, never publishes a package whose .git folder could not be removed, optionally refuses a git that is too old, and reports the files identity as the content identity (the commit a tag points to is used only to keep a listing and its fetch consistent and is never recorded). */

        metadata info : SoftwareInfo {
            description = "Package source provider for one Git repository whose tags that match a pattern are the versions of a single package: lists versions without cloning, acquires one tag by a shallow fetch into a cache keyed by repository and commit, refuses links, submodules and case- or Unicode-colliding names in the package layout, never publishes a package whose .git folder could not be removed, optionally refuses a git that is too old, and reports the files identity as the content identity (the commit a tag points to is used only to keep a listing and its fetch consistent and is never recorded).";
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/GitSourceProvider.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitSourceProviderTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitSourceProviderIntegrationTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitRepositoryFixture.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/git-source-provider.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/git-source-provider.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/git-source-provider.yaml");
        }
    }
}
