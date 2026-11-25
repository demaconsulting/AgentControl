### SwitchBranchViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SwitchBranchViewModel` backs the "Switch branch" dialog that a repo card's menu opens. Its single responsibility is to
show the branches the repo already knows - local first, then remote-tracking - let the user filter them and choose one
other than the current branch, read them without blocking the window, and report the choice (or none) exactly once; the
window itself stays free of logic, and making the switch is the card's job (`RepoCardViewModel.SwitchBranchAsync`), so
the dialog closes first and the busy card shows the progress. It also names where the repo is (`Currently on main`, or
`Detached HEAD at 1a2b3c4`), says that nothing is fetched (the hint points to Git Pull for newer remote branches) and
explains an empty list or a failed read in words. Its behavior is specified by the unit requirements
`AgentControl-SwitchBranchViewModel-ListsLocalAndRemote`, `-MarksCurrent`, `-DetachedHead`, `-Filter`,
`-ConfirmChoice`, `-LoadsInBackground`, `-LoadFailureShown` and `-CancelCompletesWithNone` (each prefixed
`AgentControl-SwitchBranchViewModel`), and together with `RepoCardViewModel` it provides
`AgentControl-LauncherUI-SwitchBranch` and `AgentControl-LauncherUI-SwitchBranchDialog`. The window
`SwitchBranchWindow`, the row `BranchItemViewModel` and the answer `BranchChoice` are supporting types.

#### Data Model

**Title**: `Switch branch - {repo name}`. **HintText**: the constant sentence *Remote branches are the ones Git already
knows about. Use Git Pull to update them.*

**BranchChoice** (record, `SwitchBranchViewModel.cs`): `Name` (the display name; for a remote branch the name on the
remote, which is also the name of the local branch that will be created), `FullName` (what git is given: the local name,
or `<remote>/<name>`) and `IsRemote`. It is the dialog's answer.

**BranchItemViewModel** (class, same file): one row - `Name`, `FullName`, `Remote` (`null` for a local branch),
`IsCurrent`, and derived `IsRemote`, `Kind` (`Local`/`Remote`), `Caption` (`current`, `remote origin` or empty),
`HasCaption` and `AccessibleText`; `ToChoice()` builds the `BranchChoice`.

**All rows** (private): every row, local first (git's sorted order), then remote; empty until loaded.

**FilterText**: the text the list is filtered by; changing it re-applies the filter. **Items**: the rows that match it -
a branch is listed when its `FullName` contains the text ignoring letter case (so the remote's name counts), blank lists
everything (`AgentControl-SwitchBranchViewModel-Filter`). **SelectedItem**: the selected row. When the filter drops the
selected row the selection is cleared; when a non-blank filter leaves exactly one row that can be chosen it is
selected, so typing a name and pressing Enter is enough.

**IsLoading** (initially `true`), **ErrorText** / **HasError**: while the branches are read the dialog shows its
indeterminate bar; a failed read is shown as text (`AgentControl-SwitchBranchViewModel-LoadFailureShown`).
**CurrentText**: where the repo is (`AgentControl-SwitchBranchViewModel-MarksCurrent`, `-DetachedHead`).
**EmptyText** / **HasEmptyText**: `This repository has no branches yet.` or `No branch matches the filter.` in place of
an empty list, `null` while loading, on an error and when rows are listed.

**CanSwitch**: not loading, no error and the selected row is not the current branch. **SwitchCommand** (available while
`CanSwitch`) and **CancelCommand**; the **Completed** event (`EventHandler<BranchChoice?>`) is raised once with the
choice, or `null` when canceled.

#### Key Methods

**Constructor(repoName, loadBranches, backgroundWork = null)**: Stores the title, the function that reads the
branches and the `IBackgroundWorkRunner` (the thread-pool runner by default). `loadBranches` is supplied by the card
(`RepoCardViewModel.CreateSwitchBranchViewModel`, which reads through its own `GitClient`), so the dialog never builds a
git client. Throws `ArgumentNullException` for a null name or function.

**LoadAsync(cancellationToken)**: Sets the loading state, runs `loadBranches` through the background work runner - never
on the calling (UI) thread, so the window stays usable and, in the tests, the guard against blocking calls on the UI
thread is never tripped (`AgentControl-SwitchBranchViewModel-LoadsInBackground`) - and lists the result: local
branches then remote ones, the branch that git reported as current marked, `CurrentText` set (`Currently on {branch}`;
`Detached HEAD at {commit}` when git named the commit; `Detached HEAD` otherwise). Any failure of the read becomes
`ErrorText` (`The branches could not be read: {message}`, with credentials removed by `SecretRedactor`) instead of an
exception; a canceled read (the window was closed) throws `OperationCanceledException` and shows nothing. The loading
state always ends.

**Confirm(item)**: Selects the row and, when it can be switched to, completes with it - what a double-click on a row
does (`AgentControl-SwitchBranchViewModel-ConfirmChoice`). **Switch** (via `SwitchCommand`): does nothing unless
`CanSwitch`; completes with the selected row's `BranchChoice`.

**Cancel** (via `CancelCommand`, also called by the window when it closes by any other route): Completes with `null`
(`AgentControl-SwitchBranchViewModel-CancelCompletesWithNone`). **Complete** (private) raises `Completed` the first time
only, so a dialog answered by the Switch button and then closed by the title bar tells its listener exactly once.

#### Error Handling

Only the constructor's argument validation throws, and a canceled `LoadAsync` throws `OperationCanceledException`. A
failed read is not an error of the view model: it is text, and `CanSwitch` stays `false`. Choosing nothing, or the
current branch, does nothing.

#### Dependencies

- **GitBranchList** (`GitIntegration` subsystem, returned by `GitClient.GetBranches`) - what the loader returns.
- **IBackgroundWorkRunner** (`LauncherUI`) - runs the read off the UI thread.
- **SecretRedactor** (`PackageCredentials` subsystem) - removes credentials from a failure's text.
- **ViewModelBase**, **RelayCommand** (`LauncherUI`) - change notification and commands.

#### Callers

- **RepoCardViewModel** (`LauncherUI`) - `CreateSwitchBranchViewModel` builds it for the window.
- **MainWindow** (view) - shows it in `SwitchBranchWindow` when the card raises `SwitchBranchRequested`, and passes a
  chosen `BranchChoice` to `RepoCardViewModel.SwitchBranchAsync`.
- **SwitchBranchWindow** (view) - binds to it, starts `LoadAsync` when the window opens and cancels it when it closes,
  closes when it completes, turns a double-click on a row into `Confirm` and applies `DialogBehavior` (focus in the
  filter box, Tab staying in the dialog, Escape canceling).
