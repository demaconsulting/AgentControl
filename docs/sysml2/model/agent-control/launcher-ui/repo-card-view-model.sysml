package AgentControl {
    private import SoftwareItemMetadata::*;

    part def RepoCardViewModel {
        doc /* Per-repo card view-model: pin/git status, badges, launch/pull/upgrade/select-package/change-package/favorite/remove actions, the signature shield for a pinned source that requires signed packages, the refuse/confirm/adopt policy for a package version republished with different content (the confirmation is reached through Change package), the Git source presentation (icon, tag and short commit, the "Tag moved" warning that applies nothing, the git-too-old problem), the setup of Git package sources (user-started work may prompt, background work never), and the user-specific group and card color. */

        metadata info : SoftwareInfo {
            description = "Per-repo card view-model: pin/git status, badges, launch/pull/upgrade/select-package/change-package/favorite/remove actions, the signature shield for a pinned source that requires signed packages, the refuse/confirm/adopt policy for a package version republished with different content (the confirmation is reached through Change package), the Git source presentation (icon, tag and short commit, the 'Tag moved' warning that applies nothing, the git-too-old problem), the setup of Git package sources (user-started work may prompt, background work never), and the user-specific group and card color.";
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/RepoCardViewModel.cs", "src/DemaConsulting.AgentControl/LauncherUI/RepoCardStatus.cs", "src/DemaConsulting.AgentControl/LauncherUI/CardColorPalette.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardViewModelTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardServerSourceTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardSignatureTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardImageTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardColorTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardContentIdentityTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardGitSourceTests.cs", "test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardGitAwarenessTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/repo-card-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/repo-card-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/repo-card-view-model.yaml");
        }
    }
}
