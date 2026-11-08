### RepoCardViewModel

#### Verification Approach

`RepoCardViewModel` is verified with unit tests defined in `RepoCardViewModelTests.cs`, for NuGet server
sources, `RepoCardServerSourceTests.cs` and, for the signature indicator, `RepoCardSignatureTests.cs`. Every
test substitutes a stub script for the git executable (via `GitStub`, mirroring
`GitClientTests`) and uses real temporary directories/zip fixtures for package-source and
pin-file checks, so these tests never depend on a real git installation, a real repository,
or a real package source. The server-source tests start the in-process `MockNuGetFeed` (Kestrel, plain HTTP on
a fresh loopback port, optionally requiring a Basic sign-in) and a `FakeCredentialPrompt`, with the NuGet HTTP
cache isolated per test process (`HttpCacheIsolation`). Apart from the picture tests, which show a card in a
headless Avalonia window (and decode small generated PNG files), no Avalonia `Window` is constructed; only the
view-model class itself is under test.

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
- The AGENTS.md template offer fires only when the repo genuinely has no AGENTS.md and the
  package genuinely has a template to offer, never re-fires after a decline is persisted, and
  never fires when the ensure-synced-before-launch check found the managed folders already
  present.
- Every package-source operation is bound to the source named in the pin, with a pin lacking a source
  read as a pin to Default; the pin records the source alias and keeps its other fields; a pin naming
  an unconfigured source gives an error state and no upgrade, and nothing is extracted from any other
  source; a pin from a newer schema stops an apply before the repo is touched.
- A NuGet folder source can be selected from, upgraded from, and applied to a repo.
- A background upgrade check never opens the sign-in dialog and explains on the card what to do; a check the
  user starts may ask once, and the credential is then remembered for the session.
- The signature shield and its tooltip appear only for a pinned NuGet source that requires signed packages and follow
  a change of that requirement.

#### Test Scenarios

**RepoCardViewModel_DisplayInfo_ExposesFolderNameAndPackageChip**: The card's display name
resolves to the repo's leaf folder name, a `Refresh` after a pin file is written populates the
pinned package name/version fields, and the package chip text and tooltip name the package, its
version and the source - with the source kind's icon for a zip folder, a NuGet folder and a NuGet server
(the server's icon differs from the other two, and the source's alias is shown as text beside it), and an
explicit "not configured" description when the pinned source does not exist. A repo with no pin
shows only the "no agent package" chip, and a missing repo shows neither package chip. This
scenario is tested by
`RepoCardViewModel_RepoName_ReturnsLeafFolderName`,
`RepoCardViewModel_Refresh_PinFileExists_PopulatesPinnedFields`,
`RepoCardViewModel_PackageChip_PinnedToZipSource_ShowsNameVersionAndSourceInTooltip`,
`RepoCardViewModel_PackageChip_PinnedToNuGetFolderSource_ShowsNuGetFolderIconAndKind`,
`RepoCardViewModel_PinnedToServerSource_ChipShowsAliasAndServerIcon`,
`RepoCardViewModel_PackageChip_PinnedSourceNotConfigured_ShowsUnknownSource`,
`RepoCardViewModel_NoPackageChip_NeverPinnedRepo_ShowsNoPackageChipOnly`, and
`RepoCardViewModel_Chips_MissingRepo_ShowNeitherPackageChip`, covering
`AgentControl-RepoCardViewModel-DisplayInfo`.

**RepoCardViewModel_UpgradeBadge_ReflectsSourceComparisonOnly**: The upgrade chip is shown
only when a newer version exists at the pin's source, and not when the repo is already on the
latest version or when no source is configured; when shown it states the target version and its
tooltip both versions. This scenario is
tested by `RepoCardViewModel_Refresh_NewerVersionAtSource_SetsUpgradeAvailableTrue`,
`RepoCardViewModel_Refresh_AlreadyOnLatestVersion_SetsUpgradeAvailableFalse`,
`RepoCardViewModel_Refresh_NoPackageSourceConfigured_SetsUpgradeAvailableFalse`, and
`RepoCardViewModel_UpgradeChip_NewerVersionAtSource_ShowsTargetVersionAndTooltipWithBothVersions`,
covering `AgentControl-RepoCardViewModel-UpgradeBadge`.

**RepoCardViewModel_LaunchLabel_NamesToolOrFallsBack**: The Launch label names the configured
agent tool when it has a short display name, falls back to "Launch" for an unknown or long
name, and follows a change of the configured tool after a cheap refresh. This scenario is tested
by `RepoCardViewModel_LaunchButtonText_ConfiguredTool_NamesToolOrFallsBack` and
`RepoCardViewModel_LaunchButtonText_AgentToolChanged_UpdatesAfterRefreshCheap`, covering
`AgentControl-RepoCardViewModel-LaunchLabel`.

**RepoCardViewModel_ResponsiveOperations_ReadAndApplyDoNotBlockTheCaller**: Applying a package against a
source that never answers returns control to the caller while the source is still being read, and an upgrade
check against a slow source does not block the caller either and shows a "checking" state. This scenario is
tested by `RepoCardViewModel_RefreshUpgradeStatusAsync_SourceSlow_DoesNotBlockAndShowsChecking` and
`RepoCardViewModel_ApplySelectedPackageAsync_SourceSlow_ReturnsToCallerBeforeSourceAnswers`, covering
`AgentControl-RepoCardViewModel-ResponsiveOperations`. Pull is routed through the same busy-operation wrapper
but is not exercised against a slow git process, so it is not claimed by this requirement.

**RepoCardViewModel_OperationBusyState_ReportsBusyAndDisablesOtherActions**: While a package apply is waiting
on a slow source the card is busy, shows the "Applying agent package..." message, and the Launch, Refresh and
Select Package commands cannot execute; when the operation ends the card is idle and the actions are available
again. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackageAsync_Running_ReportsBusyAndDisablesOtherActions`, covering
`AgentControl-RepoCardViewModel-OperationBusyState`.

**RepoCardViewModel_OneOperationAtATime_SecondRequestIsIgnored**: A second apply requested while one is
running returns without reading the source again. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackageAsync_SecondCallWhileRunning_IsIgnored`, covering
`AgentControl-RepoCardViewModel-OneOperationAtATime`.

**RepoCardViewModel_CancelOperation_NoErrorAndPinUnchanged**: Cancelling a running package apply ends it with
the "canceled" status, raises no `ErrorOccurred` and leaves the repo unpinned, and cancelling an upgrade check
throws the cancellation and keeps the previous upgrade state. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackageAsync_CancelledWhileRunning_ReportsNoErrorAndKeepsPinUnchanged` and
`RepoCardViewModel_RefreshUpgradeStatusAsync_Cancelled_ThrowsAndKeepsPreviousState`, covering
`AgentControl-RepoCardViewModel-CancelOperation`. The combined
`RepoCardViewModel_ApplySelectedPackageAsync_Running_IsBusyIgnoresSecondCallAndCancelLeavesPinUnchanged`
test asserts the busy, repeat and cancel outcomes together and remains as supporting evidence for the three
requirements above.

**RepoCardViewModel_SupersededUpgradeCheck_OlderResultIsDiscarded**: When a newer upgrade check starts while an
older one is still waiting, the older check's late answer does not overwrite the newer result. This scenario is
tested by `RepoCardViewModel_RefreshUpgradeStatusAsync_NewerCheckStarted_OlderResultIsDiscarded`, covering
`AgentControl-RepoCardViewModel-SupersededUpgradeCheck`.

**RepoCardViewModel_PullGating_CleanTreeEnablesDirtyOrFailureDisables**: A clean working tree
sets `CanPull` true, a dirty working tree sets it false, and a git status failure also sets it
false without throwing. This scenario is tested by
`RepoCardViewModel_Refresh_CleanWorkingTree_SetsCanPullTrue`,
`RepoCardViewModel_Refresh_DirtyWorkingTree_SetsCanPullFalse`, and
`RepoCardViewModel_Refresh_GitStatusFails_SetsCanPullFalseWithoutThrowing`, covering
`AgentControl-RepoCardViewModel-PullGating`.

**RepoCardViewModel_PullDisabledExplanation_DirtyAndMissingExposeReasonNotJustHiding**:
`IsWorkingTreeDirty` is false for a clean tree, a missing repo, a git-status failure, and a
card whose git status has not yet been checked, and true only for a confirmed dirty tree;
`PullTooltip` explains the specific reason (missing-folder, not-yet-checked,
status-undeterminable, or uncommitted-changes text) rather than Pull simply disappearing with
no explanation, and neither a status-check failure nor a not-yet-checked card is misreported
as a confirmed dirty working tree. A repo that temporarily disappears and then reappears gets
its cached Git status invalidated while missing, correctly reporting "not yet checked" rather
than the stale pre-disappearance result until an explicit fresh check (`RefreshDirtyStatus`)
actually runs. This scenario is tested by
`RepoCardViewModel_BeforeFirstGitStatusCheck_DoesNotReportDirty`,
`RepoCardViewModel_Refresh_CleanWorkingTree_SetsCanPullTrue`,
`RepoCardViewModel_Refresh_DirtyWorkingTree_SetsCanPullFalse`,
`RepoCardViewModel_Refresh_GitStatusFails_SetsCanPullFalseWithoutThrowing`,
`RepoCardViewModel_RefreshCheap_RepoPathDoesNotExist_SetsIsMissingAndSuppressesOtherState`, and
`RepoCardViewModel_RepoReappearsAfterBeingMissing_InvalidatesStaleGitStatusInsteadOfReportingIt`,
covering `AgentControl-RepoCardViewModel-PullDisabledExplanation`.

**RepoCardViewModel_Pull_ReportsSuccessOrFailureViaStatusMessage**: `PullCommand` is executed
against a stub reporting success (status message updated) and a stub reporting failure (a
failure status message is set instead). This scenario is tested by
`RepoCardViewModel_PullCommand_SuccessfulPull_UpdatesStatusMessage` and
`RepoCardViewModel_PullCommand_FailedPull_SetsFailureStatusMessage`, covering
`AgentControl-RepoCardViewModel-Pull`.

**RepoCardViewModel_Launch_RecordsTimestampOrRaisesErrorWhenUnconfigured**: `LaunchCommand`
succeeds and records the launch timestamp (raising `LaunchRecorded`) when a command is
configured, raises `ErrorOccurred` instead when no agent-tool command is configured, and still
launches when the user cancels the wait for the pre-launch sync. This
scenario is tested by
`RepoCardViewModel_LaunchCommand_Succeeds_RecordsLaunchTimestampAndRaisesLaunchRecorded`,
`RepoCardViewModel_LaunchCommand_NoCommandConfigured_RaisesErrorOccurred`, and
`RepoCardViewModel_LaunchCommand_PreLaunchSyncCancelledByUser_StillLaunches`, covering
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
`RepoCardViewModel_SelectPackageCommand_SourceConfigured_RaisesSelectPackageRequestedWithSourceAlias`,
covering `AgentControl-RepoCardViewModel-SelectPackage`.

**RepoCardViewModel_ApplySelectedPackage_RejectsStaleVersionWithoutMutatingPin**: Applying a
valid chosen name/version updates the pin, extracts files, and raises `ReleaseNotesReady`; a
version no longer available at the source raises `ErrorOccurred` without mutating the existing
pin; and a corrupt/unreadable existing pin file does not abort the apply - the extraction and
new pin write still complete, defaulting the carried-over `AgentsMdTemplateDeclined` value to
`false`. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackage_ValidNameAndVersion_UpdatesPinAndExtractsAndRaisesReleaseNotesReady`,
`RepoCardViewModel_ApplySelectedPackage_VersionNoLongerAtSource_RaisesErrorOccurredWithoutMutatingPin`,
and
`RepoCardViewModel_ApplySelectedPackage_CorruptExistingPinFile_StillAppliesAndWritesNewPin`,
covering `AgentControl-RepoCardViewModel-ApplySelectedPackage`.

**RepoCardViewModel_AgentsMdTemplateOffer_FiresOnlyWhenAbsentAndTemplateAvailableAndNotDeclined**:
After a successful extraction during package selection, upgrade, or the
ensure-synced-before-launch re-extraction, the offer fires with the template's content when
the repo has no `AGENTS.md` and the package has a template; it does not fire when the repo
already has an `AGENTS.md` (which is left untouched), when the package has no template, when
a prior decline is on record for the repo, or when the ensure-synced-before-launch check found
all four managed folders already present (so no re-extraction, and no zip read, occurred at
all). This scenario is tested by
`RepoCardViewModel_ApplySelectedPackage_NoAgentsMdAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequestedWithContent`,
`RepoCardViewModel_ApplySelectedPackage_AgentsMdAlreadyExists_DoesNotRaiseAgentsMdTemplateOfferRequested`,
`RepoCardViewModel_ApplySelectedPackage_PackageHasNoAgentsMdTemplate_DoesNotRaiseAgentsMdTemplateOfferRequested`,
`RepoCardViewModel_ApplySelectedPackage_PreviouslyDeclined_DoesNotRaiseAgentsMdTemplateOfferRequestedAgain`,
`RepoCardViewModel_UpgradeCommand_NoAgentsMdAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequestedWithContent`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_ReExtractsAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequested`,
and
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_FoldersAlreadyPresent_NeverReadsZipOrRaisesOffer`,
covering `AgentControl-RepoCardViewModel-AgentsMdTemplateOffer`.

**RepoCardViewModel_AcceptAgentsMdTemplate_WritesTemplateVerbatimToRepoRoot**: Accepting the
offer writes the given template content verbatim to `AGENTS.md` at the repo root (never into
a managed folder), using a create-new (never-overwrite) write; if `AGENTS.md` was created by
another user/process in the gap between the offer being raised and being accepted, the
pre-existing file's content is preserved and the outcome is reported via `StatusMessage` as a
non-destructive, informational result rather than a generic error. This scenario is tested by
`RepoCardViewModel_AcceptAgentsMdTemplate_WritesFileToRepoRootWithGivenContent`,
`RepoCardViewModel_AcceptAgentsMdTemplate_WriteFails_RaisesErrorOccurred`, and
`RepoCardViewModel_AcceptAgentsMdTemplate_FileCreatedConcurrently_PreservesExistingContentAndReportsNonDestructiveOutcome`,
covering `AgentControl-RepoCardViewModel-AcceptAgentsMdTemplate`.

**RepoCardViewModel_DeclineAgentsMdTemplate_PersistsDeclineWithoutWritingFileAndDegradesOnFailure**:
Declining the offer persists the decline in the repo's pin file without writing an
`AGENTS.md` file, a subsequent sync for the same repo does not re-raise the offer, and a
pin-write failure raises `ErrorOccurred` rather than throwing. This scenario is tested by
`RepoCardViewModel_DeclineAgentsMdTemplate_PersistsDeclinedFlagInPinFile`,
`RepoCardViewModel_DeclineAgentsMdTemplate_SubsequentApplySelectedPackage_DoesNotReprompt`, and
`RepoCardViewModel_DeclineAgentsMdTemplate_PinWriteFails_RaisesErrorOccurred`, covering
`AgentControl-RepoCardViewModel-DeclineAgentsMdTemplate`.

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

**RepoCardViewModel_SourceAwareness_BindsEveryOperationToThePinsSource**: Applying a package from a
legacy single path records source `Default` in a current-schema pin; selecting from the second of
two sources offering the same package and version records that alias and extracts that source's
content; a pin to a source that is not configured sets the card's source problem (naming the
alias), offers no upgrade and no latest version even though another source offers a higher
version; applying a package for an unconfigured alias raises an error naming it and leaves the
repo and pin unchanged; the ensure-synced repair for such a pin fails without extracting; with the
same package at two sources a pin to the lower-version source is compared only against that source;
a legacy pin file with no source resolves against `Default` and, when no `Default` source exists,
shows a source problem; applying over a pin with an unknown field and a recorded decline keeps both;
applying over a pin from a newer schema raises an error and leaves the pin and repo untouched.
This scenario is tested by
`RepoCardViewModel_ApplySelectedPackage_LegacySinglePath_PinRecordsDefaultSourceAlias`,
`RepoCardViewModel_ApplySelectedPackage_SecondSourceSelected_PinRecordsThatSourceAndItsContent`,
`RepoCardViewModel_Refresh_PinNamesUnconfiguredSource_ShowsSourceProblemAndOffersNoUpgrade`,
`RepoCardViewModel_ApplySelectedPackage_UnconfiguredSourceAlias_RaisesErrorAndChangesNothing`,
`RepoCardViewModel_EnsureAgentFilesSynced_PinNamesUnconfiguredSource_ReturnsFalseWithoutExtracting`,
`RepoCardViewModel_Refresh_SamePackageNameInTwoSources_ComparesOnlyAgainstPinnedSource`,
`RepoCardViewModel_Refresh_LegacyV1PinFile_ResolvesAgainstDefaultSource`,
`RepoCardViewModel_Refresh_LegacyV1PinFileAndNoDefaultSource_ShowsSourceProblem`,
`RepoCardViewModel_ApplySelectedPackage_PinWithUnknownFieldAndDecline_PreservesBoth`, and
`RepoCardViewModel_ApplySelectedPackage_NewerSchemaPinFile_RaisesErrorAndChangesNothing`; and, for
a NuGet folder source (a real folder feed, with the NuGet cache isolated through the startup
configuration-directory override), by
`RepoCardViewModel_ApplySelectedPackage_NuGetFolderSource_ExtractsPinsAliasAndRaisesReleaseNotes`
and `RepoCardViewModel_UpgradeCommand_NuGetFolderSource_UpgradesToHighestVersion`, covering
`AgentControl-RepoCardViewModel-SourceAwareness`.

**RepoCardViewModel_BackgroundChecksNeverPrompt_UserStartedChecksMay**: Against an in-process mock NuGet
server (see the `NuGetSourceProvider` verification; plain HTTP with the source's explicit opt-in), an
anonymous server answers the upgrade check with the newer version; a background check (the default,
`allowPrompt` false) against a server that requires a sign-in never calls the scripted prompt and sets the
card's source problem to say a sign-in is needed and to use the card's menu > Refresh; a check the user
requested (`allowPrompt` true) asks once, finds the upgrade, and a later background check - even by another
card - reuses the remembered credential without asking. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatusAsync_AnonymousServer_OffersNewerVersion`,
`RepoCardViewModel_RefreshUpgradeStatusAsync_BackgroundCheckNeedsSignIn_DoesNotPromptAndExplains` and
`RepoCardViewModel_RefreshUpgradeStatusAsync_UserRequestedCheck_PromptsOnceThenBackgroundChecksReuseCredential`,
in `RepoCardServerSourceTests.cs`, covering `AgentControl-RepoCardViewModel-BackgroundChecksNeverPrompt`.

**RepoCardViewModel_SignatureIndicator_ShieldOnlyForNuGetSourcesThatRequireSignatures**: A card pinned to a NuGet
source that requires signed packages reports `RequiresSignature` and a tooltip that says signatures are required and
either that any trusted signer is accepted or - for a source that pins a publisher - the start and end of the
fingerprint only; a source without the requirement, a zip source and a pinned source that is not configured report
no shield; when the source's requirement changes and the card refreshes, the card raises change notifications for
the shield and its tooltip. This scenario is tested by
`RepoCardViewModel_PinnedSourceRequiresSignatures_ShowsShieldWithTooltip`,
`RepoCardViewModel_NoSignatureRequirement_ShowsNoShield` and
`RepoCardViewModel_SourceRequirementChanges_RaisesChangeNotificationsForTheShield`, in
`RepoCardSignatureTests.cs`, covering `AgentControl-RepoCardViewModel-SignatureIndicator`.

**RepoCardViewModel_StatusAccent_StatusFollowsStateAndHasWordsAndReason**: Cards that are pinned and current,
pinned with a newer version available, and not pinned report `UpToDate` ("Up to date"), `UpgradeAvailable`
("Upgrade available", the tooltip naming both versions) and `NoPackage` ("No agent package"); a missing folder
and a pin naming an unconfigured source report `Problem` ("Needs attention") with the reason in the tooltip; a
git pull that git refuses, or an error the card reports to the user, makes the status a problem, and the next
operation (a refresh) clears it again. This scenario is tested by
`RepoCardViewModel_Status_ReflectsPackageAndUpgradeState`,
`RepoCardViewModel_Status_MissingFolderOrSourceProblem_IsProblemWithReason`,
`RepoCardViewModel_Status_FailedPull_IsProblemUntilNextOperation` and
`RepoCardViewModel_Status_ErrorReported_IsProblem`, covering `AgentControl-RepoCardViewModel-StatusAccent`.

**RepoCardViewModel_UpgradeFromChip_OneGuardedCommandForChipAndMenu**: The upgrade chip shows the target version
with a tooltip that says it upgrades the agent files to it; the chip and the menu's Upgrade item are bound to the
same command, which can run while an upgrade is available and is disabled while the card is busy with another
operation, and which performs the upgrade (pin, release notes) when run. The click on the real chip is also
exercised end to end by `windows@UpgradeChip_Click_SyncsManagedFoldersAndShowsReleaseNotes`. This scenario is
tested by `RepoCardViewModel_UpgradeChip_NewerVersionAtSource_ShowsTargetVersionAndTooltipWithBothVersions`,
`RepoCardViewModel_UpgradeCommand_ChipAndMenuShareOneGuardedCommand` and
`RepoCardViewModel_UpgradeCommand_NewerVersionAvailable_UpdatesPinAndRaisesReleaseNotesReady`, covering
`AgentControl-RepoCardViewModel-UpgradeFromChip`.

**RepoCardViewModel_AvatarTile_InitialsStableColorAndFolderFallback**: Repo names give the capital letters of
their first two words (or one letter, or none for a name without letters or digits, which falls back to the
folder glyph); the color index is a fixed value for a fixed path (pinning the hash function), is the same
whatever the case of the path or a trailing separator, always lies in the palette and uses several colors across
many paths. This scenario is tested by `RepoCardViewModel_AvatarInitials_RepoName_UsesFirstLettersOfFirstTwoWords`,
`RepoCardViewModel_Avatar_EmptyName_FallsBackToFolderGlyph` and
`RepoCardViewModel_AvatarColorIndex_SamePath_IsStableAndInPalette`, covering
`AgentControl-RepoCardViewModel-AvatarTile`.

**RepoCardViewModel_RepoPicture_OnlySafeRootPngIsShownAndLoadedOnce**: Small PNG files generated in the tests
(`TestPng`, no binary assets) show that `icon.png` is preferred to `logo.png`, names are matched in any letter
case, a file without the PNG signature (a renamed text or JPEG file), an empty file, a file over the 1 MB cap
(while one exactly at the cap is accepted) and a file declaring absurd dimensions are refused (falling through
to `logo.png` where there is one), pictures in folders below the root and a folder named `icon.png` are ignored, a symbolic
link is refused (the test is skipped on an account that may not create one, like the repo's other link tests), a
missing folder has no picture, and the found file's identity changes when the file changes. Decoding (headless
Avalonia) scales a large logo down to the tile width keeping its aspect ratio and returns no bitmap, without
throwing, for a corrupt, vanished or replaced file. The view model loads a found picture in the background,
does not decode an unchanged file again on a second refresh but does after the file changes, falls back to the
initials when the file is removed, never looks for a picture for a missing folder, and keeps the tile - without
retrying - when the decoder fails or throws. A headless main window shows the picture (image visible, initials
hidden, light backing) for the repo that has one and the initials for the one that does not. This scenario is
tested by the `RepoImageLocator_Find_*` and `RepoImageLocator_Decode_*` tests of `RepoImageLocatorTests.cs` and the
`RepoCardViewModel_RepoImage_*` and `MainWindow_RepoCard_AvatarShowsRepoPictureWhenPresent` tests of
`RepoCardImageTests.cs`, covering `AgentControl-RepoCardViewModel-RepoPicture`.
