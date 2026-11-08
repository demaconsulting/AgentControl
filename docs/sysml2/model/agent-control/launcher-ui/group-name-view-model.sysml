package AgentControl {
    part def GroupNameViewModel {
        doc /* View-model of the prompt that asks for a repo group's name (new group or rename): validates as the user types, lets only a usable name through, lets a new group join an existing one of the same name, and completes exactly once. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/GroupNameViewModel.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/GroupNameViewModelTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/group-name-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/group-name-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/group-name-view-model.yaml */
    }
}
