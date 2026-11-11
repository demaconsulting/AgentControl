package AgentControl {
    private import SoftwareItemMetadata::*;

    doc
    /*
     * AgentControl is a Windows desktop launcher application that keeps proprietary
     * agent-tool configuration (instructions, standards, review checklists) synchronized
     * alongside a developer's public git repos, and launches an agentic CLI tool
     * (e.g. GitHub Copilot CLI) against a chosen repo once its agent files are up to date.
     */

    part def AgentControlSystem {
        doc /* The AgentControl system as a whole. */

        // Note - test/DemaConsulting.AgentControl.UiTests/: all files
        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentControl.UiTests/");
            designFiles = ("docs/design/agent-control.md");
            verificationFiles = ("docs/verification/agent-control.md");
            requirementFiles = ("docs/reqstream/agent-control.yaml");
        }

        part program : Program;
        part launcherUi : LauncherUISubsystem;
        part agentPackageManagement : AgentPackageManagementSubsystem;
        part packageCredentials : PackageCredentialsSubsystem;
        part repoSync : RepoSyncSubsystem;
        part repoConfig : RepoConfigSubsystem;
        part agentToolLauncher : AgentToolLauncherSubsystem;
        part gitIntegration : GitIntegrationSubsystem;
        part settings : SettingsSubsystem;
        part logging : LoggingSubsystem;
        part startup : StartupSubsystem;
        part utilities : UtilitiesSubsystem;

        // Git repository source: the provider runs hardened git through the remote client; the editor validates the
        // address and the tag pattern; the content identity (of the agent files) the provider reports flows through the extractor to the
        // pin the card writes
        connect agentPackageManagement.gitSourceProvider to gitIntegration.gitRemoteClient;
        connect launcherUi.sourceEditorViewModel to agentPackageManagement.gitRepositoryAddress;
        connect launcherUi.sourceEditorViewModel to agentPackageManagement.gitTagPattern;
        connect repoSync.packageContentExtractor to agentPackageManagement.packageContentIdentity;
        connect launcherUi.repoCardViewModel to repoConfig.repoPinStore;
        // the pin names no source: the card asks the version cache what all configured sources offer and finds the one
        // that delivers the files with the recorded identity
        connect launcherUi.repoCardViewModel to agentPackageManagement.packageVersionCache;
    }
}
