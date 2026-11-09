package AgentControl {
    private import SoftwareItemMetadata::*;

    part def CredentialPromptViewModel {
        doc /* Sign-in dialog view-model: shows which server will receive the credential, collects the user name and password, offers an opt-in "Remember on this computer" choice (kept by the user's Git credential helper, disabled when none is available), and answers once with a credential and the choice, or none. */

        metadata info : SoftwareInfo {
            description = "Sign-in dialog view-model: shows which server will receive the credential, collects the user name and password, offers an opt-in 'Remember on this computer' choice (kept by the user's Git credential helper, disabled when none is available), and answers once with a credential and the choice, or none.";
            sourceFiles = ("src/DemaConsulting.AgentControl/LauncherUI/CredentialPromptViewModel.cs");
            testFiles = ("test/DemaConsulting.AgentControl.Tests/LauncherUI/CredentialPromptViewModelTests.cs");
            designFiles = ("docs/design/agent-control/launcher-ui/credential-prompt-view-model.md");
            verificationFiles = ("docs/verification/agent-control/launcher-ui/credential-prompt-view-model.md");
            requirementFiles = ("docs/reqstream/agent-control/launcher-ui/credential-prompt-view-model.yaml");
        }
    }
}
