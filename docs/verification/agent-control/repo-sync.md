## RepoSync

### Verification Approach

The `RepoSync` subsystem is verified through its constituent units' test suites
(`PackageContentExtractorTests.cs`, `PackageZipExtractorTests.cs`,
`ReleaseNotesViewerViewModelTests.cs`, and `GitIgnoreEnsurerTests.cs`), each documented in its own
unit-level verification design, and - for the content identity a reader reports and the layout-root form a Git package
is delivered in - by `RepoSyncSubsystemTests.cs`, whose tests use the readers and the extractor together against real
temporary folders and a real zip, the way the application drives them. `ReleaseNotesViewerViewModelTests.cs` also
directly exercises
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
- The content identity a reader reports is the identity its source supplied (or, for a zip, that of exactly the bytes
  read and applied - the file cannot be replaced while it is held), and none when none was supplied.
- A folder whose root is the package layout is read and applied as a package: only the four managed folders and the two
  root files are seen and written, never the folder's metadata or other content; a link in the layout is refused before
  the repo is touched and a link outside it is ignored.

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

**RepoSync_ContentIdentity_ReadersReportTheIdentityOfWhatTheyRead**: With the reader and the extractor together against
real folders and a real zip, a folder reader (an unpacked package folder or a layout root) reports exactly the identity
its opener supplied - a NuGet-style `sha256:` identity or a Git `git-commit:` identity - and none when it was supplied
none, never computing one of its own. This scenario is tested by
`RepoSync_ContentIdentity_FolderReadersReportTheSuppliedIdentityOrNone` in `RepoSyncSubsystemTests.cs`, covering
`AgentControl-RepoSync-ContentIdentity` (children: `AgentControl-PackageContentExtractor-FolderReaderIdentity` and
`AgentControl-PackageContentExtractor-FolderReaderNoIdentity`).

**RepoSync_ContentIdentityOfAppliedBytes_ZipIdentityDescribesTheBytesThatAreApplied**: A zip is read by the zip reader
and applied to a repo by the extractor: the identity the reader reports equals the SHA-256 of the zip file computed
independently by the test; while the reader holds the zip, replacing the file fails (so the identity cannot describe
other bytes than the ones that are read); the file applied to the repo is the one that was in the zip; and the same
zip republished with other content reports another identity and applies the other content to another repo - what is
recorded is always what was applied. This scenario is tested by
`RepoSync_ContentIdentity_ZipIdentityDescribesTheBytesThatAreApplied`, covering
`AgentControl-RepoSync-ContentIdentityOfAppliedBytes` (children:
`AgentControl-PackageContentExtractor-ReaderContentIdentity` and
`AgentControl-PackageContentExtractor-ZipReaderReleasesHandleOnFailure`, whose unit tests are
`ZipPackageContentReader_Open_ValidZip_ReportsSha256OfFileAndReadsEntries` and
`ZipPackageContentReader_Open_NotAZip_ReleasesFileHandle`).

**RepoSync_LayoutRoot_OnlyTheLayoutIsSeenReadAndApplied**: A folder shaped like a repository - the layout plus `.git`,
`.github/workflows`, a README and source files - is opened with `OpenLayoutRoot` with a commit identity: only the four
managed folders and the two root files are listed, a path outside the layout (the README, `.git/config`, a file of
`.github/workflows`) is refused, the identity supplied by the opener is reported, release notes and the `AGENTS.md`
template are read, and applying the folder with the extractor writes only the four managed folders into the repo - no
workflows folder, no README, no `.git`. This scenario is tested by `RepoSync_LayoutRoot_OnlyTheLayoutIsSeenReadAndApplied`,
covering `AgentControl-RepoSync-LayoutRoot` (children: `ReadLayoutRoot`, `LayoutRootIdentity` and
`RefuseMissingOrLinkedLayoutRoot`) and `AgentControl-RepoSync-LayoutRootOnlyLayout` (children: `RefuseOutsideLayoutPaths`
and `ExcludeGitFolder`; each child prefixed `AgentControl-PackageContentExtractor-`). The unit tests of the missing or
linked root are `DirectoryPackageContentReader_OpenLayoutRoot_MissingFolderOrLink_ThrowsInvalidOperationException` and
the others in `PackageContentExtractorTests.cs`.

**RepoSync_LayoutRoot_LinkInTheLayoutIsRefusedWhileLinkOutsideItIsIgnored**: A directory link (a junction on Windows,
which needs no privilege, a symbolic link elsewhere; skipped where the operating system does not let the test create
one) in `docs`, outside the layout, is no obstacle: the folder is applied to a repo that held an old file, which is
replaced. When a managed folder of the package is then replaced by a link to a folder outside, applying it again is
refused with an `InvalidOperationException` before the repo is touched (the repo's files are as they were) and the link
target is untouched. The link in place of `.github` itself, in place of a root file and the symbolic-link cases are
verified by the unit tests (`DirectoryPackageContentReader_OpenLayoutRoot_JunctionOutsideLayoutIgnoredInsideLayoutRefused`,
`..._SymlinkOutsideLayoutIgnoredInsideLayoutRefused`, `..._JunctionReplacingGitHubFolder_IsRefused`,
`..._SymlinkReplacingGitHubFolder_IsRefused`, `..._SymlinkReplacingManagedFolder_IsRefused` and the root-file cases).
This scenario is tested by `RepoSync_LayoutRoot_LinkInTheLayoutIsRefusedWhileLinkOutsideItIsIgnored`, covering
`AgentControl-RepoSync-LayoutRootRefusesLinks` (children: `RefuseLinkInManagedFolder`, `RefuseLinkInPlaceOfGitHub`,
`RefuseLinkInPlaceOfManagedFolder` and `RefuseLinkAsRootFile`) and `AgentControl-RepoSync-LayoutRootIgnoresOtherLinks`
(child: `IgnoreLinksOutsideLayout`; each child prefixed `AgentControl-PackageContentExtractor-`).
