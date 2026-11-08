### PackageCredentialCallback

#### Verification Approach

`PackageCredentialCallback` is verified with unit tests in `PackageCredentialCallbackTests.cs`. The real
callback, `SessionCredentialStore` and `FeedOrigin` are used; the user is replaced by `FakeCredentialPrompt`
(a scripted prompt that answers from a queue, counts and records the questions, and can wait forever to
simulate a user who walked away). One test invokes the `Callback` delegate exactly as the NuGet library does,
with a URI and a token. A test that cancels the token while the fake prompt never answers proves that the
operation is not held by an abandoned dialog. The user's own credential store is replaced by
`FakeRememberedCredentialStore` (scripted saved credentials, recorded look-ups, saves and removals, and switches to
refuse, throw, fail only the look-up, or report no helper), so the saved-credential tier, the remember option and the
removal of a rejected saved credential are exercised against the real callback and session store; the fake is
credential-aware, so a removal of a credential that has been replaced in the user's store meanwhile can be observed;
the concurrency tests start several operations at once and count the look-ups the fake received.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- A challenge from a foreign origin gets no credential and opens no prompt, even when a credential is remembered
  for the feed.
- A remembered session credential is used without asking; otherwise a credential saved in the user's credential
  store is used without asking (also by a silent callback); otherwise the user is asked once, told which server and
  source the credential is for and whether remembering is possible; a silent callback never asks.
- The user's credential store is consulted once per server per session however many operations run at once, and not at
  all when the session already holds a credential.
- A failing look-up of the user's credential store leads to the prompt without the remember option, even when the
  store reports a helper as available.
- Several challenges (index, version list, package file) of one operation cause one question and the same answer; a
  decline is remembered as well; cancelling during the question surfaces as a cancellation rather than a decline.
- A prompted credential is stored only after `CommitAsync`, and handed to the user's credential store only when the
  user ticked Remember; a refusing or failing store never fails the operation and the credential stays in the session.
- `RejectAsync` forgets exactly the credential that was offered (not a newer one) from the session and, only when it
  came from the user's credential store, removes exactly that credential from there (not a newer one saved there
  meanwhile).
- Redaction leases cover a credential from the moment it is entered or found until its operation ends, whatever other
  operations forget or the store clears.
- Missing constructor arguments are refused.

#### Test Scenarios

**PackageCredentialCallback_ForeignOriginRefused**: A request for another host (another host, port or scheme) gets
`null` without a prompt although the feed's credential is remembered. This scenario is tested by
`PackageCredentialCallback_GetCredential_ForeignOrigin_ReturnsNullWithoutPrompting`, covering
`AgentControl-PackageCredentialCallback-ForeignOriginRefused` and
`AgentControl-PackageCredentialCallback-ForeignOriginNotPrompted`.

**PackageCredentialCallback_TierOrder**: The remembered session credential is used without asking; the `Callback`
delegate behaves like the method. This scenario is tested by
`PackageCredentialCallback_GetCredential_SessionCredentialStored_UsesItWithoutPrompting` and
`PackageCredentialCallback_Callback_InvokedLikeTheNuGetLibraryDoes_AnswersFromTheStore`, covering
`AgentControl-PackageCredentialCallback-TierOrder`.

**PackageCredentialCallback_PromptsWhenNothingKnown**: With nothing remembered the user is asked, once, and the
answer is used. This scenario is tested by
`PackageCredentialCallback_GetCredential_NothingStored_AsksTheUserForThatFeed`, covering
`AgentControl-PackageCredentialCallback-PromptsWhenNothingKnown`.

**PackageCredentialCallback_PromptNamesServerAndSource**: The question carries the feed's origin and the configured
source alias (and whether an earlier credential was rejected). This scenario is tested by
`PackageCredentialCallback_GetCredential_NothingStored_AsksTheUserForThatFeed`, covering
`AgentControl-PackageCredentialCallback-PromptNamesServerAndSource`.

**PackageCredentialCallback_PromptReportsRememberPossible**: The question says remembering is possible when the
user's credential store reports a helper, and is not when it reports none or there is no store at all. This scenario
is tested by `PackageCredentialCallback_GetCredential_Prompting_ReportsWhetherRememberingIsPossible` and
`PackageCredentialCallback_GetCredential_NoUsersStore_PromptCannotRemember`, covering
`AgentControl-PackageCredentialCallback-PromptReportsRememberPossible`.

**PackageCredentialCallback_SilentNoCredential**: A silent callback with nothing remembered answers `null` and never
asks. This scenario is tested by `PackageCredentialCallback_GetCredential_NoPromptAndNothingStored_ReturnsNull`,
covering `AgentControl-PackageCredentialCallback-SilentNoCredential`.

**PackageCredentialCallback_SavedCredentialLookup**: A credential saved in the user's credential store is used
without asking, and is remembered for the session as one that the store holds too. This scenario is tested by
`PackageCredentialCallback_GetCredential_SavedCredentialExists_UsesItWithoutPrompting`, covering
`AgentControl-PackageCredentialCallback-SavedCredentialLookup`.

**PackageCredentialCallback_SavedCredentialLookupWhenSilent**: A silent callback also reads a saved credential. This
scenario is tested by `PackageCredentialCallback_GetCredential_SilentCallbackAndSavedCredential_UsesIt`, covering
`AgentControl-PackageCredentialCallback-SavedCredentialLookupWhenSilent`.

**PackageCredentialCallback_SavedCredentialLookupOnce**: With nothing saved the credential store is consulted once
per session and the user is then asked; several concurrent operations share one look-up. This scenario is tested by
`PackageCredentialCallback_GetCredential_NothingSaved_LooksUpOncePerSessionThenPrompts` and
`PackageCredentialCallback_GetCredential_ConcurrentOperations_ShareOneLookup`, covering
`AgentControl-PackageCredentialCallback-SavedCredentialLookupOnce`.

**PackageCredentialCallback_SessionCredentialSkipsLookup**: A session credential is used without the credential
store being consulted. This scenario is tested by
`PackageCredentialCallback_GetCredential_SessionCredentialExists_DoesNotConsultTheUsersStore`, covering
`AgentControl-PackageCredentialCallback-SessionCredentialSkipsLookup`.

**PackageCredentialCallback_FailedLookup**: A credential store that throws does not fail the request: the user is
asked, and the question offers no remembering. When the store reports a helper as available but only the look-up
fails, the question still offers no remembering. This scenario is tested by
`PackageCredentialCallback_GetCredential_UsersStoreThrows_FallsThroughToPromptWithoutRememberOption` and
`PackageCredentialCallback_GetCredential_LookupFailsWhileHelperAvailable_PromptCannotRemember`, covering
`AgentControl-PackageCredentialCallback-FailedLookupFallsThroughToPrompt` (the first test) and
`AgentControl-PackageCredentialCallback-FailedLookupDisablesRemember` (both tests).

**PackageCredentialCallback_AnswerMemoized**: Several challenges, one after the other and concurrently, prompt once
and receive the same credential. This scenario is tested by
`PackageCredentialCallback_GetCredential_SeveralChallenges_PromptsOnce`, covering
`AgentControl-PackageCredentialCallback-AnswerMemoized` and
`AgentControl-PackageCredentialCallback-ConcurrentRequestsSameAnswer`.

**PackageCredentialCallback_DeclineMemoized**: A cancelled dialog is remembered for the operation so a retry does not
reopen it and the decline is reported. This scenario is tested by
`PackageCredentialCallback_GetCredential_UserDeclines_NullIsMemoizedAndNotAskedAgain`, covering
`AgentControl-PackageCredentialCallback-DeclineMemoized`.

**PackageCredentialCallback_CancellationStopsWaiting**: Cancelling the token while the dialog is open throws
`OperationCanceledException` and is not reported as a decline. This scenario is tested by
`PackageCredentialCallback_GetCredential_CancelledWhilePrompting_ThrowsOperationCanceled`, covering
`AgentControl-PackageCredentialCallback-CancellationStopsWaiting`.

**PackageCredentialCallback_CommitOnSuccess**: A credential from the prompt is not in the store until `Commit`.
This scenario is tested by `PackageCredentialCallback_Commit_PromptedCredential_IsStoredOnlyAfterCommit`, covering
`AgentControl-PackageCredentialCallback-CommitOnSuccess`.

**PackageCredentialCallback_CommitStoresNothingWhenNoneEntered**: `Commit` with nothing prompted stores nothing. This
scenario is tested by `PackageCredentialCallback_Commit_NothingPrompted_StoresNothing`, covering
`AgentControl-PackageCredentialCallback-CommitStoresNothingWhenNoneEntered`.

**PackageCredentialCallback_NullArgumentsRefused**: Missing constructor arguments are refused. This scenario is
tested by `PackageCredentialCallback_Constructor_NullArguments_Throw`, covering
`AgentControl-PackageCredentialCallback-NullArgumentsRefused`.

**PackageCredentialCallback_RememberOnRequest**: With Remember ticked, `CommitAsync` hands the credential to the
user's credential store, and not before the commit. This scenario is tested by
`PackageCredentialCallback_Commit_RememberTicked_ApprovesInTheUsersStore`, covering
`AgentControl-PackageCredentialCallback-RememberOnRequest`.

**PackageCredentialCallback_RememberNeverUnchosen**: Without the tick, nothing is ever handed to the user's credential
store, and the credential is kept for the session only. This scenario is tested by
`PackageCredentialCallback_Commit_RememberNotTicked_NeverApproves`, covering
`AgentControl-PackageCredentialCallback-RememberNeverUnchosen`.

**PackageCredentialCallback_UsersStoreFailure**: A credential store that refuses or throws still leaves the
credential in the session store, not marked as saved, and does not fail the commit. This scenario is tested by
`PackageCredentialCallback_Commit_UsersStoreRefusesOrFails_StillKeepsTheSessionCredential`, covering
`AgentControl-PackageCredentialCallback-UsersStoreFailureKeepsSessionCredential` and
`AgentControl-PackageCredentialCallback-UsersStoreFailureDoesNotFailOperation`.

**PackageCredentialCallback_RejectForgetsSessionCredential**: `Reject` forgets the remembered credential, whether it
was a session one or one that came from the user's credential store. This scenario is tested by
`PackageCredentialCallback_Reject_SessionCredential_IsForgotten` and
`PackageCredentialCallback_Reject_SavedCredential_IsRemovedFromTheUsersStoreAndTheNextOperationPrompts`, covering
`AgentControl-PackageCredentialCallback-RejectForgetsSessionCredential`.

**PackageCredentialCallback_RejectForgetsExactSessionCredential**: Rejecting a credential that another operation has
replaced in the session meanwhile keeps the newer credential. This scenario is tested by
`PackageCredentialCallback_Reject_CredentialReplacedMeanwhile_KeepsTheNewerCredential`, covering
`AgentControl-PackageCredentialCallback-RejectForgetsExactSessionCredential`.

**PackageCredentialCallback_RejectRemovesSavedCredential**: Rejecting a credential that came from the user's
credential store removes it from there and from the session, so the next operation asks the user. This scenario is
tested by
`PackageCredentialCallback_Reject_SavedCredential_IsRemovedFromTheUsersStoreAndTheNextOperationPrompts`, covering
`AgentControl-PackageCredentialCallback-RejectRemovesSavedCredential`.

**PackageCredentialCallback_RejectRemovesExactSavedCredential**: A credential-aware fake of the user's credential
store lets a newer credential replace the saved one while an operation still uses the older one; rejecting the older
credential leaves the newer credential in the user's credential store. This scenario is tested by
`PackageCredentialCallback_Reject_SavedCredentialReplacedInUsersStoreMeanwhile_KeepsTheNewerCredentialThere`,
covering `AgentControl-PackageCredentialCallback-RejectRemovesExactSavedCredential`.

**PackageCredentialCallback_RejectSessionOnlyLeavesUsersStoreAlone**: Rejecting a session-only credential leaves the
user's credential store alone. This scenario is tested by
`PackageCredentialCallback_Reject_SessionOnlyCredential_LeavesTheUsersStoreAlone`, covering
`AgentControl-PackageCredentialCallback-RejectSessionOnlyLeavesUsersStoreAlone`.

**PackageCredentialCallback_RegistersTypedSecret**: A credential obtained from the prompt is removed from text by the
redaction engine while the operation runs. This scenario is tested by
`PackageCredentialCallback_PromptedCredential_IsRegisteredForRedactionUntilTheOperationEnds`, covering
`AgentControl-PackageCredentialCallback-RegistersTypedSecret`.

**PackageCredentialCallback_LeasesSessionCredential**: An operation that uses a remembered session credential has it
redacted through its own lease, which survives the store forgetting or clearing it. This scenario is tested by
`PackageCredentialCallback_ConcurrentOperationRejectsCredential_ActiveOperationStaysRedactedUntilItEnds` and
`PackageCredentialCallback_StoreClearedWhileOperationRuns_OperationStaysRedactedUntilItEnds`, covering
`AgentControl-PackageCredentialCallback-LeasesSessionCredential`.

**PackageCredentialCallback_LeaseEndsWithOperation**: `Release` (also when repeated) and `Reject` withdraw the
secret; after every operation using a secret has ended it is no longer tracked. This scenario is tested by
`PackageCredentialCallback_PromptedCredential_IsRegisteredForRedactionUntilTheOperationEnds` and
`PackageCredentialCallback_ConcurrentOperationRejectsCredential_ActiveOperationStaysRedactedUntilItEnds`, covering
`AgentControl-PackageCredentialCallback-LeaseEndsWithOperation`.

**PackageCredentialCallback_LeaseSurvivesForgetting**: Two concurrent operations use the same remembered credential:
when one rejects (forgets) it the other still has it redacted until it ends, and clearing the store does not end
redaction for a running operation. This scenario is tested by
`PackageCredentialCallback_ConcurrentOperationRejectsCredential_ActiveOperationStaysRedactedUntilItEnds` and
`PackageCredentialCallback_StoreClearedWhileOperationRuns_OperationStaysRedactedUntilItEnds`, covering
`AgentControl-PackageCredentialCallback-LeaseSurvivesForgetting`.

**PackageCredentialCallback_CommittedCredentialStaysRegistered**: `Commit` leaves the credential registered by the
store until the store forgets it. This scenario is tested by
`PackageCredentialCallback_PromptedCredential_IsRegisteredForRedactionUntilTheOperationEnds`, covering
`AgentControl-PackageCredentialCallback-CommittedCredentialStaysRegistered`.
