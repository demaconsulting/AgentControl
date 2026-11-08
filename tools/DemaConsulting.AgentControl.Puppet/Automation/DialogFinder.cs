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

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DemaConsulting.AgentControl.Puppet.Automation;

/// <summary>
///     One control of a dialog, as <c>dialogs</c> lists it.
/// </summary>
/// <param name="AutomationId">The AutomationId, or <see langword="null"/> when there is none.</param>
/// <param name="Name">The control's name (its caption or text).</param>
/// <param name="Kind">The kind: <c>button</c>, <c>edit</c>, <c>checkbox</c>, <c>combo</c>, <c>list</c> or <c>text</c>.</param>
/// <param name="IsEnabled">Whether the control accepts input.</param>
/// <param name="HelpText">The control's help text (tooltip) when UI Automation exposes one, else an empty string.</param>
internal sealed record DialogControl(string? AutomationId, string Name, string Kind, bool IsEnabled, string HelpText);

/// <summary>
///     A dialog and its controls.
/// </summary>
/// <param name="Title">The window title.</param>
/// <param name="Controls">The visible controls.</param>
internal sealed record DialogDescription(string Title, IReadOnlyList<DialogControl> Controls);

/// <summary>
///     Finds and describes the dialogs (extra top-level windows) of an instance.
/// </summary>
internal static class DialogFinder
{
    /// <summary>
    ///     Lists the open dialogs, outer to inner: the titled, visible top-level windows of the process other than the main
    ///     window, including a dialog opened from a dialog (at any depth).
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <returns>Each dialog's window and description.</returns>
    internal static IReadOnlyList<(AutomationElement Window, DialogDescription Description)> Snapshot(AttachContext context)
    {
        var result = new List<(AutomationElement, DialogDescription)>();
        foreach (var dialog in context.Roots().Skip(1))
        {
            var title = UiActions.Safe(() => dialog.Properties.Name.ValueOrDefault, string.Empty);
            if (string.IsNullOrWhiteSpace(title) || UiActions.Safe(() => dialog.Properties.IsOffscreen.ValueOrDefault, false))
            {
                continue;
            }

            result.Add((dialog, Describe(dialog, title)));
        }

        return result;
    }

    /// <summary>
    ///     Lists every visible window of the instance other than the main window: dialogs and open pop-ups (a context menu
    ///     has no title, so it is not a <see cref="Snapshot"/> dialog but a screenshot should still show it).
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <returns>The windows.</returns>
    internal static IReadOnlyList<AutomationElement> VisibleWindows(AttachContext context) =>
        [.. context.Roots().Skip(1).Where(w => !UiActions.Safe(() => w.Properties.IsOffscreen.ValueOrDefault, false))];

    /// <summary>
    ///     Lists the visible native windows of the instance that UI Automation does not list as windows (an Avalonia flyout,
    ///     a combo box drop-down or a tooltip is a window of its own).
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <returns>The window handles.</returns>
    internal static IReadOnlyList<IntPtr> VisiblePopups(AttachContext context)
    {
        var known = context.Roots()
            .Select(w => UiActions.Safe(() => w.Properties.NativeWindowHandle.ValueOrDefault, IntPtr.Zero))
            .Where(h => h != IntPtr.Zero)
            .ToList();
        return WindowCapture.FindPopupHandles(context.ProcessId, known);
    }

    /// <summary>
    ///     Formats the dialogs as the text <c>dialogs</c> prints.
    /// </summary>
    /// <param name="pid">The instance's process id.</param>
    /// <param name="dialogs">The dialogs.</param>
    /// <returns>The text.</returns>
    internal static string Format(int pid, IReadOnlyList<DialogDescription> dialogs)
    {
        if (dialogs.Count == 0)
        {
            return $"NO_DIALOGS pid={pid}";
        }

        return string.Join(
            Environment.NewLine,
            dialogs.Select((dialog, index) =>
                $"DIALOG pid={pid} index={index} title=\"{dialog.Title}\"{Environment.NewLine}" +
                string.Join(Environment.NewLine, dialog.Controls.Select(c =>
                    $"  {c.Kind} id=\"{c.AutomationId ?? string.Empty}\" name=\"{UiTreeDumper.Escape(c.Name)}\" enabled={c.IsEnabled}" +
                    (c.HelpText.Length > 0 ? $" help=\"{UiTreeDumper.Escape(c.HelpText)}\"" : string.Empty)))));
    }

    /// <summary>
    ///     Classifies a control for the listing.
    /// </summary>
    /// <param name="type">The control type.</param>
    /// <param name="hasId">Whether the control has an AutomationId.</param>
    /// <returns>The kind, or <see langword="null"/> when the control is not listed.</returns>
    internal static string? KindOf(ControlType type, bool hasId) => type switch
    {
        ControlType.Button => "button",
        ControlType.Edit => "edit",
        ControlType.CheckBox => "checkbox",
        ControlType.ComboBox => "combo",
        ControlType.RadioButton => "radio",

        // Lists and texts are only worth a line when something can address them
        ControlType.List when hasId => "list",
        ControlType.Text when hasId => "text",
        _ => null,
    };

    /// <summary>
    ///     Lists the buttons, edits, check boxes, combo boxes, lists and identified texts of a dialog.
    /// </summary>
    /// <param name="dialog">The dialog window.</param>
    /// <param name="title">The dialog title.</param>
    /// <returns>The description.</returns>
    private static DialogDescription Describe(AutomationElement dialog, string title)
    {
        var elements = new List<AutomationElement>();
        CollectOwnControls(dialog, elements);
        var controls = new List<DialogControl>();
        foreach (var element in elements)
        {
            var id = UiActions.Safe(() => element.AutomationId, string.Empty);
            if (KindOf(UiActions.Safe(() => element.ControlType, ControlType.Custom), !string.IsNullOrWhiteSpace(id)) is not { } kind)
            {
                continue;
            }

            // Title-bar buttons (minimize, close) are chrome, not part of the dialog's content
            if (UiActions.Safe(() => element.IsOffscreen, false) || UiActions.Safe(() => element.Parent?.ControlType == ControlType.TitleBar, false))
            {
                continue;
            }

            var name = UiActions.Safe(() => element.Name, string.Empty);
            controls.Add(new DialogControl(
                string.IsNullOrWhiteSpace(id) ? null : id,
                name,
                kind,
                UiActions.Safe(() => element.IsEnabled, true),
                UiActions.HelpTextOf(element)));
        }

        return new DialogDescription(title, controls);
    }

    /// <summary>
    ///     Collects the elements under a window in tree order, without entering a window nested in it: a dialog opened from
    ///     this one is listed as a dialog of its own.
    /// </summary>
    /// <param name="parent">The window or element to walk.</param>
    /// <param name="elements">Receives the elements.</param>
    private static void CollectOwnControls(AutomationElement parent, List<AutomationElement> elements)
    {
        foreach (var child in UiActions.Safe(() => parent.FindAllChildren(), []))
        {
            if (UiActions.Safe(() => child.ControlType, ControlType.Custom) == ControlType.Window)
            {
                continue;
            }

            elements.Add(child);
            CollectOwnControls(child, elements);
        }
    }
}
