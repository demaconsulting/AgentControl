package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SettingsWindowViewModel {
        doc /* Settings dialog view-model: package sources list (zip folder, NuGet folder, NuGet server and Git repository, with the Add menu commands and the refusal of unusable Git settings), git override, agent tool, shell preference. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/SettingsWindowViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/SettingsWindowViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/SettingsWindowServerSourceTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/SettingsWindowGitSourceTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/settings-window-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/settings-window-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/settings-window-view-model.yaml");
        }
    }
}
