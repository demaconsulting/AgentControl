### CredentialPromptViewModel

#### Verification Approach

`CredentialPromptViewModel` is verified with unit tests in `CredentialPromptViewModelTests.cs`. The view model
is built from a real `CredentialPromptRequest` (a real `FeedOrigin`, a source alias and the rejected-before
flag) and driven through its properties and commands; nothing is mocked. The window that shows it and the
`AvaloniaCredentialPrompt` that marshals it onto the UI thread are verified one level up, with headless
Avalonia window tests, as part of the `LauncherUI` subsystem verification.

#### Test Environment

N/A - standard test environment (no window is created by these tests).

#### Acceptance Criteria

- The origin text is the server's scheme, host and port, shown for both a secure and an insecure server; only
  the insecure one is flagged; the source alias is named; the session-only sentence is exactly "Used for this
  session only - AgentControl does not save it."
- Signing in is possible only with a non-blank user name and a non-empty password.
- The view model answers exactly once: with the credential (user name trimmed, password untouched) or with
  none.
- A null request is refused.

#### Test Scenarios

**CredentialPromptViewModel_ShowsDestination**: A view model for an `https` feed with a non-default port and
one for an `http` loopback feed show their origins, name the source in the explanation, flag only the `http`
one as insecure, and expose the session-only sentence; a null request throws. This scenario is tested by
`CredentialPromptViewModel_Constructor_ShowsOriginSourceAndSessionNote` and
`CredentialPromptViewModel_Constructor_NullRequest_Throws`, covering
`AgentControl-CredentialPromptViewModel-ShowsDestination`.

**CredentialPromptViewModel_SignIn**: Sign in is disabled with nothing entered, with only a user name and with
a blank user name; it is enabled with both. Signing in completes once with a credential whose user name is
trimmed and whose password, spaces included, is exactly as typed, and a later cancel or sign-in changes
nothing; cancelling completes with no credential and signing in before the form is complete does nothing. This
scenario is tested by `CredentialPromptViewModel_CanSignIn_NeedsUserNameAndPassword`,
`CredentialPromptViewModel_SignIn_EnteredCredential_CompletesOnceWithTrimmedUserName` and
`CredentialPromptViewModel_Cancel_CompletesWithNoCredential`, covering
`AgentControl-CredentialPromptViewModel-SignIn`.
