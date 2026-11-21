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

using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Opens the operating system's folder picker from a button of AgentControl's main window and
///     finds the picker as the native window it is.
/// </summary>
/// <remarks>
///     <para>
///     The application shows the Windows Common Item Dialog from a thread of its own, owned by the
///     main window. The picker is found through the operating system's list of the process's
///     top-level windows (a dialog-class window with the expected title, owned by the main window),
///     not by walking the UI Automation tree below the main window: that walk crosses into the
///     picker's whole shell view while the picker is still filling it, and on a slow build machine
///     it reported no picker for 15 seconds although the teardown found the picker open moments
///     later.
///     </para>
///     <para>
///     The button is only invoked once the main window accepts input and the button is enabled, and
///     the wait allows for a first picker that takes a long time to start. Should the click still
///     have been lost (no dialog window exists at all and the main window was never disabled by a
///     modal dialog), it is invoked once more. A picker that does not open reports every window the
///     process has, so the failure can be told apart from a picker with another title or owner.
///     </para>
/// </remarks>
internal static class NativeFolderPicker
{
    /// <summary>
    ///     The window class of the Windows dialogs, the Common Item Dialog among them.
    /// </summary>
    private const string DialogClassName = "#32770";

    /// <summary>
    ///     The <c>GetWindow</c> relationship that returns a window's owner.
    /// </summary>
    private const uint GwOwner = 4;

    /// <summary>
    ///     How long the main window gets to be ready for the click.
    /// </summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     How long the picker gets to open after the click.
    /// </summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How long after the click, with no sign of a dialog, the click is taken as lost and sent again.
    /// </summary>
    private static readonly TimeSpan ClickRetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     How often to poll while waiting.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Win32 <c>EnumWindows</c> callback: receives one top-level window.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="parameter">The value passed to <c>EnumWindows</c> (unused).</param>
    /// <returns><see langword="true"/> to continue the enumeration.</returns>
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    /// <summary>
    ///     Win32 <c>EnumWindows</c>: calls a callback for every top-level window of the desktop.
    /// </summary>
    /// <param name="callback">The callback.</param>
    /// <param name="parameter">A value passed to the callback.</param>
    /// <returns><see langword="true"/> when the enumeration ran.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    /// <summary>
    ///     Win32 <c>GetWindowThreadProcessId</c>: the process that created a window.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="processId">Receives the process id.</param>
    /// <returns>The id of the thread that created the window.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>
    ///     Win32 <c>GetClassNameW</c>: a window's class name.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="buffer">Receives the class name.</param>
    /// <param name="maxCount">The buffer's length in characters.</param>
    /// <returns>The number of characters copied, or zero on failure.</returns>
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetClassName(IntPtr window, [Out] char[] buffer, int maxCount);

    /// <summary>
    ///     Win32 <c>GetWindowTextW</c>: a window's title.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="buffer">Receives the title.</param>
    /// <param name="maxCount">The buffer's length in characters.</param>
    /// <returns>The number of characters copied, or zero when there is no title.</returns>
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowText(IntPtr window, [Out] char[] buffer, int maxCount);

    /// <summary>
    ///     Win32 <c>GetWindow</c>: a window related to another one, here its owner.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="command">The relationship to look up.</param>
    /// <returns>The related window's handle, or zero when there is none.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    /// <summary>
    ///     Win32 <c>IsWindowVisible</c>: whether a window is shown.
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <returns><see langword="true"/> when the window is shown.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

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
    ///     Invokes a button of the main window that opens the folder picker and waits for the picker,
    ///     as described on <see cref="NativeFolderPicker"/>.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The application's main window.</param>
    /// <param name="button">The button that opens the picker.</param>
    /// <param name="title">The title the application gives the picker.</param>
    /// <returns>The picker.</returns>
    public static Window Open(AgentControlTestContext context, Window mainWindow, AutomationElement button, string title)
    {
        var processId = (uint)context.App!.ProcessId;
        var mainHandle = mainWindow.Properties.NativeWindowHandle.Value;

        // Wait until the main window accepts input, no dialog is open and the button can be clicked
        var ready = Retry.WhileFalse(
            () => IsWindowEnabled(mainHandle) && button.IsEnabled &&
                  !WindowsOf(processId).Any(window => window.ClassName == DialogClassName && window.IsVisible),
            ReadyTimeout,
            PollInterval,
            ignoreException: true).Result;
        Assert.True(
            ready,
            $"The main window was not ready to open the folder picker.{Environment.NewLine}{DescribeWindows(processId, mainHandle)}");

        button.AsButton().Invoke();
        var clock = Stopwatch.StartNew();
        var clicks = 1;
        var mainWasDisabled = false;
        var pickerHandle = IntPtr.Zero;
        while (clock.Elapsed < OpenTimeout)
        {
            var windows = WindowsOf(processId);
            pickerHandle = windows
                .FirstOrDefault(window => window.ClassName == DialogClassName && window.IsVisible &&
                                          window.Owner == mainHandle &&
                                          string.Equals(window.Title, title, StringComparison.Ordinal))
                ?.Handle ?? IntPtr.Zero;
            if (pickerHandle != IntPtr.Zero)
            {
                break;
            }

            // Only a click that left no trace at all is sent again, so a slow picker is never opened twice
            mainWasDisabled |= !IsWindowEnabled(mainHandle);
            if (clicks == 1 && clock.Elapsed >= ClickRetryDelay && !mainWasDisabled &&
                !windows.Any(window => window.ClassName == DialogClassName))
            {
                Log($"No folder picker {clock.ElapsedMilliseconds} ms after the click; clicking again.");
                button.AsButton().Invoke();
                clicks++;
            }

            Thread.Sleep(PollInterval);
        }

        Assert.True(
            pickerHandle != IntPtr.Zero,
            $"The folder picker \"{title}\" did not open within {OpenTimeout.TotalSeconds:0} seconds " +
            $"({clicks} click(s)).{Environment.NewLine}{DescribeWindows(processId, mainHandle)}");
        Log($"The folder picker \"{title}\" opened {clock.ElapsedMilliseconds} ms after the click ({clicks} click(s)).");

        // The picker's UI Automation element may lag a moment behind its window
        var picker = Retry.WhileNull(
            () => context.Automation!.FromHandle(pickerHandle)?.AsWindow(),
            TimeSpan.FromSeconds(10),
            PollInterval,
            ignoreException: true).Result;
        Assert.True(picker is not null, $"The folder picker \"{title}\" is open but UI Automation cannot read it.");
        return picker;
    }

    /// <summary>
    ///     Lists the top-level windows (owned windows included) of a process.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <returns>The windows.</returns>
    private static List<NativeWindow> WindowsOf(uint processId)
    {
        var windows = new List<NativeWindow>();
        EnumWindows(
            (handle, _) =>
            {
                GetWindowThreadProcessId(handle, out var owner);
                if (owner == processId)
                {
                    windows.Add(new NativeWindow(
                        handle,
                        ReadText(handle, GetClassName),
                        ReadText(handle, GetWindowText),
                        GetWindow(handle, GwOwner),
                        IsWindowVisible(handle),
                        IsWindowEnabled(handle)));
                }

                return true;
            },
            IntPtr.Zero);
        return windows;
    }

    /// <summary>
    ///     Reads a text of a window (its class name or title).
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <param name="read">The Win32 function that copies the text into a buffer.</param>
    /// <returns>The text, or an empty string when there is none.</returns>
    private static string ReadText(IntPtr window, Func<IntPtr, char[], int, int> read)
    {
        var buffer = new char[512];
        var length = read(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>
    ///     Describes every top-level window of a process and whether the main window accepts input,
    ///     for a failure message.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <param name="mainHandle">The main window's handle.</param>
    /// <returns>One line per window.</returns>
    private static string DescribeWindows(uint processId, IntPtr mainHandle)
    {
        var lines = new List<string> { $"Main window 0x{mainHandle:X}: enabled={IsWindowEnabled(mainHandle)}. Windows of the process:" };
        lines.AddRange(WindowsOf(processId).Select(window => $"  {window}"));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    ///     Writes a diagnostic line to the test's output.
    /// </summary>
    /// <param name="message">The line.</param>
    private static void Log(string message)
    {
        try
        {
            TestContext.Current.TestOutputHelper?.WriteLine(message);
        }
        catch (InvalidOperationException)
        {
            // Diagnostics are a convenience only; the xUnit context may be unavailable
        }
    }

    /// <summary>
    ///     One top-level window, as the operating system reports it.
    /// </summary>
    /// <param name="Handle">The window handle.</param>
    /// <param name="ClassName">The window class.</param>
    /// <param name="Title">The window title.</param>
    /// <param name="Owner">The owner window's handle, or zero for none.</param>
    /// <param name="IsVisible">Whether the window is shown.</param>
    /// <param name="IsEnabled">Whether the window accepts input.</param>
    private sealed record NativeWindow(
        IntPtr Handle, string ClassName, string Title, IntPtr Owner, bool IsVisible, bool IsEnabled)
    {
        /// <inheritdoc />
        public override string ToString() =>
            $"'{Title}' [class='{ClassName}'; hwnd=0x{Handle:X}; owner=0x{Owner:X}; visible={IsVisible}; enabled={IsEnabled}]";
    }
}
