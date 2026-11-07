## PackageCredentials

### Verification Approach

The `PackageCredentials` subsystem is verified by integration tests defined in
`PackageCredentialsSubsystemTests.cs`. They drive the units together the way the NuGet library does - a
`PackageCredentialCallback` for one operation against one server, a `SessionCredentialStore`, a
`FeedOrigin`, and a `SecretRedactingLogger` over a recording logger - without a server or a user interface.
The user is replaced by `FakeCredentialPrompt`, a scripted `ICredentialPrompt` that answers from a queue,
records every question and can instead wait forever; logging is observed through `ListLogger`, a logger that
records the text it receives. The tests use a sentinel password so that any appearance of the secret in a
store listing or a log line is detected. The server side (a real NuGet client challenging a real HTTP server)
and the sign-in dialog are verified at the `AgentPackageManagement` and `LauncherUI` levels, so nothing here
needs mocking beyond the prompt, the user's credential store and the logger. The user's own credential store (their
Git credential helper) is replaced by `FakeRememberedCredentialStore`, a scripted `IRememberedCredentialStore` that
holds saved credentials in a dictionary, records every look-up, save and removal, and can be told to refuse, fail
or report that no helper is available; the real `git credential` protocol against the real helper is verified at the
`GitIntegration` level (`GitCredentialClientTests.cs` and `GitCredentialClientIntegrationTests.cs`).

### Test Environment

N/A - standard test environment. The tests use no network, no files and no user interface.

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A credential typed during one operation is remembered only after that operation succeeded, and is then used
  by the next operation on the same server without asking again; a credential the server rejected is
  forgotten and the next operation asks again (and says the last one failed).
- A credential remembered for one server is never offered for another origin, and a challenge from a foreign
  host never opens the prompt, even while the user is being asked for the server's own credential.
- No password, authorization header value or web-address query string reaches the log, whichever of the
  units wrote it.
- A credential is handed to the user's own credential store only when the user chose to remember it and the
  operation succeeded; a later session finds it there and uses it without asking, also when it may not ask; and
  forgetting the saved sign-in makes the next session ask again.
- Forgetting a saved sign-in reports whether nothing was saved, it was removed, or the credential store could not
  remove it, and a context without a prompt keeps the user's credential store.

### Test Scenarios

**PackageCredentials_TwoOperations_FirstPromptsAndSecondReusesTheSessionCredential**: The first operation's
callback asks the user and returns the typed credential while the store is still empty; after `Commit`, the
second operation's callback answers for another address of the same server from the store, the prompt having
been asked exactly once and the second callback never having prompted. This scenario is tested by
`PackageCredentials_TwoOperations_FirstPromptsAndSecondReusesTheSessionCredential`, covering
`AgentControl-PackageCredentials-SessionOnly` and `AgentControl-PackageCredentials-AskOncePerOperation`.

**PackageCredentials_RejectedCredential_IsForgottenAndTheNextOperationAsksAgain**: A remembered credential is
offered by a silent callback and then rejected (`Reject`); the next, prompting callback asks the user, tells
the prompt that a credential was just rejected, and after `Commit` the store holds the new credential. This
scenario is tested by `PackageCredentials_RejectedCredential_IsForgottenAndTheNextOperationAsksAgain`,
covering `AgentControl-PackageCredentials-SessionOnly` and
`AgentControl-PackageCredentials-AskOncePerOperation`.

**PackageCredentials_CredentialForOneFeed_IsNeverOfferedToAnotherOrigin**: A credential is remembered for one
server; a callback for a different server finds nothing and may not ask, and a callback for the first server
challenged by a foreign host answers no credential and never opens the prompt. This scenario is tested by
`PackageCredentials_CredentialForOneFeed_IsNeverOfferedToAnotherOrigin`, covering
`AgentControl-PackageCredentials-ScopedToFeedOrigin`.

**PackageCredentials_SignInThroughCallbackAndRedactingLogger_NeverLogsASecret**: A sign-in is performed with a
logger-carrying callback over the redacting logger, followed by log lines that carry a signed address and an
`Authorization: Basic` value for the same password; every line the recording logger received is searched for
the password and for its Base64 form, and none contains either. This scenario is tested by
`PackageCredentials_SignInThroughCallbackAndRedactingLogger_NeverLogsASecret`, covering
`AgentControl-PackageCredentials-SecretsNeverLogged`.

**PackageCredentials_RememberOnRequest_SavedByTheUsersStoreAndFoundInALaterSession**: The user ticks "Remember" and
signs in; after the operation succeeds the credential is in the (fake) user's credential store, the session store
marks it as saved; a later session with a fresh session store finds it in the credential store, without asking, even
for a silent callback; forgetting the saved sign-in makes the next session ask again. A context derived with
`WithoutPrompt` keeps the same session store and the same credential store, because reading a saved sign-in needs no
dialog. This scenario is tested by
`PackageCredentials_RememberLifeCycle_SavedByTheUsersStoreAndFoundInALaterSession` and
`PackageCredentialContext_WithoutPrompt_KeepsStoreAndTheUsersCredentialStore`, covering
`AgentControl-PackageCredentials-RememberOnRequest`.

**PackageCredentials_ForgetSavedSignIn_ReportsWhatWasDone**: Forgetting reports "nothing saved" when neither the
session nor the credential store holds a sign-in, "removed" when the credential store held one and removed it (and
removes only that origin's), "removed" when only the session's copy existed, "failed" when the credential store
refuses, and "nothing saved" without a credential store. This scenario is tested by
`PackageCredentialContext_ForgetSavedSignIn_ReportsWhatWasDone`, covering
`AgentControl-PackageCredentials-ForgetSavedSignIn`.
