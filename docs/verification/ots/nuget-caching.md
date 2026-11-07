## NuGetCaching Verification

This document provides the verification evidence for the `NuGetCaching` OTS software item.
Requirements for this OTS item are defined in the NuGetCaching OTS Software Requirements
document.

### Required Functionality

DemaConsulting.NuGet.Caching backs the `NuGetSourceProvider` unit of AgentControl's
`AgentPackageManagement` subsystem: it lists the versions of a package held in a NuGet folder
feed or on a NuGet V3 server and unpacks a chosen version into a package cache whose location is governed by
the configuration found at the starting folder AgentControl supplies. For a server it also obtains
credentials for an address that asks for them through a callback supplied for each call (and from the user's
NuGet configuration and non-interactive credential providers), bounds each credential exchange by a supplied
time, and reports a refused access distinctly. Its credential service is process-wide and is registered by the
library itself.

### Verification Approach

The library does not provide dotnet self-validation, and the repository holds no separate OTS
test project for it. The evidence is therefore the `NuGetSourceProvider` integration tests, which
exercise the real library against a real NuGet folder feed that the tests create on the fly (a
minimal package archive holding a package manifest and the content folder) and, for server access, against an
in-process NuGet V3 mock feed (`MockNuGetFeed`, Kestrel over plain HTTP, with a Basic-authentication mode) in
`NuGetSourceProviderServerTests.cs`. Each test uses its
own temporary feed (or its own mock feed on a fresh port) and its own AgentControl-owned starting folder, so the
tests are parallel-safe and never touch the developer's global NuGet cache; the NuGet HTTP cache is isolated
per test process (`HttpCacheIsolation`). This evidence is indirect: it proves the
behavior AgentControl depends on, not the library's complete behavior.

### Test Scenarios

#### NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias

**Scenario**: A folder feed holds several versions of one package; the provider lists the
versions through the library.

**Expected**: Every version in the feed is returned, proving the library lists a folder feed's
versions.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ListAndCache`.

#### NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder

**Scenario**: A listed package version is acquired through the library, which unpacks it into the
package cache.

**Expected**: The unpacked package folder exists and its content folder holds the files placed in
the package, proving the library caches the chosen version.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ListAndCache`.

#### NuGetSourceProvider_Acquire_UsesExplicitAgentControlOwnedRootWithGeneratedConfig

**Scenario**: A package is acquired with a generated configuration in the starting folder that
names a private package cache.

**Expected**: The package is unpacked inside the private package cache named by that
configuration, proving the library honors the cache location of the starting folder's
configuration.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ListAndCache`.

#### NuGetSourceProvider_ListVersions_AnonymousServer_ReturnsVersionsWithoutDownloading

**Scenario**: A package's versions are listed from an anonymous mock NuGet V3 server through the library.

**Expected**: Every version is returned and the mock records no package download, proving the library lists a
server's versions without downloading a package.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_Acquire_AnonymousServer_DownloadsPackageAndExposesContent

**Scenario**: A listed version is acquired from the mock server through the library.

**Expected**: The package is downloaded and unpacked into the private cache and its content folder is readable,
proving the library downloads a chosen version from a server.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession

**Scenario**: The mock server answers 401 with a Basic challenge; the per-call credential callback supplies a
credential for the first challenged address.

**Expected**: The library invokes the callback, applies its credential to the retried request, and the call
succeeds; the callback is not asked again for the same address within the call, proving the callback mechanism
that AgentControl scopes a credential with.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt

**Scenario**: The callback supplies a wrong password.

**Expected**: The library reports a refused access (`NuGetAuthenticationException`) after a single request for
the credential instead of retrying in a loop, proving access refusal is reported distinctly and promptly.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_SignInTakesTooLong_ThrowsTimedOut

**Scenario**: The callback never answers while a short authentication timeout is set for the call.

**Expected**: The library stops the credential exchange when the timeout elapses and the call ends with a
cancellation that is not the caller's own, proving the per-call time limit is applied.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.

#### NuGetSourceProvider_ListVersions_NuGetConfigHasCredentialsForTheFeed_SignsInWithoutPrompting

**Scenario**: A `nuget.config` found by the library's discovery from the starting folder lists the server's
address as a source with stored credentials, and no credential callback answer is available.

**Expected**: The library signs in with the configured credentials and the call succeeds without any question,
proving the user's NuGet configuration is honored for a source with the same address.

**Requirement coverage**: `AgentControl-OTS-NuGetCaching-ServerAccess`.
