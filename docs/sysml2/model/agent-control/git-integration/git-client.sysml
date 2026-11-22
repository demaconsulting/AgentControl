package AgentControl {
    private import SoftwareItemMetadata::*;

    part def GitClient {
        doc /* Invokes the configured git executable for status, pull, branch, and committed-files queries, and stops git tracking managed agent folders (index only); tells whether a folder is the top folder of a Git repository (a .git folder or file directly in it) without running git; every command runs under a time limit (a minute locally, ten minutes for a pull) and can be canceled, a stopped command being killed with every process it started. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/GitIntegration/GitClient.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/GitClientTests.cs");
            designFiles = ("docs/design/agent-control/git-integration/git-client.md");
            verificationFiles = ("docs/verification/agent-control/git-integration/git-client.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration/git-client.yaml");
        }
    }

    // Asks the blocking-call guard before every git process, so the tests catch a git call made on the UI thread
    dependency GitClient to BlockingCallGuard;
}
