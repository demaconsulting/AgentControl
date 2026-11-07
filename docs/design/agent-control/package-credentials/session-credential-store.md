### SessionCredentialStore

![PackageCredentials Structure](PackageCredentialsView.svg)

#### Purpose

`SessionCredentialStore` is the whole of AgentControl's own credential memory: the credentials the user
typed for package servers, kept in memory only, for the length of the session, so a server that needs a
sign-in asks once per session rather than once per operation. It satisfies
`AgentControl-SessionCredentialStore-InMemoryOnly` and `AgentControl-SessionCredentialStore-ForgetAndClear`.

#### Data Model

**_credentials**: `ConcurrentDictionary<FeedOrigin, NuGetCredential>` - the remembered credentials by
server origin. The dictionary is private and never enumerated by any member, so the credentials cannot be
listed; the type has no serialization support and implements no interface that would write it out.

**Count**: `int` - how many servers have a remembered credential; exposed for display and tests, it reveals
nothing about the credentials.

A `NuGetCredential` (from the NuGetCaching OTS item) holds the user name and secret in an ordinary managed
string, because the NuGet library requires one; such a string cannot be wiped from memory, so the guarantee
is *never persisted*, not *never in process memory*. The store is thread-safe because operations on
different sources run concurrently.

#### Key Methods

**TryGet(origin, out credential)**: Finds the credential remembered for exactly this origin. A different
port, scheme or host finds nothing.

**Set(origin, credential)**: Remembers a credential for an origin, replacing any earlier one. The secret is
registered with `SecretRedactor` (and the replaced secret withdrawn) under a lock so each stored secret is
registered exactly once (`AgentControl-SessionCredentialStore-RegistersSecretsForRedaction`).

**Forget(origin)**: Removes the credential of one origin (used when the server rejected it), withdraws its
secret from `SecretRedactor`, and reports whether one was removed; other origins are untouched.

**Clear()**: Removes every credential and withdraws their secrets; `App` calls it when the desktop lifetime
exits.

#### Error Handling

A null argument throws `ArgumentNullException`; every other situation is a normal return value. Nothing is
logged by the store.

#### Dependencies

- **FeedOrigin** - the key.
- **NuGetCaching** (OTS) - the `NuGetCredential` type.

#### Callers

- **PackageCredentialCallback** - looks up, stores (on `Commit`) and forgets (on `Reject`) credentials.
- **NuGetSourceProvider** - holds one through the `PackageCredentialContext` it is given (or a private,
  empty one when it has none).
- **App** - creates the process-wide instance and clears it at exit.
