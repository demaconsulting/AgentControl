package AgentControl {
    part def SettingsWindowViewModel {
        doc /* Settings dialog view-model: package sources list (zip folder, NuGet folder, NuGet server and Git repository, with the Add menu commands and the refusal of unusable Git settings), git override, agent tool, shell preference. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/SettingsWindowViewModel.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/SettingsWindowViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/SettingsWindowServerSourceTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/SettingsWindowGitSourceTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/settings-window-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/settings-window-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/settings-window-view-model.yaml */
    }
}
