### PackageZipExtractor

#### Verification Approach

`PackageZipExtractor` is verified with unit tests defined in `PackageZipExtractorTests.cs`.
Every test creates a real temporary repo directory and a real temporary package zip file
(built with `System.IO.Compression`), so extraction, folder-presence checks, and release-notes
reading are all exercised against genuine filesystem/zip-archive state.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Extraction fully replaces managed folder contents and creates them when absent.
- An invalid zip file is rejected with a clear exception rather than a partial extraction.
- Folder-presence and release-notes checks reflect genuine filesystem/archive state.

#### Test Scenarios

**PackageZipExtractor_Extract_CreatesOrReplacesManagedFoldersAndRejectsInvalidZip**:
Extracting into a fresh repo creates only the managed folders; extracting again over an
existing managed folder replaces its prior contents; and extracting an invalid (non-zip) file
throws `InvalidOperationException`. This scenario is tested by
`PackageZipExtractor_Extract_FreshRepo_CreatesManagedFoldersOnly`,
`PackageZipExtractor_Extract_ExistingManagedFolder_ReplacesOldContents`, and
`PackageZipExtractor_Extract_InvalidZipFile_ThrowsInvalidOperationException`, covering
`AgentControl-PackageZipExtractor-Extract`.

**PackageZipExtractor_AllManagedFoldersExist_ReflectsActualPresence**: The check returns true
when all four managed folders are present, and false when some or none are present, or when a
managed folder is only reachable through a reparse-point (symlink/junction) repo root,
ancestor, or the folder itself. This scenario is tested by
`PackageZipExtractor_AllManagedFoldersExist_AllFourPresent_ReturnsTrue`,
`PackageZipExtractor_AllManagedFoldersExist_SomeMissing_ReturnsFalse`,
`PackageZipExtractor_AllManagedFoldersExist_NoneExist_ReturnsFalse`,
`PackageZipExtractor_AllManagedFoldersExist_AncestorIsJunctionWithRealFolders_ReturnsFalse`, and
`PackageZipExtractor_AllManagedFoldersExist_ManagedFolderItselfIsJunction_ReturnsFalse`, covering
`AgentControl-PackageZipExtractor-AllManagedFoldersExist`.

**PackageZipExtractor_ReadReleaseNotes_ReturnsContentOrNullWithoutExtracting**: Reading
release notes from a zip with a release-notes entry returns its content without extracting
the archive, and reading from a zip with no such entry returns null. This scenario is tested
by `PackageZipExtractor_ReadReleaseNotes_EntryPresent_ReturnsContentWithoutExtracting` and
`PackageZipExtractor_ReadReleaseNotes_NoEntry_ReturnsNull`, covering
`AgentControl-PackageZipExtractor-ReadReleaseNotes`.

**PackageZipExtractor_Extract_RejectsReparsePointsAtEveryVulnerablePoint**: Extraction refuses
to operate through a reparse point (symlink/junction) wherever one could otherwise let content
be read from or written/deleted outside the repo root: a managed-folder ancestor (e.g. a
linked `.github`), whether empty or already containing real content that must survive; the
repo root itself; a reparse point nested *inside* a managed folder (not just above it); and a
*dangling* link (whose target no longer exists), which a naive `Directory.Exists`-based check
would silently miss. This scenario is tested by
`PackageZipExtractor_Extract_ManagedFolderAncestorIsJunction_ThrowsAndDoesNotWriteThroughLink`,
`PackageZipExtractor_Extract_ManagedFolderAncestorIsJunctionWithExistingContent_DoesNotBlindDeleteThroughLink`,
`PackageZipExtractor_Extract_RepoRootIsJunction_ThrowsAndDoesNotWriteThroughLink`,
`PackageZipExtractor_Extract_ManagedFolderContainsNestedJunction_ThrowsAndDoesNotDeleteThroughLink`,
and `PackageZipExtractor_Extract_ManagedFolderAncestorIsDanglingLink_ThrowsAndDoesNotBypassGuard`,
covering `AgentControl-PackageZipExtractor-Extract`.
