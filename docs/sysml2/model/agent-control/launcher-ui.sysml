package AgentControl {
    part def LauncherUISubsystem {
        doc
        /*
         * Presentation layer: the recent-repos main window, per-repo cards, the
         * package-selection window, the settings window, the package-source editor window
         * (including its signed-package options and "Forget saved sign-in") and the sign-in
         * dialog (including its "Remember on this computer" option). Also spans the Avalonia
         * .axaml Views (including
         * CredentialPromptWindow) and shared MVVM support types (RelayCommand,
         * AsyncRelayCommand, ViewModelBase, PackageSourceIcons, PackageSourceItemViewModel,
         * SelectPackageSourceViewModel, AvaloniaCredentialPrompt, ConnectionTestResultKind,
         * FavoriteIconConverter, FavoriteColorConverter), which are folded into this
         * subsystem-level description rather than given separate unit files.
         */

        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/ */
        comment designRef /* Design: docs/design/agent-control/launcher-ui.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui.yaml */

        part mainWindowViewModel : MainWindowViewModel;
        part repoCardViewModel : RepoCardViewModel;
        part selectPackageWindowViewModel : SelectPackageWindowViewModel;
        part settingsWindowViewModel : SettingsWindowViewModel;
        part sourceEditorViewModel : SourceEditorViewModel;
        part credentialPromptViewModel : CredentialPromptViewModel;
        part avalonia : Avalonia;
    }
}
