### GitSourceProvider

#### Verification Approach

`GitSourceProvider` is verified two ways. `GitSourceProviderIntegrationTests.cs` uses the **real git** with local bare
repositories built by `GitRepositoryFixture` (a working repository that is committed to and tagged, pushed to a bare
"remote"); the user's global and system git configuration is replaced by an empty file, so a developer's credential
helper, `insteadOf` rewrite or `safe.directory` can never change a result. Symbolic-link and submodule entries are staged
with `hash-object` and `update-index --cacheinfo`, which needs no operating-system privilege, so the
tree-level refusal is
exercised on Windows, Linux and macOS alike. `GitSourceProviderTests.cs` uses the recording stub of
`GitRemoteClientTests` (`GitRemoteStub`) where no repository is needed: definition validation, the arguments and
environment the provider causes, the interactive flag, failure translation and cancellation.

#### Test Environment

The integration tests need a git executable on `PATH` and are skipped only when there is none. They belong to the
`RealProcess` collection (no parallelization) and run on the Windows, Ubuntu and macOS legs; no network or credential is
ever used. The hook test builds a hostile environment (a template directory and a global `core.hooksPath` that install a
`post-checkout` hook) and first proves, with plain git, that the hook would run; where hooks do not run at all the test
is skipped rather than reported as proof.

#### Acceptance Criteria

- Tags are listed with their commits (annotated tags peeled), filtered by the pattern, with duplicates resolved and
  unknown package IDs empty; the preview separates versions from other tags.
- An acquired package holds exactly the layout, no `.git`, the identity `git-commit:{sha}`, and is cached by commit so a
  second acquisition needs no remote; a moved tag shows as another revision and a stale listing is refused.
- A link, a submodule or a `.git` entry in the layout, a bad sub-folder and a tag without layout are refused with nothing
  cached or left behind; links elsewhere are ignored.
- Two layout entries that differ only in letter case or Unicode form (two files, a file and a folder, two folders, the
  composed and decomposed spelling of one name) are refused on every operating system before checkout, with nothing
  cached or left behind.
- When the `.git` folder cannot be removed (the removal fails or silently leaves it) the acquisition fails with an
  actionable message, no cache folder of the commit exists or is marked complete, no temporary folder is left, a
  leftover temporary folder is never served, and a later acquisition succeeds.
- With the version check on, an old git is refused as `GitTooOld` before anything is listed or fetched, and a new one is
  checked once.
- No hook runs, git failures become the provider's failure kinds without leaking text, cancellation stops the work, old
  commits are pruned, and a hand-edited definition fails closed.

#### Test Scenarios

**GitSourceProvider_ListVersions_ResolvesTagsToCommitsFiltersByPatternAndPreviews**: lightweight and annotated tags
resolve to commits (the annotated tag object is not returned), non-versions (`latest`, `v1.2`, `v1.2.3+build`, `x1.0.0`,
`V1.0.1`, `v1.2.3.4`) are skipped, an empty result is not an error, two tags naming one version keep the smaller name and
log a warning, another package ID has no versions, the pattern's prefix narrows the listing with only the address'
transport, and the preview counts versions and other tags. This scenario is tested by
`GitSourceProvider_ListVersions_LightweightAndAnnotatedTags_ResolveToCommitsAndFilterByPattern`,
`GitSourceProvider_ListVersions_NoMatchingTags_ReturnsEmpty`,
`GitSourceProvider_ListVersions_TwoTagsSameVersion_KeepsSmallestTagAndWarns`,
`GitSourceProvider_ListVersions_UsesPatternGlobAndAddressTransport`,
`GitSourceProvider_SshAddress_AllowsOnlySsh_AndListPackageIdsNeedsNoGit` and
`GitSourceProvider_Preview_SeparatesMatchingFromOtherTags`, covering `AgentControl-GitSourceProvider-ListPackageId`,
`AgentControl-GitSourceProvider-ListVersionsByPattern`, `AgentControl-GitSourceProvider-ResolveAnnotatedTags` and
`AgentControl-GitSourceProvider-TagPreview`.

**GitSourceProvider_Acquire_DeliversLayoutWithIdentityCachesByCommitAndDetectsMovedTags**: the layout (four folders and
two root files) is delivered without `.git` or the repository's other files, the content identity is
`git-commit:{sha}`, the package extracts into a repo, the cache folder is named by the commit with a marker and no
temporary folder remains; a second acquisition succeeds with the remote deleted; a moved tag lists as a new revision, the
stale listing entry is refused ("changed while it was being fetched"), the new one gets its own folder and the old folder
stays; an unmarked or stale-temporary folder is replaced or swept. This scenario is tested by
`GitSourceProvider_Acquire_DeliversOnlyLayoutWithCommitIdentityAndNoGitFolder`,
`GitSourceProvider_Acquire_SecondTime_UsesCacheWithoutRemote`,
`GitSourceProvider_MovedTag_ShowsNewRevisionAndRefusesStaleListing` and
`GitSourceProvider_Acquire_SweepsStaleTemporaryFolderAndReplacesUnmarkedCacheFolder`, covering
`AgentControl-GitSourceProvider-AcquireShallowTag`, `AgentControl-GitSourceProvider-CacheByAddressAndCommit`,
`AgentControl-GitSourceProvider-TagMovedBetweenListAndFetch`, `AgentControl-GitSourceProvider-ExcludeGitFolder` and
`AgentControl-GitSourceProvider-ReportContentIdentity`.

**GitSourceProvider_Acquire_RefusesLinksSubmodulesAndBadLayouts**: a symbolic link as a file in a managed folder, as a
managed folder, as `release-notes.md` or as `.github`, and a submodule in the layout are refused ("a symbolic link", "a
submodule") with nothing cached and no temporary folder left; links in `docs` and `.github/workflows` are ignored; a
layout in a sub-folder is read from there; a missing, linked or layout-less sub-folder and a tag without any layout are
refused with a message that says why. This scenario is tested by
`GitSourceProvider_Acquire_LinkOrSubmoduleInLayout_IsRefusedAndNothingCached`,
`GitSourceProvider_Acquire_SymbolicLinkOutsideLayout_IsIgnored`,
`GitSourceProvider_Acquire_Subfolder_ReadsLayoutFromThatFolder`, `GitSourceProvider_Acquire_BadSubfolder_IsRefused` and
`GitSourceProvider_Acquire_TagWithoutLayout_IsRefused`, covering
`AgentControl-GitSourceProvider-RejectLinksAndSubmodulesInLayout` and `AgentControl-GitSourceProvider-Subfolder`.

**GitSourceProvider_Acquire_RefusesFoldedPathCollisions**: the pairs `.github/agents/A.md` and `a.md`, a file
`.github/skills/notes` and a folder `.github/skills/NOTES/`, two folders `Docs` and `docs`, and `café.md` spelled composed
and decomposed are committed with `hash-object` and `update-index --cacheinfo` (with git's case-folding and Unicode
composition settings turned off for that call, so neither the platform nor the user's configuration can merge them
in the
index), tagged and pushed; acquiring the tag fails with "differ only in letter case or Unicode form", no `{commit}` folder
and no temporary folder exist afterwards. Because the check works on the listing, the outcome does not depend on the file
system the test runs on. A table of path sets (including identical repeated paths and distinct names that are not
collisions) checks the detection itself. This scenario is tested by
`GitSourceProvider_FindFoldedPathCollision_DetectsCaseAndUnicodeFormCollisions` and
`GitSourceProvider_Acquire_FoldedPathCollisionInLayout_IsRefusedAndNothingCached`, covering
`AgentControl-GitSourceProvider-RejectFoldedPathCollisions`.

**GitSourceProvider_Acquire_FailsClosedWhenGitFolderRemains**: the test seam of `GitSourceOptions` replaces the removal
of `.git` with one that throws an `IOException` and, in a second case, one that does nothing (so the post-removal check
finds `.git`); the acquisition fails with an `InvalidOperationException` ("could not be finished in the cache
folder"), the
`{commit}` folder does not exist, no `.tmp-*` folder remains, and a second acquisition (the seam now deletes for real)
delivers the layout with the marker and without `.git`. A leftover `.tmp-*` folder that holds a `.git` folder and even a
marker for the commit is never served - the package comes from the tag - and is swept once it is old. This scenario is
tested by `GitSourceProvider_Acquire_GitFolderRemovalFails_IsRefusedNothingPublishedAndLaterAcquisitionSucceeds` and
`GitSourceProvider_Acquire_LeftoverTemporaryFolderWithGit_IsNeverServedAndIsSweptWhenStale`, covering
`AgentControl-GitSourceProvider-FailClosedWhenGitFolderRemains`.

**GitSourceProvider_EnforceMinimumGitVersion**: with `EnforceMinimumGitVersion` and a stub git that answers
`git version 2.20.1`, listing, previewing and acquiring each throw the `GitRemoteException` of kind `GitTooOld` itself
(its message names Git 2.29, not the source alias) and the stub is only ever asked for its version; with a stub that
answers `git version 2.43.0`, the version is asked once, before the first `ls-remote`, and not again for the second
listing. This scenario is tested by
`GitSourceProvider_EnforceMinimumGitVersion_OldGit_IsRefusedAsGitTooOldBeforeAnyGitWork` and
`GitSourceProvider_EnforceMinimumGitVersion_NewGit_IsCheckedOnceBeforeTheFirstListing`, covering
`AgentControl-GitSourceProvider-MinimumGitVersion`.

**GitSourceProvider_Acquire_NeverRunsHooksAndOnlyAcquiresItsOwnPackages**: with a template directory and a global
hooks path that install a hook, the hook's marker never appears; packages of another alias or package, without a
revision or with a malformed one, or with a tag that does not match are refused before git runs. This scenario is tested
by `GitSourceProvider_Acquire_HostileHooksAndTemplate_NeverRun`,
`GitSourceProvider_Acquire_PackageNotReportedBySource_IsRefused` and
`GitSourceProvider_Acquire_UnreportedPackage_IsRefusedWithoutRunningGit`, covering
`AgentControl-GitSourceProvider-HostileHooksNeverRun` and `AgentControl-GitSourceProvider-AcquireOnlyFromOwnSource`.

**GitSourceProvider_Failures_ValidationFlagsCancellationAndPruning**: an invalid address, pattern, sub-folder or package
list, a wrong type or a relative cache root fail at construction with nothing run; the interactive flag decides
`GCM_INTERACTIVE`/askpass handling; authentication, host key, not-found and unreachable git failures, a timeout, a
missing git and an unsafe repository owner map to the provider's failure kinds or a plain error without raw git text;
a missing repository is `NotFound`; cancellation raises `OperationCanceledException`; with a limit of two, only the two
most recently used of four commits remain. This scenario is tested by
`GitSourceProvider_Constructor_InvalidSettings_FailsClosed`,
`GitSourceProvider_Constructor_WrongTypeBlankAliasOrRelativeCache_ThrowsArgumentException`,
`GitSourceProvider_InteractiveFlag_IsHonored`, `GitSourceProvider_GitFailure_IsTranslatedToFailureKind`,
`GitSourceProvider_OtherFailures_AreReportedClearly`, `GitSourceProvider_ListVersions_RepositoryMissing_ReportsNotFound`,
`GitSourceProvider_ListVersions_Cancelled_ThrowsOperationCanceled`,
`GitSourceProvider_Acquire_AlreadyCancelled_ThrowsOperationCanceled` and
`GitSourceProvider_Acquire_PrunesOldestCommitsBeyondTheLimit`, covering
`AgentControl-GitSourceProvider-ValidateDefinition`, `AgentControl-GitSourceProvider-InteractiveFlagHonored`,
`AgentControl-GitSourceProvider-FailureClassification`, `AgentControl-GitSourceProvider-CancellationObserved` and
`AgentControl-GitSourceProvider-CachePruning`.
