### ZipFolderSourceProvider

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`ZipFolderSourceProvider` serves a folder of `{name}-{semver}.zip` agent package files through
the package-source provider contract. It is an adapter over the existing static `PackageSource`
scan: discovery rules and behavior are exactly those of `PackageSource`, so zip sources behave as
they always have while taking part in the multi-source model. Per architecture.md, the folder is
a plain filesystem path (local, mapped drive, or UNC) and no authentication, checksum, or
signature verification is performed — that trust boundary belongs to whoever maintains the
shared folder.

#### Data Model

**Alias**: `string` — the alias of the configured source this provider serves.

**\_folder**: `string` — the folder scanned for package zips.

The provider is stateless beyond these two fields and thread-safe.

#### Key Methods

**ListPackageIdsAsync**: Lists the distinct package names found in the folder, sorted
case-insensitively, by running `PackageSource.EnumeratePackageNames` on a worker thread so a
slow share never blocks the caller (`AgentControl-ZipFolderSourceProvider-ListAndAcquire`).

**ListVersionsAsync**: Lists the versions of one named package, by running
`PackageSource.EnumeratePackages` on a worker thread and stamping each result with the source
alias. Zip names with a fourth version segment or build metadata are not packages, because
`PackageSource` parses names strictly (`AgentControl-ZipFolderSourceProvider-ListAndAcquire`).

**AcquireAsync**: Opens a discovered package's zip as a package content reader.

- *Preconditions*: The package carries this provider's alias and its zip lies directly in this
  provider's folder; otherwise `ArgumentException` is thrown, so content is never fetched on
  behalf of another source or from an arbitrary path
  (`AgentControl-ZipFolderSourceProvider-AcquireOnlyFromOwnSource`).
- *Postconditions*: The caller owns and disposes the returned `ZipPackageContentReader`.

#### Error Handling

A missing or unreachable folder surfaces as `DirectoryNotFoundException` from `PackageSource`;
an unreadable or invalid zip as `InvalidOperationException` from the zip reader; the constructor
throws `ArgumentNullException` for a null definition and `ArgumentException` for a blank alias or
location or a definition that is not a zip source. Cancellation surfaces as
`OperationCanceledException`.

#### Dependencies

- **PackageSource** — the scanning logic it adapts.
- **ZipPackageContentReader** (`RepoSync` subsystem) — the reader it returns.
- **PackageSourceDefinition** (subsystem data type) — its configuration.

#### Callers

- **PackageSourceRegistry** — creates it for every zip source.
