### PackageCredentialCallback

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`PackageCredentialCallback` answers the NuGet library's "this address needs credentials" questions for one
operation against one server: scoped to the server's origin, asked at most once, and consulting the session
store, then the user's own credential store, before the user. It satisfies
`AgentControl-PackageCredentialCallback-ForeignOriginRefused`,
`AgentControl-PackageCredentialCallback-ForeignOriginNotPrompted`,
`AgentControl-PackageCredentialCallback-TierOrder`,
`AgentControl-PackageCredentialCallback-PromptsWhenNothingKnown`,
`AgentControl-PackageCredentialCallback-PromptNamesServerAndSource`,
`AgentControl-PackageCredentialCallback-PromptReportsRememberPossible`,
`AgentControl-PackageCredentialCallback-SilentNoCredential`,
`AgentControl-PackageCredentialCallback-SavedCredentialLookup`,
`AgentControl-PackageCredentialCallback-SavedCredentialLookupWhenSilent`,
`AgentControl-PackageCredentialCallback-SavedCredentialLookupOnce`,
`AgentControl-PackageCredentialCallback-SessionCredentialSkipsLookup`,
`AgentControl-PackageCredentialCallback-FailedLookupFallsThroughToPrompt`,
`AgentControl-PackageCredentialCallback-FailedLookupDisablesRemember`,
`AgentControl-PackageCredentialCallback-AnswerMemoized`,
`AgentControl-PackageCredentialCallback-DeclineMemoized`,
`AgentControl-PackageCredentialCallback-ConcurrentRequestsSameAnswer`,
`AgentControl-PackageCredentialCallback-CancellationStopsWaiting`,
`AgentControl-PackageCredentialCallback-CommitOnSuccess`,
`AgentControl-PackageCredentialCallback-CommitStoresNothingWhenNoneEntered`,
`AgentControl-PackageCredentialCallback-NullArgumentsRefused`,
`AgentControl-PackageCredentialCallback-RememberOnRequest`,
`AgentControl-PackageCredentialCallback-RememberNeverUnchosen`,
`AgentControl-PackageCredentialCallback-UsersStoreFailureKeepsSessionCredential`,
`AgentControl-PackageCredentialCallback-UsersStoreFailureDoesNotFailOperation`,
`AgentControl-PackageCredentialCallback-RejectForgetsSessionCredential`,
`AgentControl-PackageCredentialCallback-RejectForgetsExactSessionCredential`,
`AgentControl-PackageCredentialCallback-RejectRemovesSavedCredential`,
`AgentControl-PackageCredentialCallback-RejectRemovesExactSavedCredential`,
`AgentControl-PackageCredentialCallback-RejectSessionOnlyLeavesUsersStoreAlone`,
`AgentControl-PackageCredentialCallback-RegistersTypedSecret`,
`AgentControl-PackageCredentialCallback-LeasesSessionCredential`,
`AgentControl-PackageCredentialCallback-LeaseEndsWithOperation`,
`AgentControl-PackageCredentialCallback-LeaseSurvivesForgetting` and
`AgentControl-PackageCredentialCallback-CommittedCredentialStaysRegistered`.

#### Data Model

**_feed**: `FeedOrigin` - the only origin that ever receives a credential.

**_sourceAlias**: `string` - the configured source's alias, shown by the prompt.

**_store**: `SessionCredentialStore` - consulted first, updated by `CommitAsync` and `RejectAsync`, and the owner of
the once-per-session lookup of the user's credential store.

**_prompt**: `ICredentialPrompt?` - asks the user; `null` makes the callback *silent*.

**_remembered**: `IRememberedCredentialStore?` - the user's own credential store (their Git credential helper), or
`null` when credentials cannot be kept beyond the session.

**_previousCredentialRejected**: `bool` - passed to the prompt so the dialog can say the last sign-in did
not work.

**_logger**: `ILogger?` - receives origin-only diagnostics: the feed origin and which tier answered, never a
credential.

**_answer**: `Task<NuGetCredential?>?` - the memoized answer to the first challenge, created under a lock.
A credential, or `null` for "none"; once created it is shared by every later challenge.

**SuppliedCredential**, **SuppliedCredentialWasSaved**, **RememberRequested**, **WasPrompted**, **UserDeclined**:
outcome properties the owner reads to decide
whether a credential was supplied (and so rejected), whether it came from the user's credential store (so a
rejection removes it from there as well), whether the user ticked "Remember on this computer", whether the user was
asked, and whether the user
cancelled the dialog, so a following failure can be reported as "sign-in canceled" rather than "access
denied". One instance serves exactly one operation, which is what makes the memoization safe.

#### Key Methods

**GetCredentialAsync(uri, cancellationToken)** (exposed as `Callback`): If the address does not belong to
the feed's origin (`FeedOrigin.Matches`), returns no credential immediately, without reading the store and
without asking the user - a redirect target or any other host never gets the credential
(`AgentControl-PackageCredentialCallback-ForeignOriginRefused`,
`AgentControl-PackageCredentialCallback-ForeignOriginNotPrompted`). Otherwise the first call creates the
memoized answer by resolving the tiers in order - the session store, then the user's credential store (a
once-per-session look-up through `SessionCredentialStore.EnsureRememberedLookupAsync`, without any user interface,
also for a silent callback, whose result joins the session store and is read back from there), then (when a prompt
was supplied) the prompt, then none - and every call, including the library's retries and concurrent challenges,
receives that same answer, a "no" included, so the dialog cannot reopen in a loop
(`AgentControl-PackageCredentialCallback-TierOrder`, `AgentControl-PackageCredentialCallback-PromptsWhenNothingKnown`,
`AgentControl-PackageCredentialCallback-SilentNoCredential`,
`AgentControl-PackageCredentialCallback-SavedCredentialLookup`,
`AgentControl-PackageCredentialCallback-SavedCredentialLookupWhenSilent`,
`AgentControl-PackageCredentialCallback-SavedCredentialLookupOnce`,
`AgentControl-PackageCredentialCallback-SessionCredentialSkipsLookup`,
`AgentControl-PackageCredentialCallback-AnswerMemoized`, `AgentControl-PackageCredentialCallback-DeclineMemoized`,
`AgentControl-PackageCredentialCallback-ConcurrentRequestsSameAnswer`).

The look-up of the user's credential store records its *outcome*, and the callback uses that one outcome for the
prompt: a look-up that fails for any reason other than cancellation is logged without any secret, the user is asked as
usual (the operation does not fail), and `CanRemember` is forced to `false` for that operation, so the dialog does not
offer "Remember on this computer" even when the store reports that a helper is available. The failure is recorded for
every operation that waited on that look-up, not only the one that started it
(`AgentControl-PackageCredentialCallback-FailedLookupFallsThroughToPrompt`,
`AgentControl-PackageCredentialCallback-FailedLookupDisablesRemember`). When the look-up did not fail, the callback
learns from the credential store whether a helper applies (`IsAvailableAsync`) and passes that as `CanRemember`, so
the remember choice is offered only when it can be honored; the prompt is also told which server and which configured
source the credential is for (`AgentControl-PackageCredentialCallback-PromptNamesServerAndSource`,
`AgentControl-PackageCredentialCallback-PromptReportsRememberPossible`). Waiting is bound to the caller's token, so
cancelling the operation stops the wait even while a dialog is open
(`AgentControl-PackageCredentialCallback-CancellationStopsWaiting`).

**CommitAsync()**: Keeps a credential that came from the prompt in the session store, but only when the operation
succeeded; one that came from the store is already there, and when no credential was entered nothing is stored. When
the user ticked "Remember on this computer" (and a credential store exists) it first asks that store to keep the
credential (`ApproveAsync`) and marks the session copy as saved, so a later rejection removes it from there too; a
credential the user did not choose to remember is never handed over. A store that refuses, fails or takes too long
does not fail the operation: the credential is still kept for the session and the problem is logged without any
secret (`AgentControl-PackageCredentialCallback-CommitOnSuccess`,
`AgentControl-PackageCredentialCallback-CommitStoresNothingWhenNoneEntered`,
`AgentControl-PackageCredentialCallback-RememberOnRequest`,
`AgentControl-PackageCredentialCallback-RememberNeverUnchosen`,
`AgentControl-PackageCredentialCallback-UsersStoreFailureKeepsSessionCredential`,
`AgentControl-PackageCredentialCallback-UsersStoreFailureDoesNotFailOperation`). It then calls `Release`.

**RejectAsync()**: For when the server refused a credential this operation offered. It forgets **exactly that
credential** from the session store, leaving a different one a concurrent operation stored meanwhile, and - when the
credential came from the user's credential store - removes it from there as well (`RejectAsync` of the store), again
only that exact credential, so a newer one the user saved there meanwhile is kept and the next operation asks the user.
A credential that was only a session one leaves the user's credential store alone. It then calls `Release`
(`AgentControl-PackageCredentialCallback-RejectForgetsSessionCredential`,
`AgentControl-PackageCredentialCallback-RejectForgetsExactSessionCredential`,
`AgentControl-PackageCredentialCallback-RejectRemovesSavedCredential`,
`AgentControl-PackageCredentialCallback-RejectRemovesExactSavedCredential`,
`AgentControl-PackageCredentialCallback-RejectSessionOnlyLeavesUsersStoreAlone`).

**Release()**: Ends this operation's redaction *lease* on the credential it used and is safe to call more than
once. Every operation that uses a credential - a remembered session credential, taken with the store's
`TryAcquire` so that finding it and leasing it are one atomic step, or one the user typed, leased as soon as
the prompt returned it - holds its own lease on the secret, independent of the store's registration. A
concurrent operation that causes `Forget` or `Clear` therefore cannot end redaction for an operation that is
still running and may still log or throw the secret. The owner calls `Release` in a `finally` for every
attempt (the silent one included), so a secret never stays tracked after an operation that was canceled or
failed for another reason; a credential that `CommitAsync` stored stays registered by the store
(`AgentControl-PackageCredentialCallback-RegistersTypedSecret`,
`AgentControl-PackageCredentialCallback-LeasesSessionCredential`,
`AgentControl-PackageCredentialCallback-LeaseEndsWithOperation`,
`AgentControl-PackageCredentialCallback-LeaseSurvivesForgetting`,
`AgentControl-PackageCredentialCallback-CommittedCredentialStaysRegistered`).

#### Error Handling

A decline is not an error: it yields `null` and is memoized. A cancelled token surfaces as
`OperationCanceledException` from the awaited answer; other exceptions from the prompt propagate to the NuGet
library, which reports them as a failure of the operation. A failure of the user's credential store - an
exception, a refusal or a time-out - is never a failure of the operation: a failed look-up falls through to the
prompt without the remember option, a failed save keeps the session credential, a failed removal is logged. Null
constructor arguments throw `ArgumentNullException` (`AgentControl-PackageCredentialCallback-NullArgumentsRefused`).
No message or log line contains a credential.

#### Dependencies

- **FeedOrigin**, **SessionCredentialStore** - scoping, remembered credentials and the once-per-session look-up.
- **ICredentialPrompt** / **CredentialPromptRequest** / **CredentialPromptAnswer** (supporting types of the
  subsystem) - the question to the user and the answer.
- **IRememberedCredentialStore** (supporting type of the subsystem; implemented by `GitCredentialClient`) - the
  user's own credential store.
- **SecretRedactor** - the redaction leases.
- **NuGetCaching** (OTS) - the `NuGetCredential` type and the callback shape.
- **Microsoft.Extensions.Logging** - the optional logger.

#### Callers

- **NuGetSourceProvider** - creates one silent callback for the first attempt of an operation and, only
  after the server refused it, one prompting callback for the second; calls `CommitAsync` or `RejectAsync` and
  `Release`.
