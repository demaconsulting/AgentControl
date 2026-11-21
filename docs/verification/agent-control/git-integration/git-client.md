### GitClient

#### Verification Approach

`GitClient` is verified with unit tests defined in `GitClientTests.cs`. Most tests run against `GitStub`, a fake git
executable script (matching the pattern used by `ProgramTests`/`RepoCardViewModelTests`) that records its invocation
arguments and returns configured stdout/stderr/exit-code values - and, for the time-limit tests, sleeps before it
answers a chosen command and then writes a marker, so a test can tell that the command was stopped - so those tests
never depend on a real git installation or a real repository history. Tests using `GitStub` belong to the `RealProcess`
xUnit collection since they still launch a real subprocess (the stub script itself). The tests of listing and stopping
git tracking agent folders also use a real temporary git repository (built by `GitRepositoryFixture`, skipped where git
is not installed and failed when the environment variable `CI` is set), because what git tracks before and after must be
observed, not assumed.

#### Test Environment

N/A - standard test environment; `RealProcess` collection applies (see Verification
Approach).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Every git-derived status/action reflects exactly the outcome configured on the stub.
- A missing executable or a failed underlying git command raises a clear
  `InvalidOperationException` rather than propagating a raw process failure.
- The current-branch fast path avoids invoking git whenever `.git/HEAD` can be read directly.
- The repository-root check reports a root only for a folder with its own `.git` folder or file.
- A command that outlives its time limit, or whose caller cancels it, is stopped before it finishes and reported as
  timed out (with the limit) or canceled; a pull runs under its own, longer limit.
- On a thread guarded against blocking calls no git command starts.

#### Test Scenarios

**GitClient_CheckWorkingTree_ReflectsCleanDirtyAndFailureStates**: A clean working tree
returns true, a dirty one returns false, a failed status command throws
`InvalidOperationException`, and a nonexistent git executable also throws
`InvalidOperationException`. This scenario is tested by
`GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue`,
`GitClient_IsWorkingTreeClean_DirtyRepo_ReturnsFalse`,
`GitClient_IsWorkingTreeClean_StatusCommandFails_ThrowsInvalidOperationException`, and
`GitClient_IsWorkingTreeClean_ExecutableNotFound_ThrowsInvalidOperationException`, covering
`AgentControl-GitClient-CheckWorkingTree`.

**GitClient_Pull_ReportsResultWithoutThrowingOnFailure**: A successful pull returns a
succeeded result, and a failed pull returns a non-succeeded result without throwing. This
scenario is tested by `GitClient_Pull_Succeeds_ReturnsSucceededResult` and
`GitClient_Pull_Fails_ReturnsNonSucceededResultWithoutThrowing`, covering
`AgentControl-GitClient-Pull`.

**GitClient_DetectCommittedFiles_ReportsPresenceOrRaisesOnFailure**: A repo with tracked
agent files reports true, one with none reports false, and a failed `ls-files` query throws
`InvalidOperationException`. This scenario is tested by
`GitClient_HasCommittedAgentFiles_LsFilesReportsFiles_ReturnsTrue`,
`GitClient_HasCommittedAgentFiles_LsFilesReportsNothing_ReturnsFalse`, and
`GitClient_HasCommittedAgentFiles_LsFilesFails_ThrowsInvalidOperationException`, covering
`AgentControl-GitClient-DetectCommittedFiles`.

**GitClient_ListTrackedAgentFolders_ReportsOnlyTrackedFoldersOrRaisesOnFailure**: In a real
repo that tracks some of the four agent folders, only those are reported, in the fixed folder
order, and an untracked folder that merely exists on disk is not; a file tracked at the exact path of a
managed folder counts while a prefix sibling (`.github/standards-old`) does not; a repo that tracks none
reports an empty list; and a failed `ls-files` query throws `InvalidOperationException`. This
scenario is tested by
`GitClient_GetTrackedAgentFolders_SomeFoldersTracked_ReturnsOnlyThoseInKnownOrder`,
`GitClient_GetTrackedAgentFolders_NothingTracked_ReturnsEmptyList`,
`GitClient_GetTrackedAgentFolders_ExactPathTracked_CountsItButNotPrefixSibling`, and
`GitClient_GetTrackedAgentFolders_LsFilesFails_ThrowsInvalidOperationException`, covering
`AgentControl-GitClient-ListTrackedAgentFolders`.

**GitClient_UntrackAgentFolders_RemovesFromIndexOnlyOrRaisesOnFailure**: In a real repo,
stopping git tracking two of three tracked agent folders removes exactly those from git's index, leaves
every file on disk with its content and every other tracked path (and the third folder) still
tracked, and a failing `git rm` throws `InvalidOperationException` naming the command. This
scenario is tested by
`GitClient_UntrackAgentFolders_TrackedFolders_RemovesThemFromIndexOnlyAndKeepsFilesOnDisk` and
`GitClient_UntrackAgentFolders_GitRmFails_ThrowsInvalidOperationException`, covering
`AgentControl-GitClient-UntrackAgentFolders`.

**GitClient_RestoreAgentFolderTracking_UndoesUntrack**: In a real repo, a folder that was untracked is tracked again
after `RestoreAgentFolderTracking` and its file is unchanged on disk. This scenario is tested by
`GitClient_RestoreAgentFolderTracking_AfterUntrack_RestoresIndexAndKeepsFiles`, covering
`AgentControl-GitClient-RestoreAgentFolderTracking`.

**GitClient_UntrackAgentFolders_RefusesAnythingButAgentFolders**: An empty list, a folder that
is not a known agent folder, an option-like value and `null` are refused with
`ArgumentException`/`ArgumentNullException` and git is never invoked (the stub's invocation log
stays absent). This scenario is tested by
`GitClient_UntrackAgentFolders_EmptyOrUnknownFolders_ThrowsArgumentException`, covering
`AgentControl-GitClient-UntrackOnlyAgentFolders`.

**GitClient_CurrentBranch_FastPathHandlesAllHeadFormsWithSubprocessFallback**: The fast path
resolves a normal branch, a detached HEAD marker, and a worktree indirection directly from
`.git/HEAD` without invoking git; a missing `.git` directory or unrecognized `HEAD` content
falls back to a subprocess call; and a fallback failure throws `InvalidOperationException`.
This scenario is tested by
`GitClient_GetCurrentBranch_FastPathNormalBranch_ReturnsBranchNameWithoutInvokingGit`,
`GitClient_GetCurrentBranch_FastPathDetachedHead_ReturnsDetachedMarker`,
`GitClient_GetCurrentBranch_FastPathWorktreeIndirection_ReturnsBranchName`,
`GitClient_GetCurrentBranch_NoGitDirectory_FallsBackToSubprocess`,
`GitClient_GetCurrentBranch_UnrecognizedHeadContent_FallsBackToSubprocess`, and
`GitClient_GetCurrentBranch_FallbackFails_ThrowsInvalidOperationException`, covering
`AgentControl-GitClient-CurrentBranch`.

**GitClient_HeadHash_ReturnsTrimmedHashOrRaisesOnFailureOrEmptyExecutablePath**: Reading the
HEAD commit hash returns it trimmed of surrounding whitespace; a failed query throws
`InvalidOperationException`; and constructing a `GitClient` with an empty executable path
throws `ArgumentException`. This scenario is tested by
`GitClient_GetHeadCommitHash_Succeeds_ReturnsTrimmedHash`,
`GitClient_GetHeadCommitHash_Fails_ThrowsInvalidOperationException`, and
`GitClient_Constructor_EmptyExecutablePath_ThrowsArgumentException`, covering
`AgentControl-GitClient-HeadHash`.

**GitClient_RepositoryRoot_OnlyAFolderWithItsOwnGitIsARoot**: A folder with a `.git` folder directly in it is a
repository root, and so is a folder with a `.git` file (as a linked worktree or a submodule has); a folder without
either, a subfolder of a repository (the parents are not searched) and a folder that does not exist are not, and the
check does not fail for the missing folder; an empty or whitespace path is refused with `ArgumentException`. These
tests build plain temporary folders and need neither git nor the stub, because the check starts no process. This
scenario is tested by `GitClient_IsRepositoryRoot_GitDirectory_ReturnsTrue`,
`GitClient_IsRepositoryRoot_GitFile_ReturnsTrue`, `GitClient_IsRepositoryRoot_NoGit_ReturnsFalse`,
`GitClient_IsRepositoryRoot_SubfolderOfRepository_ReturnsFalse`, `GitClient_IsRepositoryRoot_MissingFolder_ReturnsFalse`
and `GitClient_IsRepositoryRoot_BlankPath_ThrowsArgumentException`, covering `AgentControl-GitClient-RepositoryRoot`.

**GitClient_TimeLimit_HungGitIsStoppedAndReported**: A stub whose `status` sleeps well past a one-second local limit
is stopped (it never writes its post-sleep marker) and reported as `GitCommandTimeoutException` - still an
`InvalidOperationException` - naming `git status` and the one-second limit
(`GitClient_IsWorkingTreeClean_GitHangsPastTimeout_StopsItAndThrowsTimedOut`); a pull that takes two seconds completes
under the same one-second local limit, because a pull runs under the pull limit
(`GitClient_Pull_UsesPullTimeoutNotLocalTimeout`). These cover `AgentControl-GitClient-TimeoutStopsGit`.

**GitClient_Cancel_RunningGitIsStopped**: A stub whose `rev-parse HEAD` hangs is canceled by its caller after one
second; the call throws `OperationCanceledException` and the stub never finishes. This scenario is tested by
`GitClient_GetHeadCommitHash_Canceled_StopsGitAndThrowsCanceled`, covering `AgentControl-GitClient-CancelStopsGit`.

**GitClient_GuardedThread_NoGitStarted**: On a thread guarded by `BlockingCallGuard`, `IsWorkingTreeClean`, `Pull`,
`GetHeadCommitHash`, `HasCommittedAgentFiles` and `GetCurrentBranch` are each refused with
`BlockingCallOnGuardedThreadException`, five refusals are recorded, and the stub's invocation log was never written, so
git never started. This scenario is tested by `GitClient_AnyCommand_OnGuardedThread_ThrowsBlockingCallOnGuardedThread`;
it verifies a guard point of `Quality-UiThreadRegressionGuard` and is not linked to a `GitClient` requirement.
