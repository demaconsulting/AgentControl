## NuGetCaching

This document describes the integration and usage design for the `NuGetCaching` OTS software item
(the DemaConsulting.NuGet.Caching library).

### Purpose

DemaConsulting.NuGet.Caching backs the `NuGetSourceProvider` unit of AgentControl's
`AgentPackageManagement` subsystem: it lists the versions of a package held in a NuGet package
source and unpacks a chosen version into a package cache, using NuGet's own client libraries, so
AgentControl does not implement the NuGet protocol or package extraction itself
(`AgentControl-OTS-NuGetCaching-ListAndCache`). Like Serilog, it ships as part of the AgentControl
application itself, so it also has a corresponding SysML2 part (`docs/sysml2/model/ots.sysml`).
It is a prerelease library, so it is isolated behind the `IPackageSourceProvider` contract and its
version is pinned exactly in the project file.

### Features Used

- Listing a package's versions (`NuGetCache.ListVersionsAsync`), including prereleases, with the
  listed/hidden flag, for an explicitly named source.
- Unpacking a chosen version into the global packages folder (`NuGetCache.EnsureCachedAsync`) and
  returning the unpacked package folder.
- `NuGetCacheOptions.Sources` to name exactly one source (a rooted local or UNC folder in this
  release), which bypasses any configured sources and source mapping, and `Logger` to route the
  library's diagnostics to AgentControl's logging.
- Honoring the `globalPackagesFolder` setting of the `nuget.config` discovered from the supplied
  root folder, which AgentControl uses to give each source a private package cache.

Not used: credential callbacks, interactive prompting, authentication timeouts, signature
requirements, and server (http/https) sources; those belong to later work and are not enabled
by `NuGetSourceProvider`, which also never registers a NuGet credential service of its own (the
service is process-wide).

### Integration Pattern

The library is consumed as a NuGet package reference in the main project. Every call from
`NuGetSourceProvider` passes an AgentControl-owned root folder (one per source, under the
per-user local application-data folder unless a base folder is supplied) so NuGet's configuration
discovery starts there and never at the target repository or the current directory; the folder
contains a generated `nuget.config` that only redirects the global packages folder into the same
folder. The provider names the configured folder as the only source for each call, verifies the
unpacked package landed inside that private cache (failing closed if the `NUGET_PACKAGES`
environment variable redirected it), and reads the package's `content` folder through
`DirectoryPackageContentReader`, never modifying the package cache. Cancellation tokens are passed
through so a slow or unreachable feed can be abandoned. The library has no initialization or
disposal requirements of its own. The single-file, self-contained publish used for the installer
includes the library and its NuGet client dependencies.
