## RepoSync

### Verification Approach

The `RepoSync` subsystem is verified indirectly through its constituent units' test suites
(`PackageZipExtractorTests.cs`, `ReleaseNotesViewerViewModelTests.cs`, and
`GitIgnoreEnsurerTests.cs`), each documented in its own unit-level verification design. The
`ReleaseNotesViewerView` Avalonia view has no dedicated test file and is covered only by the
FlaUI end-to-end release-notes-dialog check described at the system level (see the
`AgentControl` system-level verification design). All `PackageZipExtractor` and
`GitIgnoreEnsurer` tests operate on real temporary directories and real files (zip archives
and `.gitignore` files respectively).

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `PackageZipExtractor`, `ReleaseNotesViewerViewModel`, and
  `GitIgnoreEnsurer` pass with zero failures.
- A sync fully replaces a repo's managed agent-file folders and never partially extracts an
  invalid zip.
- The release-notes dialog always has coherent content, even for a package with no release
  notes.
- A repo's `.gitignore` covers the four managed agent folders after every successful sync,
  without disturbing any pre-existing `.gitignore` content.

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

**RepoSync_GitIgnorePrevention_UnitTestsCoverMarkerScanCreateAppendAndWriteFailure**:
Proactively ensuring a repo's `.gitignore` covers the four managed agent folders is verified by
the `GitIgnoreEnsurer` unit tests (see the `GitIgnoreEnsurer` unit verification design), with
`GitIgnoreEnsurer_Ensure_MarkerAlreadyPresent_DoesNotModifyFile`,
`GitIgnoreEnsurer_Ensure_NoGitIgnoreFile_CreatesFileWithManagedFoldersBlock`,
`GitIgnoreEnsurer_Ensure_ExistingContentWithoutMarker_AppendsBlockPreservingExistingLines`,
`GitIgnoreEnsurer_Ensure_ExistingContentEndsWithBlankLine_AppendsBlockWithoutExtraBlankLine`,
`GitIgnoreEnsurer_Ensure_ExistingContentUsesCrLf_AppendsBlockWithCrLf`,
`GitIgnoreEnsurer_Ensure_ExistingFileHasUtf8Bom_PreservesBomOnWrite`, and
`GitIgnoreEnsurer_Ensure_PathIsDirectory_ThrowsInvalidOperationException` cited directly at
the subsystem level, covering `AgentControl-RepoSync-GitIgnorePrevention` (children:
`AgentControl-GitIgnoreEnsurer-Ensure`).
