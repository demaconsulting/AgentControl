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

using DemaConsulting.AgentControl.Puppet.Automation;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace DemaConsulting.AgentControl.Puppet;

/// <content>
///     Real-input commands: <c>click-at</c>, <c>drag-element</c>, <c>wheel</c> and <c>hover</c>.
/// </content>
/// <remarks>
///     These commands move the real cursor and send real mouse events. Targets are physical screen pixels (read them
///     from <c>find</c>) or elements (found by AutomationId, then aimed at the centre of their bounds). Before anything is
///     sent, <see cref="Guard"/> checks that the topmost window at the target pixel belongs to the instance.
/// </remarks>
internal static partial class Commands
{
    /// <summary>
    ///     Clicks a screen pixel.
    /// </summary>
    /// <param name="parsed">The parsed command: x and y in physical screen pixels, <c>--right</c>, <c>--double</c>, <c>--shift</c>, <c>--ctrl</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int ClickAt(ParsedCommand parsed)
    {
        RequireArgs(parsed, 2, "agentcontrol-puppet click-at <x> <y> (physical screen pixels) [--right] [--double] [--shift|--ctrl] [--instance <alias>]");
        var x = Number(parsed.Positional[0], "x");
        var y = Number(parsed.Positional[1], "y");
        var modifiers = ModifiersOf(parsed);
        var button = parsed.HasOption("right") ? MouseButton.Right : MouseButton.Left;
        using var context = Attach(parsed);
        InputDriver.BringToFront(context);
        var at = new ScreenPoint(x, y);
        Guard(context, at, $"the point ({x},{y})");
        InputDriver.Click(at, button, modifiers, parsed.HasOption("double") ? 2 : 1);
        return Ok($"OK click-at screen=({at.PixelX},{at.PixelY}) button={button}{(parsed.HasOption("double") ? " double" : string.Empty)}");
    }

    /// <summary>
    ///     Drags a control (for example a splitter) by a screen offset, pressing at the centre of its bounds.
    /// </summary>
    /// <param name="parsed">The parsed command: the AutomationId, dx and dy in physical screen pixels.</param>
    /// <returns>The exit code.</returns>
    internal static int DragElement(ParsedCommand parsed)
    {
        RequireArgs(parsed, 3, "agentcontrol-puppet drag-element <automationId> <dx> <dy> (physical screen pixels) [--item <repo|index>] [--window <title>] [--instance <alias>]");
        var id = parsed.Positional[0];
        var dx = Number(parsed.Positional[1], "dx");
        var dy = Number(parsed.Positional[2], "dy");
        using var context = Attach(parsed);
        InputDriver.BringToFront(context);
        var element = Locate(context, parsed, id);
        var from = CenterOf(element, id);
        var to = new ScreenPoint(from.X + dx, from.Y + dy);
        Guard(context, from, $"element '{id}'");
        Guard(context, to, "the drop position");
        InputDriver.Drag(from, to, KeyChordModifiers.None);
        return Ok($"OK drag-element id={id}{ScopeSuffix(parsed)} delta=({dx},{dy}) screen=({from.PixelX},{from.PixelY})->({to.PixelX},{to.PixelY})");
    }

    /// <summary>
    ///     Turns the mouse wheel over the window, an element or a pixel (with Ctrl it usually zooms).
    /// </summary>
    /// <param name="parsed">The parsed command: the notches (positive up), <c>--ctrl</c>, <c>--on &lt;automationId&gt;</c> or <c>--at x,y</c> in physical screen pixels.</param>
    /// <returns>The exit code.</returns>
    internal static int Wheel(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet wheel <notches (+up/-down)> [--ctrl] [--on <automationId> | --at <x>,<y>] [--item <repo|index>] [--instance <alias>]");
        var notches = (int)Number(parsed.Positional[0], "notches");
        using var context = Attach(parsed);
        InputDriver.BringToFront(context);
        ScreenPoint target;
        if (parsed.GetOption("at") is { } at)
        {
            var parts = at.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                throw PuppetException.Usage("--at needs '<x>,<y>' in physical screen pixels.");
            }

            target = new ScreenPoint(Number(parts[0], "--at x"), Number(parts[1], "--at y"));
        }
        else if (parsed.GetOption("on") is { } on)
        {
            target = CenterOf(Locate(context, parsed, on), on);
        }
        else
        {
            target = ToRect(context.MainWindow.BoundingRectangle).Center;
        }

        Guard(context, target, "the wheel position");
        InputDriver.Wheel(target, notches, parsed.HasOption("ctrl") ? KeyChordModifiers.Control : KeyChordModifiers.None);
        return Ok($"OK wheel notches={notches} ctrl={parsed.HasOption("ctrl")} at=({target.PixelX},{target.PixelY})");
    }

    /// <summary>
    ///     Rests the real cursor on an element so its tooltip opens, optionally taking a screen copy while it shows.
    /// </summary>
    /// <remarks>
    ///     The element's bounds are all that is used, so a disabled element can be hovered (its tooltip explains why it is
    ///     disabled). The cursor is parked beside the element first so the tooltip opens fresh.
    /// </remarks>
    /// <param name="parsed">The parsed command: the AutomationId, <c>--wait</c>, <c>--shot</c>, and the scope options.</param>
    /// <returns>The exit code.</returns>
    internal static int Hover(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet hover <automationId> [--wait <ms>] [--shot <path.png>] [--item <repo|index>] [--window <title>] [--instance <alias>]");
        var id = parsed.Positional[0];
        var options = HoverOptions.Parse(parsed);
        using var context = Attach(parsed);
        InputDriver.BringToFront(context);
        var element = Locate(context, parsed, id);
        var bounds = BoundsOf(element, id);
        var target = bounds.Center;

        // Refuse before anything moves: the target must be on this instance's own window
        Guard(context, target, $"element '{id}'");
        var park = HoverPlanner.ChooseParkPoint(
            bounds,
            ToRect(context.MainWindow.BoundingRectangle),
            candidate => InputDriver.IsOwnedBy(candidate, context.ProcessId));

        // Park, arrive in steps and rest; the shot is taken while the cursor still rests (and without raising the
        // window again, which could close the tooltip)
        InputDriver.Hover(park, target, TimeSpan.FromMilliseconds(options.WaitMs));
        var shot = string.Empty;
        if (options.ShotPath is { } path)
        {
            _ = SaveShot(context, path, fromScreen: true, bringToFront: false);
            shot = $" shot=\"{Path.GetFullPath(path)}\"";
        }

        var parked = park is { } p ? $"({p.PixelX},{p.PixelY})" : "none";
        return Ok($"OK hover id={id}{ScopeSuffix(parsed)} at=({target.PixelX},{target.PixelY}) park={parked} wait={options.WaitMs}{shot}");
    }

    /// <summary>
    ///     Reads the modifier flags (<c>--shift</c>, <c>--ctrl</c>) of a command.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The modifiers to hold.</returns>
    private static KeyChordModifiers ModifiersOf(ParsedCommand parsed) =>
        (parsed.HasOption("shift") ? KeyChordModifiers.Shift : KeyChordModifiers.None)
        | (parsed.HasOption("ctrl") ? KeyChordModifiers.Control : KeyChordModifiers.None);

    /// <summary>
    ///     Converts a UI Automation rectangle to a screen rectangle.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>The screen rectangle.</returns>
    private static ScreenRect ToRect(System.Drawing.Rectangle rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    /// <summary>
    ///     Reads where an element is, refusing one that is not drawn.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="id">Its AutomationId, for messages.</param>
    /// <returns>The bounds.</returns>
    /// <exception cref="PuppetException">Thrown with <see cref="ExitCodes.Obscured"/> when the element has no area or is off screen.</exception>
    private static ScreenRect BoundsOf(AutomationElement element, string id)
    {
        var bounds = ToRect(UiActions.Safe(() => element.BoundingRectangle, default));
        if (bounds.IsEmpty || UiActions.Safe(() => element.IsOffscreen, false))
        {
            throw PuppetException.Obscured($"element '{id}' is not on screen (no bounds, or scrolled out of view); no input was sent.");
        }

        return bounds;
    }

    /// <summary>
    ///     Gets the centre of an element's bounds.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="id">Its AutomationId, for messages.</param>
    /// <returns>The centre pixel.</returns>
    private static ScreenPoint CenterOf(AutomationElement element, string id) => BoundsOf(element, id).Center;

    /// <summary>
    ///     Refuses to send real input unless the target is on the instance's own window.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <param name="at">The target pixel.</param>
    /// <param name="what">What the target is, for the message.</param>
    /// <exception cref="PuppetException">Thrown with <see cref="ExitCodes.Obscured"/> when the input would not land on the instance.</exception>
    private static void Guard(AttachContext context, ScreenPoint at, string what)
    {
        // Another window (typically the terminal running the Puppet) may have taken the foreground back since
        // BringToFront: try again a few times before giving up
        for (var attempt = 0; attempt < 3 && !InputDriver.IsOwnedBy(at, context.ProcessId); attempt++)
        {
            InputDriver.BringToFront(context);
            Thread.Sleep(200);
        }

        if (!InputDriver.IsOwnedBy(at, context.ProcessId))
        {
            throw PuppetException.Obscured($"{what} at ({at.PixelX},{at.PixelY}) is on {InputDriver.DescribeOwner(at)}, not on instance '{context.Alias}' (covered by another window, off screen or the desktop is locked). No input was sent.");
        }
    }
}
