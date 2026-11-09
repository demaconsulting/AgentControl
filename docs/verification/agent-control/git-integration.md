## GitIntegration

### Verification Approach

The `GitIntegration` subsystem is verified indirectly through its constituent units' test
suites (`GitClientTests.cs`, `CommittedAgentFilesCacheTests.cs` and `GitCredentialClientTests.cs` /
`GitCredentialClientIntegrationTests.cs`) and `GitRemoteClientTests.cs` /
`GitRemoteClientIntegrationTests.cs`), each documented in its
own unit-level verification design, plus `GitIntegrationSubsystemTests.cs`, which drives the saved-sign-in capability
through its contract with the real git and a throw-away credential helper. `GitClientTests` uses `GitStub`, a fake git executable
script that records its invocation arguments and returns configured output/exit codes, so
tests never depend on a real git installation or a real repository's actual history. The credential tests use
`GitCredentialStub`, a similar fake that also records the environment, working folder and standard input, and -
separately - run the real git against a throw-away credential helper in a temporary folder.

### Test Environment

N/A - standard test environment; the `GitStub` and `GitCredentialStub` fake executables remove any dependency on a
real git installation for the status, pull and stub-credential tests (see Verification Approach). Tests using them
belong to the
`RealProcess` xUnit collection (disabling parallelization) since they still launch a real
subprocess (the stub script itself). The real-git credential tests need a git executable on `PATH` and are skipped
only when there is none; they never touch the user's real credential store.

### Acceptance Criteria

- All unit tests for `GitClient`, `CommittedAgentFilesCache` and `GitCredentialClient` pass with zero failures.
- Working-tree cleanliness, pull outcome, committed-agent-files detection, and current-branch
  resolution all reflect the exact behavior configured on the git stub.
- The committed-agent-files cache never serves a stale result for a changed HEAD hash.
- A package feed's sign-in is found, kept and removed through the credential helper without any prompt, within a time
  limit, without a secret in the log, and a missing git or helper is "nothing saved" rather than an error.

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

**GitIntegration_SavedSignIns_SubsystemTestsUseTheRealCredentialHelperProtocol**: Through the credential-store
contract the rest of the application uses, a sign-in kept by one instance (one session) is found by another, a lookup
before that finds nothing without any prompt, forgetting by origin removes it for every instance, a sign-in whose
values the line-based protocol cannot carry (a line feed, carriage return or NUL) is never handed to the helper -
nothing is stored - while a token with `=` padding is kept and found unchanged, and with no helper configured the
subsystem reports "unavailable" and finds nothing instead of failing,
all against the real git and a throw-away helper. This scenario is tested by
`GitIntegrationSubsystem_SavedSignIn_KeptInOneSessionFoundInAnotherAndRemovedOnRequest`,
`GitIntegrationSubsystem_SavedSignIn_ValueWithLineBreakIsNeverStoredButEqualsIs` and
`GitIntegrationSubsystem_SavedSignIn_NoHelperConfigured_ReportsUnavailableAndNothingSaved`, covering
`AgentControl-GitIntegration-SavedSignIns`. The exact requests, the never-interactive environment, the time limit
and the absence of secrets from the log are verified against a stub git by the `GitCredentialClient` unit tests (see
the `GitCredentialClient` unit verification design) and covered by the children
`AgentControl-GitCredentialClient-FillApproveReject`, `AgentControl-GitCredentialClient-HelperAvailability`,
`AgentControl-GitCredentialClient-NeverInteractive`, `AgentControl-GitCredentialClient-BoundedTime`,
`AgentControl-GitCredentialClient-UnavailableIsNotAnError` and `AgentControl-GitCredentialClient-SecretsNeverLogged`.

**GitIntegration_RemoteGit_SubsystemTestRunsTheRealGitAgainstALocalRepository**: Through the client, with the user's git
configuration replaced and no network, tags of a local bare repository are listed without a clone (an annotated tag
resolves to its commit), one tag is initialized and fetched shallow, its tree is listed, and the same address given with
a transport that is not allowed (https for a local path) is refused by git. The exact command lines, the prompt rules
of background and interactive work, trace-variable removal, time limits, cancellation, output limits, redaction and
failure classification are verified against a stub git by the `GitRemoteClient` unit tests (see the `GitRemoteClient`
unit verification design). This scenario is tested by
`GitIntegrationSubsystem_RemoteClient_ListsFetchesOneTagAndRefusesOtherTransports`, covering
`AgentControl-GitIntegration-RemoteGit` (children: every `AgentControl-GitRemoteClient-*` requirement).
