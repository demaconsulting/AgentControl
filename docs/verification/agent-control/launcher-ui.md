## LauncherUI

### Verification Approach

The `LauncherUI` subsystem is verified by the combined unit tests of its eleven units
(`MainWindowViewModelTests.cs` and `RepoGroupingTests.cs`, `RepoCardViewModelTests.cs`,
`RepoCardLaunchVerificationTests.cs` (the checks of a repo's agent files before launch),
`RepoCardServerSourceTests.cs`
and `RepoCardImageTests.cs`,
`SelectPackageWindowViewModelTests.cs` and `SelectPackageRetryTests.cs`,
`SettingsWindowViewModelTests.cs`, `SettingsWindowGitSourceTests.cs`, `SourceEditorViewModelTests.cs`,
`SourceEditorServerViewModelTests.cs`, `SourceEditorSignatureViewModelTests.cs` and `SourceEditorGitViewModelTests.cs`,
`RepoCardGitAwarenessTests.cs` (the Git icon, tooltip, refresh and Change package of the card) and
`PackageSourceGitPresentationTests.cs` (the Git icon and names, and how a source row classifies a remote Git source),
`PackageSourceStatusPresentationTests.cs` (the "Online source" status, the folder statuses and the "Signed only"
badge of a source row, with the Settings window showing it),
`CredentialPromptViewModelTests.cs`, `RepoImageLocatorTests.cs`, `RepoGroupHeaderViewModelTests.cs`,
`GroupNameViewModelTests.cs`, `DialogBehaviorTests.cs` and `GlobalProgressViewModelTests.cs`, the last five units each
with its own verification
document), plus
`AsyncRelayCommandTests.cs` for the shared asynchronous
command, which together
exercise the full recent-repos/card/select-package/settings/sign-in interaction surface without ever
constructing an Avalonia `Window` (apart from the picture decoder and `DialogBehavior`, which need Avalonia's headless
session). The windows themselves are exercised by headless Avalonia tests
(`DialogWindowTests.cs` - including the confirmation window's custom button labels -, `MainWindowTests.cs`,
`UsabilityWindowTests.cs` for the favorite star with real pointer
and key input, the "Check again" menu item, the up-to-date check mark, the narrow card, the Settings sections, the
keyboard behavior of the dialogs, the About version row, the order of the AGENTS.md question and the release
notes and the keep-or-update and launch-anyway questions about a repo's agent files,
`RepoGroupWindowTests.cs` for the group headers, the card menu's
"Move to group" submenu, the group-name prompt, the card and group color palette pop-ups (and the shared
`ColorPalettePanel`), and adding several folders from the picker,
`RepoReorderWindowTests.cs` for the user's own repo order (cards that stay put, the card menu's Reorder submenu, drag
and drop through the window's drag-source seam and the platform's drop events, the insertion line and header tint,
reordering turned off by the filter, the order after a restart; the real operating-system drag is covered only by the
FlaUI tests), the user's order of the groups (the header menu's Move items and header drags through the same
`DragStarter` seam, pressing the header's toggle) and that a group's color and place follow a rename (through the name
prompt), are forgotten when the group is removed or its last repo leaves it, and are kept when the Settings window is
saved, that the user's organizing choices made through the window (a group, a card color, a group color, the group
order and a collapsed section) are written to the settings file only, never into a repo folder, and folders dropped
from the file manager (a `DataTransfer` of
`DataTransferItem.CreateFile` items for storage items obtained from the window's `StorageProvider` -
`TryGetFolderFromPathAsync` / `TryGetFileFromPathAsync`, since user code cannot implement `IStorageItem` - raised with
`window.DragDrop` offering Copy, Move and Link), `RepoOrderingTests.cs` for the view model's rules behind them,
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
and a non-empty dependency list) is verified via the FlaUI
`DemaConsulting.AgentControl.UiTests` project, while its banner (logo, name and tagline above the details) and its
layout (600 wide, limited height, the banner and Close outside the one scrolling region that holds every detail) are
verified by headless tests in `DialogWindowTests.cs`, and the legibility of the banner text by `ThemeResourceTests`.
The logo image itself is not asserted beyond its presence in the banner (it has no bound text content to check) and
remains a visual-inspection-only element. The temporary repo folders the window tests add carry a `.git` folder whose
`HEAD` names a branch, so the Git-repository check accepts them; the tests of that check use folders without one.
`FavoriteIconConverter`, the favorite-star glyph's shape converter (its color is the theme resource
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

**Background work, without timing.** The responsiveness requirements (`AgentControl-LauncherUI-AddReposResponsive`,
`-GlobalProgress`, `-AddCancel`, `-AddQueued`, `-AddSaveFailureChoice`, `-SettingsApplyResponsive`,
`-GitStatusInBackground`, `-BoundedRepoReads`) are verified through two constructor seams instead of timing assertions.
The background runner seam takes `GatedBackgroundWorkRunner` (a test helper in `GatedBackgroundWorkRunner.cs`, a
supporting type of the tests), which sits under the shared refresh queue: it counts every piece of work handed to it
(`Calls`, with `FirstCall` completing at the first one) and the most pieces handed over and unfinished at the same time
(`MaxInFlight`, how many reads the queue ran at once), and holds the work at a gate until the test calls `Release`,
then runs it on the thread pool; while the work is held the tests assert
what the user would see - nothing added yet, the strip and its text, the Cancel button - and, in the headless window
tests (`HeadlessTestApp.RunAsync`), that the UI thread is free: `Dispatcher.UIThread.InvokeAsync` of a trivial action
completes while the add or the refresh is still held. No test asserts on thread identity or elapsed time. The save seam
of `MainWindowViewModel` takes a delegate that counts saves and throws `InvalidOperationException` on demand, so "one
save per batch", Retry and Discard are deterministic (the earlier way of breaking a save, turning `settings.json` into
a folder, still works for the older tests). The window tests that pick or drop folders run asynchronously and let the
queued add settle before asserting (`RepoReorderWindowTests.SettleAddsAsync`: run the posted add, await
`PendingAddRepos`, run what it posted afterwards).

**Drag auto-scroll.** The scroll step is a pure function (`MainWindow.ComputeAutoScrollStep`) verified with exact
values and a monotonicity check; the timer behavior is verified in headless windows short enough that the list
scrolls, by raising the platform's drag events (card and group drags through the `DragStarter` seam, folders as a
`DataTransfer` of storage items) at a point held in a scroll zone and calling the timer's handler
(`MainWindow.AutoScrollTick`) directly, since the headless platform does not advance real time; `IsAutoScrolling`
tells whether the timer runs. Whether Avalonia's `DispatcherTimer` ticks while the operating system's own drag loop
runs on Windows (OLE `DoDragDrop`) cannot be shown headlessly and must be confirmed by a UI run.

**A git that hangs.** `MainWindowViewModel` takes optional git time limits (`GitClientTimeouts`), passed to every card;
the headless test of `AgentControl-LauncherUI-GitTimeoutShown` uses a one-second local limit and a stub git whose
working-tree query sleeps past it, so the timeout happens within the test without any timing assertion: the test
awaits the card's working-tree check and asserts what the card shows.

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
  as an "Online source"; the sign-in dialog shows which server receives the credential, masks the password, answers
  once, and is shown one at a time; background upgrade checks never open it.
- The sign-in dialog's "Remember on this computer" option is off by default, enabled only when a credential helper
  can keep the sign-in and otherwise disabled with a tooltip that explains why, and the answer carries the choice.
- The source editor offers the signature options for NuGet sources only (disabled with the reason where unsupported)
  and, for a server, "Forget saved sign-in" only after a confirmation; the repo card's package chip shows the signature
  shield only when the source that supplies the package requires signed packages.
- A Git repository can be added from the Add menu; its editor explains the address, tag pattern, folder and sign-in,
  validates as the user types, lists the matching tags (or the tags that did not match, with a hint) after Test
  connection and saves only an acceptable source; a Git source is shown with the Git icon, "Online source" when remote;
  a card its package comes from names the tag, a refresh the user starts reads every source again (a newly published
  version or pushed tag is offered), and the card offers Change package..., which opens on the supplying source's
  choice and asks before
  applying changed content.
- The Settings source list shows a remote source as a neutral "Online source" (cloud icon, no warning styling, a
  tooltip saying when it is contacted), a "Signed only" badge only on NuGet sources that require signed packages
  (its tooltip naming a pinned publisher by the start and end of the fingerprint only), and, like the source editor,
  can be resized with its fields widening and a short window scrolling instead of clipping Save.
- A group header's menu starts with "Add repository...", which uses the same folder picker as the top-bar add, puts
  the picked repos (one or more) in that group (moving an already listed repo into it, and saying so), and changes
  nothing when cancelled.
- The About window shows a banner with the logo, name and tagline legible in both themes, is 600 wide with a limited
  height, and keeps the banner and Close in view while the details scroll in one region.
- A user group can be given a palette color or none from its header menu; the header shows an accent named in words,
  the tiles of its repos without a color of their own follow it, the accent reaches 3:1 in both themes, and the color
  and position follow a rename and are forgotten with the group.
- The groups are listed in the user's order (alphabetically until ordered) between Favorites and Ungrouped, are moved
  with the header menu's Move items (disabled with a reason at the ends and while filtering) and by dragging a header
  above or below another group's header (refused elsewhere and while filtering), and the order survives a restart.
- The picker accepts several folders and folders dropped from the file manager are added (into the group dropped on,
  otherwise ungrouped, with copy feedback that is cleared); only top folders of Git repositories are added, and one
  message says which folders were not added and why.
- While the folders of an add are checked or the cards refresh after a settings save, the UI thread stays free, the
  progress strip under the app bar shows the operation (with the count, and Cancel for an add only) and is gone
  afterwards; Cancel adds nothing and says nothing; a further add waits for the running one; a failed save asks Retry
  or Discard, Retry adds the folders and Discard leaves the list as saved; a shown card's working tree is checked in
  the background and shows "not checked yet" until git answers; and the strip is legible in both themes.
- However many cards are shown, never more than two repo reads run at once, each card waiting for or undergoing a read
  says it is refreshing until its state was applied, and a git that hangs is stopped and reported on its card.
- A drag held near the list's top or bottom edge, over the app bar or below the list keeps the list scrolling, faster
  the deeper the pointer is, redraws the drop feedback as the list moves and stops when the drag leaves, drops or
  ends.

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

**LauncherUI_AddRepo_GitRootAddedOthersRefusedWithAMessage**: In a headless main window whose folder picker seam
answers with one path, the app bar's Add adds a new folder that is the top folder of a Git repository as an ungrouped
card. It refuses, with a message, a folder that is already listed, which stays in its group
(`MainWindow_AddRepoButton_UsesSamePickerAndAddsUngrouped`). It refuses a folder that is not the top folder of a Git
repository (`MainWindow_AddRepoButton_FolderThatIsNotAGitRepository_IsRefusedWithAMessage`) and a folder that does
not exist (`MainWindow_AddRepoButton_FolderThatDoesNotExist_IsRefusedWithAMessage`), adding nothing. These cover
`AgentControl-LauncherUI-AddRepo`. The child `AgentControl-MainWindowViewModel-AddRepo` has unit tests that check the
returned results and the saved settings.

**LauncherUI_AddSeveralRepos_PickerTakesSeveralFoldersAndEachIsHandled**: In a headless main window whose folder picker
seam answers with several paths, the app bar's Add adds each repository, skips the folders that cannot be added and
tells the user in one message which were not added and why
(`MainWindow_AddRepoButton_SeveralFoldersPicked_AddsEachAndSummarizesTheRest`); a group header's "Add repository..."
puts every picked repository in that group, silently when all were added
(`MainWindow_HeaderMenuAddRepository_SeveralFolders_JoinTheGroup`). These cover
`AgentControl-LauncherUI-AddSeveralRepos` (child `AgentControl-MainWindowViewModel-AddRepos`).

**LauncherUI_AddRepoDropFolders_FoldersFromTheFileManagerAreAdded**: Folders dropped from the file manager (raised as
platform drag events with storage items for their paths) on a group's header, or on a card in a group's section, are
added to that group and no card is moved (`MainWindow_DropFolders_OnGroupHeader_AddsThemToTheGroup`); dropped on the
*Ungrouped* header, on the *Favorites* header or on empty space in the list they are added without a group
(`MainWindow_DropFolders_OnUngroupedFavoritesOrEmptySpace_AddsThemUngrouped`); dropped on the empty-state panel of a
window with no repo yet they are added (`MainWindow_DropFolders_OnEmptyStatePanel_AddsThem`); the drag is answered with
the copy effect, the header of the group they would join is tinted and the list area is outlined where they would be
added ungrouped, and the feedback is cleared when the drag leaves and after the drop
(`MainWindow_DropFolders_ShowsCopyFeedbackAndClearsIt`); and a dropped file and a dropped folder that is not a Git
repository are not added but reported, by name, in one message while the repository dropped with them is added
(`MainWindow_DropFile_IsSkippedAsNotARepository`). These cover `AgentControl-LauncherUI-AddRepoDropFolders` (child
`AgentControl-MainWindowViewModel-AddRepos`).

**LauncherUI_AddRepoGitRootOnly_FolderThatIsNotARepositoryIsRefused**: The app bar's Add with a picked folder that is
not the top folder of a Git repository adds nothing and shows a message that says why
(`MainWindow_AddRepoButton_FolderThatIsNotAGitRepository_IsRefusedWithAMessage`), and a dropped file or plain folder is
not added and is reported as not a repository (`MainWindow_DropFile_IsSkippedAsNotARepository`). These cover
`AgentControl-LauncherUI-AddRepoGitRootOnly` (child `AgentControl-MainWindowViewModel-AddGitReposOnly`, whose unit
tests include the subfolder, the `.git` file and the repos already listed).

**LauncherUI_AddRepoSummary_OneMessageForSeveralFolders**: Adding several picked folders of which some cannot be added
shows one summary message (`MainWindow_AddRepoButton_SeveralFoldersPicked_AddsEachAndSummarizesTheRest`), and so does
a drop of a repository with a file and a plain folder (`MainWindow_DropFile_IsSkippedAsNotARepository`); a single
folder keeps its single-add message. These cover `AgentControl-LauncherUI-AddRepoSummary` (child
`AgentControl-MainWindowViewModel-AddReposSummary`, whose unit tests check every wording and the five-name limit).

**LauncherUI_AddReposResponsive_WindowStaysUsableWhileFoldersAreChecked**: In a headless main window whose background
runner is a gate, two folders are dropped on the empty-state panel; while their checks are held the UI thread runs
other work, no card is added yet, and the strip shows "Adding 0 of 2 repositories..." (also the progress bar's
accessible name) with its Cancel button. Once the checks are released both folders are added, silently, and the strip
is gone (`MainWindow_DropFolders_WhileChecked_WindowStaysResponsiveAndShowsProgress`). With two repos in the group
`Work` and one in `Home`, two folders are dropped on the `Work` header; while their checks are held (the add is still
pending and the strip shown) clicking the `Home` header toggle collapses it at once and typing a filter narrows the
shown cards at once. Once released both folders are added into `Work`, silently, the shown cards follow the filter in
force (only the new folder that matches it appears) and the strip is gone; with the filter cleared `Home` stays
collapsed and the new repos follow `Work`'s stored repos in drop order
(`MainWindow_DropFolders_FilterAndCollapseWhileChecked_TakeEffectAtOnceAndApplyToTheNewCards`). The removal of repos
and groups during an add is verified through the child. This covers
`AgentControl-LauncherUI-AddReposResponsive` (child `AgentControl-MainWindowViewModel-AddReposInBackground`).

**LauncherUI_GlobalProgress_StripShownOnlyWhileBulkWorkRuns**: The strip is collapsed in an idle window, shows the add's
count and Cancel while dropped folders are checked and goes when they are added
(`MainWindow_DropFolders_WhileChecked_WindowStaysResponsiveAndShowsProgress`); it shows "Refreshing repositories 0 of
1..." without Cancel while the cards refresh after Settings are saved and goes when they are done
(`MainWindow_SettingsApplied_RefreshesCardsInBackgroundShowingProgress`); and in the light and the dark theme the bar
reaches 3:1 against its track and the strip and the status text 4.5:1 on the strip
(`ThemeResources_GlobalProgress_IsLegibleInLightAndDark`). These cover `AgentControl-LauncherUI-GlobalProgress`
(children `AgentControl-MainWindowViewModel-AddReposProgress` and the `GlobalProgressViewModel` unit requirements).

**LauncherUI_AddCancel_CancelStopsTheAddSilently**: While dropped folders are held at the gate the strip's Cancel button
is clicked; after the checks are released nothing is added or saved, no message is shown and the strip is gone
(`MainWindow_GlobalProgressCancel_WhileAddingFolders_AddsNothing`). This covers `AgentControl-LauncherUI-AddCancel`
(children `AgentControl-MainWindowViewModel-AddReposCancel`, `AgentControl-GlobalProgressViewModel-CancelOnce` and
`-NoCancelWhenNotCancelable`).

**LauncherUI_AddQueued_FurtherDropWaitsForTheRunningAdd**: While the first dropped folder is held at the gate a second
folder is dropped; only the first folder has been handed to the runner (the second add waits), and once released both
are added in drop order and the strip is gone (`MainWindow_DropFolders_WhileAnAddRuns_AddsThemAfterwards`). This covers
`AgentControl-LauncherUI-AddQueued` (child `AgentControl-MainWindowViewModel-AddReposQueued`, whose unit test also
shows a folder named by both adds is added once).

**LauncherUI_AddSaveFailureChoice_RetryOrDiscard**: With a save that fails ("The disk is full."), a folder picked from
the app bar's Add brings up the confirmation window with the reason and the buttons Retry and Discard, and nothing is
added meanwhile; after saving works again Retry adds and saves the folder and nothing more is shown
(`MainWindow_AddSaveFails_RetryAddsThem`); with every save failing, Discard adds nothing and a message says the folder
was not added and why (`MainWindow_AddSaveFails_DiscardLeavesListAsSaved`). These cover
`AgentControl-LauncherUI-AddSaveFailureChoice` (child `AgentControl-MainWindowViewModel-AddReposSaveFailure`, whose unit
tests show that moved repos keep their group, place and collapsed state on Discard and that no subscriber means
Discard).

**LauncherUI_SettingsApplyResponsive_CardsRefreshInTheBackground**: In a headless main window over one repo whose
background reads are held, Settings is opened and saved; the UI thread is free and the strip shows "Refreshing
repositories 0 of 1..." without Cancel; once the reads are released the refresh ends and the strip is gone
(`MainWindow_SettingsApplied_RefreshesCardsInBackgroundShowingProgress`). This covers
`AgentControl-LauncherUI-SettingsApplyResponsive` (children `AgentControl-MainWindowViewModel-ApplySettingsInBackground`
and `AgentControl-RepoCardViewModel-CheapRefreshInBackground`).

**LauncherUI_GitStatusInBackground_ShownCardIsCheckedWithoutBlocking**: A headless main window is shown over one repo
whose stub git reports a clean working tree, with the background reads held; the shown card's working-tree check has
started but the card is not checked yet and is not shown as dirty; once released the card is checked and can pull
(`MainWindow_CardShown_ChecksGitStatusInBackground`). This covers `AgentControl-LauncherUI-GitStatusInBackground`
(child `AgentControl-RepoCardViewModel-GitStatusInBackground`).

**LauncherUI_BoundedRepoReads_ShownCardsCheckedTwoAtATime**: A headless main window is shown, tall enough for several
cards, over six repos whose stub git reports clean working trees, with the background reads held; exactly two
working-tree checks have been handed to the runner, the UI thread is free, and every shown card (at least two) says it
is refreshing; once released every shown card is checked, none says it is refreshing any more, and never more than two
checks ran at once (`MainWindow_CardsShown_WorkingTreeChecksRunAtMostTwoAtOnce`). This covers
`AgentControl-LauncherUI-BoundedRepoReads` (children: the `AgentControl-RepoRefreshQueue` unit requirements,
`AgentControl-MainWindowViewModel-SharedRepoReads`, whose unit tests show that adds and the settings refresh share the
same limit, and `AgentControl-RepoCardViewModel-RefreshingState`).

**LauncherUI_GitTimeoutShown_CardSaysGitWasStopped**: A headless main window is shown over one repo whose stub git
hangs on its working-tree check, with a one-second git limit; the UI thread stays free, and once the check has ended
the card's status message is visible and says that git did not answer within 1 seconds and was stopped and to use
Refresh to try again, and the card's Git state is unknown (not clean, no Pull)
(`MainWindow_CardShown_GitHangs_CardSaysGitWasStopped`). This covers `AgentControl-LauncherUI-GitTimeoutShown` (child
`AgentControl-RepoCardViewModel-GitTimeoutShown`).

**LauncherUI_DragAutoScroll_ListKeepsScrollingWhileADragIsHeldNearItsEdges**: The step function gives no scroll in
the middle of the list and at a zone's inner boundary, 4.25 DIP per tick one pixel inside a 32-DIP zone, 12 at the
edge, the 36-DIP cap 96 DIP beyond the edge (over the app bar) or below the list and further out, zones that split a
viewport shorter than two zones in halves, and nothing for a zero, negative or unknown height or an unknown position
(`MainWindow_ComputeAutoScrollStep_ZonesAndDepth_GiveExpectedSteps`); the speed never drops as the pointer goes deeper
and never exceeds the cap (`MainWindow_ComputeAutoScrollStep_DeeperIsNeverSlower`). In a short headless window over
many repos: a card held still near the bottom edge keeps the list scrolling on every tick without a new drag event,
and the scroll stops when the drag leaves the list
(`MainWindow_DragCardNearListEdge_KeepsScrollingWhileHeldAndStopsOnLeave`);
a group header held over the app bar scrolls the list up, a drop there is refused (the groups keep their order) and the
drop stops the scroll (`MainWindow_DragGroupHeaderOverAppBar_ScrollsUpAndDropIsRefused`); folders held near the bottom
edge scroll the list and dropping them stops the scroll and adds them
(`MainWindow_DropFoldersNearBottomEdge_AutoScrollsAndDropStopsIt`); after a tick the insertion line is shown on
exactly one row, the card now under the unmoved pointer (`MainWindow_AutoScrollTick_UpdatesDropIndicatorForThePointer`);
and a card drag that ends without a drop stops the scroll (`MainWindow_CardDragEnds_StopsAutoScroll`). These cover
`AgentControl-LauncherUI-DragAutoScroll`. That the timer ticks during a real Windows drag is left to a UI run (see the
Verification Approach).

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

**LauncherUI_SortOrder_FavoritesFirstThenStoredOrder**: Displayed cards are checked for favorites being listed
first and the others in the user's stored order, whatever the launch times are: in the window (rows top to bottom,
`MainWindow_ExistingRepos_ShownInStoredOrder_FavoritesFirst`) and in the view model
(`MainWindowViewModel_DisplayedRepoCards_FavoritesFirstThenStoredOrder`); a stored order is kept as it is, and settings
without a stored order are arranged once as favorites first and then by recency. This scenario is tested by
`MainWindow_ExistingRepos_ShownInStoredOrder_FavoritesFirst`,
`MainWindowViewModel_DisplayedRepoCards_FavoritesFirstThenStoredOrder`, covering
`AgentControl-LauncherUI-SortOrder` (with `MainWindowViewModel_Constructor_StoredOrder_IsKeptWhateverTheLaunchTimes` and
`MainWindowViewModel_Constructor_NoStoredOrder_SeedsFavoritesFirstThenMostRecentlyLaunched` for the unit requirements
below it).

**LauncherUI_StableRepoOrder_LaunchNeverMovesTheCardAndNewReposGoToTheBottom**: In a shown main window (tested by
`RepoReorderWindowTests.cs`, with a fake process starter so nothing is started) launching the last repo leaves every
row where it was, and a repo added with the top-bar add and one added into a group appear at the bottom of their
sections. This scenario is tested by `MainWindow_Launch_DoesNotMoveTheCard` and
`MainWindow_RepoAdded_AppearsAtTheBottomOfItsSection`, covering `AgentControl-LauncherUI-StableRepoOrder`.

**LauncherUI_RepoOrderMenu_MoveItemsReorderAndDisableAtTheBoundaries**: The card menu's Organize group holds exactly
"Move to group", "Reorder" and "Card color..."; the Reorder submenu has an AutomationId, an icon and a tooltip and holds
the items Move to top, Move up, Move down and Move to bottom, each with an AutomationId, an icon and a tooltip; the
first repo's Top and Up items are disabled with the reason as their tooltip, the last repo's Down and Bottom items
likewise; choosing an enabled item (the menu item's click) reorders the rows; the Reorder submenu itself is disabled,
with the reason as its tooltip, for the only repo of its section and enabled for a repo with a neighbor. This scenario
is tested by `MainWindow_CardMenu_MoveItems_ReorderAndDisableAtTheBoundaries`,
`MainWindow_CardMenu_OrganizeGroupHasReorderSubmenuWithMoveItems` and
`MainWindow_CardMenu_Reorder_DisabledForTheOnlyRepoOfItsSection`, covering `AgentControl-LauncherUI-RepoOrderMenu`.

**LauncherUI_RepoDrag_ReordersWithinAndBetweenSectionsAndOnHeaders**: The headless platform has no drag source, so a
drag is started with a real pointer press and move on a card (which calls the window's `DragStarter` seam, here a fake
that records the dragged data and leaves the drag open) and finished with the platform's drag-enter, drag-over and
drop events raised on the window at points computed from the rows' bounds (upper half, lower half, header). Dropping
on the lower or upper half of another card of the same section reorders the rows
(`MainWindow_DragCard_WithinSection_ReordersAtDropPosition`) and in a list without groups a drop in the other tier is
refused with no drop effect, nothing becoming a favorite
(`MainWindow_DragCard_FlatList_OnlyReordersWithinItsTier`), covering `AgentControl-LauncherUI-RepoDragReorder`. Six
cases drop a card in another section - group to group, group to Ungrouped, Ungrouped to a group, into Favorites keeping
the group, out of Favorites into a group and out of Favorites into Ungrouped - and check the card's group, favorite
state, place and the stored values (`MainWindow_DragCard_IntoOtherSection_ChangesGroupOrFavoriteAtDropPosition`),
covering `AgentControl-LauncherUI-RepoDragBetweenSections`. A drop on a header, and on a collapsed group's header (the
group stays collapsed and its header says two repos), puts the card at the bottom of the group
(`MainWindow_DropOnGroupHeader_MovesCardToTheGroupBottom` and
`MainWindow_DropOnCollapsedGroupHeader_MovesCardAndKeepsTheGroupCollapsed`), covering
`AgentControl-LauncherUI-RepoDropOnHeader`.

**LauncherUI_RepoDropFeedback_LineAndTintShownAndClearedAndLegibleInBothThemes**: During a drag the dragged row has the
`dragging` class; the upper half of a card shows the line above it, the lower half the line below it (the previous
line is cleared), the dragged card itself shows nothing, a header over which the pointer is gets the `dropHeader` class,
leaving the list clears it, and after the drop no row has any of the classes and no indicator is visible
(`MainWindow_DragOver_ShowsInsertionIndicatorAboveOrBelowAndClearsItAfterwards`). The insertion line reaches 3:1
contrast against the card and the page in both themes and the group header's text keeps 4.5:1 on the drop tint
(`ThemeResources_DropIndicator_IsLegibleInLightAndDark`; `RepoDropIndicator` is also in the graphics checked by
`AppStyles_TextAndGraphicColors_MeetContrastInBothThemes`), covering `AgentControl-LauncherUI-RepoDropFeedback`.

**LauncherUI_ReorderOffWhileFiltering_NoDragAndMoveItemsDisabledWithTooltip**: With text in the filter a press and move
on a card starts no drag (the `DragStarter` seam is never called and the row is not dimmed) and the Reorder submenu and
all four Move items are
disabled with the filter explanation as their tooltip; with the filter cleared the drag starts and the items apply
again. This scenario is tested by `MainWindow_FilterActive_DragStartsNothingAndMoveItemsAreDisabledWithTooltip`,
covering `AgentControl-LauncherUI-ReorderOffWhileFiltering`.

**LauncherUI_RepoOrderRemembered_OrderSurvivesAWindowRestart**: After a drag and a menu move, the saved settings are
loaded into a new view model and a new window, which shows the rows in the same order. This scenario is tested by
`MainWindow_ReorderedList_OrderSurvivesAWindowRestart`, covering `AgentControl-LauncherUI-RepoOrderRemembered`.

**LauncherUI_GroupOrder_StoredOrderThenAlphabeticalBetweenFavoritesAndUngrouped**: A headless main window over settings
with a stored order of the groups lists Favorites first, then the groups in that order, then the groups without a
stored position alphabetically, then Ungrouped
(`MainWindow_GroupOrder_StoredOrderThenAlphabeticalBetweenFavoritesAndUngrouped`,
covering `AgentControl-LauncherUI-GroupOrder`, child `AgentControl-MainWindowViewModel-GroupOrder`).

**LauncherUI_GroupMoveMenu_MoveUpDownReorderAndDisableWithAReason**: A group header's menu moves the group up and down;
Move up is disabled for the first group and Move down for the last, each with the reason as its tooltip, and both are
disabled while the filter has text (`MainWindow_HeaderMenu_MoveUpDown_ReorderGroupsAndDisableAtTheEnds`); the menu
holds the items in the documented order (`MainWindow_HeaderMenu_AddRepositoryIsFirstItemBeforeSeparator`). These
cover `AgentControl-LauncherUI-GroupMoveMenu` (children `AgentControl-MainWindowViewModel-MoveGroup`,
`-MoveGroupAvailability` and `AgentControl-RepoGroupHeaderViewModel-MoveRequests`).

**LauncherUI_GroupDragReorder_HeaderDraggedAboveOrBelowAnotherGroup**: A group header's toggle is pressed and moved far
enough to start a drag through the window's `DragStarter` seam: the section is not collapsed or expanded by the press,
the header is dimmed, the line shows below the target group's header, the group lands below it, a collapsed group stays
collapsed and the feedback is cleared afterwards
(`MainWindow_DragGroupHeader_BelowAnotherGroup_ReordersGroupsWithLineFeedback`); a dragged header dropped on the
Favorites or Ungrouped header or on a card is refused and nothing changes, while a card drag keeps working
(`MainWindow_DragGroupHeader_OntoBuiltInSectionOrCard_IsRefused` and
`MainWindow_DragCard_WithinSection_ReordersAtDropPosition`); and while the filter has text a press and move on a group
header starts no drag, and the Favorites and Ungrouped headers never start one
(`MainWindow_FilterActive_GroupHeaderDragStartsNothing`). These cover `AgentControl-LauncherUI-GroupDragReorder`
(child `AgentControl-MainWindowViewModel-DropGroup`). The real operating-system drag of a header is covered by the
FlaUI test of `AgentControl-System-GroupReorder`.

**LauncherUI_GroupOrderRemembered_OrderSurvivesAWindowRestart**: A group order made in the window (a header drag and a
menu move) is what a new window over the saved settings shows
(`MainWindow_ReorderedGroups_OrderSurvivesAWindowRestart`, covering `AgentControl-LauncherUI-GroupOrderRemembered`,
child `AgentControl-MainWindowViewModel-GroupOrderPersisted`).

**LauncherUI_Launch_OnlyTheUsersCancelOfAFailedRestoreStopsIt**: The Launch command is executed for
a card whose agent files are already synced, recording the launch timestamp; the pre-launch check is exercised
confirming it returns without re-extracting when a pin without an identity finds all four folders present; and
separate scenarios confirm the agent-tool process still launches (raising `LaunchRecorded`) when the check has no pin
to work with or fails with nobody to ask, when the user keeps agent files that differ from the pin, and - the one
exception - does not launch and records nothing when agent files that were needed could not be installed and the user
cancels. This scenario is tested by
`RepoCardViewModel_LaunchCommand_Succeeds_RecordsLaunchTimestampAndRaisesLaunchRecorded`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedAndFoldersPresent_ReturnsTrueWithoutReExtracting`,
`RepoCardViewModel_LaunchCommand_SyncFailsOrNoPin_StillLaunches`,
`RepoCardViewModel_LaunchCommand_MismatchKeep_Launches` and
`RepoCardViewModel_LaunchCommand_RestoreFailsAndUserCancels_DoesNotLaunchOrRecordLaunch`, covering
`AgentControl-LauncherUI-Launch`.

**LauncherUI_VerifyAgentFilesBeforeLaunch_InstallsSilentlyComparesLocallyAndAsksOnDifference**: Through the real
card with real package fixtures: a repo with no agent files is given the pinned package without a question; a repo
whose files are the package's launches without a question and without any package-source access; and a repo that
lost half of its files while all four folders remain (the reported defect) raises the keep-or-update question, is
left untouched by Keep, and is restored in full by Update, after which the launch command starts the tool. The same
difference is asked about again on the next launch. In the headless main window the card's
`AgentFilesChoiceRequested` event opens the confirmation with the request's own labels and answers the request for
every way of closing it. This scenario is tested by
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoManagedFolders_RestoresSilentlyWithoutPrompt`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_FilesMatchPin_ReturnsInSyncWithoutPromptOrPackageAccess`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_HalfOfTheFilesMissing_AsksAndUpdateRestoresAllFiles`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_MismatchPersists_AsksOnEveryLaunch`,
`RepoCardViewModel_LaunchCommand_MismatchKeep_Launches`,
`RepoCardViewModel_LaunchCommand_MismatchUpdate_RestoresThenLaunches` and
`MainWindow_AgentFilesChoiceRequested_ShowsLabeledConfirmationAndAnswersRequest`, covering
`AgentControl-LauncherUI-VerifyAgentFilesBeforeLaunch`.

**LauncherUI_RestoreFailureChoice_OffersLaunchAnywayOrCancelWithCancelAsDefault**: When agent files that a launch needs
cannot be installed, the card raises a question with the reason and the labels "Launch anyway" and "Cancel"; Cancel
starts no process and records no launch, "Launch anyway" starts the tool. The confirmation window shows custom button
labels while keeping the declining button focused and Escape as a decline, returns true when the confirming button is
clicked, and rejects null arguments; the main window shows the question with those labels and answers it. This scenario
is tested by `RepoCardViewModel_LaunchCommand_RestoreFailsAndUserCancels_DoesNotLaunchOrRecordLaunch`,
`RepoCardViewModel_LaunchCommand_RestoreFailsAndUserChoosesLaunchAnyway_Launches`,
`ConfirmationWindow_CustomLabels_ShowTextAndKeepNoButtonAsDefault`,
`ConfirmationWindow_CustomLabelsConfirmClicked_ReturnsTrue`,
`ConfirmationWindow_CustomLabelsNullArgument_Throws` and
`MainWindow_AgentFilesChoiceRequested_ShowsLabeledConfirmationAndAnswersRequest`, covering
`AgentControl-LauncherUI-RestoreFailureChoice`.

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
genuine sync (package selection, upgrade, or the restore or update of agent files before launch), the
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
window over the main window and confirming the banner and its title, the mission tagline, version, copyright, and
license
text are all present and non-empty, and that the third-party dependency list control is present and
rendered with at least one entry. The application logo image is not automated-asserted beyond its presence (it has
no text content to check) and remains a visual-inspection-only element. The dependency list's
underlying data source (name/license entries matching the direct runtime dependencies in the application's
build-produced dependency manifest, no
duplicates, no empty fields, and the expected display format) has direct unit coverage. This scenario is tested by
`windows@AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright`,
`MainWindow_AboutClicked_OpensModalAboutOwnedByMainWindow`,
`ThirdPartyDependencies_All_MatchesTheApplicationsDirectRuntimeDependencies`,
`ThirdPartyDependencies_All_NoDuplicateNames`,
`ThirdPartyDependencies_All_EveryEntryHasNonEmptyLicense`,
and `DependencyInfo_ToString_RepresentativeEntry_ReturnsNameLicenseFormat`, covering
`AgentControl-LauncherUI-About`. The labeled Version row is the scenario
`LauncherUI_AboutVersionRow_VersionIsLabeledShortAndFullInTooltip` below.

**LauncherUI_AboutBanner_LogoNameAndTaglineLegibleInBothThemes**: The headless About window starts with a banner across
its full width, identified for assistive technology, that shows the logo, the name and the project's tagline above the
details (`AboutWindow_Banner_ShowsLogoNameAndTaglineAboveTheDetails`); in the light and the dark theme the banner's
name and tagline reach 4.5:1 on the band and on the band under each glow's strongest stop
(`ThemeResources_AboutBanner_TextIsLegibleInLightAndDark`). These cover `AgentControl-LauncherUI-AboutBanner`.

**LauncherUI_AboutScrollableDetails_BannerAndCloseStayInView**: The headless About window is 600 wide with a limited
height, the banner and the Close button are outside the one scrolling region, and every detail - tagline, version,
copyright, license and the dependency list - is inside it
(`AboutWindow_Layout_BannerAndCloseStayOutsideTheOneScrollingRegion`, covering
`AgentControl-LauncherUI-AboutScrollableDetails`).

**LauncherUI_SourceAwareActions_UnitTestsCoverSourceMemoryAndTargetedInvalidation**: Source-aware
select/upgrade/launch-sync behavior is verified by the `RepoCardViewModel`,
`SettingsWindowViewModel`, `SourceEditorViewModel`, `MainWindowViewModel`, and `SelectPackageWindowViewModel` unit tests
(see their unit verification designs), with
`RepoCardViewModel_RefreshCheap_NoSourceConfigured_ShowsNoSourceOffersProblemAndOffersNoUpgrade` and
`RepoCardViewModel_ApplySelectedPackage_SecondSourceSelected_ExtractsThatSourcesContentAndRemembersIt`
cited directly at the subsystem level, covering `AgentControl-LauncherUI-SourceAwareActions`
(children: `AgentControl-RepoCardViewModel-SourceAwareness`,
`AgentControl-MainWindowViewModel-InvalidateChangedSources`,
`AgentControl-SelectPackageWindowViewModel-SourceScoped`).

**LauncherUI_PackageSources_ListedWithKindLocationAndStatus**: The settings view model lists the configured
sources in order with their kind and location and whether their folder can be found, and a saved NuGet server
is listed as an "Online source" (a neutral status) without any folder check or network access; the status
text, cloud icon and tooltip of a remote row are checked, and a local folder row keeps its folder statuses.
This scenario is tested by
`SettingsWindowViewModel_Constructor_ListsConfiguredSourcesInOrder`,
`SettingsWindowViewModel_SaveServerSource_ListsItAsOnlineSourceAndPersistsIt`,
`PackageSourceItemViewModel_ServerSource_ShowsNeutralOnlineSourceStatus` and
`PackageSourceItemViewModel_LocalFolderSource_KeepsFolderStatuses`, covering
`AgentControl-LauncherUI-PackageSources`.

**LauncherUI_SourceSignedOnlyBadge_OnlyForSignedNuGetSources**: A NuGet source (folder or server) that requires
signed packages is flagged for the "Signed only" badge and a source that does not, a zip folder and a Git
repository are not; the badge's tooltip says signed packages are required and accepts any trusted signer, or with
a pinned publisher names it by the first eight and last four digits of the fingerprint without the rest; and the
Settings window shows the badge (`SourceSignedOnlyBadge`, with its tooltip) only on the signed rows next to the
unchanged status text. This scenario is tested by
`PackageSourceItemViewModel_RequiresSignature_OnlyForSignedNuGetSources`,
`PackageSourceItemViewModel_SignatureTooltip_NoPinnedPublisher_SaysAnyTrustedSigner`,
`PackageSourceItemViewModel_SignatureTooltip_PinnedPublisher_ShowsOnlyStartAndEndOfFingerprint` and
`SettingsWindow_SourceRows_ShowSignedOnlyBadgeOnlyWhereRequiredAndOnlineSourceStatus`, covering
`AgentControl-LauncherUI-SourceSignedOnlyBadge`. The badge colors are the neutral `Chip` resources, whose contrast
`AppStyles_TextAndGraphicColors_MeetContrastInBothThemes` already checks in both themes.

**LauncherUI_ResizableSourceDialogs_WidenFieldsAndScroll**: In the headless test application the Settings window and
the source editor of each kind (zip folder, NuGet folder, NuGet server, Git repository) are resizable, have a minimum
size, no maximum width and keep their default opening width; making the window 300 units wider makes the text boxes
(and, in Settings, the source list and a long location) at least 250 units wider; and a window made as short as its
minimum scrolls its form and keeps the Save button inside the window. This scenario is tested by
`SettingsWindow_Resizable_HasMinimumSizeAndWidensFieldsAndSourceList` and
`SourceEditorWindow_Resizable_HasMinimumSizeAndWidensFields`, covering
`AgentControl-LauncherUI-ResizableSourceDialogs`. Dialog focus, Tab and Escape behavior is covered by the existing
dialog tests, which pass unchanged.

**LauncherUI_GroupAddRepository_HeaderMenuAddsRepoToGroup**: The "..." menu of a user group's header has
"Add repository..." (`GroupAddRepoMenuItem`, with a tooltip) as its first item, then a separator, the Move up, Move
down and Group color items, another separator and then the rename and
remove items, each identified and explained; choosing it with the folder picker answered by a path adds a new card in
that group; a cancelled
picker changes nothing and shows no message; an already listed repo is moved into the group and a message says it was
already in the list; several picked folders all join the group, silently when all were added
(`MainWindow_HeaderMenuAddRepository_SeveralFolders_JoinTheGroup`); and the top-bar add still adds ungrouped through
the same picker and still refuses a repo that is already listed. This scenario is tested by
`MainWindow_HeaderMenu_AddRepositoryIsFirstItemBeforeSeparator`,
`MainWindow_HeaderMenuAddRepository_PickedFolderJoinsGroup_CancelChangesNothing`,
`MainWindow_HeaderMenuAddRepository_AlreadyListedRepo_MovesIntoGroupAndSaysSo`,
`MainWindow_HeaderMenuAddRepository_SeveralFolders_JoinTheGroup` and
`MainWindow_AddRepoButton_UsesSamePickerAndAddsUngrouped`, covering `AgentControl-LauncherUI-GroupAddRepository`
(children `AgentControl-RepoGroupHeaderViewModel-AddRepoRequest` and `AgentControl-MainWindowViewModel-AddRepoToGroup`).

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

**LauncherUI_RemovePackageSource_WarnsAboutDependentReposThenRemoves**: Removing the selected source first asks
for confirmation, describing how many repos get their package from it and that they will use another configured source
that delivers the identical files (or else show a package source problem), and then removes it. This scenario is
tested by
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

**LauncherUI_SignInCancelsWithOperation_TokenCancellationClosesDialog**: Canceling the question's token closes
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
shield icon, with the card's tooltip, only for a card whose supplying source requires signatures; the card's properties
name the start and end of a pinned fingerprint only. This scenario is tested by
`MainWindow_PackageChip_ShowsShieldOnlyWhenSignaturesAreRequired` and
`RepoCardViewModel_SupplyingSourceRequiresSignatures_ShowsShieldWithTooltip`, covering
`AgentControl-LauncherUI-SignatureIndicator`.

**LauncherUI_BackgroundChecksDoNotPrompt_UserStartedChecksMay**: A background upgrade check against a
server that needs a sign-in never opens the dialog and puts the instruction on the card - click the "Sign-in
needed" chip or use Refresh; a check the user
starts opens it once, finds the upgrade, and a later background check - even for another card - reuses the
remembered credential without asking. This scenario is tested by
`RepoCardViewModel_RefreshUpgradeStatusAsync_BackgroundCheckNeedsSignIn_DoesNotPromptAndExplains` and
`RepoCardViewModel_RefreshUpgradeStatusAsync_UserRequestedCheck_PromptsOnceThenBackgroundChecksReuseCredential`,
covering `AgentControl-LauncherUI-BackgroundChecksDoNotPrompt`.

**LauncherUI_RepoCardChips_PackageUpgradeBranchAndLaunchLabel**: A card whose package a zip source supplies
shows its package chip with name, version and source in the tooltip, a card supplied by a server source shows
the source's alias beside the server icon (a different icon from the zip and NuGet-folder icons), a card with no known
supplying source shows neither alias nor source in its chip, an upgrade
chip names the
target version, a branch chip shows the branch, and the Launch label names the configured tool.
This scenario is tested by
`RepoCardViewModel_PackageChip_SuppliedByZipSource_ShowsNameVersionAndSourceInTooltip`,
`RepoCardViewModel_PinnedToServerSource_ChipShowsAliasAndServerIcon`,
`RepoCardViewModel_PackageChip_NoSupplyingSource_HidesSourceAndNamesNone`,
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
cancellation as no error, and canceling a package apply that is waiting on a slow source raises no error and
leaves the pin unchanged. This scenario is tested by
`AsyncRelayCommand_Cancel_Running_SignalsTokenAndIsNotAnError` and
`RepoCardViewModel_ApplySelectedPackageAsync_CancelledWhileRunning_ReportsNoErrorAndKeepsPinUnchanged`,
covering `AgentControl-LauncherUI-CancelOperation`.

**LauncherUI_SourceFailureIsolation_OneUnreachableSourceAffectsOnlyItself**: The background upgrade refresh
keeps checking other cards when one source is unreachable, and the Select Package dialog loads the other
sources and shows an inline error for the unreachable one. This scenario is tested by
`MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_ReadableSourceStillOffersUpgrades` and
`SelectPackageWindowViewModel_LoadAsync_OneSourceUnreachable_OthersStillLoadAndErrorIsInline`, covering
`AgentControl-LauncherUI-SourceFailureIsolation`.

**LauncherUI_RetryFailedRead_OnlyTheFailedReadIsRepeated**: A read that failed can be retried on its own
without touching the other sources, and a retry that still fails keeps the error and the retry offer. This
scenario is tested by `SelectPackageWindowViewModel_Retry_SourceFailedThenAnswers_LoadsOnlyThatSource` and
`SelectPackageWindowViewModel_Retry_StillFailing_KeepsErrorAndRetryAvailable`, covering
`AgentControl-LauncherUI-RetryFailedRead`.

**LauncherUI_StaleResultsDiscarded_SupersededReadNeverOverwritesNewerState**: When a newer upgrade check starts the
older check's late answer is discarded, and a slower earlier version load is superseded by a later package selection.
This scenario is tested by `RepoCardViewModel_RefreshUpgradeStatusAsync_NewerCheckStarted_OlderResultIsDiscarded` and
`SelectPackageWindowViewModel_ChangeSelectedPackageName_SupersedesSlowerEarlierLoad`, covering
`AgentControl-LauncherUI-StaleResultsDiscarded` (children also:
`AgentControl-RepoCardViewModel-StaleUpgradeCheckDiscarded` and
`AgentControl-RepoCardViewModel-UpgradeStateResetOnRefresh`, whose unit tests are in the `RepoCardViewModel`
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
package IDs section; NuGet folder: with it, and canceling it adds nothing; server: feed-address controls; Git: the
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

**LauncherUI_KeepShellOpen_OptionIsOfferedNextToTheShellPreference**: The Settings window shows a "Keep the shell
open after the agent tool exits" checkbox in the Tools section directly after the shell preference, with a non-empty
tooltip, checked by default and bound both ways to the view model. This scenario is tested by
`SettingsWindow_Layout_KeepShellOpenCheckBoxFollowsShellPreferenceWithTooltip` (headless), covering
`AgentControl-LauncherUI-KeepShellOpenOption` (child: `AgentControl-SettingsWindowViewModel-KeepShellOpen`; the view
model's test is in its unit verification design).

**LauncherUI_KeepShellOpen_ChangeAppliesToTheNextLaunch**: With a recording process starter and a deterministic shell
detector handed to the main window view model, a launch asks for PowerShell with `-NoExit`; after settings that clear
the choice are applied (as Save does), the next launch of the same card asks for PowerShell without `-NoExit`; no
process is started. This scenario is tested by
`MainWindowViewModel_ApplySettings_KeepShellOpenChanged_NextLaunchUsesIt`, covering
`AgentControl-LauncherUI-KeepShellOpenApplied` (child: `AgentControl-RepoCardViewModel-KeepShellOpen`).

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

**LauncherUI_DialogKeyboard_TabStaysInsideTheDialog**: Settings shown as a modal dialog over a window that has the focus
keeps the focus inside the dialog through more Tab presses than the dialog has stops (the window behind never gets it;
the last control, Save, is reached and the focus wraps to the first) and wraps backwards with Shift+Tab; the
confirmation (Tab and Shift+Tab cycle between No and Yes) and the other small dialogs do the same. The Select Package
dialog is the one dialog whose keyboard behavior is verified end to end only, because it is opened from a repo card with
real package sources: its focus starts inside, Tab and Shift+Tab cycle inside it and never reach the window behind. This
scenario is tested by `SettingsWindow_ShownModally_FocusStartsInsideTabCyclesInsideAndReturnsToOwner`,
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
  its group on its card): `MainWindow_GroupedRepos_ShowHeadersWithNameCountAndState`,
  `MainWindow_FavoriteInGroup_ShowsGroupChip` and
  `MainWindow_GroupOrder_StoredOrderThenAlphabeticalBetweenFavoritesAndUngrouped` (the groups in the user's stored
  order, then alphabetically, between Favorites and Ungrouped).
- `AgentControl-LauncherUI-RepoGroupsSettingsOnly` (groups, card colors, group colors, the group order and collapsed
  sections only in the user's settings): `MainWindow_GroupsColorsAndOrder_WriteNothingIntoTheRepository`
  (`RepoReorderWindowTests.cs`). Through the window a repo is moved into a group from the card menu, a card color and
  a group color are chosen from the palette pop-ups, a group is moved down from its header menu and a section is
  collapsed. Every repo folder keeps exactly the entries it had, and the settings file holds the group, the card
  color, the group color, the order and the collapsed section.
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
next start, and when other settings change); and
`MainWindowViewModel_FilterText_ShowsMatchesInCollapsedGroupsAndHidesEmptyOnes` and
`MainWindowViewModel_FilterText_NoMatch_ShowsNothing` for `AgentControl-LauncherUI-RepoGroupFilter` (the filter
shows every match, also in a collapsed section, with the count of matches only, no header for a section without a
match and the collapse toggles disabled and explained meanwhile). The
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

**LauncherUI_GroupColor_PalettePopupColorsTheHeaderAndNoneClears**: A headless main window opens the group palette from
a user group's "Group color..." item and finds a swatch for every palette color and "None", each identified and
explained; choosing a swatch colors the group - an accent on its header, named in its tooltip and accessible name, and
the tiles of its uncolored repos - marks the swatch and closes the palette, and "None" takes the color away
(`MainWindow_GroupColorPalette_ChoosingSwatchColorsHeaderAndNoneClears`). The shared `ColorPalettePanel` is checked on
its own: one swatch per palette color in palette order, each with the AutomationId of its prefix and name, its name as
accessible name, tooltip and tag and the swatch fill of its palette index, plus a heading and a clear button named
from the prefix (`ColorPalettePanel_Swatches_OnePerPaletteColorWithPrefixedIdsNamesAndTooltips`); the chosen color, in
any letter case, marks only its own swatch, and with none chosen the clear button shows its check mark
(`ColorPalettePanel_SelectedColorName_MarksOnlyThatSwatch`); and a swatch click reports its color while the clear button
reports none (`ColorPalettePanel_SwatchOrClearClicked_RaisesColorChosen`). These cover
`AgentControl-LauncherUI-GroupColor` (children `AgentControl-MainWindowViewModel-GroupColor`, `-GroupColorShown` and
`AgentControl-RepoGroupHeaderViewModel-ShowsGroupColor`).

**LauncherUI_GroupColorOnCards_TilesFollowTheGroupUnlessTheyHaveTheirOwn**: In a headless main window the tiles of a
colored group's repos without a card color of their own - a favorite in the group included - take the group's color,
a card color of its own wins, and the card palette's heading says when the tile follows the group's color
(`MainWindow_GroupColor_TilesWithoutOwnColorFollowAndOwnColorWins`, covering
`AgentControl-LauncherUI-GroupColorOnCards`, child `AgentControl-RepoCardViewModel-GroupColorFallback`).

**LauncherUI_GroupColorLegible_AccentReachesGraphicsContrast**: In the light and the dark theme every group accent
brush reaches 3:1 on the page a header is drawn on and on the drop tint a header gets while something is dragged over
it (`ThemeResources_GroupAccent_MeetsGraphicsContrastInLightAndDark`, covering
`AgentControl-LauncherUI-GroupColorLegible`).

**LauncherUI_GroupSettingsKeptInStep_ColorAndPositionFollowTheGroup**: In the headless main window, a group with a
color and a stored place is renamed through its header menu and the name prompt: the header keeps the color accent
(named "Group color: Teal") and its place in the list, also in the saved settings
(`MainWindow_RenameGroup_KeepsItsColorAccentAndItsPlace`). A group removed from its header menu, and a group whose last
repo leaves it through "Remove from group", lose both: their color and place are gone from the saved settings, and a
new group of the same name is listed alphabetically without an accent
(`MainWindow_RemoveGroupOrItsLastRepo_ForgetsItsColorAccentAndItsPlace`). Saving the Settings window keeps the accent
and the order (`MainWindow_SettingsSaved_KeepsGroupColorAccentsAndOrder`). These cover
`AgentControl-LauncherUI-GroupSettingsKeptInStep` (children
`AgentControl-MainWindowViewModel-GroupSettingsFollowRenameAndRemove` and `-GroupSettingsKeptOnApplySettings`).

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

**LauncherUI_PortablePins_AnotherSourceWithTheIdenticalFilesSuppliesThePinnedPackage**: A repo's pin names no source:
the card finds the supplying source among the user's own configured sources. A configured source - whatever its alias
or kind - that delivers the identical files (the first one in Settings order) is shown on the package chip with no
source problem; a source that offers the same name and version with other files before it is skipped; with sources that
offer the version but none delivering the recorded files the card shows the source problem and explains that the
version now contains different files (the package may have been republished) and what to do; with no source offering
the version, or no source configured, the card shows the source problem saying so, and still offers a newer version a
source does offer (the problem and the upgrade coexist); a pin that recorded no files identity is served by the source
that last supplied it, then the source an older pin file named, then the first source that offers it, and is never
refused; the restore before launch uses only a source verified by the identical files (the last supplier first) and
never writes the pin; and the upgrade check offers the newest version the configured sources offer, which Upgrade
applies from the source that offers it, recording no source. This scenario is verified by the `RepoCardViewModel` unit
tests (see their unit verification design), with
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SourceNamedDifferently_RestoresFromIdenticalSourceWithoutWritingPin`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_OnlyOtherFilesAvailable_RefusesWithExplanation`,
`RepoCardViewModel_Refresh_PinWithoutFilesIdentity_UsesLegacySourceHintWhenConfigured`,
`RepoCardViewModel_Refresh_SourceNamedDifferentlyHasIdenticalFiles_NamesSupplierWithoutProblem`,
`RepoCardViewModel_Refresh_OnlySourceHasDifferentFiles_ShowsNoMatchingFilesProblem`,
`RepoCardViewModel_Refresh_NoSourceOffersPinnedVersion_ShowsNoSourceOffersProblem`,
`RepoCardViewModel_Refresh_NoSourceOffersPinnedVersionButNewerOffered_ShowsProblemAndStillOffersUpgrade` and
`RepoCardViewModel_UpgradeCommand_SeveralSources_UsesNewestVersionAcrossSourcesAndRecordsNoSource` cited
directly at the subsystem level, and by the headless window tests
`MainWindow_PinNamesNoSourceAndIdenticalFilesElsewhere_ChipNamesSupplierAndShowsNoProblem` (the chip names the
supplying source and the problem chip is not shown, with real zip sources and a pin written by a real apply) and
`MainWindow_PinNamesNoSource_UpgradeChipOffersNewestVersionFromConfiguredSource` (the upgrade chip is offered for
the newest version of the configured source), covering `AgentControl-LauncherUI-ResolveSourceByFiles` (children:
`AgentControl-RepoCardViewModel-ResolveByFilesIdentity`, `-PreferLastSupplier`, `-ResolveRejectsDifferentFiles` and
`-ResolveWithoutIdentityByHints`), `AgentControl-LauncherUI-SupplyingSourceShown` (children:
`-ChipNamesSupplyingSource`, `-NoSourceOffersProblem`, `-UnresolvedPinExplained` and `-UnreadableSourceIsNotAProblem`)
and `AgentControl-LauncherUI-UpgradeAcrossSources` (children: `-UpgradeAcrossSources` and
`-ProblemKeepsUpgradeOffered`; each child prefixed `AgentControl-RepoCardViewModel`).

**LauncherUI_ContentChangeProtection_RepublishedVersionIsNeverAppliedUnnoticed**: A package version republished with
other files is not applied silently: an Upgrade and the restore or update of agent files before launch refuse it and
say so (before launch the user then chooses to launch anyway or cancel), and a Select Package of the pinned version
raises the confirmation request. In the main window the request
becomes a modal confirmation whose answer No changes nothing (the repo and the pin keep what they had) and whose
answer Yes applies the changed package and records its new identity; a pin without an identity adopts one silently.
This scenario is verified by the `RepoCardViewModel` unit tests (see their unit verification design), with
`RepoCardViewModel_ApplySelectedPackage_FirstApply_RecordsFilesIdentityInPin`,
`RepoCardViewModel_UpgradeCommand_SameVersionRepublished_RefusesWithoutChangingRepoOrPin`,
`RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_SameVersionRepublished_RefusesWithoutExtractingOrWritingPin`,
`RepoCardViewModel_ApplySelectedPackage_SameVersionRepublished_RequestsConfirmationAndChangesNothing` and
`RepoCardViewModel_ApplySelectedPackage_PinWithoutIdentity_AdoptsIdentityWithoutWarning` cited directly at the
subsystem level, and by the headless window test
`MainWindow_SelectingRepublishedPinnedVersion_AsksFirstAndAppliesOnlyWhenConfirmed` (the question is asked before
anything changes; No changes nothing; Yes applies and records the new identity). The requirements are covered as
follows: `AgentControl-LauncherUI-ContentChangeProtection` (the identity is recorded; child
`AgentControl-RepoCardViewModel-RecordContentIdentity`) by the first test;
`AgentControl-LauncherUI-ContentChangeRefusal`
(children: `AgentControl-RepoCardViewModel-RefuseChangedContent`, `-RefuseChangedContentMessage` and
`-LaunchSyncNeverWritesPin`) by the Upgrade and the launch-restore tests;
`AgentControl-LauncherUI-ContentChangeConfirmation` (children: `-ConfirmChangedContent`, `-ApplyConfirmedContent` and
`-RefuseChangeWithoutAsking`) by the confirmation-request test and the window test; and
`AgentControl-LauncherUI-ContentChangeAdoption` (children: `-AdoptMissingIdentity` and
`-IgnoreMissingSourceIdentity`) by
the pin-without-identity test. As the evidence rules above say, these are view-model tests of the units that realize the
behavior, except the headless window test: the behavior is observable only in the view-model layer the window is drawn
from, so no window-level test exists for the refusal.
The card offers "Change package..." to a repo that has a pin (and "Select Package..." only to one that has none), so the
confirmation is reached through the card's menu: the Change package scenario below shows the dialog
opening on the pinned
choice, and the end-to-end scenarios of the system verification
(`AgentControl_PackageContentIntegrity_MovedGitTagIs...`)
take it through to the confirmation with a real Git repository.

**LauncherUI_ConvertCommittedAgentFiles_AsksFirstAndConvertsOnlyWhenConfirmed**: Applying a package to a repo whose
managed agent folders are tracked by git asks first, in a modal question, and changes nothing until it is answered.
Against a real temporary git repository that tracks an agents folder and a README, with a package source serving one
package, the headless window test
`MainWindow_ApplyingToRepoWithTrackedAgentFolders_AsksFirstAndConvertsOnlyWhenConfirmed` applies the package and finds
one `ConfirmationWindow` owned by the main window while the card is still busy and no pin file exists; answering No
leaves the question gone, the card idle, the file tracked by git, the old file on disk and the pin absent; applying
again and answering Yes leaves git no longer tracking the agents folder, the old file replaced by the package, the pin
written, the status message telling the user to commit the change and the committed-files badge cleared. The same test
is the system-level evidence of `AgentControl-System-ConvertCommittedAgentFiles` (the real window and card with a real
git repository; it is not a UI-automation test), and
`RepoCardViewModel_ApplySelectedPackage_TrackedAgentFoldersDeclined_ChangesNothing` is cited at the subsystem level
besides it, covering `AgentControl-LauncherUI-ConvertCommittedAgentFiles` (children
`AgentControl-RepoCardViewModel-CheckTrackedFoldersFresh`, `-ConfirmBeforeConverting`, `-DeclineConvertChangesNothing`,
`-NeverConvertWithoutAsking`, `-ConvertTrackedFolders`, `-ConvertedRepoStatus`, `-StopWhenUntrackFails` and
`-NoConvertWhenNotTracked`); the other behavior - only the tracked folders are untracked, the untrack failure, no
question for an untracked repo, a stale badge - is verified by the `RepoCardViewModel` unit tests (see the
`RepoCardViewModel` unit verification design).

**LauncherUI_GitSourceSetup_PromptOnlyForUserStartedWork**: The registry the card builds for Git sources runs git with
the configured git executable (or the one the owner supplies) and caches Git packages inside the isolated configuration
directory; the registry for work the user started lets git show its credential UI, and the registry for background work
never does; when the background check finds that the installed git is older than the supported version the
card's source problem says so and does not ask the user to sign in, and no upgrade is offered.
This scenario is tested by
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
`SourceEditorWindow_GitSource_Save_ClosesWithTheGitDefinition` (the windows,
against a `FakeGitSourceProvider`, no git or
network), and the settings view model's refusal of a Git source whose settings the application would not accept is
shown by
`SettingsWindowViewModel_Save_GitSourceWithBadTagPattern_IsRefusedWithErrorMessage` (unit evidence cited directly),
covering
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

**LauncherUI_GitSourcePresentation_IconTypeNameStatusAndTag**: A Git source has the Git icon and the type name "Git
repository" (and the short name "git repository") in the lists that show sources; the Select Package source list shows
the same icon and name; a Git source on a remote address is listed as an "Online source" and never asks for its folder,
while one on a local path gets the folder status, and the classification of the zip, NuGet folder and server kinds is
unchanged; and a card whose package a Git source supplies shows the Git icon and the alias in its package chip. This
scenario is tested by `PackageSourceIcons_GitSource_HasGitIconAndNames`,
`SelectPackageWindowViewModel_Constructor_GitSource_ShowsGitIconAndTypeName` and
`RepoCardViewModel_SuppliedByGitSource_ShowsGitIconAndAlias` (covering `AgentControl-LauncherUI-GitSourcePresentation`,
child `AgentControl-RepoCardViewModel-GitSourceAwareness`),
`PackageSourceItemViewModel_RemoteGitSource_IsOnlineSourceAndNeverAsksTheFolder`,
`PackageSourceItemViewModel_LocalGitSource_ChecksTheFolder` and
`PackageSourceItemViewModel_OtherKinds_RemoteClassificationUnchanged` (covering
`AgentControl-LauncherUI-GitSourceRowStatus`, children `AgentControl-SettingsWindowViewModel-GitSourceRowStatus`,
`-GitSourceLocalFolderStatus` and `-GitSourcePersisted`) and
`RepoCardViewModel_PackageChipTooltip_GitSource_NamesTagOnly` (the tag in the chip's tooltip, with no commit, covering
`AgentControl-LauncherUI-GitSourceCardTooltip`, child `AgentControl-RepoCardViewModel-GitSourceTooltip`; the tag follows
the source's current tag pattern, by `RepoCardViewModel_PinnedTagName_TagPatternEdited_FollowsTheSourcesPattern`, and a
zip source has none, by `RepoCardViewModel_PackageChipTooltip_ZipSource_HasNoTag`), and by the headless main window test
`MainWindow_GitSourceCard_ChipShowsGitIconAliasAndTag`: the chip drawn for a repo supplied by a Git source holds the Git
icon, the alias and the package, and its tooltip names the tag (the window-level evidence of both
`AgentControl-LauncherUI-GitSourcePresentation` and `AgentControl-LauncherUI-GitSourceCardTooltip`; the source is a
local folder, so nothing is fetched). The drawn icon is shown only as ink in the end-to-end test
(`SettingsWindow_AddMenu_OffersAllFourSourceTypes...`).

**LauncherUI_RefreshRelistsSources_NewVersionIsOfferedAndBackgroundChecksUseTheCache**: A card refresh the user starts
reads every configured source again - Git, NuGet and zip folder - so a version published after a listing was cached is
offered as the upgrade (a tag pushed to a real local repository, a zip added to a real folder, a version published to
an in-memory source), both of two Git sources are listed again, while the background check keeps using the cached
listing. This scenario is tested by
`RepoCardViewModel_RefreshAsync_GitSourceNewTagPushedAfterCachedListing_ListsAgainAndOffersNewVersion` and
`RepoCardViewModel_RefreshAsync_ZipFolderGainedVersionAfterListing_OffersUpgradeAfterRefresh` (covering
`AgentControl-LauncherUI-RefreshRelistsSources`, child `AgentControl-RepoCardViewModel-RefreshRelistsSources`),
`RepoCardViewModel_RefreshAsync_SeveralGitSources_ListsEachOfThemAgain` and
`RepoCardViewModel_RefreshAsync_NonGitSource_ListsAgain`.

**LauncherUI_SelectPackageReadsSourcesFresh_OpeningTheDialogShowsWhatTheSourcesOfferNow**: Opening the Select Package
dialog reads the sources again, so a package or version published since an earlier listing is among the choices. This
scenario is tested by `SelectPackageWindowViewModel_LoadAsync_SourceChangedSincePreviousListing_ShowsTheNewContent`,
covering `AgentControl-LauncherUI-SelectPackageReadsSourcesFresh`, child
`AgentControl-SelectPackageWindowViewModel-ReadsSourcesFresh`.

**LauncherUI_PeriodicUpgradeRecheck_BadgesFollowNewVersionsWhileTheWindowIsOpen**: While the main window is open the
cards' upgrade availability is checked again every five minutes, and the check stops when the window closes. The
view-model tests run on a fake clock: a version published after the launch-time check is shown as an upgrade once the
interval has passed (and not before), and no round runs after the view model is disposed. The wiring in the window
(the timer is started when the view model is attached, its ticks are posted to the UI thread, and the view model is
disposed when the window closes) is covered by a headless main window test that attaches a view model on a fake clock,
shows that a published version appears on the card after the interval, closes the window and shows the view model is
disposed. This scenario is tested by
`MainWindowViewModel_PeriodicRecheck_VersionPublishedAfterLaunchCheck_ShowsUpgradeAfterInterval`,
`MainWindowViewModel_Dispose_StopsThePeriodicRecheck` and
`MainWindow_ViewModelAttachedThenWindowClosed_StartsPeriodicRecheckAndDisposesViewModel`, covering
`AgentControl-LauncherUI-PeriodicUpgradeRecheck`,
children `AgentControl-MainWindowViewModel-PeriodicUpgradeRecheck` and
`AgentControl-MainWindowViewModel-PeriodicRecheckLifecycle`.

**LauncherUI_SignInNeededChipSignsIn_ClickingTheChipRefreshesTheCard**: In a headless main window, a card whose
background check found that its source needs a sign-in shows the clickable "Sign-in needed" chip - visible, enabled,
named "Sign-in needed" for assistive technology, with a tooltip that says to click it or use Refresh - and not the
plain problem chip; a real mouse click on it refreshes the card (the source is read again), and once the source
answers the chip is gone and the upgrade is offered. A card with any other source problem (no configured source)
shows the plain chip with the problem in its tooltip and no sign-in chip. This scenario is tested by
`MainWindow_SignInNeededChipClicked_RefreshesTheCardAndClearsTheChip` and
`MainWindow_SourceProblemOtherThanSignIn_ShowsTheTooltipChipNotTheButton`, covering
`AgentControl-LauncherUI-SignInNeededChipSignsIn`, child `AgentControl-RepoCardViewModel-SignInNeededChip`.

**LauncherUI_SignInNotGivenKeepsChip_CancelOrRejectKeepsTheChip**: In a headless main window whose only card's
background check found that its source needs a sign-in, the chip's tooltip is noted; the source then answers as if
the user canceled the sign-in dialog (one case) or the server rejected the sign-in typed (the other case), giving the
sign-in-needed words with the failure, and a real mouse click on the chip refreshes the card. The source is read
again, and the clickable chip is still visible and enabled, named "Sign-in needed", with exactly the same tooltip,
the plain problem chip stays hidden, and the card's status is "Needs attention" rather than "Update check failed".
This scenario is tested by `MainWindow_SignInNeededChipClicked_SignInNotGiven_KeepsTheChip`, covering
`AgentControl-LauncherUI-SignInNotGivenKeepsChip`, child `AgentControl-RepoCardViewModel-SignInNotGivenStillNeeded`.

**LauncherUI_SettingsClosedRechecksProblems_CardsWithAProblemAreCheckedAgain**: In a headless main window whose only
card needs a sign-in, the Settings window is opened from the main window; while it is open the source starts to
answer, and closing Settings without saving checks the card again, so the sign-in chip is gone and the upgrade is
offered. Saving the settings instead checks the card once (the save's own check) and closing the window after the
save reads the source no further time. This scenario is tested by
`MainWindow_SettingsClosedWithoutSaving_RechecksTheCardThatNeedsSignIn` and
`MainWindow_SettingsSaved_ChecksEachCardOnce`, covering `AgentControl-LauncherUI-SettingsClosedRechecksProblems`,
child `AgentControl-MainWindowViewModel-RecheckAfterSettingsClosed`.

**LauncherUI_ChangePackage_PinnedRepoChoosesAgainOnTheCurrentChoice**: For a repo that has a pin the select-package
command can execute and raises the request ("Change package..."); the main window opens the Select Package dialog on the
source that supplies the package (the card's remembered supplier), the pin's own package and version although a newer
version exists; and choosing the pinned version again when its
content changed - a republished package, or a Git tag moved to a commit with other files - asks for confirmation first,
No changing nothing and Yes applying and recording the new identity. This
scenario is tested by `RepoCardViewModel_SelectPackageCommand_PinAlreadyExists_CanExecuteAsChangePackage` (covering
`AgentControl-LauncherUI-ChangePackage`, children `AgentControl-RepoCardViewModel-ChangePackage` and
`-ChangePackageRequest`), `MainWindow_ChangePackageOnPinnedRepo_OpensSelectPackageOnThePinnedChoice` (also covering
`AgentControl-LauncherUI-ChangePackageStartsOnCurrentChoice`, children:
the `AgentControl-SelectPackageWindowViewModel-Preselect*`
requirements: `-PreselectCurrentPackage`, `-PreselectNewestWhenVersionGone`, `-PreselectNothingWhenSourceFailed`,
`-PreselectNothingWhenNotOffered`, `-PreselectNothingWhenSourceUnknown`, `-PreselectKeepsUserChoice` and
`-PreselectCancelled`) and `MainWindow_SelectingRepublishedPinnedVersion_AsksFirstAndAppliesOnlyWhenConfirmed` (the
confirmation, covered above).
