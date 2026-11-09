## RepoSync

### Verification Approach

The `RepoSync` subsystem is verified indirectly through its constituent units' test suites
(`PackageContentExtractorTests.cs`, `PackageZipExtractorTests.cs`,
`ReleaseNotesViewerViewModelTests.cs`, and `GitIgnoreEnsurerTests.cs`), each documented in its own
unit-level verification design. `ReleaseNotesViewerViewModelTests.cs` also directly exercises
`ReleaseNotesViewer`'s internal `BuildBlockTextBlock` rendering helper (via `InternalsVisibleTo`),
constructing `MarkdownBlock` values and asserting on the resulting `TextBlock`'s `Run` styling,
without constructing the `Window` itself. End-to-end window display is covered only by the FlaUI
release-notes-dialog check described at the system level (see the `AgentControl` system-level
verification design). All `PackageContentExtractor`, `PackageZipExtractor` and `GitIgnoreEnsurer`
tests operate on real temporary directories and real files (zip archives, unpacked package folders,
and `.gitignore` files respectively); the extractor tests also use a small stub reader to present
hostile paths a real package reader would never produce.

### Test Environment

N/A - standard test environment; no external services or hardware required. The linked-folder
tests need directory junctions (Windows, no privilege) or symbolic links (Linux; skipped where the
account may not create them).

### Acceptance Criteria

- All unit tests for `PackageContentExtractor`, `PackageZipExtractor`,
  `ReleaseNotesViewerViewModel`, and `GitIgnoreEnsurer` pass with zero failures.
- An unpacked package folder is applied to a repo exactly as a zip package is, never modifying the
  package, and a hostile path or (where the platform lets a test create one) a symbolic link in
  the package is refused before anything in the repo is deleted.
- A repo whose `.github` folder or a managed folder is a symbolic link or junction is refused
  with nothing deleted or written, at the link target or elsewhere.
- A sync fully replaces a repo's managed agent-file folders and never partially extracts an
  invalid zip.
- The release-notes dialog always has coherent content, even for a package with no release
  notes.
- Heading blocks render every inline run bold, regardless of whether the Markdown source
  explicitly marked them `**bold**`; non-heading blocks bold only explicitly-marked runs.
- A repo's `.gitignore` covers the four managed agent folders after every successful sync,
  without disturbing any pre-existing `.gitignore` content.
- An optional root-level AGENTS.md template is readable without extraction, and is never
  written as part of the managed-folder extraction itself.

### Test Scenarios

**RepoSync_Sync_UnitTestsCoverFreshExtractReplaceAndInvalidZip**: Blind delete-replace sync
behavior is verified by the `PackageZipExtractor` unit tests (see the `PackageZipExtractor`
unit verification design), with `PackageZipExtractor_Extract_FreshRepo_CreatesManagedFoldersOnly`,
`PackageZipExtractor_Extract_ExistingManagedFolder_ReplacesOldContents`, and
`PackageZipExtractor_Extract_InvalidZipFile_ThrowsInvalidOperationException` cited directly at
the subsystem level, covering `AgentControl-RepoSync-Sync` (children:
`AgentControl-PackageZipExtractor-Extract`,
`AgentControl-PackageZipExtractor-AllManagedFoldersExist`, `AgentControl-PackageContentExtractor-ZipOpenFailure`,
`AgentControl-Utilities-SafePaths`).

**RepoSync_ReleaseNotes_UnitTestsCoverReadWithoutExtractAndPlaceholder**: Reading release
notes without extracting and displaying them (with a placeholder for a package with none) are
verified by the `PackageZipExtractor` and `ReleaseNotesViewerViewModel` unit tests (see their
respective unit verification designs), with
`PackageZipExtractor_ReadReleaseNotes_EntryPresent_ReturnsContentWithoutExtracting`,
`PackageZipExtractor_ReadReleaseNotes_NoEntry_ReturnsNull`, and
`ReleaseNotesViewerViewModel_Constructor_EmptyReleaseNotes_UsesPlaceholderMessage` cited
directly at the subsystem level, plus `ReleaseNotesViewer_BuildBlockTextBlock_HeadingBlock_RendersAllRunsBold`
and `ReleaseNotesViewer_BuildBlockTextBlock_BodyBlock_OnlyBoldsMarkedRuns` covering the
heading/body bold-rendering distinction, covering `AgentControl-RepoSync-ReleaseNotes`
(children:
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

**RepoSync_AgentsMdTemplate_UnitTestsCoverReadWithoutExtractAndNeverExtractedToManagedFolders**:
Reading the optional root-level AGENTS.md template without extracting, and confirming it is
never written as part of a managed-folder extraction, is verified by the
`PackageZipExtractor` unit tests (see the `PackageZipExtractor` unit verification design),
with `PackageZipExtractor_ReadAgentsMdTemplate_EntryPresent_ReturnsContentWithoutExtracting`,
`PackageZipExtractor_ReadAgentsMdTemplate_NoEntry_ReturnsNull`, and
`PackageZipExtractor_Extract_FreshRepoWithAgentsMdEntry_DoesNotExtractAgentsMdRootEntry` cited
directly at the subsystem level, covering `AgentControl-RepoSync-AgentsMdTemplate` (children:
`AgentControl-PackageZipExtractor-ReadAgentsMdTemplate`).

**RepoSync_SyncFromFolderPackage_SameResultAsZipAndRefusesHostilePackages**: An unpacked package
folder replaces the managed folders and is left unmodified; the same content supplied as a zip and
as a folder yields identical repo trees; a path that escapes the repo is rejected before any
managed folder is deleted. This scenario is verified by the `PackageContentExtractor` unit tests
(see their unit verification design), with
`PackageContentExtractor_Extract_DirectoryPackageIntoFreshRepo_CreatesManagedFoldersOnly`,
`PackageContentExtractor_Extract_ZipAndDirectoryWithSameContent_ProduceIdenticalRepoTrees`, and
`PackageContentExtractor_Extract_TraversalPathFromReader_ThrowsBeforeDeletingManagedFolders` cited
directly at the subsystem level, covering `AgentControl-RepoSync-SyncFromFolderPackage` (children:
`AgentControl-PackageContentExtractor-ExtractFromFolder`, `-ReplaceManagedFolders`, `-NoRootFilesCopied`,
`-PackageNeverModified`, `-RejectMissingContentFolder`, `-RejectLinksAndTraversal`, `-RejectPathOutsideContent`,
`-RejectLinkedPackageFolder`, `-RejectLinkedPackageDirectory`, `-RejectLinkedPackageFile`, `-RejectBrokenLink`,
`-ReadReleaseNotes`, `-ReadAgentsMdTemplate` (each prefixed `AgentControl-PackageContentExtractor`) and
`AgentControl-Utilities-SafePaths`).

**RepoSync_RefuseLinkedRepoFolders_ChangesNothing**: Extracting a package zip through the
subsystem's `PackageZipExtractor` entry point into a repo whose `.github` folder or a managed
folder is a symbolic link (Linux) or junction (Windows) is refused with an error naming a link; the
link target and the repo's other managed folders are left unchanged, and a linked folder is not
reported by `AllManagedFoldersExist` as existing. This scenario is tested by
`RepoSync_Extract_GitHubFolderIsJunction_RefusesAndLeavesTargetUntouched`,
`RepoSync_Extract_ManagedFolderIsJunction_RefusesAndLeavesTargetUntouched`,
`RepoSync_Extract_GitHubFolderIsSymlink_RefusesAndLeavesTargetUntouched`, and
`RepoSync_Extract_ManagedFolderIsSymlink_RefusesAndLeavesTargetUntouched` (in
`PackageZipExtractorTests.cs`; junction tests run on Windows, symbolic-link tests on Linux and
skip where links cannot be created), covering `AgentControl-RepoSync-RefuseLinkedRepoFolders`
(children: `AgentControl-PackageContentExtractor-RefuseLinkedRepoFolders`,
`AgentControl-PackageContentExtractor-ManagedFoldersExist` and
`AgentControl-PackageContentExtractor-AcceptRepoRootThroughLink`, plus
`AgentControl-PackageContentExtractor-RefuseNonFolderRepoComponent` and
`AgentControl-PackageContentExtractor-RefuseLinkedDestinationFile`, whose unit tests are
described in the `PackageContentExtractor` unit verification design).

**RepoSync_ContentIdentity_ReadersReportTheIdentityOfWhatTheyRead**: A zip reader reports the SHA-256 identity of the
zip, computed from the handle its entries are read from, and a folder reader reports the identity its opener
supplied (or none). This scenario is verified by the `PackageContentExtractor` unit tests (see their unit
verification design), with `ZipPackageContentReader_Open_ValidZip_ReportsSha256OfFileAndReadsEntries` and
`DirectoryPackageContentReader_Open_WithAndWithoutIdentity_ReportsSuppliedIdentityOrNull` cited directly at the
subsystem level, covering `AgentControl-RepoSync-ContentIdentity` (children:
`AgentControl-PackageContentExtractor-ReaderContentIdentity`, `AgentControl-PackageContentExtractor-ZipReaderReleasesHandleOnFailure`,
`AgentControl-PackageContentExtractor-FolderReaderIdentity` and `AgentControl-PackageContentExtractor-FolderReaderNoIdentity`).

**RepoSync_LayoutRoot_ReadsOnlyTheLayoutOfACheckedOutFolder**: A folder shaped like a checked-out repository - the
layout plus `.git`, `.github/workflows`, a README and source files - is opened with `OpenLayoutRoot`: only the four
managed folders and the two root files are listed, the identity supplied by the opener is reported, the package extracts
into a repo without the workflows folder or any `.git`, and release notes and the `AGENTS.md` template are read; a path
outside the layout, including one that climbs out of it to `.git`, is refused; a missing folder or a link as the root is
refused; a link outside the layout (a junction or symbolic link in `docs`) is ignored while the same kind of link in a
managed folder, in place of `.github`, or as `release-notes.md` is refused. This scenario is tested by
`DirectoryPackageContentReader_OpenLayoutRoot_ListsOnlyLayoutAndCarriesIdentity`,
`DirectoryPackageContentReader_OpenLayoutRoot_PathOutsideLayout_IsRefused`,
`DirectoryPackageContentReader_OpenLayoutRoot_MissingFolderOrLink_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_OpenLayoutRoot_JunctionOutsideLayoutIgnoredInsideLayoutRefused` and
`DirectoryPackageContentReader_OpenLayoutRoot_SymlinkOutsideLayoutIgnoredInsideLayoutRefused` (the symbolic-link cases
skip where the operating system does not let the test create links; the junction case runs on Windows, the symbolic-link
cases on Linux and macOS), covering `AgentControl-RepoSync-LayoutRoot` (children: every
`AgentControl-PackageContentExtractor-*` requirement of the layout root, from `ReadLayoutRoot` to
`RefuseLinkAsRootFile`, and `AgentControl-PackageContentExtractor-ExcludeGitFolder`).
