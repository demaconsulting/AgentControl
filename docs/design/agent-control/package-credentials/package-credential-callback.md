### PackageCredentialCallback

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`PackageCredentialCallback` answers the NuGet library's "this address needs credentials" questions for one
operation against one server: scoped to the server's origin, asked at most once, and consulting the session
store before the user. It satisfies `AgentControl-PackageCredentialCallback-ForeignOriginRefused`,
`AgentControl-PackageCredentialCallback-TierOrder`, `AgentControl-PackageCredentialCallback-AnswerMemoized`
and `AgentControl-PackageCredentialCallback-CommitOnSuccess`.

#### Data Model

**_feed**: `FeedOrigin` - the only origin that ever receives a credential.

**_sourceAlias**: `string` - the configured source's alias, shown by the prompt.

**_store**: `SessionCredentialStore` - consulted first, updated by `Commit` and `Reject`.

**_prompt**: `ICredentialPrompt?` - asks the user; `null` makes the callback *silent*.

**_previousCredentialRejected**: `bool` - passed to the prompt so the dialog can say the last sign-in did
not work.

**_logger**: `ILogger?` - receives origin-only diagnostics: the feed origin and which tier answered, never a
credential.

**_answer**: `Task<NuGetCredential?>?` - the memoized answer to the first challenge, created under a lock.
A credential, or `null` for "none"; once created it is shared by every later challenge.

**SuppliedCredential**, **WasPrompted**, **UserDeclined**: outcome properties the owner reads to decide
whether a credential was supplied (and so rejected), whether the user was asked, and whether the user
cancelled the dialog, so a following failure can be reported as "sign-in canceled" rather than "access
denied". One instance serves exactly one operation, which is what makes the memoization safe.

#### Key Methods

**GetCredentialAsync(uri, cancellationToken)** (exposed as `Callback`): If the address does not belong to
the feed's origin (`FeedOrigin.Matches`), returns no credential immediately, without reading the store and
without asking the user - a redirect target or any other host never gets the credential
(`AgentControl-PackageCredentialCallback-ForeignOriginRefused`). Otherwise the first call creates the
memoized answer by resolving the tiers in order - the session store, then (when a prompt was supplied) the
prompt, then none - and every call, including the library's retries and concurrent challenges, receives that
same answer, a "no" included, so the dialog cannot reopen in a loop
(`AgentControl-PackageCredentialCallback-TierOrder`, `AgentControl-PackageCredentialCallback-AnswerMemoized`).
Waiting is bound to the caller's token, so cancelling the operation stops the wait even while a dialog is
open.

**Commit()**: Stores the credential in the session store, but only when it came from the prompt (one that
came from the store is already there). The owner calls it after the operation succeeded, so a wrong
credential is never kept (`AgentControl-PackageCredentialCallback-CommitOnSuccess`). It then calls `Release`.

**Reject()**: Forgets the server's remembered credential, for when the server refused one this operation
offered, and calls `Release`.

**Release()**: Withdraws the credential the user typed during this operation from `SecretRedactor`, where
the callback registered it as soon as the prompt returned it; safe to call more than once. The owner calls it
in a `finally`, so a typed secret never stays tracked after an operation that was canceled or failed for
another reason; a credential that `Commit` stored stays registered by the store
(`AgentControl-PackageCredentialCallback-RegistersTypedSecret`).

#### Error Handling

A decline is not an error: it yields `null` and is memoized. A cancelled token surfaces as
`OperationCanceledException` from the awaited answer; other exceptions from the prompt propagate to the NuGet
library, which reports them as a failure of the operation. Null constructor arguments throw
`ArgumentNullException`. No message or log line contains a credential.

#### Dependencies

- **FeedOrigin**, **SessionCredentialStore** - scoping and remembered credentials.
- **ICredentialPrompt** / **CredentialPromptRequest** (supporting types of the subsystem) - the question to
  the user.
- **NuGetCaching** (OTS) - the `NuGetCredential` type and the callback shape.
- **Microsoft.Extensions.Logging** - the optional logger.

#### Callers

- **NuGetSourceProvider** - creates one silent callback for the first attempt of an operation and, only
  after the server refused it, one prompting callback for the second.
