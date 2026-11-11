package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitTagPattern {
        doc /* Turns the tags of a Git repository into package versions: validates a tag pattern with one version placeholder, matches a tag name to a strict semantic version with case-sensitive literal parts, formats a tag from a version, derives the listing glob and vets tag names that a server reports. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/AgentPackageManagement/GitTagPattern.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/GitTagPatternTests.cs");
            designFiles = ("docs/design/agent-control/agent-package-management/git-tag-pattern.md");
            verificationFiles = ("docs/verification/agent-control/agent-package-management/git-tag-pattern.md");
            requirementFiles = ("docs/reqstream/agent-control/agent-package-management/git-tag-pattern.yaml");
        }
    }
}
