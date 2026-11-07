### NuGetSourceProvider

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`NuGetSourceProvider` serves NuGet packages held in a local or network folder feed, or on a NuGet V3
server, through the package-source provider contract, using the NuGet caching OTS item (see _NuGetCaching_
in the OTS integration design) to list versions and to unpack the chosen package. A NuGet agent package
keeps its content under the package's `content` folder (`content/.github/...`,
`content/release-notes.md`, `content/AGENTS.md`) — the zip layout rooted at `content` — which
`DirectoryPackageContentReader` exposes exactly as a zip's root. A source location is a folder or UNC
path, or the address of a server's V3 service index: an absolute `https` address ending in `index.json`,
with no user information, query string or fragment (`http` only when the source's `AllowInsecureHttp` is
set, because a credential would cross the network unprotected). Listing versions never downloads package
content (`AgentControl-NuGetSourceProvider-ServerListAndAcquire`). For a server the provider also signs in:
it obtains credentials in a defined order, offers them only to the server's own origin, reports each failure
in terms the user can act on, and keeps secrets out of messages and the log
(`AgentControl-NuGetSourceProvider-SignInOrder`, `AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`,
`AgentControl-NuGetSourceProvider-ServerFailures`, `AgentControl-NuGetSourceProvider-SecretsNotLogged`).

#### Data Model

**Alias**, **\_location**: the source's alias and feed location (a folder, or the service index address).

**\_feed**: `FeedOrigin?` — the origin of a server source's address; `null` for a folder. It decides
whether the provider is a server provider at all and is the only origin a credential is ever offered to.

**\_credentials**: `PackageCredentialContext?` — the session credential store and the sign-in prompt, or
`null` for none (a server source then uses only what NuGet itself supplies).

**\_authenticationTimeout**: `TimeSpan` — the longest one credential exchange (a prompt or a credential
plug-in) may take before the operation is stopped; `DefaultAuthenticationTimeout` is two minutes, because a
person has to read the dialog, find a token and type it, yet an abandoned dialog must not hold the operation
forever.

**\_nugetLogger**: `ILogger` — the application logger wrapped in a `SecretRedactingLogger`; the NuGet library
and the provider's own diagnostics only ever see the wrapped logger.

**\_packageIds**: the package IDs the user configured for the source, trimmed, blank-dropped and
de-duplicated case-insensitively. A feed cannot be enumerated cheaply, so these are the packages
offered.

**\_root**: `string` — the AgentControl-owned folder passed to the NuGet library as its starting
folder: `{base}/{key}`, where `{base}` is the supplied base folder or, by default, `nuget` under
AgentControl's per-user local application-data folder, and `{key}` is the sanitized alias plus a
hash of the case-folded alias and normalized location (`BuildSourceKey`). Editing a source's
location therefore starts a fresh cache and two sources never share one
(`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`).

**\_prepared** / **\_prepareLock**: one-time, locked preparation of `_root`.

The provider is otherwise stateless and thread-safe.

#### Key Methods

**Constructor**: Validates the definition (a NuGet source with a non-blank alias) and derives the root.
A server location is checked with `PackageSourceDefinition.GetServerLocationProblem` (https, or http only
when allowed; ends in `index.json`; no credential) and a folder location must be an absolute path; either
failure throws `InvalidOperationException` naming the source and never repeating the location. A non-NuGet
definition throws `ArgumentException`, as does a supplied base folder that is not a fully qualified absolute
path, so the root can never be resolved against the current directory or a target repo
(`AgentControl-NuGetSourceProvider-ValidateLocation`,
`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`).

**ListPackageIdsAsync**: Returns the configured package IDs, sorted
(`AgentControl-NuGetSourceProvider-ListVersions`).

**ListVersionsAsync**: For a folder, verifies the feed folder exists (reporting a missing folder as
`DirectoryNotFoundException`, as the zip provider does). Prepares the root and asks the NuGet library for
every listed version including prereleases, naming the folder or server explicitly as the only source so it
is always queried and no other configured source is consulted. Only the version list is read: no package
content is downloaded. Unlisted versions and versions the version type cannot represent are skipped; each
result is stamped with the alias (`AgentControl-NuGetSourceProvider-ListVersions`,
`AgentControl-NuGetSourceProvider-ServerListAndAcquire`).

**AcquireAsync**: Refuses a package from another source (`ArgumentException`), prepares the
root, and asks the library to unpack the chosen version into the global packages folder named by
the generated configuration, then returns a `DirectoryPackageContentReader` over the unpacked
folder. The call fails closed (`InvalidOperationException`) when the unpacked package did not land
inside this source's own `packages` folder — which happens only when the `NUGET_PACKAGES`
environment variable overrides the generated configuration and so could serve a same-ID,
same-version package from another source (`AgentControl-NuGetSourceProvider-AcquireToPrivateCache`,
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`). A package with no `content` folder
is rejected by the reader.

**PrepareRoot / WriteConfigIfChanged**: Creates `_root` and writes its `nuget.config`, which
contains only a `globalPackagesFolder` entry pointing at `{_root}/packages` — no sources, no
credentials, no trust data. The file is replaced atomically and only when its content differs.

**CreateOptions**: Builds the per-call library options: the source list is exactly the one
location, `Interactive` false (a credential plug-in may sign in silently but never opens its own
window), the redacting logger, and — for a server — the credential callback and `AuthenticationTimeout`.
The library registers its credential dispatcher once for the process and applies these options to the one call
only, so one source's callback and timeout never affect another operation.

**RunAsync** (private): Runs one library call. A folder calls the library directly. A server call is made up
to **twice**, which is what gives the credential order (below): attempt 1 is _silent_ — a
`PackageCredentialCallback` over the session store without a prompt — and only if the server then refuses with
HTTP 401/403 and a prompt is available does attempt 2 run with a callback that may ask the user, once
(`PromptAndRetryAsync`). A credential the user typed is committed to the session store only after attempt 2
succeeded; a credential the server rejected (from the store in attempt 1, or typed in attempt 2) is forgotten.
A caller's cancellation is never turned into a failure.

#### Credential Order

For a server source the credentials come from, in this order:

1. **What NuGet itself knows.** A source in the user's own `nuget.config` whose address equals this source's
   address (compared ignoring case and a trailing slash — the library resolves a configured source by name
   first and otherwise by address, and AgentControl passes the address) contributes its
   `packageSourceCredentials` up front, and installed NuGet credential-provider plug-ins (for example the Azure
   Artifacts credential provider) run **non-interactively** when the server challenges. The configuration
   NuGet reads is whatever its normal discovery finds starting from the AgentControl-owned root - the
   user-level NuGet configuration and any `nuget.config` in a parent folder of the root - never the
   repo's.
2. **A credential the user already typed this session**, supplied by the silent callback of attempt 1.
3. **The sign-in dialog**, supplied by attempt 2 only — at most once per operation — so a plug-in that can
   sign in by itself never causes a dialog.

A credential is only ever sent to a server when that server challenges (HTTP 401); AgentControl never sends
one preemptively.

#### Isolation and Safety Properties

- Every library call passes the AgentControl-owned `_root`, never the target repo or the current
  directory, so no repo `nuget.config` is ever discovered, read or written
  (`AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`).
- The private `packages` folder means a package unpacked for one source (or present in the
  developer's own NuGet cache) can never be served as another source's package.
- The provider never registers a NuGet credential service itself (the service is process-wide and
  is registered by the NuGet library, which applies the per-call callback and timeout it is given) and
  never persists a credential; one test observes that the process-wide service is unchanged after
  listing and acquiring, and another inspects the product assembly's type references to guard
  the design constraint (`AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`).
  `RequireSignedPackages` and `SignerFingerprint` on the definition are persisted but not yet
  enforced; they belong to the package-signing work.
- Callback scoping: the callback answers only requests for the server's own origin and declines every
  other host without asking the user (`AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`).
  **Limit of that guarantee:** AgentControl itself only ever supplies a credential for the configured
  server's origin, but once a server has authenticated, the NuGet client answers HTTP 401 challenges from
  other hosts that the server's own service index advertises for package resources with the same
  credential. This was observed in testing: a second server advertised by the feed's index received an
  `Authorization` header. The index of a feed controls where its package resources are, so users must only
  add servers they trust. The provider's test shows the part AgentControl controls: the user is asked only
  for the feed's own origin.
- Messages and logs: the NuGet library receives a `SecretRedactingLogger`-wrapped logger, and every
  exception message the provider raises names the source alias and the server's origin (scheme, host,
  port) only — never a path, a query string or a credential
  (`AgentControl-NuGetSourceProvider-SecretsNotLogged`). The library's own exception is never kept: a
  `PackageSourceAccessException` stores only a `SanitizedException` description of its cause (redacted type,
  message chain and stack trace), any other exception type the library or the network stack throws during a
  server operation is wrapped the same way as `Unreachable`, and the password being tried is registered with
  the redaction engine while it is in use. The failure is described (and so sanitized) before the rejected
  credential is forgotten.
- The unpacked package is only read, never modified.

#### Error Handling

`DirectoryNotFoundException` for a missing feed folder; `OperationCanceledException` for the caller's
cancellation. Failures of a server are reported as `PackageSourceAccessException` (derived from
`InvalidOperationException`, so existing handlers keep working) carrying a `PackageSourceFailureKind`
(`AgentControl-NuGetSourceProvider-ServerFailures`):

- `AuthenticationFailed` — the server answered 401/403 and no credential was available (a background
  operation, or no prompt), or the credential supplied was rejected (the rejected credential is forgotten);
- `SignInCanceled` — the user cancelled the sign-in dialog;
- `TimedOut` — the sign-in took longer than `AuthenticationTimeout` and the library stopped it (distinguished
  from the caller's own cancellation, which is propagated unchanged);
- `Unreachable` — the server could not be reached or the address did not answer as a NuGet feed (the
  library reports this, and a package that cannot be downloaded, as `InvalidOperationException`);
- `NotFound` is defined in the enumeration for a server that answers but lacks the package; the provider
  itself reports a package the server does not have as an _empty version list_, which the connection test
  turns into "Package not found", and a download that fails is reported as `Unreachable` with a message that
  says the package may no longer exist there.

Other `InvalidOperationException`s come from this unit for the private-cache and layout checks (messages are
safe to show to the user); `ArgumentException`/`ArgumentNullException` for invalid arguments. A 401 on a
non-index resource after a credential was supplied makes the NuGet client retry with its own back-off
(several seconds), so callers show progress and offer cancel.

#### Dependencies

- **NuGetCaching** (OTS) — `NuGetCache.ListVersionsAsync`/`EnsureCachedAsync`, `NuGetCacheOptions`
  (including the per-call credential callback and authentication timeout), `NuGetCredential` and
  `NuGetAuthenticationException`.
- **PackageCredentials** subsystem — `FeedOrigin`, `SessionCredentialStore` and `PackageCredentialContext`,
  `PackageCredentialCallback`, `ICredentialPrompt`, and `SecretRedactingLogger`.
- **DirectoryPackageContentReader** (`RepoSync` subsystem) — the reader it returns.
- **PackageVersion**, **PackageSourceDefinition**, **DiscoveredPackage**, **PackageSourceAccessException** —
  parsing, configuration, results and failure causes.
- **AppLogging** (`Logging` subsystem) — logger passed (redacted) to the library.
- **.NET BCL** — `System.Security.Cryptography`, and XML writing for the generated configuration.

#### Callers

- **PackageSourceRegistry** — creates it for every NuGet source, passing the credential context.
- **MainWindowViewModel** — creates one for the settings window's connection test
  (`CreateConnectionTestProvider`).
