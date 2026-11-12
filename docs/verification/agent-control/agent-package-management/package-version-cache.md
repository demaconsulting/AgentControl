### PackageVersionCache

#### Verification Approach

`PackageVersionCache` is verified with unit tests defined in `PackageVersionCacheTests.cs`.
Most tests use a real temporary directory populated with real zip files behind a real
`PackageSourceRegistry`, and confirm caching behavior by deleting the directory after the first
call and showing that a repeated call still returns the cached result. The multi-source tests
use an in-memory fake provider (`FakePackageSourceProvider`) that counts its calls, so the tests
can assert exactly which source was queried how often, and can hold a query open to create an
in-flight call. The expiry tests move a `FakeTimeProvider` by hand.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Results are cached and invalidated per source alias; the same package ID at two sources is never
  mixed, and an unconfigured alias fails without contacting any source.
- Invalidating one source leaves the others cached, and an in-flight query for the invalidated
  source does not repopulate the cache.
- Listing across the configured sources returns their offers in configuration order, skips a source that cannot be
  read (telling a caller that asks), reports a failure when every source failed (the first refusal of access when
  there was one, otherwise the first failure), and attributes the newest
  version - a tie to the first source configured.
- Cached results are reused on repeated calls without redundant re-scanning, until they are five minutes old; an older
  entry is read from its source again.
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

**PackageVersionCache_UpgradeCheck_LatestCarriesRevisionFromOneListing**: With a fake source that reports a revision per
version, the upgrade check returns the latest version with its revision from one listing call. This scenario is tested
by `PackageVersionCache_IsNewerVersionAvailable_NewerListed_ReturnsLatestWithRevisionFromOneListing`.

**PackageVersionCache_UpgradeCheck_PinNotListedOrNotParsable_StillOffersLatest**: A pinned version the source does not
list does not hide the latest version, and a pin that does not parse as a version is treated as needing attention (the
latest version is offered). This scenario is tested by
`PackageVersionCache_IsNewerVersionAvailable_PinnedVersionNotListed_StillReportsLatest` and
`PackageVersionCache_IsNewerVersionAvailable_PinDoesNotParse_OffersLatest`.

**PackageVersionCache_ListAcrossSources_ConfigurationOrderAndExclusion**: Through a registry of fake sources, listing
across sources returns each source's offers in the order the sources are configured, each source's versions from
highest to lowest, leaves out the source it is told to exclude without contacting it, and reads each source only once.
This scenario is tested by `PackageVersionCache_GetVersionsAcrossSources_ReturnsOffersInConfigurationOrderAndExcludesAlias`,
covering `AgentControl-PackageVersionCache-ListAcrossSources`.

**PackageVersionCache_ListAcrossSources_ToleratesFailingSources**: A source that cannot be read is skipped and the
others' offers are returned; a caller that passes a failure callback is told about each skipped source's failure; when
every consulted source fails a failure is thrown instead of an empty answer - the first refusal of access (HTTP 403)
when a source gave one, so that an earlier unreachable source does not mask it, otherwise the first failure. This
scenario is tested by
`PackageVersionCache_GetVersionsAcrossSources_OneSourceFails_ReturnsTheOthersOffers`,
`PackageVersionCache_GetVersionsAcrossSources_EverySourceFails_ThrowsTheFirstFailure`,
`PackageVersionCache_GetVersionsAcrossSources_EverySourceFailsOneRefusesAccess_ThrowsTheRefusal` and
`PackageVersionCache_GetVersionsAcrossSources_SourceFailureCallback_ReportsEachSkippedSource`, covering
`AgentControl-PackageVersionCache-ListAcrossSourcesTolerateFailure`.

**PackageVersionCache_RoundFailureMemo_FailureRememberedForTheRoundOnly**: With the failure memo of a round, a source
whose read failed is read once however often it is asked (the later askers get the same failure, while another source
is read normally); the memo is keyed by source alias (case-insensitively) and package, so a failure for one package
does not stop another package from being read at that source; and nothing outlives the memo - a read with the next
round's memo, or with no memo, reaches the source again, and once the source recovers the cache serves its listing.
This scenario is tested by `PackageVersionCache_FailureMemo_SourceFailed_IsNotReadAgainAndRethrowsTheSameFailure`,
`PackageVersionCache_FailureMemo_FailureIsNotPersistedBeyondTheMemo` and
`PackageVersionCache_FailureMemo_FailureOfOnePackage_DoesNotAffectAnotherPackage`, covering
`AgentControl-PackageVersionCache-RoundFailureMemo`.

**PackageVersionCache_NewestAcrossSources_NewestWinsAndTieKeepsConfigurationOrder**: The upgrade check across sources
reports the newest version offered by any source, attributes a version that several sources offer to the first of them
in configuration order, and reports no upgrade and no latest version when no source offers the package. This scenario
is tested by `PackageVersionCache_IsNewerVersionAvailableAcrossSources_NewestWinsAndTieKeepsConfigurationOrder` and
`PackageVersionCache_IsNewerVersionAvailableAcrossSources_NobodyOffersPackage_ReportsNoUpgrade`, covering
`AgentControl-PackageVersionCache-NewestAcrossSources`.

**PackageVersionCache_EntriesExpire_AfterFiveMinutes**: With a fake clock (`FakeTimeProvider`, so no test waits), a
package list and a version list that were fetched are served from the cache just under five minutes later, without
reading the source; at five minutes both are read again and show a version and a package published meanwhile; an
entry that was fetched again is served for another five minutes counted from the second fetch, not the first. This
scenario is tested by `PackageVersionCache_EntryYoungerThanFiveMinutes_IsStillServedFromCache`,
`PackageVersionCache_EntryOlderThanFiveMinutes_IsFetchedAgain` and
`PackageVersionCache_EntryFetchedAgainAfterExpiry_IsServedForAnotherFiveMinutes`, covering
`AgentControl-PackageVersionCache-EntriesExpire` (and, through it, the subsystem requirement
`AgentControl-AgentPackageManagement-ListingsExpire`).
