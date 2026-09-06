### GitClient

#### Verification Approach

`GitClient` is verified with unit tests defined in `GitClientTests.cs`. Every test runs
against `GitStub`, a fake git executable script (matching the pattern used by
`ProgramTests`/`RepoCardViewModelTests`) that records its invocation arguments and returns
configured stdout/stderr/exit-code values, so tests never depend on a real git installation
or a real repository history. Tests using `GitStub` belong to the `RealProcess` xUnit
collection since they still launch a real subprocess (the stub script itself).

#### Test Environment

N/A - standard test environment; `RealProcess` collection applies (see Verification
Approach).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Every git-derived status/action reflects exactly the outcome configured on the stub.
- A missing executable or a failed underlying git command raises a clear
  `InvalidOperationException` rather than propagating a raw process failure.
- The current-branch fast path avoids invoking git whenever `.git/HEAD` can be read directly.

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
