## LauncherUI

### Verification Approach

The `LauncherUI` subsystem is verified by the combined unit tests of its four units
(`MainWindowViewModelTests.cs`, `RepoCardViewModelTests.cs`,
`SelectPackageWindowViewModelTests.cs`, `SettingsWindowViewModelTests.cs`), which together
exercise the full recent-repos/card/select-package/settings interaction surface without ever
constructing an Avalonia `Window`. At the subsystem boundary, collaborating subsystems
(`AgentPackageManagement`, `RepoSync`, `RepoConfig`, `GitIntegration`, `AgentToolLauncher`,
`Settings`) are exercised through their real implementations against real temporary
directories and package-zip fixtures, and git is substituted with a stub script (`GitStub`,
shared with `GitClientTests`) so no test depends on a real git installation. The About
command's dependency-list data source has direct unit coverage
(`ThirdPartyDependenciesTests.cs`), since it is a plain static data source independent of any
Avalonia control; the About dialog itself still has no dedicated view-model class, so its
end-to-end rendering (opening the window and showing the tagline, version, copyright, license,
and a non-empty dependency list) is verified only via the FlaUI
`DemaConsulting.AgentControl.UiTests` project. The logo image itself is not asserted by any
automated test (it has no bound text content to check) and remains a visual-inspection-only
element.

### Test Environment

Unit tests run under xUnit v3 using unique temporary configuration and repo directories per
test (hermetic, parallel-safe). Tests that construct a `MainWindowViewModel` or
`RepoCardViewModel` run in the `RealProcess` xUnit collection (disabled parallelization)
because eager `RefreshCheap()` calls spawn real subprocesses via `GitStub` or the system git
executable.

### Acceptance Criteria

- All unit tests pass with zero failures.
- Every documented UI action (add/remove repo, search/sort, launch, pull, select package,
  upgrade, settings, about) produces its documented observable outcome.
- Removal requires explicit confirmation; no test path removes a card without it.
- Status badges (upgrade-available, missing-repo, committed-files) reflect the correct
  underlying state for both normal and boundary conditions.
- The AGENTS.md template offer fires at most once per repo, never overwrites an existing
  `AGENTS.md`, and a decline is honored on every subsequent sync for that repo.

### Test Scenarios

**LauncherUI_RecentReposDisplay_ConstructorPopulatesCards**: `MainWindowViewModel` is
constructed from settings containing two recent repos; both appear as cards in order. This
scenario is tested by `MainWindowViewModel_Constructor_SettingsHaveRecentRepos_PopulatesRepoCards`,
covering `AgentControl-LauncherUI-RecentReposDisplay`.

**LauncherUI_StatusBadges_UpgradeMissingAndCommittedFilesBadgesReflectState**: A repo card is
refreshed under three conditions — a newer package version available, a repo path that no
longer exists, and an unchanged HEAD hash — and the upgrade, missing-repo, and cached
committed-files badges are each set correctly. This scenario is tested by
`RepoCardViewModel_Refresh_NewerVersionAtSource_SetsUpgradeAvailableTrue`,
`RepoCardViewModel_RefreshCheap_RepoPathDoesNotExist_SetsIsMissingAndSuppressesOtherState`, and
`RepoCardViewModel_RefreshCheap_UnchangedHeadHash_CommittedFilesBadgeIsCached`, covering
`AgentControl-LauncherUI-StatusBadges`.

**LauncherUI_AddRepo_ValidDuplicateAndMissingPaths_ProduceCorrectOutcomes**: `AddRepo` is
called with a valid new folder path (inserted and persisted), a nonexistent path (rejected),
and an already-tracked path (rejected). This scenario is tested by
`MainWindowViewModel_AddRepo_ValidNewPath_InsertsCardAndPersistsSettings`,
`MainWindowViewModel_AddRepo_PathDoesNotExist_ReturnsFalse`, and
`MainWindowViewModel_AddRepo_DuplicatePath_ReturnsFalse`, covering
`AgentControl-LauncherUI-AddRepo`.

**LauncherUI_RemoveRepo_RequiresConfirmationBeforeMutatingState**: A card's `RemoveCommand` is
executed (raising `RemoveRequested` without removing the card), and `MainWindowViewModel`'s
own `RemoveRepo` is called separately (removing the card and persisting settings), proving
removal is a two-step, confirmation-gated flow. This scenario is tested by
`MainWindowViewModel_RemoveRepo_RemovesFromCollectionsAndPersistsSettings` and
`MainWindowViewModel_CardRaisesRemoveRequested_DoesNotRemoveCardWithoutConfirmation`, covering
`AgentControl-LauncherUI-RemoveRepo`.

**LauncherUI_SearchFilter_MatchesNameOrPathCaseInsensitively**: `FilterText` is set to a
substring that matches only one repo's display name, then to a substring that matches only by
full path; in both cases only the matching card remains displayed. This scenario is tested by
`MainWindowViewModel_FilterText_MatchesRepoNameCaseInsensitive_FiltersDisplayedRepoCards` and
`MainWindowViewModel_FilterText_MatchesRepoPath_FiltersDisplayedRepoCards`, covering
`AgentControl-LauncherUI-SearchFilter`.

**LauncherUI_SortOrder_FavoritesFirstThenMostRecentlyLaunched**: Displayed cards are checked
for favorites sorting above non-favorites regardless of launch recency, and for
most-recently-launched-first ordering within the same favorite tier with never-launched repos
last. This scenario is tested by
`MainWindowViewModel_DisplayedRepoCards_SortsFavoritesAboveNonFavorites` and
`MainWindowViewModel_DisplayedRepoCards_SortsByLastLaunchedUtcDescendingWithNullsLast`,
covering `AgentControl-LauncherUI-SortOrder`.

**LauncherUI_Launch_NeverBlockedByAgentPackageSyncState**: The Launch command is executed for
a card whose agent files are already synced, recording the launch timestamp; the ensure-synced
check is exercised confirming it returns true without re-extracting when folders are already
present; and a separate scenario confirms the agent-tool process still launches (raising
`LaunchRecorded`) when the best-effort sync attempt has no pin to work with, or is attempted
and fails outright. This scenario is tested by
`RepoCardViewModel_LaunchCommand_Succeeds_RecordsLaunchTimestampAndRaisesLaunchRecorded`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedAndFoldersPresent_ReturnsTrueWithoutReExtracting`,
and `RepoCardViewModel_LaunchCommand_SyncFailsOrNoPin_StillLaunches`, covering
`AgentControl-LauncherUI-Launch`.

**LauncherUI_Pull_EnabledOnlyForCleanWorkingTree**: A card with a clean working tree reports
pull as enabled, a card with a dirty working tree reports it disabled, and executing
`PullCommand` on a clean repo updates the status message on success. This scenario is tested
by `RepoCardViewModel_Refresh_CleanWorkingTree_SetsCanPullTrue`,
`RepoCardViewModel_Refresh_DirtyWorkingTree_SetsCanPullFalse`, and
`RepoCardViewModel_PullCommand_SuccessfulPull_UpdatesStatusMessage`, covering
`AgentControl-LauncherUI-Pull`.

**LauncherUI_SelectPackage_BrowseAndApplyToUnpinnedRepo**: The select-package request is
raised with the configured source path, the selection window populates versions descending
and defaults to latest, confirming raises the selected pair, and applying the selected package
updates the pin, extracts the files, and raises release-notes-ready. This scenario is tested
by `RepoCardViewModel_SelectPackageCommand_SourceConfigured_RaisesSelectPackageRequestedWithSourcePath`,
`SelectPackageWindowViewModel_SelectPackageName_PopulatesVersionsDescendingAndDefaultsToLatest`,
`SelectPackageWindowViewModel_ConfirmCommand_RaisesConfirmedWithSelectedPair`, and
`RepoCardViewModel_ApplySelectedPackage_ValidNameAndVersion_UpdatesPinAndExtractsAndRaisesReleaseNotesReady`,
covering `AgentControl-LauncherUI-SelectPackage`.

**LauncherUI_Upgrade_OnlyWhenNewerVersionAvailable**: `UpgradeCommand` is executed when a newer
version is available (updating the pin and raising release-notes-ready) and when no package
source is configured (raising an error instead). This scenario is tested by
`RepoCardViewModel_UpgradeCommand_NewerVersionAvailable_UpdatesPinAndRaisesReleaseNotesReady`
and `RepoCardViewModel_UpgradeCommand_NoPackageSourceConfigured_RaisesErrorOccurred`, covering
`AgentControl-LauncherUI-Upgrade`.

**LauncherUI_AgentsMdTemplateOffer_OfferedOnceAndNeverOverwritesExistingFile**: After a
genuine sync (package selection, upgrade, or ensure-synced-before-launch re-extraction), the
offer is raised with the package's template content when the repo lacks an `AGENTS.md`;
accepting writes the template verbatim to the repo root; declining persists the decision in
the pin file without writing a file, and a subsequent sync for the same repo does not
re-raise the offer. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackage_NoAgentsMdAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequestedWithContent`,
`RepoCardViewModel_AcceptAgentsMdTemplate_WritesFileToRepoRootWithGivenContent`,
`RepoCardViewModel_DeclineAgentsMdTemplate_PersistsDeclinedFlagInPinFile`, and
`RepoCardViewModel_DeclineAgentsMdTemplate_SubsequentApplySelectedPackage_DoesNotReprompt`,
covering `AgentControl-LauncherUI-AgentsMdTemplateOffer`.

**LauncherUI_Settings_SaveAndApplyPreservesRecentReposAndUpdatesFields**: The settings
dialog's `SaveCommand` invokes its `onSave` callback and raises `Saved` with the current
property values, and applying the resulting settings on `MainWindowViewModel` preserves the
existing recent-repos list while adopting the new field values. This scenario is tested by
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved` and
`MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields`, covering
`AgentControl-LauncherUI-Settings`.

**LauncherUI_About_EndToEndOnly_OpensAboutWindow**: The About dialog itself has no dedicated
view-model class, so its end-to-end rendering is verified via the FlaUI project: opening the
window and confirming the mission tagline, version, copyright, and license text are all
present and non-empty, and that the third-party dependency list control is present and
rendered with at least one entry. The application logo image is not automated-asserted (it has
no text content to check) and remains a visual-inspection-only element. The dependency list's
underlying data source (name/version/license entries, no duplicates, no empty fields, and the
expected display format) has direct unit coverage. This scenario is tested by
`windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright`,
`ThirdPartyDependencies_All_MatchesCsprojDirectRuntimeDependencies`,
`ThirdPartyDependencies_All_NoDuplicateNames`,
`ThirdPartyDependencies_All_EveryEntryHasNonEmptyLicense`,
`ThirdPartyDependencies_All_EveryEntryHasNonEmptyVersion`, and
`DependencyInfo_ToString_RepresentativeEntry_ReturnsNameVersionLicenseFormat`, covering
`AgentControl-LauncherUI-About`.
