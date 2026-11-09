## LauncherUI

### Verification Approach

The `LauncherUI` subsystem is verified by the combined unit tests of its ten units
(`MainWindowViewModelTests.cs` and `RepoGroupingTests.cs`, `RepoCardViewModelTests.cs`, `RepoCardServerSourceTests.cs`
and `RepoCardImageTests.cs`,
`SelectPackageWindowViewModelTests.cs` and `SelectPackageRetryTests.cs`,
`SettingsWindowViewModelTests.cs`, `SettingsWindowGitSourceTests.cs`, `SourceEditorViewModelTests.cs`,
`SourceEditorServerViewModelTests.cs`, `SourceEditorSignatureViewModelTests.cs` and `SourceEditorGitViewModelTests.cs`,
`RepoCardGitAwarenessTests.cs` (the Git icon, tooltip, moved tag and Change package of the card) and
`PackageSourceGitPresentationTests.cs` (the Git icon and names, and how a source row classifies a remote Git source),
`CredentialPromptViewModelTests.cs`, `RepoImageLocatorTests.cs`, `RepoGroupHeaderViewModelTests.cs`,
`GroupNameViewModelTests.cs` and `DialogBehaviorTests.cs`, the last four units each with its own verification
document), plus
`AsyncRelayCommandTests.cs` for the shared asynchronous
command, which together
exercise the full recent-repos/card/select-package/settings/sign-in interaction surface without ever
constructing an Avalonia `Window` (apart from the picture decoder and `DialogBehavior`, which need Avalonia's headless
session). The windows themselves are exercised by headless Avalonia tests
(`DialogWindowTests.cs`, `MainWindowTests.cs`, `UsabilityWindowTests.cs` for the favorite star with real pointer
and key input, the "Check again" menu item, the up-to-date check mark, the narrow card, the Settings sections, the
keyboard behavior of the dialogs, the About version row and the order of the AGENTS.md question and the release
notes, `RepoGroupWindowTests.cs` for the group headers, the card menu's
"Move to group" submenu, the group-name prompt and the card color palette pop-up,
`SettingsWindowServerSourceTests.cs` for the Settings
window, the source editor window and its connection test, `SourceEditorSignatureWindowTests.cs` for the signature
section and "Forget saved sign-in" of the source editor, `SourceEditorGitWindowTests.cs` for the Git editor's
sections, tag pattern error, tag preview and Save (against a `FakeGitSourceProvider`, so no git or network is needed),
`RepoCardSignatureTests.cs` for the signature shield in the
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
element. `FavoriteIconConverter`, the favorite-star glyph's shape converter (its color is the theme resource
`FavoriteStarForeground`, checked for contrast by `ThemeResourceTests`), is likewise a visual-only value converter
with no bound text content to assert and remains a visual-inspection-only element;
`RepoCardViewModel.IsFavorite` itself
(the state it renders) is covered indirectly by the sort-order scenario below. The pure display helpers
(`AgentToolKindDisplayNameConverter`, `ThemePreferenceDisplayNameConverter` and the `PathEllipsisText` control) are
supporting types of the subsystem with their own test files (`AgentToolKindDisplayNameConverterTests.cs`,
`ThemePreferenceDisplayNameConverterTests.cs`, `PathEllipsisTextTests.cs`), as are `App` and the shared
`AppStyles.axaml` theme resources (`ThemeResourceTests.cs`); each is a presentational adapter with no behavior of its
own beyond formatting, measuring or applying a theme for the view, so no unit requirement exists for it and the
subsystem requirements cite its tests directly.

The evidence of a subsystem requirement is, in order of preference, a headless window test (the main window, the
dialogs, the shared styles), a test of a supporting type listed above, and - where a behavior is observable only in
the view-model layer the window is drawn from, with the real collaborators of the subsystem - the view-model tests of
the units that realize it. Unit-level behaviors that no subsystem-level test can show on their own (for example the
rules of `RepoImageLocator`) are carried by the unit requirements and their tests, and a subsystem scenario that cites
a unit test says so. The FlaUI tests are the evidence of the system requirements and are cited at this level only for
the Select Package dialog's keyboard behavior and the About dialog, which nothing else can exercise.

The server-source tests use scripted fakes at the subsystem boundary instead of a network: a
`FakePackageSourceProvider` (which can be gated to simulate a slow source and can be told to throw
a given exception, including a `PackageSourceAccessException` of each failure kind) stands behind the view
models, and `FakeCredentialPrompt` stands in for the sign-in dialog; the real `NuGetSourceProvider` against
a mock NuGet server is verified in the `AgentPackageManagement` subsystem. The headless window tests rely on
test-infrastructure properties that were corrected in this pass: `HeadlessTestApp.Run` and
`HeadlessTestApp.RunAsync` now wait for the body to finish (`Run` previously returned without waiting, so an assertion
failing inside a headless test never failed the test, and the asynchronous variant had the same defect before it),
the headless application now uses the real Skia drawing backend (so pictures are genuinely decoded and text is
measured, which the earlier stub backend could not do), and the `DialogWindowTests.Click` helper also executes a
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
- Repos are listed in collapsible sections once any repo is in a group (a flat list otherwise), the card menu and
  the group header offer moving, renaming and removing groups, a chosen card color is shown on the tile only, and
  the letters on every palette color have at least 4.5:1 contrast in both themes.
- A theme chosen in Settings takes effect on the running application and every text color of the shared styles has at
  least 4.5:1 contrast, and the status bars and the favorite star at least 3:1, in both themes; the contrast of every
  state of every control and the drawn keyboard focus ring are not asserted (the focus-visible state of the focused
  control is).
- A change the user makes is applied only when it was saved; when the save fails nothing changes and the user is told,
  and a removed repo's card reports nothing afterwards.
- Every dialog keeps Tab inside, starts with the focus on its first sensible control, cancels on Escape where it can
  and returns the focus to the window behind; the Select Package dialog's keyboard behavior is verified end to end.
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
- A Git repository can be added from the Add menu; its editor explains the address, tag pattern, folder and sign-in,
  validates as the user types, lists the matching tags (or the tags that did not match, with a hint) after Test
  connection and saves only an acceptable source; a Git source is shown with the Git icon, "Not checked" when remote;
  a card pinned to one names the tag and commit, shows "Tag moved" without applying anything when the tag moved, and
  offers Change package..., which opens on the pinned choice and asks before applying changed content.

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
raised for a repo that has no pin, the selection window populates versions descending and defaults to latest, confirming
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
covering `AgentControl-LauncherUI-AgentsMdTemplateOffer`. The order of the question and the release notes is the
scenario `LauncherUI_AgentsMdOfferBeforeReleaseNotes_QuestionFirstThenReleaseNotes` below.

**LauncherUI_Settings_SaveAndApplyPreservesRecentReposAndUpdatesFields**: The settings
dialog's `SaveCommand` invokes its `onSave` callback and raises `Saved` with the current
property values, and applying the resulting settings on `MainWindowViewModel` preserves the
existing recent-repos list while adopting the new field values. This scenario is tested by
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved` and
`MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields`, covering
`AgentControl-LauncherUI-Settings`.

**LauncherUI_About_EndToEndOnly_OpensAboutWindow**: The About dialog itself has no dedicated
view-model class, so its end-to-end rendering is verified via the FlaUI project: opening the
window over the main window and confirming the mission tagline, version, copyright, and license
text are all present and non-empty, and that the third-party dependency list control is present and
rendered with at least one entry. The application logo image is not automated-asserted (it has
no text content to check) and remains a visual-inspection-only element. The dependency list's
underlying data source (name/license entries matching the project file's package names, no
duplicates, no empty fields, and the expected display format) has direct unit coverage. This scenario is tested by
`windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright`,
`MainWindow_AboutClicked_OpensModalAboutOwnedByMainWindow`,
`ThirdPartyDependencies_All_MatchesCsprojDirectRuntimeDependencies`,
`ThirdPartyDependencies_All_NoDuplicateNames`,
`ThirdPartyDependencies_All_EveryEntryHasNonEmptyLicense`,
and `DependencyInfo_ToString_RepresentativeEntry_ReturnsNameLicenseFormat`, covering
`AgentControl-LauncherUI-About`. The labeled Version row is the scenario
`LauncherUI_AboutVersionRow_VersionIsLabeledShortAndFullInTooltip` below.

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

**LauncherUI_PackageSources_ListedWithKindLocationAndStatus**: The settings view model lists the configured
sources in order with their kind and location and whether their folder can be found, and a saved NuGet server
is listed as "Not checked" without any folder check or network access. This scenario is tested by
`SettingsWindowViewModel_Constructor_ListsConfiguredSourcesInOrder` and
`SettingsWindowViewModel_SaveServerSource_ListsItAsNotCheckedAndPersistsIt`, covering
`AgentControl-LauncherUI-PackageSources`.

**LauncherUI_AddPackageSource_ZipNuGetFolderAndServerThroughDialogs**: The settings window offers an add
command for each kind of source, each raising the editor request for a new source of that kind; saving the
editor adds and selects the zip source or keeps the package IDs of the NuGet folder source, a NuGet server is
added through its own button, which opens the server editor, and a valid saved definition carries a trimmed
location. This scenario is tested by
`SettingsWindowViewModel_AddCommands_RaiseEditorRequestedForNewSourceOfEachKind`,
`SettingsWindowViewModel_AddZipSource_EditorSaved_AddsAndSelectsSource`,
`SettingsWindowViewModel_AddNuGetFolderSource_EditorSaved_KeepsPackageIds`,
`SettingsWindowViewModel_AddNuGetServerSourceCommand_RequestsServerEditor`,
`SettingsWindow_AddNuGetServerSourceButton_OpensServerEditorWithServerControls` and
`SourceEditorViewModel_SaveCommand_ValidZipSource_RaisesSavedWithDefinition`, covering
`AgentControl-LauncherUI-AddPackageSource`.

**LauncherUI_PackageSourceValidation_OnlyAValidSourceCanBeSaved**: An alias that duplicates a configured
alias ignoring case, an invalid alias, an invalid or credential-bearing folder location, an invalid or
plain-http server address, and a NuGet source without a package ID each show an explanation and keep Save
disabled, and the save command does nothing while the editor is invalid. This scenario is tested by
`SettingsWindowViewModel_AddSource_AliasAlreadyConfiguredIgnoringCase_EditorCannotSave`,
`SourceEditorViewModel_Alias_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_Location_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_ServerLocation_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_NuGetSource_NoPackageIds_CannotSaveUntilOneIsAdded` and
`SourceEditorViewModel_SaveCommand_InvalidEditor_DoesNotRaiseSaved`, covering
`AgentControl-LauncherUI-PackageSourceValidation`.

**LauncherUI_PackageSourcePackageIds_AddRemoveAndRefuseBadIds**: A package ID can be added to a NuGet source
and saved with it, a listed ID can be removed, and an ID that is malformed or already listed (ignoring case)
is refused with a reason. This scenario is tested by
`SourceEditorViewModel_SaveCommand_ValidNuGetSource_SavesPackageIds`,
`SourceEditorViewModel_RemovePackageId_RemovesSelectedId` and
`SourceEditorViewModel_AddPackageId_InvalidOrDuplicate_IsRefused`, covering
`AgentControl-LauncherUI-PackageSourcePackageIds`.

**LauncherUI_ServerSourceEditor_AsksForFeedAddressAndShowsExamples**: The server editor window has the feed
address, the example addresses for JFrog Artifactory and Azure Artifacts and the server title. This scenario
is tested by `SettingsWindow_AddNuGetServerSourceButton_OpensServerEditorWithServerControls` and
`SourceEditorViewModel_Constructor_NewServer_HasServerTitleLabelAndExamples`, covering
`AgentControl-LauncherUI-ServerSourceEditor`.

**LauncherUI_InsecureHttpChoice_OffByDefaultAndWarnsWhenTicked**: "Allow insecure HTTP" is off in a new server
editor, and ticking it makes a plain-http address acceptable and shows the warning. This scenario is tested by
`SourceEditorViewModel_Constructor_NewServer_HasServerTitleLabelAndExamples` and
`SourceEditorViewModel_AllowInsecureHttp_Toggled_AcceptsHttpAndShowsWarning`, covering
`AgentControl-LauncherUI-InsecureHttpChoice`.

**LauncherUI_ServerSourceSettingsKept_SavedAndEditedServerKeepsItsSettings**: A valid server is saved with its
trimmed address, package IDs and insecure choice, and editing an existing server keeps it a server with its
settings. This scenario is tested by
`SourceEditorViewModel_SaveCommand_ValidServer_SavesAddressPackageIdsAndInsecureChoice` and
`SourceEditorViewModel_EditExistingServer_StaysAServerWithItsSettings`, covering
`AgentControl-LauncherUI-ServerSourceSettingsKept`.

**LauncherUI_EditPackageSource_ReplacedInPlaceKeepingAliasAndHiddenSettings**: Editing a source replaces it in
place with its alias fixed and the settings the dialog does not show preserved, without modifying the
definition the editor was given, and editing a server source opens the server editor wired to the connection
test. This scenario is tested by
`SettingsWindowViewModel_EditSource_EditorSaved_ReplacesSourceInPlaceKeepingAlias`,
`SettingsWindowViewModel_EditServerSource_OpensServerEditorThatTestsThroughTheFactory` and
`SourceEditorViewModel_EditExistingSource_AliasFixedAndOtherSettingsPreserved`, covering
`AgentControl-LauncherUI-EditPackageSource`.

**LauncherUI_RemovePackageSource_WarnsAboutPinnedReposThenRemoves**: Removing the selected source first asks
for confirmation, describing how many repos are pinned to it and that they will show a package source problem,
and then removes it. This scenario is tested by
`SettingsWindowViewModel_RemoveSourceCommand_RaisesRemoveRequestedForSelectedSource` and
`SettingsWindowViewModel_RemoveSource_WarnsAboutPinnedReposAndRemoves`, covering
`AgentControl-LauncherUI-RemovePackageSource`.

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

**LauncherUI_SignIn_DialogReturnsTheTypedCredentialOrNoneAndAnswersOnce**: Sign in closes the sign-in window with
the typed credential, while Cancel, Escape and closing the window all answer no credential;
`AvaloniaCredentialPrompt`, asked from a worker thread, returns the typed credential and reports a cancel as no
credential. This scenario is tested by `CredentialPromptWindow_SignInClicked_ReturnsTheTypedCredential`,
`CredentialPromptWindow_CancelEscapeOrClose_ReturnNoCredential`,
`AvaloniaCredentialPrompt_PromptAsync_FromWorkerThread_ShowsDialogAndReturnsCredential` and
`AvaloniaCredentialPrompt_PromptAsync_UserCancels_ReturnsNull`, covering `AgentControl-LauncherUI-SignIn`.

**LauncherUI_SignInDestination_DialogShowsTheServerOrigin**: The sign-in window, shown headlessly, displays the
server's origin (scheme, host and port). This scenario is tested by
`CredentialPromptWindow_Shown_ShowsOriginSessionNoteAndMaskedPassword`, covering
`AgentControl-LauncherUI-SignInDestination`.

**LauncherUI_SignInWarnings_PlainHttpAndRejectedCredential**: A plain-http server shows a warning and a
previously rejected credential shows a notice, and neither is shown otherwise. This scenario is tested by
`CredentialPromptWindow_InsecureAndRejected_ShowWarningAndRejectedNotice` (the not-shown case is asserted by
`CredentialPromptWindow_Shown_ShowsOriginSessionNoteAndMaskedPassword`), covering
`AgentControl-LauncherUI-SignInWarnings`.

**LauncherUI_SignInPasswordMasked_PasswordBoxMasksTypedText**: The password box of the sign-in window masks
what is typed and Sign in stays disabled until both fields are filled. This scenario is tested by
`CredentialPromptWindow_Shown_ShowsOriginSessionNoteAndMaskedPassword`, covering
`AgentControl-LauncherUI-SignInPasswordMasked`.

**LauncherUI_SignInSessionOnlyNote_DialogStatesTheCredentialIsNotSaved**: The sign-in window displays the
sentence "Used for this session only - AgentControl does not save it.". This scenario is tested by
`CredentialPromptWindow_Shown_ShowsOriginSessionNoteAndMaskedPassword`, covering
`AgentControl-LauncherUI-SignInSessionOnlyNote`.

**LauncherUI_SignInPlacement_ShownOverTheOwnerOneAtATime**: `AvaloniaCredentialPrompt`, asked from a worker
thread, shows the dialog on the UI thread as a window owned by the asking window, and shows two simultaneous
questions one at a time. This scenario is tested by
`AvaloniaCredentialPrompt_PromptAsync_FromWorkerThread_ShowsDialogAndReturnsCredential` and
`AvaloniaCredentialPrompt_PromptAsync_TwoQuestionsAtOnce_AreShownOneAtATime`, covering
`AgentControl-LauncherUI-SignInPlacement`.

**LauncherUI_SignInCancelsWithOperation_TokenCancellationClosesDialog**: Cancelling the question's token closes
the open dialog and throws a cancellation. This scenario is tested by
`AvaloniaCredentialPrompt_PromptAsync_TokenCancelled_ClosesDialogAndThrows`, covering
`AgentControl-LauncherUI-SignInCancelsWithOperation`.

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
configured for the server, the button is disabled and its explanation is shown on hover; the button lives in the
editor's titled "Sign-in" section, not beside the connection test. This scenario is tested by
`SourceEditorWindow_ForgetSavedSignIn_ConfirmationDecidesAndResultIsShown`,
`SourceEditorWindow_ForgetSavedSignIn_NoCredentialStore_ButtonDisabledWithTooltip`,
`SourceEditorWindow_ForgetSavedSignIn_NoCredentialHelper_ButtonDisabledWithTooltip` and
`SourceEditorWindow_Sections_ServerEditorHasTitledSectionsAndForgetBelongsToSignIn`, covering
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

**LauncherUI_ResponsiveOperations_SlowSourcesNeverFreezeTheWindow**: Upgrade checks and package applies against
a slow source return control to the caller, the Select Package dialog shows progress without blocking while a
package's versions load, and constructing the main window view model reads no source until the background
upgrade refresh begins. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatusAsync_SourceSlow_DoesNotBlockAndShowsChecking`,
`RepoCardViewModel_ApplySelectedPackageAsync_SourceSlow_ReturnsToCallerBeforeSourceAnswers`,
`SelectPackageWindowViewModel_SelectPackageName_VersionsSlow_ShowsProgressWithoutBlocking` and
`MainWindowViewModel_Constructor_DoesNotReadSourcesUntilUpgradeRefreshBegins`, covering
`AgentControl-LauncherUI-ResponsiveOperations`.

**LauncherUI_OperationProgress_BusyCardDisablesOtherActions**: A card applying a package reports that it is
busy and disables its other actions until the operation ends, and the Select Package dialog shows that it is
loading. This scenario is tested by
`RepoCardViewModel_ApplySelectedPackageAsync_Running_ReportsBusyAndDisablesOtherActions` and
`SelectPackageWindowViewModel_SelectPackageName_VersionsSlow_ShowsProgressWithoutBlocking`, covering
`AgentControl-LauncherUI-OperationProgress`.

**LauncherUI_OneOperationPerRepo_RepeatedRequestIsIgnored**: The asynchronous command ignores a repeated
request while running, and a second package apply for the same card does not read the source again. This
scenario is tested by `AsyncRelayCommand_ExecuteAsync_WhileRunning_DoesNotRunOperationTwice` and
`RepoCardViewModel_ApplySelectedPackageAsync_SecondCallWhileRunning_IsIgnored`, covering
`AgentControl-LauncherUI-OneOperationPerRepo`.

**LauncherUI_CancelOperation_CancellationIsNotAnErrorAndLeavesPinUnchanged**: The asynchronous command treats
cancellation as no error, and cancelling a package apply that is waiting on a slow source raises no error and
leaves the pin unchanged. This scenario is tested by
`AsyncRelayCommand_Cancel_Running_SignalsTokenAndIsNotAnError` and
`RepoCardViewModel_ApplySelectedPackageAsync_CancelledWhileRunning_ReportsNoErrorAndKeepsPinUnchanged`,
covering `AgentControl-LauncherUI-CancelOperation`.

**LauncherUI_SourceFailureIsolation_OneUnreachableSourceAffectsOnlyItself**: The background upgrade refresh
keeps checking other cards when one source is unreachable, and the Select Package dialog loads the other
sources and shows an inline error for the unreachable one. This scenario is tested by
`MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_OtherCardsStillChecked` and
`SelectPackageWindowViewModel_LoadAsync_OneSourceUnreachable_OthersStillLoadAndErrorIsInline`, covering
`AgentControl-LauncherUI-SourceFailureIsolation`.

**LauncherUI_RetryFailedRead_OnlyTheFailedReadIsRepeated**: A read that failed can be retried on its own
without touching the other sources, and a retry that still fails keeps the error and the retry offer. This
scenario is tested by `SelectPackageWindowViewModel_Retry_SourceFailedThenAnswers_LoadsOnlyThatSource` and
`SelectPackageWindowViewModel_Retry_StillFailing_KeepsErrorAndRetryAvailable`, covering
`AgentControl-LauncherUI-RetryFailedRead`.

**LauncherUI_StaleResultsDiscarded_SupersededReadNeverOverwritesNewerState**: When a newer upgrade check starts
the older check's late answer is discarded, and a slower earlier version load is superseded by a later package
selection. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatusAsync_NewerCheckStarted_OlderResultIsDiscarded` and
`SelectPackageWindowViewModel_ChangeSelectedPackageName_SupersedesSlowerEarlierLoad`, covering
`AgentControl-LauncherUI-StaleResultsDiscarded` (children also: `AgentControl-RepoCardViewModel-StaleUpgradeCheckDiscarded`
and `AgentControl-RepoCardViewModel-UpgradeStateResetOnRefresh`, whose unit tests are in the `RepoCardViewModel`
verification design: an answer that arrives after the pin changed or the repo became missing is not published).

**LauncherUI_CardIdentityAndStatus_AccentIsTheCardStatusInColorAndWords**: The status the accent bar shows (up to
date, upgrade available, a problem - a missing folder, a source problem, a failed action - no agent package, or
unknown) and its words and reason are view-model facts observable only in the layer the card is drawn from; the
status of a pin whose configured sources are invalid is a problem and never "up to date", and a failed action's
problem is cleared when the next operation starts. This scenario is tested by
`RepoCardViewModel_Status_ReflectsPackageAndUpgradeState` and
`RepoCardViewModel_Status_MissingFolderOrSourceProblem_IsProblemWithReason`, covering
`AgentControl-LauncherUI-CardIdentityAndStatus` (children: `AgentControl-RepoCardViewModel-StatusAccent`,
`AgentControl-RepoCardViewModel-StatusWords`, `AgentControl-RepoCardViewModel-StatusProblemCleared`,
`AgentControl-RepoCardViewModel-InvalidSourceConfigurationIsProblem`). The colors of the bars are checked for contrast
in the theme scenarios below, and the real application keeps the accent's accessible status text unchanged by the
card color (system verification).

**LauncherUI_CardAvatarTile_InitialsFolderGlyphAndStableColor**: The tile's letters are the first letters of the
first two words of the repo name, a name with no letter or digit gives no letters (so the folder symbol is shown),
and the tile color index of a path is the same every time, whatever the letter case of the path, and always inside the
palette. This scenario is tested by `RepoCardViewModel_AvatarInitials_RepoName_UsesFirstLettersOfFirstTwoWords`,
`RepoCardViewModel_Avatar_EmptyName_FallsBackToFolderGlyph` and
`RepoCardViewModel_AvatarColorIndex_SamePath_IsStableAndInPalette`, covering
`AgentControl-LauncherUI-CardAvatarTile`.

**LauncherUI_RepoPicture_RepoOwnPictureReplacesTheTileWhenUsable**: A headless main window shows, for a repo with a
root `icon.png`, the picture in the avatar tile (image visible, initials hidden) and, for a repo without one, the
initials; a picture that is removed leaves the generated tile; and a picture whose decoding fails or throws keeps the
tile and is not retried. This scenario is tested by `MainWindow_RepoCard_AvatarShowsRepoPictureWhenPresent`,
`RepoCardViewModel_RepoImage_FileRemoved_FallsBackToInitials` and
`RepoCardViewModel_RepoImage_DecodeFailsOrThrows_KeepsTileAndDoesNotRetry`, covering
`AgentControl-LauncherUI-RepoPicture` (children: the `RepoPicture*` requirements of `RepoCardViewModel` and the
`AgentControl-RepoImageLocator-*` requirements that find, decode and identify a picture).

**LauncherUI_RepoPictureTrusted_OnlyGenuineSmallPlainPngOfTheRepoRootIsShown**: The rules of which picture is
acceptable are the `RepoImageLocator` unit's and are verified there, one by one and on every platform that has the
link type. At the subsystem boundary the card is shown to use the real locator (a repo's picture is loaded once and
again only when the file changes), and the refusal of a symbolic link and of a junction is repeated by the unit tests
on the platform that has them: Windows (junction), Ubuntu and macOS (symbolic link). This scenario
is tested by `RepoCardViewModel_RepoImage_IconPresent_LoadsOnceUntilFileChanges`,
`windows@RepoImageLocator_Find_RootIsJunction_ReturnsNull`,
`ubuntu@RepoImageLocator_Find_SymbolicLinkIcon_IsRefused` and
`macos@RepoImageLocator_Find_SymbolicLinkIcon_IsRefused`, covering
`AgentControl-LauncherUI-RepoPictureTrusted` (children: the other `AgentControl-RepoImageLocator-*` requirements).
The `LauncherUI` subsystem has no end-to-end or window-level test that offers a hostile picture; that is verified at
the unit level, which this scenario discloses rather than hides.

**LauncherUI_AddSourceMenu_OneAddButtonOffersEverySourceType**: The Settings window has one `AddSourceButton`
whose menu holds the zip-package folder, NuGet-package folder, NuGet-package server and Git repository items (each
with an icon) under their stable AutomationIds, in that order; choosing an item opens the matching editor (zip: no
package IDs section; NuGet folder: with it, and cancelling it adds nothing; server: feed-address controls; Git: the
Git sections), and saving the zip editor adds and
selects the source. This scenario is tested by `SettingsWindow_AddZipSourceClicked_ShowsEditorAndSavingAddsSource`,
`SettingsWindow_AddNuGetFolderClickedThenCancelled_AddsNothing`,
`SettingsWindow_AddNuGetServerSourceButton_OpensServerEditorWithServerControls` and
`SettingsWindow_AddGitSourceButton_IsInTheAddMenuAndOpensGitEditor`, covering
`AgentControl-LauncherUI-AddSourceMenu` (children: `AgentControl-SettingsWindowViewModel-AddSource` and
`-AddGitSourceMenu`, the latter shown by `SettingsWindowViewModel_AddGitSourceCommand_RequestsGitEditor`). The icons and
descriptions of all four items are asserted end to end by
`windows@SettingsWindow_AddMenu_OffersAllFourSourceTypesWithIconAndDescription` (system verification, which also checks
the empty-list text names the four types), and the Git item's description, the kind name and status of a saved Git
source in the list and the persisted settings by
`windows@SettingsWindow_AddGitRepositoryViaDialog_TestsConnectionListsTagsAndPersistsIt`.

**LauncherUI_Appearance_ThemeIsChosenInItsOwnSettingsSection**: The Settings view model offers the system default,
light and dark choices in that order, starts from the saved theme (the system default when none was saved) and
passes the chosen theme to its owner on save; the Settings window shows the Appearance heading with the Theme picker
under it, above the Tools heading and the git, agent tool and shell settings; and the display names of the choices
read as words and an unknown value reads as empty. This scenario is tested by
`SettingsWindow_Layout_AppearanceIsItsOwnSectionBeforeTools` (headless),
`ThemePreferenceDisplayNameConverter_Convert_KnownTheme_ReturnsFriendlyName` and
`ThemePreferenceDisplayNameConverter_Convert_NotATheme_ReturnsEmpty`, covering
`AgentControl-LauncherUI-Appearance` (children: `AgentControl-SettingsWindowViewModel-Appearance`,
`AgentControl-SettingsWindowViewModel-AppearanceStartsFromSaved`,
`AgentControl-SettingsWindowViewModel-AppearanceSaved`; the view model's tests are in its unit verification design).
`ThemePreferenceDisplayNameConverter` is a supporting type of this subsystem, so its tests are cited here and not
by a unit requirement.

**LauncherUI_ThemeApplied_ChoiceTakesEffectOnTheRunningApplication**: Each theme choice maps to the matching Avalonia
variant (system default to `ThemeVariant.Default`, which follows the operating system) and applying a choice changes
the running application's requested and actual theme, so every window follows it; settings that were refused are
not applied (a unit fact of `MainWindowViewModel`). This scenario is tested by
`App_ToThemeVariant_MapsEachPreferenceToItsVariant` and `App_ApplyTheme_SwitchesRunningApplicationTheme`, covering
`AgentControl-LauncherUI-ThemeApplied` (children: `AgentControl-MainWindowViewModel-ApplyTheme`,
`AgentControl-MainWindowViewModel-RefusedThemeNotApplied`). That the real application re-themes its windows without
a restart is the system scenario `AgentControl_Appearance_ChoosingLightThenDarkReThemesWithoutRestart`.

**LauncherUI_ThemeTextLegible_EveryTextColorHasContrastInBothThemes**: The light and the dark dictionary of
`AppStyles.axaml` define exactly the same keys, and in both themes every text color of the shared styles (the five
chip palettes in every state, the secondary button in every state, the disabled primary button, the ghost icon
buttons, validation, caution and success text on the page and on a card, the group header text and chevron on the
page, the empty-state tile and all ten avatar letter colors) has at least 4.5:1 contrast against the fill and
surface it is drawn on. What is measured is the pairs of colors the shared styles define; the rendered windows are
measured end to end (system verification, `AgentControl-System-WindowsLegible`), and the contrast of every state of
every control and the keyboard focus ring are not claimed. This scenario is tested by
`AppStyles_LightAndDarkDictionaries_DefineTheSameKeys` and
`AppStyles_TextAndGraphicColors_MeetContrastInBothThemes`, covering `AgentControl-LauncherUI-ThemeTextLegible`.

**LauncherUI_ThemeGraphicsLegible_StatusBarsAndFavoriteStarHaveContrastInBothThemes**: In both themes the status
accents and the favorite star have at least 3:1 contrast against the card they are drawn on. This scenario is tested
by `AppStyles_TextAndGraphicColors_MeetContrastInBothThemes`, covering
`AgentControl-LauncherUI-ThemeGraphicsLegible`.

**LauncherUI_AppBar_LogoFilterAndButtonsInOrderAndNothingClipsAtMinimumWidth**: In the headless main window at its
narrowest width the app bar holds the logo (accessible name and tooltip "AgentControl", no text or tagline in
the bar), the filter box and the Add, Settings and About buttons from left to right; the first four keyboard stops
are the filter box and then those three buttons; the About button ends inside the window, no button is narrower
than it needs, and the filter box has shrunk but is still wide enough to use. The bar is a view without a unit of its
own, so this headless test is its evidence. This scenario is tested by
`MainWindow_AppBar_LogoFilterAndButtonsInOrderAndNothingClipsAtMinimumWidth`, covering
`AgentControl-LauncherUI-AppBar` (content, accessible name and keyboard order) and
`AgentControl-LauncherUI-AppBarNarrow` (the shrinking of the filter box and the buttons that are not clipped).

**LauncherUI_FavoriteStar_RealClickAndKeyToggleOncePerPressBothWays**: In the headless main window a
real mouse click (a button press and release at the star's center) on an unfavorited repo's star favorites it

- the star is on, the repo is listed under a Favorites section (which appears) and the choice is stored - and a
second click unfavorites it again (back in its own section); the Space key toggles once per press, off to on and
on to off, and the keyboard focus stays on the star although the card moves between sections (after the mouse
click too: the star gets the focus back the way it had it); the star has no
command (a command that also toggled flipped the state twice per click) and the accessible name "Favorite". In the
real application the same moves happen through the UI Automation toggle pattern, and the choice is stored in the
settings file (system verification). This scenario is tested by
`MainWindow_FavoriteStar_MouseClick_FavoritesThenUnfavoritesMovingBetweenSections` and
`MainWindow_FavoriteStar_SpaceKey_TogglesOncePerPress`, covering `AgentControl-LauncherUI-FavoriteStar` (one change
per click or key press, accessible name; child: `AgentControl-RepoCardViewModel-Favorite`),
`AgentControl-LauncherUI-FavoriteStarMovesRepo` (the move to the Favorites section and back; the mouse test) and
`AgentControl-LauncherUI-FavoriteStarKeepsFocus` (the keyboard focus stays on the star; the Space test).

**LauncherUI_MissingRepoRecheck_CheckAgainIsEnabledAndFindsTheFolder**: The menu of a repo whose folder is
missing offers its refresh item enabled and labeled "Check again" with the tooltip "Check whether the folder is
available again" (Git Pull stays disabled), and the menu of a present repo says "Refresh". Refreshing a repo whose
folder has come back stops reporting it as missing and makes its actions available again. This scenario is tested by
`MainWindow_MissingRepoMenu_OffersEnabledCheckAgain_PresentRepoOffersRefresh`, covering
`AgentControl-LauncherUI-MissingRepoRecheck`, and
`RepoCardViewModel_RefreshCommand_MissingFolderReturns_ClearsMissingAndRestoresActions`, covering
`AgentControl-LauncherUI-MissingRepoRecheckClears`. In the real application, creating the missing folder and
choosing the item clears the "Folder missing" badge (system verification).

**LauncherUI_UpToDateCue_CheckMarkOnlyOnAnUpToDateCard**: With three repos (pinned to the newest version, pinned to an
older version, no package) exactly one "Agent files are up to date" check mark is visible, inside the package chip of
the up-to-date card, with that text as its tooltip and accessible name. This scenario is tested by
`MainWindow_UpToDateCue_ShownOnlyForPinnedAndUpToDateCard`, covering `AgentControl-LauncherUI-UpToDateCue`. In the
real application the check mark is found on a card whose pinned package is the only version, and not on one with an
upgrade badge (system verification).

**LauncherUI_LongTextShortened_PathsKeepTheFolderNameAndNothingCollides**: In a window at the narrowest width, a long
repo name ends in an ellipsis with the whole name as its tooltip and never reaches the Launch button, and the path is
shortened with the folder part giving way first and a very long folder name keeping its start and its end around a
middle ellipsis, with the whole path as the tooltip and accessible name. `PathEllipsisText`, a supporting type of
this subsystem, is checked without a window: its middle and end ellipses keep the right characters and never split a
surrogate pair, a path that fits is drawn whole (also when the width grows back), the folder part gives way before a
folder name that fits, a folder name that does not fit keeps both ends and the result fits the width, and a missing
path draws nothing. This scenario is tested by
`MainWindow_NarrowWidth_NameEllipsisPathKeepsFolderNameAndNothingCollides` and the `PathEllipsisText_*` tests of
`PathEllipsisTextTests.cs`, covering `AgentControl-LauncherUI-LongTextShortened` (child:
`AgentControl-RepoCardViewModel-RepoPathKeepsFolderName`).

**LauncherUI_UpgradeFromChip_ChipAndMenuItemShareOneUpgrade**: The upgrade chip and the card menu's Upgrade item run
the same command, so the chip has the same effect and the same availability (only while an upgrade is available and
no other operation is running); the chip's tooltip names both versions and says that it upgrades the agent files to
the new version. This scenario is tested by
`RepoCardViewModel_UpgradeCommand_ChipAndMenuShareOneGuardedCommand` and
`RepoCardViewModel_UpgradeChip_NewerVersionAtSource_ShowsTargetVersionAndTooltipWithBothVersions`, covering
`AgentControl-LauncherUI-UpgradeFromChip` (children: `AgentControl-RepoCardViewModel-UpgradeFromChip`,
`AgentControl-RepoCardViewModel-UpgradeChipAvailability`, `AgentControl-RepoCardViewModel-UpgradeChipTooltip`). The
click on the chip in the real application is the system scenario `AgentControl_UpgradeFromChip_ClickStartsTheUpgrade`.

**LauncherUI_DialogModal_EveryDialogIsShownModallyOverTheMainWindow**: About, opened from the headless main window,
is a window owned by the main window and is gone when closed; Settings, the confirmation, the message box, the
group-name prompt, the source editor and the sign-in prompt are opened over their owner and answer when closed in the
dialog tests below. The dialogs are views without a unit of their own. This scenario is tested by
`MainWindow_AboutClicked_OpensModalAboutOwnedByMainWindow`,
`SettingsWindow_ShownModally_FocusStartsInsideTabCyclesInsideAndReturnsToOwner`,
`Dialogs_ConfirmationMessageBoxAbout_InitialFocusTabCycleAndEscape` and
`Dialogs_PromptsAndEditors_InitialFocusInFirstFieldAndEscapeCancels`, covering
`AgentControl-LauncherUI-DialogModal`. That the operating system really disables the window behind Settings and About,
and that clicks and keys do not reach it, is verified end to end (system verification).

**LauncherUI_DialogKeyboard_TabStaysInsideTheDialog**: Settings shown as a modal dialog over a window that has the
focus keeps the focus inside the dialog through more Tab presses than the dialog has stops (the window behind never
gets it; the last control, Save, is reached and the focus wraps to the first) and wraps backwards with Shift+Tab; the
confirmation (Tab and Shift+Tab cycle between No and Yes) and the other small dialogs do the same. The Select
Package dialog is the one dialog whose keyboard behavior is verified end to end only, because it is opened from a
repo card with real package sources: its focus starts inside, Tab and Shift+Tab cycle inside it and never reach the
window behind. This scenario is tested by `SettingsWindow_ShownModally_FocusStartsInsideTabCyclesInsideAndReturnsToOwner`,
`Dialogs_ConfirmationMessageBoxAbout_InitialFocusTabCycleAndEscape` and
`windows@SelectPackageWindow_Opened_FocusStartsInsideTabCyclesAndEscapeCancels`, covering
`AgentControl-LauncherUI-DialogKeyboard` (children: `AgentControl-DialogBehavior-TabStaysInside`,
`AgentControl-DialogBehavior-RequiresDialog`; the keyboard behavior is verified in isolation by the `DialogBehavior`
unit's tests).

**LauncherUI_DialogInitialFocus_FocusStartsOnTheFirstSensibleControlWithFocusRing**: Settings shown as a modal dialog
puts the focus on its Add button and marks it as focused by the keyboard (the focus-visible state that the focus ring
style applies to); the confirmation opens with the focus on No, the message box on OK, About on Close, and the
group-name prompt, the source editor and the sign-in prompt on their first field; the Select Package dialog starts
with the focus inside (end to end). The focus ring is asserted as that state and not as pixels; it cannot be seen
through UI Automation, so it is not claimed end to end, and the `DialogBehavior` unit tests verify the keyboard
navigation method and the choice of the starting control, including a disabled or hidden named control and an
explicit tab order. This scenario is tested by
`SettingsWindow_ShownModally_FocusStartsInsideTabCyclesInsideAndReturnsToOwner`,
`Dialogs_ConfirmationMessageBoxAbout_InitialFocusTabCycleAndEscape`,
`Dialogs_PromptsAndEditors_InitialFocusInFirstFieldAndEscapeCancels` and
`windows@SelectPackageWindow_Opened_FocusStartsInsideTabCyclesAndEscapeCancels`, covering
`AgentControl-LauncherUI-DialogInitialFocus` (child: `AgentControl-DialogBehavior-InitialFocus`).

**LauncherUI_DialogEscapeCancels_EscapeAnswersCancelWhereThereIsOne**: Escape answers No in the confirmation and
closes the message box and About, and cancels the group-name prompt, the source editor and the sign-in prompt; the
Select Package dialog is canceled by Escape end to end, pinning nothing. This scenario is tested by
`Dialogs_ConfirmationMessageBoxAbout_InitialFocusTabCycleAndEscape`,
`Dialogs_PromptsAndEditors_InitialFocusInFirstFieldAndEscapeCancels` and
`windows@SelectPackageWindow_Opened_FocusStartsInsideTabCyclesAndEscapeCancels`, covering
`AgentControl-LauncherUI-DialogEscapeCancels` (child: `AgentControl-DialogBehavior-EscapeCancels`).

**LauncherUI_DialogFocusReturn_FocusReturnsToTheWindowBehind**: When Settings closes, the focus returns to where it
was in the main window; the Select Package dialog, canceled by Escape, hands the focus back to the main window end to
end. This scenario is tested by `SettingsWindow_ShownModally_FocusStartsInsideTabCyclesInsideAndReturnsToOwner` and
`windows@SelectPackageWindow_Opened_FocusStartsInsideTabCyclesAndEscapeCancels`, covering
`AgentControl-LauncherUI-DialogFocusReturn`.

**LauncherUI_RepoGroups_HeadersCollapseMenuAndPromptWorkInTheWindow**: Headless main-window tests
(`RepoGroupWindowTests.cs`, `RealProcess` collection) show that grouped repos are listed under headers
(Favorites, the group, Ungrouped) whose accessible names state the section, its count and whether it is
expanded, with a tooltip each and a "..." menu on the user's own group only; that a list without groups has
no header; that invoking a header collapses and expands its section (the cards disappear and return, the
accessible name changes and the state is remembered); that the card menu's "Move to group" submenu lists the
groups (the repo's own marked), "New group..." and, for a grouped repo only, "Remove from group", each with an
AutomationId and a tooltip, and that choosing a group moves the repo; that "New group..." opens the name
prompt, which refuses an unusable name and creates the group with a usable one (the header appears) or changes
nothing when canceled; that the header's menu renames a group through the prompt and removes it after a
confirmation (declining keeps it) leaving its repos listed without headers; that a favorite in a group shows
the group chip and another repo does not; and that the prompt window validates while typing and reports the
name. The requirements are split by behavior; each is covered by the tests that exercise it:

- `AgentControl-LauncherUI-RepoGroups` (sections, order, headers with name and count, a favorite listed once, with
  its group on its card): `MainWindow_GroupedRepos_ShowHeadersWithNameCountAndState` and
  `MainWindow_FavoriteInGroup_ShowsGroupChip`.
- `AgentControl-LauncherUI-RepoGroupsFlatList` (no header while no repo is in a group):
  `MainWindow_NoGroups_ShowsNoHeader`.
- `AgentControl-LauncherUI-RepoGroupMove` (the card menu's move into an existing group, a new group or out of the
  group): `MainWindow_CardMenu_MoveToGroupSubmenu_ListsChoicesAndMovesRepo` and
  `MainWindow_NewGroupMenuItem_PromptsForNameAndCreatesGroup`.
- `AgentControl-LauncherUI-GroupNamePrompt` (the prompt validates the typed name and can be canceled):
  `GroupNameWindow_ValidatesWhileTyping_AndAccepts` and `MainWindow_NewGroupPrompt_Canceled_ChangesNothing`.
- `AgentControl-LauncherUI-RepoGroupManagement` (rename and remove from the header):
  `MainWindow_HeaderMenu_RenameAndRemoveGroup`.
- `AgentControl-LauncherUI-RepoGroupCollapse` (collapse and expand from the header):
  `MainWindow_HeaderToggle_CollapsesAndExpandsSection`.

The behaviors that are observable only in the view-model layer the window is drawn from are covered by the
view-model tests of the units that realize them, cited at this level as the subsystem's convention (see the
Verification Approach): `MainWindowViewModel_ToggleGroupCollapsed_HidesCardsAndPersists` and
`MainWindowViewModel_ApplySettings_KeepsCollapsedGroups` for
`AgentControl-LauncherUI-RepoGroupCollapseRemembered` (the sections the user collapsed are still collapsed at the
next start, and when other settings change);
`MainWindowViewModel_FilterText_ShowsMatchesInCollapsedGroupsAndHidesEmptyOnes` and
`MainWindowViewModel_FilterText_NoMatch_ShowsNothing` for `AgentControl-LauncherUI-RepoGroupFilter` (the filter
shows every match, also in a collapsed section, with the count of matches only, no header for a section without a
match and the collapse toggles disabled and explained meanwhile); and
`RepoCardViewModel_GroupAndColor_NeverWriteToTheRepository` for `AgentControl-LauncherUI-RepoGroupsSettingsOnly`
(groups, colors and collapsed sections are written to the user's settings only, never into the repository). The
end-to-end checks of the same behavior are the system scenarios (`AgentControl-System-RepoGroups` and the
requirements next to it).

**LauncherUI_ChangesSaved_ChangeIsAppliedOnlyWhenSavedAndAFailedSaveChangesNothing**: Every change the user makes to
a repo or a section (favorite, group, card color, launch time, collapse, rename or removal of a group, adding or
removing a repo) is applied and written when the save succeeds; when the save fails - the tests put a directory
where the settings file belongs - the live settings, the cards, the displayed list and the file stay as they were, a
later unrelated save
does not write the failed change, the user is told in a message, and the operations that return to a caller (collapse,
rename, remove a group, add and remove a repo) refuse by reporting the failure. In the headless main window a
settings change that cannot be saved shows the message and keeps the state as it was. This scenario is tested by
`MainWindowViewModel_ChangesThatSave_AreAppliedAndWritten` and
`RepoCardViewModel_Changes_GoThroughTheOwnerAndAreNotAppliedWhenItRefuses`, covering
`AgentControl-LauncherUI-ChangeAppliedWhenSaved`; by `MainWindow_SettingsCannotBeSaved_ShowsMessageAndKeepsStateAsItWas`
and `MainWindowViewModel_FavoriteSaveFails_ChangesNothingAndLaterSaveDoesNotPersistIt`, covering
`AgentControl-LauncherUI-FailedSaveChangesNothing`; and by
`MainWindow_SettingsCannotBeSaved_ShowsMessageAndKeepsStateAsItWas`, covering
`AgentControl-LauncherUI-FailedSaveReported`. The unit tests of each operation are in
`MainWindowViewModelPersistenceTests.cs` (see the `MainWindowViewModel` verification design).

**LauncherUI_RemovedRepoGoesQuiet_NothingIsReportedForARemovedCard**: When a repo is removed (after the removal was
saved) its card is retired: a running upgrade check publishes nothing, a running operation is canceled and reports
nothing, and the card checks nothing when asked to refresh; the window releases the handlers it had on the card. This
scenario is tested by `MainWindow_RepoRemoved_ReleasesTheCardHandlers` and
`MainWindowViewModel_RemoveRepo_RetiresTheCard`, covering `AgentControl-LauncherUI-RemovedRepoGoesQuiet`.

**LauncherUI_CardColor_PalettePopupColorsTheTileAndKeepsLettersReadable**: A headless main window opens the
palette pop-up from the card menu's "Card color..." item and finds a swatch for every palette color (each with
an AutomationId, and its name as accessible name and tooltip) and the Automatic button; choosing Teal gives the
card that color, puts the matching palette class and the custom-color class on the tile and closes the pop-up;
reopening it marks Teal (and not Blue) as chosen, and its heading names the chosen color in words; choosing Automatic
returns the tile to its path-derived palette class. This scenario is tested by
`MainWindow_CardColorPalette_ChoosingSwatchColorsTileAndAutomaticClears` and
`MainWindow_CardColorPalette_HeadingNamesTheChosenColor`, covering `AgentControl-LauncherUI-CardColor` (children: the
`CardColor` requirements of `RepoCardViewModel`). That choosing a color leaves the status unchanged is tested by
`RepoCardViewModel_SetCardColor_DoesNotChangeStatus`, covering `AgentControl-LauncherUI-CardColorKeepsStatus`. For
every palette color and in both themes the letters on the fill are the better of white and near-black and have at
least 4.5:1 contrast, tested by `AppStyles_TileLetters_AreTheBetterOfLightAndDarkTextOnEveryPaletteColor`, covering
`AgentControl-LauncherUI-CardColorLettersLegible`.

**LauncherUI_PackageSourceLocationTooltip_WholeLocationIsTheTooltip**: The location box of the source editor offers
the whole entered folder or feed address as its tooltip, and none when it is empty. This scenario is tested by
`SourceEditorViewModel_Location_TooltipShowsWholeValueAndIsNullWhenEmpty`, covering
`AgentControl-LauncherUI-PackageSourceLocationTooltip`.

**LauncherUI_AgentsMdOfferBeforeReleaseNotes_QuestionFirstThenReleaseNotes**: After an upgrade with an AGENTS.md
offer only the modal question is open in the headless main window (the release-notes window is not), and answering
it opens the release-notes window in front; an upgrade without an offer opens the release notes at once. This
scenario is tested by `MainWindow_UpgradeWithAgentsMdOffer_ShowsQuestionFirstThenReleaseNotes` and
`MainWindow_UpgradeWithoutAgentsMdOffer_ShowsReleaseNotesAtOnce`, covering
`AgentControl-LauncherUI-AgentsMdOfferBeforeReleaseNotes`. The same ordering through UI Automation in the real
application is the system scenario `AgentControl_AgentsMdOfferBeforeReleaseNotes_QuestionComesFirstThenReleaseNotes`.

**LauncherUI_AboutVersionRow_VersionIsLabeledShortAndFullInTooltip**: The About window's Version row is labeled, shows
the version without its build metadata, carries the full build version as its tooltip, and the shortening drops only
the metadata. This scenario is tested by `AboutWindow_VersionRow_IsLabeledAndShortWithFullVersionInTooltip`, covering
`AgentControl-LauncherUI-AboutVersionRow`.

**LauncherUI_ContentChangeProtection_RepublishedVersionIsNeverAppliedUnnoticed**: A package version republished with
other content is not applied silently: an Upgrade and the restore of missing agent files before launch refuse it and
say so, and a Select Package of the pinned version raises the confirmation request. In the main window the request
becomes a modal confirmation whose answer No changes nothing (the repo and the pin keep what they had) and whose
answer Yes applies the changed package and records its new identity; a pin without an identity adopts one silently.
This scenario is verified by the `RepoCardViewModel` unit tests (see their unit verification design), with
`RepoCardViewModel_ApplySelectedPackage_FirstApply_RecordsZipSha256InPin`,
`RepoCardViewModel_UpgradeCommand_SameVersionRepublished_RefusesWithoutChangingRepoOrPin`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SameVersionRepublished_RefusesWithoutExtractingOrWritingPin`,
`RepoCardViewModel_ApplySelectedPackage_SameVersionRepublished_RequestsConfirmationAndChangesNothing` and
`RepoCardViewModel_ApplySelectedPackage_PinWithoutIdentity_AdoptsIdentityWithoutWarning` cited directly at the
subsystem level, and by the headless window test
`MainWindow_SelectingRepublishedPinnedVersion_AsksFirstAndAppliesOnlyWhenConfirmed` (the question is asked before
anything changes; No changes nothing; Yes applies and records the new identity). The requirements are covered as
follows: `AgentControl-LauncherUI-ContentChangeProtection` (the identity is recorded; child
`AgentControl-RepoCardViewModel-RecordContentIdentity`) by the first test; `AgentControl-LauncherUI-ContentChangeRefusal`
(children: `AgentControl-RepoCardViewModel-RefuseChangedContent`, `-RefuseChangedContentMessage` and
`-LaunchSyncNeverWritesPin`) by the Upgrade and the launch-restore tests;
`AgentControl-LauncherUI-ContentChangeConfirmation` (children: `-ConfirmChangedContent`, `-ApplyConfirmedContent` and
`-RefuseChangeWithoutAsking`) by the confirmation-request test and the window test; and
`AgentControl-LauncherUI-ContentChangeAdoption` (children: `-AdoptMissingIdentity` and `-IgnoreMissingSourceIdentity`) by
the pin-without-identity test. As the evidence rules above say, these are view-model tests of the units that realize the
behavior, except the headless window test: the behavior is observable only in the view-model layer the window is drawn
from, so no window-level test exists for the refusal.
The card offers "Change package..." to a repo that has a pin (and "Select Package..." only to one that has none), so the
confirmation is reached through the card's menu: the Change package scenario below shows the dialog opening on the pinned
choice, and the end-to-end scenarios of the system verification (`AgentControl_PackageContentIntegrity_MovedGitTagIs...`)
take it through to the confirmation with a real Git repository.

**LauncherUI_GitSourceSetup_PromptOnlyForUserStartedWork**: The registry the card builds for Git sources runs git with
the configured git executable (or the one the owner supplies) and caches Git packages inside the isolated configuration
directory; the registry for work the user started lets git show its credential UI, and the registry for background work
never does; when the background check finds that the installed git is older than the supported version the
card's source problem says so and does not ask the user to sign in, and no upgrade is offered. This scenario is tested by
`RepoCardViewModel_CreateSourceRegistry_GitSource_RunsTheConfiguredOrOverriddenGit` (covering
`AgentControl-LauncherUI-GitSourceSetup`, children `AgentControl-RepoCardViewModel-GitSourceOptions` and
`-GitSourceCacheRoot`), `RepoCardViewModel_CreateSourceRegistry_GitSource_PromptsOnlyForUserStartedWork` (covering
`AgentControl-LauncherUI-GitSourcePrompts`, child `AgentControl-RepoCardViewModel-GitSourcePrompts`) and
`RepoCardViewModel_RefreshUpgradeStatus_GitTooOld_ShowsSourceProblemWithoutSignIn` (covering
`AgentControl-LauncherUI-GitTooOld`, children `AgentControl-RepoCardViewModel-GitTooOldProblem` and
`-GitTooOldNoUpgrade`). These are view-model tests of the unit that realizes the behavior; the same prompt rule is shown
end to end against a private Git server in the system verification (`GitSignInTests.cs`).

**LauncherUI_GitSourceEditor_AddressPatternPreviewAndSave**: The Git editor window shows the Git sections (the package
name box with the alias as its placeholder, the tag pattern box with `v{version}` and its help, the folder box, the
sign-in note, the connection test and the Git examples under a "Repository URL" label) and hides the NuGet package
list, the signature section and the server sign-in section; a tag pattern without the version placeholder shows its
error under the box and disables Save, and fixing it removes both; clicking Test connection with a provider that
answers lists the matching tags in the preview panel with their summary; and Save closes the window with the Git
definition (address, pattern, folder, package name). This scenario is tested by
`SourceEditorWindow_GitSource_ShowsGitSectionsAndHidesNuGetOnes`,
`SourceEditorWindow_GitSource_BadTagPattern_ShowsErrorAndDisablesSave`,
`SourceEditorWindow_GitSource_TestConnection_ShowsTagPreviewList` and
`SourceEditorWindow_GitSource_Save_ClosesWithTheGitDefinition` (the windows, against a `FakeGitSourceProvider`, no git or
network), and the settings view model's refusal of a Git source whose settings the application would not accept is
shown by
`SettingsWindowViewModel_Save_GitSourceWithBadTagPattern_IsRefusedWithErrorMessage` (unit evidence cited directly), covering
`AgentControl-LauncherUI-GitSourceEditor` (children: `AgentControl-SourceEditorViewModel-GitEditor`,
`-GitEditorDefaults`, `-GitSubfolderNormalized`, `-GitSaveNew`, `-GitSaveExisting`, `-GitSaveGate`, `-GitValidation`,
`-GitValidationTagPattern`, `-GitValidationSubfolder`, `-GitValidationPackageName`, `-GitTagPatternHelp`,
`AgentControl-SettingsWindowViewModel-RefuseInvalidGitSource`, `-RefuseInvalidGitAddress`, `-RefuseInvalidGitSubfolder`
and `-RefuseInvalidGitPackageName`) with the first, second and fourth window test, and
`AgentControl-LauncherUI-GitSourceConnectionTest` (children: `-GitTestConnection`, `-GitTestRuns`,
`-GitTestDefaultProvider`, `-GitTestFailureReasons`, `-GitTestGitTooOld`, `-GitTestOtherFailure`, `-GitTestCancel`,
`-GitTestCancelAndStale`, `-GitTagPreview`, `-GitTagPreviewNoMatch`, `-GitTagPreviewNoTags`, `-GitTagPreviewCleared`,
each prefixed `AgentControl-SourceEditorViewModel`, and `AgentControl-MainWindowViewModel-GitConnectionTestProvider`,
`-GitConnectionTestInteractive` and `-GitConnectionTestRefusesOldGit`) with the third window test.
The real provider, the real git and the connection test against a repository are shown end to end in the system
verification.

**LauncherUI_GitSignInGuidance_EditorSaysWhoSignsIn**: The Git editor window shows the sign-in note under its own
section: AgentControl uses the user's own Git sign-in, never stores credentials, may show Git's sign-in for the
connection test and Select Package, and never prompts in the background. This scenario is tested by
`SourceEditorWindow_GitSource_ShowsGitSectionsAndHidesNuGetOnes`, covering `AgentControl-LauncherUI-GitSignInGuidance`
(child: `AgentControl-SourceEditorViewModel-GitSignInNote`).

**LauncherUI_GitSourcePresentation_IconTypeNameStatusTagAndCommit**: A Git source has the Git icon and the type name "Git
repository" (and the short name "git repository") in the lists that show sources; the Select Package source list shows
the same icon and name; a Git source on a remote address is listed "Not checked" and never asks for its folder, while one
on a local path gets the folder status, and the classification of the zip, NuGet folder and server kinds is unchanged;
and a card pinned to a Git source shows the Git icon and the alias in its package chip. This scenario is tested by
`PackageSourceIcons_GitSource_HasGitIconAndNames`, `SelectPackageWindowViewModel_Constructor_GitSource_ShowsGitIconAndTypeName`
and `RepoCardViewModel_PinnedGitSource_ShowsGitIconAndAlias` (covering `AgentControl-LauncherUI-GitSourcePresentation`,
child `AgentControl-RepoCardViewModel-GitSourceAwareness`),
`PackageSourceItemViewModel_RemoteGitSource_IsNotCheckedAndNeverAsksTheFolder`,
`PackageSourceItemViewModel_LocalGitSource_ChecksTheFolder` and
`PackageSourceItemViewModel_OtherKinds_RemoteClassificationUnchanged` (covering
`AgentControl-LauncherUI-GitSourceRowStatus`, children `AgentControl-SettingsWindowViewModel-GitSourceRowStatus`,
`-GitSourceLocalFolderStatus` and `-GitSourcePersisted`) and
`RepoCardViewModel_PackageChipTooltip_GitPinWithCommit_NamesTagAndShortCommit` (the tag and the seven-character commit
in the chip's tooltip, covering `AgentControl-LauncherUI-GitSourceCardTooltip`, child
`AgentControl-RepoCardViewModel-GitSourceTooltip`).
The drawn icon is shown only as ink in the end-to-end test (`SettingsWindow_AddMenu_OffersAllFourSourceTypes...`).

**LauncherUI_TagMovedWarning_ShownNeverApplied**: When a check finds the pinned Git version at another
commit than the pin recorded, the card's status is "Tag moved" (it outranks "upgrade available", hides the up-to-date
cue and uses the tooltip that names the tag, both commits and Change package...), and nothing is acquired, extracted or
written; a card refresh the user starts reads the Git source again, so a tag moved after a listing was cached is shown,
while a failed refresh clears the warning instead of leaving it up. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatus_TagMoved_ShowsTagMovedStatusAndTooltip` (covering
`AgentControl-LauncherUI-TagMovedWarning`, children `AgentControl-RepoCardViewModel-TagMovedWarning`,
`-TagMovedNeedsEvidence`, `-TagMovedClearedByNewPin` and `-TagMovedFollowsTagPattern`),
`RepoCardViewModel_RefreshUpgradeStatus_TagMoved_AppliesNothing` (covering `AgentControl-LauncherUI-TagMovedAppliesNothing`,
child `-TagMovedAppliesNothing`),
`RepoCardViewModel_RefreshAsync_GitSourceTagMovedAfterCachedListing_ListsAgainAndShowsTagMoved` (covering
`AgentControl-LauncherUI-TagMovedFreshListing`, child `-TagMovedFreshListing`) and
`RepoCardViewModel_RefreshAsync_FailsAfterTagMoved_ClearsTagMovedAndShowsCheckFailed` (covering
`AgentControl-LauncherUI-TagMovedClearedWhenUnknown`, child `-TagMovedClearedWhenUnknown`; each child prefixed
`AgentControl-RepoCardViewModel`). The chip itself is exercised end to end only
(`TagMovedChip` in the system verification); no headless window test renders it.

**LauncherUI_ChangePackage_PinnedRepoChoosesAgainOnTheCurrentChoice**: For a repo that has a pin the select-package
command can execute and raises the request ("Change package..."); the main window opens the Select Package dialog on the
pin's own source, package and version although a newer version exists; and choosing the pinned version again when its
content changed asks for confirmation first, No changing nothing and Yes applying and recording the new identity. This
scenario is tested by `RepoCardViewModel_SelectPackageCommand_PinAlreadyExists_CanExecuteAsChangePackage` (covering
`AgentControl-LauncherUI-ChangePackage`, children `AgentControl-RepoCardViewModel-ChangePackage` and
`-ChangePackageRequest`), `MainWindow_ChangePackageOnPinnedRepo_OpensSelectPackageOnThePinnedChoice` (also covering
`AgentControl-LauncherUI-ChangePackageStartsOnCurrentChoice`, children: the `AgentControl-SelectPackageWindowViewModel-Preselect*`
requirements: `-PreselectCurrentPackage`, `-PreselectNewestWhenVersionGone`, `-PreselectNothingWhenSourceFailed`,
`-PreselectNothingWhenNotOffered`, `-PreselectNothingWhenSourceUnknown`, `-PreselectKeepsUserChoice` and
`-PreselectCancelled`) and `MainWindow_SelectingRepublishedPinnedVersion_AsksFirstAndAppliesOnlyWhenConfirmed` (the
confirmation, covered above).
