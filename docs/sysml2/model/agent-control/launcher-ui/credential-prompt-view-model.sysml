package AgentControl {
    part def CredentialPromptViewModel {
        doc /* Sign-in dialog view-model: shows which server will receive the credential, collects the user name and password, offers an opt-in "Remember on this computer" choice (kept by the user's Git credential helper, disabled when none is available), and answers once with a credential and the choice, or none. */

        comment sourceRef /* Source: src/DemaConsulting.AgentControl/LauncherUI/CredentialPromptViewModel.cs */
        comment testRef /* Test: test/DemaConsulting.AgentControl.Tests/LauncherUI/CredentialPromptViewModelTests.cs */
        comment designRef /* Design: docs/design/agent-control/launcher-ui/credential-prompt-view-model.md */
        comment verificationRef /* Verification: docs/verification/agent-control/launcher-ui/credential-prompt-view-model.md */
        comment reqRef /* Requirements: docs/reqstream/agent-control/launcher-ui/credential-prompt-view-model.yaml */
    }
}
