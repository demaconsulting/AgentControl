package AgentControl {
    private import SoftwareItemMetadata::*;

    part def LauncherUISubsystem {
        doc
        /*
         * Presentation layer: the recent-repos main window, per-repo cards, the
         * package-selection window (opened as Select Package or, for a pinned repo, Change package),
         * the settings window, the package-source editor window (including its signed-package
         * options, its Git repository mode with tag pattern and tag preview, and "Forget saved
         * sign-in") and the sign-in
         * dialog (including its "Remember on this computer" option). Also spans the Avalonia
         * .axaml Views (including CredentialPromptWindow and GroupNameWindow) and shared MVVM
         * support types (RelayCommand, AsyncRelayCommand, ViewModelBase, PackageSourceIcons,
         * PackageSourceItemViewModel, SelectPackageSourceViewModel, AvaloniaCredentialPrompt,
         * ConnectionTestResultKind, RepoCardStatus, CardColorPalette, ThirdPartyDependencies and
         * the shared AppStyles.axaml theme resources). The pure display helpers - the value
         * converters (FavoriteIconConverter, AgentToolKindDisplayNameConverter,
         * ThemePreferenceDisplayNameConverter) and the PathEllipsisText control - are documented
         * as supporting types of this subsystem rather than as units of their own: each is a
         * presentational adapter with no behavior of its own beyond formatting or measuring for
         * the view, verified through the subsystem's requirements and its own test files
         * (AgentToolKindDisplayNameConverterTests, ThemePreferenceDisplayNameConverterTests,
         * PathEllipsisTextTests). The independently testable classes with security or
         * interaction behavior of their own are units: RepoImageLocator,
         * RepoGroupHeaderViewModel (with RepoGroupKind), GroupNameViewModel and DialogBehavior.
         */

        metadata info : SoftwareInfo {
            description = "Presentation layer: the recent-repos main window, per-repo cards, the package-selection window (opened as Select Package or, for a pinned repo, Change package), the settings window, the package-source editor window (including its signed-package options, its Git repository mode with tag pattern and tag preview, and 'Forget saved sign-in') and the sign-in dialog (including its 'Remember on this computer' option). Also spans the Avalonia .axaml Views (including CredentialPromptWindow and GroupNameWindow) and shared MVVM support types (RelayCommand, AsyncRelayCommand, ViewModelBase, PackageSourceIcons, PackageSourceItemViewModel, SelectPackageSourceViewModel, AvaloniaCredentialPrompt, ConnectionTestResultKind, RepoCardStatus, CardColorPalette, ThirdPartyDependencies and the shared AppStyles.axaml theme resources). The pure display helpers - the value converters (FavoriteIconConverter, AgentToolKindDisplayNameConverter, ThemePreferenceDisplayNameConverter) and the PathEllipsisText control - are documented as supporting types of this subsystem rather than as units of their own: each is a presentational adapter with no behavior of its own beyond formatting or measuring for the view, verified through the subsystem's requirements and its own test files (AgentToolKindDisplayNameConverterTests, ThemePreferenceDisplayNameConverterTests, PathEllipsisTextTests). The independently testable classes with security or interaction behavior of their own are units: RepoImageLocator, RepoGroupHeaderViewModel (with RepoGroupKind), GroupNameViewModel and DialogBehavior.";
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/");
            designFiles = ("docs/design/agent-control/launcher-ui.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui.yaml");
        }

        part mainWindowViewModel : MainWindowViewModel;
        part repoCardViewModel : RepoCardViewModel;
        part selectPackageWindowViewModel : SelectPackageWindowViewModel;
        part settingsWindowViewModel : SettingsWindowViewModel;
        part sourceEditorViewModel : SourceEditorViewModel;
        part credentialPromptViewModel : CredentialPromptViewModel;
        part repoImageLocator : RepoImageLocator;
        part repoGroupHeaderViewModel : RepoGroupHeaderViewModel;
        part groupNameViewModel : GroupNameViewModel;
        part dialogBehavior : DialogBehavior;
        part avalonia : Avalonia;
    }
}
