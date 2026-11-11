package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageZipExtractor {
        doc /* Facade over the shared package content extractor for zip packages; extracts managed folders (blind delete-replace) and reads release notes without extracting. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/RepoSync/PackageZipExtractor.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoSync/PackageZipExtractorTests.cs");
            designFiles = ("docs/design/agent-control/repo-sync/package-zip-extractor.md");
            verificationFiles = ("docs/verification/agent-control/repo-sync/package-zip-extractor.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-sync/package-zip-extractor.yaml");
        }
    }
}
