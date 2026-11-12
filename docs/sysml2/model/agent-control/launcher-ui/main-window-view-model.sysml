package AgentControl {
    private import SoftwareItemMetadata::*;

    part def MainWindowViewModel {
        doc /* Main-window view-model: recent-repos collection, filter, sort, user-specific repo groups (collapsible sections), add/remove, settings, and the provider that the settings window's connection test uses (NuGet, or an interactive Git provider for a Git source). */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/MainWindowViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/MainWindowViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupingTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/main-window-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/main-window-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/main-window-view-model.yaml");
        }
    }
}
