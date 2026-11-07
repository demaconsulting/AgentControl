### PackageCredentialCallback

#### Verification Approach

`PackageCredentialCallback` is verified with unit tests in `PackageCredentialCallbackTests.cs`. The real
callback, `SessionCredentialStore` and `FeedOrigin` are used; the user is replaced by `FakeCredentialPrompt`
(a scripted prompt that answers from a queue, counts and records the questions, and can wait forever to
simulate a user who walked away). One test invokes the `Callback` delegate exactly as the NuGet library does,
with a URI and a token. A test that cancels the token while the fake prompt never answers proves that the
operation is not held by an abandoned dialog. The user's own credential store is replaced by
`FakeRememberedCredentialStore` (scripted saved credentials, recorded look-ups, saves and removals, and switches to
refuse, throw or report no helper), so the saved-credential tier, the remember option and the removal of a rejected
saved credential are exercised against the real callback and session store; the concurrency tests start several
operations at once and count the look-ups the fake received.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- A challenge from a foreign origin gets no credential, reads no store entry and opens no prompt, even when a
  credential is remembered for the feed.
- A remembered session credential is used without asking; otherwise a credential saved in the user's credential
  store is used without asking (also by a silent callback); otherwise the user is asked once, told which server and
  source the credential is for and whether remembering is possible; a silent callback never asks.
- The user's credential store is consulted once per server per session however many operations run at once, and not at
  all when the session already holds a credential; a failing store leads to the prompt without the remember option.
- Several challenges (index, version list, package file) of one operation cause one question; a decline is
  remembered as well; cancelling during the question surfaces as a cancellation rather than a decline.
- A prompted credential is stored only after `CommitAsync`, and handed to the user's credential store only when the
  user ticked Remember; a refusing or failing store never fails the operation; `RejectAsync` forgets exactly the
  credential that was offered (not a newer one), and removes it from the user's credential store only when it came
  from there.

#### Test Scenarios

**PackageCredentialCallback_ForeignOriginRefused**: A request for another host gets `null` without a prompt
although the feed's credential is remembered. This scenario is tested by
`PackageCredentialCallback_GetCredential_ForeignOrigin_ReturnsNullWithoutPrompting`, covering
`AgentControl-PackageCredentialCallback-ForeignOriginRefused`.

**PackageCredentialCallback_TierOrder**: The remembered session credential is used without asking; with
nothing remembered the user is asked once for that feed and source, and the question says whether remembering is
possible (it is when the user's credential store reports a helper, and is not when there is no store); a silent
callback with nothing remembered answers `null` and never asks; the `Callback` delegate behaves like the method. This
scenario is tested by
`PackageCredentialCallback_GetCredential_SessionCredentialStored_UsesItWithoutPrompting`,
`PackageCredentialCallback_GetCredential_NothingStored_AsksTheUserForThatFeed`,
`PackageCredentialCallback_GetCredential_NoPromptAndNothingStored_ReturnsNull`,
`PackageCredentialCallback_Callback_InvokedLikeTheNuGetLibraryDoes_AnswersFromTheStore`,
`PackageCredentialCallback_GetCredential_Prompting_ReportsWhetherRememberingIsPossible` and
`PackageCredentialCallback_GetCredential_NoUsersStore_PromptCannotRemember`, covering
`AgentControl-PackageCredentialCallback-TierOrder`.

**PackageCredentialCallback_SavedCredentialLookup**: A credential saved in the user's credential store is used
without asking, also by a silent callback; with nothing saved the credential store is consulted once per session and
the user is then asked; several concurrent operations share one look-up; a session credential is used without the
credential store being consulted; a credential store that throws leads to the prompt, offered without the remember
option. This scenario is tested by
`PackageCredentialCallback_GetCredential_SavedCredentialExists_UsesItWithoutPrompting`,
`PackageCredentialCallback_GetCredential_SilentCallbackAndSavedCredential_UsesIt`,
`PackageCredentialCallback_GetCredential_NothingSaved_LooksUpOncePerSessionThenPrompts`,
`PackageCredentialCallback_GetCredential_ConcurrentOperations_ShareOneLookup`,
`PackageCredentialCallback_GetCredential_SessionCredentialExists_DoesNotConsultTheUsersStore` and
`PackageCredentialCallback_GetCredential_UsersStoreThrows_FallsThroughToPromptWithoutRememberOption`, covering
`AgentControl-PackageCredentialCallback-SavedCredentialLookup`.

**PackageCredentialCallback_AnswerMemoized**: Several challenges prompt once; a cancelled dialog is remembered
for the operation so a retry does not reopen it and the decline is reported; cancelling the token while the
dialog is open throws `OperationCanceledException`. This scenario is tested by
`PackageCredentialCallback_GetCredential_SeveralChallenges_PromptsOnce`,
`PackageCredentialCallback_GetCredential_UserDeclines_NullIsMemoizedAndNotAskedAgain` and
`PackageCredentialCallback_GetCredential_CancelledWhilePrompting_ThrowsOperationCanceled`, covering
`AgentControl-PackageCredentialCallback-AnswerMemoized`.

**PackageCredentialCallback_CommitOnSuccess**: A credential from the prompt is not in the store until
`Commit`; `Commit` with nothing prompted stores nothing; `Reject` forgets the remembered credential; missing
constructor arguments are refused. This scenario is tested by
`PackageCredentialCallback_Commit_PromptedCredential_IsStoredOnlyAfterCommit`,
`PackageCredentialCallback_Commit_NothingPrompted_StoresNothing`,
`PackageCredentialCallback_Reject_SessionCredential_IsForgotten` and
`PackageCredentialCallback_Constructor_NullArguments_Throw`, covering
`AgentControl-PackageCredentialCallback-CommitOnSuccess`.

**PackageCredentialCallback_RememberOnRequest**: With Remember ticked, `CommitAsync` hands the credential to the
user's credential store; without the tick it never does; a credential store that refuses or throws still leaves the
credential in the session store and does not fail the commit. This scenario is tested by
`PackageCredentialCallback_Commit_RememberTicked_ApprovesInTheUsersStore`,
`PackageCredentialCallback_Commit_RememberNotTicked_NeverApproves` and
`PackageCredentialCallback_Commit_UsersStoreRefusesOrFails_StillKeepsTheSessionCredential`, covering
`AgentControl-PackageCredentialCallback-RememberOnRequest`.

**PackageCredentialCallback_RejectRemovesSavedCredential**: Rejecting a credential that came from the user's
credential store removes it from there and from the session, so the next operation asks the user; rejecting a
session-only credential leaves the user's credential store alone; rejecting a credential that another operation
has replaced meanwhile keeps the newer credential. This scenario is tested by
`PackageCredentialCallback_Reject_SavedCredential_IsRemovedFromTheUsersStoreAndTheNextOperationPrompts`,
`PackageCredentialCallback_Reject_SessionOnlyCredential_LeavesTheUsersStoreAlone` and
`PackageCredentialCallback_Reject_CredentialReplacedMeanwhile_KeepsTheNewerCredential`, covering
`AgentControl-PackageCredentialCallback-RejectRemovesSavedCredential`.

**PackageCredentialCallback_RegistersTypedSecret**: A credential obtained from the prompt is removed from text
by the redaction engine while the operation runs; `Release` (also when repeated) and `Reject` withdraw it;
`Commit` leaves it registered by the store until the store forgets it. Two concurrent operations use the same
remembered credential: when one rejects (forgets) it the other still has it redacted until it ends, and
clearing the store does not end redaction for a running operation; after both end the secret is no longer
tracked. This scenario is tested by
`PackageCredentialCallback_PromptedCredential_IsRegisteredForRedactionUntilTheOperationEnds`,
`PackageCredentialCallback_ConcurrentOperationRejectsCredential_ActiveOperationStaysRedactedUntilItEnds` and
`PackageCredentialCallback_StoreClearedWhileOperationRuns_OperationStaysRedactedUntilItEnds`, covering
`AgentControl-PackageCredentialCallback-RegistersTypedSecret`.
