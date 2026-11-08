### DialogBehavior

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`DialogBehavior` is the keyboard behavior every AgentControl dialog shares: Tab and Shift+Tab cycle inside the dialog,
the first sensible control has the (visible) keyboard focus when the dialog opens, and Escape cancels a dialog that has
a way to cancel. It exists as one static helper instead of the same few lines in each dialog's code-behind, so the
dialogs cannot drift apart - a keyboard user gets the same, predictable behavior in the Settings window, the source
editor, the package picker, the sign-in prompt, the group-name prompt, the confirmations, the message boxes and
About. Its behavior is specified by the unit requirements `AgentControl-DialogBehavior-TabStaysInside`,
`-InitialFocus`, `-EscapeCancels` and `-RequiresDialog` (each prefixed `AgentControl-DialogBehavior`), and together with
the dialogs that apply it - and the owner's modal `ShowDialog`, which is what stops Tab and clicks reaching the window
behind - it provides `AgentControl-LauncherUI-DialogKeyboard`.

#### Data Model

None: the class is static and holds no state. Everything it needs is passed to `Apply`.

#### Key Methods

**Apply(dialog, initialFocusName = null, onEscape = null)** (internal, static): Applies the shared behavior to a
dialog window. It must be called once, from the dialog's constructor, on the UI thread.

- *Parameters*: `dialog` - the window; `initialFocusName` - the XAML name of the control that takes the focus when the
  dialog opens, or `null`; `onEscape` - what Escape does (normally cancels the dialog), or `null` for a dialog with
  no way to cancel.
- *Preconditions*: `dialog` is not `null`.
- *Postconditions*: The window's tab navigation is `KeyboardNavigationMode.Cycle`, so Tab from the last control wraps
  to the first and Shift+Tab from the first wraps to the last, never leaving the dialog
  (`AgentControl-DialogBehavior-TabStaysInside`). When the window opens, `FocusInitialControl` runs. When `onEscape`
  is given, an Escape key-down handler is attached.

**FocusInitialControl** (private): Takes the named control if it exists and can take the focus, otherwise the first
control in the window's visual tree that can; the focus is set with `NavigationMethod.Tab`, so the control shows its
keyboard focus ring (a focus set from code with the default method would show none, leaving the user unable to see
where the keyboard is) (`AgentControl-DialogBehavior-InitialFocus`).

**CanTakeFocus** (private): A control can hold the focus when it is focusable, effectively visible, effectively
enabled and a tab stop - so a hidden or disabled named control is skipped.

**Escape handler**: Attached for the bubbling phase of the key-down event, so Escape is seen on the way back up from
the focused control: a control that uses Escape itself (an open drop-down, an open menu) marks it handled and keeps it,
and only an unhandled Escape runs `onEscape` (once, marking the key handled)
(`AgentControl-DialogBehavior-EscapeCancels`).

#### Error Handling

`Apply` throws `ArgumentNullException` for a `null` dialog (`AgentControl-DialogBehavior-RequiresDialog`). Everything
else is tolerant: a missing, hidden or disabled named control falls back to the first tab stop, and a dialog with
nothing focusable simply gets no initial focus.

#### Dependencies

- **Avalonia** (OTS) - `Window`, `KeyboardNavigation`, `InputElement.KeyDownEvent`, `FocusManager` via
  `Control.Focus`.

#### Callers

- **AboutWindow**, **ConfirmationWindow**, **CredentialPromptWindow**, **GroupNameWindow**, **MessageBoxWindow**,
  **SelectPackageWindow**, **SettingsWindow** and **SourceEditorWindow** (`LauncherUI` views) - each calls `Apply` from
  its constructor with its own starting control and cancel action.
