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
filter so its evidence comes from the Linux leg of the build matrix.

#### Test Environment

Symbolic-link tests need an environment that permits creating symbolic links (always true on Linux
and macOS; on Windows only with the symbolic-link privilege or developer mode, otherwise they are
skipped).

#### Acceptance Criteria

- All unit tests pass with zero failures (skipped symbolic-link tests excepted where links cannot
  be created).
- Extracting an unpacked package folder creates only the managed folders in the repo, replaces a
  managed folder's old contents, and never copies root-level files.
- The package folder is byte-for-byte unchanged by extraction and reads.
- The same content as a zip and as a folder produces an identical repo tree.
- A package folder without a content folder is rejected when opened.
- A path that escapes the repo, a path outside the content folder, and a symbolic link in the
  package are refused, with the repo's managed folders untouched.
- Release notes and the AGENTS.md template are read from a folder package, or reported absent.

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
`AgentControl-PackageContentExtractor-ExtractFromFolder`.

**PackageContentExtractor_RejectLinksAndTraversal_BeforeAnyDeletion**: A reader listing a path that
escapes the repo (two spellings) makes extraction fail while a pre-existing managed file survives;
asking the folder reader for a path that leaves the content folder, or for a missing file, fails (or
returns none for reading text); a symbolic link to a file in a managed folder, a symbolic link to a
directory, a symbolic content folder, and a symbolic release-notes file are each refused, the first
with the repo's existing managed content surviving. This scenario is tested by
`PackageContentExtractor_Extract_TraversalPathFromReader_ThrowsBeforeDeletingManagedFolders`,
`DirectoryPackageContentReader_OpenReadAndReadText_TraversalOrMissingPath_Rejected`,
`PackageContentExtractor_Extract_SymlinkedFileInPackage_ThrowsBeforeDeletingManagedFolders`,
`DirectoryPackageContentReader_ListFiles_SymlinkedDirectoryInPackage_ThrowsInvalidOperationException`,
`DirectoryPackageContentReader_Open_ContentFolderIsSymlink_ThrowsInvalidOperationException`, and
`DirectoryPackageContentReader_ReadText_SymlinkedReleaseNotes_ThrowsInvalidOperationException`,
covering `AgentControl-PackageContentExtractor-RejectLinksAndTraversal`.

**PackageContentExtractor_ReadRootFiles_ReturnsTextOrNull**: Release notes and the AGENTS.md
template are returned from a folder package's content root, and `null` when absent; null
arguments to `Extract` are rejected. This scenario is tested by
`PackageContentExtractor_ReadReleaseNotes_DirectoryPackage_ReturnsContentOrNull`,
`PackageContentExtractor_ReadAgentsMdTemplate_DirectoryPackage_ReturnsContentOrNull`, and
`PackageContentExtractor_Extract_NullArguments_ThrowsArgumentNullException`, covering
`AgentControl-PackageContentExtractor-ReadRootFiles`.
