package AgentControl {
    private import SoftwareItemMetadata::*;

    part def MainWindowViewModel {
        doc /* Main-window view-model: user-ordered repo collection (stable order; move to top/up/down/bottom and drag and drop within and between sections and onto section headers; the order is saved in the user's settings), filter (which turns reordering off), user-specific repo groups (collapsible sections, a color per group shown on its header and on the tiles of its uncolored repos, and the user's own order of the groups changed from the header menu or by dragging a header), add (one or several folders, from the picker or dropped from the file manager, only Git repository roots, checked in the background off the UI thread and saved once per batch - all or nothing - queued after an add that is running, cancelable before it saves, with a Retry/Discard question when the save fails, and one combined summary)/remove, settings (the cards are refreshed in the background after a save, with progress), the progress of that bulk work in the global progress strip, the periodic (every five minutes) background re-check of every card's upgrade availability, and the provider that the settings window's connection test uses (NuGet, or an interactive Git provider for a Git source). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/MainWindowViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupingTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoOrderingTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelRecheckTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelPersistenceTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/main-window-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/main-window-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/main-window-view-model.yaml");
        }
    }

    // The add flows accept only Git repository roots, which GitClient decides
    dependency MainWindowViewModel to GitClient;

    // Adds and the card refresh after a settings save report their progress (and an add its Cancel) through the strip
    dependency MainWindowViewModel to GlobalProgressViewModel;
}
