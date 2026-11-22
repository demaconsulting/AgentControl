package AgentControl {
    private import SoftwareItemMetadata::*;

    part def PackageContentExtractor {
        doc /* Shared extractor applying a package content reader (zip or directory) to a repo with delete-then-copy semantics and path containment, and telling whether any agent file is installed in a repo's four managed folders (the reader that presents those folders as a package, so the installed files can be compared with a package's, lives with it). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/RepoSync/PackageContentExtractor.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoSync/PackageContentExtractorTests.cs");
            designFiles = ("docs/design/agent-control/repo-sync/package-content-extractor.md");
            verificationFiles = ("docs/verification/agent-control/repo-sync/package-content-extractor.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-sync/package-content-extractor.yaml");
        }
    }

    // Asks the blocking-call guard before every extraction, so the tests catch one made on the UI thread
    dependency PackageContentExtractor to BlockingCallGuard;
}
