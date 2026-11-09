### RepoCardViewModel

#### Verification Approach

`RepoCardViewModel` is verified with unit tests defined in `RepoCardViewModelTests.cs`, for NuGet server
sources, `RepoCardServerSourceTests.cs`, for the signature indicator, `RepoCardSignatureTests.cs`, for the
card color and group chip, `RepoCardColorTests.cs` (and, for moving into groups, `RepoGroupingTests.cs`), for the
Git source (the Git icon and tooltip, the moved tag, Change package and the git version problem)
`RepoCardGitAwarenessTests.cs`, for the Git source's registry options `RepoCardGitSourceTests.cs` and, for the
repo picture, `RepoCardImageTests.cs`. Every
test substitutes a stub script for the git executable (via `GitStub`, mirroring
`GitClientTests`) and uses real temporary directories/zip fixtures for package-source and
pin-file checks, so these tests never depend on a real git installation, a real repository,
or a real package source. The tests that must control exactly when a package source answers (stale and retired
upgrade checks, the busy guard) give the card an in-memory source whose answer a test releases, or that signals when it
is read and then never answers, so no timing assumption is needed. One test gives the card an owner callback that
accepts the first change and refuses the rest; the other tests build cards without an owner. The server-source
tests start the
in-process `MockNuGetFeed` (Kestrel, plain HTTP on
a fresh loopback port, optionally requiring a Basic sign-in) and a `FakeCredentialPrompt`, with the NuGet HTTP
cache isolated per test process (`HttpCacheIsolation`). Apart from the picture tests (`RepoCardImageTests.cs`),
which show a card in a headless Avalonia window (using the `RepoImageLocator` unit with small generated PNG files
or an injected decoder, and checking that replaced, cleared and overtaken bitmaps are disposed), no Avalonia `Window`
is constructed; only the view-model class itself is under test. The
`RepoImageLocator` unit has its own verification document.

#### Test Environment

N/A - standard test environment; `RealProcess` collection applies (git-stub subprocess
invocations are not parallelized with other tests using real process launches).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Display fields, badges, and gating conditions reflect the correct underlying pin/git/source
  state for every tested input.
- Launch is never blocked by the outcome of the best-effort ensure-synced check.
- The committed-agent-files result is read from git once for an unchanged HEAD hash (proved by the stub's invocation
  log), and a second operation requested while one is really running is refused by the busy guard and accepted again
  after the first ended.
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
- The pin records the content identity of the package that was applied; an Upgrade, or the restore of missing agent
  files before launch, that finds the pinned version delivering content that differs from the recorded identity applies
  nothing and leaves the pin unchanged (and says that Change package... accepts the change), Change package... or
  Select Package of that version asks first and applies it, recording the new
  identity, only after the user confirmed, a pin or a source without an identity is never treated as changed, and the
  launch-time restore never writes the pin.
- A card pinned to a Git source shows the Git icon and the alias, names the tag and the first seven characters of the
  commit in its chip tooltip, shows "Tag moved" - and applies nothing - when the tag now points to a different commit
  than the one recorded, never reports a move for a pin without a recorded commit or a source that is not Git, and
  says when the installed git is too old without suggesting a sign-in; a pinned repo can execute Select Package as
  Change package.
- A NuGet folder source can be selected from, upgraded from, and applied to a repo.
- A background upgrade check never opens the sign-in dialog and explains on the card what to do; a check the
  user starts may ask once, and the credential is then remembered for the session.
- The signature shield and its tooltip appear only for a pinned NuGet source that requires signed packages and follow
  a change of that requirement.
- A repo moves into, between and out of groups under the group-name rules, an unknown stored color reads as
  the automatic color, and a chosen color never changes the card's status.
- A change to the favorite status, group, card color or launch time is applied only after the owner saved it, and a
  refusal leaves the card as it was.
- An upgrade check that was overtaken by a changed pin or a missing repo publishes nothing and leaves no "checking"
  or "check failed" behind; an invalid source configuration is a problem, never "up to date".
- A retired card cancels what it runs, reports nothing and checks nothing; every picture a card replaces, clears,
  never shows or drops when retired is disposed, and the picture it shows is intact.

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

**RepoCardViewModel_OneOperationAtATime_SecondRequestIsIgnored**: A package source signals when it is being read
and then never answers. A second apply requested while the first is really reading the source returns at once,
does not read the source again (the source's call count stays at one) and leaves the first operation running and
the card busy; once the first has been cancelled and has ended, a new apply is accepted (the source is read a
second time), which proves it was the busy guard, not some other cause, that refused the second request. This
scenario is tested by `RepoCardViewModel_ApplySelectedPackageAsync_SecondCallWhileRunning_IsIgnored`, covering
`AgentControl-RepoCardViewModel-OneOperationAtATime`.

**RepoCardViewModel_CancelOperation_NoErrorAndPinUnchanged**: Canceling a running package apply ends it with
the "canceled" status, raises no `ErrorOccurred` and leaves the repo unpinned, and canceling an upgrade check
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

**RepoCardViewModel_SelectPackage_OfferedForUnpinnedPresentRepoWithSource**:
`IsPackageSelectionNeeded` reflects pin state; `SelectPackageCommand` cannot execute when
the repo is missing, raises only an error when no source is configured,
and raises `SelectPackageRequested` with the source path when a source is configured. This
scenario is tested by `RepoCardViewModel_IsPackageSelectionNeeded_ReflectsPinState`,
`RepoCardViewModel_SelectPackageCommand_RepoIsMissing_CannotExecute`,
`RepoCardViewModel_SelectPackageCommand_NoPackageSourceConfigured_RaisesErrorOccurredOnly`, and
`RepoCardViewModel_SelectPackageCommand_SourceConfigured_RaisesSelectPackageRequestedWithSourceAlias`,
covering `AgentControl-RepoCardViewModel-SelectPackage`.

**RepoCardViewModel_ChangePackage_OfferedForPinnedRepo**: The same command is "Change package..." for a repo that has
a pin: it can execute although a pin exists (renamed from `..._PinAlreadyExists_CannotExecute`, which asserted the
opposite before the card offered Change package), the card reports a pinned package and counts the action among those
of the menu's agent files group (`HasAgentFileActions`), and executing it raises `SelectPackageRequested` once while the
repo stays pinned. This scenario is tested by
`RepoCardViewModel_SelectPackageCommand_PinAlreadyExists_CanExecuteAsChangePackage` and
`RepoCardViewModel_SelectPackageCommand_PinnedRepo_RaisesSelectPackageRequested`, covering
`AgentControl-RepoCardViewModel-ChangePackage` (offered, counted among the agent files actions) and
`AgentControl-RepoCardViewModel-ChangePackageRequest` (the request, the repo still pinned).

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

**RepoCardViewModel_AgentsMdOfferBeforeReleaseNotes_OfferIsRaisedFirst**: When both the offer and the release
notes apply to an apply of a package, the offer is raised first and the release notes after it. This scenario is
tested by
`RepoCardViewModel_ApplySelectedPackage_TemplateOfferAndReleaseNotes_RaisesOfferBeforeReleaseNotes`, covering
`AgentControl-RepoCardViewModel-AgentsMdOfferBeforeReleaseNotes`.

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
`RepoCardViewModel_RefreshCheap_RepoPathDoesNotExist_SetsIsMissingAndSuppressesOtherState`, covering
`AgentControl-RepoCardViewModel-MissingRepo`.

**RepoCardViewModel_MissingRepoRecheck_RefreshStaysAvailableAsCheckAgain**: For a missing repo `RefreshCommand`
stays executable (Launch does not) and is offered as "Check again" with the tooltip "Check whether the folder is
available again". This scenario is tested by
`RepoCardViewModel_RefreshCheap_RepoPathDoesNotExist_SetsIsMissingAndSuppressesOtherState` and
`RepoCardViewModel_RefreshCommand_MissingFolderReturns_ClearsMissingAndRestoresActions`, covering
`AgentControl-RepoCardViewModel-MissingRepoRecheck`.

**RepoCardViewModel_MissingRepoCleared_FolderComesBack**: Creating the folder and executing the refresh command
clears `IsMissing`, restores the label "Refresh" and its tooltip and re-enables Launch. This scenario is tested by
`RepoCardViewModel_RefreshCommand_MissingFolderReturns_ClearsMissingAndRestoresActions`, covering
`AgentControl-RepoCardViewModel-MissingRepoCleared`.

**RepoCardViewModel_CommittedFilesBadge_CachedByHeadHashAndResolvesBranchCheaply**: Against a git stub that logs
every invocation and answers a constant HEAD hash, two lightweight refreshes both show the committed-files badge,
and the stub's invocation log proves the cache: the file listing (`ls-files`) ran exactly once, while the HEAD hash
was asked for on each refresh (the cache is keyed by it); a refresh also resolves the current branch and does not
invoke the working-tree dirty-check subprocess. This
scenario is tested by
`RepoCardViewModel_RefreshCheap_UnchangedHeadHash_CommittedFilesBadgeIsCached`,
`RepoCardViewModel_RefreshCheap_ResolvesCurrentBranch`, and
`RepoCardViewModel_RefreshCheap_DoesNotRunWorkingTreeDirtyCheck`, covering
`AgentControl-RepoCardViewModel-CommittedFilesBadge`.

**RepoCardViewModel_Favorite_SettingTheStateRaisesTheEventOncePerChange**: Setting `IsFavorite` on, repeating
that, and setting it off updates the repo's settings entry each time it changes and raises `FavoriteChanged`
exactly once per change (nothing for a set to the value it already has). This scenario is tested by
`RepoCardViewModel_IsFavorite_Set_UpdatesRecentRepoAndRaisesFavoriteChangedOncePerChange`, covering
`AgentControl-RepoCardViewModel-Favorite`. That the card has a single owner of the state (no favorite command that
could toggle a second time) is a design property.

**RepoCardViewModel_ChangeSavedBeforeApplied_OwnerDecidesAndRefusalChangesNothing**: A card created with an owner
callback that accepts the first change and refuses the rest changes through it: the first change (a favorite) is
applied, and the following ones - a move to a group (returns `false`), a card color (returns `false`) and clearing
the favorite - leave the settings entry as it was; the owner was asked for every one of the four changes. How
the owning `MainWindowViewModel` saves, and what the user is told, is verified by that unit. This scenario is tested
by `RepoCardViewModel_Changes_GoThroughTheOwnerAndAreNotAppliedWhenItRefuses`, covering
`AgentControl-RepoCardViewModel-ChangeSavedBeforeApplied`.

**RepoCardViewModel_UpToDateCue_OnlyForPinnedAndUpToDate**: `ShowUpToDateCue` is true for a pinned
package that is the newest one, and false for an older pin with an upgrade available, a repo with no
package, a missing folder, and a pin whose source is not configured. This scenario is tested by
`RepoCardViewModel_ShowUpToDateCue_OnlyForPinnedAndUpToDate`, covering
`AgentControl-RepoCardViewModel-UpToDateCue`.

**RepoCardViewModel_RepoPathKeepsFolderName_PathIsSplitAtTheLastSeparator**: `RepoPathParent` and
`RepoPathLeaf` split the path at its last separator (a trailing separator ignored; a path with no
separator is all leaf), so the card can shorten the parent and keep the repo's folder name. This scenario is tested
by `RepoCardViewModel_RepoPathParts_SplitAtLastSeparator`, covering
`AgentControl-RepoCardViewModel-RepoPathKeepsFolderName`.

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

**RepoCardViewModel_StatusAccent_StatusFollowsState**: Cards that are pinned and current, pinned with a newer
version available, and not pinned report `UpToDate`, `UpgradeAvailable` and `NoPackage`; a missing folder and a pin
naming an unconfigured source report `Problem`; a git pull that git refuses, or an error the card reports to the
user, makes the status a problem. A pinned card whose package source cannot be read reports `Unknown` (with no
up-to-date cue) and returns to `UpToDate` once a later check succeeds. This scenario is tested by
`RepoCardViewModel_Status_ReflectsPackageAndUpgradeState`,
`RepoCardViewModel_Status_UpgradeCheckFailed_IsUnknownNotUpToDate`,
`RepoCardViewModel_Status_MissingFolderOrSourceProblem_IsProblemWithReason`,
`RepoCardViewModel_Status_FailedPull_IsProblemUntilNextOperation` and
`RepoCardViewModel_Status_ErrorReported_IsProblem`, covering `AgentControl-RepoCardViewModel-StatusAccent`.

**RepoCardViewModel_StatusWords_TextAndReason**: The status texts are "Up to date", "Upgrade available" (the tooltip
naming both versions) and "No agent package"; a missing folder or a pin naming an unconfigured source says
"Needs attention" with the reason in the tooltip; an unreadable source says "Update check failed" with a tooltip that
says it is not known whether a newer version exists. This scenario is tested by
`RepoCardViewModel_Status_ReflectsPackageAndUpgradeState` and
`RepoCardViewModel_Status_MissingFolderOrSourceProblem_IsProblemWithReason`, covering
`AgentControl-RepoCardViewModel-StatusWords`.

**RepoCardViewModel_StatusProblemCleared_NextOperationClearsFailure**: After a refused pull the status is a
problem, and the next operation (a refresh) clears it again. This scenario is tested by
`RepoCardViewModel_Status_FailedPull_IsProblemUntilNextOperation`, covering
`AgentControl-RepoCardViewModel-StatusProblemCleared`.

**RepoCardViewModel_InvalidSourceConfiguration_IsAProblemNotUpToDate**: A card pinned to a package whose
configured sources are invalid (two sources with the same alias) is refreshed and its upgrade is checked: the card
shows a source problem (status `Problem`) with a reason that says the sources are not configured correctly, offers
no upgrade and is not reported as up to date. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatusAsync_InvalidSourceConfiguration_IsAProblemNotUpToDate`, covering
`AgentControl-RepoCardViewModel-InvalidSourceConfigurationIsProblem`.

**RepoCardViewModel_StaleUpgradeCheck_ResultIsNotPublishedAfterPinOrRepoChanged**: An upgrade check is held at its
source (the source would answer 9.0.0 for the old pin). In one case the pin file changes to another version and the
card re-reads it before the source answers: afterwards the card shows the new pin, no upgrade, no latest version, and
neither "checking" nor "check failed". In the other the folder is deleted and the card re-checks before the source
fails: afterwards the card is still missing (status `Problem`), shows no "checking", no late "check failed" and no
upgrade. This scenario is tested by
`RepoCardViewModel_RefreshCheap_PinChangesDuringUpgradeCheck_StaleResultIsNotPublished` and
`RepoCardViewModel_RefreshCheap_RepoBecomesMissingDuringUpgradeCheck_StateIsResetAndResultIgnored`, covering
`AgentControl-RepoCardViewModel-StaleUpgradeCheckDiscarded` and
`AgentControl-RepoCardViewModel-UpgradeStateResetOnRefresh`.

**RepoCardViewModel_UpgradeFromChip_ChipAndMenuBehaveAlike**: The upgrade chip shows the target version with a
tooltip that says it upgrades the agent files to it (`AgentControl-RepoCardViewModel-UpgradeChipTooltip`, tested by
`RepoCardViewModel_UpgradeChip_NewerVersionAtSource_ShowsTargetVersionAndTooltipWithBothVersions`); the chip and the
menu's Upgrade item are one command, which can run while an upgrade is available and no other operation holds the
card busy (`AgentControl-RepoCardViewModel-UpgradeChipAvailability`, tested by
`RepoCardViewModel_UpgradeCommand_ChipAndMenuShareOneGuardedCommand`); and the command performs the upgrade (pin,
release notes) when run (`AgentControl-RepoCardViewModel-UpgradeFromChip`, tested by that same test and by
`RepoCardViewModel_UpgradeCommand_NewerVersionAvailable_UpdatesPinAndRaisesReleaseNotesReady`). The click on the
real chip is also exercised end to end by `windows@UpgradeChip_Click_SyncsManagedFoldersAndShowsReleaseNotes`.

**RepoCardViewModel_AvatarTile_InitialsStableColorAndFolderFallback**: Repo names give the capital letters of
their first two words, or one letter (`AgentControl-RepoCardViewModel-AvatarTile`, tested by
`RepoCardViewModel_AvatarInitials_RepoName_UsesFirstLettersOfFirstTwoWords`); a name without letters or digits
gives none, so the folder glyph is shown (`AgentControl-RepoCardViewModel-AvatarFolderGlyph`, tested by
`RepoCardViewModel_Avatar_EmptyName_FallsBackToFolderGlyph`); the color index is a fixed value for a fixed path, is
the same whatever the case of the path or a trailing separator, always lies in the palette and uses several colors
across many paths (`AgentControl-RepoCardViewModel-AvatarColorStable`, tested by
`RepoCardViewModel_AvatarColorIndex_SamePath_IsStableAndInPalette`).

**RepoCardViewModel_RepoPicture_ShownInPlaceOfTileAndNeverInterruptsTheCard**: Tested by `RepoCardImageTests.cs`
with small PNGs generated by `TestPng` (no binary assets), on a headless Avalonia session. The view model loads a
found picture in the background, does not decode an unchanged file again on a second refresh but does after the
file changes (`AgentControl-RepoCardViewModel-RepoPicture` and `-RepoPictureNotDecodedAgain`, tested by
`RepoCardViewModel_RepoImage_IconPresent_LoadsOnceUntilFileChanges`); falls back to the initials when the file is
removed and never looks for a picture for a missing folder (`AgentControl-RepoCardViewModel-RepoPictureFallback`,
tested by `RepoCardViewModel_RepoImage_FileRemoved_FallsBackToInitials` and
`RepoCardViewModel_RepoImage_MissingFolder_NeverLooksForAnImage`); and keeps the tile - without retrying - when the
decoder fails or throws (a decoder that throws several kinds of exception, resource exhaustion included, never
faults the background refresh task), while an older refresh that fails after a newer one has published leaves the
newer picture in place (`AgentControl-RepoCardViewModel-RepoPictureFailureIsolated`, tested by
`RepoCardViewModel_RepoImage_DecodeFailsOrThrows_KeepsTileAndDoesNotRetry` and
`RepoCardViewModel_RepoImage_OlderRefreshFailsAfterNewerPublished_KeepsNewerImage`). A headless main window shows the
picture (image visible, initials hidden, light backing) for the repo that has one and the initials for the one that
does not (`MainWindow_RepoCard_AvatarShowsRepoPictureWhenPresent`, supporting evidence at the window level). Which
files are acceptable, and the security checks on them (genuine PNG, size and dimension limits, links, the repo
root), are verified by the `RepoImageLocator` unit with the `RepoImageLocatorTests.cs` tests.

**RepoCardViewModel_RepoPictureReleased_ReplacedClearedOvertakenAndRetiredBitmapsAreDisposed**: A card shows a
picture; the repo replaces its icon, and the first bitmap is disposed (touching it throws
`ObjectDisposedException`) while the new one is usable; the icon is removed, the property is cleared and the second
bitmap is disposed; a picture appears again, the card is retired, the property is cleared and the third bitmap is
disposed. In the second test a decode is held in the decoder while a newer refresh publishes: when the older decode
is released, the bitmap it decoded is disposed and never published, and the published bitmap stays intact and shown.
This scenario is tested by `RepoCardViewModel_RepoImage_SupersededClearedOrRetired_BitmapIsDisposed` and
`RepoCardViewModel_RepoImage_DecodedByOvertakenRefresh_IsDisposedAndNotPublished`, covering
`AgentControl-RepoCardViewModel-RepoPictureReleased`.

**RepoCardViewModel_MoveToGroup_RulesEventsAndGroupChip**: Tested by `RepoCardColorTests.cs` and
`RepoGroupingTests.cs` (cards are built over a settings entry, without a refresh, so no git is started for
`RepoCardColorTests`). A blank stored group reads as no group and the existing groups of the settings are listed
(`AgentControl-RepoCardViewModel-GroupList`); moving into a group raises the change event once, the remove command
is available only while the repo is in a group and takes it out, and a valid name is cleaned up and recorded on the
settings entry (`AgentControl-RepoCardViewModel-MoveToGroup`, tested by
`RepoCardViewModel_MoveToGroup_UpdatesGroupAndRaisesChange` and
`RepoCardViewModel_MoveToGroup_NewExistingInvalidAndRemove`); a name that matches an existing one in another letter
case adopts the existing spelling, and names that are too long or are *Favorites* or *Ungrouped* are refused without
any change (`AgentControl-RepoCardViewModel-GroupNameRules`, tested by the same two tests); the new-group command
only raises its request (`AgentControl-RepoCardViewModel-NewGroupPrompt`) and the group chip shows only for a
favorite that has a group (`AgentControl-RepoCardViewModel-GroupChipForFavorites`), both tested by
`RepoCardViewModel_NewGroupCommand_RaisesRequest_AndChipShowsForGroupedFavorites`.

**RepoCardViewModel_CardColor_ChooseResetRefuseAndFallBack**: The palette has one entry per avatar palette
index with distinct names; a card with no choice uses the path-hash color; choosing "tEaL" stores `Teal` and moves
the palette index and style class, and choosing Automatic returns to the hash color and clears the value
(`AgentControl-RepoCardViewModel-CardColor`, tested by `CardColorPalette_All_MatchesTheAvatarPalette`,
`RepoCardViewModel_CardColor_Default_IsAutomatic` and `RepoCardViewModel_SetCardColor_ChoosesAndClearsColor`); the
choice is recorded on the settings entry, the change event is raised once and choosing the same color again raises
nothing (`AgentControl-RepoCardViewModel-CardColorRecorded`, tested by
`RepoCardViewModel_SetCardColor_ChoosesAndClearsColor`); an unknown name is refused and changes nothing
(`AgentControl-RepoCardViewModel-CardColorUnknownRefused`, tested by
`RepoCardViewModel_SetCardColor_UnknownName_IsRefused`); a stored name is found ignoring case and white space and a
blank, null or unknown stored name reads as the automatic color
(`AgentControl-RepoCardViewModel-CardColorStoredFallback`, tested by `CardColorPalette_Find_ToleratesAnyStoredValue`
and `RepoCardViewModel_CardColor_UnknownStoredValue_FallsBackToAutomatic`); and the card's status, status text and
status tooltip are the same before and after a color is chosen
(`AgentControl-RepoCardViewModel-CardColorKeepsStatus`, tested by
`RepoCardViewModel_SetCardColor_DoesNotChangeStatus`).

**RepoCardViewModel_Retire_RemovedCardStopsAndReportsNothing**: An upgrade check held at its source is overtaken by
retiring the card; when the source then answers, the card shows no upgrade and no "checking", and neither an error
nor release notes were raised (`AgentControl-RepoCardViewModel-RetiredCardReportsNothing`, tested by
`RepoCardViewModel_Retire_UpgradeCheckStillRunning_PublishesNothingAndRaisesNothing`). A package apply waiting for its
source is cancelled by retiring: it ends, the card is idle, nothing was reported and nothing was pinned
(`AgentControl-RepoCardViewModel-RetiredCardReportsNothing` and `-RetireCancelsRunningWork`, tested by
`RepoCardViewModel_Retire_OperationRunning_IsCancelledAndNothingIsReported`). A refresh and an upgrade check on a
retired card read no source and offer no upgrade (`AgentControl-RepoCardViewModel-RetiredCardChecksNothing`, tested by
`RepoCardViewModel_Retire_LaterRefresh_DoesNothing`).

**RepoCardViewModel_ContentIdentity_RecordedRefusedConfirmedAndAdopted**: Applying a package (Select Package, or
Upgrade to a newer version) writes the SHA-256 of the zip, computed independently by the test, into the pin. Using
real zip sources, after the 1.0.0 zip is republished with other content: an Upgrade that finds it as the latest
version raises an error saying the files "now contain different files" and that nothing was changed, extracts nothing
and leaves the pin file byte for byte unchanged; the restore of missing managed folders before launch returns
`false` with an error saying the agent files were not synced, creates no folder and leaves the pin file byte for byte
unchanged; selecting the same version raises `ContentChangeConfirmationRequested` (carrying the selection and a
question naming the package), raises no error and changes nothing, and selecting it with the user's confirmation
applies the changed content and records its new identity; selecting it with nobody subscribed to the confirmation is
refused with the same error. A pin written without an identity applies silently and has an identity recorded; an
Upgrade to a newer version records the new version's identity without a warning; the restore at launch of such a pin,
and of an unchanged package, extracts and leaves the pin file untouched; a source whose reader reports no identity
is applied without any warning and the pin then records none. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackage_FirstApply_RecordsZipSha256InPin`,
`RepoCardViewModel_UpgradeCommand_NewerVersion_RecordsNewIdentityWithoutWarning`,
`RepoCardViewModel_UpgradeCommand_SameVersionRepublished_RefusesWithoutChangingRepoOrPin`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SameVersionRepublished_RefusesWithoutExtractingOrWritingPin`,
`RepoCardViewModel_ApplySelectedPackage_SameVersionRepublished_RequestsConfirmationAndChangesNothing`,
`RepoCardViewModel_ApplySelectedPackage_SameVersionRepublishedAndAccepted_AppliesAndRecordsNewIdentity`,
`RepoCardViewModel_ApplySelectedPackage_SameVersionRepublishedWithoutSubscriber_IsRefused`,
`RepoCardViewModel_ApplySelectedPackage_PinWithoutIdentity_AdoptsIdentityWithoutWarning`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinWithoutIdentity_SyncsSilentlyAndNeverWritesPin`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_UnchangedContent_SyncsAndNeverWritesPin`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_AgentsMdOfferDeclined_NeverWritesPin` (a package carrying an
`AGENTS.md` template whose offer, raised by the restore, is declined leaves the pin file byte for byte unchanged and
not marked declined, while declining the offer raised by an explicit apply is still recorded, per
`RepoCardViewModel_ApplySelectedPackage_AgentsMdOfferDeclined_RecordsDeclineInPin`) and
`RepoCardViewModel_ApplySelectedPackage_SourceReportsNoIdentity_IsNeverTreatedAsChanged` (in
`RepoCardContentIdentityTests.cs`), covering `AgentControl-RepoCardViewModel-RecordContentIdentity`,
`AgentControl-RepoCardViewModel-RefuseChangedContent`, `AgentControl-RepoCardViewModel-RefuseChangedContentMessage`,
`AgentControl-RepoCardViewModel-ConfirmChangedContent`, `AgentControl-RepoCardViewModel-ApplyConfirmedContent`,
`AgentControl-RepoCardViewModel-RefuseChangeWithoutAsking`, `AgentControl-RepoCardViewModel-AdoptMissingIdentity`,
`AgentControl-RepoCardViewModel-IgnoreMissingSourceIdentity` and
`AgentControl-RepoCardViewModel-LaunchSyncNeverWritesPin`.
The refusal messages (the Upgrade of a republished version and the restore before launch) say that "Change package..."
is how to accept the change and no longer tell the user to delete the pin file; the two refusal tests assert that
(`RepoCardContentIdentityTests.cs`: the message contains `Change package...` and does not contain `.agentcontrol.json`;
the Upgrade refusal also asserts that neither `sha256` nor the recorded hash appears in the message).
Refusing an Upgrade of a republished version is, in the application, reachable only by a race (an Upgrade is offered
only for a version newer than the pin), so this refusal is verified at this unit level only; the confirmation, by
contrast, is reachable through Change package... and is shown end to end in the system verification.

**RepoCardViewModel_GitSourceAwareness_IconAliasTagAndShortCommit**: A card pinned to a Git source shows the Git icon
and the source's alias beside the package and version; its chip tooltip reads "Agent files: {package} {version} from
source '{alias}' (git repository), tag {tag} @ {first seven characters of the commit}."
where the tag is the source's tag
pattern applied to the pinned version and the commit is the one the pin recorded; with no recorded commit (a pin made
before commits were recorded) it names the tag only; and a pin to a zip source names neither. This scenario is tested by
`RepoCardViewModel_PinnedGitSource_ShowsGitIconAndAlias`,
`RepoCardViewModel_PackageChipTooltip_GitPinWithCommit_NamesTagAndShortCommit`,
`RepoCardViewModel_PackageChipTooltip_GitPinWithoutIdentity_NamesTagOnly` and
`RepoCardViewModel_PackageChipTooltip_ZipPin_HasNoTagOrCommit`, covering
`AgentControl-RepoCardViewModel-GitSourceAwareness` (icon and alias)
and `AgentControl-RepoCardViewModel-GitSourceTooltip`
(tag and short commit). The tag is derived from the source's tag pattern as configured now, not stored in the pin. The
tests use a `FakePackageSourceProvider` standing in for the Git
source and a pin file written for the test, so no Git repository or network is involved.

**RepoCardViewModel_TagMovedWarning_ShownNeverAppliedAndClearedWhenAccepted**: With a Git pin that recorded a commit,
a listing that shows the pinned version at another commit makes the card show the "Tag moved" status - outranking an
available upgrade, hiding the up-to-date cue, with a tooltip that names the tag, "abcdef0 -> 1234567" and "Change
package..." - and the check acquires nothing, extracts nothing and leaves the pin byte for byte unchanged; a tag that
still points to the recorded commit, a pin that recorded no commit, a pin whose identity is not a well-formed commit
identity (a `sha256:` digest, a malformed or short `git-commit:` value or any other text, in a theory) and a source that
is not a Git source (even if its listing carries a revision) are never reported as moved; and once the pin records the
new commit (the user accepted it) a refresh clears the warning and the card is up to date again. Choosing the pinned
version again when the tag now
delivers another commit asks for confirmation and changes nothing (no error, no extraction, the pin as it was); with the
confirmation it extracts the new content and records the new commit (the short commit and the warning follow); and with
nobody to ask the refusal advises Change package... and does not tell the user to edit or delete the pin file. This
scenario is tested by `RepoCardViewModel_RefreshUpgradeStatus_TagMoved_ShowsTagMovedStatusAndTooltip`,
`RepoCardViewModel_RefreshUpgradeStatus_TagMoved_AppliesNothing`,
`RepoCardViewModel_RefreshUpgradeStatus_SameCommit_IsNotMoved`,
`RepoCardViewModel_RefreshUpgradeStatus_ZipSourceWithRevision_IsNotMoved`,
`RepoCardViewModel_RefreshUpgradeStatus_PinWithoutIdentity_IsNotMoved`,
`RepoCardViewModel_RefreshUpgradeStatus_GitSourceWithNonCommitIdentity_IsNotMoved`,
`RepoCardViewModel_RefreshUpgradeStatus_PinUpdatedAfterTagMoved_ClearsMovedTag`,
`RepoCardViewModel_ApplySelectedPackage_GitVersionAtNewCommit_AsksAndChangesNothing`,
`RepoCardViewModel_ApplySelectedPackage_GitVersionAtNewCommitAccepted_AppliesAndRecordsCommit` and
`RepoCardViewModel_ApplySelectedPackage_GitVersionAtNewCommitWithoutSubscriber_AdvisesChangePackage` (all in
`RepoCardGitAwarenessTests.cs`), covering `AgentControl-RepoCardViewModel-TagMovedWarning`, `-TagMovedAppliesNothing`,
`-TagMovedNeedsEvidence` and `-TagMovedClearedByNewPin` and, for the apply tests,
`AgentControl-RepoCardViewModel-ConfirmChangedContent`, `-ApplyConfirmedContent` and `-RefuseChangeWithoutAsking`. That
a click on the chip opens Change package... is the chip's binding to the same command and is exercised only end to end.

**RepoCardViewModel_TagMovedFreshness_UserRefreshListsAgainAndUnknownStateClearsTheWarning**: A listing is cached while
the tag still points to the recorded commit and the tag is then moved: a background check answers from the cache (the
fake provider was asked once and no warning shows), while the card's Refresh reads the Git source again (the provider is
asked a second time) and shows "Tag moved"; the same Refresh of a card pinned to a zip source keeps using the cached
listing (still one call). When the warning is shown and the next Refresh fails because the source cannot be read, the
warning is cleared, its tooltip is empty, the check-failed state is shown and the status is no longer "Tag moved"; and
when the repo's folder disappears the next cheap refresh clears the warning and tells its bindings. Because a pin does
not record the tag, editing the source's tag pattern re-maps the pinned version to another tag: with the tags
`release/1.2.3` at the recorded commit and `v1.2.3` at another commit, a pattern changed from `release/{version}` to
`v{version}` makes the card name `v1.2.3` and show "Tag moved" with both commits (nothing applied, the pin file byte for
byte unchanged), and restoring the pattern clears it. This scenario is tested by
`RepoCardViewModel_RefreshAsync_GitSourceTagMovedAfterCachedListing_ListsAgainAndShowsTagMoved`,
`RepoCardViewModel_RefreshAsync_ZipSource_KeepsUsingCachedListing`,
`RepoCardViewModel_RefreshAsync_FailsAfterTagMoved_ClearsTagMovedAndShowsCheckFailed`,
`RepoCardViewModel_RefreshCheap_RepoFolderGoesMissingAfterTagMoved_ClearsTagMoved` and
`RepoCardViewModel_RefreshUpgradeStatus_TagPatternEdited_RemappedTagAtOtherCommitIsReportedAsTagMoved` (the last uses a
fake Git provider that lists a fixed tag set through the tag pattern of the definition it is created from), covering
`AgentControl-RepoCardViewModel-TagMovedFreshListing`, `-TagMovedClearedWhenUnknown` and `-TagMovedFollowsTagPattern`.

**RepoCardViewModel_GitTooOld_BackgroundCheckExplainsWithoutSignIn**: When the background check's listing fails because
the installed git is older than the supported version, the card has a package source problem with the explanation as
the message, does not say a sign-in is needed, is a problem status and offers no upgrade. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatus_GitTooOld_ShowsSourceProblemWithoutSignIn`, covering
`AgentControl-RepoCardViewModel-GitTooOldProblem` and `AgentControl-RepoCardViewModel-GitTooOldNoUpgrade`. The version
gate itself (git older than 2.29 is refused before any
remote command, and a git at the minimum is accepted) is verified in the `GitIntegration` and `AgentPackageManagement`
tests, and the existing `RepoCardGitSourceTests.cs` shows the gate asked first, then `ls-remote`.

**RepoCardViewModel_GitSourceOptions_PromptOnlyForUserStartedWork**: With a Git source configured and a stub as the git
executable (from the settings, or from the startup override when the settings name none), the registry runs git through
that executable; the registry built for work the user started runs git without
`core.askPass=` and with `GCM_INTERACTIVE` left alone, and the registry built for background work runs it with
`GCM_INTERACTIVE=never` and `core.askPass=`, both with `GIT_TERMINAL_PROMPT=0`; the Git cache root is a `git-cache`
folder inside the isolated configuration directory when one is given, and the default (none) otherwise. This scenario is
tested by `RepoCardViewModel_CreateSourceRegistry_GitSource_RunsTheConfiguredOrOverriddenGit`,
`RepoCardViewModel_CreateSourceRegistry_GitSource_PromptsOnlyForUserStartedWork` and
`RepoCardViewModel_GetGitCacheRoot_UsesConfigDirectoryWhenGiven`, covering
`AgentControl-RepoCardViewModel-GitSourceOptions`, `-GitSourcePrompts` and `-GitSourceCacheRoot`.
