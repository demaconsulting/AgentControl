## RepoSync

### Verification Approach

The `RepoSync` subsystem is verified indirectly through its constituent units' test suites
(`PackageZipExtractorTests.cs` and `ReleaseNotesViewerViewModelTests.cs`), each documented in
its own unit-level verification design. The `ReleaseNotesViewerView` Avalonia view has no
dedicated test file and is covered only by the FlaUI end-to-end release-notes-dialog check
described at the system level (see the `AgentControl` system-level verification design). All
`PackageZipExtractor` tests operate on real temporary directories and real zip files.

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `PackageZipExtractor` and `ReleaseNotesViewerViewModel` pass with zero
  failures.
- A sync fully replaces a repo's managed agent-file folders and never partially extracts an
  invalid zip.
- The release-notes dialog always has coherent content, even for a package with no release
  notes.

### Test Scenarios

**RepoSync_Sync_UnitTestsCoverFreshExtractReplaceAndInvalidZip**: Blind delete-replace sync
behavior is verified by the `PackageZipExtractor` unit tests (see the `PackageZipExtractor`
unit verification design), with `PackageZipExtractor_Extract_FreshRepo_CreatesManagedFoldersOnly`,
`PackageZipExtractor_Extract_ExistingManagedFolder_ReplacesOldContents`, and
`PackageZipExtractor_Extract_InvalidZipFile_ThrowsInvalidOperationException` cited directly at
the subsystem level, covering `AgentControl-RepoSync-Sync` (children:
`AgentControl-PackageZipExtractor-Extract`,
`AgentControl-PackageZipExtractor-AllManagedFoldersExist`, `AgentControl-Utilities-SafePaths`).

**RepoSync_ReleaseNotes_UnitTestsCoverReadWithoutExtractAndPlaceholder**: Reading release
notes without extracting and displaying them (with a placeholder for a package with none) are
verified by the `PackageZipExtractor` and `ReleaseNotesViewerViewModel` unit tests (see their
respective unit verification designs), with
`PackageZipExtractor_ReadReleaseNotes_EntryPresent_ReturnsContentWithoutExtracting`,
`PackageZipExtractor_ReadReleaseNotes_NoEntry_ReturnsNull`, and
`ReleaseNotesViewerViewModel_Constructor_EmptyReleaseNotes_UsesPlaceholderMessage` cited
directly at the subsystem level, covering `AgentControl-RepoSync-ReleaseNotes` (children:
`AgentControl-PackageZipExtractor-ReadReleaseNotes`,
`AgentControl-ReleaseNotesViewerViewModel-Display`).
