### DialogBehavior

#### Verification Approach

`DialogBehavior` is verified with unit tests in `DialogBehaviorTests.cs` on a plain headless Avalonia window - a
button, a text box and a disabled text box in a stack panel - to which the behavior is applied directly, so each part
of it is proven without any of the real dialogs. The tests run on the shared headless session (`HeadlessTestApp`),
show the window, give it key events with the headless keyboard helpers and read which control has the focus, how it
got it and whether the cancel action ran. Nothing is mocked. That every real dialog applies the behavior with a
sensible starting control, is shown modally and returns the focus to its owner is verified one level up by the
headless window tests of the `LauncherUI` subsystem (`DialogWindowTests.cs`, `UsabilityWindowTests.cs`,
`CredentialPromptWindowTests.cs` and `RepoGroupWindowTests.cs`).

#### Test Environment

The headless Avalonia session (no display needed); no files and no processes are involved.

#### Acceptance Criteria

- After `Apply`, the dialog's Tab navigation mode is a cycle.
- When the dialog opens, the named control has the keyboard focus, set as a keyboard navigation (so the focus ring is
  shown); with no name, an unknown name or a disabled named control, the first control Tab reaches has it.
- Escape runs the cancel action once; Escape handled by the focused control does not; with no cancel action Escape does
  nothing and the dialog stays open.
- A `null` dialog is refused.

#### Test Scenarios

**DialogBehavior_TabStaysInside**: The behavior is applied and the window's tab navigation mode is read. This
scenario is tested by `DialogBehavior_Apply_TabNavigation_CyclesInsideTheDialog`, covering
`AgentControl-DialogBehavior-TabStaysInside`.

**DialogBehavior_InitialFocus**: The window is shown with the second control named: it has the focus, the first one
does not, and the focus event reports the keyboard (Tab) navigation method; then with no name, with a name that does
not exist and with the disabled control named, the first control has the focus. This scenario is tested by
`DialogBehavior_Apply_Opened_FocusesNamedControlWithVisibleKeyboardFocus` and
`DialogBehavior_Apply_NoUsableNamedControl_FocusesFirstTabStop`, covering
`AgentControl-DialogBehavior-InitialFocus`.

**DialogBehavior_EscapeCancels**: With a cancel action, Escape on the first control runs it once, and Escape on a
control that handles the key itself does not run it again; with no cancel action Escape leaves the dialog open. This
scenario is tested by `DialogBehavior_Apply_Escape_RunsCancelOnceUnlessTheFocusedControlUsedIt` and
`DialogBehavior_Apply_EscapeWithoutCancelAction_DoesNothing`, covering `AgentControl-DialogBehavior-EscapeCancels`.

**DialogBehavior_RequiresDialog**: `Apply` is called with `null`. This scenario is tested by
`DialogBehavior_Apply_NullDialog_Throws`, covering `AgentControl-DialogBehavior-RequiresDialog`.
