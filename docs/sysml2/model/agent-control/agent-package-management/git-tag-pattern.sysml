package AgentControl {
    part def GitTagPattern {
        doc /* Turns the tags of a Git repository into package versions: validates a tag pattern with one version placeholder, matches a tag name to a strict semantic version with case-sensitive literal parts, formats a tag from a version, derives the listing glob and vets tag names that a server reports. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/AgentPackageManagement/GitTagPattern.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitTagPatternTests.cs */
        comment designRef /* Design: docs/design/agent-control/agent-package-management/git-tag-pattern.md */
        comment verificationRef /* Verification: docs/verification/agent-control/agent-package-management/git-tag-pattern.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/agent-package-management/git-tag-pattern.yaml */
    }
}
