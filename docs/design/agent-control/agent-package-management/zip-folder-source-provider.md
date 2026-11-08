### ZipFolderSourceProvider

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`ZipFolderSourceProvider` serves a folder of `{name}-{semver}.zip` agent package files through
the package-source provider contract. It is an adapter over the existing static `PackageSource`
scan: discovery rules and behavior are exactly those of `PackageSource`, so zip sources behave as
they always have while taking part in the multi-source model. Per architecture.md, the folder is
a plain filesystem path (local, mapped drive, or UNC) and no authentication, checksum, or
signature verification is performed — that trust boundary belongs to whoever maintains the
shared folder. It satisfies `AgentControl-ZipFolderSourceProvider-ListAndAcquire`,
`AgentControl-ZipFolderSourceProvider-ListPackageNames`,
`AgentControl-ZipFolderSourceProvider-AcquireContent`,
`AgentControl-ZipFolderSourceProvider-FolderErrorsNormalized`,
`AgentControl-ZipFolderSourceProvider-CancellationObserved`,
`AgentControl-ZipFolderSourceProvider-AcquireOnlyFromOwnSource`,
`AgentControl-ZipFolderSourceProvider-ZipMustBeInOwnFolder`,
`AgentControl-ZipFolderSourceProvider-RefusesLinkedZipOutsideFolder`,
`AgentControl-ZipFolderSourceProvider-AllowsLinkedZipInsideFolder` and
`AgentControl-ZipFolderSourceProvider-ValidateDefinition`.

#### Data Model

**Alias**: `string` — the alias of the configured source this provider serves.

**\_folder**: `string` — the folder scanned for package zips.

The provider is stateless beyond these two fields and thread-safe.

#### Key Methods

**ListPackageIdsAsync**: Lists the distinct package names found in the folder, sorted
case-insensitively, by running `PackageSource.EnumeratePackageNames` as cancellable background work
(see `RunCancellableAsync` below) so a slow share never blocks the caller
(`AgentControl-ZipFolderSourceProvider-ListPackageNames`).

**ListVersionsAsync**: Lists the versions of one named package, by running
`PackageSource.EnumeratePackages` as cancellable background work and stamping each result with the
source alias. Zip names with a fourth version segment or build metadata are not packages, because
`PackageSource` parses names strictly (`AgentControl-ZipFolderSourceProvider-ListAndAcquire`).

**AcquireAsync**: Opens a discovered package's zip as a package content reader
(`AgentControl-ZipFolderSourceProvider-AcquireContent`).

- *Preconditions*: The package carries this provider's alias and its zip lies directly in this
  provider's folder; otherwise `ArgumentException` is thrown, so content is never fetched on
  behalf of another source or from an arbitrary path
  (`AgentControl-ZipFolderSourceProvider-AcquireOnlyFromOwnSource`,
  `AgentControl-ZipFolderSourceProvider-ZipMustBeInOwnFolder`). The zip itself must not be a
  symbolic link or other reparse point that leads outside the folder: the path is resolved to its
  final target, and a target that is not inside this provider's folder is refused with
  `ArgumentException`, while a link whose target is inside the folder is allowed
  (`AgentControl-ZipFolderSourceProvider-RefusesLinkedZipOutsideFolder`,
  `AgentControl-ZipFolderSourceProvider-AllowsLinkedZipInsideFolder`).
- *Postconditions*: The caller owns and disposes the returned `ZipPackageContentReader`.
- *Residual risk*: The link check runs immediately before the zip is opened, but the check and the
  open are two steps. Someone with the same access as the person running AgentControl who replaces the
  zip with a link in that interval is not detected. This is accepted, as for the repo extractor
  in the `RepoSync` subsystem, because such a person can already edit the shared folder itself, whose
  maintainers are the trust boundary of this source type.

**RunCancellableAsync** (internal static, so its cancellation behavior can be tested directly): Runs a
piece of synchronous file-system work — a folder scan or a zip
open — so that cancellation is observed. The token is checked before any work starts; the work runs on
a worker thread, and when the token is canceled while it is still running the caller gets
`OperationCanceledException` at once instead of waiting for a slow or offline share. The abandoned
work cannot be interrupted and finishes in the background; its result is discarded, and a package
reader that completes after the cancellation is disposed so that it does not keep the zip file
open (`AgentControl-ZipFolderSourceProvider-CancellationObserved`).

#### Error Handling

The provider normalizes failures at its boundary
(`AgentControl-ZipFolderSourceProvider-FolderErrorsNormalized`):

- A missing or unreachable folder, and a source location that is not a usable folder (for example,
  a file), is always `DirectoryNotFoundException`, for listing and for acquisition — also when the
  folder disappeared after the package was listed, in which case the zip reader's own failure is
  not allowed to surface as a different type.
- Any other failure while reading the folder (for example, access denied) or opening the zip (an
  unreadable or invalid zip) is `InvalidOperationException` with a message that is safe to show;
  the raw file-system or zip exception is not exposed.
- Cancellation surfaces as `OperationCanceledException`.
- The constructor throws `ArgumentNullException` for a null definition and `ArgumentException` for
  a blank alias or location or a definition that is not a zip source
  (`AgentControl-ZipFolderSourceProvider-ValidateDefinition`).

#### Dependencies

- **PackageSource** — the scanning logic it adapts.
- **ZipPackageContentReader** (`RepoSync` subsystem) — the reader it returns.
- **PackageSourceDefinition** (subsystem data type) — its configuration.

#### Callers

- **PackageSourceRegistry** — creates it for every zip source.
