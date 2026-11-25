package AgentControl_MainWindowViewModel {
    private import AgentControl_GitClient::*;
    private import AgentControl_GlobalProgressViewModel::*;
    private import AgentControl_SettingsWriter::*;
    private import AgentControl_RepoRefreshQueue::*;
    private import AgentControl_RepoGroupHierarchy::*;
    private import SoftwareItemMetadata::*;

    part def MainWindowViewModel {
        doc /* Main-window view-model: user-ordered repo collection (stable order; move to top/up/down/bottom and drag and drop within and between sections and onto section headers; the order is saved in the user's settings), the selection of several cards in the order the list shows them (dropped together in one change and removed together in one change), filter (which turns reordering off), user-specific repo groups (collapsible sections, nested groups listed as a tree - each group's subgroups before its own repos - and created, moved, dropped into and out of each other, sorted by name, renamed and deleted (keeping what is in them, or together with their repos and groups), a color per group shown on its header and on the tiles of its uncolored repos, and the user's own order of the groups changed from the header menu or by dragging a header), add (one or several folders, from the picker or dropped from the file manager, only Git repository roots, checked in the background off the UI thread and saved once per batch - all or nothing, a confirmed save in the background - queued after an add that is running, cancelable before it saves, with a Retry/Discard question asked again each time the save fails, and one combined summary)/remove, every other change of the list applied at once and saved in the background (a failed save reported once per failure streak, the change kept and saved later, and what is not saved yet written when the window closes), settings (saved in the background without the window waiting, then the cards are refreshed in the background, with progress), a lazy start-up (the cards are built from the settings alone and their repo state is read in the background, the cards shown first, with progress), the progress of that bulk work in the global progress strip, background upgrade checks that start once a card's state was read and share one limit of four source reads, the periodic (every five minutes) background re-check of every card's upgrade availability, and the provider that the settings window's connection test uses (NuGet, or an interactive Git provider for a Git source). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/MainWindowViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupingTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupTreeTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupSortTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupDeleteTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoMultiSelectTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoOrderingTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelRecheckTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelPersistenceTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/main-window-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/main-window-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/main-window-view-model.yaml");
        }
    }

    // The add flows accept only Git repository roots, which GitClient decides
    dependency MainWindowViewModel to GitClient;

    // Adds, the cards' start-up reads and the card refresh after a settings save report their progress (and an add its Cancel) through the strip
    dependency MainWindowViewModel to GlobalProgressViewModel;

    // Every settings save goes through its background settings writer: the changes made in the window are scheduled, an add and the Settings window's values are confirmed saves, and the close's own write follows every confirmed write still running
    dependency MainWindowViewModel to SettingsWriter;

    // Owns the one repo refresh queue: the cards' start-up reads and an add's folder checks go through it, it is handed to every card, and its progress wave is shown in the strip
    dependency MainWindowViewModel to RepoRefreshQueue;

    // The group tree the list shows, the moves that keep it a tree and the order within a level are the group hierarchy's rules
    dependency MainWindowViewModel to RepoGroupHierarchy;
}
