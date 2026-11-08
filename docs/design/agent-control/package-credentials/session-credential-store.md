### SessionCredentialStore

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`SessionCredentialStore` is the whole of AgentControl's own credential memory: the credentials the user
typed (or found saved in their own credential store) for package servers, kept in memory only, for the length of
the session, so a server that needs a sign-in asks once per session rather than once per operation. It satisfies
`AgentControl-SessionCredentialStore-InMemoryOnly`, `AgentControl-SessionCredentialStore-KeyedByOrigin`,
`AgentControl-SessionCredentialStore-ReplaceOnSet`, `AgentControl-SessionCredentialStore-NullArgumentsRefused`,
`AgentControl-SessionCredentialStore-ForgetAndClear`, `AgentControl-SessionCredentialStore-ClearForgetsAll`,
`AgentControl-SessionCredentialStore-ForgetExactCredential`, `AgentControl-SessionCredentialStore-RememberedOrigin`,
`AgentControl-SessionCredentialStore-RememberedOriginReplaced`,
`AgentControl-SessionCredentialStore-RememberedOriginDropped`,
`AgentControl-SessionCredentialStore-RememberedLookupOnce`,
`AgentControl-SessionCredentialStore-RememberedLookupAfterClear`,
`AgentControl-SessionCredentialStore-RememberedLookupFaultSurfaced`,
`AgentControl-SessionCredentialStore-RememberedLookupFaultNotRepeated`,
`AgentControl-SessionCredentialStore-ClearInvalidatesInFlightLookups`,
`AgentControl-SessionCredentialStore-DiscardedLookupSecretNotRegistered`,
`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction`,
`AgentControl-SessionCredentialStore-WithdrawsSecretsForRedaction` and
`AgentControl-SessionCredentialStore-AcquireGivesRedactionLease`.

A credential reaches the store in one of two ways, which differ in *when* it is stored:

- **A credential the user typed** is stored only after the operation it was typed for succeeded
  (`PackageCredentialCallback.CommitAsync` calls `Set`), so a wrong password is never kept.
- **A credential the user saved in their own credential store** is stored by the remembered-store look-up
  (`EnsureRememberedLookupAsync`) *before* any operation has used it, and so before it is known to work; it is marked
  as coming from the user's credential store, and a rejection removes it from the session and from there.

#### Data Model

**_credentials**: `ConcurrentDictionary<FeedOrigin, NuGetCredential>` - the remembered credentials by
server origin. The dictionary is private and exposes no enumeration, so callers cannot list the credentials;
only `Clear` enumerates its values internally, solely to withdraw each registration with `SecretRedactor`. The type
has no serialization support and implements no interface that would write it out
(`AgentControl-SessionCredentialStore-InMemoryOnly`).

**_fromRememberedStore**: `HashSet<FeedOrigin>` - the origins whose stored credential came from, or was saved to,
the user's own credential store, so that a rejection of it must also remove it from there. The mark follows the
credential: replaced when another credential is stored, dropped when it is forgotten or cleared.

**_rememberedLookups**: `ConcurrentDictionary<FeedOrigin, Lazy<Task>>` - the one look-up per origin of the user's
credential store, shared by every operation that asks (see `EnsureRememberedLookupAsync`).

**Generation counter**: an integer that `Clear` increments. A look-up captures the value current when it started, and
flows it to the `Set` calls it makes through an `AsyncLocal`; a `Set` made by a look-up whose captured value is no
longer current is *discarded* - neither stored nor registered with `SecretRedactor` - so a look-up that finishes after
`Clear` cannot put a credential back.

**Count**: `int` - how many servers have a remembered credential; exposed for display and tests, it reveals
nothing about the credentials.

A `NuGetCredential` (from the NuGetCaching OTS item) holds the user name and secret in an ordinary managed
string, because the NuGet library requires one; such a string cannot be wiped from memory, so the guarantee
is *never persisted*, not *never in process memory*. The store is thread-safe because operations on
different sources run concurrently.

#### Key Methods

**TryGet(origin, out credential)**: Finds the credential remembered for exactly this origin. A different
port, scheme or host finds nothing (`AgentControl-SessionCredentialStore-KeyedByOrigin`).

**TryAcquire(origin, out credential, out lease[, out fromRememberedStore])**: Like `TryGet`, and in the same
locked step takes a redaction lease on the secret for the caller and (in the longer form) reports whether the
credential came from the user's credential store. The lease is independent of the store's own registration, so a
`Forget` or `Clear` caused by another operation does not end redaction for a caller that is still using the
credential (`AgentControl-SessionCredentialStore-AcquireGivesRedactionLease`).

**Set(origin, credential, fromRememberedStore = false)**: Remembers a credential for an origin, replacing any
earlier one, and records whether it came from the user's credential store. The secret is
registered with `SecretRedactor` (and the replaced secret withdrawn) under a lock so each stored secret is
registered exactly once. A `Set` made by a look-up that `Clear` has invalidated is discarded instead (see
`EnsureRememberedLookupAsync`)
(`AgentControl-SessionCredentialStore-ReplaceOnSet`, `AgentControl-SessionCredentialStore-KeyedByOrigin`,
`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction`,
`AgentControl-SessionCredentialStore-WithdrawsSecretsForRedaction`,
`AgentControl-SessionCredentialStore-RememberedOrigin`,
`AgentControl-SessionCredentialStore-RememberedOriginReplaced`).

**IsFromRememberedStore(origin)**: Whether the credential held for the origin came from, or was saved to, the
user's credential store.

**EnsureRememberedLookupAsync(origin, lookup)**: Runs the look-up delegate **once per origin for the whole
session**, however many operations ask at the same moment, and lets every caller wait for that one task; a look-up
that found nothing is not repeated, and neither is one that faulted - the fault is surfaced to every caller waiting
for it (`AgentControl-SessionCredentialStore-RememberedLookupFaultSurfaced`,
`AgentControl-SessionCredentialStore-RememberedLookupFaultNotRepeated`). A `Lazy` makes a race between callers start
exactly one look-up. The look-up itself puts what it finds into this store with `Set`, *before* any operation has
used the credential (`AgentControl-SessionCredentialStore-RememberedLookupOnce`). The generation current when the
look-up starts is captured and flowed to its `Set` calls through an `AsyncLocal`, so `Clear` can invalidate it
(`AgentControl-SessionCredentialStore-ClearInvalidatesInFlightLookups`,
`AgentControl-SessionCredentialStore-DiscardedLookupSecretNotRegistered`). A caller that asks after a `Clear` starts a
fresh look-up for the new generation (`AgentControl-SessionCredentialStore-RememberedLookupAfterClear`). It lives in
the store, not in each callback, because a callback serves one operation but the memo must span all operations of the
session.

**Forget(origin)**: Removes the credential of one origin (used when the user asks to forget it), withdraws its
secret from `SecretRedactor`, drops its remembered-store mark, and reports whether one was removed; other origins
are untouched (`AgentControl-SessionCredentialStore-ForgetAndClear`,
`AgentControl-SessionCredentialStore-RememberedOriginDropped`).

**Forget(origin, credential)**: Removes the origin's credential only if it is still *that exact credential*
(used when the server rejected it). Concurrent operations on one server can each hold a different credential; a
rejection of the old one arriving late must not discard a newer credential that works - the race this overload
closes (`AgentControl-SessionCredentialStore-ForgetExactCredential`).

**Clear()**: Removes every credential and withdraws their secrets (enumerating the credentials internally only for
that), forgets which look-ups ran, and increments the generation so that every look-up still in flight is
invalidated: a credential such a look-up tries to store after `Clear` is discarded, so the exit-time clearing cannot be
undone by a late look-up. `App` calls it when the desktop lifetime exits
(`AgentControl-SessionCredentialStore-ClearForgetsAll`,
`AgentControl-SessionCredentialStore-ClearInvalidatesInFlightLookups`).

#### Error Handling

A null argument throws `ArgumentNullException` (`AgentControl-SessionCredentialStore-NullArgumentsRefused`); every
other situation is a normal return value. A fault in a look-up is surfaced to every caller waiting for it and is not
repeated until the store is cleared (the callback treats it as a failed look-up: the user is asked without the
remember option). A look-up invalidated by `Clear` is not an error: it completes normally, but its `Set` is silently
discarded. Nothing is logged by the store.

#### Dependencies

- **FeedOrigin** - the key.
- **SecretRedactor** - registration and leases.
- **NuGetCaching** (OTS) - the `NuGetCredential` type.

#### Callers

- **PackageCredentialCallback** - looks up, stores (on `CommitAsync`) and forgets (on `RejectAsync`) credentials, and
  runs the once-per-session look-up of the user's credential store.
- **PackageCredentialContext** - forgets the session's copy when the user asks to forget a saved sign-in.
- **NuGetSourceProvider** - holds one through the `PackageCredentialContext` it is given (or a private,
  empty one when it has none).
- **App** - creates the process-wide instance and clears it at exit.
