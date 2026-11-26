package AgentControl_GitClient {
    private import AgentControl_BlockingCallGuard::*;
    private import SoftwareItemMetadata::*;

    part def GitClient {
        doc /* Invokes the configured git executable for status, pull, branch, and committed-files queries, and stops git tracking managed agent folders (index only); lists the local and remote-tracking branches a repo already knows about and switches a repo to one of them without ever forcing it, and clones a repository into a new folder (through a hidden working folder it renames into place on success, treating the address as data, removing after a canceled or failed clone only that working folder, never logging the credentials in an address); tells whether a folder is the top folder of a Git repository (a .git folder or file directly in it) without running git; every command runs under a time limit (a minute locally, ten minutes for a pull or a switch, an hour for a clone) and can be canceled, a stopped command being killed with every process it started. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/GitIntegration/GitClient.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/GitIntegration/GitClientTests.cs", "test/DemaConsulting.AgentControl.Tests/GitIntegration/GitClientRealRepositoryTests.cs", "test/DemaConsulting.AgentControl.Tests/GitIntegration/GitClientListLogger.cs", "test/DemaConsulting.AgentControl.Tests/GitIntegration/GitStub.cs");
            designFiles = ("docs/design/agent-control/git-integration/git-client.md");
            verificationFiles = ("docs/verification/agent-control/git-integration/git-client.md");
            requirementFiles = ("docs/reqstream/agent-control/git-integration/git-client.yaml");
        }
    }

    // Asks the blocking-call guard before every git process, so the tests catch a git call made on the UI thread
    dependency GitClient to BlockingCallGuard;
}
