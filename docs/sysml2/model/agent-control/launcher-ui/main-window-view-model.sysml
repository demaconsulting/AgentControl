package AgentControl {
    private import SoftwareItemMetadata::*;

    part def MainWindowViewModel {
        doc /* Main-window view-model: user-ordered repo collection (stable order; move to top/up/down/bottom and drag and drop within and between sections and onto section headers; the order is saved in the user's settings), filter (which turns reordering off), user-specific repo groups (collapsible sections), add/remove, settings, the periodic (every five minutes) background re-check of every card's upgrade availability, and the provider that the settings window's connection test uses (NuGet, or an interactive Git provider for a Git source). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/MainWindowViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupingTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoOrderingTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelRecheckTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/main-window-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/main-window-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/main-window-view-model.yaml");
        }
    }
}
