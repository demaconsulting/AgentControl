package AgentControl {
    part def RepoCardViewModel {
        doc /* Per-repo card view-model: pin/git status, badges, launch/pull/upgrade/select-package/favorite/remove actions, and the signature shield for a pinned source that requires signed packages. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/RepoCardViewModel.cs; src/DemaConsulting.AgentControl/LauncherUI/RepoCardStatus.cs; src/DemaConsulting.AgentControl/LauncherUI/RepoImageLocator.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardViewModelTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardServerSourceTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardSignatureTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoImageLocatorTests.cs; test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoCardImageTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/repo-card-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/repo-card-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/repo-card-view-model.yaml */
    }
}
