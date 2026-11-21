package AgentControl {
    private import SoftwareItemMetadata::*;

    part def LauncherUISubsystem {
        doc
        /*
         * Presentation layer: the main window with its user-ordered repo list (Move
         * items and drag and drop included, with the list scrolling while a drag is held near its
         * edges, over the app bar or below it), user-colored groups in the user's own order (header
         * menu and header drag), adding several repos at once (multi-select folder picker and
         * folders dropped from the file manager, Git repository roots only, checked off the UI
         * thread and saved once per batch, queued and cancelable, with a Retry/Discard question when
         * the save fails, one combined summary), the global progress strip under the app bar for
         * bulk work (adding repos, refreshing the cards after Settings are saved),
         * the About dialog with its banner, per-repo cards, the
         * package-selection window (opened as Select Package or, for a pinned repo, Change package),
         * the settings window, the package-source editor window (including its signed-package
         * options, its Git repository mode with tag pattern and tag preview, and "Forget saved
         * sign-in") and the sign-in
         * dialog (including its "Remember on this computer" option). Also spans the Avalonia
         * .axaml Views (including CredentialPromptWindow and GroupNameWindow) and shared MVVM
         * support types (RelayCommand, AsyncRelayCommand, ViewModelBase, PackageSourceIcons,
         * PackageSourceItemViewModel, SelectPackageSourceViewModel, AvaloniaCredentialPrompt,
         * ConnectionTestResultKind, RepoCardStatus, CardColorPalette, ThirdPartyDependencies,
         * BackgroundWorkRunner - the seam under the repo refresh queue that runs blocking file-system
         * and git reads off the UI thread - and the shared AppStyles.axaml theme resources). The pure display helpers - the value
         * converters (FavoriteIconConverter, AgentToolKindDisplayNameConverter,
         * ThemePreferenceDisplayNameConverter) and the PathEllipsisText and ColorPalettePanel
         * controls - are documented
         * as supporting types of this subsystem rather than as units of their own: each is a
         * presentational adapter with no behavior of its own beyond formatting or measuring for
         * the view, verified through the subsystem's requirements and its own test files
         * (AgentToolKindDisplayNameConverterTests, ThemePreferenceDisplayNameConverterTests,
         * PathEllipsisTextTests). The independently testable classes with security or
         * interaction behavior of their own are units: RepoImageLocator,
         * RepoGroupHeaderViewModel (with RepoGroupKind), GroupNameViewModel, DialogBehavior,
         * GlobalProgressViewModel and RepoRefreshQueue (the one queue, two reads at a time, every
         * lazy read of a repo's state goes through).
         */

        metadata info : SoftwareInfo {
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
        part globalProgressViewModel : GlobalProgressViewModel;
        part repoRefreshQueue : RepoRefreshQueue;
        part avalonia : Avalonia;
    }
}
