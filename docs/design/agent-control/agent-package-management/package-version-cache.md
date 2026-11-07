### PackageVersionCache

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageVersionCache` is a session-scoped cache of what each configured package source offers,
keyed by `(source alias, package ID)`. Per architecture.md's repo-fact caching strategy, the set
of available package versions at a source is not repo-scoped (every repo pinned to the same
package at the same source shares the same answer) and may involve slow network I/O, so it is
fetched once per app session rather than per repo card. It is constructed once by
`MainWindowViewModel` and shared across every `RepoCardViewModel` and every
`SelectPackageWindowViewModel` for the app session's lifetime. Keying by alias (never by
location, and never by package ID alone) guarantees a result fetched from one source is never
returned for another.

#### Data Model

**\_sources**: `Dictionary<string, SourceEntry>` (case-insensitive alias) — one private
`SourceEntry` per source holding that source's cached package IDs (`null` = not yet queried) and
a `Dictionary<string, IReadOnlyList<DiscoveredPackage>>` of descending version lists by package
ID. A present key with an empty list means "queried, none found", which is distinct from "not
yet queried"; a failed query is never cached.

**\_lock**: `object` — guards the dictionaries and entries so the cache is safe for concurrent
use.

**UpgradeCheck** (record, same file): `IsNewerAvailable: bool`, `Latest: DiscoveredPackage?` —
the answer to the upgrade question.

#### Key Methods

**GetPackageIdsAsync**: Gets the package names a source offers, consulting/populating the
cache (`AgentControl-PackageVersionCache-CachePackageNames`).

- *Parameters*: `PackageSourceRegistry registry`, `string sourceAlias`, `CancellationToken`.
- *Returns*: `Task<IReadOnlyList<string>>`.
- *Postconditions*: The alias is resolved through the registry first, so an alias that is not
  configured fails with `UnknownSourceException` instead of returning cached data
  (`AgentControl-PackageVersionCache-KeyedBySource`).

**GetVersionsDescendingAsync**: Gets a package's versions at a source, highest first,
consulting/populating the cache (`AgentControl-PackageVersionCache-CacheVersions`).

- *Parameters*: `PackageSourceRegistry registry`, `string sourceAlias`, `string packageId`,
  `CancellationToken`.
- *Returns*: `Task<IReadOnlyList<DiscoveredPackage>>`.

**IsNewerVersionAvailableAsync**: Determines whether a newer version exists than a pinned one,
using the cached descending list; the highest entry is the latest
(`AgentControl-PackageVersionCache-CacheNewerVersion`).

- *Parameters*: as `GetVersionsDescendingAsync`, plus `string pinnedVersion`.
- *Returns*: `Task<UpgradeCheck>`.
- *Postconditions*: No packages found gives `IsNewerAvailable = false`; an unparsable pinned
  version gives `true` (treated as needing attention); otherwise the latest is compared with
  the pin via `PackageVersion.CompareTo`, so a pin with a fourth segment or build metadata is
  compared by version rather than treated as unparsable.

**Invalidate()**: Clears every cached entry for every source.

**Invalidate(sourceAlias)**: Clears one source's entry, leaving the others cached
(`AgentControl-PackageVersionCache-Invalidate`, `AgentControl-PackageVersionCache-InvalidateBySource`).

- *Postconditions*: A query already in flight for that source captured the old entry; it writes
  its result into that detached entry, so a stale answer never repopulates the cache.

#### Error Handling

Every method that queries a source on a cache miss propagates the provider's exceptions
unchanged (`DirectoryNotFoundException` for a missing folder, `InvalidOperationException` for
other source failures, `OperationCanceledException`), and `UnknownSourceException` for an
unconfigured alias; argument checks throw `ArgumentNullException`/`ArgumentException`. A cache
hit returns the previously observed result instead, even if the source has since become
unreachable — a deliberate staleness trade-off to avoid re-querying a potentially slow or
offline location on every UI refresh. Two simultaneous misses for the same key may both query;
the later result wins.

#### Dependencies

- **PackageSourceRegistry** — resolves an alias to its provider on a cache miss.
- **IPackageSourceProvider** (via the registry) — the source being cached.
- **PackageVersion** — used to order versions and compare against a pin.

#### Callers

- **MainWindowViewModel** — constructs the single shared instance for the app session and
  calls `Invalidate(sourceAlias)` for each source that is edited or removed when settings are
  applied.
- **RepoCardViewModel** — calls `IsNewerVersionAvailableAsync` when refreshing the upgrade
  badge.
- **SelectPackageWindowViewModel** — calls `GetPackageIdsAsync`/`GetVersionsDescendingAsync` to
  populate its picker lists.
