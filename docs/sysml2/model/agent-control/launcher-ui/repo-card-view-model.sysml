package AgentControl {
    part def RepoCardViewModel {
        doc /* Per-repo card view-model: pin/git status, badges, launch/pull/upgrade/select-package/favorite/remove actions, the signature shield for a pinned source that requires signed packages, the refuse/confirm/adopt policy for a package version republished with different content, the setup of Git package sources (user-started work may prompt, background work never), and the user-specific group and card color. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/RepoCardViewModel.cs; src/DemaConsulting.AgentControl/LauncherUI/RepoCardStatus.cs; src/DemaConsulting.AgentControl/LauncherUI/CardColorPalette.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardServerSourceTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardSignatureTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardImageTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardColorTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardContentIdentityTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardGitSourceTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/repo-card-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/repo-card-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/repo-card-view-model.yaml */
    }
}
