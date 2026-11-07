## PackageCredentials

![PackageCredentials Structure](PackageCredentialsView.svg)

### Overview

The `PackageCredentials` subsystem handles the credentials that package servers ask for. It keeps
that handling in one place so the rules that matter for secrets can be reviewed together: a
credential is never persisted, is offered only to the server it was typed for, is asked for at
most once per operation, and never reaches the application log. It spans `FeedOrigin.cs` (the
server a credential belongs to), `SessionCredentialStore.cs` (in-memory session credentials),
`PackageCredentialCallback.cs` (the per-operation answer to a server's request for credentials)
and `SecretRedactingLogger.cs` (log redaction); the supporting `ICredentialPrompt`,
`CredentialPromptRequest` and `PackageCredentialContext` types (`ICredentialPrompt.cs`,
`PackageCredentialContext.cs`) have no dedicated unit documents and are documented here as
supporting value types. The subsystem contains four units: `FeedOrigin`, `SessionCredentialStore`,
`PackageCredentialCallback` and `SecretRedactingLogger`. It has no user interface of its own: the
sign-in dialog that implements `ICredentialPrompt` belongs to the `LauncherUI` subsystem
(`AgentControl-LauncherUI-SignIn`), and the consumer of the callback is `NuGetSourceProvider` in
the `AgentPackageManagement` subsystem. It provides `AgentControl-PackageCredentials-SessionOnly`,
`AgentControl-PackageCredentials-ScopedToFeedOrigin`,
`AgentControl-PackageCredentials-AskOncePerOperation` and
`AgentControl-PackageCredentials-SecretsNeverLogged`, which together support
`AgentControl-System-CredentialSafety`.

### Interfaces

**ICredentialPrompt.PromptAsync**: Asks the user for a user name and a password or token.

- *Type*: In-process .NET interface (asynchronous).
- *Role*: Consumer (the subsystem calls it; `LauncherUI`'s `AvaloniaCredentialPrompt` implements it).
- *Contract*: Takes a `CredentialPromptRequest` (the `FeedOrigin` the credential will be sent to, the
  alias of the configured source, and whether a credential was already rejected for this server during
  this operation) and a cancellation token, and returns the credential the user entered or no credential
  when the user declined. An implementation shows at most one question at a time, withdraws the question
  when the token is cancelled, and never logs or persists what was entered.
- *Constraints*: Implementations are called from worker threads and marshal to the UI thread themselves.

**PackageCredentialCallback.Callback**: The delegate handed to the NuGet caching library as its
credential callback.

- *Type*: In-process .NET delegate, `Func<Uri, CancellationToken, Task<NuGetCredential?>>`.
- *Role*: Provider (invoked by the NuGet library whenever a server answers HTTP 401 for an address).
- *Contract*: Returns a credential for an address of the server's own origin and no credential for any
  other address; see the unit design (`AgentControl-PackageCredentialCallback-ForeignOriginRefused`,
  `AgentControl-PackageCredentialCallback-TierOrder`, `AgentControl-PackageCredentialCallback-AnswerMemoized`).
- *Constraints*: One instance serves exactly one operation against one server; `Commit` and `Reject`
  are called by the owner of the operation after the outcome is known.

**PackageCredentialContext**: The credential facilities the application shares.

- *Type*: In-process .NET immutable record, a session store plus an optional prompt.
- *Role*: Provider (created once at application start-up and handed down to the places that create
  package source providers).
- *Contract*: A context whose prompt is absent is *silent*: it may use remembered credentials but never
  asks the user. `WithoutPrompt` derives a silent context over the same store, which background work uses
  so that no dialog ever appears unprompted (`AgentControl-LauncherUI-BackgroundChecksDoNotPrompt`).
- *Constraints*: None beyond immutability.

**SecretRedactingLogger**: The logger the NuGet library and the repo card are given, with the redaction
engine and the secret-free exception stand-in that go with it.

- *Type*: In-process .NET `ILogger` implementation wrapping another `ILogger`, plus a static redaction engine
  (`SecretRedactor`) and `SanitizedException`.
- *Role*: Provider.
- *Contract*: Forwards each message, structured property value, scope and exception to the wrapped logger
  with URL user information, query strings, fragments, the complete value of every authorization header
  (whatever the scheme) and every registered secret value removed; the engine also gives the user interface
  `SafeMessage` text and gives `PackageSourceAccessException` a sanitized description of an inner exception
  (`AgentControl-PackageCredentials-SecretsNeverLogged`).
- *Constraints*: An exception is forwarded only as a sanitized stand-in (redacted type, message chain and
  stack trace); the original object, its inner exceptions and its data are never forwarded or kept.

### Design

**Collaboration.** `FeedOrigin` is the key and the scope: it normalizes a feed address to scheme, host
and port. `SessionCredentialStore` maps a `FeedOrigin` to the `NuGetCredential` the user entered.
For each operation the owner (`NuGetSourceProvider`) creates a `PackageCredentialCallback` for the
server's `FeedOrigin`, hands its `Callback` to the NuGet library, and afterwards calls `Commit` when the
operation succeeded or `Reject` when the server refused a credential. The NuGet library may challenge
several addresses and retry; the callback answers the first challenge once (store first, then the
prompt) and shares that answer, including a "no", with every later challenge of the operation.
`SecretRedactingLogger` wraps the application's logger before it is given to the NuGet library (and is the
logger of the repo card), and every secret in use is registered with its `SecretRedactor`: the store
registers a credential for as long as it stores it, and the callback registers a credential the user just
typed until the operation ends (`Commit`, `Reject` or `Release`), so even an unforeseen path cannot put the
password into a log line, a property, a scope or an exception message.

**Silent and prompting attempts.** The callback can be created with or without a prompt. The NuGet
library consults the callback first and only when it yields nothing falls through to its own credential
providers, so a callback that prompted straight away would ask the user before a provider that can sign
in by itself had a chance to. The owner therefore makes a first, silent attempt (the callback answers
from the session store, or with nothing so that NuGet's providers run) and creates a prompting callback
only for a second attempt after the server refused the first. This keeps the order *what NuGet itself
knows, then what the user already typed this session, then ask* (see the `NuGetSourceProvider` design).

**Lifetime of secrets.** The only place the application keeps a typed credential is the
`SessionCredentialStore`, in an ordinary managed string, because the NuGet library takes one; that
cannot be wiped from memory, so the guarantee is *never persisted*, not *never in process memory*.
`App` creates one store and one `PackageCredentialContext` for the process and clears the store when
the desktop lifetime exits. Nothing in this subsystem writes to the settings file, a repo, or a log.

**Security properties and their limit.** The callback scoping guarantees that AgentControl itself
supplies a credential only for requests to the configured server's origin: a redirect target, another
port, another scheme or another sub-domain is answered with no credential and never triggers a prompt.
A credential is only ever sent to a server when that server challenges (HTTP 401), never preemptively
by AgentControl. That guarantee does not extend to what the NuGet client does with a credential it has
already been given: once a server has authenticated, the NuGet client answers HTTP 401 challenges from
other hosts that the server's own service index advertises for package resources with that same
credential. This was observed in testing - a second server named by the feed's index received an
`Authorization` header - and is behavior of the NuGet client rather than of this subsystem. The server's
index therefore controls where package resources are, and a user must only add servers they trust; the
user guide and the `NuGetSourceProvider` design state the same limit.

**Dependencies.** The subsystem depends on the *NuGetCaching* OTS item for the `NuGetCredential` type
(see the NuGetCaching integration design) and on `Microsoft.Extensions.Logging` for `ILogger`. It
depends on no other subsystem. It is consumed by `AgentPackageManagement` (`NuGetSourceProvider` and
`PackageSourceRegistry.Create`), implemented in part by `LauncherUI` (`AvaloniaCredentialPrompt`), and
created and cleared by `App`.
