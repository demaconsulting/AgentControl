### PackageVersionCache

#### Verification Approach

`PackageVersionCache` is verified with unit tests defined in `PackageVersionCacheTests.cs`.
Most tests use a real temporary directory populated with real zip files behind a real
`PackageSourceRegistry`, and confirm caching behavior by deleting the directory after the first
call and showing that a repeated call still returns the cached result. The multi-source tests
use an in-memory fake provider (`FakePackageSourceProvider`) that counts its calls, so the tests
can assert exactly which source was queried how often, and can hold a query open to create an
in-flight call.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Results are cached and invalidated per source alias; the same package ID at two sources is never
  mixed, and an unconfigured alias fails without contacting any source.
- Invalidating one source leaves the others cached, and an in-flight query for the invalidated
  source does not repopulate the cache.
- Cached results are reused on repeated calls without redundant re-scanning.
- Invalidation forces the next call to re-scan and reflects updated source-folder contents.
- An uncached call against a missing source folder throws `DirectoryNotFoundException`.

#### Test Scenarios

**PackageVersionCache_CachePackageNames_ReusesResultOnRepeatedCall**: Calling
`GetPackageNames` repeatedly against the same source folder returns the same cached result
without re-scanning. This scenario is tested by
`PackageVersionCache_GetPackageIds_RepeatedCall_ReturnsCachedResultWithoutRescanning`,
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

**PackageVersionCache_KeyedBySource_SamePackageIdInTwoSourcesStaysSeparate**: The same package
ID offered by two sources is cached and returned separately for each, each source being queried
once; an alias that is not configured fails with `UnknownSourceException` and no other source is
contacted; a pin with a fourth segment or build metadata is compared by version rather than
treated as unparsable; and no packages found is a definite "no upgrade" answer with no latest
package. This scenario is tested by
`PackageVersionCache_SamePackageIdInTwoSources_CachedPerSourceAlias`,
`PackageVersionCache_UnknownAlias_ThrowsUnknownSourceExceptionWithoutContactingOtherSources`,
`PackageVersionCache_PinWithRevisionOrMetadata_ComparedByVersionNotTreatedAsUnparsable`, and
`PackageVersionCache_NoPackagesFound_ReturnsNoUpgradeAndNoLatest`, covering
`AgentControl-PackageVersionCache-KeyedBySource`.

**PackageVersionCache_InvalidateBySource_OnlyThatSourceIsQueriedAgain**: Invalidating one source
(by alias, case-insensitively) forces only that source to be queried again while the other
remains cached; a query already in flight when its source is invalidated completes without
repopulating the cache, so the next query fetches again. This scenario is tested by
`PackageVersionCache_InvalidateSingleSource_LeavesOtherSourcesCached` and
`PackageVersionCache_InvalidateDuringInFlightFetch_DoesNotCacheStaleResult`, covering
`AgentControl-PackageVersionCache-InvalidateBySource`.

**PackageVersionCache_PinnedListing_CarriesRevisionOfThePinnedVersion**: With a fake source that reports a revision per
version, the upgrade check returns the latest version and the listing entry of the pinned version,
each with its revision,
from one listing call. This scenario is tested by
`PackageVersionCache_IsNewerVersionAvailable_PinnedVersionListed_ReturnsPinnedPackageWithRevision`, covering
`AgentControl-PackageVersionCache-ReportPinnedListing`.

**PackageVersionCache_PinnedListing_NoneWhenNotListedOrNotParsable**: A pinned version the source does not list gives no
pinned entry (the latest version is still reported), and a pin that does not parse as a version gives no pinned entry.
This
scenario is tested by `PackageVersionCache_IsNewerVersionAvailable_PinnedVersionNotListed_ReturnsNoPinnedPackage` and
`PackageVersionCache_IsNewerVersionAvailable_PinDoesNotParse_ReturnsNoPinnedPackage`, covering
`AgentControl-PackageVersionCache-ReportPinnedListing`.
