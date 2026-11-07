### SessionCredentialStore

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`SessionCredentialStore` is the whole of AgentControl's own credential memory: the credentials the user
typed (or found saved in their own credential store) for package servers, kept in memory only, for the length of
the session, so a server that needs a sign-in asks once per session rather than once per operation. It satisfies
`AgentControl-SessionCredentialStore-InMemoryOnly`, `AgentControl-SessionCredentialStore-ForgetAndClear`,
`AgentControl-SessionCredentialStore-ForgetExactCredential`, `AgentControl-SessionCredentialStore-RememberedOrigin`,
`AgentControl-SessionCredentialStore-RememberedLookupOnce` and
`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction`.

#### Data Model

**_credentials**: `ConcurrentDictionary<FeedOrigin, NuGetCredential>` - the remembered credentials by
server origin. The dictionary is private and never enumerated by any member, so the credentials cannot be
listed; the type has no serialization support and implements no interface that would write it out.

**_fromRememberedStore**: `HashSet<FeedOrigin>` - the origins whose stored credential came from, or was saved to,
the user's own credential store, so that a rejection of it must also remove it from there. The mark follows the
credential: replaced when another credential is stored, dropped when it is forgotten or cleared.

**_rememberedLookups**: `ConcurrentDictionary<FeedOrigin, Lazy<Task>>` - the one look-up per origin of the user's
credential store, shared by every operation that asks (see `EnsureRememberedLookupAsync`).

**Count**: `int` - how many servers have a remembered credential; exposed for display and tests, it reveals
nothing about the credentials.

A `NuGetCredential` (from the NuGetCaching OTS item) holds the user name and secret in an ordinary managed
string, because the NuGet library requires one; such a string cannot be wiped from memory, so the guarantee
is *never persisted*, not *never in process memory*. The store is thread-safe because operations on
different sources run concurrently.

#### Key Methods

**TryGet(origin, out credential)**: Finds the credential remembered for exactly this origin. A different
port, scheme or host finds nothing.

**TryAcquire(origin, out credential, out lease[, out fromRememberedStore])**: Like `TryGet`, and in the same
locked step takes a redaction lease on the secret for the caller and (in the longer form) reports whether the
credential came from the user's credential store. The lease is independent of the store's own registration, so a
`Forget` or `Clear` caused by another operation does not end redaction for a caller that is still using the
credential (`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction`).

**Set(origin, credential, fromRememberedStore = false)**: Remembers a credential for an origin, replacing any
earlier one, and records whether it came from the user's credential store. The secret is
registered with `SecretRedactor` (and the replaced secret withdrawn) under a lock so each stored secret is
registered exactly once (`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction`,
`AgentControl-SessionCredentialStore-RememberedOrigin`).

**IsFromRememberedStore(origin)**: Whether the credential held for the origin came from, or was saved to, the
user's credential store.

**EnsureRememberedLookupAsync(origin, lookup)**: Runs the look-up delegate **once per origin for the whole
session**, however many operations ask at the same moment, and lets every caller wait for that one task; a look-up
that found nothing is not repeated. A `Lazy` makes a race between callers start exactly one look-up. The look-up
itself puts what it finds into this store with `Set`
(`AgentControl-SessionCredentialStore-RememberedLookupOnce`). It lives in the store, not in each callback, because a
callback serves one operation but the memo must span all operations of the session.

**Forget(origin)**: Removes the credential of one origin (used when the user asks to forget it), withdraws its
secret from `SecretRedactor`, and reports whether one was removed; other origins are untouched.

**Forget(origin, credential)**: Removes the origin's credential only if it is still *that exact credential*
(used when the server rejected it). Concurrent operations on one server can each hold a different credential; a
rejection of the old one arriving late must not discard a newer credential that works - the race this overload
closes (`AgentControl-SessionCredentialStore-ForgetExactCredential`).

**Clear()**: Removes every credential and withdraws their secrets, and forgets which look-ups ran; `App` calls it
when the desktop lifetime exits.

#### Error Handling

A null argument throws `ArgumentNullException`; every other situation is a normal return value. A fault in a
look-up is surfaced to every caller waiting for it (the callback treats it as "nothing saved"). Nothing is logged
by the store.

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
