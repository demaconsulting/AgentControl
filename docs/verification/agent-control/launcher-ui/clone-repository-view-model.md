### CloneRepositoryViewModel

#### Verification Approach

`CloneRepositoryViewModel` is verified with unit tests in `CloneRepositoryViewModelTests.cs`. Each test constructs a
real view model, types an address, a parent folder and a folder name, picks a group and executes the commands, and reads
the texts, `CanAccept`, `ErrorText` and the request it completes with. The destination check reads the real file system,
so each test works in its own temporary folder; the background work runner is the real thread-pool one, or a
`GatedBackgroundWorkRunner` (a test helper of the `LauncherUI` tests) that holds the check until the test releases it,
so a test asserts that the calling thread - which the tests guard against blocking calls - stays free while the check is
outstanding. The dialog as the user meets it (its controls, the focus, Browse, Enter, Escape, Cancel) is verified by the
headless window tests in `CloneRepositoryWindowTests.cs`. No git is started: the clone itself is verified with
`MainWindowViewModel` (`MainWindowViewModelCloneTests.cs`) and the real-git end-to-end tests.

#### Test Environment

N/A - standard test environment for the unit tests (a temporary folder per test, no window and no processes); the window
tests use the shared headless Avalonia session (`HeadlessTestApp`).

#### Acceptance Criteria

- Clone is disabled until the address, the parent folder and the folder name are filled in, and an empty field shows no
  error.
- An address that starts with a dash, uses the remote-helper syntax or has more than one line is refused with a message,
  as is a parent folder that is not an absolute path, and Clone stays disabled.
- The folder name follows the address (last segment, without `.git`, for URLs, scp-like addresses and local paths) until
  the user types a name, and then stays; a name that is not one valid Windows file name is refused with a message, on
  every host operating system (the forbidden characters are a fixed set, not the host's).
- The destination is checked on disk through the background work runner when Clone is pressed, so the calling thread is
  free meanwhile; an existing non-empty folder or a missing parent keeps the dialog open with the reason, and a new or
  an empty folder is accepted.
- The group box starts on Ungrouped, lists the groups it was given after it, and falls back to Ungrouped.
- The dialog completes exactly once - with the trimmed address, the destination and the group, or with none when
  canceled - whatever is done afterwards.

#### Test Scenarios

**CloneRepositoryViewModel_UrlRequired**: A new dialog cannot be accepted and shows no error; with an address it can; a
blank parent folder or a blank folder name makes it unavailable again, still without an error. This scenario is tested
by `CloneRepositoryViewModel_EmptyUrl_CannotAccept` and, in the window, by
`CloneRepositoryWindow_Opened_FocusesAddressAndTypingAnAddressFillsFolderNameAndEnablesClone` (the focus is in the
address box, the parent folder is the one given, Clone is disabled until an address is typed), covering
`AgentControl-CloneRepositoryViewModel-UrlRequired`.

**CloneRepositoryViewModel_UrlOptionRefused**: The addresses `-u=evil`, `--upload-pack=calc.exe`, `ext::sh -c ...`,
`fd::17/repo` and two lines of text are each refused with a message and Clone stays unavailable; a relative parent
folder is refused with a message naming the absolute path. This scenario is tested by
`CloneRepositoryViewModel_UrlStartingWithDashOrHelperOrMultiline_IsRefused`,
`CloneRepositoryViewModel_RelativeParentFolder_IsRefused` and, in the window,
`CloneRepositoryWindow_UrlLooksLikeAnOption_ShowsErrorAndDisablesClone` (the message is visible under the fields),
covering `AgentControl-CloneRepositoryViewModel-UrlOptionRefused`.

**CloneRepositoryViewModel_FolderNameFromUrl**: `https://github.com/owner/repo.git`, the same without `.git`, with a
trailing slash, with another letter case of `.GIT`, with a query and a fragment, `git@github.com:owner/repo.git`,
`git@example.com:repo.git`, `ssh://git@example.com:2222/team/proj.git`, `C:\repos\origin.git` and `/srv/git/origin.git`
give `repo`, `repo`, `repo`, `Repo`, `repo`, `repo`, `repo`, `proj`, `origin`, `origin`; blank and `null` give an empty
name. Typing an address and then another changes the folder name each time; once a name is typed it stays when the
address changes again, and the destination is the parent folder plus that name. This scenario is tested by
`CloneRepositoryViewModel_DeriveFolderName_FollowsTheLastSegment` and
`CloneRepositoryViewModel_FolderName_FollowsUrlUntilEdited`, covering
`AgentControl-CloneRepositoryViewModel-FolderNameFromUrl`.

**CloneRepositoryViewModel_FolderNameRules**: Names with a slash, a backslash, a colon or an asterisk, `.`, `..`, a
trailing dot, `con`, `NUL.txt`, `com1` and `LPT9` are refused with a message and Clone is unavailable; `repo`,
`my repo`, `repo.v2` and `console` pass. This scenario is tested by
`CloneRepositoryViewModel_FolderName_InvalidNames_AreRefused`, covering
`AgentControl-CloneRepositoryViewModel-FolderNameRules`.

**CloneRepositoryViewModel_DestinationCheckedInBackground**: With the destination check held at a gate and the calling
thread guarded, pressing Clone records no blocking call, shows the checking state, keeps Clone unavailable, and after
the gate opens an existing folder holding a file is refused with `already exists and is not empty`, the dialog stays
open, the file is untouched and another folder name clears the message. A parent folder that does not exist is refused
with `does not exist`, and an existing empty folder is accepted. In the window, Enter in the address box checks the
folder, shows the reason under the fields and keeps the dialog open; another name and Enter close it with the request.
This scenario is tested by `CloneRepositoryViewModel_Accept_ChecksDestinationInBackground_ShowsExistingFolderMessage`,
`CloneRepositoryViewModel_Accept_NewOrEmptyFolderIsAcceptedMissingParentRefused` and
`CloneRepositoryWindow_EnterInAddressBox_ClonesAndExistingFolderShowsReason`, covering
`AgentControl-CloneRepositoryViewModel-DestinationCheckedInBackground`.

**CloneRepositoryViewModel_GroupChoiceDefaultsUngrouped**: Given two groups, the choices are Ungrouped, `Work` and
`Work > Backend`, Ungrouped is selected, choosing a group selects it and assigning `null` selects Ungrouped again; in
the window the group box lists the same entries and selecting the third one sets the group. This scenario is tested by
`CloneRepositoryViewModel_Groups_UngroupedIsDefaultAndTreePathsListed` and
`CloneRepositoryWindow_Opened_FocusesAddressAndTypingAnAddressFillsFolderNameAndEnablesClone`, covering
`AgentControl-CloneRepositoryViewModel-GroupChoiceDefaultsUngrouped`.

**CloneRepositoryViewModel_CompletesWithRequest**: A usable input completes once with the trimmed address, the parent
folder plus the folder name and the chosen group, and a later cancel tells nobody; the cancel command completes once
with no request and a second cancel completes nothing. In the window the Clone button returns the request, Escape and
the Cancel button return none, and Browse puts the folder the picker returns in the parent folder box while a canceled
picker changes nothing. This scenario is tested by `CloneRepositoryViewModel_Accept_CompletesWithRequest`,
`CloneRepositoryViewModel_Cancel_CompletesWithNone`, `CloneRepositoryWindow_CloneClicked_ReturnsTheRequest`,
`CloneRepositoryWindow_EscapeAndCancel_ReturnNull` and
`CloneRepositoryWindow_BrowseClicked_PutsChosenFolderInParentBoxAndCancelChangesNothing`, covering
`AgentControl-CloneRepositoryViewModel-CompletesWithRequest`; the constructors refuse missing groups and a missing view
model (`CloneRepositoryViewModel_Constructor_NullGroups_Throws`,
`CloneRepositoryWindow_Constructors_NullViewModelThrows_ParameterlessWorks`).
