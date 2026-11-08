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

using System.Drawing.Imaging;
using DemaConsulting.AgentControl.Puppet.Automation;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace DemaConsulting.AgentControl.Puppet;

/// <content>
///     UI Automation commands: <c>window</c>, <c>tree</c>, <c>find</c>, <c>click</c>, <c>type</c>, <c>choose</c>,
///     <c>select</c>, <c>key</c>, <c>read</c>, <c>menu</c>, <c>dialogs</c>, <c>shot</c> and <c>place</c>.
/// </content>
internal static partial class Commands
{
    /// <summary>
    ///     Prints the main window's identity and bounds.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The exit code.</returns>
    internal static int Window(ParsedCommand parsed)
    {
        using var context = Attach(parsed);
        var rect = context.MainWindow.BoundingRectangle;
        var windows = context.Roots().Count;
        return Ok($"WINDOW instance={context.Alias} pid={context.ProcessId} title=\"{context.MainWindow.Title}\" bounds=({rect.X},{rect.Y},{rect.Width},{rect.Height}) windows={windows}");
    }

    /// <summary>
    ///     Prints the UI Automation tree of the instance (or of one element).
    /// </summary>
    /// <param name="parsed">The parsed command: <c>--depth</c>, <c>--under</c>, and the scope options.</param>
    /// <returns>The exit code.</returns>
    internal static int Tree(ParsedCommand parsed)
    {
        var depth = IntOption(parsed, "depth", 6);
        using var context = Attach(parsed);
        var scope = ElementScope.FromCommand(parsed);
        if (parsed.GetOption("under") is { } under)
        {
            var root = Locate(context, parsed, under);
            Console.Write(UiTreeDumper.Dump(root, depth));
            return ExitCodes.Success;
        }

        // Dialogs and pop-ups are separate top-level windows, so each root is dumped in turn
        foreach (var root in ElementFinder.SearchRoots(context, scope))
        {
            Console.Write(UiTreeDumper.Dump(root, depth));
        }

        return ExitCodes.Success;
    }

    /// <summary>
    ///     Describes one element.
    /// </summary>
    /// <param name="parsed">The parsed command: the AutomationId.</param>
    /// <returns>The exit code.</returns>
    internal static int Find(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet find <automationId> [--item <repo|index>] [--window <title>] [--instance <alias>]");
        return WithElement(parsed, parsed.Positional[0], (_, element) =>
        {
            var r = UiActions.Safe(() => element.BoundingRectangle, default);
            var help = UiActions.HelpTextOf(element);
            return Ok(
                $"FOUND id=\"{parsed.Positional[0]}\"{ScopeSuffix(parsed)} type={UiActions.Safe(() => element.ControlType.ToString(), "?")} name=\"{UiTreeDumper.Escape(UiActions.Safe(() => element.Name, string.Empty))}\" " +
                $"enabled={UiActions.Safe(() => element.IsEnabled, true)} offscreen={UiActions.Safe(() => element.IsOffscreen, false)} bounds=({r.X},{r.Y},{r.Width},{r.Height})" +
                (help.Length > 0 ? $" help=\"{UiTreeDumper.Escape(help)}\"" : string.Empty));
        });
    }

    /// <summary>
    ///     Clicks (invokes) an element through UI Automation patterns.
    /// </summary>
    /// <param name="parsed">The parsed command: the AutomationId.</param>
    /// <returns>The exit code.</returns>
    internal static int Click(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet click <automationId> [--item <repo|index>] [--window <title>] [--instance <alias>]");
        return WithElement(parsed, parsed.Positional[0], (_, element) =>
        {
            Activate(element);
            return Ok($"OK clicked automationId=\"{parsed.Positional[0]}\"{ScopeSuffix(parsed)}");
        });
    }

    /// <summary>
    ///     Sets the text of an edit box.
    /// </summary>
    /// <param name="parsed">The parsed command: the AutomationId and the text.</param>
    /// <returns>The exit code.</returns>
    internal static int Type(ParsedCommand parsed)
    {
        RequireArgs(parsed, 2, "agentcontrol-puppet type <automationId> \"<text>\" [--item <repo|index>] [--window <title>] [--instance <alias>]");
        var automationId = parsed.Positional[0];
        var text = parsed.Positional[1];
        return WithElement(parsed, automationId, (_, element) =>
        {
            var via = "value-pattern";
            if (element.Patterns.Value.IsSupported)
            {
                if (UiActions.Safe(() => element.Patterns.Value.Pattern.IsReadOnly.ValueOrDefault, false))
                {
                    throw new PuppetException(ExitCodes.ReadOnly, "READ_ONLY", $"'{automationId}' reports a read-only Value pattern.");
                }

                if (!UiActions.Safe(() => element.IsEnabled, true))
                {
                    throw new PuppetException(ExitCodes.Disabled, "DISABLED", $"Cannot type into '{automationId}' because it is disabled.");
                }

                element.Patterns.Value.Pattern.SetValue(text);
            }
            else
            {
                via = "keyboard";
                TypeWithKeyboard(element, text);
            }

            return Ok($"OK type automationId=\"{automationId}\"{ScopeSuffix(parsed)} length={text.Length} via={via}");
        });
    }

    /// <summary>
    ///     Picks an item of a combo box by its text.
    /// </summary>
    /// <param name="parsed">The parsed command: the combo box's AutomationId and the item text.</param>
    /// <returns>The exit code.</returns>
    internal static int Choose(ParsedCommand parsed)
    {
        RequireArgs(parsed, 2, "agentcontrol-puppet choose <comboAutomationId> \"<item text>\" [--window <title>] [--instance <alias>]");
        var text = parsed.Positional[1];
        return WithElement(parsed, parsed.Positional[0], (context, combo) =>
        {
            if (!UiActions.Safe(() => combo.IsEnabled, true))
            {
                throw new PuppetException(ExitCodes.Disabled, "DISABLED", $"Cannot choose in '{parsed.Positional[0]}' because it is disabled.");
            }

            if (combo.Patterns.ExpandCollapse.IsSupported)
            {
                combo.Patterns.ExpandCollapse.Pattern.Expand();
                Thread.Sleep(250);
            }

            // The drop-down is a pop-up window of the instance, so every window is searched
            var item = context.Roots()
                .SelectMany(root => UiActions.Safe(() => root.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)), []))
                .FirstOrDefault(e => UiActions.ItemTexts(e).Any(t => string.Equals(t, text, StringComparison.OrdinalIgnoreCase)))
                ?? throw new PuppetException(ExitCodes.NotFound, NotFoundCode, $"'{parsed.Positional[0]}' has no item \"{text}\".");
            UiActions.Activate(item);
            _ = UiActions.Safe(() => { combo.Patterns.ExpandCollapse.Pattern.Collapse(); return true; }, false);
            return Ok($"OK chose automationId=\"{parsed.Positional[0]}\" item=\"{text}\"");
        });
    }

    /// <summary>
    ///     Selects an item of a list by its text (or index).
    /// </summary>
    /// <param name="parsed">The parsed command: the list's AutomationId and the item text or index.</param>
    /// <returns>The exit code.</returns>
    internal static int Select(ParsedCommand parsed)
    {
        RequireArgs(parsed, 2, "agentcontrol-puppet select <listAutomationId> \"<item text or index>\" [--window <title>] [--instance <alias>]");
        var listId = parsed.Positional[0];
        var spec = parsed.Positional[1];
        return WithElement(parsed, listId, (_, list) =>
        {
            var items = UiActions.ListItemsOf(list);
            var texts = items.Select(i => UiActions.ListItemTexts(listId, i)).ToList();
            if (!ItemMatcher.TryResolve(texts, spec, out var index, out var error))
            {
                throw new PuppetException(ExitCodes.NotFound, NotFoundCode, $"'{listId}': {error}");
            }

            Activate(items[index]);
            return Ok($"OK selected automationId=\"{listId}\" item=\"{(texts[index].Count > 0 ? UiTreeDumper.Escape(texts[index][0]) : string.Empty)}\" index={index}");
        });
    }

    /// <summary>
    ///     Presses a key chord with real keyboard input.
    /// </summary>
    /// <param name="parsed">The parsed command: the chord and <c>--to</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Key(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet key <chord, e.g. Ctrl+C|Delete|F2|Escape|Ctrl+Enter> [--to <automationId>] [--item <repo|index>] [--instance <alias>]");
        if (!KeyChord.TryParse(parsed.Positional[0], out var chord, out var error))
        {
            throw PuppetException.Usage(error);
        }

        using var context = Attach(parsed);
        InputDriver.BringToFront(context);
        if (parsed.GetOption("to") is { } id)
        {
            var requested = Locate(context, parsed, id);
            if (!UiActions.TryFocus(KeyboardTarget(requested)))
            {
                throw new PuppetException(ExitCodes.NoFocus, "NO_FOCUS", $"'{id}' would not take keyboard focus.");
            }
        }

        var focused = context.TryGetFocusedElement();
        var ownerPid = focused is null ? 0 : UiActions.Safe(() => focused.Properties.ProcessId.ValueOrDefault, 0);
        if (focused is null || ownerPid != context.ProcessId)
        {
            throw new PuppetException(ExitCodes.NoFocus, "NO_FOCUS", $"No element of instance '{context.Alias}' holds keyboard focus (is the desktop locked, or another window in front?).");
        }

        var target = UiActions.Describe(focused);
        InputDriver.PressChord(chord);
        return Ok($"OK key={chord} target=\"{target}\"");
    }

    /// <summary>
    ///     Prints the text or state an element shows.
    /// </summary>
    /// <param name="parsed">The parsed command: the AutomationId and optionally <c>--raw</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Read(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet read <automationId> [--raw] [--item <repo|index>] [--window <title>] [--instance <alias>]");
        return WithElement(parsed, parsed.Positional[0], (_, element) =>
        {
            var text = UiActions.ReadText(element);
            if (parsed.HasOption("raw"))
            {
                return Ok(text);
            }

            var line = $"READ id=\"{parsed.Positional[0]}\"{ScopeSuffix(parsed)} text=\"{UiTreeDumper.Escape(text)}\" enabled={UiActions.Safe(() => element.IsEnabled, true)}";
            if (element.Patterns.Toggle.IsSupported)
            {
                line += $" toggle={UiActions.Safe(() => element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault.ToString(), "?")}";
            }

            if (element.Patterns.SelectionItem.IsSupported)
            {
                line += $" selected={UiActions.Safe(() => element.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault, false)}";
            }

            return Ok(line);
        });
    }

    /// <summary>
    ///     Walks a menu path through the menus of the instance.
    /// </summary>
    /// <param name="parsed">The parsed command: the menu path.</param>
    /// <returns>The exit code.</returns>
    internal static int Menu(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet menu <Menu>/<Item>[/<Sub>] (item text or AutomationId, e.g. RefreshMenuItem) [--window <title>] [--instance <alias>]");
        var segments = MenuNavigator.Split(parsed.Positional[0]);
        if (segments.Count == 0)
        {
            throw PuppetException.Usage("The menu path is empty.");
        }

        using var context = Attach(parsed);
        var result = MenuNavigator.Invoke(ElementFinder.WindowsOf(context, ElementScope.FromCommand(parsed)), segments);
        if (result.Disabled)
        {
            throw new PuppetException(ExitCodes.Disabled, "DISABLED", $"Menu item '{result.FailedSegment}' ({result.InvokedId}) is disabled.");
        }

        if (!result.Succeeded)
        {
            throw new PuppetException(ExitCodes.NotFound, NotFoundCode, $"Menu item '{result.FailedSegment}' of '{parsed.Positional[0]}' was not found (is its menu open? try: click MoreActionsButton --item <repo>).");
        }

        return Ok($"OK menu path=\"{parsed.Positional[0]}\" id=\"{result.InvokedId}\"");
    }

    /// <summary>
    ///     Lists the open dialogs and their controls.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The exit code: <see cref="ExitCodes.BlockedOnDialog"/> when a dialog is open.</returns>
    internal static int Dialogs(ParsedCommand parsed)
    {
        using var context = Attach(parsed);
        var dialogs = DialogFinder.Snapshot(context);
        Console.WriteLine(DialogFinder.Format(context.ProcessId, [.. dialogs.Select(d => d.Description)]));
        return dialogs.Count == 0 ? ExitCodes.Success : ExitCodes.BlockedOnDialog;
    }

    /// <summary>
    ///     Saves a PNG of the main window with any open dialogs and pop-ups drawn in at their places.
    /// </summary>
    /// <param name="parsed">The parsed command: the output path.</param>
    /// <returns>The exit code.</returns>
    internal static int Shot(ParsedCommand parsed)
    {
        RequireArgs(parsed, 1, "agentcontrol-puppet shot <path.png> [--screen] [--instance <alias>]");
        using var context = Attach(parsed);
        return SaveShot(context, parsed.Positional[0], parsed.HasOption("screen"));
    }

    /// <summary>
    ///     Saves a PNG of the main window, either rendered with <c>PrintWindow</c> or copied from the screen.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <param name="file">The output path.</param>
    /// <param name="fromScreen"><see langword="true"/> to copy the screen pixels (needs the window uncovered).</param>
    /// <param name="bringToFront">
    ///     <see langword="true"/> (the default) to raise the window before a screen copy; <see langword="false"/> when the caller
    ///     has just raised and checked it and raising it again could close what the shot should show (a hover tooltip).
    /// </param>
    /// <returns>The exit code.</returns>
    private static int SaveShot(AttachContext context, string file, bool fromScreen, bool bringToFront = true)
    {
        var path = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());

        // PrintWindow renders each window itself, so a covering window does not end up in the picture
        if (fromScreen && bringToFront)
        {
            InputDriver.BringToFront(context);
        }

        var windows = DialogFinder.VisibleWindows(context);
        var popups = DialogFinder.VisiblePopups(context);
        using var bitmap = fromScreen
            ? WindowCapture.CaptureScreen(context.MainWindow, windows, popups)
            : WindowCapture.Render(context.MainWindow, windows, popups);
        bitmap.Save(path, ImageFormat.Png);
        return Ok($"OK shot path=\"{path}\" bytes={new FileInfo(path).Length} size={bitmap.Width}x{bitmap.Height} windows={windows.Count} popups={popups.Count}{(fromScreen ? " source=screen" : string.Empty)}");
    }

    /// <summary>
    ///     Moves and resizes the main window of an instance.
    /// </summary>
    /// <param name="parsed">The parsed command: <c>--x --y --width --height</c>, or <c>--work-area</c> with <c>--left-gap</c> and <c>--margin</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Place(ParsedCommand parsed)
    {
        var request = PlacementRequest.Parse(parsed);
        using var context = Attach(parsed);
        var handle = context.MainWindow.Properties.NativeWindowHandle.ValueOrDefault;
        if (handle == IntPtr.Zero)
        {
            throw new PuppetException(ExitCodes.NotFound, NotFoundCode, "the main window has no window handle.");
        }

        var workArea = WindowPlacement.GetWorkArea();
        var target = request.Resolve(workArea);
        var actual = WindowPlacement.Place(handle, target);
        Thread.Sleep(300);
        return Ok($"OK place instance={context.Alias} bounds=({actual.X},{actual.Y},{actual.Width},{actual.Height}) workArea=({workArea.X},{workArea.Y},{workArea.Width},{workArea.Height})");
    }

    /// <summary>
    ///     Activates an element through UI Automation patterns, reporting a disabled element with its own exit code.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <exception cref="PuppetException">Thrown with <see cref="ExitCodes.Disabled"/> when the element is disabled.</exception>
    private static void Activate(AutomationElement element)
    {
        try
        {
            UiActions.Activate(element);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new PuppetException(ExitCodes.Disabled, "DISABLED", ex.Message);
        }
    }

    /// <summary>
    ///     Finds the edit box inside an element, or the element itself.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The element that takes the keyboard.</returns>
    private static AutomationElement KeyboardTarget(AutomationElement element) =>
        element.ControlType == ControlType.Edit
            ? element
            : UiActions.Safe(() => element.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)), null) ?? element;

    /// <summary>
    ///     Replaces the text of an element by typing it (for edit boxes without a Value pattern).
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="text">The text.</param>
    /// <exception cref="PuppetException">Thrown when the element will not take focus.</exception>
    private static void TypeWithKeyboard(AutomationElement element, string text)
    {
        var target = KeyboardTarget(element);
        if (!UiActions.TryFocus(target, TimeSpan.FromSeconds(1)))
        {
            throw new PuppetException(ExitCodes.NoFocus, "NO_FOCUS", $"'{UiActions.Describe(element)}' would not take keyboard focus.");
        }

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Press(VirtualKeyShort.DELETE);
        Keyboard.Release(VirtualKeyShort.DELETE);
        if (text.Length > 0)
        {
            Keyboard.Type(text);
        }
    }
}
