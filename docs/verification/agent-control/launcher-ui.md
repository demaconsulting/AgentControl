## LauncherUI

### Verification Approach

The `LauncherUI` subsystem is verified by the combined unit tests of its six units
(`MainWindowViewModelTests.cs`, `RepoCardViewModelTests.cs` and `RepoCardServerSourceTests.cs`,
`SelectPackageWindowViewModelTests.cs` and `SelectPackageRetryTests.cs`,
`SettingsWindowViewModelTests.cs`, `SourceEditorViewModelTests.cs`,
`SourceEditorServerViewModelTests.cs` and `SourceEditorSignatureViewModelTests.cs`,
`CredentialPromptViewModelTests.cs`), plus
`AsyncRelayCommandTests.cs` for the shared asynchronous
command, which together
exercise the full recent-repos/card/select-package/settings/sign-in interaction surface without ever
constructing an Avalonia `Window`. The windows themselves are exercised by headless Avalonia tests
(`DialogWindowTests.cs`, `MainWindowTests.cs`, `SettingsWindowServerSourceTests.cs` for the Settings
window, the source editor window and its connection test, `SourceEditorSignatureWindowTests.cs` for the signature
section and "Forget saved sign-in" of the source editor, `RepoCardSignatureTests.cs` for the signature shield in the
main window's package chip, and `CredentialPromptWindowTests.cs` for the sign-in
dialog, its "Remember on this computer" option and `AvaloniaCredentialPrompt`), and the end-to-end flows -
adding a zip and a
NuGet folder source through the dialogs, adding a NuGet server and testing its connection with a typed
sign-in, selecting a package from the NuGet sources, a source that requires signed packages refusing an unsigned
upgrade, and the
chips on a repo card - by the FlaUI `DemaConsulting.AgentControl.UiTests` project. At the
subsystem boundary, collaborating subsystems
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
element. `FavoriteIconConverter`/`FavoriteColorConverter`, the favorite-star glyph's
shape/color converters, are likewise visual-only value converters with no bound text content
to assert and remain visual-inspection-only elements; `RepoCardViewModel.IsFavorite` itself
(the state they render) is covered indirectly by the sort-order scenario below.

The server-source tests use scripted fakes at the subsystem boundary instead of a network: a
`FakePackageSourceProvider` (which can be gated to simulate a slow source and can be told to throw
a given exception, including a `PackageSourceAccessException` of each failure kind) stands behind the view
models, and `FakeCredentialPrompt` stands in for the sign-in dialog; the real `NuGetSourceProvider` against
a mock NuGet server is verified in the `AgentPackageManagement` subsystem. The headless window tests rely on
two test-infrastructure properties that were corrected in this pass: `HeadlessTestApp.RunAsync` now waits for
the asynchronous body to finish (it previously returned without waiting, so an assertion failing inside an
asynchronous headless test never failed the test), and the `DialogWindowTests.Click` helper now also executes a
button's bound `Command` (raising the click event alone does not run a view-model command).

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
- A NuGet server source can be added, tested (with progress, Cancel and a plain-language result) and listed
  as "Not checked"; the sign-in dialog shows which server receives the credential, masks the password, answers
  once, and is shown one at a time; background upgrade checks never open it.
- The sign-in dialog's "Remember on this computer" option is off by default, enabled only when a credential helper
  can keep the sign-in and otherwise disabled with a tooltip that explains why, and the answer carries the choice.
- The source editor offers the signature options for NuGet sources only (disabled with the reason where unsupported)
  and, for a server, "Forget saved sign-in" only after a confirmation; the repo card's package chip shows the signature
  shield only when the pinned source requires signed packages.

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
raised, the selection window populates versions descending and defaults to latest, confirming
raises the full package reference (source, package, version), and applying the selected package
updates the pin, extracts the files, and raises release-notes-ready. This scenario is tested
by `RepoCardViewModel_SelectPackageCommand_SourceConfigured_RaisesSelectPackageRequestedWithSourceAlias`,
`SelectPackageWindowViewModel_SelectPackageName_PopulatesVersionsDescendingAndDefaultsToLatest`,
`SelectPackageWindowViewModel_ConfirmCommand_RaisesConfirmedWithFullPackageReference`, and
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
underlying data source (name/license entries matching the project file's package names, no
duplicates, no empty fields, and the expected display format) has direct unit coverage. This scenario is tested by
`windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright`,
`ThirdPartyDependencies_All_MatchesCsprojDirectRuntimeDependencies`,
`ThirdPartyDependencies_All_NoDuplicateNames`,
`ThirdPartyDependencies_All_EveryEntryHasNonEmptyLicense`,
and `DependencyInfo_ToString_RepresentativeEntry_ReturnsNameLicenseFormat`, covering
`AgentControl-LauncherUI-About`.

**LauncherUI_SourceAwareActions_UnitTestsCoverPinBindingAndTargetedInvalidation**: Source-aware
select/upgrade/launch-sync behavior is verified by the `RepoCardViewModel`,
`SettingsWindowViewModel`, `SourceEditorViewModel`, `MainWindowViewModel`, and `SelectPackageWindowViewModel` unit tests
(see their unit verification designs), with
`RepoCardViewModel_Refresh_PinNamesUnconfiguredSource_ShowsSourceProblemAndOffersNoUpgrade` and
`RepoCardViewModel_ApplySelectedPackage_SecondSourceSelected_PinRecordsThatSourceAndItsContent`
cited directly at the subsystem level, covering `AgentControl-LauncherUI-SourceAwareActions`
(children: `AgentControl-RepoCardViewModel-SourceAwareness`,
`AgentControl-MainWindowViewModel-InvalidateChangedSources`,
`AgentControl-SelectPackageWindowViewModel-SourceScoped`).

**LauncherUI_PackageSources_ManageSourcesThroughSettingsAndEditor**: The settings view model
lists, adds, edits and removes sources (warning about pinned repos before a removal), and the
source editor validates what is entered and only saves a valid source; the
dialogs are exercised end to end by adding a zip folder source and a NuGet folder source
through the real windows. A NuGet server is added through its own button and editor window (feed address,
examples, "Allow insecure HTTP" off by default, test button), an invalid or plain-http address blocks Save
until fixed or explicitly allowed, and a saved server is listed as "Not checked" without any folder check or
network access. This scenario is tested by
`SettingsWindowViewModel_Constructor_ListsConfiguredSourcesInOrder`,
`SettingsWindowViewModel_AddZipSource_EditorSaved_AddsAndSelectsSource`,
`SettingsWindowViewModel_RemoveSource_WarnsAboutPinnedReposAndRemoves`,
`SourceEditorViewModel_EditExistingSource_AliasFixedAndOtherSettingsPreserved`,
`SettingsWindowViewModel_AddNuGetServerSourceCommand_RequestsServerEditor`,
`SettingsWindowViewModel_SaveServerSource_ListsItAsNotCheckedAndPersistsIt`,
`SettingsWindow_AddNuGetServerSourceButton_OpensServerEditorWithServerControls` and
`SourceEditorViewModel_ServerLocation_Invalid_ReportsProblemAndBlocksSave`, covering
`AgentControl-LauncherUI-PackageSources`.

**LauncherUI_TestConnection_ProgressCancelAndPlainLanguageResult**: In the server editor, the test lists the
versions of the first package ID through a scripted provider; while it runs the dialog shows progress and a
Cancel button, and afterwards it shows a headline and detail for each outcome (connection OK with count and
newest, package not found, sign-in failed, sign-in canceled, timed out, server unreachable, other failure);
closing the window while a test runs cancels it, and the application's view model hands the editor a real NuGet
provider wired to the same credential facilities as the cards. This scenario is tested by
`SourceEditorWindow_TestConnection_ShowsProgressCancelAndResult`,
`SourceEditorWindow_ClosedWhileTesting_CancelsTheTest`,
`SourceEditorViewModel_TestConnection_ServerAnswers_ReportsVersionCountAndLatest`,
`SourceEditorViewModel_TestConnection_PackageMissing_ReportsPackageNotFound`,
`SourceEditorViewModel_TestConnection_ProviderFails_ReportsHeadlineAndMessage`,
`SourceEditorViewModel_TestConnection_CancelWhileRunning_StopsWithCanceledResult` and
`MainWindowViewModel_CreateConnectionTestProvider_ReturnsNuGetProviderWithCredentials`, covering
`AgentControl-LauncherUI-TestConnection`.

**LauncherUI_SignIn_DialogShowsDestinationMasksPasswordAndAnswersOnce**: The sign-in window, shown
headlessly, displays the server's origin and the sentence "Used for this session only - AgentControl does not
save it.", masks the password box and keeps Sign in disabled until both fields are filled; a plain-http server
shows a warning and a previously rejected credential shows a notice; Sign in closes with the typed credential
while Cancel, Escape and closing the window all answer no credential. `AvaloniaCredentialPrompt`, asked from a
worker thread, shows the dialog on the UI thread and returns the typed credential, reports a cancel as no
credential, closes the dialog and throws a cancellation when its token is cancelled, shows two simultaneous
questions one at a time, and refuses a null request. This scenario is tested by
`CredentialPromptWindow_Shown_ShowsOriginSessionNoteAndMaskedPassword`,
`CredentialPromptWindow_InsecureAndRejected_ShowWarningAndRejectedNotice`,
`CredentialPromptWindow_SignInClicked_ReturnsTheTypedCredential`,
`CredentialPromptWindow_CancelEscapeOrClose_ReturnNoCredential`,
`AvaloniaCredentialPrompt_PromptAsync_FromWorkerThread_ShowsDialogAndReturnsCredential`,
`AvaloniaCredentialPrompt_PromptAsync_UserCancels_ReturnsNull`,
`AvaloniaCredentialPrompt_PromptAsync_TokenCancelled_ClosesDialogAndThrows` and
`AvaloniaCredentialPrompt_PromptAsync_TwoQuestionsAtOnce_AreShownOneAtATime`, covering
`AgentControl-LauncherUI-SignIn`.

**LauncherUI_RememberSignIn_OptionOfferedOnlyWhenItCanBeKept**: With a request that says a credential helper is
available the sign-in window shows the "Remember on this computer" checkbox enabled and unchecked, and ticking it
makes the dialog answer with the choice and say that the helper saves it; without a credential helper the checkbox is
disabled, still unchecked, and carries a tooltip that explains why - shown for the disabled control (the tooltip's
show-on-disabled setting is asserted). This scenario is tested by
`CredentialPromptWindow_CanRemember_CheckboxEnabledUncheckedAndAnswersRemember`,
`CredentialPromptWindow_CannotRemember_CheckboxDisabledWithExplanatoryTooltip` and
`CredentialPromptViewModel_Remember_TickedWhenAvailable_AnswersWithRememberAndChangesTheNote`, covering
`AgentControl-LauncherUI-RememberSignIn`.

**LauncherUI_ForgetSavedSignIn_ConfirmationDecidesAndResultIsShown**: In the editor window of a server source,
"Forget saved sign-in" first shows a confirmation naming the server - declining leaves the saved sign-in alone - and
once confirmed removes it and shows the outcome in words; with no credential store, or with no Git credential helper
configured for the server, the button is disabled and its explanation is shown on hover. This scenario is tested by
`SourceEditorWindow_ForgetSavedSignIn_ConfirmationDecidesAndResultIsShown`,
`SourceEditorWindow_ForgetSavedSignIn_NoCredentialStore_ButtonDisabledWithTooltip` and
`SourceEditorWindow_ForgetSavedSignIn_NoCredentialHelper_ButtonDisabledWithTooltip`, covering
`AgentControl-LauncherUI-ForgetSavedSignIn`.

**LauncherUI_SignatureOptions_TickEnablesFingerprintAndUnsupportedPlatformExplains**: In the editor window of a NuGet
source the signature section shows the unchecked checkbox, the help text that explains the limit of requiring
signatures without a pinned publisher, and the disabled fingerprint field; ticking the checkbox enables the field, an
unreadable fingerprint shows an error and disables Save, and a readable one is saved in its normalized form; where
verification is unsupported the checkbox is disabled and its tooltip, which gives the reason, is shown even though the
control is disabled; a zip-package editor has no signature section. This scenario is tested by
`SourceEditorWindow_SignatureOptions_TickingEnablesTheFingerprintAndValidatesIt`,
`SourceEditorWindow_SignatureOptions_UnsupportedPlatform_CheckboxDisabledWithTooltip`,
`SourceEditorWindow_ZipSource_HasNoSignatureSection` and
`SourceEditorViewModel_Fingerprint_ValidationFollowsTheRules`, covering
`AgentControl-LauncherUI-SignatureOptions`.

**LauncherUI_SignatureIndicator_ShieldShownOnlyWhenSignaturesAreRequired**: The main window's package chip shows the
shield icon, with the card's tooltip, only for a card whose pinned source requires signatures; the card's properties
name the start and end of a pinned fingerprint only. This scenario is tested by
`MainWindow_PackageChip_ShowsShieldOnlyWhenSignaturesAreRequired` and
`RepoCardViewModel_PinnedSourceRequiresSignatures_ShowsShieldWithTooltip`, covering
`AgentControl-LauncherUI-SignatureIndicator`.

**LauncherUI_BackgroundChecksDoNotPrompt_UserStartedChecksMay**: A background upgrade check against a
server that needs a sign-in never opens the dialog and puts the instruction on the card; a check the user
starts opens it once, finds the upgrade, and a later background check - even for another card - reuses the
remembered credential without asking. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatusAsync_BackgroundCheckNeedsSignIn_DoesNotPromptAndExplains` and
`RepoCardViewModel_RefreshUpgradeStatusAsync_UserRequestedCheck_PromptsOnceThenBackgroundChecksReuseCredential`,
covering `AgentControl-LauncherUI-BackgroundChecksDoNotPrompt`.

**LauncherUI_RepoCardChips_PackageUpgradeBranchAndLaunchLabel**: A card pinned to a zip source
shows its package chip with name, version and source in the tooltip, a card pinned to a server source shows
the source's alias beside the server icon (a different icon from the zip and NuGet-folder icons), an upgrade
chip names the
target version, a branch chip shows the branch, and the Launch label names the configured tool.
This scenario is tested by
`RepoCardViewModel_PackageChip_PinnedToZipSource_ShowsNameVersionAndSourceInTooltip`,
`RepoCardViewModel_PinnedToServerSource_ChipShowsAliasAndServerIcon`,
`RepoCardViewModel_UpgradeChip_NewerVersionAtSource_ShowsTargetVersionAndTooltipWithBothVersions`,
`RepoCardViewModel_BranchChip_BranchKnown_ShowsBranchWithTooltip`, and
`RepoCardViewModel_LaunchButtonText_ConfiguredTool_NamesToolOrFallsBack`, covering
`AgentControl-LauncherUI-RepoCardChips`.

**LauncherUI_ResponsiveOperations_AsyncCommandsBackgroundChecksAndIsolatedFailures**: The
asynchronous command ignores a repeated request while running and treats cancellation as no
error; a package apply that is waiting on a slow source reports busy and cancels without changing
the pin; the background upgrade refresh keeps checking other cards when one source is
unreachable; and the Select Package dialog loads the other sources and shows an inline error for
the unreachable one, and the failed read can be retried on its own without touching the other sources. This
scenario is tested by
`AsyncRelayCommand_ExecuteAsync_WhileRunning_DoesNotRunOperationTwice`,
`AsyncRelayCommand_Cancel_Running_SignalsTokenAndIsNotAnError`,
`RepoCardViewModel_ApplySelectedPackageAsync_Running_IsBusyIgnoresSecondCallAndCancelLeavesPinUnchanged`,
`MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_OtherCardsStillChecked`,
`SelectPackageWindowViewModel_LoadAsync_OneSourceUnreachable_OthersStillLoadAndErrorIsInline`, and
`SelectPackageWindowViewModel_Retry_SourceFailedThenAnswers_LoadsOnlyThatSource`,
covering `AgentControl-LauncherUI-ResponsiveOperations`.
