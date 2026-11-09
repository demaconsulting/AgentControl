### PackageSource

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`PackageSource` scans a package source folder's zip files to enumerate package names and
versions, find the latest version of a named package, and detect whether a newer version is
available than a given pinned version. Per architecture.md, the package source is a plain
filesystem path (local, mapped drive, or UNC) — no authentication, checksum, or signature
verification is performed here.

#### Data Model

`PackageSource` holds no instance state; it is a static class. Every member is a pure
function of its parameters plus the filesystem at the moment of the call.

**DiscoveredPackage** (record, same file): `PackageName: string`, `Version: PackageVersion`,
`Location: string`, `SourceAlias: string = ""`, `Revision: string? = null` — describes one package version
discovered at a source. For this static scan `Location` is the zip file path, `SourceAlias` stays empty (the
scan knows nothing of aliases) and `Revision` is `null`; `ZipFolderSourceProvider` stamps the alias, and other
source kinds use `Location` for a provider-defined locator (the version text for a NuGet folder, the tag name for a
Git source). `Revision` is the exact revision of the content known when the version was listed: `null` for every
source that has no such notion (the static zip scan, zip folders, NuGet), and for a Git source the commit the tag
pointed to (40 or 64 lower-case hexadecimal digits). It lets a later listing show that a tag was moved, and lets
`GitSourceProvider.AcquireAsync` refuse a package that the repository does not list at that commit.

#### Key Methods

**EnumeratePackages**: Enumerates all discoverable versions of a named package.

- *Parameters*: `string sourceDirectory`, `string packageName`.
- *Returns*: `IReadOnlyList<DiscoveredPackage>`, in no particular order.
- *Preconditions*: Neither parameter null; `packageName` not empty/whitespace.
- *Postconditions*: Files not matching the `{packageName}-{semver}.zip` naming convention are
  silently skipped, not treated as errors (`AgentControl-PackageSource-EnumeratePackages`).

**FindLatest**: Finds the highest-versioned discoverable package matching a name.

- *Parameters*: `string sourceDirectory`, `string packageName`.
- *Returns*: `DiscoveredPackage?` — the highest-versioned match, or `null`
  (`AgentControl-PackageSource-FindLatest`).

**EnumeratePackageNames**: Enumerates the distinct package base names discoverable at a
source, without requiring the caller to already know a name.

- *Parameters*: `string sourceDirectory`.
- *Returns*: `IReadOnlyList<string>`, ordered `OrdinalIgnoreCase`
  (`AgentControl-PackageSource-DiscoverNames`).
- *Postconditions*: For each zip's base name, scans hyphen positions left-to-right; the first
  hyphen whose suffix parses via `PackageVersion.TryParseStrict` is the split point (name = prefix,
  version = suffix). A file with no valid split point is skipped. This correctly handles
  package names that themselves contain hyphens (e.g. `contoso-agents-extra-1.2.0.zip` →
  name `contoso-agents-extra`, version `1.2.0`), since earlier hyphen candidates fail to parse
  until the last hyphen is reached — a known, accepted "simplicity over precision" limitation
  per architecture.md for the rare case a package-name segment itself parses as a version.

**IsNewerVersionAvailable**: Determines whether a newer version exists than a pinned one.

- *Parameters*: `string sourceDirectory`, `string packageName`, `string pinnedVersion`,
  `out DiscoveredPackage? latest`.
- *Returns*: `bool`.
- *Postconditions*: Returns `false` when no packages are found; returns `true` when
  `pinnedVersion` fails to parse (treated as needing attention rather than silently ignored);
  otherwise compares `latest.Version` against the parsed pin via `PackageVersion.CompareTo`
  (`AgentControl-PackageSource-DetectNewerVersion`).

#### Error Handling

`EnumeratePackages`/`EnumeratePackageNames`/`FindLatest` throw `ArgumentNullException` for a
null `sourceDirectory`/`packageName`, `ArgumentException` for an empty/whitespace
`packageName`, and `DirectoryNotFoundException` when `sourceDirectory` does not exist or is
unreachable (e.g. an offline UNC path). `IsNewerVersionAvailable` never throws for a malformed
`pinnedVersion` string — it deliberately resolves to `true` instead, so the upgrade-available
badge always produces a definite answer.

#### Dependencies

- **PackageVersion** — every zip file-name version is parsed via `PackageVersion.TryParseStrict`
  (so the file-name splitting rules are unaffected by the wider NuGet version grammar), a pinned
  version via `PackageVersion.TryParse`, and versions compared via `PackageVersion.CompareTo`.
- **.NET BCL** — `Directory.EnumerateFiles`, `Path`.

#### Callers

- **ZipFolderSourceProvider** — adapts `EnumeratePackageNames`/`EnumeratePackages` to the
  package-source provider contract; every zip-folder source is reached through it (and, above
  it, through `PackageVersionCache` for memoization), so no other unit calls `PackageSource`
  directly.
