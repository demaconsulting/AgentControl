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
holds saved credentials in a dictionary, records every look-up, save and removal, removes a credential only when the
credential supplied equals the one stored (as the interface requires), and can be told to refuse, fail or report that
no helper is available; the real `git credential` protocol against the real helper is verified at the
`GitIntegration` level (`GitCredentialClientTests.cs` and `GitCredentialClientIntegrationTests.cs`).

### Test Environment

N/A - standard test environment. The tests use no network, no files and no user interface.

### Acceptance Criteria

- All subsystem tests pass with zero failures.
- A credential typed during one operation is kept for the session only after that operation succeeded, and is
  then used by the next operation on the same server without asking again; a credential the server rejected is
  forgotten and the next operation asks again (and says the last one failed); clearing the store forgets every
  session credential.
- A credential kept for one server is never offered for another origin - whether the host, the port, the scheme
  or the sub-domain differs - and a challenge from a foreign origin never opens the prompt, even while the user
  is being asked for the server's own credential.
- Repeated challenges on one callback ask the user once and share the answer, a declined prompt included.
- A credential is handed to the user's own credential store only when the user chose to remember it and the
  operation succeeded, and the choice is offered only when that store can keep it; a later session finds it
  there and uses it without asking, also when it may not ask, looking it up once per server per session;
  a saved credential the server rejects is removed from there, but a newer credential that replaced it
  meanwhile is kept.
- Forgetting a saved sign-in removes the saved copy and the session's copy, and reports whether nothing was
  saved, it was removed, or the credential store could not remove it; a context without a prompt keeps the
  user's credential store.
- No password, token, user information, query string, fragment or authorization value of any scheme reaches
  the log, whichever of the units wrote it, in a message, a structured property, a scope or an exception; a
  secret stays redacted until the last operation that uses it ends.

### Test Scenarios

**PackageCredentials_TwoOperations_FirstPromptsAndSecondReusesTheSessionCredential**: The first operation's
callback asks the user and returns the typed credential while the store is still empty; after `Commit`, the
second operation's callback answers for another address of the same server from the store, the prompt having
been asked exactly once and the second callback never having prompted. This scenario is tested by
`PackageCredentials_TwoOperations_FirstPromptsAndSecondReusesTheSessionCredential`, covering
`AgentControl-PackageCredentials-SessionOnly`, `AgentControl-PackageCredentials-KeptOnlyAfterSuccess`,
`AgentControl-PackageCredentials-SessionCredentialReused`,
`AgentControl-PackageCredentials-AskWhenNoCredentialKnown` and
`AgentControl-PackageCredentials-AskOncePerOperation`.

**PackageCredentials_RejectedCredential_IsForgottenAndTheNextOperationAsksAgain**: A remembered credential is
offered by a silent callback and then rejected (`Reject`); the next, prompting callback asks the user, tells
the prompt that a credential was just rejected, and after `Commit` the store holds the new credential. This
scenario is tested by `PackageCredentials_RejectedCredential_IsForgottenAndTheNextOperationAsksAgain`,
covering `AgentControl-PackageCredentials-KeptOnlyAfterSuccess`,
`AgentControl-PackageCredentials-AskWhenNoCredentialKnown` and
`AgentControl-PackageCredentials-RejectedCredentialForgotten`.

**PackageCredentials_ClearedSessionStore_ForgetsEveryCredentialAndTheNextOperationAsksAgain**: Credentials are
kept for two servers and the store is cleared, as the application does when it exits; no credential is found
for either server and the next operation on each asks the user. This scenario is tested by
`PackageCredentials_ClearedSessionStore_ForgetsEveryCredentialAndTheNextOperationAsksAgain`, covering
`AgentControl-PackageCredentials-ForgottenAtExit`. (A test still to be written.)

**PackageCredentials_CredentialForOneFeed_IsNeverOfferedToAnotherOrigin**: A credential is remembered for one
server; a callback for a different server finds nothing and may not ask, and a callback for the first server
challenged by a foreign host answers no credential and never opens the prompt. This scenario is tested by
`PackageCredentials_CredentialForOneFeed_IsNeverOfferedToAnotherOrigin`, covering
`AgentControl-PackageCredentials-ScopedToFeedOrigin` and
`AgentControl-PackageCredentials-NoPromptForOtherOrigins`.

**PackageCredentials_ChallengeFromDifferentSchemeHostOrPort_GetsNoCredentialAndNoPrompt**: A theory over
addresses that differ from the server's origin by scheme, by host, by port and by sub-domain; a callback for
the server, with a credential in the store and a prompt that may ask, answers each challenge with no credential
and the prompt is never asked. This scenario is tested by
`PackageCredentials_ChallengeFromDifferentSchemeHostOrPort_GetsNoCredentialAndNoPrompt`, covering
`AgentControl-PackageCredentials-ScopedToFeedOrigin` and
`AgentControl-PackageCredentials-NoPromptForOtherOrigins`.

**PackageCredentials_RepeatedChallengesOnOneCallback_PromptsOnceAndSharesTheAnswer**: One callback is
challenged repeatedly - several addresses of the server and retries - and the prompt is asked once; every
challenge receives the same answer. A second callback whose user declines is challenged repeatedly too; the
prompt is asked once and every challenge receives no credential. This scenario is tested by
`PackageCredentials_RepeatedChallengesOnOneCallback_PromptsOnceAndSharesTheAnswer`, covering
`AgentControl-PackageCredentials-AskOncePerOperation` and
`AgentControl-PackageCredentials-DeclinedAnswerShared`.

**PackageCredentials_RememberLifeCycle_SavedByTheUsersStoreAndFoundInALaterSession**: The user ticks "Remember"
and signs in; after the operation succeeds the credential is in the (fake) user's credential store, the session
store marks it as saved; a later session with a fresh session store finds it in the credential store, without
asking, even for a silent callback; forgetting the saved sign-in makes the next session ask again. This scenario
is tested by `PackageCredentials_RememberLifeCycle_SavedByTheUsersStoreAndFoundInALaterSession`, covering
`AgentControl-PackageCredentials-SessionOnly` (a new session holds nothing until the user's store supplies it),
`AgentControl-PackageCredentials-RememberOnRequest`,
`AgentControl-PackageCredentials-SavedCredentialUsedWithoutAsking` and
`AgentControl-PackageCredentials-ForgetSavedSignIn`.

**PackageCredentialContext_WithoutPrompt_KeepsStoreAndTheUsersCredentialStore**: A context derived with
`WithoutPrompt` keeps the same session store and the same credential store, because reading a saved sign-in needs
no dialog. This scenario is tested by `PackageCredentialContext_WithoutPrompt_KeepsStoreAndTheUsersCredentialStore`,
covering `AgentControl-PackageCredentials-SavedCredentialUsedWithoutAsking`.

**PackageCredentials_RememberNotTickedOrOperationNotSucceeded_NothingIsSavedInTheUsersStore**: The user signs
in without ticking "Remember" and the operation succeeds - nothing is handed to the user's credential store;
the user ticks "Remember" but the operation is rejected or released - nothing is handed to it either. This
scenario is tested by `PackageCredentials_RememberNotTickedOrOperationNotSucceeded_NothingIsSavedInTheUsersStore`,
covering `AgentControl-PackageCredentials-NeverRememberUnasked` and
`AgentControl-PackageCredentials-RememberOnlyAfterSuccess`. (A test still to be written.)

**PackageCredentials_PromptRequest_OffersRememberOnlyWhenTheUsersStoreCanKeepIt**: The question the callback
puts to the prompt says the sign-in can be remembered only when the user's credential store applies to the
server, and says it cannot with no store or when the store reports that no helper is available. This scenario is
tested by `PackageCredentials_PromptRequest_OffersRememberOnlyWhenTheUsersStoreCanKeepIt`, covering
`AgentControl-PackageCredentials-RememberOfferedOnlyWhenPossible`. (A test still to be written; the sign-in
dialog's presentation of an unavailable choice - shown disabled, with an explanation - is verified at the
`LauncherUI` level.)

**PackageCredentials_ConcurrentOperationsOnOneServer_LookUpTheSavedCredentialOnce**: Several operations on one
server start together with nothing in the session store and a credential saved in the user's credential store;
the store is asked once and every operation uses the saved credential. This scenario is tested by
`PackageCredentials_ConcurrentOperationsOnOneServer_LookUpTheSavedCredentialOnce`, covering
`AgentControl-PackageCredentials-SavedCredentialLookedUpOnce`. (A test still to be written.)

**PackageCredentials_RejectedSavedCredential_IsRemovedFromTheUsersStoreAndTheNextSessionAsks**: A credential
found in the user's credential store is offered and the server rejects it; it is removed from the user's
credential store and the session store, and the next session asks the user. This scenario is tested by
`PackageCredentials_RejectedSavedCredential_IsRemovedFromTheUsersStoreAndTheNextSessionAsks`, covering
`AgentControl-PackageCredentials-RejectedSavedCredentialRemoved`. (A test still to be written.)

**PackageCredentials_LateRejectionOfAnOldCredential_KeepsTheNewerRememberedCredential**: An operation holds a
saved credential; before the server's rejection of it arrives, the user signs in again and a newer credential
replaces it in the user's credential store; the late rejection removes nothing, because the credential it names
is no longer the stored one, and the newer credential is still found. This scenario is tested by
`PackageCredentials_LateRejectionOfAnOldCredential_KeepsTheNewerRememberedCredential`, covering
`AgentControl-PackageCredentials-NewerCredentialSurvivesLateRejection`.

**PackageCredentialContext_ForgetSavedSignIn_ReportsWhatWasDone**: Forgetting reports "nothing saved" when
neither the session nor the credential store holds a sign-in, "removed" when the credential store held one and
removed it (and removes only that origin's), "removed" when only the session's copy existed, "failed" when the
credential store refuses, and "nothing saved" without a credential store. This scenario is tested by
`PackageCredentialContext_ForgetSavedSignIn_ReportsWhatWasDone`, covering
`AgentControl-PackageCredentials-ForgetSavedSignIn`, `AgentControl-PackageCredentials-ForgetSessionSignIn` and
`AgentControl-PackageCredentials-ForgetSignInReportsOutcome`.

**PackageCredentials_SignInThroughCallbackAndRedactingLogger_NeverLogsASecret**: A sign-in is performed with a
logger-carrying callback over the redacting logger, followed by log lines that carry a signed address and an
`Authorization: Basic` value for the same password; every line the recording logger received is searched for
the password and for its Base64 form, and none contains either. This scenario is tested by
`PackageCredentials_SignInThroughCallbackAndRedactingLogger_NeverLogsASecret`, covering
`AgentControl-PackageCredentials-SecretsNeverLogged`, `AgentControl-PackageCredentials-UrlQueryNeverLogged`,
`AgentControl-PackageCredentials-AuthorizationValuesNeverLogged` and
`AgentControl-PackageCredentials-LoggedMessagesRedacted`.

**PackageCredentials_LoggedUrlParts_NeverReachTheLog**: A theory over a web address whose user information,
query string or fragment carries the sentinel; the address is logged through the redacting logger and the
recording logger's lines never contain the sentinel while the scheme, host and path remain. This scenario is
tested by `PackageCredentials_LoggedUrlParts_NeverReachTheLog`, covering
`AgentControl-PackageCredentials-UrlUserInfoNeverLogged`, `AgentControl-PackageCredentials-UrlQueryNeverLogged`
and `AgentControl-PackageCredentials-UrlFragmentNeverLogged`.

**PackageCredentials_LoggedAuthorizationSchemes_NeverReachTheLog**: A theory over `Authorization` headers of the
Basic, Bearer, Digest, NTLM and Negotiate schemes and of a custom scheme; each is logged through the redacting
logger and the recording logger's lines never contain the sentinel. This scenario is tested by
`PackageCredentials_LoggedAuthorizationSchemes_NeverReachTheLog`, covering
`AgentControl-PackageCredentials-AuthorizationValuesNeverLogged`.

**PackageCredentials_LoggedExceptions_AreSanitized**: An exception whose type name, message, inner exception,
data and stack trace carry the sentinel is logged through the redacting logger; what the recording logger
receives carries no sentinel and is not the original exception. This scenario is tested by
`PackageCredentials_LoggedExceptions_AreSanitized`, covering
`AgentControl-PackageCredentials-ExceptionsLoggedSanitized`.

**PackageCredentials_LoggedScopesAndProperties_AreRedacted**: A scope and the structured properties of a message
carry the sentinel; neither the scopes nor the properties the recording logger receives contain it. This
scenario is tested by `PackageCredentials_LoggedScopesAndProperties_AreRedacted`, covering
`AgentControl-PackageCredentials-LoggedPropertiesRedacted` and
`AgentControl-PackageCredentials-LoggedScopesRedacted`.

**PackageCredentials_RedactionLease_SecretIsRedactedUntilTheLeaseEnds**: An operation holds a credential, another
operation's rejection makes the store forget it, and a line that carries the secret is still redacted; once
the operation ends its lease, the secret is no longer registered. This scenario is tested by
`PackageCredentials_RedactionLease_SecretIsRedactedUntilTheLeaseEnds`, covering
`AgentControl-PackageCredentials-SecretsNeverLogged` and
`AgentControl-PackageCredentials-SecretRedactedWhileInUse`.
