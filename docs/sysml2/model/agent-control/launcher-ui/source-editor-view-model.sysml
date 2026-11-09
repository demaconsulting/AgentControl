package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SourceEditorViewModel {
        doc /* Package source editor dialog view-model (one class in two files: the general part and the Git part): validates and saves one zip-package folder, NuGet-package folder, NuGet-package server or Git repository source, tests a server's or a Git repository's connection (a Git test lists the repository's tags and previews which ones match the tag pattern), offers the signed-package requirement with an optional publisher fingerprint pin for NuGet sources, explains the Git sign-in, and forgets a server's saved sign-in after confirmation. */

        metadata info : SoftwareInfo {
            description = "Package source editor dialog view-model (one class in two files: the general part and the Git part): validates and saves one zip-package folder, NuGet-package folder, NuGet-package server or Git repository source, tests a server's or a Git repository's connection (a Git test lists the repository's tags and previews which ones match the tag pattern), offers the signed-package requirement with an optional publisher fingerprint pin for NuGet sources, explains the Git sign-in, and forgets a server's saved sign-in after confirmation.";
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/SourceEditorViewModel.cs", "src/DemaConsulting.AgentControl/LauncherUI/SourceEditorViewModel.Git.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorServerViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorSignatureViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/SourceEditorGitViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/AgentPackageManagement/FakeGitSourceProvider.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/source-editor-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/source-editor-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/source-editor-view-model.yaml");
        }
    }
}
