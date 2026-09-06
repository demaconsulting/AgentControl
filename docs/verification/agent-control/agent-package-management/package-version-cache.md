### PackageVersionCache

#### Verification Approach

`PackageVersionCache` is verified with unit tests defined in `PackageVersionCacheTests.cs`.
Every test uses a real temporary directory populated with real zip files, and confirms
caching behavior by checking that a scan result is returned unchanged across repeated calls
even after the underlying directory contents are known to have been read only once (via
directly re-asserting cached values rather than mocking the file system).

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Cached results are reused on repeated calls without redundant re-scanning.
- Invalidation forces the next call to re-scan and reflects updated source-folder contents.
- An uncached call against a missing source folder throws `DirectoryNotFoundException`.

#### Test Scenarios

**PackageVersionCache_CachePackageNames_ReusesResultOnRepeatedCall**: Calling
`GetPackageNames` repeatedly against the same source folder returns the same cached result
without re-scanning. This scenario is tested by
`PackageVersionCache_GetPackageNames_RepeatedCall_ReturnsCachedResultWithoutRescanning`,
covering `AgentControl-PackageVersionCache-CachePackageNames`.

**PackageVersionCache_CacheVersions_DescendingCachedAndMissingDirectoryThrows**: Requesting
descending versions for a package with multiple versions returns them in descending order and
caches the result across repeated calls; an uncached call against a missing source directory
throws `DirectoryNotFoundException`. This scenario is tested by
`PackageVersionCache_GetVersionsDescending_MultipleVersions_ReturnsDescendingAndCached`,
`PackageVersionCache_RepeatedCallSameSource_ReturnsCachedResultWithoutRescanning`, and
`PackageVersionCache_UncachedCallMissingDirectory_ThrowsDirectoryNotFoundException`, covering
`AgentControl-PackageVersionCache-CacheVersions`.

**PackageVersionCache_CacheNewerVersion_UnparsablePinIsAlwaysUpgradable**: Checking for a
newer version using an unparsable pinned version returns true, preserving the underlying
`PackageSource` safety behavior through the cache. This scenario is tested by
`PackageVersionCache_UnparsablePinnedVersion_ReturnsTrue`, covering
`AgentControl-PackageVersionCache-CacheNewerVersion`.

**PackageVersionCache_Invalidate_ForcesReScanAndClearsBothCaches**: Invalidating the cache
forces the next call to re-enumerate the source folder, and clears both the package-names
cache and the descending-versions cache. This scenario is tested by
`PackageVersionCache_Invalidate_ForcesReEnumeration` and
`PackageVersionCache_Invalidate_ClearsPackageNamesAndVersionsDescendingCaches`, covering
`AgentControl-PackageVersionCache-Invalidate`.
