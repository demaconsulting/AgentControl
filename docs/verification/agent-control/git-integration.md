## GitIntegration

### Verification Approach

The `GitIntegration` subsystem is verified indirectly through its constituent units' test
suites (`GitClientTests.cs`, `CommittedAgentFilesCacheTests.cs`, `GitCredentialClientTests.cs` /
`GitCredentialClientIntegrationTests.cs` and `GitRemoteClientTests.cs` /
`GitRemoteClientIntegrationTests.cs`), each documented in its
own unit-level verification design, plus `GitIntegrationSubsystemTests.cs`, which drives the saved-sign-in capability
through its contract with the real git and a throw-away credential helper, and the remote Git capability
(`GitRemoteClient`) the way the Git package source uses it: against a real local repository with the user's git
configuration replaced by an isolated one (and, for the sign-in test, against `GitHttpsServer.cs`, a private Git server
of the test), or against `GitRemoteStub`, a fake git executable that records its arguments and environment. The
subsystem test class reuses `GitRepositoryFixture` of the `AgentPackageManagement` test folder, a deliberate
cross-hierarchy dependency of test code only (the fixture builds real local repositories with the user's git
configuration isolated; nothing in the shipped code depends on it). `GitClientTests` uses `GitStub`,
a fake git executable
script that records its invocation arguments and returns configured output/exit codes, so
tests never depend on a real git installation or a real repository's actual history. The credential tests use
`GitCredentialStub`, a similar fake that also records the environment, working folder and standard input, and -
separately - run the real git against a throw-away credential helper in a temporary folder.

### Test Environment

The `GitStub`, `GitCredentialStub` and `GitRemoteStub` fake executables remove any dependency on a
real git installation for the status, pull and stub-based credential and remote tests (see Verification Approach). Tests
using them
belong to the
`RealProcess` xUnit collection (disabling parallelization) since they still launch a real
subprocess (the stub script itself). The real-git credential and remote tests need a git executable on `PATH`
(of at least the minimum supported version for the remote capability) and are skipped when there is none - and **failed
instead** when the environment variable `CI` is set, so a pipeline never passes with that evidence silently missing;
they never touch the user's real credential store, because every git process runs with the global and system
configuration replaced. The private-server test needs loopback networking (Kestrel with TLS on a fresh port, hosted by
the ASP.NET Core shared framework that the test project references) and trusts its throw-away certificate only through
the test's isolated git configuration.

### Acceptance Criteria

- All unit tests for `GitClient`, `CommittedAgentFilesCache`, `GitCredentialClient` and `GitRemoteClient` pass with zero
  failures.
- Working-tree cleanliness, pull outcome, committed-agent-files detection, and current-branch
  resolution all reflect the exact behavior configured on the git stub.
- The committed-agent-files cache never serves a stale result for a changed HEAD hash.
- A package feed's sign-in is found, kept and removed through the credential helper without any prompt, within a time
  limit, without a secret in the log, and a missing git or helper is "nothing saved" rather than an error.
- The remote Git capability lists the tags of a repository without a clone, fetches exactly one tag shallow, lists its
  tree and reads the raw bytes of its blobs (no conversion, and no filter program runs), and refuses a transport other
  than the one of the address.
- Work the application starts itself never lets git prompt and a repository that needs a sign-in is reported as
  "sign-in needed" without a secret; work the user starts lets the user's own helper prompt and then succeeds.
- A failure of the remote capability is classified and reported in one fixed sentence that repeats neither the address
  nor git's text; a git older than the minimum is refused with advice before any other command; a git that hangs is
  stopped at the time limit.

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

**GitIntegration_RemoteGit_SubsystemTestsRunTheRealGitAgainstALocalRepository**: Through the client, with the user's git
configuration replaced and no network, tags of a local bare repository are listed without a clone (an annotated tag
resolves to its commit), one tag is initialized and fetched shallow, its tree is listed, and the same address given with
a transport that is not allowed (https for a local path) is refused by git. In a second test the raw content of a listed
blob is read from the fetched commit under a user configuration that asks for line-ending conversion and defines a
filter program, with attributes that select the filter, expand identifiers and convert line endings: the bytes are those
of the commit, and the filter program never ran. The exact command lines, the prompt rules of background and
interactive work, trace-variable removal, time limits, cancellation, output limits, redaction and
failure classification are verified against a stub git by the `GitRemoteClient` unit tests (see the `GitRemoteClient`
unit verification design). These tests are
`GitIntegrationSubsystem_RemoteClient_ListsFetchesOneTagAndRefusesOtherTransports` and
`GitIntegrationSubsystem_RemoteClient_ReadsRawBlobsWithoutCheckoutFiltersOrConversion`, covering
`AgentControl-GitIntegration-RemoteGit` (children: the `AgentControl-GitRemoteClient-` tag listing, fetch, tree and blob
requirements) and `AgentControl-GitIntegration-RemoteGitNoCodeExecution` (children: the no-hook, transport allow-list,
no-submodule, no-filter, ssh, command-scoped configuration, trace-variable and neutral-folder requirements; the
transport refusal and the filter that never ran are the evidence at this level).

**GitIntegration_RemoteGit_PromptsOnlyForUserStartedWork**: With a stub standing in for git, work the application starts
itself runs git with the terminal, the credential manager's UI and both askpass programs switched off, while work the
user started leaves the credential manager free (and git never reads a terminal in either case). Against a private Git
server of the test (`GitHttpsServer.cs`: Kestrel on a loopback port, TLS with a throw-away self-signed certificate, the
smart HTTP protocol served by the real `git upload-pack`, a 401 for every request without the right credentials) and
a credential helper in an isolated git configuration that behaves like Git Credential Manager (it supplies the
credential only when `GCM_INTERACTIVE` is not `never`, and logs `asked` or `prompted`), the same tag listing started
as background work fails as "sign-in needed" without a secret in the message, the helper was asked but never prompted
and the server saw no credential, and started as user work the helper prompts, the request is signed in and the tags
are listed. These tests are `GitIntegrationSubsystem_RemoteClient_BackgroundWorkNeverPromptsWhileUserStartedWorkMay` and
`GitIntegrationSubsystem_RemoteClient_PrivateRepositorySignInIsOnlyRequestedForUserStartedWork`, covering
`AgentControl-GitIntegration-RemoteGitNoBackgroundPrompt` (child: `AgentControl-GitRemoteClient-NoPromptInBackground`)
and `AgentControl-GitIntegration-RemoteGitSignIn` (child: `AgentControl-GitRemoteClient-InteractiveWhenUserStarted`).

**GitIntegration_RemoteGit_FailuresAreClassifiedBoundedAndVersionChecked**: With the real git,
a repository that does not
exist is reported as "not found" in one fixed sentence that repeats neither the path nor git's text, and a git
executable that does not exist is reported as missing with a pointer to Settings. With a stub that reports a git older
than the minimum, the check refuses it with a message that says what to do before any other command ran, and with a stub
whose tag listing hangs, the listing is stopped at the time limit and reported as timed out, and the command never
finishes. These tests are
`GitIntegrationSubsystem_RemoteClient_FailuresAreClassifiedAndTheirMessagesNeverRepeatThePath` (covering
`AgentControl-GitIntegration-RemoteGitFailureReporting`, children: the fixed-message, secret-free-log, unsafe-value,
classification and missing-git requirements) and
`GitIntegrationSubsystem_RemoteClient_OldGitIsRefusedAndHungGitIsStoppedAtTheTimeLimit` (covering
`AgentControl-GitIntegration-RemoteGitMinimumVersion`, children: the `AgentControl-GitRemoteClient-MinimumGitVersion*`
and `-UnreadableVersionNotBlocked` requirements, and `AgentControl-GitIntegration-RemoteGitBounded`, children: the time
limit, cancellation, unstopped-process and output-limit requirements).
