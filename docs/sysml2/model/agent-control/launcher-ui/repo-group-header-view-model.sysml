package AgentControl {
    part def RepoGroupHeaderViewModel {
        doc /* Collapsible header row that starts a section of the main window's repo list (Favorites, a user group or Ungrouped): describes the section and its count and state, and reports toggle, rename and remove requests; RepoGroupKind identifies the kind of section. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/RepoGroupHeaderViewModel.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/RepoGroupHeaderViewModelTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/repo-group-header-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/repo-group-header-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/repo-group-header-view-model.yaml */
    }
}
