## AgentPackageManagement

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

### Overview

The `AgentPackageManagement` subsystem spans `PackageSource.cs` (discovers packages/versions
at a source folder), `PackageVersion.cs` (parses/compares version strings), and
`PackageVersionCache.cs` (caches per-source scan results). It provides the package-discovery
(`AgentControl-AgentPackageManagement-DiscoverPackages`) and version-comparison/upgrade-
detection behavior (`AgentControl-AgentPackageManagement-UpgradeDetection`) shared by the rest
of the application: `LauncherUI` uses it to populate the package-selection window and compute
the upgrade-available badge. The subsystem contains three units: `PackageSource`,
`PackageVersion`, and `PackageVersionCache`.

### Interfaces

**PackageSource.EnumeratePackageNames**: Enumerates the distinct package base names present
in a source folder.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Splits each `*.zip` file's base name at the leftmost hyphen whose suffix parses
  via `PackageVersion.TryParse` (see architecture.md's "Package name/version splitting
  algorithm"), skipping files with no valid split point, and returns the distinct names,
  sorted (`AgentControl-PackageSource-DiscoverNames`).
- *Constraints*: Throws `DirectoryNotFoundException` when the source folder does not exist.

**PackageSource.FindLatest / EnumeratePackages**: Resolves the newest, or all, versions of a
named package.

- *Type*: In-process .NET static methods.
- *Role*: Provider.
- *Contract*: `EnumeratePackages` lists every zip matching a given name; `FindLatest` returns
  the highest-versioned match, or none (`AgentControl-PackageSource-EnumeratePackages`,
  `AgentControl-PackageSource-FindLatest`).
- *Constraints*: `EnumeratePackages` throws `DirectoryNotFoundException` for a missing source
  folder.

**PackageSource.IsNewerVersionAvailable**: Determines whether a newer version than a pinned
one exists at the source.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns `true`/`false` for every input, including no packages found (`false`)
  and an unparsable pinned version (`true`, treated as always upgradable)
  (`AgentControl-PackageSource-DetectNewerVersion`).
- *Constraints*: Never throws for a malformed pinned-version string; the badge/upgrade flow
  must always resolve to a definite answer.

**PackageVersionCache**'s memoized equivalents of the three `PackageSource` interfaces above,
plus **PackageVersionCache.Invalidate**.

- *Type*: In-process .NET instance methods.
- *Role*: Provider.
- *Contract*: Reuse a cached scan result per source folder across repeated calls until
  `Invalidate` is called (e.g. when the configured package source path changes)
  (`AgentControl-PackageVersionCache-CachePackageNames`,
  `AgentControl-PackageVersionCache-CacheVersions`,
  `AgentControl-PackageVersionCache-CacheNewerVersion`,
  `AgentControl-PackageVersionCache-Invalidate`).
- *Constraints*: An uncached call against a missing source folder still throws
  `DirectoryNotFoundException`, matching `PackageSource`'s own behavior.

### Design

`PackageVersion` is a pure value type implementing `IComparable<PackageVersion>`: it parses a
release (`major.minor.patch`) or prerelease (`major.minor.patch-identifier`) string into
components, and orders a release version above a prerelease version with the same numeric
components, matching conventional semantic-versioning precedence. `PackageSource` is a static
class with no persistent state; every method re-scans the filesystem on each call, deriving
package name/version from each zip's filename via `PackageVersion.TryParse` against
progressively shorter name candidates.

`PackageVersionCache` sits in front of `PackageSource` as an instance-level memoization layer:
`MainWindowViewModel` constructs one shared instance so multiple `RepoCardViewModel`s pointed
at the same configured package source do not each re-scan the filesystem independently.
`MainWindowViewModel.ApplySettings` calls `Invalidate` whenever the package source path
itself changes, since previously cached results become stale at that point (architecture.md's
"Repo-fact caching strategy": available package versions are cached once per app session or a
short TTL, refreshed on app start or manual refresh, rather than rescanned per repo card).
