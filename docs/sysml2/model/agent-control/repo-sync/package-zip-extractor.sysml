package AgentControl {
    part def PackageZipExtractor {
        doc /* Facade over the shared package content extractor for zip packages; extracts managed folders (blind delete-replace) and reads release notes without extracting. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/RepoSync/PackageZipExtractor.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/RepoSync/PackageZipExtractorTests.cs */
        comment designRef /* Design: docs/design/agent-control/repo-sync/package-zip-extractor.md */
        comment verificationRef /* Verification: docs/verification/agent-control/repo-sync/package-zip-extractor.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/repo-sync/package-zip-extractor.yaml */
    }
}
