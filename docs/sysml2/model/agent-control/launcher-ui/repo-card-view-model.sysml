package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoCardViewModel {
        doc /* Per-repo card view-model: pin/git status, badges, launch/pull/upgrade/select-package/change-package/favorite/remove actions, the signature shield for the source that supplies the package when it requires signed packages, the refuse/confirm/adopt policy for a package version republished with different files (the confirmation is reached through Change package), the portable pin (the pin names no source: the user's own configured sources are asked which offer the pinned package and version, the first whose files have the recorded identity supplies it - whatever its alias or kind - and is named on the package chip, a newer version is still offered when none does, and a pin without a recorded identity is never refused), the Git source presentation (icon and tag, the git-too-old problem, a Refresh that re-reads Git sources so new tags appear), the setup of Git package sources (user-started work may prompt, background work never), and the confirmation and conversion that takes committed agent folders out of git before a package is applied, and the user-specific group and card color. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/RepoCardViewModel.cs", "src/DemaConsulting.AgentControl/LauncherUI/RepoCardStatus.cs", "src/DemaConsulting.AgentControl/LauncherUI/CardColorPalette.cs", "src/DemaConsulting.AgentControl/LauncherUI/CommittedFilesConversionRequest.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardServerSourceTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardSignatureTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardImageTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardColorTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardContentIdentityTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardGitSourceTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardGitAwarenessTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardPortablePinTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardCommittedFilesTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/repo-card-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/repo-card-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/repo-card-view-model.yaml");
        }
    }
}
