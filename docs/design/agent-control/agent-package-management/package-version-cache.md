### PackageVersionCache

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageVersionCache` is a session-scoped cache decorating `PackageSource`'s version-scanning
logic, keyed by `(sourceDirectory, packageName)`. Per architecture.md's repo-fact caching
strategy, the set of available package versions at the configured source is not repo-scoped
(every repo pinned to the same package name shares the same answer) and may involve slow
network/UNC I/O, so it is cached once per app session rather than rescanned per repo card.
`PackageSource` itself is deliberately left untouched (stateless/pure) — this class is a
decorator, constructed once by `MainWindowViewModel` and shared across every
`RepoCardViewModel` instance for the app session's lifetime.

#### Data Model

**\_cache**: `Dictionary<(string, string), DiscoveredPackage?>` — the "latest discovered
package" result per source/name pair; a cached `null` (no package found) is a valid, distinct
entry from "not yet queried".

**\_packageNamesCache**: `Dictionary<string, IReadOnlyList<string>>` — discovered package
names per source directory.

**\_versionsDescendingCache**: `Dictionary<(string, string), IReadOnlyList<DiscoveredPackage>>`
— the descending-by-version package list per source/name pair.

Not thread-safe; used only from the UI thread, consistent with every other view-model-adjacent
class in this codebase.

#### Key Methods

**GetPackageNames**: Enumerates distinct package base names at a source, consulting/populating
the cache instead of re-scanning on every call
(`AgentControl-PackageVersionCache-CachePackageNames`).

**GetVersionsDescending**: Enumerates a named package's discoverable versions, descending by
version, consulting/populating the cache (`AgentControl-PackageVersionCache-CacheVersions`).

**IsNewerVersionAvailable**: Determines whether a newer version exists than a pinned one,
consulting/populating the cache; semantics otherwise identical to
`PackageSource.IsNewerVersionAvailable` (`AgentControl-PackageVersionCache-CacheNewerVersion`).

**Invalidate**: Clears every cached entry.

- *Parameters*: None.
- *Postconditions*: The next call for any source/package pair re-enumerates the filesystem.
  Called when the package-source path setting changes, since a cached result for the old path
  would otherwise be silently (and incorrectly) reused for the new path
  (`AgentControl-PackageVersionCache-Invalidate`).

#### Error Handling

Every method that scans the filesystem on a cache miss propagates `PackageSource`'s exceptions
unchanged (`ArgumentNullException`, `ArgumentException`, `DirectoryNotFoundException`). A
cache hit returns the previously observed result instead, even if the source directory has
since become unreachable — a deliberate staleness trade-off to avoid re-querying a potentially
slow or offline UNC path on every UI refresh.

#### Dependencies

- **PackageSource** — the underlying scan logic this class decorates.
- **PackageVersion** — used indirectly via `PackageSource`'s comparisons.

#### Callers

- **MainWindowViewModel** — constructs the single shared instance for the app session and
  calls `Invalidate` when the package-source path setting changes.
- **RepoCardViewModel** — calls `IsNewerVersionAvailable` when refreshing the upgrade badge.
- **SelectPackageWindowViewModel** — calls `GetPackageNames`/`GetVersionsDescending` to
  populate its picker lists.
