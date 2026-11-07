### NuGetSourceProvider

#### Verification Approach

`NuGetSourceProvider` is verified with unit tests defined in `NuGetSourceProviderTests.cs`. The
tests run the real NuGet caching library against a real NuGet folder feed built on the fly in a
temporary folder (`NuGetFeedBuilder` writes minimal unsigned `.nupkg` files: a nuspec plus content
files), so listing, unpacking, the private cache and the generated configuration are all exercised
for real without any network access. Each test supplies its own per-source cache base folder and
package IDs unique to the test, so tests are parallel-safe and never touch the developer's NuGet
cache. Two properties are verified by inspection of real outcomes rather than mocks: a hostile
repo-level `nuget.config` is left byte-for-byte unchanged and ignored, and the product assembly's
type references are read with `System.Reflection.Metadata` to prove it never references NuGet's
credential-service types.

#### Test Environment

Requires the `NUGET_PACKAGES` environment variable to be unset (acquisition deliberately fails
closed when it redirects the package cache outside the per-source folder); symbolic links are not
used by these tests.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Versions of a package (including prereleases and four-part versions) are listed with the source
  alias; an absent package yields an empty list; a missing feed folder is reported as such;
  cancellation is honored.
- Acquisition yields a reader over the package's `content` folder (managed files, release notes,
  AGENTS.md template), a wrong-layout package is rejected, and the content can be applied to a repo
  through the shared extractor.
- The root folder passed to the library is AgentControl-owned, absolute and distinct per source (a
  relative cache base is refused); two sources with the same package ID and version each yield
  their own content.
- Listing and acquiring leave the process-wide NuGet credential service unchanged.
- Nothing is read from or written to a repo, including a repo `nuget.config`.
- Server and relative locations and non-NuGet definitions are rejected; a package from another
  source is refused.

#### Test Scenarios

**NuGetSourceProvider_ListVersions_ReportsVersionsEmptyMissingAndCancellation**: A feed with
several versions (a release, a prerelease, a four-part version) of one package and another
package lists exactly the requested package's versions stamped with the alias; an absent package
returns an empty list; a missing feed folder throws `DirectoryNotFoundException`; an already
canceled token throws `OperationCanceledException`; the configured package IDs are returned
trimmed, de-duplicated and sorted. This scenario is tested by
`NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias`,
`NuGetSourceProvider_ListVersions_PackageNotInFeed_ReturnsEmptyList`,
`NuGetSourceProvider_ListVersions_FolderDoesNotExist_ThrowsDirectoryNotFoundException`,
`NuGetSourceProvider_ListVersions_AlreadyCanceled_ThrowsOperationCanceledException`, and
`NuGetSourceProvider_ListPackageIds_ConfiguredIds_ReturnsDistinctSortedTrimmedIds`, covering
`AgentControl-NuGetSourceProvider-ListVersions`.

**NuGetSourceProvider_Acquire_ReaderOverContentFolderInPrivateCache**: Acquiring a package
returns a reader exposing the managed files, release notes and AGENTS.md template; the same
reader applied through the shared extractor writes only the managed folders to a repo; two feeds
holding the same package ID and version with different content each yield their own content; a
package without a `content` folder is rejected. This scenario is tested by
`NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder`,
`NuGetSourceProvider_Acquire_ThenExtract_WritesManagedFoldersToRepoOnly`,
`NuGetSourceProvider_Acquire_SamePackageInTwoSources_EachSourceGetsItsOwnContent`, and
`NuGetSourceProvider_Acquire_PackageWithoutContentFolder_ThrowsInvalidOperationException`,
covering `AgentControl-NuGetSourceProvider-AcquireToPrivateCache`.

**NuGetSourceProvider_ExplicitRoot_AgentControlOwnedAndGeneratedConfigOnly**: The root folder is
directly under the supplied base, its generated `nuget.config` contains a global-packages-folder
setting and no sources or credentials, and the unpacked package is inside it; the per-source key is
stable for the same alias and location (alias case and trailing separators ignored), differs when
the location or alias differs, and is safe as a folder name; a relative or drive-relative cache base
is refused with `ArgumentException`, and the default base is an absolute path under the per-user
local application-data folder. This scenario is tested by
`NuGetSourceProvider_Acquire_UsesExplicitAgentControlOwnedRootWithGeneratedConfig`,
`NuGetSourceProvider_BuildSourceKey_AliasAndLocation_StableDistinctAndFolderSafe`,
`NuGetSourceProvider_Constructor_RelativeRootBase_ThrowsArgumentException`, and
`NuGetSourceProvider_Constructor_NoRootBase_RootIsAbsoluteUnderLocalApplicationData`, covering
`AgentControl-NuGetSourceProvider-ExplicitCacheRoot`.

**NuGetSourceProvider_NoRepoNuGetConfig_RepoIsNeverReadOrWritten**: With a repo folder holding a
`nuget.config` that tries to redirect the package cache, listing and acquiring leave the repo
holding only that original file, unchanged, create no cache there, and use a root outside the repo.
This scenario is tested by `NuGetSourceProvider_ListAndAcquire_NeverTouchesRepoNuGetConfig`,
covering `AgentControl-NuGetSourceProvider-NoRepoNuGetConfig`.

**NuGetSourceProvider_NoCredentialService_ProcessWideServiceUnchanged**: After listing and
acquiring, the process-wide NuGet credential service is the same instance as before, and the
product assembly's type references contain no credential-service or HTTP-handler-resource type.
This scenario is tested by
`NuGetSourceProvider_ListAndAcquire_LeavesProcessWideCredentialServiceUnchanged` and
`NuGetSourceProvider_ProductAssembly_DoesNotReferenceNuGetCredentialService`, covering `AgentControl-NuGetSourceProvider-NoCredentialServiceRegistration`.

**NuGetSourceProvider_RejectServerAndInvalidLocations_WithClearErrors**: An `http` or `https`
server location is rejected with a message naming the source and saying servers are not supported;
a relative folder is rejected; a zip-type or null definition is rejected. This scenario is tested
by
`NuGetSourceProvider_Constructor_ServerLocation_ThrowsInvalidOperationExceptionWithClearMessage`,
`NuGetSourceProvider_Constructor_RelativeLocation_ThrowsInvalidOperationException`, and
`NuGetSourceProvider_Constructor_ZipTypeOrNullDefinition_Throws`, covering
`AgentControl-NuGetSourceProvider-RejectServerLocation`.

**NuGetSourceProvider_AcquireOnlyFromOwnSource_RefusesForeignPackages**: A package that names a
different source alias is refused with `ArgumentException`. This scenario is tested by
`NuGetSourceProvider_Acquire_PackageFromOtherSource_ThrowsArgumentException`, covering
`AgentControl-NuGetSourceProvider-AcquireOnlyFromOwnSource`.
