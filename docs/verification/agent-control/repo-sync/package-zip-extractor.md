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
- A zip entry that escapes the repo root, or resolves inside the repo root but outside every
  managed folder, is rejected/skipped without ever writing outside a managed folder, and
  without leaving pre-existing managed folders blind-deleted ahead of a rejected extraction.
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

**PackageZipExtractor_Extract_RejectsPathTraversalWithoutPartialUpgrade**: A zip entry that
textually starts with a managed-folder prefix but uses ".." to resolve outside every managed
folder (while staying under the repo root) is silently skipped rather than extracted. A zip
entry that escapes the repo root entirely is rejected with `InvalidOperationException` before
any managed folder is deleted, leaving a pre-existing managed folder's contents untouched.
This scenario is tested by
`PackageZipExtractor_Extract_TraversalEntryWithinRepoRoot_DoesNotEscapeManagedFolders` and
`PackageZipExtractor_Extract_EntryEscapesRepoRoot_ThrowsBeforeDeletingManagedFolders`, covering
`AgentControl-PackageZipExtractor-Extract`.

**PackageZipExtractor_AllManagedFoldersExist_ReflectsActualPresence**: The check returns true
when all four managed folders are present, and false when some or none are present. This
scenario is tested by
`PackageZipExtractor_AllManagedFoldersExist_AllFourPresent_ReturnsTrue`,
`PackageZipExtractor_AllManagedFoldersExist_SomeMissing_ReturnsFalse`, and
`PackageZipExtractor_AllManagedFoldersExist_NoneExist_ReturnsFalse`, covering
`AgentControl-PackageZipExtractor-AllManagedFoldersExist`.

**PackageZipExtractor_ReadReleaseNotes_ReturnsContentOrNullWithoutExtracting**: Reading
release notes from a zip with a release-notes entry returns its content without extracting
the archive, and reading from a zip with no such entry returns null. This scenario is tested
by `PackageZipExtractor_ReadReleaseNotes_EntryPresent_ReturnsContentWithoutExtracting` and
`PackageZipExtractor_ReadReleaseNotes_NoEntry_ReturnsNull`, covering
`AgentControl-PackageZipExtractor-ReadReleaseNotes`.
