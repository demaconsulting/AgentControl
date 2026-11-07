## NuGetCaching Verification

This document provides the verification evidence for the `NuGetCaching` OTS software item.
Requirements for this OTS item are defined in the NuGetCaching OTS Software Requirements
document.

### Required Functionality

DemaConsulting.NuGet.Caching backs the `NuGetSourceProvider` unit of AgentControl's
`AgentPackageManagement` subsystem: it lists the versions of a package held in a NuGet folder
feed and unpacks a chosen version into a package cache whose location is governed by the
configuration found at the starting folder AgentControl supplies.

### Verification Approach

The library does not provide dotnet self-validation, and the repository holds no separate OTS
test project for it. The evidence is therefore the `NuGetSourceProvider` integration tests, which
exercise the real library against a real NuGet folder feed that the tests create on the fly (a
minimal package archive holding a package manifest and the content folder). Each test uses its
own temporary feed and its own AgentControl-owned starting folder, so the tests are parallel-safe
and never touch the developer's global NuGet cache. This evidence is indirect: it proves the
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
