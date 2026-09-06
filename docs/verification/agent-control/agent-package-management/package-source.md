### PackageSource

#### Verification Approach

`PackageSource` is verified with unit tests defined in `PackageSourceTests.cs`. Every test
creates a unique real temporary directory populated with real (empty-content, correctly
named) zip files, so package discovery is exercised against genuine filesystem state rather
than a mocked file system.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Enumeration and discovery reflect only files physically present in the source folder.
- A missing source folder is reported clearly rather than producing a silently empty result.
- Newer-version detection produces a definite true/false answer for every tested input.

#### Test Scenarios

**PackageSource_EnumeratePackages_MatchesNameAndRejectsMissingFolder**: Enumerating a named
package in a mixed-content directory returns only the matching zip files, and enumerating
against a nonexistent source folder throws `DirectoryNotFoundException`. This scenario is
tested by `PackageSource_EnumeratePackages_MixedDirectory_FindsOnlyMatchingPackage` and
`PackageSource_EnumeratePackages_DirectoryDoesNotExist_ThrowsDirectoryNotFoundException`,
covering `AgentControl-PackageSource-EnumeratePackages`.

**PackageSource_FindLatest_ReturnsHighestOrNullWhenAbsent**: Finding the latest version of a
package with multiple versions present returns the highest, and finding a package with no
match returns null. This scenario is tested by
`PackageSource_FindLatest_MultipleVersions_ReturnsHighest` and
`PackageSource_FindLatest_NoMatchingPackage_ReturnsNull`, covering
`AgentControl-PackageSource-FindLatest`.

**PackageSource_DiscoverNames_SplitsHyphenatedNamesAndSkipsMalformedFiles**: Enumerating
distinct package names in a mixed directory returns a sorted distinct list; a hyphenated
package name is split at the leftmost parseable hyphen; a file with no valid name/version
split point is skipped rather than throwing; an empty directory returns an empty list; and a
nonexistent directory throws `DirectoryNotFoundException`. This scenario is tested by
`PackageSource_EnumeratePackageNames_MixedDirectory_ReturnsDistinctSortedNames`,
`PackageSource_EnumeratePackageNames_HyphenatedPackageName_SplitsAtLeftmostParseableHyphen`,
`PackageSource_EnumeratePackageNames_NoValidSplitPoint_SkipsFile`,
`PackageSource_EnumeratePackageNames_EmptyDirectory_ReturnsEmptyList`, and
`PackageSource_EnumeratePackageNames_DirectoryDoesNotExist_ThrowsDirectoryNotFoundException`,
covering `AgentControl-PackageSource-DiscoverNames`.

**PackageSource_DetectNewerVersion_AlwaysResolvesDefinitely**: A newer version at the source
returns true; a pin already at the latest version returns false; no packages found returns
false; and an unparsable pinned version returns true (always upgradable). This scenario is
tested by `PackageSource_IsNewerVersionAvailable_SourceHasNewerVersion_ReturnsTrue`,
`PackageSource_IsNewerVersionAvailable_PinIsCurrent_ReturnsFalse`,
`PackageSource_IsNewerVersionAvailable_NoPackagesFound_ReturnsFalse`, and
`PackageSource_IsNewerVersionAvailable_UnparsablePin_ReturnsTrue`, covering
`AgentControl-PackageSource-DetectNewerVersion`.
