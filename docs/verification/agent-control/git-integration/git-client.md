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
observed, not assumed. The branch and clone tests come in two kinds. The stub kind, in `GitClientTests.cs`, pins down
the command lines (the stub is given extra canned commands - `for-each-ref`, `remote`, `switch`, `clone`,
`rev-parse --short` - whose output is read from files, and logs all arguments of each call) and the time-limit,
cancellation and clean-up behavior (the stub's `clone` can create the destination with a read-only pack file and then
hang). The real kind, in `GitClientRealRepositoryTests.cs`, proves that git really does what the command lines assume,
against a real working repository and a local bare repository playing the remote (never the network), with the user's
global git configuration replaced by an empty one through the client's `extraEnvironment`.

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
- A command that exited while a process it started still holds its output open is not waited for past its limit: it
  is reported as timed out (with the limit) instead of returning once that process ends.
- On a thread guarded against blocking calls no git command starts.
- The branch list holds the local and remote-tracking branches git already knows, marks the checked-out one (or
  reports a detached HEAD with its short commit id) and never contacts a remote.
- A switch never passes an option that forces, discards or merges; a switch git refuses is returned with git's
  explanation and leaves the branch and the user's uncommitted work as they were.
- A clone refuses an address git could read as an option or a remote helper, and a destination that is not new or
  empty, before git starts; a canceled or timed-out clone removes only what it created; a failed clone deletes nothing;
  the password of an address appears in no log message, exception message or result.

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
(`GitClient_Pull_UsesPullTimeoutNotLocalTimeout`). A stub whose `status` answers and exits at once, but first starts
a background copy of itself that inherits its output and keeps it open for four seconds (standing in for a helper git
started that outlives git), is reported as `GitCommandTimeoutException` naming `git status` and the one-second limit,
rather than returning once the helper ends
(`GitClient_IsWorkingTreeClean_HelperHoldsOutputAfterGitExits_StopsWaitingAndThrowsTimedOut`). Whether the helper was
killed is not asserted: killing what git started after git exited is best effort and platform-dependent (on Linux and
macOS the helper is handed to another parent when the stub exits), and the outcome - a timeout - is the same on every
platform; the test waits for the helper's four seconds before it removes the stub. These cover
`AgentControl-GitClient-TimeoutStopsGit`.

**GitClient_Cancel_RunningGitIsStopped**: A stub whose `rev-parse HEAD` hangs is canceled by its caller after one
second; the call throws `OperationCanceledException` and the stub never finishes. This scenario is tested by
`GitClient_GetHeadCommitHash_Canceled_StopsGitAndThrowsCanceled`, covering `AgentControl-GitClient-CancelStopsGit`.

**GitClient_GuardedThread_NoGitStarted**: On a thread guarded by `BlockingCallGuard`, `IsWorkingTreeClean`, `Pull`,
`GetHeadCommitHash`, `HasCommittedAgentFiles` and `GetCurrentBranch` are each refused with
`BlockingCallOnGuardedThreadException`, five refusals are recorded, and the stub's invocation log was never written, so
git never started. This scenario is tested by `GitClient_AnyCommand_OnGuardedThread_ThrowsBlockingCallOnGuardedThread`;
it verifies a guard point of `Quality-UiThreadRegressionGuard` and is not linked to a `GitClient` requirement.

**GitClient_ListBranches_ReportsLocalRemoteAndDetached**: A stub that lists two local branches (one current) and
remote branches of two remotes - one remote named `up/stream` - is reported with the current branch marked, the remote
`up/stream` split from its branch `z`, the remote's `HEAD` pointer and the remote `main` (which has a local twin)
left out; a stub whose listing carries no current marker reports `null` and the short commit id of `rev-parse --short
HEAD`; a failing `for-each-ref` throws `InvalidOperationException` carrying git's text. In a real clone of a remote
with `main`, `feature/x` and `other`, the list holds local `main` and remote `origin/feature/x` and `origin/other`
(not `origin/HEAD`, not `origin/main`), and a clone with a detached HEAD reports a commit id that starts the full
`HEAD`. This scenario is tested by `GitClient_GetBranches_ListsLocalAndRemoteWithCurrentMarked`,
`GitClient_GetBranches_DetachedHead_ReportsNullBranchAndShortCommit`,
`GitClient_GetBranches_ForEachRefFails_ThrowsInvalidOperationException`,
`GitClient_GetBranches_RealRepo_ListsLocalAndRemoteTracking` and
`GitClient_SwitchBranch_RealRepo_FromDetachedHead_Works`, covering `AgentControl-GitClient-ListBranches`,
`AgentControl-GitClient-ListBranchesRemoteDistinct` and `AgentControl-GitClient-ListBranchesDetached`.

**GitClient_SwitchBranch_RunsSafeCommandsAndReportsRefusals**: The stub's argument log shows `switch -- feature/x` for
a local branch and `switch --track -- origin/feature/y` for a remote one, and the only options ever present are `--`
and `--track`; a blank, `-f`, `--detach` or multi-line name is refused with `ArgumentException` and the stub is never
started; a stub whose `switch` takes two seconds completes under a one-second local limit (the switch limit applies); a
stub whose `switch` fails with "would be overwritten" is returned as a result, not thrown. In real repositories a switch
to a remote branch creates a local tracking branch (`branch.feature/x.remote` is `origin`) and changes the files, a
switch back works, a switch that would overwrite an edited file or an untracked file is refused with git's text naming
the file, the current branch stays and the file content is intact. This scenario is tested by
`GitClient_SwitchBranch_Local_RunsSwitchWithDoubleDash`, `GitClient_SwitchBranch_Remote_RunsSwitchTrackWithRemoteName`,
`GitClient_SwitchBranch_NeverPassesForceOrDiscardOptions`, `GitClient_SwitchBranch_BlankOrOptionLikeName_ThrowsArgumentException`,
`GitClient_SwitchBranch_UsesSwitchTimeoutNotLocalTimeout`,
`GitClient_SwitchBranch_Refused_ReturnsGitsMessageWithoutThrowing`,
`GitClient_SwitchBranch_RealRepo_LocalAndRemoteCreatesTrackingBranch`,
`GitClient_SwitchBranch_RealRepo_DirtyConflictingFile_IsRefusedWithGitsReasonAndChangesNothing` and
`GitClient_SwitchBranch_RealRepo_UntrackedFileWouldBeOverwritten_IsRefused`, covering
`AgentControl-GitClient-SwitchBranch`, `AgentControl-GitClient-SwitchNeverForces` and
`AgentControl-GitClient-SwitchRefusalReported`.

**GitClient_Clone_RunsInParentWithDataArgumentsAndRefusesBadInput**: The stub's argument log shows
`clone -- <trimmed url> <destination>`; an address that starts with a dash, is blank, spans two lines or uses `ext::`,
`EXT::` or `fd::` is refused with `ArgumentException` and the stub is never started, while ordinary https, ssh, scp-like
and path addresses are accepted by `CheckCloneUrl`; a destination that is a non-empty folder (whose file stays intact),
has a missing parent, is relative or is a file is refused with a reason; a new or an empty folder is accepted; a
stub whose `clone` takes two seconds completes under a one-second local limit (the clone limit applies); on a guarded
thread the branch list, the switch, the clone and the destination check are each refused with
`BlockingCallOnGuardedThreadException`. A real clone of a local bare repository works into a new folder and into an
existing empty folder (a repository root on `main` with the committed file), and a clone of a path that is not a
repository is a failed result. This scenario is tested by `GitClient_Clone_Succeeds_RunsCloneWithDoubleDashAndArgumentList`,
`GitClient_Clone_RefusedUrl_ThrowsArgumentExceptionAndRunsNoGit`, `GitClient_CheckCloneUrl_UsualAddress_ReturnsNull`,
`GitClient_Clone_DestinationExistsNotEmpty_ThrowsArgumentExceptionAndTouchesNothing`,
`GitClient_Clone_ParentMissingRelativeOrFile_ThrowsArgumentException`,
`GitClient_CheckCloneDestination_NewOrEmptyFolder_ReturnsNull`, `GitClient_Clone_UsesCloneTimeoutNotLocalTimeout`,
`GitClient_Constructor_NonPositiveSwitchOrCloneLimit_ThrowsArgumentOutOfRangeException`,
`GitClient_BranchAndCloneCommands_OnGuardedThread_ThrowBlockingCallOnGuardedThread`,
`GitClient_Clone_RealLocalBareRepository_ClonesIntoNewAndIntoEmptyFolder` and
`GitClient_Clone_RealGit_NotARepository_ReturnsFailedResult`, covering `AgentControl-GitClient-Clone`,
`AgentControl-GitClient-CloneRefusesOptionLikeUrl` and `AgentControl-GitClient-CloneDestinationRules`.

**GitClient_Clone_StopsAndCleansUpOnlyWhatItCreated**: A stub whose `clone` creates its destination (with a read-only
pack file) and then hangs is canceled after the file exists: the call throws `OperationCanceledException`, the stub never
finishes, and the destination - which the call created - is gone while its parent remains; with an existing empty
destination the folder stays and is empty afterwards; with a one-second clone limit the same stub is stopped and
reported as `GitCommandTimeoutException` naming `git clone` and the limit, and the destination is removed; a stub whose
`clone` leaves its destination and exits with an error is returned as a failed result and the destination is left alone.
This scenario is tested by `GitClient_Clone_Canceled_RemovesDestinationItCreated`,
`GitClient_Clone_CanceledIntoExistingEmptyFolder_RemovesContentsKeepsFolder`,
`GitClient_Clone_TimesOut_StopsGitAndRemovesDestinationItCreated` and `GitClient_Clone_Fails_LeavesFolderAlone`
(and, for a folder with content, `GitClient_Clone_DestinationExistsNotEmpty_ThrowsArgumentExceptionAndTouchesNothing`),
covering `AgentControl-GitClient-CloneRemovesOnlyWhatItCreated` and `AgentControl-GitClient-CloneCancelStopsGit`.

**GitClient_Clone_NeverLogsTheAddressPassword**: With an address `https://alice:<password>@example.com/...`, a stub
whose `clone` fails and echoes the address in its error text, and a client whose git cannot be started, the password
appears in no logged message (captured through `GitClientListLogger`), no result text and no exception text (its
message and its full text), while the host `example.com` remains. This scenario is tested by
`GitClient_Clone_UrlWithPassword_IsNeverWrittenToLogOrExceptionText`, covering
`AgentControl-GitClient-CloneRedactsCredentials`.
