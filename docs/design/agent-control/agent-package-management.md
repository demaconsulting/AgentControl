## AgentPackageManagement

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

### Overview

The `AgentPackageManagement` subsystem models the agent package sources a user has configured
and discovers what each one offers. It spans `PackageSource.cs` (discovers packages/versions at
a zip source folder), `PackageVersion.cs` (parses/compares version strings),
`PackageVersionCache.cs` (caches per-source results), the source providers behind the
`IPackageSourceProvider` contract (`ZipFolderSourceProvider.cs`, `NuGetSourceProvider.cs`) and
`PackageSourceRegistry.cs` (resolves a source alias to its provider) and
`SignatureVerificationSupport.cs` (tells whether the platform can verify package signatures). It provides the
package-discovery (`AgentControl-AgentPackageManagement-DiscoverPackages`) and
version-comparison/upgrade-detection behavior
(`AgentControl-AgentPackageManagement-UpgradeDetection`) shared by the rest of the application:
`LauncherUI` uses it to populate the package-selection window and compute the upgrade-available
badge. It also provides the package-authenticity behavior
(`AgentControl-AgentPackageManagement-SignedPackages`, `AgentControl-AgentPackageManagement-SignaturePlatformSupport`,
which support `AgentControl-System-PackageAuthenticity`). It also provides the content identity of a
delivered package (`AgentControl-AgentPackageManagement-ContentIdentity`, which supports
`AgentControl-System-PackageContentIntegrity`). It also provides the Git repository source - the tags of a repository
that match a pattern as the versions of one package, fetched shallow into a cache keyed by repository and commit
(`AgentControl-AgentPackageManagement-GitSource`, which supports `AgentControl-System-GitSources` and
`AgentControl-System-GitSourceSafety`). The subsystem contains eleven units: `PackageSource`,
`PackageVersion`, `PackageVersionCache`, `PackageSourceRegistry`, `ZipFolderSourceProvider`,
`NuGetSourceProvider`, `SignatureVerificationSupport`, `PackageContentIdentity`, `GitSourceProvider`,
`GitRepositoryAddress` and `GitTagPattern`. The supporting types  `PackageSourceDefinition`,
`PackageSourceType`,
`PackageReference`, `IPackageSourceProvider`, `PackageSourceAccessException` (with its
`PackageSourceFailureKind`) and the `DiscoveredPackage` record have no dedicated
unit documents and are folded into this subsystem-level description. The subsystem depends on the
`PackageCredentials` subsystem (session credentials and the sign-in prompt, consumed by
`NuGetSourceProvider`) and on the *NuGetCaching* OTS item.

### Interfaces

**IPackageSourceProvider**: The contract every kind of package source implements.

- *Type*: In-process .NET interface.
- *Role*: Provider (implemented by `ZipFolderSourceProvider`, `NuGetSourceProvider` and `GitSourceProvider`;
  consumed through `PackageSourceRegistry` and `PackageVersionCache`).
- *Contract*: Asynchronous, cancellable operations to list a source's package IDs, list one
  package's versions (each stamped with the source alias), and make one discovered package's
  content available as a package reader. A provider serves exactly one configured source and
  never consults another; it refuses a package discovered at a different source, and never
  writes to a repo or persists credentials.
- *Constraints*: Missing folders surface as `DirectoryNotFoundException`, other failures as
  `InvalidOperationException` with a user-safe message. A server source reports the cause as a
  `PackageSourceAccessException` (derived from `InvalidOperationException`), whose `PackageSourceFailureKind`
  - `AuthenticationFailed`, `SignInCanceled`, `NotFound`, `Unreachable`, `TimedOut`, `SignatureRejected` - lets a
  caller react
  (the connection test, the Select Package window, the card's background check) and whose message never
  contains a secret or more of the server's address than its scheme, host and port.
- *Content identity.* The reader that `AcquireAsync` returns reports a `ContentIdentity` (see *Content identity*
  below); `null` means the provider cannot name one, which is never a change.

**SignatureVerificationSupport.IsSupported**: Tells whether package signatures can be verified here.

- *Type*: In-process .NET static property.
- *Role*: Provider.
- *Contract*: `true` on Windows, Linux and macOS when the platform's certificate-chain facility works, `false`
  elsewhere; `UnsupportedMessage` is the one explanation used wherever the signature requirement cannot be honored
  (`AgentControl-AgentPackageManagement-SignaturePlatformSupport`).
- *Constraints*: Stateless; never throws.

**PackageSourceRegistry.Resolve**: Resolves a source alias to the provider of exactly that
source.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Returns the provider for a configured alias (case-insensitive); an alias that is
  not configured is an error (`UnknownSourceException`) and no other source is ever substituted
  or contacted (`AgentControl-AgentPackageManagement-SourceResolution`).
- *Constraints*: Duplicate or blank aliases and missing entries are rejected when the registry is built, and the
  registry works from a private snapshot of the definitions, so later changes to the settings objects it was
  built from cannot change what an alias resolves to.

**PackageSource.EnumeratePackageNames**: Enumerates the distinct package base names present
in a zip source folder.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Splits each `*.zip` file's base name at the leftmost hyphen whose suffix parses
  via `PackageVersion.TryParseStrict` (see architecture.md's "Package name/version splitting
  algorithm"), skipping files with no valid split point, and returns the distinct names,
  sorted (`AgentControl-PackageSource-DiscoverNames`).
- *Constraints*: Throws `DirectoryNotFoundException` when the source folder does not exist.

**PackageSource.FindLatest / EnumeratePackages**: Resolves the newest, or all, versions of a
named package in a zip source folder.

- *Type*: In-process .NET static methods.
- *Role*: Provider.
- *Contract*: `EnumeratePackages` lists every zip matching a given name; `FindLatest` returns
  the highest-versioned match, or none (`AgentControl-PackageSource-EnumeratePackages`,
  `AgentControl-PackageSource-FindLatest`).
- *Constraints*: `EnumeratePackages` throws `DirectoryNotFoundException` for a missing source
  folder.

**PackageSource.IsNewerVersionAvailable**: Determines whether a newer version than a pinned
one exists at a zip source folder.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns `true`/`false` for every input, including no packages found (`false`)
  and an unparsable pinned version (`true`, treated as always upgradable)
  (`AgentControl-PackageSource-DetectNewerVersion`).
- *Constraints*: Never throws for a malformed pinned-version string; the badge/upgrade flow
  must always resolve to a definite answer.

**PackageVersionCache**'s memoized, per-source equivalents of the discovery interfaces above,
plus **PackageVersionCache.Invalidate**.

- *Type*: In-process .NET instance methods (asynchronous).
- *Role*: Provider.
- *Contract*: Reuse a cached result per `(source alias, package ID)` across repeated calls until
  `Invalidate` clears everything or `Invalidate(alias)` clears one source
  (`AgentControl-PackageVersionCache-CachePackageNames`,
  `AgentControl-PackageVersionCache-CacheVersions`,
  `AgentControl-PackageVersionCache-CacheNewerVersion`,
  `AgentControl-PackageVersionCache-Invalidate`,
  `AgentControl-PackageVersionCache-KeyedBySource`,
  `AgentControl-PackageVersionCache-InvalidateBySource`).
- *Constraints*: An uncached call against a missing source folder still throws
  `DirectoryNotFoundException`, matching the providers' own behavior.

### Design

**Source model.** A configured source is a `PackageSourceDefinition`: a unique case-insensitive
`Alias`, a `Type` (`Zip`, `NuGet` or `Git`, persisted by name), a `Location`, the `PackageIds` a NuGet
source offers (a Git source has exactly one: the package its repository delivers), the Git-only `TagPattern` and
`Subfolder` (`AgentControl-AgentPackageManagement-GitSourceDefinition`), and the trust options
`AllowInsecureHttp`, `RequireSignedPackages` and
`SignerFingerprint`. Whether a location is a folder or a server is derived from the location text
(an absolute `http`/`https` URI is a server; anything else is a folder) rather than persisted, so
the two can never disagree. The definition deliberately has no password, token or other secret
member, and a test guards that property; credentials are obtained from the user (or from NuGet's
own configuration) when needed and never persisted. `GetCredentialProblem` flags a location that embeds a
credential (URL user information, or a server URL with a query string or fragment) so the settings save
paths can refuse it without repeating it in any message, and `GetServerLocationProblem` judges a server
address: an absolute `https` address (`http` only when `AllowInsecureHttp` is set, which is enforced) of a V3
service index, so ending in `index.json`, with no credential - NuGet treats an address that does not end in
`.json` as the older V2 protocol, which AgentControl does not rely on
(`AgentControl-AgentPackageManagement-SourceDefinitions`, `AgentControl-AgentPackageManagement-SourceLocationKind`,
`AgentControl-AgentPackageManagement-SourceNoSecrets`,
`AgentControl-AgentPackageManagement-SourceCredentialBearingLocation`,
`AgentControl-AgentPackageManagement-SourceServerAddress`). `RequireSignedPackages` and `SignerFingerprint`
are the per-source trust options (see *Package authenticity* below). `TryNormalizeFingerprint` accepts the
fingerprint as it is pasted from a tool or certificate viewer - hexadecimal with optional colons, dashes or
spaces, in either letter case - and reduces it to 64 upper-case hexadecimal characters; `GetSignaturePolicyProblem`
reports a fingerprint that cannot be read on a NuGet source that requires signatures, which every consumer treats
as an error rather than ignoring the fingerprint (ignoring it would silently widen trust from one publisher to any
signer (`AgentControl-AgentPackageManagement-SourceFingerprintNormalization`,
`AgentControl-AgentPackageManagement-SourceUnreadableFingerprint`). A
`PackageReference` is the identity a repo pin records: source alias, package ID and the exact
version text — a package ID is never unique across sources, so a package is only ever identified
together with its source. `DiscoveredPackage` is a package version found at a source, carrying
its alias, a provider-defined location (the tag name for a Git source) and an optional `Revision`: the
revision of the content known when the version was listed (the commit of a Git tag), which lets a later listing show
that a tag was moved and lets an acquisition refuse content that is not that commit. `PackageVersionCache`
passes the listing entry of the pinned version on in `UpgradeCheck.Pinned` so a caller can compare revisions without
another call (`AgentControl-PackageVersionCache-ReportPinnedListing`).

**Providers.** `ZipFolderSourceProvider` adapts the unchanged static `PackageSource` scan to the
provider contract (`AgentControl-AgentPackageManagement-ZipFolderSource`); it reports an unusable folder as
`DirectoryNotFoundException` and every other failure as `InvalidOperationException`, honors cancellation
while a slow share is being read, and refuses a package zip that is a link leading outside its folder.
`NuGetSourceProvider` wraps the NuGet caching OTS item for folder feeds and for NuGet V3 servers, keeping
all of its state in AgentControl-owned, per-source storage and never touching the target repo
(`AgentControl-AgentPackageManagement-NuGetFolderSource`). For a server it additionally signs in, using in
order what NuGet itself knows (a `nuget.config` source with the same address; the library runs
non-interactively, so nothing it does can open a dialog of its own), the credential the user already typed this
session or saved earlier with their own Git credential helper, and only then the
sign-in dialog - at most once per operation - through the `PackageCredentials` subsystem, and maps every
failure to a `PackageSourceAccessException` with a secret-free message
(`AgentControl-AgentPackageManagement-NuGetServerSource`, `AgentControl-AgentPackageManagement-NuGetServerAcquire`,
`AgentControl-AgentPackageManagement-NuGetServerTransportPolicy`,
`AgentControl-AgentPackageManagement-NuGetServerSignIn`,
`AgentControl-AgentPackageManagement-NuGetServerCredentialRetention`,
`AgentControl-AgentPackageManagement-NuGetServerOriginScoping`,
`AgentControl-AgentPackageManagement-NuGetServerFailures`,
`AgentControl-AgentPackageManagement-NuGetServerSecretSafety`). Listing a server's versions never downloads a
package. Both hand a package's content to the
`RepoSync` subsystem as an `IPackageContentReader`.

**Content identity.** Nothing in a folder of zip files or a NuGet feed keeps a published version unchanged, so
every package a zip or NuGet source delivers carries a *content identity*: `sha256:` followed by the lower-case
hexadecimal SHA-256 of the package file (`AgentControl-AgentPackageManagement-ContentIdentity`). The decisions:

- *Which bytes.* For a zip source, the zip file itself, hashed by `ZipPackageContentReader` from the one open handle
  its entries are then read from, so the identity describes exactly the bytes that are extracted. For a NuGet
  source, the single `.nupkg` file in the package's folder of the source's private cache, which is the copy this
  machine unpacked (a machine whose cache holds other bytes for the same version reports a different identity,
  which is the warning that is wanted); a folder with none or several, or a package file that is a link, is
  refused rather than recorded under a guessed hash.
- *Why not the unpacked files.* The package file is one stable thing to hash on every platform; hashing a tree
  would depend on file order, attributes and line endings.
- *The comparison.* `PackageContentIdentity.Compare(pinned, actual)` answers *not comparable* when either side is
  missing, *same*, or *changed*; callers (the repo card) decide what to do. A missing identity is never a change,
  so pins made before identities were recorded, and sources that report none, cause no warning.
- *Git.* `PackageContentIdentity.FromGitCommit` formats `git-commit:` plus the commit a tag pointed to, which
  `GitSourceProvider` reports as the identity of what it delivered; the comparison treats identities of different
  kinds as different.
- *Not a signature.* The identity detects a republished or altered package; it does not prove who published it
  (see *Package authenticity* for that).

**Git repository source.** `GitSourceProvider` (with `GitRepositoryAddress` and `GitTagPattern`, over the hardened
`GitRemoteClient` of the `GitIntegration` subsystem) makes one configured repository one package. The decisions:

- *One source, one package.* The package ID is the single entry of `PackageIds`, so the cache keys, the package list
  and the pin keep their existing meaning; the user picks the name.
- *Tags are versions.* The tags that match the tag pattern (a literal prefix and suffix around `{version}`, strict
  semantic versions only) are listed with one remote query, no clone. The commit of a tag is the version's
  `Revision`; an annotated tag is peeled to its commit because the tag object changes whenever a tag is re-created.
- *Nothing from the repository runs.* The temporary repository is created from an empty template, hooks are disabled,
  submodules and filters are off and only the transport of the configured address is allowed - all enforced by
  `GitRemoteClient` per command. The user's own credential helper and ssh keys supply the sign-in; AgentControl
  stores none and the address may not contain one.
- *Only the layout is read.* The tree of the fetched commit is examined *before* anything is checked out: a symbolic
  link, a submodule or a `.git` entry in the layout (the four managed folders, `release-notes.md`, `AGENTS.md`, below
  the optional sub-folder) is refused, and so are two entries whose names differ only in letter case or Unicode form
  (a case-insensitive or normalizing file system would merge them, and the cache may be read on one whatever system
  wrote it); only the layout paths are checked out, `.git` is deleted and verified gone - if it cannot be removed the
  acquisition fails and nothing is published - and the result is read
  through `DirectoryPackageContentReader.OpenLayoutRoot`, which lists and reads only those paths. Links elsewhere in the
  repository are never checked out and so never matter.
- *Cache.* `{CacheRoot}/{addressKey}/{commit}` with a completeness marker, written to a temporary folder and renamed;
  the five most recently used commits per repository are kept. The key is a hash of the address, so two sources never
  share a folder, and a commit never needs fetching twice.
- *Moved tags.* If the tag no longer points to the listed commit when it is fetched the acquisition is refused; a
  later listing shows the new commit as the pinned version's revision, which the card compares with the pin.
- *User-started versus background.* `GitSourceOptions.Interactive` is the `allowPrompt` switch of the card: only work
  the user started lets git show its credential UI; a missing sign-in in the background is
  `PackageSourceFailureKind.AuthenticationFailed`, which the card already shows as "sign-in needed".
- *Git version.* The fetch needs Git 2.29 or newer. When the application sets `GitSourceOptions.EnforceMinimumGitVersion`
  the provider checks the git in use before the first listing or fetch and throws the client's `GitRemoteException` of
  kind `GitTooOld` unchanged, so the UI can recognize it and explain how to update git or choose another one.

**Package authenticity.** A NuGet source (folder or server) can require signed packages and optionally pin the
publisher's certificate. The decisions, in the order a reader will ask about them:

- *Where it is enforced.* In `NuGetSourceProvider`, through the NuGet caching library's own signature requirement,
  on a fresh download and again on every acquisition of a package already held in the source's private cache, so
  turning the requirement on later, or changing the pin, takes effect at once and a package cached earlier is not
  trusted by default. Folder and server sources are treated alike; a zip package carries no signature, so zip
  sources ignore the options (`AgentControl-AgentPackageManagement-SignedPackages`).
- *What the pin is.* The SHA-256 fingerprint of the package **author** certificate (the publisher), not of the
  repository countersignature: the author's signature says who published the package, whereas a repository
  countersignature says only which feed served it. The fingerprint lives in the user's per-source definition in the
  settings file, never in a repo's pin file, so a repo cannot choose its own trusted publisher.
- *Who decides what is trusted.* Only the source's own description. The provider writes a per-source
  `nuget.config` whose `trustedSigners` section begins with `<clear />` and, when a publisher is pinned, adds one
  author entry for that fingerprint. The `<clear />` is essential: without it the NuGet client would merge the
  trusted signers of the user's and the machine's configuration into the result, so with a pinned publisher
  another configured signer would *widen* what the source accepts and, with no pin, a configuration that lists
  only some other signers would *narrow* it from "any validly signed package" to those signers. Tests that fail
  without the `<clear />` prove both directions. A source that does not require
  signatures gets no trust configuration at all.
- *Why pin.* Without a pin, any validly signed package that chains to a trusted root is accepted, including one
  signed by an unrelated publisher; the settings dialog and the user guide say so and recommend pinning.
- *Platforms.* Verification is supported on Windows, Linux and macOS (`SignatureVerificationSupport`). Where it is
  not supported the settings dialog shows the option disabled with an explanation, and `NuGetSourceProvider`
  *fails closed* — it refuses to list or acquire for a source whose settings require signatures — instead of
  skipping the check (`AgentControl-AgentPackageManagement-SignaturePlatformSupport`).
- *Failures.* A refusal is reported as `PackageSourceFailureKind.SignatureRejected` with a message that names the
  package and the source, says whether a publisher is pinned (naming only the start and end of its fingerprint) and
  lists the likely causes; it is never reported as an unreachable or unauthorized source.

**Version handling.** `PackageVersion` is a pure value type implementing
`IComparable<PackageVersion>`: it parses a release (`major.minor.patch`), four-part, prerelease
or build-metadata version string into components and orders a release version above a
prerelease version with the same numeric components, matching conventional semantic-versioning
precedence; metadata never takes part in comparison. Zip file names keep the strict three-part
parse so name/version splitting is unchanged.

**Caching.** `PackageVersionCache` sits in front of the providers as an instance-level
memoization layer: `MainWindowViewModel` constructs one shared instance so multiple
`RepoCardViewModel`s pointed at the same source do not each re-query it independently, and
`MainWindowViewModel.ApplySettings` invalidates only the sources that were edited or removed,
since previously cached results for those become stale (architecture.md's "Repo-fact caching
strategy": available package versions are cached once per app session or a short TTL, refreshed
on app start or manual refresh, rather than rescanned per repo card).

**Resolution.** `PackageSourceRegistry` is built from the configured definitions (and, optionally, the
`PackageCredentialContext` that NuGet server sources use) and resolves an
alias to its provider lazily; callers always go through it, which is what makes "a pin is only
resolved at its own source, and a missing source is an error" a property of one unit rather than
of every caller.

**Security constraints.** Credentials for a server are never persisted by this subsystem, and a credential
is only ever offered to the configured server's own origin. The limit of that guarantee is documented in
the `NuGetSourceProvider` and `PackageCredentials` designs: once a server has authenticated, the NuGet client
may answer 401 challenges from other hosts that the server's service index advertises with the same
credential, so users must only add servers they trust. A credential is only sent when a server challenges,
never preemptively. A 401 on a non-index resource after credentials were supplied makes the NuGet client
retry with its own back-off, which can take several seconds, so callers show progress and let the user
cancel (`AgentControl-LauncherUI-ResponsiveOperations`).
