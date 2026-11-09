package AgentControl {
    doc
    /*
     * AgentControl is a Windows desktop launcher application that keeps proprietary
     * agent-tool configuration (instructions, standards, review checklists) synchronized
     * alongside a developer's public git repos, and launches an agentic CLI tool
     * (e.g. GitHub Copilot CLI) against a chosen repo once its agent files are up to date.
     */

    part def AgentControlSystem {
        doc /* The AgentControl system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentControl.UiTests/ (all files) */
        comment designRef /* Design: docs/design/agent-control.md */
        comment verificationRef /* Verification: docs/verification/agent-control.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control.yaml */

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
        // address and the tag pattern; the content identity the provider reports flows through the extractor to the
        // pin the card writes
        connect agentPackageManagement.gitSourceProvider to gitIntegration.gitRemoteClient;
        connect launcherUi.sourceEditorViewModel to agentPackageManagement.gitRepositoryAddress;
        connect launcherUi.sourceEditorViewModel to agentPackageManagement.gitTagPattern;
        connect repoSync.packageContentExtractor to agentPackageManagement.packageContentIdentity;
        connect launcherUi.repoCardViewModel to repoConfig.repoPinStore;
    }
}
