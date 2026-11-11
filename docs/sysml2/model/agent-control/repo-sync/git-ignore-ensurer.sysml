package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitIgnoreEnsurer {
        doc /* Proactively ensures a repo's .gitignore covers the four managed agent folders via a marker-comment scan; additive-only, no git dependency. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/RepoSync/GitIgnoreEnsurer.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/RepoSync/GitIgnoreEnsurerTests.cs");
            designFiles = ("docs/design/agent-control/repo-sync/git-ignore-ensurer.md");
            verificationFiles = ("docs/verification/agent-control/repo-sync/git-ignore-ensurer.md");
            requirementFiles = ("docs/reqstream/agent-control/repo-sync/git-ignore-ensurer.yaml");
        }
    }
}
