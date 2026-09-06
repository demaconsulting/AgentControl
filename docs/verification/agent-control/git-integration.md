## GitIntegration

### Verification Approach

The `GitIntegration` subsystem is verified indirectly through its constituent units' test
suites (`GitClientTests.cs` and `CommittedAgentFilesCacheTests.cs`), each documented in its
own unit-level verification design. `GitClientTests` uses `GitStub`, a fake git executable
script that records its invocation arguments and returns configured output/exit codes, so
tests never depend on a real git installation or a real repository's actual history.

### Test Environment

N/A - standard test environment; the `GitStub` fake executable removes any dependency on a
real git installation (see Verification Approach). Tests using `GitStub` belong to the
`RealProcess` xUnit collection (disabling parallelization) since they still launch a real
subprocess (the stub script itself).

### Acceptance Criteria

- All unit tests for `GitClient` and `CommittedAgentFilesCache` pass with zero failures.
- Working-tree cleanliness, pull outcome, committed-agent-files detection, and current-branch
  resolution all reflect the exact behavior configured on the git stub.
- The committed-agent-files cache never serves a stale result for a changed HEAD hash.

### Test Scenarios

**GitIntegration_PullGating_UnitTestsCoverCleanAndDirtyWorkingTree**: Working-tree
cleanliness reporting is verified by the `GitClient` unit tests (see the `GitClient` unit
verification design), with `GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue` and
`GitClient_IsWorkingTreeClean_DirtyRepo_ReturnsFalse` cited directly at the subsystem level,
covering `AgentControl-GitIntegration-PullGating` (child:
`AgentControl-GitClient-CheckWorkingTree`).

**GitIntegration_Pull_UnitTestsCoverSuccessAndFailureWithoutThrowing**: Pull success/failure
reporting is verified by the `GitClient` unit tests (see the `GitClient` unit verification
design), with `GitClient_Pull_Succeeds_ReturnsSucceededResult` and
`GitClient_Pull_Fails_ReturnsNonSucceededResultWithoutThrowing` cited directly at the
subsystem level, covering `AgentControl-GitIntegration-Pull` (child:
`AgentControl-GitClient-Pull`).

**GitIntegration_DetectCommittedFiles_UnitTestsCoverDetectionAndHashCaching**: Committed
agent-files detection and its HEAD-hash-keyed caching are verified by the `GitClient` and
`CommittedAgentFilesCache` unit tests (see their respective unit verification designs), with
`GitClient_HasCommittedAgentFiles_LsFilesReportsFiles_ReturnsTrue` and
`CommittedAgentFilesCache_SameRepoAndHash_ReturnsCachedValue` cited directly at the subsystem
level, covering `AgentControl-GitIntegration-DetectCommittedFiles` (children:
`AgentControl-GitClient-DetectCommittedFiles`, `AgentControl-CommittedAgentFilesCache-Cache`,
`AgentControl-GitClient-HeadHash`).

**GitIntegration_CurrentBranch_UnitTestsCoverFastPathResolution**: Current-branch resolution
is verified by the `GitClient` unit tests (see the `GitClient` unit verification design), with
`GitClient_GetCurrentBranch_FastPathNormalBranch_ReturnsBranchNameWithoutInvokingGit` cited
directly at the subsystem level, covering `AgentControl-GitIntegration-CurrentBranch` (child:
`AgentControl-GitClient-CurrentBranch`).
