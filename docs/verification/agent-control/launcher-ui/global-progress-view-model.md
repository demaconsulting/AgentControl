### GlobalProgressViewModel

#### Verification Approach

`GlobalProgressViewModel` (with the `GlobalProgressOperation` in its file) is verified with unit tests in
`GlobalProgressViewModelTests.cs`. Each test constructs a real view model, begins operations with and without a cancel
action (a counting delegate), reports through the returned operations, executes `CancelCommand` and disposes the
operations, and reads the bound properties; nothing is mocked and no Avalonia `Window` is constructed. Which operations
`MainWindowViewModel` reports, with which texts, is verified by that unit; how the window shows the strip (its
visibility, text, Cancel button, the progress bar's accessible name and the contrast of its brushes) by the headless
window tests and the theme tests of the `LauncherUI` subsystem (`MainWindowTests.cs`, `ThemeResourceTests.cs`).

#### Test Environment

N/A - standard test environment (no window, no files and no processes are involved).

#### Acceptance Criteria

- A new strip, and a strip whose operations have all ended, is not shown, has no text and no Cancel, and its Cancel
  command cannot execute; ending an operation twice or reporting after it ended changes nothing.
- Beginning an operation shows the strip, indeterminate, with the operation's text, which is also the progress bar's
  accessible name.
- A reported count makes the bar determinate with the count, the total and the new text; a step without a count makes
  it indeterminate again.
- An operation with a cancel action offers Cancel; executing Cancel twice runs the action once and hides Cancel while
  the operation stays shown until its owner ends it.
- An operation without a cancel action, or during a step that does not allow a cancel, offers no Cancel and runs no
  cancel action; Cancel is offered again when the step allows it.
- With overlapping operations the most recently begun one is shown, and when it ends the previous one is shown again
  in its latest state (including reports it made meanwhile).

#### Test Scenarios

**GlobalProgressViewModel_IdleHidden**: A new strip is not active, has an empty text and accessible name, no Cancel and
a Cancel command that cannot execute (`GlobalProgressViewModel_Idle_IsInactiveWithNoText`); after its operations have
ended - one of them disposed twice and reported to after its end - the strip is inactive with no text and no Cancel
(`GlobalProgressViewModel_AllEnded_ReturnsToIdle`). These cover `AgentControl-GlobalProgressViewModel-IdleHidden`.

**GlobalProgressViewModel_ShowsOperation**: Beginning "Adding repositories..." makes the strip active and indeterminate
with that text. This scenario is tested by `GlobalProgressViewModel_Begin_ShowsIndeterminateText`, covering
`AgentControl-GlobalProgressViewModel-ShowsOperation`.

**GlobalProgressViewModel_ReportsAmount**: Reporting 7 of 20 with "Adding 7 of 20 repositories..." makes the bar
determinate with a value of 7 and a maximum of 20 and that text; a following "Saving..." step makes it indeterminate
with the new text. This scenario is tested by `GlobalProgressViewModel_Report_ShowsCountsDeterminate`, covering
`AgentControl-GlobalProgressViewModel-ReportsAmount`.

**GlobalProgressViewModel_CancelOnce**: An operation begun with a counting cancel action offers Cancel and its command
can execute; executing the command twice counts one cancel, Cancel is no longer offered and the strip is still active.
This scenario is tested by `GlobalProgressViewModel_CancelableOperation_CancelCommandCancelsOnce`, covering
`AgentControl-GlobalProgressViewModel-CancelOnce`.

**GlobalProgressViewModel_NoCancelWhenNotCancelable**: An operation without a cancel action offers no Cancel and the
command cannot execute; an operation with a cancel action whose step disallows a cancel offers none and no cancel
runs; when the step allows a cancel again, Cancel is offered. This scenario is tested by
`GlobalProgressViewModel_NotCancelable_CancelCommandDisabled`, covering
`AgentControl-GlobalProgressViewModel-NoCancelWhenNotCancelable`.

**GlobalProgressViewModel_OverlappingOperations**: A refresh without a cancel action is begun and then an add with one;
the refresh reports 1 of its total while the add is shown, and the strip keeps showing the add with Cancel; when the
add ends the strip shows the refresh again, determinate with its value of 1, its text and no Cancel. This scenario is
tested by `GlobalProgressViewModel_OverlappingOperations_ShowsLatestThenPrevious`, covering
`AgentControl-GlobalProgressViewModel-OverlappingOperations`.

**GlobalProgressViewModel_AccessibleName**: The accessible name is empty while idle and equals the shown operation's
text once an operation begins. This scenario is tested by `GlobalProgressViewModel_Begin_ShowsIndeterminateText` (and
`GlobalProgressViewModel_Idle_IsInactiveWithNoText`), covering `AgentControl-GlobalProgressViewModel-AccessibleName`;
that the window's progress bar carries this name is shown by
`MainWindow_DropFolders_WhileChecked_WindowStaysResponsiveAndShowsProgress` at the subsystem level.
