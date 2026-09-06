package AgentControl {
    part def LauncherUISubsystem {
        doc
        /*
         * Presentation layer: the recent-repos main window, per-repo cards, the
         * package-selection window, and the settings window. Also spans the Avalonia .axaml
         * Views and shared MVVM support types (RelayCommand, ViewModelBase,
         * FavoriteIconConverter), which have no dedicated test files and are folded into this
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
        part avalonia : Avalonia;
    }
}
