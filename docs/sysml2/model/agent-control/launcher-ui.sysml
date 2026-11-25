package AgentControl_LauncherUISubsystem {
    public import AgentControl_MainWindowViewModel::*;
    public import AgentControl_RepoCardViewModel::*;
    public import AgentControl_SelectPackageWindowViewModel::*;
    public import AgentControl_SettingsWindowViewModel::*;
    public import AgentControl_SourceEditorViewModel::*;
    public import AgentControl_CredentialPromptViewModel::*;
    public import AgentControl_RepoImageLocator::*;
    public import AgentControl_RepoGroupHeaderViewModel::*;
    public import AgentControl_GroupNameViewModel::*;
    public import AgentControl_SwitchBranchViewModel::*;
    public import AgentControl_DialogBehavior::*;
    public import AgentControl_GlobalProgressViewModel::*;
    public import AgentControl_RepoRefreshQueue::*;
    private import OtsDependencies::*;
    private import SoftwareItemMetadata::*;

    part def LauncherUISubsystem {
        doc
        /*
         * Presentation layer: the main window with its user-ordered repo list (Move
         * items and drag and drop included, with the list scrolling while a drag is held near its
         * edges, over the app bar or below it), user-colored groups in the user's own order, nested to
         * any depth as a collapsible indented tree (header menu and header drag: new subgroup, move up
         * and down, move into another group or to the top level, sort by name, the two deletes -
         * keeping or together with the contents - and drops of cards and headers), several repo
         * cards selected at once (Ctrl+click, Shift+click, Ctrl+A) to drag, move to a group (from
         * the card's menu too, without dragging) or
         * remove together,
         * adding several repos at once (multi-select folder picker and
         * folders dropped from the file manager, Git repository roots only, checked off the UI
         * thread and saved once per batch, queued and cancelable, with a Retry/Discard question when
         * the save fails, one combined summary), the global progress strip under the app bar for
         * bulk work (adding repos, reading the cards at start-up, refreshing the cards after
         * Settings are saved),
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
         * ThemePreferenceDisplayNameConverter, GroupIndentConverter) and the PathEllipsisText,
         * ColorPalettePanel and RepoListBox
         * (the repo list with multiple selection) controls - are documented
         * as supporting types of this subsystem rather than as units of their own: each is a
         * presentational adapter with no behavior of its own beyond formatting or measuring for
         * the view, verified through the subsystem's requirements and its own test files
         * (AgentToolKindDisplayNameConverterTests, ThemePreferenceDisplayNameConverterTests,
         * PathEllipsisTextTests). The independently testable classes with security or
         * interaction behavior of their own are units: RepoImageLocator,
         * RepoGroupHeaderViewModel (with RepoGroupKind), GroupNameViewModel, SwitchBranchViewModel (the
         * branch dialog opened from a card's menu, with its SwitchBranchWindow view), DialogBehavior,
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
        part switchBranchViewModel : SwitchBranchViewModel;
        part dialogBehavior : DialogBehavior;
        part globalProgressViewModel : GlobalProgressViewModel;
        part repoRefreshQueue : RepoRefreshQueue;
        part avalonia : Avalonia;
    }
}
