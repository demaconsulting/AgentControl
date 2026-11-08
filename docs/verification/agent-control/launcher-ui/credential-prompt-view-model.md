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
- The previous-credential-rejected warning is set when the request says the earlier credential for the server was
  rejected, and is clear on a first request.
- The "Remember on this computer (saved by your Git credential helper)" choice is unticked by default; it can be
  ticked only when the question says a credential helper can keep the sign-in, and is otherwise disabled with an
  explanation and never reflected in the answer; ticking it changes the note to say that the Git credential helper
  saves it and AgentControl does not store it, and the answer carries the choice.
- Signing in is possible only with a non-blank user name and a non-empty password.
- The view model answers exactly once: with the credential (user name trimmed, password untouched) and the remember
  choice, or with none.
- A null request is refused.

#### Test Scenarios

**CredentialPromptViewModel_ShowsDestination**: A view model for an `https` feed with a non-default port and
one for an `http` loopback feed show their origins, name the source in the explanation, flag only the `http`
one as insecure, and expose the session-only sentence. This scenario is tested by
`CredentialPromptViewModel_Constructor_ShowsOriginSourceAndSessionNote` (the session-only sentence is also
checked in the default state by
`CredentialPromptViewModel_Remember_TickedWhenAvailable_AnswersWithRememberAndChangesTheNote`), covering
`AgentControl-CredentialPromptViewModel-ShowsDestination`,
`AgentControl-CredentialPromptViewModel-InsecureHttpFlag` and
`AgentControl-CredentialPromptViewModel-SessionOnlyNote`. A null request is refused by
`CredentialPromptViewModel_Constructor_NullRequest_Throws`, a defensive test that no requirement cites.

**CredentialPromptViewModel_PreviousCredentialRejected**: A view model built from a request that says the earlier
credential for the server was rejected shows the rejected-credential warning, and one built from a first request
(no earlier credential rejected) does not. This scenario is tested by
`CredentialPromptViewModel_Constructor_PreviousCredentialRejected_ShowsWarningOnlyWhenRejected`, covering
`AgentControl-CredentialPromptViewModel-PreviousCredentialRejected`.

**CredentialPromptViewModel_RememberOption**: With a request that says a credential helper is available the choice
is enabled, unticked by default, with its enabled tooltip; ticking it changes the note to the saved-by-the-helper
sentence and signing in completes with the credential and the remember flag set. With a request that says no helper
is available the choice is shown disabled with the
explanatory tooltip, setting it has no effect, and signing in completes with the flag clear. This scenario is tested
by `CredentialPromptViewModel_Remember_TickedWhenAvailable_AnswersWithRememberAndChangesTheNote` and
`CredentialPromptViewModel_Remember_NoCredentialHelper_IsDisabledAndNeverRemembers`, covering
`AgentControl-CredentialPromptViewModel-RememberOption` (enabled and unticked by default),
`AgentControl-CredentialPromptViewModel-RememberUnavailable`,
`AgentControl-CredentialPromptViewModel-RememberNeverWithoutHelper`,
`AgentControl-CredentialPromptViewModel-RememberAnswer` and
`AgentControl-CredentialPromptViewModel-RememberNote`.

**CredentialPromptViewModel_SignIn**: Sign in is disabled with nothing entered, with only a user name and with
a blank user name; it is enabled with both. Signing in completes once with a credential whose user name is
trimmed and whose password, spaces included, is exactly as typed, and a later cancel or sign-in changes
nothing; cancelling completes with no credential and signing in before the form is complete does nothing. This
scenario is tested by `CredentialPromptViewModel_CanSignIn_NeedsUserNameAndPassword`,
`CredentialPromptViewModel_SignIn_EnteredCredential_CompletesOnceWithTrimmedUserName` and
`CredentialPromptViewModel_Cancel_CompletesWithNoCredential`, covering
`AgentControl-CredentialPromptViewModel-SignInRequiresBoth`, `AgentControl-CredentialPromptViewModel-SignIn`,
`AgentControl-CredentialPromptViewModel-PasswordVerbatim`, `AgentControl-CredentialPromptViewModel-Cancel` and
`AgentControl-CredentialPromptViewModel-AnswerOnce`.
