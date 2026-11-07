### NuGetSourceProvider

![AgentPackageManagement Structure](AgentPackageManagementView.svg)

#### Purpose

`NuGetSourceProvider` serves NuGet packages held in a local or network folder feed through the
package-source provider contract, using the NuGet caching OTS item (see _NuGetCaching_ in the OTS
integration design) to list versions and to unpack the chosen package. A NuGet agent package
keeps its content under the package's `content` folder (`content/.github/...`,
`content/release-notes.md`, `content/AGENTS.md`) — the zip layout rooted at `content` — which
`DirectoryPackageContentReader` exposes exactly as a zip's root. Only folder and UNC locations
are accepted; a server location is rejected with a clear error because credential handling and
transport safeguards for servers are separate work (`AgentControl-NuGetSourceProvider-RejectServerLocation`).

#### Data Model

**Alias**, **\_location**: the source's alias and folder feed location.

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

**Constructor**: Validates the definition (a NuGet source with a non-blank alias and an absolute
folder location), and derives the root. A server location or a relative path throws
`InvalidOperationException`; a non-NuGet definition throws `ArgumentException`, as does a supplied
base folder that is not a fully qualified absolute path, so the root can never be resolved against
the current directory or a target repo (`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`).

**ListPackageIdsAsync**: Returns the configured package IDs, sorted
(`AgentControl-NuGetSourceProvider-ListVersions`).

**ListVersionsAsync**: Verifies the feed folder exists (reporting a missing folder as
`DirectoryNotFoundException`, as the zip provider does), prepares the root, and asks the NuGet
library for every listed version including prereleases, naming the folder explicitly as the only
source so it is always queried and no other configured source is consulted. Unlisted versions
and versions the version type cannot represent are skipped; each result is stamped with the
alias (`AgentControl-NuGetSourceProvider-ListVersions`).

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
location, non-interactive, no credential callback, with the application logger.

#### Isolation and Safety Properties

- Every library call passes the AgentControl-owned `_root`, never the target repo or the current
  directory, so no repo `nuget.config` is ever discovered, read or written
  (`AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`).
- The private `packages` folder means a package unpacked for one source (or present in the
  developer's own NuGet cache) can never be served as another source's package.
- The provider never registers a NuGet credential service (the service is process-wide) and
  carries no credentials; one test observes that the process-wide service is unchanged after
  listing and acquiring, and another inspects the product assembly's type references to guard
  the design constraint (`AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`). `RequireSignedPackages`,
  `SignerFingerprint` and `AllowInsecureHttp` on the definition are persisted but not yet
  enforced; they belong to server and signing support.
- The unpacked package is only read, never modified.

#### Error Handling

`DirectoryNotFoundException` for a missing feed folder; `OperationCanceledException` for
cancellation; `InvalidOperationException` from the library for a package that cannot be found,
authentication or signature failures, or from this unit for the private-cache and layout checks
(messages are safe to show to the user); `ArgumentException`/`ArgumentNullException` for invalid
arguments.

#### Dependencies

- **NuGetCaching** (OTS) — `NuGetCache.ListVersionsAsync`/`EnsureCachedAsync` and
  `NuGetCacheOptions`.
- **DirectoryPackageContentReader** (`RepoSync` subsystem) — the reader it returns.
- **PackageVersion**, **PackageSourceDefinition**, **DiscoveredPackage** — parsing, configuration
  and results.
- **AppLogging** (`Logging` subsystem) — logger passed to the library.
- **.NET BCL** — `System.Security.Cryptography`, and XML writing for the generated configuration.

#### Callers

- **PackageSourceRegistry** — creates it for every NuGet source.
