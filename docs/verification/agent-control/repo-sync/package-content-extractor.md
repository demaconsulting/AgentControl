### PackageContentExtractor

#### Verification Approach

`PackageContentExtractor` and the package content readers behind it are verified with unit tests
defined in `PackageContentExtractorTests.cs`. Tests build real temporary repo directories and real
unpacked package folders (a `content` folder holding managed folders and root-level files, plus a
nuspec beside it as in a real unpacked NuGet package) and a real zip for the zip/folder equivalence
check, so extraction and reading run against genuine filesystem state. A small private stub reader
lists caller-supplied paths so hostile paths (traversal out of the repo) that a real reader would
never produce can be presented to the extractor. Link rejection is verified with real symbolic
links where the operating system and its privileges allow creating one; where they do not, those
tests skip themselves with the reason, and the requirement cites them with an `ubuntu@` source
filter so its evidence comes from the Linux leg of the build matrix. Directory junctions need no
privilege on Windows, so equivalent tests create junctions with the Windows command shell and the requirement
cites them with a `windows@` filter, giving the directory-level rejection passing evidence there.

#### Test Environment

Symbolic-link tests need an environment that permits creating symbolic links (always true on Linux
and macOS; on Windows only with the symbolic-link privilege or developer mode, otherwise they are
skipped). Junction tests run on Windows only (they skip elsewhere) and need no privilege.

#### Acceptance Criteria

- All unit tests pass with zero failures (skipped symbolic-link tests excepted where links cannot
  be created).
- Extracting an unpacked package folder creates only the managed folders in the repo, replaces a
  managed folder's old contents, and never copies root-level files.
- The package folder is byte-for-byte unchanged by extraction and reads.
- The same content as a zip and as a folder produces an identical repo tree.
- A package folder without a content folder is rejected when opened.
- A path that escapes the repo, a path outside the content folder, and a symbolic link or junction
  at the package folder, the content folder, any directory leading to a file, or the file itself
  are refused, with the repo's managed folders untouched; a broken link at a requested root-level
  file is refused like any other link instead of being treated as a missing file.
- A zip package given an invalid or unsupported path is reported as an `InvalidOperationException`
  rather than a path exception.
- Release notes and the AGENTS.md template are read from a folder package, or reported absent.
- A zip reader reports the SHA-256 identity of the zip it reads from the same handle its entries are read from and
  releases the handle when opening fails; a folder reader reports the identity its opener supplied, or none.
- A repo whose `.github` folder, a managed folder, or a folder on a destination path is a symbolic
  link or junction is refused with nothing deleted or written (the link target is unchanged and
  other managed folders survive), a linked managed folder is not reported as existing, and a repo
  root reached through a link still extracts.

#### Test Scenarios

**PackageContentExtractor_ExtractFromFolder_CreatesReplacesAndNeverModifiesPackage**: Extracting a
package folder into a fresh repo creates only `.github` with the managed content; extracting over a
repo with a stale file in a managed folder removes it; a snapshot of the package folder is
identical before and after extraction and reads; the same files supplied as zip and folder give
identical repo snapshots; a package folder with no `content` folder is rejected with a message
naming it. This scenario is tested by
`PackageContentExtractor_Extract_DirectoryPackageIntoFreshRepo_CreatesManagedFoldersOnly`,
`PackageContentExtractor_Extract_DirectoryPackageOverExistingFolder_ReplacesOldContents`,
`PackageContentExtractor_Extract_DirectoryPackage_NeverModifiesPackageFolder`,
`PackageContentExtractor_Extract_ZipAndDirectoryWithSameContent_ProduceIdenticalRepoTrees`, and
`DirectoryPackageContentReader_Open_NoContentFolder_ThrowsInvalidOperationException`, covering
`AgentControl-PackageContentExtractor-ExtractFromFolder` (a repo that has the same managed folders as the same
content supplied as a zip), `-ReplaceManagedFolders` (the stale-file test), `-NoRootFilesCopied` (the fresh-repo test),
`-PackageNeverModified` and `-RejectMissingContentFolder` (each prefixed `AgentControl-PackageContentExtractor`).

**PackageContentExtractor_RejectLinksAndTraversal_BeforeAnyDeletion**: A reader listing a path that
escapes the repo (two spellings) makes extraction fail while a pre-existing managed file survives;
asking the folder reader for a path that leaves the content folder, or for a missing file, fails (or
returns none for reading text); a symbolic link to a file in a managed folder, a symbolic link to a
directory, a symbolic content folder, a symbolic package folder, a symbolic parent directory holding an
ordinary file, and a symbolic release-notes file are each refused, the first with the repo's
existing managed content surviving; the same refusals are shown on Windows with directory junctions
as the package folder, the content folder, a parent directory of an ordinary file, and a directory
inside the package (the last also leaving the repo's existing managed content intact). A broken symbolic link
at a requested root-level file (a link to a target that does not exist, such as `AGENTS.md`) makes
`ReadText` throw `InvalidOperationException` instead of returning none; the test skips where symbolic links cannot
be created. This
scenario is tested by
`PackageContentExtractor_Extract_TraversalPathFromReader_ThrowsBeforeDeletingManagedFolders`,
`DirectoryPackageContentReader_OpenReadAndReadText_TraversalOrMissingPath_Rejected`,
`PackageContentExtractor_Extract_SymlinkedFileInPackage_ThrowsBeforeDeletingManagedFolders`,
`DirectoryPackageContentReader_ListFiles_SymlinkedDirectoryInPackage_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_Open_ContentFolderIsSymlink_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_ReadText_SymlinkedReleaseNotes_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_ReadText_BrokenFileSymlinkAtRoot_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_Open_PackageFolderIsSymlink_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_OpenRead_LinkedParentDirectoryWithOrdinaryFile_ThrowsInvalidOperationException`,
`PackageContentExtractor_Extract_JunctionInPackage_ThrowsBeforeDeletingManagedFolders`,
`DirectoryPackageContentReader_ListFiles_JunctionInPackage_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_Open_PackageFolderIsJunction_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_Open_ContentFolderIsJunction_ThrowsInvalidOperationException`, and
`DirectoryPackageContentReader_OpenRead_JunctionParentDirectoryWithOrdinaryFile_ThrowsInvalidOperationException`,
covering `AgentControl-PackageContentExtractor-RejectLinksAndTraversal` (the extraction-level tests),
`-RejectPathOutsideContent`, `-RejectLinkedPackageFolder`, `-RejectLinkedPackageDirectory`,
`-RejectLinkedPackageFile` and `-RejectBrokenLink` (each prefixed `AgentControl-PackageContentExtractor`; the
mapping of test to requirement is in the requirements file).

**PackageContentExtractor_RefuseLinkedRepoFolders_BeforeAnyChange**: With the repo's `.github`
folder linked to an outside folder holding content, with one managed folder linked (while another,
ordinary, managed folder holds a file), and with a destination subfolder inside a managed folder
linked, extraction fails with a message naming a link, the outside folder's snapshot is identical
afterwards, and the ordinary folder's file survives. `AllManagedFoldersExist` returns false when
one managed folder is a link and true when all four are ordinary. A repo root reached through a
link extracts normally into the real repo. Each is shown with junctions on Windows (`windows@`)
and symbolic links on Linux (`ubuntu@`). This scenario is tested by
`PackageContentExtractor_Extract_GitHubFolderIsJunction_RefusesAndLeavesTargetUntouched`,
`PackageContentExtractor_Extract_ManagedFolderIsJunction_RefusesAndLeavesTargetUntouched`,
`PackageContentExtractor_Extract_DestinationSubfolderIsJunction_RefusesAndLeavesTargetUntouched`,
`PackageContentExtractor_Extract_RepoRootUnderJunction_Succeeds`,
`PackageContentExtractor_AllManagedFoldersExist_ManagedFolderIsJunction_ReturnsFalse`,
`PackageContentExtractor_Extract_GitHubFolderIsSymlink_RefusesAndLeavesTargetUntouched`,
`PackageContentExtractor_Extract_ManagedFolderIsSymlink_RefusesAndLeavesTargetUntouched`,
`PackageContentExtractor_Extract_DestinationSubfolderIsSymlink_RefusesAndLeavesTargetUntouched`,
`PackageContentExtractor_Extract_RepoRootUnderSymlink_Succeeds`,
`PackageContentExtractor_AllManagedFoldersExist_ManagedFolderIsSymlink_ReturnsFalse`, and
`PackageContentExtractor_AllManagedFoldersExist_AllFoldersOrdinary_ReturnsTrue`, covering
`AgentControl-PackageContentExtractor-RefuseLinkedRepoFolders` (the three extraction tests per operating system),
`-ManagedFoldersExist` (the `AllManagedFoldersExist` tests) and `-AcceptRepoRootThroughLink` (the repo-root tests;
each prefixed `AgentControl-PackageContentExtractor`).

**PackageContentExtractor_RefuseUnsafeDestinations_BeforeAnyChange**: With `.github/agents` present as
a regular file (and another managed folder holding a file), extraction fails naming that component and the
other folder's file survives, with nothing written. With a reader that plants a symbolic link at the
destination of a later file while an earlier one is being copied, extraction fails naming a link and the
link's target outside the repo is unchanged (the planting reader reproduces, deterministically, the gap
between the folder checks and the write; the test skips where links cannot be created). This scenario is
tested by `PackageContentExtractor_Extract_ManagedFolderIsAFile_RefusesBeforeDeletingAnything` and
`PackageContentExtractor_Extract_DestinationFileBecomesSymlink_RefusesAndLeavesTargetUntouched`, covering
`AgentControl-PackageContentExtractor-RefuseNonFolderRepoComponent` and `-RefuseLinkedDestinationFile` (each
prefixed `AgentControl-PackageContentExtractor`).

**PackageContentExtractor_ReadRootFiles_ReturnsTextOrNull**: Release notes and the AGENTS.md
template are returned from a folder package's content root, and `null` when absent. This scenario
is tested by
`PackageContentExtractor_ReadReleaseNotes_DirectoryPackage_ReturnsContentOrNull` and
`PackageContentExtractor_ReadAgentsMdTemplate_DirectoryPackage_ReturnsContentOrNull`, covering
`AgentControl-PackageContentExtractor-ReadReleaseNotes` and
`AgentControl-PackageContentExtractor-ReadAgentsMdTemplate`. The null-argument test
`PackageContentExtractor_Extract_NullArguments_ThrowsArgumentNullException` is a defensive test that no
requirement cites.

**PackageContentExtractor_ReaderContentIdentity_ZipHashedFromTheReadHandle**: A zip reader reports the SHA-256
identity of the zip file (compared with a hash computed independently by the test) and still reads its entries
afterwards, because the hash and the archive share one handle. This scenario is tested by
`ZipPackageContentReader_Open_ValidZip_ReportsSha256OfFileAndReadsEntries`, covering
`AgentControl-PackageContentExtractor-ReaderContentIdentity`.

**PackageContentExtractor_ReaderContentIdentity_ZipHandleReleasedOnFailure**: A file that is not a zip releases the
handle when opening fails (the file can be deleted straight away). This scenario is tested by
`ZipPackageContentReader_Open_NotAZip_ReleasesFileHandle`, covering
`AgentControl-PackageContentExtractor-ZipReaderReleasesHandleOnFailure`.

**PackageContentExtractor_ReaderContentIdentity_FolderCarriesSuppliedIdentityOrNone**: A folder reader reports the
identity its opener supplied, and none when none was supplied. This scenario is tested by
`DirectoryPackageContentReader_Open_WithAndWithoutIdentity_ReportsSuppliedIdentityOrNull`, covering
`AgentControl-PackageContentExtractor-FolderReaderIdentity`, and by
`DirectoryPackageContentReader_Open_WithoutIdentity_ReportsNone`, covering
`AgentControl-PackageContentExtractor-FolderReaderNoIdentity`.

**PackageContentExtractor_ZipOpenFailure_InvalidPathIsReportedAsInvalidOperation**: Opening a zip
package with an invalid path (an argument the operating system rejects as a path) throws
`InvalidOperationException` rather than the underlying path exception. This scenario is tested by
`ZipPackageContentReader_Open_InvalidPath_ThrowsInvalidOperationException`, covering
`AgentControl-PackageContentExtractor-ZipOpenFailure`. Opening a missing or corrupt zip is verified through
`PackageZipExtractor` in the `RepoSync` subsystem tests.

#### Known Limitations

- **Check-then-use link validation cannot be tested deterministically.** Link checks on package and repo paths
  are followed by the file operation they guard, so a concurrent process that swaps a checked component for a
  link in between could redirect the operation (a time-of-check-to-time-of-use race). The risk is accepted
  and documented as a residual risk in the `PackageContentExtractor` and `RepoSync` designs (exploitation needs a
  concurrent process of the same user, who could write there anyway; the destination chain is re-checked
  immediately before each creation; the supported model is that the repo and package folders are not modified
  during extraction; a no-follow open is not portable across Windows, Linux and macOS in .NET). Winning
  such a race depends on operating-system scheduling, so no unit test can reproduce it reliably; the tests above
  verify that a link present before the operation starts is always refused.

**PackageContentExtractor_LayoutRoot_ReadsOnlyLayout**: A folder shaped like a checked-out repository is read through
`OpenLayoutRoot`: only the layout is listed (no `.git`, workflows, README or sources), the supplied identity is carried,
the package extracts and its notes and template are read, and paths outside the layout (including
`.github/agents/../../.git/config`) are refused. This scenario is tested by
`DirectoryPackageContentReader_OpenLayoutRoot_ListsOnlyLayoutAndCarriesIdentity`, covering
`AgentControl-PackageContentExtractor-ReadLayoutRoot` and `AgentControl-PackageContentExtractor-ExcludeGitFolder`;
by `DirectoryPackageContentReader_OpenLayoutRoot_PathOutsideLayout_IsRefused`, covering
`AgentControl-PackageContentExtractor-RefuseOutsideLayoutPaths`
and `AgentControl-PackageContentExtractor-ExcludeGitFolder`;
by `DirectoryPackageContentReader_OpenLayoutRoot_WithAndWithoutIdentity_ReportsSuppliedIdentityOrNull`, covering
`AgentControl-PackageContentExtractor-LayoutRootIdentity`; and by
`DirectoryPackageContentReader_OpenLayoutRoot_MissingFolderOrLink_ThrowsInvalidOperationException`, covering
`AgentControl-PackageContentExtractor-RefuseMissingOrLinkedLayoutRoot`.

**PackageContentExtractor_LayoutRoot_UnrelatedLinksIgnoredLayoutLinksRefused**: A junction or symbolic link that is not
part of the layout is neither inspected nor refused, while a link in the layout is refused in each position the
layout has: a file link inside a managed folder, a link standing in place of `.github` itself, a link standing in
place of a managed folder, and a link standing in place of a root file. The tests create links where the operating
system lets them and skip otherwise: the junction cases run on Windows, the symbolic-link cases on Linux and macOS.
This scenario is tested by
`DirectoryPackageContentReader_OpenLayoutRoot_JunctionOutsideLayoutIgnoredInsideLayoutRefused` and
`DirectoryPackageContentReader_OpenLayoutRoot_SymlinkOutsideLayoutIgnoredInsideLayoutRefused`, covering
`AgentControl-PackageContentExtractor-IgnoreLinksOutsideLayout`;
`DirectoryPackageContentReader_OpenLayoutRoot_SymlinkFileInManagedFolder_IsRefused`, covering
`AgentControl-PackageContentExtractor-RefuseLinkInManagedFolder`;
`DirectoryPackageContentReader_OpenLayoutRoot_JunctionReplacingGitHubFolder_IsRefused` and
`DirectoryPackageContentReader_OpenLayoutRoot_SymlinkReplacingGitHubFolder_IsRefused`, covering
`AgentControl-PackageContentExtractor-RefuseLinkInPlaceOfGitHub`;
`DirectoryPackageContentReader_OpenLayoutRoot_JunctionReplacingManagedFolder_IsRefused` and
`DirectoryPackageContentReader_OpenLayoutRoot_SymlinkReplacingManagedFolder_IsRefused`, covering
`AgentControl-PackageContentExtractor-RefuseLinkInPlaceOfManagedFolder`; and
`DirectoryPackageContentReader_OpenLayoutRoot_SymlinkAsRootFile_IsRefused`, covering
`AgentControl-PackageContentExtractor-RefuseLinkAsRootFile`.
