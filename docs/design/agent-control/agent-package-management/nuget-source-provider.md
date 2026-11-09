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
content (`AgentControl-NuGetSourceProvider-ServerListAndAcquire`,
`AgentControl-NuGetSourceProvider-ServerListDoesNotDownload`). For a server the provider also signs in: it
obtains credentials in a defined order, offers them only to the server's own origin, reports each failure
in terms the user can act on, and keeps secrets out of messages and the log
(`AgentControl-NuGetSourceProvider-SignInOrder`, `AgentControl-NuGetSourceProvider-AskUserOnce`,
`AgentControl-NuGetSourceProvider-CredentialRememberedAfterSuccess`,
`AgentControl-NuGetSourceProvider-NonInteractiveLibrary`,
`AgentControl-NuGetSourceProvider-CredentialOnlyForFeedOrigin`,
`AgentControl-NuGetSourceProvider-ServerFailures`, `AgentControl-NuGetSourceProvider-RejectedCredentialForgotten`,
`AgentControl-NuGetSourceProvider-SignInTimeLimit`,
`AgentControl-NuGetSourceProvider-CallerCancellationNotAFailure`,
`AgentControl-NuGetSourceProvider-SecretsNotLogged`, `AgentControl-NuGetSourceProvider-SecretsNotInExceptions`).
A user who ticked "Remember on this computer" gets a saved sign-in used without a dialog
(`AgentControl-NuGetSourceProvider-SavedSignIn`, `AgentControl-NuGetSourceProvider-SavedSignInLookedUpOnce`,
`AgentControl-NuGetSourceProvider-RejectedSavedSignInRemoved`,
`AgentControl-NuGetSourceProvider-SignInSavedOnlyWhenChosen`). A source can also require signed packages,
optionally from one pinned publisher: the provider enforces that for folder and server sources on every
acquisition, decides the trusted signers from the source's own description alone, and fails closed where
signatures cannot be verified (`AgentControl-NuGetSourceProvider-RequireSigned`,
`AgentControl-NuGetSourceProvider-SignatureCheckedForCachedPackage`,
`AgentControl-NuGetSourceProvider-SignedPackageAccepted`,
`AgentControl-NuGetSourceProvider-UnsignedAcceptedWhenNotRequired`,
`AgentControl-NuGetSourceProvider-SignerPin`, `AgentControl-NuGetSourceProvider-RepositoryCountersignatureNotAPin`,
`AgentControl-NuGetSourceProvider-ChangedPinAppliesToCachedPackages`,
`AgentControl-NuGetSourceProvider-GeneratedSignerConfiguration`,
`AgentControl-NuGetSourceProvider-NoSignerConfigurationWithoutRequirement`,
`AgentControl-NuGetSourceProvider-SignerTrustIsolated`, `AgentControl-NuGetSourceProvider-SignatureFailureReported`,
`AgentControl-NuGetSourceProvider-SignatureFailureNamesPinnedPublisher`,
`AgentControl-NuGetSourceProvider-FailClosedWithoutVerification`,
`AgentControl-NuGetSourceProvider-UnsupportedPlatformDoesNotAffectOpenSources`,
`AgentControl-NuGetSourceProvider-UnreadableFingerprintRefused`). Folder listing and acquisition also satisfy
`AgentControl-NuGetSourceProvider-ListVersions`, `AgentControl-NuGetSourceProvider-ListVersionsUnknownPackageIsEmpty`,
`AgentControl-NuGetSourceProvider-ListVersionsMissingFolderReported`,
`AgentControl-NuGetSourceProvider-ListPackageIds`,
`AgentControl-NuGetSourceProvider-CancellationHonoredBeforeSideEffects`,
`AgentControl-NuGetSourceProvider-AcquireToPrivateCache`, `AgentControl-NuGetSourceProvider-PrivateCachePerSource`,
`AgentControl-NuGetSourceProvider-RejectPackageWithoutContent`,
`AgentControl-NuGetSourceProvider-RejectLinkedCacheFolder`,
`AgentControl-NuGetSourceProvider-ServerUnknownPackageIsEmpty`, `AgentControl-NuGetSourceProvider-ServerAcquire` and
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`; location and cache-root rules satisfy
`AgentControl-NuGetSourceProvider-ValidateLocation`, `AgentControl-NuGetSourceProvider-RejectNonNuGetDefinition`,
`AgentControl-NuGetSourceProvider-PlainHttpRefusedUnlessAllowed`,
`AgentControl-NuGetSourceProvider-ServerAddressMustBeServiceIndex`,
`AgentControl-NuGetSourceProvider-CredentialInAddressRefused`,
`AgentControl-NuGetSourceProvider-HttpsAddressAccepted`, `AgentControl-NuGetSourceProvider-ExplicitCacheRoot`,
`AgentControl-NuGetSourceProvider-CacheRootMustBeAbsolute`, `AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`
and `AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`.

#### Data Model

**Alias**, **\_location**: the source's alias and feed location (a folder, or the service index address).

**\_feed**: `FeedOrigin?` — the origin of a server source's address; `null` for a folder. It decides
whether the provider is a server provider at all and is the only origin a credential is ever offered to.

**\_credentials**: `PackageCredentialContext?` — the session credential store, the sign-in prompt and the
user's own credential store (their Git credential helper), or
`null` for none (a server source then uses only what NuGet itself supplies).

**\_requireSigned**: `bool` — whether packages from this source must be validly signed (a folder source can
require signatures as well as a server).

**\_signerFingerprint**: `string?` — the normalized 64-character SHA-256 fingerprint of the trusted publisher's
(author) certificate, or `null` when any validly signed package is acceptable. It is only set when
`_requireSigned` is.

**\_signaturesSupported**: `bool` — whether the platform can verify signatures (`SignatureVerificationSupport`),
injectable so tests can exercise the fail-closed behavior of a platform that cannot.

**\_authenticationTimeout**: `TimeSpan` — the longest one credential exchange (a prompt or a credential
plug-in) may take before the operation is stopped; `DefaultAuthenticationTimeout` is two minutes, because a
person has to read the dialog, find a token and type it, yet an abandoned dialog must not hold the operation
forever.

**\_nugetLogger**: `ILogger` — the application logger wrapped in a `SecretRedactingLogger`; the NuGet library
and the provider's own diagnostics only ever see the wrapped logger.

**\_packageIds**: the package IDs the user configured for the source, trimmed, blank-dropped and
de-duplicated case-insensitively (a null list, as an explicit JSON null can produce, counts as
none). A feed cannot be enumerated cheaply, so these are the packages
offered.

**\_root**: `string` — the AgentControl-owned folder passed to the NuGet library as its starting
folder: `{base}/{key}`, where `{base}` is the supplied base folder or, by default, `nuget` under
AgentControl's per-user local application-data folder, and `{key}` is the sanitized alias plus a
hash of the case-folded alias and normalized location (`BuildSourceKey`; the location's case is folded only
where the comparison is case-insensitive - a URL's scheme and host, a Windows folder path - so locations that
differ only by case in a URL path or on a case-sensitive file system get separate caches). Editing a source's
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
(`AgentControl-NuGetSourceProvider-ValidateLocation`, `AgentControl-NuGetSourceProvider-PlainHttpRefusedUnlessAllowed`,
`AgentControl-NuGetSourceProvider-ServerAddressMustBeServiceIndex`,
`AgentControl-NuGetSourceProvider-CredentialInAddressRefused`, `AgentControl-NuGetSourceProvider-HttpsAddressAccepted`,
`AgentControl-NuGetSourceProvider-RejectNonNuGetDefinition`, `AgentControl-NuGetSourceProvider-CacheRootMustBeAbsolute`,
`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`). A signer fingerprint that cannot be read as a SHA-256
fingerprint, on a source that requires signatures, throws `InvalidOperationException` (via
`PackageSourceDefinition.GetSignaturePolicyProblem`): ignoring it would silently widen trust from one publisher to
any signer (`AgentControl-NuGetSourceProvider-UnreadableFingerprintRefused`). The fingerprint is normalized once,
here.

**ListPackageIdsAsync**: Returns the configured package IDs, sorted
(`AgentControl-NuGetSourceProvider-ListPackageIds`).

**ListVersionsAsync**: First checks the cancellation token: a token that is already canceled throws
`OperationCanceledException` before any other work, so a canceled call never reports a missing folder,
probes signature support or creates the cache root and its configuration
(`AgentControl-NuGetSourceProvider-CancellationHonoredBeforeSideEffects`). The preflight — for a folder,
verifying the feed folder exists (reporting a missing folder as `DirectoryNotFoundException`, as the zip
provider does), the signature-support probe, and the creation of the root and its configuration — then runs
on a worker thread (`Task.Run`), so a slow or offline network folder cannot block the calling (user
interface) thread (`AgentControl-NuGetSourceProvider-ListVersionsMissingFolderReported`). It asks the NuGet
library for every listed version including prereleases, naming the folder or server explicitly as the only
source so it is always queried and no other configured source is consulted. Only the version list is read: no
package content is downloaded. Unlisted versions and versions the version type cannot represent are skipped; each
result is stamped with the alias; a package the feed does not have yields an empty list
(`AgentControl-NuGetSourceProvider-ListVersions`, `AgentControl-NuGetSourceProvider-ListVersionsUnknownPackageIsEmpty`,
`AgentControl-NuGetSourceProvider-ServerListAndAcquire`, `AgentControl-NuGetSourceProvider-ServerListDoesNotDownload`,
`AgentControl-NuGetSourceProvider-ServerUnknownPackageIsEmpty`). A source that requires signatures on a platform that
cannot verify them refuses to list (below).

**AcquireAsync**: Refuses a package from another source (`ArgumentException`), checks the cancellation token
before any preflight work (the signature-support probe, the creation of the root), as listing does, prepares
the root on a worker thread, and asks the library to unpack the
chosen version into the global packages folder named by
the generated configuration, with the signature requirement set when the source has one, then returns a
`DirectoryPackageContentReader` over the unpacked
folder (`AgentControl-NuGetSourceProvider-AcquireToPrivateCache`,
`AgentControl-NuGetSourceProvider-ServerAcquire`). The call fails closed (`InvalidOperationException`) when the
unpacked package did not land
inside this source's own `packages` folder — which happens only when the `NUGET_PACKAGES`
environment variable overrides the generated configuration and so could serve a same-ID,
same-version package from another source (`AgentControl-NuGetSourceProvider-PrivateCachePerSource`,
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`). Before calling the library it refuses
(`InvalidOperationException`) when `NUGET_PACKAGES` redirects the package cache or the private `packages` folder
is a link, so nothing is written outside the private root. It also refuses (`InvalidOperationException`) a
package whose folder, or any folder between the private cache and it, is a symbolic link or junction, because
a link there still lies lexically inside the cache while redirecting every read
(`AgentControl-NuGetSourceProvider-RejectLinkedCacheFolder`). Last, it computes the package's content identity,
the `sha256:` hash of the single `.nupkg` file the library keeps in the package folder, and hands it to the
reader (`AgentControl-NuGetSourceProvider-ReportContentIdentity`); a package folder with no `.nupkg`, with more
than one, or whose package file is a link, is refused (`InvalidOperationException`) because it cannot be said
which bytes the package is (`AgentControl-NuGetSourceProvider-RefuseUnidentifiablePackage`). The identity describes the
copy this machine unpacked into its private cache, so a
machine whose cache holds different bytes for the same version reports a different identity - the warning that is
wanted. A package with no `content` folder
is rejected by the reader (`AgentControl-NuGetSourceProvider-RejectPackageWithoutContent`). A signature failure is
reported as `SignatureRejected` and nothing is returned for the
package (`AgentControl-NuGetSourceProvider-RequireSigned`,
`AgentControl-NuGetSourceProvider-SignatureCheckedForCachedPackage`).

**EnsureSignatureVerificationPossible** (private): When the source requires signed packages and the platform
cannot verify signatures, throws a `PackageSourceAccessException` of kind `SignatureRejected` whose message says
that verification is not supported on this platform, so no package can be used from the source, and what to do.
It runs at the start of both listing and acquiring. It _fails closed_ on purpose: the alternative - ignoring the
requirement - would install packages the user required to be checked, and a hand-edited settings file can enable the
requirement on any machine (`AgentControl-NuGetSourceProvider-FailClosedWithoutVerification`). A source that
does not require signatures is never affected by the answer
(`AgentControl-NuGetSourceProvider-UnsupportedPlatformDoesNotAffectOpenSources`).

**DescribeSignatureFailure / SummarizeFingerprint** (private): Build the secret-free message for a refused
package: the package and version (when known) and the source alias, then either "not validly signed, but the source
requires signed packages" with the likely causes (unsigned, invalid signature, certificate not trusted on this
computer) or — when a publisher is pinned — "not signed by the trusted publisher (certificate A3AF7AF1…7641)" with
its causes. Only the first eight and last four characters of the fingerprint are shown
(`AgentControl-NuGetSourceProvider-SignatureFailureReported`,
`AgentControl-NuGetSourceProvider-SignatureFailureNamesPinnedPublisher`).

**IsSignatureFailure** (private): Recognizes the library's signature errors — an `InvalidOperationException` whose
message says the package failed signature verification or has no package signature, or whose inner exception is
NuGet's signature exception — only for a source that requires signatures and never for an authentication failure.
Such an error is wrapped as `PackageSourceFailureKind.SignatureRejected` (the original is kept only as a sanitized
description), for folders and servers alike.

**PrepareRoot / WriteConfigIfChanged**: Creates `_root` and writes its `nuget.config`, which
contains a `globalPackagesFolder` entry pointing at `{_root}/packages` — no sources and no credentials — and, **only
when the source requires signed packages**, a `trustedSigners` section that begins with `<clear />` and, when a
publisher is pinned, continues with one `author` entry carrying the pinned certificate fingerprint with
`hashAlgorithm="SHA256"` and `allowUntrustedRoot="false"`. The file is replaced atomically and only when its
content differs, so a changed pin is written into the same root and applies to packages already cached. Why
`<clear />`: the NuGet client merges the trusted signers of every configuration it discovers (the user's, the
machine's and any in a parent folder of the root); without the clear, a signer configured elsewhere would widen
what the source accepts (with a pin) or a list naming only other signers would narrow it (without one). With the
clear, the source's own description is the only authority
(`AgentControl-NuGetSourceProvider-GeneratedSignerConfiguration`,
`AgentControl-NuGetSourceProvider-SignerTrustIsolated`,
`AgentControl-NuGetSourceProvider-SignerPin`, `AgentControl-NuGetSourceProvider-ChangedPinAppliesToCachedPackages`).
A source
that does not require signatures gets neither the section nor the signature requirement
(`AgentControl-NuGetSourceProvider-NoSignerConfigurationWithoutRequirement`).

**CreateOptions**: Builds the per-call library options: the source list is exactly the one
location, `Interactive` false (the library, and so any credential-provider plug-in it runs, can never
open a window of its own; the only sign-in dialog is AgentControl's own prompt,
`AgentControl-NuGetSourceProvider-NonInteractiveLibrary`), the redacting logger, `RequireSignedPackages` from the
source's description, and — for a server — the
credential callback and `AuthenticationTimeout` (`AgentControl-NuGetSourceProvider-SignInTimeLimit`).
The library registers its credential dispatcher once for the process and applies these options to the one call
only, so one source's callback and timeout never affect another operation.

**RunAsync** (private): Runs one library call. A folder calls the library directly (only mapping a signature
failure to words). A server call is made up
to **twice**, which is what gives the credential order (below): attempt 1 is _silent_ — a
`PackageCredentialCallback` over the session store and the user's own credential store, without a prompt — and only
if the server then refuses with
HTTP 401/403 and a prompt is available does attempt 2 run with a callback that may ask the user, once
(`PromptAndRetryAsync`). A credential the user typed is committed to the session store only after attempt 2
succeeded - and handed to the user's credential store at that moment when the user ticked "Remember on this
computer"; a credential the server rejected (from the stores in attempt 1, or typed in attempt 2) is forgotten and,
when it came from the user's credential store, removed from there. The removal is not abandoned just because the
caller cancelled (it has its own time limit). A caller's cancellation is never turned into a failure
(`AgentControl-NuGetSourceProvider-AskUserOnce`, `AgentControl-NuGetSourceProvider-CredentialRememberedAfterSuccess`,
`AgentControl-NuGetSourceProvider-RejectedCredentialForgotten`,
`AgentControl-NuGetSourceProvider-RejectedSavedSignInRemoved`,
`AgentControl-NuGetSourceProvider-SignInSavedOnlyWhenChosen`,
`AgentControl-NuGetSourceProvider-CallerCancellationNotAFailure`).

#### Credential Order

For a server source the credentials come from, in this order:

1. **What NuGet itself knows.** A source in the user's own `nuget.config` whose address equals this source's
   address (compared ignoring case and a trailing slash — the library resolves a configured source by name
   first and otherwise by address, and AgentControl passes the address) contributes its
   `packageSourceCredentials` up front (`AgentControl-NuGetSourceProvider-SignInOrder`). The library is run with
   `Interactive = false`, so that neither it nor any credential-provider plug-in it may run can open a dialog of
   its own. AgentControl does not itself supply, install or test credential-provider plug-ins: what such a
   plug-in does when the library runs it is the library's behavior (see the OTS integration design), and the
   credentials AgentControl supplies are those of the user's NuGet configuration, the session store, the user's
   own credential helper and its own prompt. The configuration
   NuGet reads is whatever its normal discovery finds starting from the AgentControl-owned root - the
   user-level NuGet configuration and any `nuget.config` in a parent folder of the root - never the
   repo's.
2. **A credential the user already typed this session, or saved earlier with their own Git credential helper**,
   supplied by the silent callback of attempt 1. The helper is asked at most once per server per session, without
   any dialog, and also when the operation may not show a dialog (a background check), which is what makes a saved
   sign-in useful (`AgentControl-NuGetSourceProvider-SavedSignIn`).
3. **The sign-in dialog**, supplied by attempt 2 only — at most once per operation — so a plug-in that can
   sign in by itself never causes a dialog. The dialog offers "Remember on this computer" only when a credential
   helper applies to the server.

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
  Signature enforcement likewise relies only on the per-call options and the generated per-source
  configuration (see _PrepareRoot_), never on process-wide or machine-wide NuGet settings.
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
  (`AgentControl-NuGetSourceProvider-SecretsNotLogged`,
  `AgentControl-NuGetSourceProvider-SecretsNotInExceptions`). The library's own exception is never kept: a
  `PackageSourceAccessException` stores only a `SanitizedException` description of its cause (redacted type,
  message chain and stack trace), any other exception type the library or the network stack throws during a
  server operation is wrapped the same way as `Unreachable`, and the password being tried is registered with
  the redaction engine while it is in use. The failure is described (and so sanitized) before the rejected
  credential is forgotten.
- The unpacked package is only read, never modified.

#### Error Handling

`DirectoryNotFoundException` for a missing feed folder; `OperationCanceledException` for the caller's
cancellation (also thrown before any preflight work when the token is already canceled). Failures of a server are
reported as `PackageSourceAccessException` (derived from
`InvalidOperationException`, so existing handlers keep working) carrying a `PackageSourceFailureKind`
(`AgentControl-NuGetSourceProvider-ServerFailures`):

- `AuthenticationFailed` — the server answered 401/403 and no credential was available (a background
  operation, or no prompt), or the credential supplied was rejected (the rejected credential is forgotten);
- `SignInCanceled` — the user cancelled the sign-in dialog;
- `SignatureRejected` — a package was refused for its signature (unsigned, invalid, or not signed by the pinned
  publisher; the message names the package and the source and never a secret), or the source requires signatures on
  a platform that cannot verify them. This is a security refusal, reported for folder and server sources alike and
  never as an availability problem; nothing is installed for the package;
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
  (including the per-call credential callback, authentication timeout and signature requirement),
  `NuGetCredential` and `NuGetAuthenticationException`.
- **PackageCredentials** subsystem — `FeedOrigin`, `SessionCredentialStore` and `PackageCredentialContext`,
  `PackageCredentialCallback`, `ICredentialPrompt`, `IRememberedCredentialStore` (through the context) and
  `SecretRedactingLogger`.
- **SignatureVerificationSupport** — whether the platform can verify signatures (fail-closed decision).
- **DirectoryPackageContentReader** (`RepoSync` subsystem) — the reader it returns.
- **PackageVersion**, **PackageSourceDefinition**, **DiscoveredPackage**, **PackageSourceAccessException** —
  parsing, configuration, results and failure causes.
- **AppLogging** (`Logging` subsystem) — logger passed (redacted) to the library.
- **.NET BCL** — `System.Security.Cryptography`, and XML writing for the generated configuration.

#### Callers

- **PackageSourceRegistry** — creates it for every NuGet source, passing the credential context.
- **MainWindowViewModel** — creates one for the settings window's connection test
  (`CreateConnectionTestProvider`).
