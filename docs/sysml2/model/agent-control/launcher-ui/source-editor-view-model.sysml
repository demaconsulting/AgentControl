package AgentControl {
    part def SourceEditorViewModel {
        doc /* Package source editor dialog view-model: validates and saves one zip-package folder, NuGet-package folder or NuGet-package server source, tests a server's connection, offers the signed-package requirement with an optional publisher fingerprint pin for NuGet sources, and forgets a server's saved sign-in after confirmation. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/SourceEditorViewModel.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorServerViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorSignatureViewModelTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/source-editor-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/source-editor-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/source-editor-view-model.yaml */
    }
}
