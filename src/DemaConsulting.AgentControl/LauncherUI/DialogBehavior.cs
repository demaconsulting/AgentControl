// Copyright (c) DEMA Consulting
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     The keyboard behavior every AgentControl dialog shares: Tab and Shift+Tab cycle inside the
///     dialog, the first sensible control has the (visible) keyboard focus when it opens, and Escape
///     cancels a dialog that has a way to cancel.
/// </summary>
/// <remarks>
///     One helper instead of the same few lines in each dialog's code-behind, so the dialogs cannot
///     drift apart: a keyboard user gets the same, predictable behavior in the Settings window, the
///     source editor, the package picker, the sign-in prompt, the group-name prompt, the
///     confirmations, the message boxes and About. The dialogs themselves are shown modally by their
///     owner (<c>ShowDialog</c> with the owner window), which is what stops Tab and clicks from
///     reaching the window behind. Only the view layer uses this class; it holds no state, so it is
///     safe to call from the UI thread only (as all Avalonia controls are).
/// </remarks>
internal static class DialogBehavior
{
    /// <summary>
    ///     Applies the shared keyboard behavior to a dialog window.
    /// </summary>
    /// <remarks>
    ///     Tab navigation is set to <see cref="KeyboardNavigationMode.Cycle"/> on the window, so Tab
    ///     from the last control wraps to the first one and Shift+Tab from the first wraps to the last,
    ///     never leaving the dialog. The initial focus is taken with <see cref="NavigationMethod.Tab"/>
    ///     so the control shows its keyboard focus ring (a focus set from code with the default
    ///     method would show none, leaving the user unable to see where the keyboard is). Escape is
    ///     handled on the way back up from the focused control, so a control that uses Escape itself
    ///     (an open drop-down, an open menu) keeps it.
    /// </remarks>
    /// <param name="dialog">The dialog window. Must not be <see langword="null"/>.</param>
    /// <param name="initialFocusName">The XAML name of the control that takes the focus when the dialog
    ///     opens, or <see langword="null"/> (or a name that is missing, hidden or disabled) to use the
    ///     first control the Tab key reaches.</param>
    /// <param name="onEscape">What Escape does (normally cancels the dialog), or <see langword="null"/>
    ///     for a dialog with no way to cancel, where Escape does nothing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dialog"/> is
    ///     <see langword="null"/>.</exception>
    internal static void Apply(Window dialog, string? initialFocusName = null, Action? onEscape = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        // Tab cycles inside the dialog: the window is the root of the dialog's focus scope
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);

        // Put the focus in the dialog as it opens, so a keyboard user does not have to find it
        dialog.Opened += (_, _) => FocusInitialControl(dialog, initialFocusName);

        if (onEscape is not null)
        {
            dialog.AddHandler(
                InputElement.KeyDownEvent,
                (_, e) =>
                {
                    if (e.Key == Key.Escape && !e.Handled)
                    {
                        e.Handled = true;
                        onEscape();
                    }
                },
                RoutingStrategies.Bubble);
        }
    }

    /// <summary>
    ///     Gives the keyboard focus to the dialog's first sensible control, with the keyboard focus ring.
    /// </summary>
    /// <param name="dialog">The dialog window.</param>
    /// <param name="initialFocusName">The preferred control's name, or <see langword="null"/>.</param>
    private static void FocusInitialControl(Window dialog, string? initialFocusName)
    {
        // The preferred control when it can take the focus, else the first one Tab would reach
        var target = initialFocusName is null ? null : dialog.FindControl<Control>(initialFocusName);
        if (target is null || !CanTakeFocus(target))
        {
            target = FirstTabStop(dialog);
        }

        target?.Focus(NavigationMethod.Tab);
    }

    /// <summary>
    ///     Finds the first control the Tab key reaches inside a dialog, in keyboard navigation order.
    /// </summary>
    /// <remarks>
    ///     Asks Avalonia's own keyboard navigation for the first focusable control of the dialog (so
    ///     explicit tab indices and navigation containers are honored; visual-tree order would differ as
    ///     soon as a tab index is set), then steps forward with the same Tab navigation past any control
    ///     that cannot take the focus (Avalonia's first-focusable search does not skip a disabled one).
    ///     Anything the navigation returns outside the dialog, or a repeat, ends the search.
    /// </remarks>
    /// <param name="dialog">The dialog window.</param>
    /// <returns>The first control Tab reaches, or <see langword="null"/> when there is none.</returns>
    private static Control? FirstTabStop(Window dialog)
    {
        var visited = new HashSet<IInputElement>();
        var candidate = FocusManager.FindFirstFocusableElement(dialog);
        while (candidate is Control control && dialog.IsVisualAncestorOf(control) && visited.Add(control))
        {
            if (CanTakeFocus(control))
            {
                return control;
            }

            candidate = dialog.FocusManager?.FindNextElement(
                NavigationDirection.Next,
                new FindNextElementOptions { SearchRoot = dialog, FocusedElement = control });
        }

        return null;
    }

    /// <summary>
    ///     Tells whether a control can hold the keyboard focus and is reached by the Tab key.
    /// </summary>
    /// <param name="control">The control to test.</param>
    /// <returns><see langword="true"/> when it is focusable, visible, enabled and a tab stop.</returns>
    private static bool CanTakeFocus(Control control) =>
        control.Focusable
        && control.IsEffectivelyVisible
        && control.IsEffectivelyEnabled
        && KeyboardNavigation.GetIsTabStop(control);
}
