### PackageCredentialCallback

#### Verification Approach

`PackageCredentialCallback` is verified with unit tests in `PackageCredentialCallbackTests.cs`. The real
callback, `SessionCredentialStore` and `FeedOrigin` are used; the user is replaced by `FakeCredentialPrompt`
(a scripted prompt that answers from a queue, counts and records the questions, and can wait forever to
simulate a user who walked away). One test invokes the `Callback` delegate exactly as the NuGet library does,
with a URI and a token. A test that cancels the token while the fake prompt never answers proves that the
operation is not held by an abandoned dialog.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- A challenge from a foreign origin gets no credential, reads no store entry and opens no prompt, even when a
  credential is remembered for the feed.
- A remembered session credential is used without asking; otherwise the user is asked once, told which server
  and source the credential is for; a silent callback never asks.
- Several challenges (index, version list, package file) of one operation cause one question; a decline is
  remembered as well; cancelling during the question surfaces as a cancellation rather than a decline.
- A prompted credential is stored only after `Commit`; `Reject` forgets the server's remembered credential.

#### Test Scenarios

**PackageCredentialCallback_ForeignOriginRefused**: A request for another host gets `null` without a prompt
although the feed's credential is remembered. This scenario is tested by
`PackageCredentialCallback_GetCredential_ForeignOrigin_ReturnsNullWithoutPrompting`, covering
`AgentControl-PackageCredentialCallback-ForeignOriginRefused`.

**PackageCredentialCallback_TierOrder**: The remembered session credential is used without asking; with
nothing remembered the user is asked once for that feed and source; a silent callback with nothing remembered
answers `null` and never asks; the `Callback` delegate behaves like the method. This scenario is tested by
`PackageCredentialCallback_GetCredential_SessionCredentialStored_UsesItWithoutPrompting`,
`PackageCredentialCallback_GetCredential_NothingStored_AsksTheUserForThatFeed`,
`PackageCredentialCallback_GetCredential_NoPromptAndNothingStored_ReturnsNull` and
`PackageCredentialCallback_Callback_InvokedLikeTheNuGetLibraryDoes_AnswersFromTheStore`, covering
`AgentControl-PackageCredentialCallback-TierOrder`.

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
