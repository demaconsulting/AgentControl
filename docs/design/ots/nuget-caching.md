## NuGetCaching

This document describes the integration and usage design for the `NuGetCaching` OTS software item
(the DemaConsulting.NuGet.Caching library).

### Purpose

DemaConsulting.NuGet.Caching backs the `NuGetSourceProvider` unit of AgentControl's
`AgentPackageManagement` subsystem: it lists the versions of a package held in a NuGet package
source - a folder feed or a NuGet V3 server - and unpacks a chosen version into a package cache, using
NuGet's own client libraries, so AgentControl does not implement the NuGet protocol, authentication
challenges or package extraction itself (`AgentControl-OTS-NuGetCaching-ListAndCache`,
`AgentControl-OTS-NuGetCaching-ServerAccess`). Like Serilog, it ships as part of the AgentControl
application itself, so it also has a corresponding SysML2 part (`docs/sysml2/model/ots.sysml`).
It is a prerelease library, so it is isolated behind the `IPackageSourceProvider` contract and its
version is pinned exactly in the project file. The `NuGetCredential` type it defines is also used by the
`PackageCredentials` subsystem, which therefore depends on this item too.

### Features Used

- Listing a package's versions (`NuGetCache.ListVersionsAsync`), including prereleases, with the
  listed/hidden flag, for an explicitly named source, without downloading any package.
- Unpacking a chosen version into the global packages folder (`NuGetCache.EnsureCachedAsync`) and
  returning the unpacked package folder.
- `NuGetCacheOptions.Sources` to name exactly one source (a rooted local or UNC folder, or the absolute
  `http`/`https` address of a V3 service index ending in `index.json`), which bypasses any configured
  sources and source mapping, and `Logger` to route the library's diagnostics - wrapped so secrets are
  removed - to AgentControl's logging.
- `NuGetCacheOptions.CredentialCallback`, a per-call delegate the library consults first for an
  address that answers HTTP 401/403 (and, when it yields nothing, falls through to the credential
  providers); AgentControl's `PackageCredentialCallback` is what is supplied. The library invokes it at
  most once per address per call and fails the call, rather than loops, when the credential it supplied is
  rejected.
- `NuGetCacheOptions.AuthenticationTimeout` to bound each credential exchange (AgentControl uses two
  minutes, long enough for a person to find and type a token), and `NuGetCacheOptions.Interactive` set to
  `false` so credential-provider plug-ins (for example Azure Artifacts) can sign in without opening a window
  of their own.
- `NuGetAuthenticationException` to tell a refused access (401/403) from other failures.
- Honoring the `globalPackagesFolder` setting of the `nuget.config` discovered from the supplied
  root folder, which AgentControl uses to give each source a private package cache, and the credentials of
  a `nuget.config` source with the same address (`packageSourceCredentials`) found by the same discovery.

Not used: signature requirements (`RequireSignedPackages`), which belong to the package-signing work.

### Integration Pattern

The library is consumed as a NuGet package reference in the main project. Every call from
`NuGetSourceProvider` passes an AgentControl-owned root folder (one per source, under the
per-user local application-data folder unless a base folder is supplied) so NuGet's configuration
discovery starts there and never at the target repository or the current directory; the folder
contains a generated `nuget.config` that only redirects the global packages folder into the same
folder. The provider names the configured folder or server address as the only source for each call, verifies the
unpacked package landed inside that private cache (failing closed if the `NUGET_PACKAGES`
environment variable redirected it), and reads the package's `content` folder through
`DirectoryPackageContentReader`, never modifying the package cache. Cancellation tokens are passed
through so a slow or unreachable feed can be abandoned.

NuGet's credential service is **process-wide**. The library registers its own dispatcher for it, once, when
it is first used, and applies each call's `CredentialCallback`, `Interactive` and `AuthenticationTimeout` to
that call only, so AgentControl never registers a credential service itself (a requirement of the
`NuGetSourceProvider` unit, checked by tests) and one source's callback cannot answer another operation's
challenge. For a server the provider makes up to two calls per operation: a silent one, whose callback
answers only from the session store so that NuGet's own providers get their turn, and - only if the server
still refuses and the user can be asked - one whose callback may open the sign-in dialog. The library has no
initialization or disposal requirements of its own. The single-file, self-contained publish used for the
installer includes the library and its NuGet client dependencies.

Known behavior that callers allow for: a 401 on a non-index resource after credentials were supplied makes
the NuGet client retry with its own back-off, which can take several seconds, so the user interface shows
progress and offers cancel; and once a server has authenticated, the NuGet client answers 401 challenges from
other hosts that the server's service index advertises for package resources with the same credential, which
is why users must only add servers they trust (see the `PackageCredentials` design).
