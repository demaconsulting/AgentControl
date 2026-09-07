### RepoCardViewModel

#### Verification Approach

`RepoCardViewModel` is verified with unit tests defined in `RepoCardViewModelTests.cs`. Every
test substitutes a stub script for the git executable (via `GitStub`, mirroring
`GitClientTests`) and uses real temporary directories/zip fixtures for package-source and
pin-file checks, so these tests never depend on a real git installation, a real repository,
or a real package source. No Avalonia `Window` is ever constructed; only the view-model class
itself is under test.

#### Test Environment

N/A - standard test environment; `RealProcess` collection applies (git-stub subprocess
invocations are not parallelized with other tests using real process launches).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Display fields, badges, and gating conditions reflect the correct underlying pin/git/source
  state for every tested input.
- Launch is never blocked by the outcome of the best-effort ensure-synced check.
- Upgrade and select-package flows never mutate the pin when their preconditions are not met.
- Remove requests never mutate state on their own.

#### Test Scenarios

**RepoCardViewModel_DisplayInfo_ExposesFolderNameAndPin**: The card's display name resolves to
the repo's leaf folder name, and a `Refresh` after a pin file is written populates the pinned
package name/version fields. This scenario is tested by
`RepoCardViewModel_RepoName_ReturnsLeafFolderName` and
`RepoCardViewModel_Refresh_PinFileExists_PopulatesPinnedFields`, covering
`AgentControl-RepoCardViewModel-DisplayInfo`.

**RepoCardViewModel_UpgradeBadge_ReflectsSourceComparisonOnly**: The upgrade-available badge
is set true only when a newer version exists at a configured source, and false both when the
repo is already on the latest version and when no source is configured. This scenario is
tested by `RepoCardViewModel_Refresh_NewerVersionAtSource_SetsUpgradeAvailableTrue`,
`RepoCardViewModel_Refresh_AlreadyOnLatestVersion_SetsUpgradeAvailableFalse`, and
`RepoCardViewModel_Refresh_NoPackageSourceConfigured_SetsUpgradeAvailableFalse`, covering
`AgentControl-RepoCardViewModel-UpgradeBadge`.

**RepoCardViewModel_PullGating_CleanTreeEnablesDirtyOrFailureDisables**: A clean working tree
sets `CanPull` true, a dirty working tree sets it false, and a git status failure also sets it
false without throwing. This scenario is tested by
`RepoCardViewModel_Refresh_CleanWorkingTree_SetsCanPullTrue`,
`RepoCardViewModel_Refresh_DirtyWorkingTree_SetsCanPullFalse`, and
`RepoCardViewModel_Refresh_GitStatusFails_SetsCanPullFalseWithoutThrowing`, covering
`AgentControl-RepoCardViewModel-PullGating`.

**RepoCardViewModel_Pull_ReportsSuccessOrFailureViaStatusMessage**: `PullCommand` is executed
against a stub reporting success (status message updated) and a stub reporting failure (a
failure status message is set instead). This scenario is tested by
`RepoCardViewModel_PullCommand_SuccessfulPull_UpdatesStatusMessage` and
`RepoCardViewModel_PullCommand_FailedPull_SetsFailureStatusMessage`, covering
`AgentControl-RepoCardViewModel-Pull`.

**RepoCardViewModel_Launch_RecordsTimestampOrRaisesErrorWhenUnconfigured**: `LaunchCommand`
succeeds and records the launch timestamp (raising `LaunchRecorded`) when a command is
configured, and raises `ErrorOccurred` instead when no agent-tool command is configured. This
scenario is tested by
`RepoCardViewModel_LaunchCommand_Succeeds_RecordsLaunchTimestampAndRaisesLaunchRecorded` and
`RepoCardViewModel_LaunchCommand_NoCommandConfigured_RaisesErrorOccurred`, covering
`AgentControl-RepoCardViewModel-Launch`.

**RepoCardViewModel_EnsureSyncedBeforeLaunch_NeverBlocksLaunchOnBestEffortSyncOutcome**: The
ensure-synced check is a best-effort, non-blocking side action: it returns true with an
informational status message (no `ErrorOccurred`) when no pin exists, skips touching the
managed folders entirely and returns true with an informational status message when the repo
has committed agent files (even when a pin exists and folders are missing), returns true
without re-extracting when folders are already present, re-extracts only the pinned version
when folders are missing, and returns false (a non-blocking warning) when the pinned version is
no longer available at the source. A separate `Launch()`-level scenario confirms the agent-tool
process is still spawned regardless of whether the sync attempt found no pin or failed
outright. This scenario is tested by
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoPin_ReturnsTrueWithInformationalStatusMessage`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_HasCommittedAgentFiles_SkipsSyncEntirelyAndReturnsTrue`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedAndFoldersPresent_ReturnsTrueWithoutReExtracting`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedButFoldersMissing_ReExtractsPinnedVersionOnly`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedVersionMissingFromSource_ReturnsFalse`,
and `RepoCardViewModel_LaunchCommand_SyncFailsOrNoPin_StillLaunches`, covering
`AgentControl-RepoCardViewModel-EnsureSyncedBeforeLaunch`.

**RepoCardViewModel_Upgrade_UpdatesPinOnlyWhenNewerVersionExists**: `UpgradeCommand` updates
the pin and raises `ReleaseNotesReady` when a newer version is available, and raises
`ErrorOccurred` instead when no package source is configured. This scenario is tested by
`RepoCardViewModel_UpgradeCommand_NewerVersionAvailable_UpdatesPinAndRaisesReleaseNotesReady`
and `RepoCardViewModel_UpgradeCommand_NoPackageSourceConfigured_RaisesErrorOccurred`, covering
`AgentControl-RepoCardViewModel-Upgrade`.

**RepoCardViewModel_SelectPackage_OfferedOnlyForUnpinnedPresentRepoWithSource**:
`IsPackageSelectionNeeded` reflects pin state; `SelectPackageCommand` cannot execute when a
pin already exists or the repo is missing, raises only an error when no source is configured,
and raises `SelectPackageRequested` with the source path when a source is configured. This
scenario is tested by `RepoCardViewModel_IsPackageSelectionNeeded_ReflectsPinState`,
`RepoCardViewModel_SelectPackageCommand_PinAlreadyExists_CannotExecute`,
`RepoCardViewModel_SelectPackageCommand_RepoIsMissing_CannotExecute`,
`RepoCardViewModel_SelectPackageCommand_NoPackageSourceConfigured_RaisesErrorOccurredOnly`, and
`RepoCardViewModel_SelectPackageCommand_SourceConfigured_RaisesSelectPackageRequestedWithSourcePath`,
covering `AgentControl-RepoCardViewModel-SelectPackage`.

**RepoCardViewModel_ApplySelectedPackage_RejectsStaleVersionWithoutMutatingPin**: Applying a
valid chosen name/version updates the pin, extracts files, and raises `ReleaseNotesReady`; a
version no longer available at the source raises `ErrorOccurred` without mutating the existing
pin. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackage_ValidNameAndVersion_UpdatesPinAndExtractsAndRaisesReleaseNotesReady`
and
`RepoCardViewModel_ApplySelectedPackage_VersionNoLongerAtSource_RaisesErrorOccurredWithoutMutatingPin`,
covering `AgentControl-RepoCardViewModel-ApplySelectedPackage`.

**RepoCardViewModel_MissingRepo_SuppressesOtherStatusWhenFolderAbsent**: A lightweight refresh
against a repo path that no longer exists sets `IsMissing` and suppresses other status
computation. This scenario is tested by
`RepoCardViewModel_RefreshCheap_RepoPathDoesNotExist_SetsIsMissingAndSuppressesOtherState`,
covering `AgentControl-RepoCardViewModel-MissingRepo`.

**RepoCardViewModel_CommittedFilesBadge_CachedByHeadHashAndResolvesBranchCheaply**: A
lightweight refresh reuses the cached committed-files badge when the HEAD hash is unchanged,
resolves the current branch, and does not invoke the working-tree dirty-check subprocess. This
scenario is tested by
`RepoCardViewModel_RefreshCheap_UnchangedHeadHash_CommittedFilesBadgeIsCached`,
`RepoCardViewModel_RefreshCheap_ResolvesCurrentBranch`, and
`RepoCardViewModel_RefreshCheap_DoesNotRunWorkingTreeDirtyCheck`, covering
`AgentControl-RepoCardViewModel-CommittedFilesBadge`.

**RepoCardViewModel_Favorite_ToggleCommandFlipsStateAndRaisesEvent**: `FavoriteToggleCommand`
toggles `IsFavorite` and raises `FavoriteChanged`. This scenario is tested by
`RepoCardViewModel_FavoriteToggleCommand_TogglesIsFavoriteAndRaisesFavoriteChanged`, covering
`AgentControl-RepoCardViewModel-Favorite`.

**RepoCardViewModel_RemoveRequest_RaisesEventWithoutMutatingState**: `RemoveCommand` raises
`RemoveRequested` without changing any of the card's own state, deferring the removal decision
to its owner. This scenario is tested by
`RepoCardViewModel_RemoveCommand_RaisesRemoveRequestedWithoutMutatingState`, covering
`AgentControl-RepoCardViewModel-RemoveRequest`.
