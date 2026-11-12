package AgentControl {
    private import SoftwareItemMetadata::*;

    part def SelectPackageWindowViewModel {
        doc /* Package-selection dialog view-model: browse package names/versions and confirm a choice; for Change package it opens on the source, package and version a repo is pinned to. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/SelectPackageWindowViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/SelectPackageWindowViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/select-package-window-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/select-package-window-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/select-package-window-view-model.yaml");
        }
    }
}
