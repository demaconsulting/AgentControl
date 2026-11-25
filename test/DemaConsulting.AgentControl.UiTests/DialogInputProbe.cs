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

using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Helpers for the tests that send real keyboard and mouse input to AgentControl's dialogs and
///     read back where the input and the keyboard focus ended up.
/// </summary>
/// <remarks>
///     Real input goes to whichever window the desktop has active, so every helper that sends input
///     first checks that the window it targets is the foreground window, and a click is only sent
///     to a point that belongs to the application under test. The focus is read as the top-level
///     window that owns the focused element (not the element itself), which is the contract the
///     dialogs make: Tab never leaves the dialog.
/// </remarks>
internal static class DialogInputProbe
{
    /// <summary>
    ///     How long to wait for the desktop to settle after input or a focus request.
    /// </summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How often to poll while waiting for the desktop to settle.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Win32 <c>IsWindowEnabled</c>: whether a window accepts input (a window that owns an open
    ///     modal dialog does not).
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <returns><see langword="true"/> when the window accepts input.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    /// <summary>
    ///     Win32 <c>GetForegroundWindow</c>: the window the user is currently working in.
    /// </summary>
    /// <returns>The foreground window's handle, or zero when there is none.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    ///     Win32 <c>SetForegroundWindow</c>.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <returns><see langword="true"/> when the window was brought to the foreground.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>
    ///     Win32 <c>BringWindowToTop</c>.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <returns><see langword="true"/> when the call succeeded.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    /// <summary>
    ///     Win32 <c>SwitchToThisWindow</c>: the shell's "alt-tab" switch to a window.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="altTab">Whether the switch is made as if by Alt+Tab.</param>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void SwitchToThisWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool altTab);

    /// <summary>
    ///     Win32 <c>GetWindowThreadProcessId</c>.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="processId">Receives the process id (unused).</param>
    /// <returns>The id of the thread that created the window.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    /// <summary>
    ///     Win32 <c>AttachThreadInput</c>.
    /// </summary>
    /// <param name="attach">The thread to attach.</param>
    /// <param name="attachTo">The thread to attach to.</param>
    /// <param name="doAttach">Whether to attach or detach.</param>
    /// <returns><see langword="true"/> when the call succeeded.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    /// <summary>
    ///     Win32 <c>GetCurrentThreadId</c>.
    /// </summary>
    /// <returns>The calling thread's id.</returns>
    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    ///     Taps the Alt key (down and up) so that this process counts as the one that last had input,
    ///     which lets it take the foreground. The tap changes nothing in the window that receives it.
    /// </summary>
    private static void TapAlt()
    {
        Keyboard.Press(VirtualKeyShort.ALT);
        Keyboard.Release(VirtualKeyShort.ALT);
    }

    /// <summary>
    ///     Brings a window to the foreground by sharing the input queue of the current foreground
    ///     window's thread for the duration of the call.
    /// </summary>
    /// <param name="handle">The window to bring forward.</param>
    private static void ForceForeground(IntPtr handle)
    {
        var current = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        var attached = foregroundThread != 0 && foregroundThread != current &&
                       AttachThreadInput(current, foregroundThread, true);
        try
        {
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(current, foregroundThread, false);
            }
        }
    }

    /// <summary>
    ///     Tells whether a window accepts input; a window that owns an open modal dialog does not.
    /// </summary>
    /// <param name="window">The window to test.</param>
    /// <returns><see langword="true"/> when the operating system has not disabled the window.</returns>
    public static bool IsEnabled(Window window) => IsWindowEnabled(HandleOf(window));

    /// <summary>
    ///     Waits until a window's enabled state is the expected one.
    /// </summary>
    /// <param name="window">The window to watch.</param>
    /// <param name="expected">The enabled state to wait for.</param>
    /// <returns><see langword="true"/> when the state was reached in time.</returns>
    public static bool WaitForEnabled(Window window, bool expected) =>
        Retry.WhileFalse(() => IsEnabled(window) == expected, SettleTimeout, PollInterval).Result;

    /// <summary>
    ///     Waits until a window is the foreground window, as it is when a dialog that had the focus
    ///     closes and hands the focus back to the window behind.
    /// </summary>
    /// <param name="window">The window to watch.</param>
    /// <returns><see langword="true"/> when the window became the foreground window in time.</returns>
    public static bool WaitForForeground(Window window) =>
        Retry.WhileFalse(() => GetForegroundWindow() == HandleOf(window), SettleTimeout, PollInterval).Result;

    /// <summary>
    ///     Makes a window the foreground window, so real keyboard input reaches it.
    /// </summary>
    /// <param name="window">The window to bring forward.</param>
    /// <returns><see langword="true"/> when the window became the foreground window in time.</returns>
    public static bool BringToForeground(Window window)
    {
        var handle = HandleOf(window);
        var attempt = 0;
        return Retry.WhileFalse(
            () =>
            {
                if (GetForegroundWindow() == handle)
                {
                    return true;
                }

                // Windows refuses SetForegroundWindow to a process that did not receive the last input
                // (the foreground lock). A synthetic Alt key tap makes this process the last to have had
                // input; attaching to the foreground window's thread and asking the shell to switch to the
                // window cover the cases where that is not enough. Each retry tries one more way.
                attempt++;
                window.SetForeground();
                if (GetForegroundWindow() == handle)
                {
                    return true;
                }

                TapAlt();
                ForceForeground(handle);
                if (attempt > 1)
                {
                    SwitchToThisWindow(handle, true);
                }

                return GetForegroundWindow() == handle;
            },
            SettleTimeout,
            PollInterval).Result;
    }

    /// <summary>
    ///     Sends one real key press (for example Tab) to the foreground window.
    /// </summary>
    /// <param name="key">The key to press.</param>
    public static void Press(VirtualKeyShort key)
    {
        Keyboard.Type(key);
        Thread.Sleep(150);
    }

    /// <summary>
    ///     Sends a real Shift+Tab chord to the foreground window.
    /// </summary>
    public static void PressShiftTab()
    {
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.TAB);
        Thread.Sleep(150);
    }

    /// <summary>
    ///     Sends real key presses that type a text to the foreground window.
    /// </summary>
    /// <param name="text">The text to type.</param>
    public static void TypeText(string text)
    {
        Keyboard.Type(text);
        Thread.Sleep(150);
    }

    /// <summary>
    ///     Gets the top-level window that owns the element that currently has the keyboard focus.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The window's handle, or zero when no element has the focus.</returns>
    public static IntPtr FocusedWindowHandle(AgentControlTestContext context)
    {
        var current = FocusedElement(context);
        while (current is not null && current.ControlType != ControlType.Window)
        {
            current = current.Parent;
        }

        return current is null ? IntPtr.Zero : HandleOf(current);
    }

    /// <summary>
    ///     Finds which of a set of controls currently has the keyboard focus: the control itself, or
    ///     any element inside it (the focus on a list's item or on a part of a control's template
    ///     counts as the focus on the list or the control).
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="automationIds">The AutomationIds of the controls to look for.</param>
    /// <returns>The first AutomationId in the set whose control holds the focus, or an empty string
    ///     when the focus is in none of them.</returns>
    public static string FocusedControlAmong(AgentControlTestContext context, IReadOnlyCollection<string> automationIds)
    {
        var current = FocusedElement(context);
        while (current is not null && current.ControlType != ControlType.Window)
        {
            var id = current.Properties.AutomationId.ValueOrDefault;
            if (!string.IsNullOrEmpty(id) && automationIds.Contains(id))
            {
                return id;
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    /// <summary>
    ///     Gets the screen position of the middle of an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The screen point.</returns>
    public static Point CenterOf(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        return new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }

    /// <summary>
    ///     Clicks the real mouse at a screen point, after checking that the point shows the
    ///     application under test so the click cannot land in an unrelated program.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="point">The screen point to click.</param>
    public static void ClickAt(AgentControlTestContext context, Point point)
    {
        var hit = context.Automation!.FromPoint(point);
        Assert.NotNull(hit);
        Assert.Equal(context.App!.ProcessId, hit.Properties.ProcessId.Value);

        Mouse.Click(point);
        Thread.Sleep(300);
    }

    /// <summary>
    ///     Brings the main window to the foreground, presses the left mouse button at a point, moves to
    ///     another point in small steps (an operating-system drag needs a moving pointer) and releases it
    ///     there.
    /// </summary>
    /// <param name="window">The window, which must be in front so the real input reaches it.</param>
    /// <param name="from">Where to press.</param>
    /// <param name="to">Where to release.</param>
    public static void Drag(Window window, Point from, Point to)
    {
        Assert.True(BringToForeground(window));
        Mouse.MoveTo(from);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(150);
        const int steps = 12;
        for (var step = 1; step <= steps; step++)
        {
            Mouse.MoveTo(new Point(from.X + ((to.X - from.X) * step / steps), from.Y + ((to.Y - from.Y) * step / steps)));
            Thread.Sleep(40);
        }

        Thread.Sleep(150);
        Mouse.Up(MouseButton.Left);
        Thread.Sleep(300);
    }

    /// <summary>
    ///     Gets a window's native handle.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The handle.</returns>
    private static IntPtr HandleOf(AutomationElement window) => window.Properties.NativeWindowHandle.ValueOrDefault;

    /// <summary>
    ///     Reads the element that has the keyboard focus, waiting briefly for the desktop to settle.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The focused element, or <see langword="null"/> when none could be read.</returns>
    private static AutomationElement? FocusedElement(AgentControlTestContext context) =>
        Retry.WhileNull(
            () => context.Automation!.FocusedElement(),
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMilliseconds(100),
            ignoreException: true).Result;
}
