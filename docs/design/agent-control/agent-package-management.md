## AgentPackageManagement

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

### Overview

The `AgentPackageManagement` subsystem models the agent package sources a user has configured
and discovers what each one offers. It spans `PackageSource.cs` (discovers packages/versions at
a zip source folder), `PackageVersion.cs` (parses/compares version strings),
`PackageVersionCache.cs` (caches per-source results), the source providers behind the
`IPackageSourceProvider` contract (`ZipFolderSourceProvider.cs`, `NuGetSourceProvider.cs`) and
`PackageSourceRegistry.cs` (resolves a source alias to its provider). It provides the
package-discovery (`AgentControl-AgentPackageManagement-DiscoverPackages`) and
version-comparison/upgrade-detection behavior
(`AgentControl-AgentPackageManagement-UpgradeDetection`) shared by the rest of the application:
`LauncherUI` uses it to populate the package-selection window and compute the upgrade-available
badge. The subsystem contains six units: `PackageSource`, `PackageVersion`,
`PackageVersionCache`, `PackageSourceRegistry`, `ZipFolderSourceProvider`, and
`NuGetSourceProvider`. The supporting types `PackageSourceDefinition`, `PackageSourceType`,
`PackageReference`, `IPackageSourceProvider` and the `DiscoveredPackage` record have no dedicated
unit documents and are folded into this subsystem-level description.

### Interfaces

**IPackageSourceProvider**: The contract every kind of package source implements.

- *Type*: In-process .NET interface.
- *Role*: Provider (implemented by `ZipFolderSourceProvider` and `NuGetSourceProvider`;
  consumed through `PackageSourceRegistry` and `PackageVersionCache`).
- *Contract*: Asynchronous, cancellable operations to list a source's package IDs, list one
  package's versions (each stamped with the source alias), and make one discovered package's
  content available as a package reader. A provider serves exactly one configured source and
  never consults another; it refuses a package discovered at a different source, and never
  writes to a repo or persists credentials.
- *Constraints*: Missing folders surface as `DirectoryNotFoundException`, other failures as
  `InvalidOperationException` with a user-safe message.

**PackageSourceRegistry.Resolve**: Resolves a source alias to the provider of exactly that
source.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Returns the provider for a configured alias (case-insensitive); an alias that is
  not configured is an error (`UnknownSourceException`) and no other source is ever substituted
  or contacted (`AgentControl-AgentPackageManagement-SourceResolution`).
- *Constraints*: Duplicate or blank aliases are rejected when the registry is built.

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
`Alias`, a `Type` (`Zip` or `NuGet`, persisted by name), a `Location`, the `PackageIds` a NuGet
source offers, and the trust options `AllowInsecureHttp`, `RequireSignedPackages` and
`SignerFingerprint`. Whether a location is a folder or a server is derived from the location text
(an absolute `http`/`https` URI is a server; anything else is a folder) rather than persisted, so
the two can never disagree. The definition deliberately has no password, token or other secret
member, and a test guards that property; sources that later need credentials obtain them when
needed and never persist them (`AgentControl-AgentPackageManagement-SourceDefinitions`). A
`PackageReference` is the identity a repo pin records: source alias, package ID and the exact
version text — a package ID is never unique across sources, so a package is only ever identified
together with its source. `DiscoveredPackage` is a package version found at a source, carrying
its alias and a provider-defined location.

**Providers.** `ZipFolderSourceProvider` adapts the unchanged static `PackageSource` scan to the
provider contract (`AgentControl-AgentPackageManagement-ZipFolderSource`).
`NuGetSourceProvider` wraps the NuGet caching OTS item for folder feeds, keeping all of its state
in AgentControl-owned, per-source storage and never touching the target repo
(`AgentControl-AgentPackageManagement-NuGetFolderSource`). Both hand a package's content to the
`RepoSync` subsystem as an `IPackageContentReader`.

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

**Resolution.** `PackageSourceRegistry` is built from the configured definitions and resolves an
alias to its provider lazily; callers always go through it, which is what makes "a pin is only
resolved at its own source, and a missing source is an error" a property of one unit rather than
of every caller.
