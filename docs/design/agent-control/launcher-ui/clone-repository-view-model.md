### CloneRepositoryViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`CloneRepositoryViewModel` backs the "Clone repository" dialog that the app bar's Clone button opens. Its single
responsibility is to collect what a clone needs - the repository address, the folder to clone into (a parent folder
and a folder name that follows the address) and the group of the new card - check it while the user types, check the
destination on disk without blocking the window, and report a `CloneRepositoryRequest` (or none) exactly once. The
window itself stays free of logic, and cloning is the main window's job (`MainWindowViewModel.CloneRepositoryAsync`), so
the dialog closes first and the progress strip shows the clone. Its behavior is specified by the unit requirements
`AgentControl-CloneRepositoryViewModel-UrlRequired`, `-UrlOptionRefused`, `-FolderNameFromUrl`, `-FolderNameRules`,
`-DestinationCheckedInBackground`, `-GroupChoiceDefaultsUngrouped` and `-CompletesWithRequest` (each prefixed
`AgentControl-CloneRepositoryViewModel`), and it provides `AgentControl-LauncherUI-CloneRepositoryDialog`. The window
`CloneRepositoryWindow`, the group choice `CloneGroupChoice` and the answer `CloneRepositoryRequest` are supporting
types.

#### Data Model

**Title**: `Clone repository`.

**CloneGroupChoice** (record, `CloneRepositoryViewModel.cs`): `GroupName` (`null` for no group) and `DisplayName`
(`Ungrouped`, or the group's path through the tree such as `Work > Backend`); the static `Ungrouped` is the default.
**CloneRepositoryRequest** (record, same file): `Url` (trimmed), `DestinationPath` and `GroupName`; it is the dialog's
answer.

**RepositoryUrl**: the address as typed. Changing it re-derives **FolderName** unless the user has typed a folder name
of their own; clearing the folder name box lets it follow the address again from the next change of the address
(`AgentControl-CloneRepositoryViewModel-FolderNameFromUrl`). **ParentFolder**: the folder that will contain
the clone, typed or filled in by the window's Browse button; the window starts it in the user's profile folder.
**DestinationPath**: the parent folder (trimmed, like the address) and the folder name exactly as typed combined as
text - the disk is not read; empty while either is blank. **Groups** / **SelectedGroup**: Ungrouped first, then the
groups the owner gave, in the order given; the
selection defaults to Ungrouped and a `null` assignment falls back to it
(`AgentControl-CloneRepositoryViewModel-GroupChoiceDefaultsUngrouped`). **\_inputVersion**: a counter that every change
of the address, the parent folder, the folder name or the group increments, so a disk check can tell that its answer
is stale.

**ErrorText** / **HasError**: the disk check's finding when it refused the destination, otherwise the first rule that
needs no disk and that the input breaks: the address (`GitClient.CheckCloneUrl`: not a single line, starts with a dash
or uses a `name::address` remote-helper syntax), the folder name (`CheckFolderName`) or a parent folder that is not an
absolute path (`AgentControl-CloneRepositoryViewModel-UrlOptionRefused`, `-FolderNameRules`). An empty field is not an
error. Any change of the input clears the disk finding. **IsChecking**: the destination is being checked on disk.

**CanAccept**: all three fields are filled in, no rule is broken, no check runs and the dialog has not completed
(`AgentControl-CloneRepositoryViewModel-UrlRequired`). **AcceptCommand** (an `AsyncRelayCommand`, available while
`CanAccept`) and **CancelCommand**; the **Completed** event (`EventHandler<CloneRepositoryRequest?>`) is raised once
with the request, or `null` when canceled.

#### Key Methods

**Constructor(groups, initialParentFolder = null, backgroundWork = null)**: Stores the group choices (adding Ungrouped
in front when the list has none), the initial parent folder and the `IBackgroundWorkRunner` (the thread-pool runner by
default). Throws `ArgumentNullException` for null groups.

**DeriveFolderName(url)** (static, pure): Cuts a query or fragment, drops trailing separators and a `.git` ending, and
returns the text after the last `/`, `\` or `:` - the name `git clone` would choose - for URLs, scp-like addresses and
local paths; empty when the address names none.

**CheckFolderName(name)** (static, pure): Returns the reason a text is not one valid Windows file name, judged as typed
(never trimmed) - blank (only a text of nothing but white space counts as empty), `.` or `..`, a character a file name
cannot have (which includes the path separators), a space at the start or the end or a dot at the end (Windows would
silently drop them, so the folder would not have the name shown), a reserved device name (`CON`, `NUL`, `COM1`, the two
console handle names, the ports numbered with a superscript digit... with or without an extension, a space before the
extension included) - or `null`. The set of forbidden characters (`\ / : * ? " < > |` and the control characters) is
fixed, so the answer does not depend on the host operating system
(`AgentControl-CloneRepositoryViewModel-FolderNameRules`).

**AcceptAsync** (the body of `AcceptCommand`): Does nothing unless `CanAccept`. Captures the address, the destination
and the group together with the *input version* (a counter that `InputChanged` and a change of `SelectedGroup`
increment), sets `IsChecking`, runs
`GitClient.CheckCloneDestination` through the background work runner - never on the calling (UI) thread, because the
check reads the disk, which can be slow on a network drive or a sleeping disk
(`AgentControl-CloneRepositoryViewModel-DestinationCheckedInBackground`, `-DestinationCheckShowsProgress`) - and, when
the dialog was not completed meanwhile (a Cancel ends it quietly) and the input version is still the captured one,
either shows the finding as `ErrorText`
(something exists at the destination - a folder, even an empty one, a file or a link - its parent does not exist, the
path is not usable) and keeps the dialog open, or
completes with the request (`-DestinationRefusalShown`). An answer that arrives after the user edited the address, the
folder, the folder name or the group is stale and is **discarded**: nothing is shown, nothing completes, `IsChecking`
ends and Clone is available again for the new input (`-StaleCheckDiscarded`). The fields are deliberately not
disabled while the disk is read, because disabling the focused box when Enter is pressed would lose the keyboard
focus the dialog's keyboard requirements keep. An unexpected failure of the check reaches the command's error callback,
which shows it as text with credentials removed (`-CheckFailureReported`); `IsChecking` always ends.

**Cancel** (via `CancelCommand`, also called by the window when it closes by any other route): Completes with `null`.
**Complete** (private) raises `Completed` the first time only, so a dialog answered by the Clone button and then closed
by the title bar tells its listener exactly once (`AgentControl-CloneRepositoryViewModel-CompletesWithRequest`).

#### Error Handling

Only the constructor's argument validation throws. Everything wrong with the user's input is text (`ErrorText`) with
Clone disabled or the dialog kept open; a failure of the destination check is text too. The destination is checked again
by `GitClient.Clone` right before git starts, because the folder may have changed since the dialog closed.

#### Dependencies

- **GitClient** (`GitIntegration` subsystem) - `CheckCloneUrl` and `CheckCloneDestination`, the same rules the clone
  itself applies, so the dialog and the clone cannot disagree.
- **IBackgroundWorkRunner** (`LauncherUI`) - runs the disk check off the UI thread.
- **SecretRedactor** (`PackageCredentials` subsystem) - removes credentials from the text of an unexpected failure.
- **RepoGroupName** (`Settings` subsystem) - the name of the Ungrouped choice.
- **ViewModelBase**, **AsyncRelayCommand**, **RelayCommand** (`LauncherUI`) - change notification and commands.

#### Callers

- **MainWindow** (view) - builds it with `MainWindowViewModel.GetGroupChoices()` when the Clone button is pressed, shows
  it in `CloneRepositoryWindow` and passes the answer to `MainWindowViewModel.CloneRepositoryAsync`.
- **CloneRepositoryWindow** (view) - binds to it, closes when it completes, fills `ParentFolder` from Browse (through
  its `FolderPicker` seam) and applies `DialogBehavior` (focus in the address box, Tab staying in the dialog, Escape
  canceling).
