### ZipFolderSourceProvider

#### Verification Approach

`ZipFolderSourceProvider` is verified with unit tests defined in `ZipFolderSourceProviderTests.cs`.
Every test uses a real unique temporary directory populated with real zip files (some with real
entries), so listing and acquisition run against genuine filesystem state. The scanning rules
themselves are verified by the `PackageSource` tests; these tests verify what the provider adds:
the source alias, the provider contract, and the checks on what may be acquired.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Listing returns only the named package's zips, labeled with the source alias, and the distinct
  sorted package names; zip names with a fourth version segment or metadata are not packages.
- A missing source folder is reported as `DirectoryNotFoundException`.
- Acquisition returns a reader over the zip's root, and refuses a package from another source or
  a zip outside the source folder.

#### Test Scenarios

**ZipFolderSourceProvider_ListAndAcquire_ReportsPackagesVersionsAndContent**: Listing versions in
a mixed folder returns only the matching package's zips with the alias and zip path; zip names
with a fourth segment or build metadata are skipped; listing names returns the distinct sorted
names; a missing folder throws `DirectoryNotFoundException` from both listings; acquiring a
discovered package returns a reader exposing its managed files and root-level release notes. This
scenario is tested by
`ZipFolderSourceProvider_ListVersions_MixedFolder_ReturnsMatchingPackagesStampedWithAlias`,
`ZipFolderSourceProvider_ListVersions_ExtendedVersionFileNames_AreSkipped`,
`ZipFolderSourceProvider_ListPackageIds_MixedFolder_ReturnsDistinctSortedNames`,
`ZipFolderSourceProvider_List_FolderDoesNotExist_ThrowsDirectoryNotFoundException`, and
`ZipFolderSourceProvider_Acquire_DiscoveredPackage_ReturnsReaderOverZipRoot`, covering
`AgentControl-ZipFolderSourceProvider-ListAndAcquire`.

**ZipFolderSourceProvider_AcquireOnlyFromOwnSource_RefusesForeignPackages**: A package that
names a different source alias is refused with `ArgumentException`; a package whose zip lies in a
different folder is refused; a NuGet-type, blank-location, or null definition is rejected by the
constructor. This scenario is tested by
`ZipFolderSourceProvider_Acquire_PackageFromOtherSource_ThrowsArgumentException`,
`ZipFolderSourceProvider_Acquire_ZipOutsideSourceFolder_ThrowsArgumentException`, and
`ZipFolderSourceProvider_Constructor_InvalidDefinition_Throws`, covering
`AgentControl-ZipFolderSourceProvider-AcquireOnlyFromOwnSource`.
