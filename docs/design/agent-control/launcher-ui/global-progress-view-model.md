### GlobalProgressViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`GlobalProgressViewModel` backs the main window's global progress strip, directly under the app bar: a thin progress
bar, one short line of status text and a Cancel button, shown only while a bulk operation that is not tied to one repo
card runs - adding several repositories, or refreshing every card after the settings were saved. Its single
responsibility is to show which bulk operation runs and how far it has got, and to forward the user's Cancel to it; it
knows nothing of repos, settings or threads. The `GlobalProgressOperation` class in the same file is part of this unit:
it is the handle through which one operation's owner reports and ends it. Its behavior is specified by the unit
requirements `AgentControl-GlobalProgressViewModel-IdleHidden`, `-ShowsOperation`, `-ReportsAmount`, `-CancelOnce`,
`-NoCancelWhenNotCancelable`, `-OverlappingOperations` and `-AccessibleName` (each prefixed
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

**_operations** (`List<GlobalProgressOperation>`, private): the running operations, oldest first; the last one is the
one shown. Every bound property above is a copy of the shown operation's state, refreshed by the private `Publish`.

**GlobalProgressOperation** (`IDisposable`): one bulk operation, created only by `Begin`. **Text**, **IsIndeterminate**
(initially `true`), **Completed** and **Total** (`int`, meaningful only when determinate) are its own state.
**AllowCancel** (`bool`, initially `true`) lets its owner refuse a cancel for a step that must run to its end once
started (the add's save) and accept it again afterwards; it has no effect on an operation without a cancel action.
**CanCancel** is `true` only when the operation has a cancel action, `AllowCancel` is set, it was not canceled already
and it has not ended. Private flags record whether the cancel action has run (it runs at most once) and whether the
operation has ended.

Invariants: exactly when `_operations` is empty the strip is idle; the shown state is always the most recently begun
operation still running. Like every view model of the subsystem it is created and changed on the UI thread only (the
operations report from the continuations of their background work, which resume on the UI thread); it is not
thread-safe.

#### Key Methods

**Begin(text, cancel = null)**: Starts showing an operation, indeterminate, with the given text.

- *Parameters*: `string text` (not null, `ArgumentNullException`), `Action? cancel` - what the strip's Cancel button
  does for this operation, run on the UI thread at most once, or `null` for an operation that cannot be canceled.
- *Returns*: the `GlobalProgressOperation`; its owner reports through it and disposes it when the work has ended.
- *Postconditions*: the operation is appended to `_operations` and becomes the shown one: `IsActive` is `true`,
  `Text` its text, the bar indeterminate, `CanCancel` `true` exactly when a cancel action was given
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
(`AgentControl-GlobalProgressViewModel-CancelOnce`).

**GlobalProgressOperation.Dispose**: Ends the operation (idempotent): it is removed from `_operations` (`End`) and the
strip shows the previous running operation again, in its latest state, or goes idle when none is left
(`AgentControl-GlobalProgressViewModel-OverlappingOperations`, `-IdleHidden`). Reports after disposal are ignored.

**OnOperationChanged** (internal): An operation's state changed; the strip is updated only when it is the shown one,
so a background operation's reports never replace what is shown, and are shown once it is the last one again.

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
  repositories {completed} of {total}...`), and disposes them when the work ends.
- **MainWindow** (view) - binds the strip (`GlobalProgressStrip`), its bar (`GlobalProgressBar`), its text
  (`GlobalProgressText`) and its Cancel button (`GlobalProgressCancelButton`) to the properties and `CancelCommand`.
