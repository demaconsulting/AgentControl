### MainWindowViewModel

#### Verification Approach

`MainWindowViewModel` is verified with unit tests defined in `MainWindowViewModelTests.cs`, `RepoGroupingTests.cs`
(sections, collapse, filter, rename and removal of groups) and `MainWindowViewModelPersistenceTests.cs` (every change
the user makes when the settings file cannot be written).
Each test constructs a real `MainWindowViewModel` against a unique temporary configuration
directory (via `SettingsStore`) and unique temporary repo directories, so tests are hermetic
and can run in parallel with each other, subject to the `RealProcess` xUnit collection (which
disables parallelization) since constructing a view model with recent repos spawns real git
subprocesses via `GitStub` or the system git executable as part of each card's eager
`RefreshCheap()`. No Avalonia `Window` is constructed; only the view-model class itself is
under test.

#### Test Environment

N/A - standard test environment; `RealProcess` collection applies (see Verification Approach).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Repo cards are created for every recent repo recorded in settings on construction.
- Add/remove operations correctly mutate both in-memory collections and persisted settings.
- Search filtering and sort ordering produce the documented result for every tested input.
- Applying updated settings preserves the recent-repos list and invalidates the shared
  package-version cache.
- Applying settings drops cached package results only for sources that were edited or removed.
- Constructing the view model reads no package source; upgrade availability is checked in the
  background, and an unreachable source does not stop the other sources from offering their upgrades.
- The connection-test provider shares the application's session credentials with the cards, and for a Git source is a
  Git provider that is interactive and runs the configured git after its version check.
- With no repo in a group the list is flat and has no header; with groups it is sectioned
  (Favorites, groups alphabetically, Ungrouped), a favorite is listed once, collapsed sections hide their
  cards but keep their headers, a filter shows every match (even in a collapsed group) and leaves the
  remembered state alone, and group changes are stored in the settings file only.
- The group-name prompt validates as the user types and completes exactly once.
- Every change the user makes (favorite, group, card color, recorded launch, collapse, group rename and removal,
  adding and removing a repo) is applied and written when the settings can be saved; when they cannot, nothing changes
  (settings, cards, displayed list, file), a later unrelated save does not write the failed change, the user is told
  (changes from a card or a header button) or the operation fails to its caller (the others).

#### Test Scenarios

**MainWindowViewModel_PopulateRecentRepos_ConstructorCreatesCardsAndSkipsEagerGitCheck**: The
constructor is called with settings containing recent repos; a card is created per repo, and
a git stub's invocation log confirms the working-tree dirty-check subprocess (`status
--porcelain`) is never invoked eagerly during construction (only the cheap checks run at
launch). This scenario is tested by
`MainWindowViewModel_Constructor_SettingsHaveRecentRepos_PopulatesRepoCards` and
`MainWindowViewModel_Constructor_DoesNotInvokeWorkingTreeDirtyCheckSubprocess`, covering
`AgentControl-MainWindowViewModel-PopulateRecentRepos`.

**MainWindowViewModel_AddRepo_ValidatesExistenceAndDuplication**: `AddRepo` is called with a
valid new existing path (inserted and persisted), a nonexistent path (rejected, returns
false), and an already-tracked path (rejected, returns false). This scenario is tested by
`MainWindowViewModel_AddRepo_ValidNewPath_InsertsCardAndPersistsSettings`,
`MainWindowViewModel_AddRepo_PathDoesNotExist_ReturnsFalse`, and
`MainWindowViewModel_AddRepo_DuplicatePath_ReturnsFalse`, covering
`AgentControl-MainWindowViewModel-AddRepo`.

**MainWindowViewModel_AddRepoToGroup_AddsOrMovesRepoIntoTheGroup**: `AddRepoToGroup` (tested in
`RepoGroupingTests.cs`) puts a new repo in the requested group - spelled as the existing group when only the letter case
differs - saves once and lists it in that group's section; moves the existing entry into the group when the repo is
already listed (still two cards, not three); returns "not added" for a folder that does not exist; opens a collapsed
target group; and refuses a blank or built-in section name with `ArgumentException`, adding nothing. This scenario is
tested by `MainWindowViewModel_AddRepoToGroup_NewRepo_AddsCardInTheGroupAndPersistsIt`,
`MainWindowViewModel_AddRepoToGroup_RepoAlreadyListed_MovesItIntoTheGroup`,
`MainWindowViewModel_AddRepoToGroup_FolderMissing_AddsNothing`,
`MainWindowViewModel_AddRepoToGroup_CollapsedGroup_ExpandsIt` and
`MainWindowViewModel_AddRepoToGroup_InvalidGroupName_ThrowsAndAddsNothing`, covering
`AgentControl-MainWindowViewModel-AddRepoToGroup`.

**MainWindowViewModel_RemoveRepo_RemovesAndPersistsOnlyOnExplicitCall**: `RemoveRepo` is
called directly, removing the card from both collections and persisting the update; a card's
own `RemoveCommand` (raising `RemoveRequested`) is confirmed to not remove the card by itself.
This scenario is tested by
`MainWindowViewModel_RemoveRepo_RemovesFromCollectionsAndPersistsSettings` and
`MainWindowViewModel_CardRaisesRemoveRequested_DoesNotRemoveCardWithoutConfirmation`, covering
`AgentControl-MainWindowViewModel-RemoveRepo`.

**MainWindowViewModel_SearchFilter_MatchesNameOrPathCaseInsensitively**: `FilterText` is set
to a mixed-case substring of a repo's display name, and separately to a substring of a repo's
full path; in each case only the matching card remains in `DisplayedRepoCards`. This scenario
is tested by
`MainWindowViewModel_FilterText_MatchesRepoNameCaseInsensitive_FiltersDisplayedRepoCards` and
`MainWindowViewModel_FilterText_MatchesRepoPath_FiltersDisplayedRepoCards`, covering
`AgentControl-MainWindowViewModel-SearchFilter`.

**MainWindowViewModel_EmptyState_DistinguishesNoReposFromNoFilterMatches**:
`HasNoDisplayedRepos`/`EmptyStateMessage` are checked with no recent repos configured at all
(message prompts adding a repo), with a tracked repo excluded by a non-matching filter
(message references the filter text), and with at least one displayed card
(`HasNoDisplayedRepos` is false). This scenario is tested by
`MainWindowViewModel_NoRepoCardsAtAll_HasNoDisplayedReposTrueWithAddRepoMessage`,
`MainWindowViewModel_FilterMatchesNoRepos_HasNoDisplayedReposTrueWithFilterMessage`, and
`MainWindowViewModel_HasDisplayedRepos_HasNoDisplayedReposFalse`, covering
`AgentControl-MainWindowViewModel-EmptyState`.

**MainWindowViewModel_SortOrder_FavoritesFirstThenRecencyWithNullsLast**: A favorite repo
launched longer ago sorts above a more-recently-launched non-favorite; among non-favorites, a
recently-launched repo sorts above an earlier-launched one, which sorts above a never-launched
repo. This scenario is tested by
`MainWindowViewModel_DisplayedRepoCards_SortsFavoritesAboveNonFavorites` and
`MainWindowViewModel_DisplayedRepoCards_SortsByLastLaunchedUtcDescendingWithNullsLast`,
covering `AgentControl-MainWindowViewModel-SortOrder`.

**MainWindowViewModel_ApplySettings_PreservesReposAndInvalidatesPackageCache**: `ApplySettings`
is called with an updated `AppSettings`; the existing recent-repos list survives, the new
field values are adopted and persisted, and the shared `PackageVersionCache` is invalidated so
a changed package-source path is reflected immediately rather than served from a stale cache.
A repo that reappeared since its last check gets an immediate fresh Git status (`RefreshCheap`
followed by `RefreshDirtyStatus`), rather than being left showing a stale or indefinitely
"not yet checked" Pull-disabled state. A present repo also gets an immediate fresh Git status
when `GitExecutablePath` itself changed, so a clean/dirty result cached against the previously
configured executable is not kept showing once a different executable is in effect. A repo
that was already present, already checked, and whose executable path did not change only gets
the cheap refresh, so re-saving settings does not re-run a synchronous `git status` for every
tracked repo. This scenario is tested by
`MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields`,
`MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache`,
`MainWindowViewModel_ApplySettings_RepoReappeared_RefreshesGitStatusImmediately`,
`MainWindowViewModel_ApplySettings_RepoAlreadyPresentAndChecked_DoesNotRecheckGitStatus`, and
`MainWindowViewModel_ApplySettings_GitExecutablePathChanged_RefreshesGitStatus`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_InvalidateChangedSources_OnlyEditedOrRemovedSourcesAreInvalidated**:
`ApplySettings` replaces one package source's location; a card whose files that unchanged source supplies
keeps its cached upgrade answer even after that source's folder has disappeared (an invalidated
cache would fail to read it and report no upgrade), while a changed source is observed afresh. This
scenario is tested by `MainWindowViewModel_ApplySettings_OnlyEditedSourceIsInvalidated` and
`MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache`, covering
`AgentControl-MainWindowViewModel-InvalidateChangedSources`.

**MainWindowViewModel_ApplySettings_CredentialBearingSource_IsRefused**: `ApplySettings` with a
package source location that embeds a sentinel password throws `InvalidOperationException` whose
message does not contain it, the live settings instance is unchanged, and no settings file is
written. This scenario is tested by
`MainWindowViewModel_ApplySettings_SourceLocationWithEmbeddedCredential_ThrowsAndKeepsCurrentSettings`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive**: `ApplySettings` against
a configuration location that cannot be written throws `InvalidOperationException`, and the live
settings instance is still the previous one with its previous values and recent repos. This
scenario is tested by `MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_BackgroundUpgradeCheck_ConstructionReadsNoSourceAndFailuresAreIsolated**:
Constructing the view model with a pinned repo reads no package source until the upgrade refresh
begins; when the refresh runs with one source unreachable, the other (readable) source still offers its upgrade
to the pinned cards; and adding a pinned repo checks its upgrade status in
the background. This scenario is tested by
`MainWindowViewModel_Constructor_DoesNotReadSourcesUntilUpgradeRefreshBegins`,
`MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_ReadableSourceStillOffersUpgrades`, and
`MainWindowViewModel_AddRepo_PinnedRepo_UpgradeCheckRunsInBackground`, covering
`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck`.

**MainWindowViewModel_ConnectionTestProvider_UsesTheSharedCredentialFacilities**: The view model hands out a
real `NuGetSourceProvider` for the settings window's connection test of a NuGet source,
built with the same credential context
(session store and prompt) as the cards, so a sign-in made during a test is remembered for the session. This
scenario is tested by `MainWindowViewModel_CreateConnectionTestProvider_ReturnsNuGetProviderWithCredentials`
(in `SettingsWindowServerSourceTests.cs`) and
`MainWindowViewModel_CreateConnectionTestProvider_NuGetSource_ReturnsNuGetProvider`,
covering `AgentControl-MainWindowViewModel-ConnectionTestProvider`.

**MainWindowViewModel_GitConnectionTestProvider_InteractiveGitWithTheConfiguredGit**: For a Git source the view model
hands out a real `GitSourceProvider` that runs the git named by the startup override (a recording stub stands in for git
here), asks for the git version before it lists the repository's tags (the version gate), and runs git as work the user
started - `GCM_INTERACTIVE` is not forced to `never` and `core.askPass` is not blanked, so Git's own sign-in may appear.
With a stub that reports an old git version (2.20.1) the provider's listing fails with a `GitRemoteException` of kind
`GitTooOld` ("Git 2.29 or newer") and the stub saw only the version query, never `ls-remote`.
This scenario is tested by `MainWindowViewModel_CreateConnectionTestProvider_GitSource_RunsTheConfiguredGitExecutable`,
`MainWindowViewModel_CreateConnectionTestProvider_GitSource_ReturnsInteractiveGitProvider` and
`MainWindowViewModel_CreateConnectionTestProvider_GitSource_OldGit_IsRefusedBeforeAnyTagIsListed`,
covering `AgentControl-MainWindowViewModel-GitConnectionTestProvider`, `-GitConnectionTestInteractive` and
`-GitConnectionTestRefusesOldGit`.

**MainWindowViewModel_ApplyTheme_SavedThemeIsHandedToTheApplierOnlyAfterTheSave**: With a recording theme
applier, `ApplySettings` with the dark theme persists it and calls the applier once with it, covering
`AgentControl-MainWindowViewModel-ApplyTheme`; when the write fails (the configuration "directory" is a file) the
exception reaches the caller, the applier is not called and the live settings keep the system default, covering
`AgentControl-MainWindowViewModel-RefusedThemeNotApplied`. This scenario is tested by
`MainWindowViewModel_ApplySettings_ThemeChanged_PersistsAndAppliesTheme` and
`MainWindowViewModel_ApplySettings_SaveFails_DoesNotApplyTheme`.

**MainWindowViewModel_GroupSections_FlatListWithoutGroupsSectionedListWithThem**: Tested by
`RepoGroupingTests.cs` (same `RealProcess` collection and temporary-directory approach, with repos
created under a unique parent so the card name is the folder name). Settings with no group give a flat
list without any header, favorites first (`AgentControl-MainWindowViewModel-FlatListWithoutGroups`, tested by
`MainWindowViewModel_DisplayedItems_NoGroups_FlatListWithoutHeaders`). Settings with groups give Favorites first,
the groups alphabetically ignoring letter case, then Ungrouped; names that differ only in letter case form one
section (`AgentControl-MainWindowViewModel-GroupSections`, tested by
`MainWindowViewModel_DisplayedItems_Groups_AreSectionedAndOrdered` and
`MainWindowViewModel_Constructor_NamesDifferingInCase_FormOneSection`). Each section has a header with its count
and a section without a repo has none (`AgentControl-MainWindowViewModel-GroupSectionHeaders`, tested by
`MainWindowViewModel_DisplayedItems_Groups_AreSectionedAndOrdered` and
`MainWindowViewModel_DisplayedItems_EmptySections_HaveNoHeader`); repos within a section are in order of most recent
launch, never-launched last (`AgentControl-MainWindowViewModel-GroupSectionRepoOrder`, tested by
`MainWindowViewModel_DisplayedItems_Groups_AreSectionedAndOrdered`). A favorite is listed only under Favorites, and
back in its group when it is no longer a favorite (`AgentControl-MainWindowViewModel-FavoriteListedOnce`, tested by
`MainWindowViewModel_Favorite_IsListedOnlyUnderFavoritesAndKeepsItsGroup`). Settings built in memory with a null
collapsed list, blank, reserved, overlong or look-alike group names load without failing
(`AgentControl-MainWindowViewModel-DamagedGroupSettings`, tested by
`MainWindowViewModel_Constructor_NullAndInvalidOrganizingSettings_FallBack` and
`MainWindowViewModel_Constructor_NamesDifferingInCase_FormOneSection`). The header rows themselves are verified by
the `RepoGroupHeaderViewModel` unit.

**MainWindowViewModel_GroupCollapse_HidesCardsAndIsRemembered**: Collapsing a group leaves its header (with
its count) and no cards, and expanding forgets it (letter case ignored); Favorites and Ungrouped collapse the same
way; a list of only collapsed sections still shows its headers; toggling a section keeps the header and the
unchanged cards as the same objects with only the hidden cards removed. This scenario is tested by
`MainWindowViewModel_ToggleGroupCollapsed_HidesCardsAndPersists`,
`MainWindowViewModel_ToggleGroupCollapsed_BuiltInSections_CollapseAndPersist`,
`MainWindowViewModel_AllGroupsCollapsed_HeadersRemainAndListIsNotEmpty` and
`MainWindowViewModel_ToggleGroupCollapsed_KeepsHeaderAndUnchangedCardsInPlace`, covering
`AgentControl-MainWindowViewModel-GroupCollapse`. The collapsed name is written to the settings file and a view
model built from those settings shows the section collapsed
(`MainWindowViewModel_ToggleGroupCollapsed_HidesCardsAndPersists` and
`MainWindowViewModel_ToggleGroupCollapsed_BuiltInSections_CollapseAndPersist`, covering
`AgentControl-MainWindowViewModel-GroupCollapseRemembered`), and applying other settings keeps the collapsed names
(`MainWindowViewModel_ApplySettings_KeepsCollapsedGroups`, covering
`AgentControl-MainWindowViewModel-GroupCollapseKeptOnApplySettings`). The header's toggle command is verified by the
`RepoGroupHeaderViewModel` unit.

**MainWindowViewModel_GroupFilter_ShowsEveryMatchEvenInCollapsedGroups**: With a group collapsed, a filter
shows its matching repos expanded (`AgentControl-MainWindowViewModel-GroupFilter`), counts only the matches in the
headers (`-GroupFilterCounts`), omits the sections without one (`-GroupFilterOmitsEmpty`), disables the toggles
(`-GroupFilterToggleDisabled`) and leaves the file's collapsed names unchanged, so clearing the filter brings the
collapsed state back (`-GroupFilterKeepsCollapsedState`); a filter that matches nothing shows no header at all. This
scenario is tested by `MainWindowViewModel_FilterText_ShowsMatchesInCollapsedGroupsAndHidesEmptyOnes` (covering all
five requirements) and `MainWindowViewModel_FilterText_NoMatch_ShowsNothing` (covering `-GroupFilter` and
`-GroupFilterOmitsEmpty`); each requirement id here is prefixed `AgentControl-MainWindowViewModel`.

**MainWindowViewModel_GroupManagement_RenameRemoveAndNothingWrittenToRepos**: Renaming moves every member
and the collapsed entry and is persisted (`AgentControl-MainWindowViewModel-GroupManagement`), accepts a change of
letter case only, and is refused for an unknown group, a blank, reserved or overlong name and another group's name
(`AgentControl-MainWindowViewModel-GroupRenameRules`); both are tested by
`MainWindowViewModel_RenameGroup_MovesMembersAndRefusesBadNames`. Removing a group leaves its repos ungrouped and
forgets its collapsed entry (`MainWindowViewModel_RemoveGroup_LeavesMembersUngrouped`, covering
`AgentControl-MainWindowViewModel-GroupRemove`). A header's rename and remove commands raise the view model's requests
with the group's name and change nothing by themselves
(`MainWindowViewModel_HeaderCommands_RaiseGroupRequestsWithTheGroupName`, covering
`AgentControl-MainWindowViewModel-GroupHeaderRequests`); and after moving, coloring, collapsing and renaming
the repository folder is still empty while the settings file holds the choices
(`RepoCardViewModel_GroupAndColor_NeverWriteToTheRepository`, covering
`AgentControl-MainWindowViewModel-GroupSettingsOnly`). The prompt that asks for a group's name is verified by the
`GroupNameViewModel` unit.

**MainWindowViewModel_ChangesThatSave_AreAppliedAndWritten**: A favorite, a group and a card color chosen on a card
are applied and read back from the settings file in the stored (canonical) spelling, and no failure is reported. This
scenario is tested by `MainWindowViewModel_ChangesThatSave_AreAppliedAndWritten`, covering
`AgentControl-MainWindowViewModel-ChangeAppliedWhenSaved`.

**MainWindowViewModel_FailedSave_ChangesNothing**: Tested by `MainWindowViewModelPersistenceTests.cs` (the
`RealProcess` collection, because every card's eager refresh spawns git). A failing save is produced by making
`settings.json` in the configuration folder a directory, which no write can replace; deleting that directory makes
saving work again. With saving broken, each change is made and nothing is checked to have changed: a favorite (card,
settings and displayed list as before), a move to a group, a card color, a recorded launch (the stamp stays unset and
no launch is recorded), a collapse (through the API, and through the header's button), a group rename and a group
removal (the group, its members and its collapsed state as before), adding a repo (nothing added to the cards, the
displayed list or the settings) and removing a repo (still listed, the settings unchanged). This scenario is tested by
`MainWindowViewModel_FavoriteSaveFails_ChangesNothingAndLaterSaveDoesNotPersistIt`,
`MainWindowViewModel_MoveToGroupSaveFails_ChangesNothingAndReports`,
`MainWindowViewModel_CardColorSaveFails_ChangesNothingAndReports`,
`MainWindowViewModel_LaunchRecordSaveFails_StampNotRecordedAndReports`,
`MainWindowViewModel_CollapseSaveFails_SectionStaysExpanded`,
`MainWindowViewModel_RenameGroupSaveFails_GroupUnchanged`,
`MainWindowViewModel_RemoveGroupSaveFails_GroupUnchanged`, `MainWindowViewModel_AddRepoSaveFails_NothingAdded` and
`MainWindowViewModel_RemoveRepoSaveFails_RepoStaysListedAndWorking`, covering
`AgentControl-MainWindowViewModel-FailedSaveChangesNothing`.

**MainWindowViewModel_FailedSave_IsNotPersistedLater**: After a failed favorite, saving is made to work and an
unrelated change (a card color on another repo) is saved: the file holds the color but not the favorite, and the
same favorite then succeeds. After a failed add the same repo can be added, and after a failed removal the card still
works (a favorite can be set on it) and can then be removed. This scenario is tested by
`MainWindowViewModel_FavoriteSaveFails_ChangesNothingAndLaterSaveDoesNotPersistIt`,
`MainWindowViewModel_AddRepoSaveFails_NothingAdded` and
`MainWindowViewModel_RemoveRepoSaveFails_RepoStaysListedAndWorking`, covering
`AgentControl-MainWindowViewModel-FailedSaveNotPersistedLater`.

**MainWindowViewModel_FailedSave_IsReported**: A failed favorite, group move, card color, recorded launch and collapse
from the header's button each raise the save-failed notice exactly once, with a message saying the change was not
applied (the favorite test also checks that the two-way bound star is told to read its value again). This scenario is
tested by `MainWindowViewModel_FavoriteSaveFails_ChangesNothingAndLaterSaveDoesNotPersistIt`,
`MainWindowViewModel_MoveToGroupSaveFails_ChangesNothingAndReports`,
`MainWindowViewModel_CardColorSaveFails_ChangesNothingAndReports`,
`MainWindowViewModel_LaunchRecordSaveFails_StampNotRecordedAndReports` and
`MainWindowViewModel_CollapseSaveFails_SectionStaysExpanded`, covering
`AgentControl-MainWindowViewModel-SaveFailureReported`.

**MainWindowViewModel_FailedSave_RefusesTheOperation**: Collapsing through the API, renaming a group, removing a
group, adding a repo and removing a repo with saving broken each throw to the caller. This scenario is tested by
`MainWindowViewModel_CollapseSaveFails_SectionStaysExpanded`,
`MainWindowViewModel_RenameGroupSaveFails_GroupUnchanged`, `MainWindowViewModel_RemoveGroupSaveFails_GroupUnchanged`,
`MainWindowViewModel_AddRepoSaveFails_NothingAdded` and
`MainWindowViewModel_RemoveRepoSaveFails_RepoStaysListedAndWorking`, covering
`AgentControl-MainWindowViewModel-SaveFailureRefusesOperation`. How the window shows that failure is verified at the
`LauncherUI` subsystem level.

**MainWindowViewModel_RemoveRepo_RetiresTheCard**: A repo's card is asked to report an error (its select-package
action with no package source configured) and does; the repo is removed and the same action is tried again: nothing is
reported for the removed card. What a retired card does with work that is still running is verified by the
`RepoCardViewModel` unit, which owns `Retire`. This scenario is tested by
`MainWindowViewModel_RemoveRepo_RetiresTheCard`, covering
`AgentControl-MainWindowViewModel-RemoveRepoRetiresCard`.
