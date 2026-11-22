### MainWindowViewModel

#### Verification Approach

`MainWindowViewModel` is verified with unit tests defined in `MainWindowViewModelTests.cs` (including adding one folder
or several and the Git-repository check), `RepoGroupingTests.cs` (sections, collapse, filter, rename and removal of
groups, group colors), `RepoOrderingTests.cs` (the user's own order: stability, new repos, the legacy seed, Move, drag
and drop, filtering, persistence, and the order of the groups), `MainWindowViewModelPersistenceTests.cs` (every change
the user makes when the settings file cannot be written, including the Retry/Discard question of a background add) and
`MainWindowViewModelRecheckTests.cs` (the periodic upgrade re-check, on a fake clock). The background add and the
background card refresh after a settings save are verified through the view model's two constructor seams rather than
timing: `backgroundWork` takes a `GatedBackgroundWorkRunner` (a test helper that counts every piece of work and holds it
until the test releases it), so a test asserts what holds while the folders are still being checked - the add has
returned to its caller, nothing is added, the global progress shows the count - and `saveSettings` takes a delegate that
counts saves, can hold them at a gate and can fail on demand, so "one save per batch", Cancel, Retry, Discard and a
change made while a save is held are deterministic. Every save runs through the view model's background settings writer
on a worker thread, so a test that reads the settings file after a change first awaits `FlushSettingsAsync` (which
writes what is waiting and completes when the writes have ended), and the persistence tests create their view model with
no synchronization context current, so a failed background write is reported before that flush completes; two of them
use a `FakeTimeProvider` so a change stays waiting until the test flushes. Tests that apply settings or add in the
background await `PendingRefresh`, the apply's task or the add's task before asserting. Each test constructs a real
`MainWindowViewModel` against a unique temporary configuration directory (via `SettingsStore`) and unique temporary repo
directories, so tests are hermetic and can run in parallel with each other, subject to the `RealProcess` xUnit
collection (which disables parallelization) since constructing a view model with recent repos starts background git
reads through the shared refresh queue, which spawn real git subprocesses via `GitStub` or the system git executable.
The temporary repo folders that tests add are made the top folder of a Git repository as far as the add is concerned by
a `.git` folder whose `HEAD` names a branch (`ref: refs/heads/main`), so the real `GitClient.IsRepositoryRoot` accepts
them and the branch is read without starting git; the tests of the Git check create folders without `.git`, with a
`.git` file, and subfolders. No Avalonia `Window` is constructed; only the view-model class itself is under test.

#### Test Environment

N/A - standard test environment; `RealProcess` collection applies (see Verification Approach).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Repo cards are created for every recent repo recorded in settings on construction.
- Add/remove operations correctly mutate both in-memory collections and persisted settings.
- Only the top folder of a Git repository (a `.git` folder or file directly in it) is added, by every add path; a
  subfolder, a plain folder and a file are refused as not a Git repository, a missing path as missing, and repos already
  in the list are kept whatever their folders hold.
- Adding several folders reports one outcome per distinct folder, in order, goes on past a folder that is refused,
  saves the whole batch once (all or nothing: when that save fails nothing is added and every folder that would have
  been added or moved is reported as not saved) and opens a collapsed target group in that save; the message for the
  user is nothing when everything was
  added, the single-add message for one folder, and one summary by kind of outcome (at most five names per line) for
  several.
- A background add returns to its caller while its folders are checked, adds nothing until all are checked, reports
  its progress (0 of the total first, then each checked folder) in the global progress and clears it when it ends,
  re-checks the list when it commits (a repo listed meanwhile is not added twice), does not check the working tree of a
  new repo, waits for an add already running, stops with nothing saved or added when canceled, and on a failed save asks
  Retry (which adds the batch) or Discard (which, like no one to ask, leaves memory, the cards, the list and the file -
  including repos the add would have moved - as saved).
- Applying settings returns before the cards' background refresh has read anything, shows "Refreshing
  repositories..." without Cancel while it runs and clears it when done.
- Search filtering and the user's own ordering (favorites first, then the stored order, never launch time) produce the
  documented result for every tested input.
- A launch, a refresh and applying settings never reorder the cards; a new repo goes to the end; settings without a
  stored order are arranged once as favorites first and then by recency, and a stored order is kept as it is.
- Move and drop reorder within a section (and between sections, setting group or favorite as the menu and the star do),
  refuse what does not apply, change nothing when saving fails, are off while the filter has text, and the order is
  saved and survives applying settings and removing a repo.
- Applying updated settings preserves the repo list and its order and invalidates the shared
  package-version cache.
- Applying settings drops cached package results only for sources that were edited or removed.
- Constructing the view model reads no package source; upgrade availability is checked in the
  background, and an unreachable source does not stop the other sources from offering their upgrades.
- While the application runs the cards' upgrade availability is checked again every five minutes (never asking for a
  sign-in, skipping cards that are busy, missing or without a pin, reading each source once per round - also a
  source that cannot be read, which is read again by the next round - never overlapping rounds) until the view
  model is disposed.
- The connection-test provider shares the application's session credentials with the cards, and for a Git source is a
  Git provider that is interactive and runs the configured git after its version check.
- With no repo in a group the list is flat and has no header; with groups it is sectioned
  (Favorites, the groups in the user's order - alphabetically until the user orders them - then Ungrouped), a favorite
  is listed once, collapsed sections hide their
  cards but keep their headers, a filter shows every match (even in a collapsed group) and leaves the
  remembered state alone, and group changes are stored in the settings file only.
- A group's color is stored in the settings only, shown on its header and on the tiles of its repos without a color of
  their own, refused for an unknown color, an unknown group or a built-in section, follows a rename, and is forgotten
  with the group's position when the group is removed or loses its last repo; applying settings keeps both.
- Groups move up and down and are dropped before or after another group, the full order of the groups is stored and
  read by the next start, and a move or drop that does not apply (an end, a built-in section, a group without a
  section, the group itself, while the filter has text) is refused with its reason, changing nothing.
- The group-name prompt validates as the user types and completes exactly once.
- Every change the user makes (favorite, group, card color, group color, group order, recorded launch, collapse, group
  rename and removal,
  adding and removing a repo) is applied and written when the settings can be saved; when they cannot, nothing changes
  (settings, cards, displayed list, file), a later unrelated save does not write the failed change, the user is told
  (changes from a card or a header button) or the operation fails to its caller (the others).

#### Test Scenarios

**MainWindowViewModel_PopulateRecentRepos_ConstructorCreatesCardsAndSkipsEagerGitCheck**: The
constructor is called with settings containing recent repos; a card is created per repo, and
a git stub's invocation log confirms the working-tree dirty-check subprocess (`status
--porcelain`) is never invoked during construction (only the cheap facts are read, in the
background, at launch). This scenario is tested by
`MainWindowViewModel_Constructor_SettingsHaveRecentRepos_PopulatesRepoCards` and
`MainWindowViewModel_Constructor_DoesNotInvokeWorkingTreeDirtyCheckSubprocess`, covering
`AgentControl-MainWindowViewModel-PopulateRecentRepos`.

**MainWindowViewModel_AddRepo_ValidatesExistenceDuplicationAndGitRoot**: `AddRepo` is called with a
valid new existing path (inserted and persisted), a nonexistent path (rejected, returns
false), an already-tracked path (rejected, returns false), and a folder without a `.git` folder or file and a
subfolder of a repository (rejected as not a Git repository). A listed folder added again with a trailing directory
separator is reported as already listed, an entry saved in the old form with a trailing separator is kept as it was and
still recognized, and the repo-path identity trims only the trailing separator, keeps a root valid and ignores letter
case. This scenario is tested by
`MainWindowViewModel_AddRepo_ValidNewPath_InsertsCardAndPersistsSettings`,
`MainWindowViewModel_AddRepo_PathDoesNotExist_ReturnsFalse`,
`MainWindowViewModel_AddRepo_DuplicatePath_ReturnsFalse`,
`MainWindowViewModel_AddRepos_ListedFolderWithTrailingSeparator_IsAlreadyListed`,
`MainWindowViewModel_AddRepo_StoredWithTrailingSeparator_IsAlreadyListed`,
`MainWindowViewModel_NormalizeRepoPath_TrailingSeparatorAndRoot_TrimsOnlyTheSeparator`,
`MainWindowViewModel_AddRepo_FolderWithoutGit_IsRefusedAsNotAGitRepository` and
`MainWindowViewModel_AddRepo_SubfolderOfARepository_IsRefused`, covering
`AgentControl-MainWindowViewModel-AddRepo`.

**MainWindowViewModel_AddRepoToGroup_AddsOrMovesRepoIntoTheGroup**: `AddRepoToGroup` (tested in
`RepoGroupingTests.cs`) puts a new repo in the requested group - spelled as the existing group when only the letter case
differs - saves once and lists it in that group's section; moves the existing entry into the group when the repo is
already listed (still two cards, not three); returns `Missing` for a folder that does not exist; refuses a subfolder
of a repository as `NotAGitRepository` (`MainWindowViewModel_AddRepo_SubfolderOfARepository_IsRefused`); opens a
collapsed target group; and refuses a blank or built-in section name with `ArgumentException`, adding nothing. This
scenario is tested by `MainWindowViewModel_AddRepoToGroup_NewRepo_AddsCardInTheGroupAndPersistsIt`,
`MainWindowViewModel_AddRepoToGroup_RepoAlreadyListed_MovesItIntoTheGroup`,
`MainWindowViewModel_AddRepoToGroup_FolderMissing_AddsNothing`,
`MainWindowViewModel_AddRepoToGroup_CollapsedGroup_ExpandsIt`,
`MainWindowViewModel_AddRepoToGroup_InvalidGroupName_ThrowsAndAddsNothing` and
`MainWindowViewModel_AddRepo_SubfolderOfARepository_IsRefused`, covering
`AgentControl-MainWindowViewModel-AddRepoToGroup`.

**MainWindowViewModel_AddGitReposOnly_OnlyTopFoldersOfGitRepositoriesAreAdded**: A folder without a `.git` folder or
file is refused as `NotAGitRepository` and nothing is added or saved
(`MainWindowViewModel_AddRepo_FolderWithoutGit_IsRefusedAsNotAGitRepository`); a subfolder of a repository is refused
by the plain add and by the add into a group alike (`MainWindowViewModel_AddRepo_SubfolderOfARepository_IsRefused`);
a folder with a `.git` file, as a linked worktree or a submodule has, is added
(`MainWindowViewModel_AddRepo_FolderWithGitFile_IsAdded`); a file (as dropped from the file manager) is reported as
`NotAGitRepository` and a path that does not exist as `Missing`
(`MainWindowViewModel_AddRepos_ExistingFile_IsNotAGitRepository`); and a repo already in the list whose folder has no
`.git` is still listed after the view model is built from the settings - the check applies only to folders being
added (`MainWindowViewModel_Constructor_ListedFolderWithoutGit_IsKept`). These cover
`AgentControl-MainWindowViewModel-AddGitReposOnly`.

**MainWindowViewModel_AddRepos_SeveralFoldersEachHandledOnTheirOwn**: Several folders - repositories, a folder that is
not a repository, a missing one, one already listed and one given twice - give one outcome per distinct folder, in
order: the repositories are added and saved and the others are skipped with their reasons
(`MainWindowViewModel_AddRepos_MixedPaths_ReportsAnOutcomePerPath`); the same folder given with and without a trailing
directory separator is handled once and gives one card, saved without the separator
(`MainWindowViewModel_AddRepos_SameFolderWithAndWithoutTrailingSeparator_AddsOneCard`); several folders added into a
collapsed group all
join it, a listed repo is moved into it, and the group is opened once
(`MainWindowViewModel_AddRepos_IntoCollapsedGroup_OpensItOnce`); when the save fails nothing at all is added (cards,
settings and the displayed list are unchanged), both new folders are reported as not saved with the reason, and the
same folders are added once saving works
(`MainWindowViewModel_AddRepos_SaveFails_NothingAddedAndEveryFolderReportedNotSaved`, in
`MainWindowViewModelPersistenceTests.cs`); and four folders added in the background are saved with exactly one save
and the displayed list is rebuilt exactly once (`HasNoDisplayedRepos` notified once), the file holding all four
(`MainWindowViewModel_AddReposAsync_SeveralFolders_SavesOnceAndUpdatesTheListOnce`).
These cover `AgentControl-MainWindowViewModel-AddRepos`.

**MainWindowViewModel_AddReposInBackground_ReturnsAtOnceAndAddsWhenAllAreChecked**: With the folder checks held at the
gate, a background add of two repos has not completed, no card is added and the global progress is active with Cancel;
once released both are added in request order and the progress is cleared
(`MainWindowViewModel_AddReposAsync_WhileFoldersAreChecked_ReturnsAndAddsNothingYet`). A repo added another way while
the background add checks it is reported `AlreadyListed` and listed once
(`MainWindowViewModel_AddReposAsync_RepoListedMeanwhile_IsAlreadyListed`). A listed ungrouped repo (with a pin) and a
new folder are added to the group `Work`, and while their checks are held the listed repo and the group `Work` are
removed; once released both are `Added` into `Work`, which exists again and is expanded, with exactly one more save,
the repo that made `Work` exist stays ungrouped, the re-added card shows its pinned version once its background read
has run, and the saved file holds both in `Work`
(`MainWindowViewModel_AddReposAsync_ListedRepoAndTargetGroupRemovedMeanwhile_AddsBothToRecreatedGroupWithOneSave`). The
target group collapsed while the add is held hides its repo at once; once released the folder is `Added` with exactly
one more save, the group is open again, both its repos are displayed and the saved collapsed sections do not hold it
(`MainWindowViewModel_AddReposAsync_TargetGroupCollapsedMeanwhile_OpensItWithTheNewRepo`). A new repo's card has its
branch read but
its Git status not checked, is not shown as dirty, and the stub git's invocation log holds no `status` call
(`MainWindowViewModel_AddReposAsync_NewRepo_GitStatusNotCheckedAndNoStatusRun`). These cover
`AgentControl-MainWindowViewModel-AddReposInBackground`.

**MainWindowViewModel_AddReposProgress_CountsShownAndCleared**: A background add of three folders reports `(0, 3)`
first and then 1, 2 and 3 of 3 in whatever order the checks finish
(`MainWindowViewModel_AddReposAsync_ReportsProgressCounts`); through `EnqueueAddReposAsync` the global progress is idle
before, determinate with "Adding 0 of 2 repositories..." and a maximum of 2 while the checks are held, and idle with no
text after (`MainWindowViewModel_EnqueueAddRepos_ShowsAndClearsGlobalProgress`). These cover
`AgentControl-MainWindowViewModel-AddReposProgress`.

**MainWindowViewModel_AddReposCancel_NothingSavedOrAdded**: The global progress's Cancel is executed while the folder
checks are held; once released the result is canceled with no outcome, no card or entry is added, no save was made
and no settings file exists, and the progress is cleared
(`MainWindowViewModel_EnqueueAddRepos_Canceled_SavesNothingAndAddsNothing`). With one listed repo read and the reads
then held, an add is started and, while its folder check is held, new settings are applied, which starts the cards'
refresh wave: the global progress still shows the add ("Adding...") with Cancel; executing Cancel makes it show the
refresh ("Refreshing repositories..."), and once released the add's result is canceled
(`MainWindowViewModel_EnqueueAddRepos_SettingsRefreshStartsDuringAdd_StripKeepsAddCancel`). These cover
`AgentControl-MainWindowViewModel-AddReposCancel`.

**MainWindowViewModel_AddReposQueued_SecondAddRunsAfterTheFirst**: While the first add's two folder checks are held, a
second add sharing one folder is requested; it has not completed and has checked nothing (still two calls at the gate);
once released the first adds both folders, the second reports its shared folder `AlreadyListed` and adds the other,
four checks were made in all and the cards are in request order
(`MainWindowViewModel_EnqueueAddRepos_SecondBatchWhileFirstRuns_RunsAfterItWithoutDuplicates`). This covers
`AgentControl-MainWindowViewModel-AddReposQueued`.

**MainWindowViewModel_AddReposSaveFailure_RetryOrDiscard**: Tested by `MainWindowViewModelPersistenceTests.cs`. With
saving failing, a background add of two folders asks once, with a message that says the settings could not be saved
and gives the reason; after saving works again Retry adds both with one more save and clears the progress
(`MainWindowViewModel_EnqueueAddRepos_SaveFailsThenRetry_AddsTheBatch`). When the save made on Retry fails again the
question is asked again, and the next Retry that saves adds the folder once, with one successful save
(`MainWindowViewModel_EnqueueAddRepos_RetryFailsAgain_AsksAgainUntilSaved`). Adding a listed repo and a new folder to a
collapsed group and answering Discard reports both as not saved, and the listed repo keeps its group (none), the stored
order is unchanged, the group stays collapsed, the cards, the displayed list and the settings file's content are
exactly as before the add
(`MainWindowViewModel_EnqueueAddRepos_SaveFailsThenDiscard_LeavesEverythingAsBeforeTheAddIncludingMovedRepos`);
with no subscriber to the question the add is discarded and the folder reported as not saved with the reason
(`MainWindowViewModel_EnqueueAddRepos_SaveFailsWithNoOneToAsk_Discards`). These cover
`AgentControl-MainWindowViewModel-AddReposSaveFailure`.

**MainWindowViewModel_AddKeepsOtherChanges_WrittenWithOrWithoutTheAdd**: Tested by
`MainWindowViewModelPersistenceTests.cs`. With the add's save held at the gate, a favorite marked meanwhile is applied
at once; once the gate opens the add is saved and then one more write holds both the new repo and the favorite (two
saves in all, so no snapshot without the add was written after it)
(`MainWindowViewModel_EnqueueAddRepos_ChangeWhileSaving_WrittenWithTheAdd`). On a clock that never fires by itself, a
favorite marked before an add stays waiting; the add's save fails, a card color is chosen while the question is
shown, and Discard is answered: the flush writes the favorite and the color, without the discarded add
(`MainWindowViewModel_EnqueueAddRepos_SaveFailsThenDiscard_KeepsChangesMadeBeforeAndDuringTheAdd`). These cover
`AgentControl-MainWindowViewModel-AddKeepsOtherChanges`.

**MainWindowViewModel_AddReposSummary_NothingOneMessageOrOneSummary**: `DescribeAddOutcomes` returns nothing when every
folder was added (`MainWindowViewModel_DescribeAddOutcomes_AllAdded_ReturnsNull`); for one folder it gives the
single-add message naming the folder and the reason - already in the list, moved into the group, does not exist, not
the top folder of a Git repository, could not be saved with the problem - and nothing for an added folder or a failed
move (`MainWindowViewModel_DescribeAddOutcomes_Single_KeepsTheSingleAddMessages`); for several it gives one summary
that starts with how many were added, has one line per kind of outcome naming the folders by their last segment, at
most five per line followed by how many more, and ends with the save problem
(`MainWindowViewModel_DescribeAddOutcomes_SeveralMixed_OneSummaryByOutcomeWithAtMostFiveNames`). These cover
`AgentControl-MainWindowViewModel-AddReposSummary`.

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

**MainWindowViewModel_SortOrder_FavoritesFirstThenStoredOrder**: Settings that hold a stored user order (the marker is
set) whose launch times point the other way round than the order are shown with the favorite first and the others in
the stored order, not by launch time; launching a repo and then putting repos in groups leaves the flat list and the
sections in the stored order. This scenario is tested by
`MainWindowViewModel_DisplayedRepoCards_FavoritesFirstThenStoredOrder` and
`MainWindowViewModel_DisplayedRepoCards_LaunchTimesDoNotAffectStoredOrder`, covering
`AgentControl-MainWindowViewModel-SortOrder`. A stored order is kept exactly, whatever the launch times say
(`MainWindowViewModel_Constructor_StoredOrder_IsKeptWhateverTheLaunchTimes`, covering
`AgentControl-MainWindowViewModel-OrderKept`). Settings without a stored order - an older file, or a first run built in
memory - are arranged once as favorites first, then the most recently launched with never-launched repos last
(`MainWindowViewModel_Constructor_NoStoredOrder_SeedsFavoritesFirstThenMostRecentlyLaunched`), and the next save of any
kind writes that order and the marker, so it is not arranged again
(`MainWindowViewModel_Constructor_SeededOrder_IsWrittenByTheNextSave`), covering
`AgentControl-MainWindowViewModel-LegacyOrderSeeded`.

**MainWindowViewModel_StableOrder_LaunchRefreshAndApplySettingsNeverMoveCards**: Tested by `RepoOrderingTests.cs` (same
`RealProcess` collection and temporary-directory approach; a fake process starter and shell detector, so a launch
starts nothing; a failing save is made by turning `settings.json` into a directory). Launching a repo records the launch
but leaves the cards, the displayed list and the saved order as they were
(`MainWindowViewModel_Launch_DoesNotReorderCards`); refreshing the cards, applying settings and the background upgrade
check leave them as they were too (`MainWindowViewModel_RefreshAndApplySettings_DoNotReorderCards`), covering
`AgentControl-MainWindowViewModel-StableOrder`. A repo added with the top-bar add is last in the list and in the file
(and in the flat list below the other repos of its tier); a repo added to a group is at the bottom of that group; a
repo that is already listed and is added to a group goes to the bottom of the group wherever it was in the list
(`MainWindowViewModel_AddRepo_NewRepoGoesToTheEndOfTheList`,
`MainWindowViewModel_AddRepoToGroup_NewRepoGoesToTheBottomOfTheGroup` and
`MainWindowViewModel_AddRepoToGroup_RepoAlreadyListed_MovesToTheBottomOfTheGroup`), covering
`AgentControl-MainWindowViewModel-NewRepoAtEnd`.

**MainWindowViewModel_MoveCard_TopUpDownBottomWithinTheSection**: `MoveCard` puts a card first, one up, one down or last
in its section and saves the order (`MainWindowViewModel_MoveCard_TopUpDownBottom_ReordersWithinTheSection`); a move
that leads nowhere is refused without saving (`MainWindowViewModel_MoveCard_AtBoundary_IsRefusedAndChangesNothing`);
when the cards of two sections alternate in the stored list a move changes only the moved card's place and leaves the
other section's order alone (`MainWindowViewModel_MoveCard_SectionsInterleavedInTheList_KeepsOtherSectionsOrder`); and
when the order cannot be saved the move is kept (the cards, the list and the stored order), the user is told once, and
the next move, once saving works, writes both
(`MainWindowViewModel_MoveCard_SaveFails_KeptReportedOnceAndSavedWithNextChange`), covering
`AgentControl-MainWindowViewModel-MoveCard`. `CanMoveCard` reports each of the four moves for the first, a middle and
the last card (`MainWindowViewModel_CanMoveCard_FirstMiddleAndLast_ReportsEachItem`), and `GetMoveDisabledReason` names
the reason - first, last, or the filter - and is `null` when the move is allowed
(`MainWindowViewModel_GetMoveDisabledReason_NamesTheReason`), and `GetReorderDisabledReason` is `null` for repos with a
section neighbor and names the filter or "the only one in its section" otherwise
(`MainWindowViewModel_GetReorderDisabledReason_NamesTheReason`), covering
`AgentControl-MainWindowViewModel-MoveAvailability`.

**MainWindowViewModel_DropCard_PlacesTheCardAtTheDropPosition**: `DropCard` puts a card before or after a card of its
section and saves (`MainWindowViewModel_DropCard_BeforeAndAfterAnAnchor_ReordersWithinTheSection`); dropping a card on
itself, or where it already is, is accepted and saves nothing (`MainWindowViewModel_DropCard_OnItself_ChangesNothing`);
in a list without groups a drop in the other tier is refused, changing no favorite state, while a drop in its own tier
works (`MainWindowViewModel_DropCard_FlatListOtherTier_IsRefused`); and a target whose card was removed, or is not in
the target section, is refused (`MainWindowViewModel_DropCard_StaleAnchor_IsRefused`), covering
`AgentControl-MainWindowViewModel-DropReorder`. Dropping in another section sets the group between two cards of another
group, removes it in Ungrouped, marks a favorite and keeps the group in Favorites, and clears the favorite state on
leaving Favorites, into a group or into Ungrouped, each at the drop position and saved
(`MainWindowViewModel_DropCard_IntoAnotherGroup_SetsTheGroupAtTheDropPosition`,
`MainWindowViewModel_DropCard_IntoUngrouped_RemovesTheGroup`,
`MainWindowViewModel_DropCard_IntoFavorites_MarksFavoriteAndKeepsTheGroup`,
`MainWindowViewModel_DropCard_OutOfFavoritesIntoGroup_ClearsFavoriteAndSetsTheGroup` and
`MainWindowViewModel_DropCard_OutOfFavoritesIntoUngrouped_ClearsFavoriteAndGroup`); when the drop cannot be saved the
card is kept in the new section at the drop place and the user is told once for both changes
(`MainWindowViewModel_DropCard_SaveFails_KeptAtTheDropPlaceAndReportedOnce`), covering
`AgentControl-MainWindowViewModel-DropChangesSection`. A drop on a group's header puts the card at the bottom of the
group; on a collapsed group's header it does the same, leaves the group collapsed (the same header object, still
collapsed) and updates its count; on the Favorites and Ungrouped headers it favorites, or un-favorites and takes the
card out of its group, at the bottom of that section
(`MainWindowViewModel_DropCard_OnGroupHeader_GoesToTheBottomOfTheGroup`,
`MainWindowViewModel_DropCard_OnCollapsedGroupHeader_KeepsItCollapsedAndUpdatesTheCount` and
`MainWindowViewModel_DropCard_OnFavoritesAndUngroupedHeaders_MoveTheCardThere`), covering
`AgentControl-MainWindowViewModel-DropOnHeader`.

**MainWindowViewModel_ReorderOffWhileFiltering_FilterTextDisablesEveryWayToReorder**: While `FilterText` has text
`IsReorderEnabled` is `false`, and `MoveCard`, `CanMoveCard`, `CanDrop` and `DropCard` refuse, changing nothing and
saving nothing; blank text is no filter (`MainWindowViewModel_FilterText_DisablesReorderingAndRefusesMoveAndDrop`).
Typing and clearing a filter leaves the stored order alone and the filtered list shows its matches in that order
(`MainWindowViewModel_FilterText_DoesNotChangeTheStoredOrder`), and changing the filter raises the property-change
notification for `IsReorderEnabled` (`MainWindowViewModel_IsReorderEnabled_RaisesPropertyChangedWithTheFilter`),
covering `AgentControl-MainWindowViewModel-ReorderOffWhileFiltering`.

**MainWindowViewModel_RepoOrder_IsPersistedAndKept**: A move is in the settings file, with the marker, and a view model
built from the loaded settings shows the same order (`MainWindowViewModel_MoveCard_OrderIsPersistedAndReloaded`);
removing a repo leaves the others in the order the user gave them, in the list and in the file
(`MainWindowViewModel_RemoveRepo_LeavesTheRestOfTheOrderAndPersistsIt`), covering
`AgentControl-MainWindowViewModel-OrderPersisted`. Applying settings from a fresh `AppSettings` (as the Settings
window's Save builds it) keeps the order, the marker and the saved file's order
(`MainWindowViewModel_ApplySettings_KeepsTheRepoOrder`), covering
`AgentControl-MainWindowViewModel-OrderKeptOnApplySettings`.

**MainWindowViewModel_GroupOrder_StoredOrderThenAlphabetically**: Tested by `RepoOrderingTests.cs`. The groups are
listed in the user's stored order, the groups without a stored position after them alphabetically, between Favorites
first and Ungrouped last, and the stored order is repaired to the groups that exist, spelled like them
(`MainWindowViewModel_DisplayedItems_GroupsInStoredOrderThenAlphabetically`, covering
`AgentControl-MainWindowViewModel-GroupOrder`).

**MainWindowViewModel_MoveGroup_SwapsWithItsNeighborAndExplainsWhatIsUnavailable**: Moving a group up or down swaps it
with the group before or after it, and the whole order of the groups as shown is stored and saved
(`MainWindowViewModel_MoveGroup_UpAndDown_SwapsWithNeighborAndStoresTheFullOrder`); when the order cannot be saved
the move is kept and the failure is reported (`MainWindowViewModel_MoveGroup_SaveFails_KeptAndReported`).
These cover `AgentControl-MainWindowViewModel-MoveGroup`. The first group cannot move up and the last cannot move
down, *Favorites*, *Ungrouped*, unknown groups and groups without a section of their own never move, nothing moves
while the filter has text, each with its reason, and the headers offer the moves accordingly
(`MainWindowViewModel_GetGroupMoveDisabledReason_EndsBuiltInSectionsAndFilter`, covering
`AgentControl-MainWindowViewModel-MoveGroupAvailability`).

**MainWindowViewModel_DropGroup_BeforeOrAfterAnotherGroup**: Dropping a group header before or after another group's
header puts the group there, a collapsed group stays collapsed and the order is saved
(`MainWindowViewModel_DropGroup_BeforeOrAfterAnotherGroup_Reorders`); a drop on the group's own header, on *Favorites*
or *Ungrouped*, on a group that is not listed, or while the filter has text is refused and changes nothing
(`MainWindowViewModel_CanDropGroup_SelfBuiltInUnknownOrFiltering_Refused`). These cover
`AgentControl-MainWindowViewModel-DropGroup`.

**MainWindowViewModel_GroupOrder_IsPersisted**: After a group is moved, a view model created from the saved settings
(the next start) shows the groups in the same order (`MainWindowViewModel_MoveGroup_PersistsOrderForANewViewModel`,
covering `AgentControl-MainWindowViewModel-GroupOrderPersisted`).

**MainWindowViewModel_ApplySettings_PreservesReposAndInvalidatesPackageCache**: `ApplySettingsAsync`
is called with an updated `AppSettings`; the existing recent-repos list survives, the new
field values are adopted and persisted, and the shared `PackageVersionCache` is invalidated so
a changed package-source path is reflected immediately rather than served from a stale cache.
A repo that reappeared since its last check gets an immediate fresh Git status (the background cheap refresh followed
by the background working-tree check), rather than being left showing a stale or indefinitely
"not yet checked" Pull-disabled state. A present repo also gets an immediate fresh Git status
when `GitExecutablePath` itself changed, so a clean/dirty result cached against the previously
configured executable is not kept showing once a different executable is in effect. A repo
that was already present, already checked, and whose executable path did not change only gets
the cheap refresh, so re-saving settings does not re-run `git status` for every
tracked repo. These tests await `PendingRefresh` (the background refresh) before asserting. This scenario is tested by
`MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields`,
`MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache`,
`MainWindowViewModel_ApplySettings_RepoReappeared_RefreshesGitStatusImmediately`,
`MainWindowViewModel_ApplySettings_RepoAlreadyPresentAndChecked_DoesNotRecheckGitStatus`, and
`MainWindowViewModel_ApplySettings_GitExecutablePathChanged_RefreshesGitStatus`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_InvalidateChangedSources_OnlyEditedOrRemovedSourcesAreInvalidated**:
`ApplySettingsAsync` replaces one package source's location; a card whose files that unchanged source supplies
keeps its cached upgrade answer even after that source's folder has disappeared (an invalidated
cache would fail to read it and report no upgrade), while a changed source is observed afresh. This
scenario is tested by `MainWindowViewModel_ApplySettings_OnlyEditedSourceIsInvalidated` and
`MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache`, covering
`AgentControl-MainWindowViewModel-InvalidateChangedSources`.

**MainWindowViewModel_ApplySettings_CredentialBearingSource_IsRefused**: `ApplySettingsAsync` with a
package source location that embeds a sentinel password throws `InvalidOperationException` whose
message does not contain it, the live settings instance is unchanged, and no settings file is
written. This scenario is tested by
`MainWindowViewModel_ApplySettings_SourceLocationWithEmbeddedCredential_ThrowsAndKeepsCurrentSettings`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive**: `ApplySettingsAsync` against
a configuration location that cannot be written throws `InvalidOperationException`, and the live
settings instance is still the previous one with its previous values and recent repos. This
scenario is tested by `MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive`,
covering `AgentControl-MainWindowViewModel-ApplySettings`.

**MainWindowViewModel_ApplySettingsInBackground_ReturnsBeforeTheCardRefresh**: With one listed repo and the cards'
background reads held at the gate, `ApplySettingsAsync` returns while `PendingRefresh` has not completed, and the global
progress shows "Refreshing repositories 0 of 1..." without Cancel; once released the refresh completes, the progress
is cleared and the card reflects the refresh (present)
(`MainWindowViewModel_ApplySettings_CardRefreshHeld_ReturnsAndShowsRefreshingProgress`). This covers
`AgentControl-MainWindowViewModel-ApplySettingsInBackground`.

**MainWindowViewModel_SharedRepoReads_TwoAtATimeAcrossAddsAndRefresh**: A background add of six repo folders, with the
checks held at the gate, has handed exactly two checks to the runner; once released all six are added in request
order, six checks ran and never more than two at once
(`MainWindowViewModel_AddReposAsync_ManyFolders_AtMostTwoInspectedAtOnce`). With four listed repos, settings are
applied (four card reads) and two folders are added (two checks) while the reads are held: two reads in all have been
handed to the runner and the strip shows the refresh; once released every read ran, never more than two at once, all
six repos are listed and the strip is gone (`MainWindowViewModel_ApplySettings_RefreshUsesSharedQueueLimit`). These
cover `AgentControl-MainWindowViewModel-SharedRepoReads`.

**MainWindowViewModel_StartupReadsQueued_BuiltAtOnceAndFilledIn**: With three listed repos (stub git) and every read
held, the view model is built and usable (a filter applies), two reads have been handed to the runner, the start-up
refresh is pending, the strip says "Refreshing repositories 0 of 3..." without Cancel, and every card is refreshing
with status `Refreshing`, branch `...`, no "No agent package" chip, no committed-files warning and not missing; once
released the cards show their branch and committed files and the strip is gone
(`MainWindowViewModel_Constructor_GitHeld_ReturnsWithCardsRefreshingAndProgressShown`). Releasing the reads one at a
time, the first card (top of the list) is filled in first - its pin and branch, no longer refreshing - while the other
two are not, with the strip at "1 of 3"; once all are released every card is filled in, the missing folder shows as a
problem and the strip is gone (`MainWindowViewModel_Constructor_ReadsComplete_CardsFillInAndProgressClears`). When
the window closes (the view model is disposed) while the start-up reads are held and the held reads are then released,
the start-up refresh completes, only the two reads that were running were ever handed to the runner, no card shows a
read state or is still refreshing, and the strip is gone
(`MainWindowViewModel_Dispose_StartupReadsHeld_GivesThemUpAndAppliesNothing`). These cover
`AgentControl-MainWindowViewModel-StartupReadsQueued`.

**MainWindowViewModel_BackgroundUpgradeCheck_WaitsForCardStateAndSharesOneLimit**: The window's upgrade check started
while a pinned card's start-up read is held reads no source and does not show the card as checking; once released the
read lands and the check's result (an upgrade to 1.1.0) stands
(`MainWindowViewModel_Startup_UpgradeCheckWaitsForCardState`). A Refresh the user starts while the check waits for the
card's read makes the card busy; the check ends while the card is busy without reading the source for it, and the
source is read exactly once, by the Refresh (`MainWindowViewModel_Startup_BusyCardSkippedByUpgradeCheck`). While the
window's check holds four cards at their held source reads, two pinned repos are added: their checks do not start (no
card of them is checking) until slots free, and every card gets its upgrade once the source answers
(`MainWindowViewModel_UpgradeChecks_OverlappingChecks_ShareOneLimit`). These cover
`AgentControl-MainWindowViewModel-BackgroundUpgradeCheck` with the other scenarios of that requirement.

**MainWindowViewModel_BackgroundUpgradeCheck_ConstructionReadsNoSourceAndFailuresAreIsolated**:
Constructing the view model with a pinned repo reads no package source until the upgrade refresh
begins; when the refresh runs with one source unreachable, the other (readable) source still offers its upgrade
to the pinned cards; and adding a pinned repo checks its upgrade status in
the background. In `MainWindowViewModelRecheckTests.cs`, with five cards (more than are checked at once) and the
source's reads held open, the launch-time check starts four cards and the fifth waits for its turn; the user then
starts refreshing that card (its read held by a gate of its own), every later read is made to fail at once and the
four held reads are let go: the launch check finishes without checking the busy card, whose refresh then shows what it
read (the upgrade) rather than the failure a background check started beside it would have put on the card as its
newest result. This scenario is tested by
`MainWindowViewModel_Constructor_DoesNotReadSourcesUntilUpgradeRefreshBegins`,
`MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_ReadableSourceStillOffersUpgrades`,
`MainWindowViewModel_AddRepo_PinnedRepo_UpgradeCheckRunsInBackground`, and
`MainWindowViewModel_LaunchCheck_WaitingCardBecomesBusy_IsNotChecked`, covering
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
applier, `ApplySettingsAsync` with the dark theme persists it and calls the applier once with it, covering
`AgentControl-MainWindowViewModel-ApplyTheme`; when the write fails (the configuration "directory" is a file) the
exception reaches the caller, the applier is not called and the live settings keep the system default, covering
`AgentControl-MainWindowViewModel-RefusedThemeNotApplied`. This scenario is tested by
`MainWindowViewModel_ApplySettings_ThemeChanged_PersistsAndAppliesTheme` and
`MainWindowViewModel_ApplySettings_SaveFails_DoesNotApplyTheme`.

**MainWindowViewModel_ApplySettings_KeepShellOpenChanged_NextLaunchUsesIt**: Built with a recording process starter and
a deterministic shell detector, the view model hands both to the cards it creates: a launch asks for PowerShell with
`-NoExit`, and after `ApplySettingsAsync` with the keep-shell-open choice cleared the next launch of the same card asks
for PowerShell without `-NoExit`, with no process started. This scenario is tested by
`MainWindowViewModel_ApplySettings_KeepShellOpenChanged_NextLaunchUsesIt`, evidencing
`AgentControl-LauncherUI-KeepShellOpenApplied` at the subsystem level (see the `LauncherUI` verification design). The
persistence tests' launch case (`MainWindowViewModel_LaunchRecordSaveFails_StampKeptAndReported`) uses the same fake
starter.

**MainWindowViewModel_GroupSections_FlatListWithoutGroupsSectionedListWithThem**: Tested by `RepoGroupingTests.cs` (same
`RealProcess` collection and temporary-directory approach, with repos created under a unique parent so the card name is
the folder name). Settings with no group give a flat list without any header, favorites first
(`AgentControl-MainWindowViewModel-FlatListWithoutGroups`, tested by
`MainWindowViewModel_DisplayedItems_NoGroups_FlatListWithoutHeaders`). Settings with groups and no stored group order
give Favorites first, the
groups alphabetically ignoring letter case, then Ungrouped; with a stored group order the groups follow it and the
groups without a stored place come after them alphabetically
(`MainWindowViewModel_DisplayedItems_GroupsInStoredOrderThenAlphabetically`); names that differ only in letter case
form one section (`AgentControl-MainWindowViewModel-GroupSections`, tested by
`MainWindowViewModel_DisplayedItems_Groups_AreSectionedAndOrdered`,
`MainWindowViewModel_Constructor_NamesDifferingInCase_FormOneSection` and
`MainWindowViewModel_DisplayedItems_GroupsInStoredOrderThenAlphabetically`). Each section has a header with its
count and a
section without a repo has none (`AgentControl-MainWindowViewModel-GroupSectionHeaders`, tested by
`MainWindowViewModel_DisplayedItems_Groups_AreSectionedAndOrdered` and
`MainWindowViewModel_DisplayedItems_EmptySections_HaveNoHeader`); repos within a section are in the user's own order,
not by launch time (`AgentControl-MainWindowViewModel-GroupSectionRepoOrder`, tested by
`MainWindowViewModel_DisplayedItems_Groups_AreSectionedAndOrdered`). A favorite is listed only under Favorites, and back
in its group when it is no longer a favorite (`AgentControl-MainWindowViewModel-FavoriteListedOnce`, tested by
`MainWindowViewModel_Favorite_IsListedOnlyUnderFavoritesAndKeepsItsGroup`). Settings built in memory with a null
collapsed list, blank, reserved, overlong or look-alike group names load without failing
(`AgentControl-MainWindowViewModel-DamagedGroupSettings`, tested by
`MainWindowViewModel_Constructor_NullAndInvalidOrganizingSettings_FallBack` and
`MainWindowViewModel_Constructor_NamesDifferingInCase_FormOneSection`). The header rows themselves are verified by the
`RepoGroupHeaderViewModel` unit.

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
`AgentControl-MainWindowViewModel-GroupHeaderRequests`); and after moving, coloring a card, coloring a group,
collapsing and renaming
the repository folder keeps exactly the entries it had (its `.git` folder and nothing else) while the settings file
holds the choices
(`RepoCardViewModel_GroupAndColor_NeverWriteToTheRepository`, covering
`AgentControl-MainWindowViewModel-GroupSettingsOnly`). The prompt that asks for a group's name is verified by the
`GroupNameViewModel` unit.

**MainWindowViewModel_GroupColor_StoredInSettingsShownAndRefusedWhenUnusable**: Tested by `RepoGroupingTests.cs`. A
group color given in another letter case is stored under the group's own spelling in the palette's spelling, saved in
the settings file only, and choosing no color clears it
(`MainWindowViewModel_SetGroupColor_StoresColorAndNoneClears`); a color the palette does not know, a group that does
not exist and the *Favorites* and *Ungrouped* sections are refused and nothing changes
(`MainWindowViewModel_SetGroupColor_UnknownColorGroupOrBuiltInSection_ChangesNothing`); and when the color cannot be
saved the color is kept - on the header, the tiles and in the settings in memory - and the failure is reported once
(`MainWindowViewModel_SetGroupColorSaveFails_KeptAndReported`, in `MainWindowViewModelPersistenceTests.cs`).
These cover `AgentControl-MainWindowViewModel-GroupColor`. The color shows on the group's header (not on *Favorites* or
*Ungrouped*) and on the tiles of the group's repos without a card color of their own, a favorite in the group
included, while a card's own color wins and other repos keep their colors
(`MainWindowViewModel_SetGroupColor_UpdatesHeaderAndCardsOfTheGroup`, covering
`AgentControl-MainWindowViewModel-GroupColorShown`).

**MainWindowViewModel_GroupSettings_FollowRenameAndRemoveAndAreKeptOnApplySettings**: Renaming a group keeps its color
and its place in the group order under the new name, in memory and in the saved settings
(`MainWindowViewModel_RenameGroup_KeepsColorAndPosition`); removing a group forgets both, so a new group of the same
name starts without them (`MainWindowViewModel_RemoveGroup_ForgetsColorAndPosition`); and a group whose last repo
leaves it forgets both like a removed group (`MainWindowViewModel_LastRepoLeavesGroup_ForgetsColorAndPosition`). These
cover `AgentControl-MainWindowViewModel-GroupSettingsFollowRenameAndRemove`. Applying new settings as the Settings
window builds them keeps the group colors and the group order
(`MainWindowViewModel_ApplySettings_KeepsGroupColorsAndOrder`, covering
`AgentControl-MainWindowViewModel-GroupSettingsKeptOnApplySettings`).

**MainWindowViewModel_ChangesThatSave_AreAppliedAndWritten**: A favorite, a group and a card color chosen on a card
are applied and, after a flush, read back from the settings file in the stored (canonical) spelling, and no failure is
reported (`MainWindowViewModel_ChangesThatSave_AreAppliedAndWritten`). With the writes held at the gate, a favorite is
marked: the card, the settings and the list show it at once while nothing has been written yet and the flush is still
pending; once the gate opens the flush completes and the file holds the favorite
(`MainWindowViewModel_Favorite_SaveHeld_AppliedAtOnceAndWrittenAfterFlush`). These cover
`AgentControl-MainWindowViewModel-ChangeAppliedWhenSaved`.

**MainWindowViewModel_FailedSave_KeepsTheChange**: Tested by `MainWindowViewModelPersistenceTests.cs` (the `RealProcess`
collection, because every card's background repo-state read spawns git). A failing write is produced by the save seam
(it throws "The disk is full." without touching the file). With writing failing, each change is made and checked to be
in effect after the flush: a favorite (the card, the settings and the list), a move to a group (the group's section
appears), a card color, a recorded launch (the stamp is set and `LaunchRecorded` raised), a collapse (through the API
and through the header's button, neither throwing), a group rename (the members and the collapsed state follow; the file
still holds the old name) and a group removal, and removing a repo (removed from the list and the settings in memory
while the file still lists it). The favorite test then makes a second change, which fails too, and, once writing works,
a third: the file then holds all three. The removal test likewise writes the removal with the next change. This scenario
is tested by `MainWindowViewModel_FavoriteSaveFails_KeptReportedOnceAndSavedWithNextChange`,
`MainWindowViewModel_MoveToGroupSaveFails_KeptAndReported`, `MainWindowViewModel_CardColorSaveFails_KeptAndReported`,
`MainWindowViewModel_LaunchRecordSaveFails_StampKeptAndReported`,
`MainWindowViewModel_CollapseSaveFails_SectionCollapsedAndReported`,
`MainWindowViewModel_RenameGroupSaveFails_RenamedAndReported`,
`MainWindowViewModel_RemoveGroupSaveFails_RemovedAndReported` and
`MainWindowViewModel_RemoveRepoSaveFails_RemovedReportedAndSavedWithNextChange`, covering
`AgentControl-MainWindowViewModel-FailedSaveChangesNothing`.

**MainWindowViewModel_FailedSave_IsReportedOncePerStreak**: A failed favorite, group move, card color, recorded launch
and collapse each raise the save-failed notice exactly once; the favorite test checks that the message says the
change could not be saved, gives the reason and says the change stays in effect, and that a second failing change
raises no second notice, nor does the successful write after it. This scenario is tested by
`MainWindowViewModel_FavoriteSaveFails_KeptReportedOnceAndSavedWithNextChange`,
`MainWindowViewModel_MoveToGroupSaveFails_KeptAndReported`, `MainWindowViewModel_CardColorSaveFails_KeptAndReported`,
`MainWindowViewModel_LaunchRecordSaveFails_StampKeptAndReported` and
`MainWindowViewModel_CollapseSaveFails_SectionCollapsedAndReported`, covering
`AgentControl-MainWindowViewModel-SaveFailureReported`.

**MainWindowViewModel_FailedSave_RefusesAddsAndSettings**: With writing failing, adding a repo throws to the caller and
adds nothing, and the same repo can be added once writing works (`MainWindowViewModel_AddRepoSaveFails_NothingAdded`);
a multi-folder add reports every folder as not saved and adds nothing
(`MainWindowViewModel_AddRepos_SaveFails_NothingAddedAndEveryFolderReportedNotSaved`); and applying the Settings
window's values throws through its task with the reason, adopts nothing (the same settings instance stays live, the
theme is not applied) and raises no background notice
(`MainWindowViewModel_ApplySettingsAsync_SaveFails_NothingAdoptedAndThrows`). These cover
`AgentControl-MainWindowViewModel-SaveFailureRefusesOperation`. How the window shows that failure is verified at the
`LauncherUI` subsystem level.

**MainWindowViewModel_ApplySettingsAsync_SavesInTheBackground**: With the writes held at the gate, applying new
settings returns a task that is still pending once the write has started, and nothing is adopted (the same settings
instance is live); once the gate opens the task completes, the new value is adopted with the repo list kept, and the
file holds it (`MainWindowViewModel_ApplySettingsAsync_SaveHeld_ReturnsPendingAndAdoptsOnceSaved`). A favorite marked
while the write is held is applied at once, kept when the settings are adopted, and written together with them after a
flush (`MainWindowViewModel_ApplySettingsAsync_ChangeDuringSave_KeptAndWrittenAfterFlush`). These cover
`AgentControl-MainWindowViewModel-ApplySettingsSaveInBackground`.

**MainWindowViewModel_Dispose_WritesWhatIsNotWrittenYet**: On a clock that never fires by itself, a favorite is marked
and nothing has been written; the view model is disposed inside a `BlockingCallGuard` scope: no blocking call is
recorded on the closing thread, and the settings file holds the favorite. This scenario is tested by
`MainWindowViewModel_Dispose_OnGuardedThread_WritesPendingChangeWithoutBlockingCall`, covering
`AgentControl-MainWindowViewModel-SettingsFlushedOnClose`.

**MainWindowViewModel_Dispose_WhileAddSaving_WritesAddAndChange**: On the headless UI thread (so, as in the
application, the add's own completion cannot run while the close blocks that thread), writes are held at a gate; an
add is started and its save is held; a favorite is marked meanwhile (deferred while the add saves); the gate is opened
and the view model disposed in the same UI-thread turn: the settings file holds both repos, the added one included,
and the favorite. This scenario is tested by
`MainWindowViewModel_Dispose_WhileAddSavingAfterAnotherChange_WritesAddAndChange`, covering
`AgentControl-MainWindowViewModel-SettingsFlushedOnClose`.

**MainWindowViewModel_RemoveRepo_RetiresTheCard**: A repo's card is asked to report an error (its select-package
action with no package source configured) and does; the repo is removed and the same action is tried again: nothing is
reported for the removed card. What a retired card does with work that is still running is verified by the
`RepoCardViewModel` unit, which owns `Retire`. This scenario is tested by
`MainWindowViewModel_RemoveRepo_RetiresTheCard`, covering
`AgentControl-MainWindowViewModel-RemoveRepoRetiresCard`.

**MainWindowViewModel_PeriodicRecheck_BadgesFollowNewVersions**: The tests in `MainWindowViewModelRecheckTests.cs`
run the view model on a `FakeTimeProvider` (so no test waits) over an in-memory source (`FakePackageSourceProvider`)
whose reads are counted and which can be given a version after construction. A card whose launch-time check found
nothing newer shows the upgrade after a version is published and the interval (five minutes by default, any positive
interval on request) has passed, and not just before it; the round repeats every interval; a round reads a source once
for five cards that pin the same package; a repo with no pin, a repo whose folder is missing and a card that is busy
refreshing itself are not checked (no source is read for them); a source that now needs a sign-in is reported on the
card (`SourceNeedsSignIn`, status unknown) with no dialog - the check is the one that never asks; a source that cannot
be reached (or that needs a sign-in) is read once per round for five cards that pin the same package - a failed read
is not cached, but the round remembers it - and every card shows the failed-check state, while the next round reads the
source again (once), also when it is still down; with two sources of which one is down, the failing source is read once
per round (not once per card) and every card still shows the upgrade the other offers; the launch-time check shares a
failure the same way; and with a
UI-thread runner supplied the tick is queued and the round runs only when the runner runs it. This scenario is
tested by `MainWindowViewModel_Constructor_RecheckInterval_DefaultsToFiveMinutesAndMustBePositive`,
`MainWindowViewModel_PeriodicRecheck_VersionPublishedAfterLaunchCheck_ShowsUpgradeAfterInterval`,
`MainWindowViewModel_PeriodicRecheck_CustomInterval_RepeatsEveryInterval`,
`MainWindowViewModel_PeriodicRecheck_SeveralCardsSamePackage_ReadsTheSourceOnce`,
`MainWindowViewModel_PeriodicRecheck_SourceUnreachable_ReadsOncePerRoundAndEveryCardShowsFailure`,
`MainWindowViewModel_PeriodicRecheck_SourceStaysUnreachable_ReadsOnceInEachRound`,
`MainWindowViewModel_PeriodicRecheck_SourceNeedsSignIn_ReadsOncePerRoundAndEveryCardReportsIt`,
`MainWindowViewModel_PeriodicRecheck_OneSourceFails_FailingSourceReadOncePerRound`,
`MainWindowViewModel_LaunchCheck_SourceUnreachable_CardsShareTheFailure`,
`MainWindowViewModel_PeriodicRecheck_CardsWithNoPinOrMissingFolder_AreSkipped`,
`MainWindowViewModel_PeriodicRecheck_CardIsBusy_IsSkipped`,
`MainWindowViewModel_PeriodicRecheck_SourceNeedsSignIn_IsReportedOnTheCard` and
`MainWindowViewModel_PeriodicRecheck_UiThreadRunnerSupplied_TickRunsThroughIt`, covering
`AgentControl-MainWindowViewModel-PeriodicUpgradeRecheck`.

**MainWindowViewModel_PeriodicRecheck_Lifecycle_NoOverlapOneTimerStopsOnDispose**: With a source whose read is held
open, a tick that arrives while the previous round is still running starts nothing (the source is read once and the
pending round is the same task), the next tick after the round ended starts a new one; starting the re-check twice
leaves one timer (one read per tick); a round that a launch-style check (a settings change) supersedes stays
registered until it has unwound, so a tick that comes after the new check completed - while the cancelled round's
provider call is still in flight - starts no second round, and the first tick after it finished does; a launch-style
check that a newer one superseded while it was inside a provider call that ignores cancellation holds off the rounds
until it has unwound, although the newer check completed and `PendingRefresh` is done, and the first tick after it
unwound starts a round that reads the source once; disposing the
view model (twice is harmless) stops the timer so later ticks read
nothing and refuses a new start, disposing while a round waits for a source cancels the round without an error and
leaves the card as it was, and disposing while the launch-time check waits for a source cancels that check too (the
provider's token is cancelled and a late answer changes no card). This scenario is tested by
`MainWindowViewModel_PeriodicRecheck_PreviousRoundStillRunning_SkipsTheTick`,
`MainWindowViewModel_PeriodicRecheck_RoundSuperseded_NoNewRoundUntilItHasUnwound`,
`MainWindowViewModel_PeriodicRecheck_SupersededLaunchCheckStillRunning_NoRoundUntilItHasUnwound`,
`MainWindowViewModel_StartPeriodicUpgradeRecheck_CalledTwice_RunsOneTimer`,
`MainWindowViewModel_Dispose_StopsThePeriodicRecheck`, `MainWindowViewModel_Dispose_RoundRunning_CancelsIt` and
`MainWindowViewModel_Dispose_LaunchCheckRunning_CancelsIt`,
covering `AgentControl-MainWindowViewModel-PeriodicRecheckLifecycle`.

**MainWindowViewModel_RecheckAfterSettingsClosed_ProblemCardsCheckedAgainWithoutPrompt**: In
`MainWindowViewModelRecheckTests.cs`, over the counted in-memory source: a card whose launch check found that the source
needs a sign-in, or could not reach it, is checked again when the view model is told that Settings closed without
saving, and once the source answers it shows no problem and offers the upgrade; when no card shows a problem or a failed
check, no source is read; after a save (`ApplySettingsAsync`, which checks every card) being told that Settings closed
after saving reads the source no further time; with two cards whose check failed, the one busy refreshing itself is not
checked while the other is (exactly one read); with five cards whose check failed (more than are checked at once), a
card that waits for its turn in the re-check and that the user starts refreshing meanwhile is not checked when its turn
comes, so it shows what the user's refresh read rather than the re-check's failure; and after the view model was
disposed nothing is started. With a periodic round that recorded a server's sign-in failure for the first of two cards
and is held at a second source, being told that Settings closed without saving (after the server started answering)
makes the round read the server again for the second card rather than replay the recorded failure, so neither card is
left showing "Sign-in needed" and both offer the upgrade. With a card that is up to date and a periodic round held while
reading the source for it, being told that Settings closed without saving checks that card again, and when the round's
old read then fails as needing a sign-in the card still shows the newer check's result (the upgrade) rather than
"Sign-in needed". End to end against an in-process mock NuGet server that requires a sign-in (with a real
`MainWindowViewModel`, its real providers and a `PackageCredentialContext` over a `SessionCredentialStore` and a
scripted prompt): the launch check reports "Sign-in needed" without asking, a listing through
`CreateConnectionTestProvider` - the Settings window's Test connection - asks once and signs in, and the re-check after
Settings closed without saving then clears the problem and offers the upgrade without asking again. This scenario is
tested by `MainWindowViewModel_RecheckAfterSettingsClosed_SourceNoLongerNeedsSignIn_ClearsTheProblem`,
`MainWindowViewModel_RecheckAfterSettingsClosed_SourceUnreachableBefore_ChecksAgain`,
`MainWindowViewModel_RecheckAfterSettingsClosed_NoCardHasAProblem_ReadsNoSource`,
`MainWindowViewModel_RecheckAfterSettingsClosed_SettingsSaved_StartsNoSecondCheck`,
`MainWindowViewModel_RecheckAfterSettingsClosed_CardIsBusy_IsSkipped`,
`MainWindowViewModel_RecheckAfterSettingsClosed_WaitingCardBecomesBusy_IsNotChecked`,
`MainWindowViewModel_RecheckAfterSettingsClosed_Disposed_ChecksNothing`,
`MainWindowViewModel_RecheckAfterSettingsClosed_RoundRecordedFailureBeforeSignIn_RoundReadsTheSourceAgain`,
`MainWindowViewModel_RecheckAfterSettingsClosed_CardCheckInFlight_OldFailureIsNotShown` and
`MainWindowViewModel_RecheckAfterSettingsClosed_SignedInDuringConnectionTest_UsesTheSignInWithoutAsking`, covering
`AgentControl-MainWindowViewModel-RecheckAfterSettingsClosed`.
