### GitSourceProvider

#### Verification Approach

`GitSourceProvider` is verified two ways. `GitSourceProviderIntegrationTests.cs` uses the **real git** with local bare
repositories built by `GitRepositoryFixture` (a working repository that is committed to and tagged, pushed to a bare
"remote"); the user's global and system git configuration is replaced by an empty file, so a developer's credential
helper, `insteadOf` rewrite or `safe.directory` can never change a result. Symbolic-link and submodule entries, and
entries whose names a platform would refuse to create, are staged with `hash-object` and `update-index --cacheinfo`
(with the platform's name protections switched off for that call), which needs no operating-system privilege, so the
tree-level refusals are exercised on Windows, Linux and macOS alike. `GitSourceProviderTests.cs` uses the recording stub
of `GitRemoteClientTests` (`GitRemoteStub`) where no repository is needed: definition validation, the arguments and
environment the provider causes, the interactive flag, the re-listing that binds an acquisition to the listing, the
cache folders of different repositories, the path-component rules, failure translation and cancellation.

#### Test Environment

The integration tests need a git executable on `PATH` and are skipped only when there is none. They belong to the
`RealProcess` collection (no parallelization) and run on the Windows, Ubuntu and macOS legs; no network or credential is
ever used. The hook test builds a hostile environment (a template directory and a global `core.hooksPath` that install a
`post-checkout` hook) and the filter test a hostile global configuration (a filter driver whose smudge command writes a
marker file, with `autocrlf` on); each first proves, with plain git, that the hook or filter would run, and where it
does not run at all the test is skipped rather than reported as proof.

#### Acceptance Criteria

- Tags are listed with their commits (annotated tags peeled), filtered by the pattern as strict Semantic Versioning 2.0
  versions (`v01.2.3` is not one), with duplicates resolved and unknown package IDs empty; the preview separates
  versions
  from other tags.
- An acquired package holds exactly the layout, no `.git`, the files identity of its agent files and the source revision
  `git-commit:{sha}`, and is cached by commit so a
  second acquisition needs no remote; a moved tag shows as another revision and a stale listing is refused.
- A package that the repository would not list - a listed tag at another commit, a version that is not listed, a tag
  that
  lost to a smaller name for its version - is refused before anything is fetched.
- The delivered files have exactly the bytes of the commit: under the user's `autocrlf`, and with attributes that select
  a filter driver, line-ending conversion and identifier expansion, the bytes are unchanged and the filter's program
  never runs.
- A link, a submodule or a `.git` entry in the layout, a managed folder that is a file, a root file that is a folder, a
  name that is not safe on every file system, a bad sub-folder and a tag without layout are refused with nothing cached
  or left behind; links elsewhere are ignored.
- Two layout entries that differ only in letter case or Unicode form (two files, a file and a folder, two folders, the
  composed and decomposed spelling of one name) are refused on every operating system before anything is written, with
  nothing cached or left behind.
- When the `.git` folder cannot be removed (the removal fails or silently leaves it) the acquisition fails with an
  actionable message, no cache folder of the commit exists or is marked complete, no temporary folder is left, a
  leftover temporary folder is never served, and a later acquisition succeeds.
- A reader that has not been disposed keeps its commit's folder complete and readable while other acquisitions take the
  cache beyond its limit; the folder is prunable again once every reader of it is disposed (however often each is
  disposed) and when an acquisition fails.
- With the version check on, an old git is refused as `GitTooOld` before anything is listed or fetched, and a new one is
  checked once.
- No hook runs, git failures become the provider's failure kinds without leaking text, cancellation stops the work, old
  commits are pruned, and a hand-edited definition fails closed.

#### Test Scenarios

**GitSourceProvider_ListVersions_ResolvesTagsToCommitsFiltersByPatternAndPreviews**: lightweight and annotated tags
resolve to commits (the annotated tag object is not returned), non-versions (`latest`, `v1.2`, `v1.2.3+build`, `x1.0.0`,
`V1.0.1`, `v1.2.3.4`, `v01.2.3`, `v1.2.3-01`) are skipped, an empty result is not an error, two tags that name one
version (they differ only in the letter case of the prerelease) keep the smaller name and log a warning, another package
ID has no versions and git is not run for it, the pattern's prefix narrows the listing with only the address' transport,
and the preview counts versions and other tags. This scenario is tested by
`GitSourceProvider_ListVersions_LightweightAndAnnotatedTags_ResolveToCommitsAndFilterByPattern`,
`GitSourceProvider_ListVersions_NoMatchingTags_ReturnsEmpty`,
`GitSourceProvider_ListVersions_TwoTagsSameVersion_KeepsSmallestTagAndWarns`,
`GitSourceProvider_ListVersions_OtherPackageId_ReturnsEmptyWithoutRunningGit`,
`GitSourceProvider_ListVersions_UsesPatternGlobAndAddressTransport`,
`GitSourceProvider_SshAddress_AllowsOnlySsh_AndListPackageIdsNeedsNoGit` and
`GitSourceProvider_Preview_SeparatesMatchingFromOtherTags`, covering `AgentControl-GitSourceProvider-ListPackageId`,
`AgentControl-GitSourceProvider-ListVersionsNoVersionsForOtherPackage`,
`AgentControl-GitSourceProvider-ListVersionsByPattern`, `AgentControl-GitSourceProvider-ListVersionsWithoutClone`,
`AgentControl-GitSourceProvider-ListVersionsOnlyOverAddressTransport`,
`AgentControl-GitSourceProvider-ListVersionsEmptyWhenNoMatch`,
`AgentControl-GitSourceProvider-ListVersionsDuplicateVersionKeepsSmallestTag`,
`AgentControl-GitSourceProvider-ResolveAnnotatedTags` and `AgentControl-GitSourceProvider-TagPreview`.

**GitSourceProvider_Acquire_DeliversLayoutWithIdentityCachesByCommitAndDetectsMovedTags**: the layout (four folders and
two root files) is delivered without `.git` or the repository's other files, the content identity is the
`files-sha256:` identity of the agent files and the source revision is
`git-commit:{sha}`, the package extracts into a repo, the cache folder is named by the commit with a marker and no
temporary folder remains; a second acquisition succeeds with the remote deleted; a moved tag lists as a new revision,
the
stale listing entry is refused before anything is fetched ("Refresh the list"), the new one gets its own folder and the
old folder stays; sources over different repositories have different cache folders under one root, and the same
repository spelled with another case of scheme and host has the same folder; an unmarked or stale-temporary folder is
replaced or swept. This scenario is tested by
`GitSourceProvider_Acquire_DeliversOnlyLayoutWithCommitRevisionAndNoGitFolder`,
`GitSourceProvider_Acquire_SecondTime_UsesCacheWithoutRemote`,
`GitSourceProvider_MovedTag_ShowsNewRevisionAndRefusesStaleListing`,
`GitSourceProvider_RepositoryCacheFolder_DiffersForDifferentRepositories` and
`GitSourceProvider_Acquire_SweepsStaleTemporaryFolderAndReplacesUnmarkedCacheFolder`, covering
`AgentControl-GitSourceProvider-AcquireShallowTag`, `AgentControl-GitSourceProvider-AcquireOnlyLayout`,
`AgentControl-GitSourceProvider-CacheByAddressAndCommit`, `AgentControl-GitSourceProvider-CacheSeparatesRepositories`,
`AgentControl-GitSourceProvider-CacheTrustsOnlyCompleteFolders`,
`AgentControl-GitSourceProvider-CacheSweepsTemporaryFolders`,
`AgentControl-GitSourceProvider-TagMovedBetweenListAndFetch`,
`AgentControl-GitSourceProvider-MovedTagReportedAsNewRevision`, `AgentControl-GitSourceProvider-ExcludeGitFolder` and
`AgentControl-GitSourceProvider-ReportContentIdentity`.

**GitSourceProvider_Acquire_OnlyListedVersions**: with a stub whose `ls-remote` lists `v1.0.0` at one commit, a package
that names `v1.0.0` at another commit, a package for the matching but unlisted `v2.0.0`, and a package that names
`v1.0.0-beta` when the version is listed under the smaller tag `v1.0.0-BETA` are each refused with an
`InvalidOperationException` ("Refresh the list" or "not offered by the repository") after exactly one `ls-remote` and no
`init` or `fetch`. This scenario is tested by
`GitSourceProvider_Acquire_ListedTagAtOtherCommit_IsRefusedBeforeFetching`,
`GitSourceProvider_Acquire_VersionNotListed_IsRefusedBeforeFetching` and
`GitSourceProvider_Acquire_TagThatIsNotTheListedOneForTheVersion_IsRefusedBeforeFetching`, covering
`AgentControl-GitSourceProvider-AcquireOnlyListedVersions`.

**GitSourceProvider_Acquire_DeliversExactBytesAndRunsNoFilter**: with the user's global configuration setting
`core.autocrlf=true` and `core.eol=crlf`, files committed with CRLF line endings, a lone carriage return, a NUL byte, a
byte-order mark, no final line feed and an empty file are delivered byte for byte; with a global configuration that
defines a filter driver `pwn` (smudge command writes a marker file and copies its input) and a repository whose
`.github/agents/.gitattributes` and `.github/standards/.gitattributes` select `filter=pwn`, `ident` and `text eol=lf`, a
plain `git checkout` of the same tag creates the marker (the control), while the acquisition does not, and the delivered
`$Id$` line and CRLF content are exactly the committed bytes. This scenario is tested by
`GitSourceProvider_Acquire_UserAutocrlfSetting_DoesNotChangeDeliveredBytes` and
`GitSourceProvider_Acquire_HostileFilterSelectedByRepository_NeverRunsAndContentIsRaw`, covering
`AgentControl-GitSourceProvider-AcquireExactBlobBytes` and `AgentControl-GitSourceProvider-NoRepositoryFilters`.

**GitSourceProvider_Acquire_RefusesLinksSubmodulesAndBadLayouts**: a symbolic link as a file in a managed folder, as a
managed folder, as `release-notes.md` or as `.github`, and a submodule in the layout are refused ("a symbolic link", "a
submodule") with nothing cached and no temporary folder left; links in `docs` and `.github/workflows` are ignored; a
file where `.github/skills` or `.github/agents` belongs and a folder where `release-notes.md` or `AGENTS.md` belongs are
refused ("is not a folder", "is not an ordinary file"); entries named `CON.md`, `com1`, `with:colon.md` and a name with
a
trailing dot are refused ("not safe on every file system"); a table of path components checks the safety rules and the
spellings of the git folder (`.git` in any case, with zero-width or direction characters, with trailing dots, `git~1`);
a layout in a sub-folder is read from there; a missing, linked or layout-less sub-folder and a tag without any layout
are
refused with a message that says why. This scenario is tested by
`GitSourceProvider_Acquire_LinkOrSubmoduleInLayout_IsRefusedAndNothingCached`,
`GitSourceProvider_Acquire_SymbolicLinkOutsideLayout_IsIgnored`,
`GitSourceProvider_Acquire_LayoutFolderAsFileOrRootFileAsFolder_IsRefusedAndNothingCached`,
`GitSourceProvider_Acquire_UnsafeEntryName_IsRefusedAndNothingCached`,
`GitSourceProvider_PathComponentRules_ClassifyUnsafeNamesAndGitFolderSpellings`,
`GitSourceProvider_Acquire_Subfolder_ReadsLayoutFromThatFolder`, `GitSourceProvider_Acquire_BadSubfolder_IsRefused` and
`GitSourceProvider_Acquire_TagWithoutLayout_IsRefused`, covering
`AgentControl-GitSourceProvider-RejectLinksAndSubmodulesInLayout`,
`AgentControl-GitSourceProvider-IgnoreLinksOutsideLayout`,
`AgentControl-GitSourceProvider-RejectLayoutEntryOfWrongKind`, `AgentControl-GitSourceProvider-RejectUnsafeEntryNames`,
`AgentControl-GitSourceProvider-RejectGitEntryInLayout`, `AgentControl-GitSourceProvider-Subfolder`,
`AgentControl-GitSourceProvider-RejectBadSubfolder` and `AgentControl-GitSourceProvider-RejectTagWithoutLayout`. (A tag
whose tree holds a `.git` entry cannot be built with git, which refuses such a path at every stage, so that rule is
proven by the table of path components.)

**GitSourceProvider_Acquire_RefusesFoldedPathCollisions**: the pairs `.github/agents/A.md` and `a.md`, a file
`.github/skills/notes` and a folder `.github/skills/NOTES/`, two folders `Docs` and `docs`, and `café.md` spelled
composed
and decomposed are committed with `hash-object` and `update-index --cacheinfo` (with git's case-folding and Unicode
composition settings turned off for that call, so neither the platform nor the user's configuration can merge them in
the
index), tagged and pushed; acquiring the tag fails with "differ only in letter case or Unicode form", no `{commit}`
folder
and no temporary folder exist afterwards. Because the check works on the listing, the outcome does not depend on the
file
system the test runs on. A table of path sets (including identical repeated paths and distinct names that are not
collisions) checks the detection itself. This scenario is tested by
`GitSourceProvider_FindFoldedPathCollision_DetectsCaseAndUnicodeFormCollisions` and
`GitSourceProvider_Acquire_FoldedPathCollisionInLayout_IsRefusedAndNothingCached`, covering
`AgentControl-GitSourceProvider-RejectFoldedPathCollisions`.

**GitSourceProvider_Acquire_FailsClosedWhenGitFolderRemains**: the test seam of `GitSourceOptions` replaces the removal
of `.git` with one that throws an `IOException` and, in a second case, one that does nothing (so the post-removal check
finds `.git`); the acquisition fails with an `InvalidOperationException` ("could not be finished in the cache folder"),
the `{commit}` folder does not exist, no `.tmp-*` folder remains, and a second acquisition (the seam now deletes for
real) delivers the layout with the marker and without `.git`. A leftover `.tmp-*` folder that holds a `.git` folder and
even a marker for the commit is never served - the package comes from the tag - and is swept once it is old. This
scenario is tested by
`GitSourceProvider_Acquire_GitFolderRemovalFails_IsRefusedNothingPublishedAndLaterAcquisitionSucceeds`
and `GitSourceProvider_Acquire_LeftoverTemporaryFolderWithGit_IsNeverServedAndIsSweptWhenStale`, covering
`AgentControl-GitSourceProvider-FailClosedWhenGitFolderRemains` and
`AgentControl-GitSourceProvider-LeftoverTemporaryFolderNeverUsed`.

**GitSourceProvider_Acquire_ReaderLeases**: with four commits and a cache that keeps two, a reader of the oldest commit
is kept open while the other three are acquired and released: the open reader's folder keeps its marker, lists six files
and reads its text although it is the oldest beyond the limit, while the old commit that no reader holds is pruned;
after the reader is disposed, a cache hit prunes again and the folder is gone, leaving the two newest. Two readers of
one commit, the first disposed twice, leave the commit held until the second is disposed. A tag without any layout is
refused and leaves no lease. Only readers of the same process are known to the registry; another process pruning the
same cache is a documented residual risk (see the design), not a tested behavior. This scenario is tested by
`GitSourceProvider_Acquire_ReaderHeldWhileOthersExceedLimit_KeepsHeldFolderUntilDisposed`,
`GitSourceProvider_Acquire_ReaderDisposedTwice_ReleasesItsLeaseOnlyOnce` and
`GitSourceProvider_Acquire_Refused_LeavesNoLease`, covering
`AgentControl-GitSourceProvider-CachePruningSparesOpenReaders` and
`AgentControl-GitSourceProvider-CacheReleasesCommitWhenReaderDisposed`.

**GitSourceProvider_EnforceMinimumGitVersion**: with `EnforceMinimumGitVersion` and a stub git that answers
`git version 2.20.1`, listing, previewing and acquiring each throw the `GitRemoteException` of kind `GitTooOld` itself
(its message names Git 2.29, not the source alias) and the stub is only ever asked for its version; with a stub that
answers `git version 2.43.0`, the version is asked once, before the first `ls-remote`, and not again for the second
listing. This scenario is tested by
`GitSourceProvider_EnforceMinimumGitVersion_OldGit_IsRefusedAsGitTooOldBeforeAnyGitWork` and
`GitSourceProvider_EnforceMinimumGitVersion_NewGit_IsCheckedOnceBeforeTheFirstListing`, covering
`AgentControl-GitSourceProvider-MinimumGitVersion`, `AgentControl-GitSourceProvider-GitTooOldReportedUnwrapped` and
`AgentControl-GitSourceProvider-MinimumGitVersionCheckedOnce`.

**GitSourceProvider_Acquire_NeverRunsHooksAndOnlyAcquiresItsOwnPackages**: with a template directory and a global
hooks path that install a hook, the hook's marker never appears; packages of another alias or package, without a
revision or with a malformed one, or with a tag that does not match are refused before git runs. This scenario is tested
by `GitSourceProvider_Acquire_HostileHooksAndTemplate_NeverRun`,
`GitSourceProvider_Acquire_PackageNotReportedBySource_IsRefused` and
`GitSourceProvider_Acquire_UnreportedPackage_IsRefusedWithoutRunningGit`, covering
`AgentControl-GitSourceProvider-HostileHooksNeverRun`, `AgentControl-GitSourceProvider-AcquireOnlyFromOwnSource` and
`AgentControl-GitSourceProvider-AcquireRequiresTagAndCommit`.

**GitSourceProvider_Failures_ValidationFlagsCancellationAndPruning**: an invalid address, pattern, sub-folder or package
list, a wrong type or a relative cache root fail at construction with nothing run and without repeating the address; the
interactive flag decides `GCM_INTERACTIVE`/askpass handling; authentication, host key, not-found and unreachable git
failures, a timeout, a missing git and an unsafe repository owner map to the provider's failure kinds or a plain error
whose message contains neither git's text nor the address nor the path git named; an untrusted ssh host key is reported
as
unreachable with the instruction to connect once with ssh and accept the key; a missing repository is `NotFound`;
cancellation raises `OperationCanceledException`; with a limit of two, only the two most recently used of four commits
remain. This scenario is tested by `GitSourceProvider_Constructor_InvalidSettings_FailsClosed`,
`GitSourceProvider_Constructor_WrongTypeBlankAliasOrRelativeCache_ThrowsArgumentException`,
`GitSourceProvider_InteractiveFlag_IsHonored`, `GitSourceProvider_GitFailure_IsTranslatedToFailureKind`,
`GitSourceProvider_GitFailure_UntrustedHostKey_TellsHowToTrustIt`,
`GitSourceProvider_OtherFailures_AreReportedClearly`,
`GitSourceProvider_ListVersions_RepositoryMissing_ReportsNotFound`,
`GitSourceProvider_ListVersions_Cancelled_ThrowsOperationCanceled`,
`GitSourceProvider_Acquire_AlreadyCancelled_ThrowsOperationCanceled` and
`GitSourceProvider_Acquire_PrunesOldestCommitsBeyondTheLimit`, covering
`AgentControl-GitSourceProvider-ValidateDefinition`,
`AgentControl-GitSourceProvider-ValidateDefinitionMessageNeverRepeatsAddress`,
`AgentControl-GitSourceProvider-InteractiveFlagHonored`, `AgentControl-GitSourceProvider-FailureClassification`,
`AgentControl-GitSourceProvider-UntrustedHostKeyHint`,
`AgentControl-GitSourceProvider-FailureMessagesNeverRepeatGitOutput`,
`AgentControl-GitSourceProvider-CancellationObserved` and `AgentControl-GitSourceProvider-CachePruning`.
