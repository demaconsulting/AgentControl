### SwitchBranchViewModel

#### Verification Approach

`SwitchBranchViewModel` is verified with unit tests in `SwitchBranchViewModelTests.cs`. Each test constructs a real view
model over a function that returns a canned `GitBranchList` (so no git runs), loads it, types a filter, selects rows and
executes the commands, and reads the rows, the texts and `CanSwitch`; the background work runner is the real thread-pool
one, or a `GatedBackgroundWorkRunner` (a test helper of the `LauncherUI` tests) that holds the read until the test
releases it, so a test asserts that the calling thread - which the tests guard against blocking calls - stays free while
the read is outstanding. That the card builds the view model over its own git and lists what git reports is verified
with a stub git in `RepoCardSwitchBranchTests.cs`; the dialog as the user meets it (its controls, the filter box, Enter,
double-click, Escape, Cancel) by the headless window tests in `SwitchBranchWindowTests.cs`, which load the branches
through the real runner and wait for the UI thread's jobs. No file or process is involved in the unit tests.

#### Test Environment

N/A - standard test environment for the unit tests (no window, no files and no processes are involved); the window
tests use the shared headless Avalonia session (`HeadlessTestApp`).

#### Acceptance Criteria

- The loaded list holds the local branches first, then the remote-tracking ones, each remote one with its
  remote-qualified name and a caption naming its remote; the checked-out branch is marked and cannot be chosen; the
  repo's position is stated, including a detached HEAD with its commit.
- The filter lists only the branches whose name contains the text, ignoring letter case; it says so when none matches,
  drops a selection that was filtered out and selects the only branch left that can be chosen.
- The dialog completes exactly once - with the chosen branch (a double-click confirms a row), or with none when
  canceled - whatever is done afterwards, and does nothing without a branch that can be chosen.
- The branches are read through the background work runner, so the calling thread is free while the read is
  outstanding; a failed read becomes text (with credentials removed) and nothing can be chosen; a canceled read shows
  nothing.
- The window opens with the focus in the filter box, shows the progress bar only while loading, keeps Switch disabled
  until a branch other than the current one is selected, and closes with the chosen branch or with none for Escape,
  Cancel and closing the window, which also stops a read that is still running.

#### Test Scenarios

**SwitchBranchViewModel_ListsLocalAndRemote**: A repo on `main` with another local branch and two remote branches is
loaded: the rows are the two local branches, then the two remote ones, with the names git is given
(`origin/feature/billing`), the captions (`current`, `remote origin`, none), the title and the hint that names Git Pull.
This scenario is tested by `SwitchBranchViewModel_Load_ListsLocalThenRemoteAndMarksCurrent` and, with the rows a card's
git reports, `RepoCardViewModel_CreateSwitchBranchViewModel_ListsTheBranchesGitReports`, covering
`AgentControl-SwitchBranchViewModel-ListsLocalAndRemote`.

**SwitchBranchViewModel_MarksCurrent**: The checked-out branch is marked and the text says `Currently on main`;
selecting it keeps Switch and the command disabled, selecting another branch enables them. This scenario is tested by
`SwitchBranchViewModel_Load_ListsLocalThenRemoteAndMarksCurrent` and
`SwitchBranchViewModel_CurrentBranchSelected_CannotSwitch`, covering
`AgentControl-SwitchBranchViewModel-MarksCurrent`.

**SwitchBranchViewModel_DetachedHead**: A repo with no checked-out branch and a named commit says `Detached HEAD at
1a2b3c4`, marks no row current and allows any branch to be chosen; a repo without any branch says so and a detached
HEAD without a commit says only `Detached HEAD`. This scenario is tested by
`SwitchBranchViewModel_DetachedHead_ShowsDetachedTextAndAllowsSwitch` and `SwitchBranchViewModel_NoBranches_SaysSo`,
covering `AgentControl-SwitchBranchViewModel-DetachedHead`.

**SwitchBranchViewModel_Filter**: Typing `FEATURE` lists the two branches that contain it in any letter case, `origin/`
lists the remote branches (the remote's name counts), a text that matches nothing lists none and says so, clearing the
filter lists everything; a selection that the filter removes is dropped, a filter that leaves one branch to choose
selects it, and one that leaves only the current branch selects nothing. This scenario is tested by
`SwitchBranchViewModel_Filter_MatchesCaseInsensitiveSubstring` and
`SwitchBranchViewModel_Filter_OneBranchLeft_SelectsItAndDropsFilteredOutSelection`, covering
`AgentControl-SwitchBranchViewModel-Filter`.

**SwitchBranchViewModel_ConfirmChoice**: Selecting a remote branch and executing the switch command completes once with
a choice carrying its display name, its remote-qualified name and the remote flag; a later cancel or switch completes
nothing more; confirming a row (a double-click) selects and completes with it, while confirming the current branch
completes nothing. This scenario is tested by `SwitchBranchViewModel_Switch_CompletesOnceWithChoice` and
`SwitchBranchViewModel_Confirm_SelectsRowAndCompletes`, covering `AgentControl-SwitchBranchViewModel-ConfirmChoice`.

**SwitchBranchViewModel_LoadsInBackground**: A load started on a guarded thread with its read held at the gate records
no blocking call, shows the loading state and no rows while held, and lists the branches once the gate opens, the read
having run off the guarded thread; a load whose token is canceled throws `OperationCanceledException` and shows no
error. This scenario is tested by `SwitchBranchViewModel_Load_RunsOffTheUiThread` and
`SwitchBranchViewModel_Load_Canceled_ThrowsCanceledWithoutShowingAnError`, covering
`AgentControl-SwitchBranchViewModel-LoadsInBackground`; that the window starts the load when it opens and stops it when
it closes is shown by `SwitchBranchWindow_Opened_ListsBranchesAndFocusesFilterBox` and
`SwitchBranchWindow_ClosedWhileLoading_ReturnsNullAndStopsTheRead` (the progress bar is shown while the read runs, the
dialog returns no branch when closed meanwhile).

**SwitchBranchViewModel_LoadFailureShown**: A read that throws with an address carrying a password in its text shows
`The branches could not be read:` and the text without the password, lists nothing, says nothing about an empty list
and cannot be switched; in the window the text is shown in place of the list and Switch is disabled. This scenario is
tested by `SwitchBranchViewModel_Load_Fails_ShowsErrorAndCannotSwitch` and
`SwitchBranchWindow_ReadFails_ShowsErrorAndDisablesSwitch`, covering
`AgentControl-SwitchBranchViewModel-LoadFailureShown`.

**SwitchBranchViewModel_CancelCompletesWithNone**: Executing the cancel command and then calling `Cancel` completes once
with no branch; in the window Escape and the Cancel button each return no branch. This scenario is tested by
`SwitchBranchViewModel_Cancel_CompletesWithNone`, `SwitchBranchViewModel_Switch_CompletesOnceWithChoice` and
`SwitchBranchWindow_EscapeAndCancel_ReturnNull`, covering `AgentControl-SwitchBranchViewModel-CancelCompletesWithNone`.

**SwitchBranchWindow_UserOperations**: The dialog lists the branches once loaded, with the progress bar hidden, the
filter box focused and Switch disabled until a branch other than the current one is selected; typing a filter that
leaves one branch and pressing Enter returns it; the Switch button returns the selected branch; a double-click on a row
returns it. These are tested by `SwitchBranchWindow_Opened_ListsBranchesAndFocusesFilterBox`,
`SwitchBranchWindow_FilterThenEnter_ChoosesTheOnlyMatchingBranch`,
`SwitchBranchWindow_SwitchClicked_ReturnsTheSelectedBranch` and `SwitchBranchWindow_DoubleClickOnBranch_ReturnsIt`,
covering `AgentControl-LauncherUI-SwitchBranchDialog` at the subsystem level; the constructors refuse a missing view
model (`SwitchBranchWindow_Constructors_NullViewModelThrows_ParameterlessWorks`).
