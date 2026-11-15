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

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Shuts a launched AgentControl process down at the end of a UI test: closes every window that
///     is still open on top of the main window, closes the main window, waits a short bounded time for
///     the process to exit, and only then kills the process tree, logging what it found.
/// </summary>
/// <remarks>
///     <para>
///     FlaUI's own <c>Application.Close()</c> (FlaUI.Core 5.0.0) sends <c>CloseMainWindow</c> to the
///     process and then waits <c>CloseTimeout</c> (5 seconds) for it to exit, logging
///     "Application failed to exit" and killing the process (not its children) when it does not. A
///     main window that is disabled by an open modal dialog, or a user interface thread that is
///     blocked, therefore costs the full 5 seconds per test with no hint of why. This class closes the
///     dialogs first, waits only as long as needed (returning the moment the process has exited), and
///     reports the open windows, whether the main window was disabled, whether the process still
///     responded and the tail of the application's own log whenever it has to kill the process.
///     </para>
///     <para>
///     AgentControl shows its dialogs as owned windows that UI Automation lists inside the main
///     window's tree rather than as top-level windows, so both the top-level windows of the process
///     and every window nested below them are examined. No keyboard or mouse input is ever sent: the
///     windows are closed through UI Automation patterns only.
///     </para>
/// </remarks>
internal static class ApplicationTeardown
{
    /// <summary>
    ///     How long the process gets to exit after its main window was asked to close.
    /// </summary>
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How long a window gets to disappear after it was asked to close.
    /// </summary>
    private static readonly TimeSpan WindowCloseTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    ///     A teardown that takes at least this long is reported even though it succeeded.
    /// </summary>
    private static readonly TimeSpan SlowTeardownThreshold = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How long to wait for the process to be gone after it was killed.
    /// </summary>
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often to poll while waiting for a window to disappear.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     The most windows closed one by one before the main window is closed regardless.
    /// </summary>
    private const int MaxWindowsToClose = 10;

    /// <summary>
    ///     How many lines of the application's log are reported when the process had to be killed.
    /// </summary>
    private const int LogTailLines = 25;

    /// <summary>
    ///     The <c>GetWindow</c> relationship that returns a window's owner.
    /// </summary>
    private const uint GwOwner = 4;

    /// <summary>
    ///     The AutomationIds of the buttons that dismiss a dialog, tried when closing the dialog
    ///     window itself did not work (the cancel or close button of each kind of dialog).
    /// </summary>
    private static readonly string[] DismissButtonIds =
    [
        "CredentialPromptCancelButton",
        "GroupNameCancelButton",
        "SelectPackageCancelButton",
        "SourceEditorCancelButton",
        "AboutCloseButton",
        "ConfirmationNoButton",
        "MessageBoxOkButton"
    ];

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
    ///     Closes the application as described on <see cref="ApplicationTeardown"/>.
    /// </summary>
    /// <param name="app">The launched application.</param>
    /// <param name="automation">The automation used to find its windows, or <see langword="null"/>
    ///     when there is none (the windows are then not examined).</param>
    /// <param name="startTime">The start time of the launched process, which guards against a reused
    ///     process id.</param>
    /// <param name="configDirectory">The application's <c>--config-dir</c>, whose <c>logs</c> folder is
    ///     quoted when the process has to be killed.</param>
    /// <param name="log">Receives the diagnostic lines.</param>
    public static void Close(
        Application app, AutomationBase? automation, DateTime startTime, string configDirectory, Action<string> log)
    {
        using var process = OpenRunningProcess(app.ProcessId, startTime);
        if (process is null)
        {
            return;
        }

        var processId = process.Id;
        var mainHandle = MainWindowHandleOf(process);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            CloseGracefully(app, automation, process, mainHandle, processId, log);
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            log($"Teardown: closing AgentControl (PID {processId}) through UI Automation failed: {ex.GetType().Name}: {ex.Message}");
        }

        if (HasExited(process))
        {
            // A slow but successful close is worth knowing about; a quick one is not
            if (stopwatch.Elapsed >= SlowTeardownThreshold)
            {
                log($"Teardown: AgentControl (PID {processId}) exited {stopwatch.ElapsedMilliseconds} ms after teardown started.");
            }

            return;
        }

        log($"Teardown: AgentControl (PID {processId}) is still running {GracefulExitTimeout.TotalSeconds:0.#}s after its " +
            "main window was asked to close; killing its process tree.");
        ReportWhyStuck(app, automation, process, mainHandle, log);
        KillProcessTree(process, log);
        ReportLogTail(configDirectory, log);
    }

    /// <summary>
    ///     Closes the windows that are open on top of the main window, then the main window, and waits
    ///     for the process to exit.
    /// </summary>
    /// <param name="app">The launched application.</param>
    /// <param name="automation">The automation used to find its windows, or <see langword="null"/>.</param>
    /// <param name="process">The running process.</param>
    /// <param name="mainHandle">The handle of the main window, or zero.</param>
    /// <param name="processId">The process id, for the log.</param>
    /// <param name="log">Receives the diagnostic lines.</param>
    private static void CloseGracefully(
        Application app, AutomationBase? automation, Process process, IntPtr mainHandle, int processId, Action<string> log)
    {
        if (automation is not null)
        {
            var extras = Snapshot(app, automation, mainHandle).Where(window => !window.IsMain).ToList();
            if (extras.Count > 0)
            {
                log($"Teardown: {extras.Count} window(s) other than the main window are open in AgentControl " +
                    $"(PID {processId}); closing them first, most recently opened first:");
                foreach (var window in extras)
                {
                    log($"Teardown:   {window}");
                }

                CloseExtraWindows(app, automation, process, mainHandle, log);
            }
        }

        if (HasExited(process))
        {
            return;
        }

        // The same request FlaUI's Application.Close() makes, but waiting only until the process is
        // gone rather than for a fixed time
        process.Refresh();
        process.CloseMainWindow();
        process.WaitForExit(GracefulExitTimeout);
    }

    /// <summary>
    ///     Closes every window that is open besides the main window, one at a time and the most
    ///     recently opened first, until none is left or no further one can be closed.
    /// </summary>
    /// <param name="app">The launched application.</param>
    /// <param name="automation">The automation used to find its windows.</param>
    /// <param name="process">The running process.</param>
    /// <param name="mainHandle">The handle of the main window, or zero.</param>
    /// <param name="log">Receives the diagnostic lines.</param>
    private static void CloseExtraWindows(
        Application app, AutomationBase automation, Process process, IntPtr mainHandle, Action<string> log)
    {
        var notClosable = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < MaxWindowsToClose && !HasExited(process); round++)
        {
            // A nested dialog is on top of everything its owner shows, and UI Automation lists
            // top-level windows topmost first, so the last nested window or else the first top-level
            // one is the most recently opened
            var candidates = Snapshot(app, automation, mainHandle)
                .Where(window => !window.IsMain && !notClosable.Contains(window.Key))
                .ToList();
            var target = candidates.LastOrDefault(window => window.IsNested) ?? candidates.FirstOrDefault();
            if (target is null)
            {
                return;
            }

            if (!TryCloseWindow(app, automation, process, mainHandle, target, log))
            {
                log($"Teardown: could not close {target}; leaving it for the main window's own close.");
                notClosable.Add(target.Key);
            }
        }
    }

    /// <summary>
    ///     Closes one window through its window pattern, falling back to invoking its dismiss button,
    ///     and waits for it to disappear.
    /// </summary>
    /// <param name="app">The launched application.</param>
    /// <param name="automation">The automation used to find its windows.</param>
    /// <param name="process">The running process.</param>
    /// <param name="mainHandle">The handle of the main window, or zero.</param>
    /// <param name="target">The window to close.</param>
    /// <param name="log">Receives the diagnostic lines.</param>
    /// <returns><see langword="true"/> when the window is gone.</returns>
    private static bool TryCloseWindow(
        Application app, AutomationBase automation, Process process, IntPtr mainHandle, OpenWindow target, Action<string> log)
    {
        log($"Teardown: closing {target}");
        if (TryAct(target.Element.Close, log) && WaitUntilGone(app, automation, process, mainHandle, target))
        {
            return true;
        }

        log($"Teardown: the Close pattern did not close {target}; trying its dismiss button");
        return TryAct(() => InvokeDismissButton(target.Element), log)
               && WaitUntilGone(app, automation, process, mainHandle, target);
    }

    /// <summary>
    ///     Invokes the first dismiss button (see <see cref="DismissButtonIds"/>) that the window contains.
    /// </summary>
    /// <param name="window">The window.</param>
    private static void InvokeDismissButton(Window window)
    {
        foreach (var id in DismissButtonIds)
        {
            var button = window.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (button is not null)
            {
                button.AsButton().Invoke();
                return;
            }
        }
    }

    /// <summary>
    ///     Runs a UI Automation action, reporting and swallowing the failures that a window going away
    ///     under it can cause.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <param name="log">Receives the diagnostic line for a failure.</param>
    /// <returns><see langword="true"/> when the action ran without failing.</returns>
    private static bool TryAct(Action action, Action<string> log)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            log($"Teardown:   {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Waits until a window is no longer open, or the process has exited.
    /// </summary>
    /// <param name="app">The launched application.</param>
    /// <param name="automation">The automation used to find its windows.</param>
    /// <param name="process">The running process.</param>
    /// <param name="mainHandle">The handle of the main window, or zero.</param>
    /// <param name="target">The window.</param>
    /// <returns><see langword="true"/> when the window is gone.</returns>
    private static bool WaitUntilGone(
        Application app, AutomationBase automation, Process process, IntPtr mainHandle, OpenWindow target) =>
        Retry.WhileTrue(
            () => !HasExited(process) && Snapshot(app, automation, mainHandle).Any(window => window.Key == target.Key),
            WindowCloseTimeout,
            PollInterval).Success;

    /// <summary>
    ///     Logs everything that explains why the process did not exit: the windows that are open, whether
    ///     the main window accepts input, and whether the process's message loop still responds.
    /// </summary>
    /// <param name="app">The launched application.</param>
    /// <param name="automation">The automation used to find its windows, or <see langword="null"/>.</param>
    /// <param name="process">The running process.</param>
    /// <param name="mainHandle">The handle of the main window, or zero.</param>
    /// <param name="log">Receives the diagnostic lines.</param>
    private static void ReportWhyStuck(
        Application app, AutomationBase? automation, Process process, IntPtr mainHandle, Action<string> log)
    {
        try
        {
            process.Refresh();
            log($"Teardown:   process responding to the window message loop: {process.Responding}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            log($"Teardown:   could not read whether the process responds: {ex.Message}");
        }

        if (mainHandle != IntPtr.Zero)
        {
            log($"Teardown:   main window 0x{mainHandle:X} accepts input: {IsWindowEnabled(mainHandle)} " +
                "(false means a modal dialog is open over it)");
        }
        else
        {
            log("Teardown:   the process has no main window handle.");
        }

        if (automation is null)
        {
            return;
        }

        try
        {
            var windows = Snapshot(app, automation, mainHandle);
            log($"Teardown:   {windows.Count} window(s) still open:");
            foreach (var window in windows)
            {
                log($"Teardown:     {window}");
            }
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            log($"Teardown:   could not list the open windows: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Kills the process and everything it started, and waits for it to be gone.
    /// </summary>
    /// <param name="process">The running process.</param>
    /// <param name="log">Receives the diagnostic line for a failure.</param>
    private static void KillProcessTree(Process process, Action<string> log)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(KillTimeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception
                                       or AggregateException)
        {
            // The process ended on its own between the check and the kill, or cannot be killed
            log($"Teardown:   killing the process tree failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Logs the last lines of the application's own log file, which shows what it was doing when it
    ///     did not exit.
    /// </summary>
    /// <param name="configDirectory">The application's <c>--config-dir</c>.</param>
    /// <param name="log">Receives the diagnostic lines.</param>
    private static void ReportLogTail(string configDirectory, Action<string> log)
    {
        try
        {
            var logFile = new DirectoryInfo(Path.Combine(configDirectory, "logs"))
                .GetFiles("agentcontrol-*.log")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (logFile is null)
            {
                log("Teardown:   the application wrote no log file.");
                return;
            }

            // The application may still hold the file open for writing for a moment
            using var stream = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > LogTailLines)
                {
                    lines.Dequeue();
                }
            }

            log($"Teardown:   last {lines.Count} line(s) of {logFile.Name}:");
            foreach (var line in lines)
            {
                log($"Teardown:     | {line}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Teardown:   could not read the application's log: {ex.Message}");
        }
    }

    /// <summary>
    ///     Lists every window of the application: its top-level windows and, below each, the windows
    ///     UI Automation nests inside it (AgentControl's dialogs).
    /// </summary>
    /// <param name="app">The launched application.</param>
    /// <param name="automation">The automation used to find the windows.</param>
    /// <param name="mainHandle">The handle of the main window, or zero.</param>
    /// <returns>The windows, each top-level window followed by the windows nested in it; a window that
    ///     disappears while the list is being built is left out.</returns>
    private static List<OpenWindow> Snapshot(Application app, AutomationBase automation, IntPtr mainHandle)
    {
        var windows = new List<OpenWindow>();
        try
        {
            foreach (var topLevel in app.GetAllTopLevelWindows(automation))
            {
                var handle = HandleOf(topLevel);
                windows.Add(Describe(topLevel, handle, handle != IntPtr.Zero && handle == mainHandle, isNested: false));
                foreach (var nested in topLevel.FindAllDescendants(cf => cf.ByControlType(ControlType.Window)))
                {
                    var window = nested.AsWindow();
                    windows.Add(Describe(window, HandleOf(window), isMain: false, isNested: true));
                }
            }
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            // A window closing while it is being read: what was read so far is still a fair snapshot
        }

        return windows;
    }

    /// <summary>
    ///     Reads what the log reports about one window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="handle">The window's handle, or zero.</param>
    /// <param name="isMain">Whether it is the application's main window.</param>
    /// <param name="isNested">Whether UI Automation lists it below another window.</param>
    /// <returns>The description.</returns>
    private static OpenWindow Describe(Window window, IntPtr handle, bool isMain, bool isNested)
    {
        var title = Read(() => window.Properties.Name.ValueOrDefault) ?? string.Empty;
        var automationId = Read(() => window.Properties.AutomationId.ValueOrDefault) ?? string.Empty;
        var className = Read(() => window.Properties.ClassName.ValueOrDefault) ?? string.Empty;
        var isModal = Read(() => window.Patterns.Window.PatternOrDefault?.IsModal.ValueOrDefault);
        var owner = handle == IntPtr.Zero ? IntPtr.Zero : GetWindow(handle, GwOwner);
        var isEnabled = handle == IntPtr.Zero ? (bool?)null : IsWindowEnabled(handle);
        var key = handle != IntPtr.Zero ? $"0x{handle:X}" : $"{title}|{className}|{automationId}|{isNested}";
        return new OpenWindow(window, key, title, automationId, className, handle, owner, isModal, isEnabled, isMain, isNested);
    }

    /// <summary>
    ///     Reads one property, answering <see langword="null"/> when the window cannot be read any more.
    /// </summary>
    /// <typeparam name="T">The property's type.</typeparam>
    /// <param name="read">The read.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static T? Read<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsAutomationFailure(ex))
        {
            return default;
        }
    }

    /// <summary>
    ///     Gets a window's native handle, or zero when it cannot be read.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The handle.</returns>
    private static IntPtr HandleOf(AutomationElement window) =>
        Read(() => window.Properties.NativeWindowHandle.ValueOrDefault);

    /// <summary>
    ///     Opens the launched process, checking it is still the same one.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <param name="startTime">The start time of the launched process.</param>
    /// <returns>The process, or <see langword="null"/> when it has exited (or the id now belongs to
    ///     another process).</returns>
    private static Process? OpenRunningProcess(int processId, DateTime startTime)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            if (!process.HasExited && process.StartTime == startTime)
            {
                return process;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception
                                       or NotSupportedException)
        {
            // Gone already
        }

        process?.Dispose();
        return null;
    }

    /// <summary>
    ///     Gets the handle of the process's main window.
    /// </summary>
    /// <param name="process">The process.</param>
    /// <returns>The handle, or zero when there is none or it cannot be read.</returns>
    private static IntPtr MainWindowHandleOf(Process process)
    {
        try
        {
            return process.MainWindowHandle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    ///     Tells whether the process has exited, treating a process that cannot be queried as gone.
    /// </summary>
    /// <param name="process">The process.</param>
    /// <returns><see langword="true"/> when the process has exited.</returns>
    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return true;
        }
    }

    /// <summary>
    ///     Tells whether an exception is what UI Automation or the operating system raise when a window
    ///     is closing or cannot be driven.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns><see langword="true"/> when it is such a failure.</returns>
    private static bool IsAutomationFailure(Exception ex) =>
        ex is COMException or FlaUI.Core.Exceptions.FlaUIException or InvalidOperationException or NotSupportedException
            or TimeoutException or Win32Exception or UnauthorizedAccessException;

    /// <summary>
    ///     One window of the application, as found at a moment.
    /// </summary>
    /// <param name="Element">The window element.</param>
    /// <param name="Key">An identity that is stable while the window is open.</param>
    /// <param name="Title">The window's title.</param>
    /// <param name="AutomationId">The window's AutomationId.</param>
    /// <param name="ClassName">The window's class name.</param>
    /// <param name="Handle">The native window handle, or zero.</param>
    /// <param name="Owner">The owner window's handle, or zero for none.</param>
    /// <param name="IsModal">Whether the window pattern reports it as modal, or <see langword="null"/>
    ///     when unknown.</param>
    /// <param name="IsEnabled">Whether the operating system accepts input for it, or
    ///     <see langword="null"/> when unknown.</param>
    /// <param name="IsMain">Whether it is the application's main window.</param>
    /// <param name="IsNested">Whether UI Automation lists it below another window.</param>
    private sealed record OpenWindow(
        Window Element,
        string Key,
        string Title,
        string AutomationId,
        string ClassName,
        IntPtr Handle,
        IntPtr Owner,
        bool? IsModal,
        bool? IsEnabled,
        bool IsMain,
        bool IsNested)
    {
        /// <inheritdoc />
        public override string ToString()
        {
            string kind;
            if (IsMain)
            {
                kind = "main window";
            }
            else
            {
                kind = IsNested ? "window nested in a top-level window" : "top-level window";
            }

            return $"'{Title}' [{kind}; AutomationId='{AutomationId}'; class='{ClassName}'; modal={IsModal?.ToString() ?? "?"}; " +
                   $"enabled={IsEnabled?.ToString() ?? "?"}; owner=0x{Owner:X}; hwnd=0x{Handle:X}]";
        }
    }
}
