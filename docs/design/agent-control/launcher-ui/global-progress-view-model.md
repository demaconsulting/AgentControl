### GlobalProgressViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`GlobalProgressViewModel` backs the main window's global progress strip, directly under the app bar: a thin progress
bar, one short line of status text and a Cancel button, shown only while a bulk operation that is not tied to one repo
card runs - adding several repositories, reading the cards at start-up, or refreshing every card after the settings
were saved. Its single
responsibility is to show which bulk operation runs and how far it has got, and to forward the user's Cancel to it; it
knows nothing of repos, settings or threads. The `GlobalProgressOperation` class in the same file is part of this unit:
it is the handle through which one operation's owner reports and ends it. Its behavior is specified by the unit
requirements `AgentControl-GlobalProgressViewModel-IdleHidden`, `-ShowsOperation`, `-ReportsAmount`, `-CancelOnce`,
`-NoCancelWhenNotCancelable`, `-OverlappingOperations`, `-AccessibleName` and `-CancelDescribesOperation` (each prefixed
`AgentControl-GlobalProgressViewModel`), and together with `MainWindowViewModel` (which decides what is reported) it
provides `AgentControl-LauncherUI-GlobalProgress` and `AgentControl-LauncherUI-AddCancel`.

#### Data Model

**IsActive** (`bool`): whether an operation is running, so the strip is shown; when `false` the strip is fully
collapsed and takes no space, so the idle window is unchanged (`AgentControl-GlobalProgressViewModel-IdleHidden`).

**Text** (`string`): the shown operation's status text, for example `Adding 7 of 20 repositories...`; empty when idle.
**AccessibleName** (`string`, derived): equal to `Text`, the name screen readers announce for the progress bar, so the
bar is never an unnamed progress bar; its change is notified with `Text`'s
(`AgentControl-GlobalProgressViewModel-AccessibleName`).

**IsIndeterminate** (`bool`), **Value** and **Maximum** (`double`): whether the shown operation's amount of work is
unknown (at its start, or in a step it cannot count, such as saving), and how much of it is done out of how much.
`Maximum` is at least 1 (an empty batch never divides by zero in the bar), `Value` is clamped to 0 up to `Maximum`
and is 0 while indeterminate. Idle: indeterminate, 0 of 1 (`AgentControl-GlobalProgressViewModel-ShowsOperation`,
`-ReportsAmount`).

**CanCancel** (`bool`) and **CancelCommand** (`RelayCommand`): whether the shown operation can be canceled now, so the
strip shows its Cancel button, and the button's command, enabled only while `CanCancel`; a change of `CanCancel` raises
the command's `CanExecuteChanged` (`AgentControl-GlobalProgressViewModel-CancelOnce`, `-NoCancelWhenNotCancelable`).

**CancelAccessibleName** and **CancelToolTip** (`string`, derived): the name screen readers announce for the Cancel
button and its tooltip, taken from the shown operation's `GlobalProgressCancelDescription` (a record in the same file:
`AccessibleName` and `ToolTip`). The one Cancel button serves every cancelable operation - adding repositories,
cloning a repository - so a button that always said "Cancel adding repositories" would tell a screen-reader user the
wrong thing while a clone runs. An operation that gives no description, and an idle strip, get
`GlobalProgressCancelDescription.AddingRepositories` ("Cancel adding repositories" / "Stop adding repositories;
nothing more is added"), the text the button always had; a change of the shown operation notifies both properties
(`AgentControl-GlobalProgressViewModel-CancelDescribesOperation`).

**_operations** (`List<GlobalProgressOperation>`, private): the running operations, oldest first. **Shown**
(`GlobalProgressOperation?`, private) is the one shown: the most recently begun running operation that can still be
canceled (`IsCancelable`) or, when none can, the most recently begun running operation; `null` when none runs. So an
add's Cancel stays offered while the cards refresh after a Settings save, and the refresh is shown once the add has
ended or was canceled. Every bound property above is a copy of the shown operation's state, refreshed by the private
`Publish`.

**GlobalProgressOperation** (`IDisposable`): one bulk operation, created only by `Begin`. **Text**, **IsIndeterminate**
(initially `true`), **Completed** and **Total** (`int`, meaningful only when determinate) are its own state.
**AllowCancel** (`bool`, initially `true`) lets its owner refuse a cancel for a step that must run to its end once
started (the add's save) and accept it again afterwards; it has no effect on an operation without a cancel action.
**CanCancel** is `true` only when the operation has a cancel action, `AllowCancel` is set, it was not canceled already
and it has not ended. **IsCancelable** (internal `bool`) is `true` when the operation has a cancel action that has
not run and it has not ended, whether or not `AllowCancel` is set: such an operation is shown in preference to newer
ones, so an add in its "Saving..." step stays shown, without Cancel, rather than flipping to the refresh. Private flags
record whether the cancel action has run (it runs at most once) and whether the
operation has ended.

Invariants: exactly when `_operations` is empty the strip is idle; the shown state is always that of `Shown` - the most
recently begun running operation that can still be canceled, otherwise the most recently begun running operation. Like
every view model of the subsystem it is created and changed on the UI thread only (the operations report from the
continuations of their background work, which resume on the UI thread); it is not thread-safe.

#### Key Methods

**Begin(text, cancel = null, cancelDescription = null)**: Starts showing an operation, indeterminate, with the given
text.

- *Parameters*: `string text` (not null, `ArgumentNullException`), `Action? cancel` - what the strip's Cancel button
  does for this operation, run on the UI thread at most once, or `null` for an operation that cannot be canceled;
  `GlobalProgressCancelDescription? cancelDescription` - how the Cancel button is named while this operation is shown
  (`null` for the adding-repositories text).
- *Returns*: the `GlobalProgressOperation`; its owner reports through it and disposes it when the work has ended.
- *Postconditions*: the operation is appended to `_operations` and becomes the shown one, unless an older operation
  that can still be canceled is running (`AgentControl-GlobalProgressViewModel-OverlappingOperations`): `IsActive` is
  `true`, `Text` its text, the bar indeterminate, `CanCancel` `true` exactly when a cancel action was given
  (`AgentControl-GlobalProgressViewModel-ShowsOperation`).

**GlobalProgressOperation.Report(completed, total, text)**: Reports a known amount of work: the operation becomes
determinate. `total` below 0 counts as 0, `completed` is clamped to 0 up to `total`; `text` must not be null. When the
operation is the shown one the strip shows `Value` = `completed` of `Maximum` = `max(1, total)` and the text
(`AgentControl-GlobalProgressViewModel-ReportsAmount`).

**GlobalProgressOperation.SetIndeterminate(text)**: Reports a step whose amount is unknown: the operation becomes
indeterminate with the new text (for example `Saving...`).

**GlobalProgressOperation.AllowCancel** (setter): When the value changes the strip is told, so the Cancel button hides
or shows again (`AgentControl-GlobalProgressViewModel-NoCancelWhenNotCancelable`).

**CancelCommand** / **GlobalProgressOperation.RequestCancel** (internal): The command runs the shown operation's
`RequestCancel`, which does nothing unless `CanCancel`; otherwise it marks the operation canceled (so `CanCancel` turns
`false` and the button disappears) and then runs the cancel action - once, however often Cancel is pressed
(`AgentControl-GlobalProgressViewModel-CancelOnce`). A canceled operation can no longer be canceled, so the strip may
show another running operation instead (`-OverlappingOperations`).

**GlobalProgressOperation.Dispose**: Ends the operation (idempotent): it is removed from `_operations` (`End`) and the
strip shows the running operation that should be shown next (`Shown`), in its latest state, or goes idle when none is
left
(`AgentControl-GlobalProgressViewModel-OverlappingOperations`, `-IdleHidden`). Reports after disposal are ignored.

**OnOperationChanged** (internal): An operation's state changed; the strip always republishes `Shown`, because a cancel
of the shown operation can change which operation is shown. A background operation's reports never replace what is
shown (the bound properties announce real changes only), and are shown once it is the shown one again.

#### Error Handling

`Begin`, `Report` and `SetIndeterminate` throw `ArgumentNullException` for a null text; nothing else throws. Out of
range counts are clamped rather than rejected. Cancel on an operation that cannot be canceled, a second Cancel, a
Dispose called twice and reports after the operation ended are ignored. Exceptions from a cancel action are not
caught: the owner's cancel action (`MainWindowViewModel.CancelAddRepos`) only signals a cancellation token.

#### Dependencies

- **ViewModelBase**, **RelayCommand** (`LauncherUI`) - change notification and the Cancel command.

#### Callers

- **MainWindowViewModel** (`LauncherUI`) - creates the one instance (`GlobalProgress`), begins one cancelable
  operation per session of adds (`Adding repositories...`, then `Adding {completed} of {total} repositories...`,
  `Saving...` with `AllowCancel` off) and one operation without a cancel action for each wave of counted reads of the
  shared refresh queue, such as the cards' reads at start-up and their refresh after a settings save (`Refreshing
  repositories {completed} of {total}...`), and disposes them when the work ends; it also begins the cancelable,
  indeterminate operation of a clone with its own cancel description.
- **MainWindow** (view) - binds the strip (`GlobalProgressStrip`), its bar (`GlobalProgressBar`), its text
  (`GlobalProgressText`) and its Cancel button (`GlobalProgressCancelButton`, whose accessible name and tooltip are
  bound to `CancelAccessibleName` and `CancelToolTip`) to the properties and `CancelCommand`.
