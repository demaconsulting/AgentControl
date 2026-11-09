package AgentControl {
    part def SourceEditorViewModel {
        doc /* Package source editor dialog view-model (one class in two files: the general part and the Git part): validates and saves one zip-package folder, NuGet-package folder, NuGet-package server or Git repository source, tests a server's or a Git repository's connection (a Git test lists the repository's tags and previews which ones match the tag pattern), offers the signed-package requirement with an optional publisher fingerprint pin for NuGet sources, explains the Git sign-in, and forgets a server's saved sign-in after confirmation. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/SourceEditorViewModel.cs; src/DemaConsulting.AgentControl/LauncherUI/SourceEditorViewModel.Git.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorServerViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorSignatureViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorGitViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/FakeGitSourceProvider.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/source-editor-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/source-editor-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/source-editor-view-model.yaml */
    }
}
