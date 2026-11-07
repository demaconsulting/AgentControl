### ZipFolderSourceProvider

#### Verification Approach

`ZipFolderSourceProvider` is verified with unit tests defined in `ZipFolderSourceProviderTests.cs`.
Every test uses a real unique temporary directory populated with real zip files (some with real
entries), so listing and acquisition run against genuine filesystem state. The scanning rules
themselves are verified by the `PackageSource` tests; these tests verify what the provider adds:
the source alias, the provider contract, the error contract, cancellation, and the checks on what
may be acquired.

Cancellation is verified in two ways: a token that is already canceled is refused before any work
starts, and the private helper that runs the file-system work is exercised with a work item that is
held open by the test, so the test can prove that the caller is released with
`OperationCanceledException` while the work is still running and that a result that completes late
is disposed. The tests never depend on a genuinely slow share.

The link checks create real symbolic links in the temporary directory. Creating a symbolic link
needs a privilege on some systems (for example, an unprivileged Windows account without developer
mode), so those two tests are skipped where a link cannot be created, and only then; they run on the
Linux and macOS builds of the project.

#### Test Environment

N/A - standard test environment. Symbolic-link tests need the ability to create symbolic links.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Listing returns only the named package's zips, labeled with the source alias, and the distinct
  sorted package names; zip names with a fourth version segment or metadata are not packages.
- Acquisition returns a reader over the zip's root.
- A missing source folder, a source location that is a file, and a folder that disappeared after
  listing are reported as `DirectoryNotFoundException`.
- An already canceled token throws `OperationCanceledException` before any work, and a cancellation
  during in-flight work returns promptly and disposes a late-completed result.
- Acquisition refuses a package from another source, a zip outside the source folder, and a zip that
  is a symbolic link to a file outside the source folder, and allows a link to a file inside it.
- The constructor rejects a definition that is null, not a zip source, or has a blank location.

#### Test Scenarios

**ZipFolderSourceProvider_ListVersions_StampsAliasAndKeepsFileNameRules**: Listing versions in a mixed
folder returns only the matching package's zips with the alias and zip path; zip names with a fourth
segment or build metadata are skipped. This scenario is tested by
`ZipFolderSourceProvider_ListVersions_MixedFolder_ReturnsMatchingPackagesStampedWithAlias` and
`ZipFolderSourceProvider_ListVersions_ExtendedVersionFileNames_AreSkipped`, covering
`AgentControl-ZipFolderSourceProvider-ListAndAcquire`.

**ZipFolderSourceProvider_ListPackageNames_ReturnsDistinctSortedNames**: Listing names returns the
distinct names in sorted order. This scenario is tested by
`ZipFolderSourceProvider_ListPackageIds_MixedFolder_ReturnsDistinctSortedNames`, covering
`AgentControl-ZipFolderSourceProvider-ListPackageNames`.

**ZipFolderSourceProvider_AcquireContent_ReturnsReaderOverZipRoot**: Acquiring a discovered package
returns a reader exposing its managed files and root-level release notes. This scenario is tested by
`ZipFolderSourceProvider_Acquire_DiscoveredPackage_ReturnsReaderOverZipRoot`, covering
`AgentControl-ZipFolderSourceProvider-AcquireContent`.

**ZipFolderSourceProvider_FolderErrors_AreNormalized**: A missing folder throws
`DirectoryNotFoundException` from both listings; a package acquired after its folder was removed
throws `DirectoryNotFoundException` rather than the zip reader's `InvalidOperationException`; a
source location that is a file is reported as a folder problem. This scenario is tested by
`ZipFolderSourceProvider_List_FolderDoesNotExist_ThrowsDirectoryNotFoundException`,
`ZipFolderSourceProvider_Acquire_FolderRemovedAfterListing_ThrowsDirectoryNotFoundException` and
`ZipFolderSourceProvider_ListPackageIds_FolderPathIsAFile_IsReportedAsFolderProblem`, covering
`AgentControl-ZipFolderSourceProvider-FolderErrorsNormalized`. The wrapping of other I/O failures
(access denied, an invalid zip) as `InvalidOperationException` with a safe message cannot be provoked
deterministically on every platform and is not separately asserted; this is a known limitation of the
evidence.

**ZipFolderSourceProvider_Cancellation_IsObserved**: A listing started with an already canceled token
throws `OperationCanceledException`; work held open by the test while the token is canceled releases
the caller promptly with `OperationCanceledException`, and the result of the work, completed afterwards,
is disposed. This scenario is tested by
`ZipFolderSourceProvider_ListPackageIds_PreCanceledToken_ThrowsOperationCanceled` and
`ZipFolderSourceProvider_RunCancellable_CanceledWhileWorkRuns_ReturnsPromptlyAndDisposesLateResult`,
covering `AgentControl-ZipFolderSourceProvider-CancellationObserved`.

**ZipFolderSourceProvider_AcquireOnlyFromOwnSource_RefusesForeignPackages**: A package that names a
different source alias is refused with `ArgumentException`. This scenario is tested by
`ZipFolderSourceProvider_Acquire_PackageFromOtherSource_ThrowsArgumentException`, covering
`AgentControl-ZipFolderSourceProvider-AcquireOnlyFromOwnSource`.

**ZipFolderSourceProvider_ZipMustBeInOwnFolder_RefusesZipInAnotherFolder**: A package whose zip lies in a
different folder is refused with `ArgumentException`, even when the alias matches. This scenario is
tested by `ZipFolderSourceProvider_Acquire_ZipOutsideSourceFolder_ThrowsArgumentException`, covering
`AgentControl-ZipFolderSourceProvider-ZipMustBeInOwnFolder`.

**ZipFolderSourceProvider_LinkedZip_OnlyAllowedInsideTheFolder**: A zip in the folder that is a symbolic
link to a zip outside the folder is refused with `ArgumentException`, and one that is a link to a zip
inside the folder is allowed. The replacement of a file by a link between the check and the open is not
tested; it is a documented residual risk (see the design). Both tests skip where symbolic links cannot
be created (for example Windows without the privilege), so they are cited with the `ubuntu@` source
filter, as the other symbolic-link tests of the repository are. This scenario is tested by
`ZipFolderSourceProvider_Acquire_ZipIsSymlinkToFileOutsideFolder_ThrowsArgumentException`, covering
`AgentControl-ZipFolderSourceProvider-RefusesLinkedZipOutsideFolder`, and
`ZipFolderSourceProvider_Acquire_ZipIsSymlinkToFileInsideFolder_IsAllowed`, covering
`AgentControl-ZipFolderSourceProvider-AllowsLinkedZipInsideFolder`.

**ZipFolderSourceProvider_ValidateDefinition_RejectsInvalidDefinitions**: A NuGet-type, blank-location,
or null definition is rejected by the constructor. This scenario is tested by
`ZipFolderSourceProvider_Constructor_InvalidDefinition_Throws`, covering
`AgentControl-ZipFolderSourceProvider-ValidateDefinition`.
