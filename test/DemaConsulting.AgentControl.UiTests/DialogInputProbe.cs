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
        return Retry.WhileFalse(
            () =>
            {
                window.SetForeground();
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
