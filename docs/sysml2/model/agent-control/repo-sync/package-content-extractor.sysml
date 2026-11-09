package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageContentExtractor {
        doc /* Shared extractor applying a package content reader (zip or directory) to a repo with delete-then-copy semantics and path containment. */

        metadata info : SoftwareInfo {
            description = "Shared extractor applying a package content reader (zip or directory) to a repo with delete-then-copy semantics and path containment.";
            sourceFiles = ("src/DemaConsulting.AgentControl/RepoSync/PackageContentExtractor.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoSync/PackageContentExtractorTests.cs");
            designFiles = ("docs/design/agent-control/repo-sync/package-content-extractor.md");
            verificationFiles = ("docs/verification/agent-control/repo-sync/package-content-extractor.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-sync/package-content-extractor.yaml");
        }
    }
}
