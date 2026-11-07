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
using System.Management;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Best-effort, precisely scoped cleanup of the shell window that one test's AgentControl
///     "Launch" click spawned (<c>pwsh -NoExit -Command "&lt;ArgLoggerStub&gt;"</c> or
///     <c>cmd /K</c>), including the Windows Terminal window the shell's console may be delegated
///     to. Nothing outside the one launch this instance is responsible for is ever touched.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why not "kill every new shell"?</b> The previous implementation killed every
///     <c>pwsh</c>/<c>powershell</c>/<c>cmd</c> that started after the test context was
///     constructed, which also killed unrelated shells the user (or another agent) started in the
///     meantime. A process is now owned by this cleaner only when <em>all</em> of these hold: it
///     descends from this context's AgentControl process (parent-process-id chain, with a
///     creation-time sanity check against process-id reuse), <em>and</em> it is either a shell
///     whose command line contains this context's exact ArgLoggerStub path or the ArgLoggerStub
///     executable itself. Command lines and parent ids are read through WMI
///     (<c>Win32_Process</c>), because <see cref="Process"/> exposes neither.
///     </para>
///     <para>
///     <b>Why the Windows Terminal tab leaked.</b> On Windows 11 with Windows Terminal as the
///     default terminal, the console of a shell started by a GUI process (AgentControl has no
///     console) is delegated to <c>WindowsTerminal.exe</c> - a separate process that is neither a
///     child of the shell nor of AgentControl - which opens a new window with one tab. Two
///     failure modes were observed on this machine with the old kill: (1) the test finished
///     within milliseconds of the click, so the shell was killed <em>mid-handoff</em> and the
///     window stayed open showing <c>[error 2147942632 (0x800700e8) when launching ...]</c>; and
///     (2) a shell killed with <see cref="Process.Kill()"/> exits with code -1, which Windows
///     Terminal's default <c>closeOnExit: "graceful"</c> treats as a failure and keeps the tab.
///     </para>
///     <para>
///     <b>Chosen approach (verified on this machine):</b>
///     (a) before terminating, wait until the handoff completed - a new Windows Terminal window
///     (one not present when the app was launched) shows a healthy terminal containing the stub
///     path, or the shell owns a classic console window, or the shell exited - so no
///     half-created tab is ever abandoned; (b) terminate the shell with exit code <c>0</c> via
///     <c>TerminateProcess</c> (managed <see cref="Process.Kill()"/> cannot choose the exit
///     code), which makes Windows Terminal auto-close the tab exactly as if the user had typed
///     <c>exit</c>, and which also closes a classic conhost window because its process died;
///     (c) as a safety net only, close a Windows Terminal window that still lingers, using UI
///     Automation to match it strictly (see <see cref="IsOwnedTerminalWindow"/>) and posting
///     <c>WM_CLOSE</c> to that single window. <c>WindowsTerminal.exe</c> and
///     <c>OpenConsole.exe</c> processes are never killed, because they host the user's other tabs.
///     </para>
///     <para>
///     Every step is best-effort: cleanup failures are reported through the supplied log callback
///     and never propagate, so they cannot fail a test. Not thread-safe; one instance per test
///     context.
///     </para>
/// </remarks>
internal sealed class LaunchedShellCleaner
{
    /// <summary>
    ///     Process names (without extension) of the shells <c>AgentToolLauncher</c> may start to
    ///     run the configured agent-tool command.
    /// </summary>
    private static readonly string[] ShellProcessNames = ["pwsh", "powershell", "cmd"];

    /// <summary>
    ///     Process names (without extension) of console hosts that are never terminated even when
    ///     they appear in a shell's process tree: they may serve other consoles.
    /// </summary>
    private static readonly string[] ProtectedProcessNames = ["conhost", "OpenConsole", "WindowsTerminal"];

    /// <summary>
    ///     Window class name of Windows Terminal top-level windows.
    /// </summary>
    private const string TerminalWindowClassName = "CASCADIA_HOSTING_WINDOW_CLASS";

    /// <summary>
    ///     Maximum time to wait for a launched shell's console hand-off to complete before
    ///     terminating it anyway.
    /// </summary>
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Maximum time spent looking for (and closing) a lingering Windows Terminal window after
    ///     the shell was terminated.
    /// </summary>
    private static readonly TimeSpan TerminalWindowSweepTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Interval between polls while waiting on the hand-off or sweeping terminal windows.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    ///     Tolerance when comparing a child's creation time with its supposed ancestor's, to
    ///     absorb timestamp-resolution differences between WMI and <see cref="Process.StartTime"/>.
    /// </summary>
    private static readonly TimeSpan CreationTimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Win32 <c>WM_CLOSE</c> window message.
    /// </summary>
    private const uint WmClose = 0x0010;

    /// <summary>
    ///     Terminal-window text can be long; only the start is needed to recognize the stub path.
    /// </summary>
    private const int MaxTerminalTextLength = 8192;

    private readonly string _stubExePath;
    private readonly Action<string> _log;
    private readonly HashSet<IntPtr> _baselineTerminalWindows = [];

    /// <summary>
    ///     Initializes a new cleaner scoped to one arg-logger stub executable path.
    /// </summary>
    /// <param name="stubExePath">The exact absolute path of the arg-logger stub this test
    ///     context configured as the agent-tool command; used to recognize this context's shell
    ///     command lines. Must not be null or empty.</param>
    /// <param name="log">Callback receiving human-readable diagnostics about what was found,
    ///     terminated or closed. Must not be null; must not throw.</param>
    public LaunchedShellCleaner(string stubExePath, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stubExePath);
        ArgumentNullException.ThrowIfNull(log);
        _stubExePath = stubExePath;
        _log = log;
    }

    /// <summary>
    ///     Records the Windows Terminal windows that exist before AgentControl is launched, so
    ///     that only windows created afterwards can ever be considered ours.
    /// </summary>
    /// <param name="automation">The UI Automation instance used to enumerate top-level
    ///     windows.</param>
    /// <remarks>
    ///     Best-effort: when enumeration fails the baseline stays empty, which only makes the
    ///     later (stricter, text-matched) leftover sweep consider more windows - it never makes
    ///     the cleaner kill a process it does not own.
    /// </remarks>
    public void CaptureBaseline(UIA3Automation automation)
    {
        try
        {
            _baselineTerminalWindows.Clear();
            foreach (var window in FindTerminalWindows(automation))
            {
                _baselineTerminalWindows.Add(GetHandle(window));
            }
        }
        catch (Exception ex) when (IsBestEffortFailure(ex))
        {
            _log($"Shell cleanup: could not capture Windows Terminal baseline: {ex.Message}");
        }
    }

    /// <summary>
    ///     Terminates the shell (and any stub) this context's AgentControl process launched, and
    ///     closes the Windows Terminal window left behind, without touching anything else.
    /// </summary>
    /// <param name="appProcessId">Process id of this context's AgentControl process (which may
    ///     already have exited - children keep the recorded parent id).</param>
    /// <param name="appStartTime">Local start time of that AgentControl process, used to reject
    ///     coincidental matches caused by process-id reuse.</param>
    /// <remarks>
    ///     Never throws for environmental failures (WMI unavailable, access denied, UI Automation
    ///     errors, processes exiting concurrently); each is logged and skipped. See the type
    ///     remarks for the full rationale and ordering.
    /// </remarks>
    public void Cleanup(int appProcessId, DateTime appStartTime)
    {
        try
        {
            // Step 1: identify exactly the processes this launch is responsible for.
            var owned = FindOwnedProcesses(appProcessId, appStartTime);
            if (owned.Count == 0)
            {
                return;
            }

            _log($"Shell cleanup: owned processes: {string.Join(", ", owned.Select(p => $"{p.Name}#{p.Id}"))}");

            // Step 2: let the terminal hand-off finish so no half-created tab is abandoned.
            WaitForHandoff(owned);

            // Step 3: terminate with exit code 0 so Windows Terminal closes the tab by itself.
            foreach (var process in owned)
            {
                TerminateGracefully(process);
            }

            // Step 4: safety net for a window that did not auto-close.
            CloseLingeringTerminalWindows(owned);
        }
        catch (Exception ex) when (IsBestEffortFailure(ex))
        {
            _log($"Shell cleanup: aborted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Determines whether an exception is an expected environmental failure that best-effort
    ///     cleanup must swallow (rather than a programming error that should surface).
    /// </summary>
    /// <param name="ex">The exception to classify.</param>
    /// <returns><see langword="true"/> for WMI, COM/UI Automation, Win32, I/O and
    ///     process-state failures.</returns>
    private static bool IsBestEffortFailure(Exception ex) =>
        ex is ManagementException or COMException or Win32Exception or InvalidOperationException
            or UnauthorizedAccessException or TimeoutException or IOException or ArgumentException
            or NotSupportedException or FlaUI.Core.Exceptions.FlaUIException;

    /// <summary>
    ///     Reads every running process (id, parent id, name, command line, image path, creation
    ///     time) through WMI in one query.
    /// </summary>
    /// <returns>The snapshot keyed by process id.</returns>
    /// <exception cref="ManagementException">Thrown when WMI cannot be queried.</exception>
    private static Dictionary<int, ProcessEntry> SnapshotProcesses()
    {
        var result = new Dictionary<int, ProcessEntry>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath, CreationDate FROM Win32_Process");
        using var rows = searcher.Get();
        foreach (var row in rows)
        {
            using (row)
            {
                var id = Convert.ToInt32(row["ProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                var parentId = Convert.ToInt32(row["ParentProcessId"], System.Globalization.CultureInfo.InvariantCulture);
                var name = Path.GetFileNameWithoutExtension(row["Name"] as string ?? string.Empty);
                var created = row["CreationDate"] is string text && text.Length > 0
                    ? ManagementDateTimeConverter.ToDateTime(text)
                    : DateTime.MinValue;
                result[id] = new ProcessEntry(
                    id, parentId, name, row["CommandLine"] as string, row["ExecutablePath"] as string, created);
            }
        }

        return result;
    }

    /// <summary>
    ///     Finds the processes owned by this launch: matching roots plus their descendants.
    /// </summary>
    /// <param name="appProcessId">Process id of this context's AgentControl process.</param>
    /// <param name="appStartTime">Local start time of that process.</param>
    /// <returns>Owned processes, parents before children (so the shell is terminated before
    ///     anything it spawned).</returns>
    private List<ProcessEntry> FindOwnedProcesses(int appProcessId, DateTime appStartTime)
    {
        var snapshot = SnapshotProcesses();
        var stubName = Path.GetFileNameWithoutExtension(_stubExePath);

        // Roots: descend from our AgentControl AND (a shell running our stub OR the stub itself).
        var roots = snapshot.Values
            .Where(p => IsRootCandidate(p, stubName) && DescendsFrom(p, appProcessId, appStartTime, snapshot))
            .ToList();

        // Expand to descendants (breadth-first) so the shell's own children go with it.
        var owned = new List<ProcessEntry>(roots);
        var seen = new HashSet<int>(roots.Select(p => p.Id));
        for (var index = 0; index < owned.Count; index++)
        {
            var parent = owned[index];
            foreach (var child in snapshot.Values.Where(
                         c => c.ParentId == parent.Id && c.Created >= parent.Created - CreationTimeTolerance))
            {
                if (seen.Add(child.Id) &&
                    !ProtectedProcessNames.Contains(child.Name, StringComparer.OrdinalIgnoreCase))
                {
                    owned.Add(child);
                }
            }
        }

        return owned;
    }

    /// <summary>
    ///     Tests whether a process is, by name and command line, one of this context's launches:
    ///     a shell whose command line contains the exact stub path, or the stub executable itself.
    /// </summary>
    /// <param name="process">The candidate process.</param>
    /// <param name="stubName">The stub executable's name without extension.</param>
    /// <returns><see langword="true"/> when the process matches.</returns>
    private bool IsRootCandidate(ProcessEntry process, string stubName)
    {
        if (ShellProcessNames.Contains(process.Name, StringComparer.OrdinalIgnoreCase))
        {
            return process.CommandLine?.Contains(_stubExePath, StringComparison.OrdinalIgnoreCase) == true;
        }

        return string.Equals(process.Name, stubName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(process.ExecutablePath, _stubExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Walks a process's parent chain looking for the AgentControl process, rejecting links
    ///     that are impossible because the supposed parent had not been created yet (pid reuse).
    /// </summary>
    /// <param name="process">The process whose ancestry to check.</param>
    /// <param name="ancestorId">The wanted ancestor's process id.</param>
    /// <param name="ancestorStart">The wanted ancestor's start time.</param>
    /// <param name="snapshot">The process snapshot to walk.</param>
    /// <returns><see langword="true"/> when <paramref name="ancestorId"/> is an ancestor.</returns>
    private static bool DescendsFrom(
        ProcessEntry process,
        int ancestorId,
        DateTime ancestorStart,
        Dictionary<int, ProcessEntry> snapshot)
    {
        var current = process;
        for (var depth = 0; depth < 32; depth++)
        {
            if (current.Created < ancestorStart - CreationTimeTolerance)
            {
                return false;
            }

            if (current.ParentId == ancestorId)
            {
                return true;
            }

            if (!snapshot.TryGetValue(current.ParentId, out var parent) ||
                parent.Created > current.Created + CreationTimeTolerance)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>
    ///     Waits (bounded) until the owned shells' console hand-off to its terminal has completed.
    /// </summary>
    /// <param name="owned">The owned processes.</param>
    /// <remarks>
    ///     Hand-off is complete when any of these holds: a Windows Terminal window created after
    ///     the baseline shows a non-error terminal containing the stub path; a shell owns a
    ///     visible classic console window; or every shell has already exited. The wait is bounded
    ///     so an unknown terminal configuration only costs time, never a hang.
    /// </remarks>
    private void WaitForHandoff(List<ProcessEntry> owned)
    {
        var shells = owned.Where(p => ShellProcessNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (shells.Count == 0)
        {
            return;
        }

        using var automation = new UIA3Automation();
        var deadline = DateTime.UtcNow + HandoffTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (shells.All(s => !IsRunning(s)) ||
                shells.Any(HasClassicConsoleWindow) ||
                FindOwnedTerminalWindows(automation, shells, requireHealthy: true).Count > 0)
            {
                return;
            }

            Thread.Sleep(PollInterval);
        }

        _log("Shell cleanup: console hand-off not observed within the timeout; terminating anyway.");
    }

    /// <summary>
    ///     Tests whether a process from the snapshot is still running (same id and creation time).
    /// </summary>
    /// <param name="entry">The snapshot entry.</param>
    /// <returns><see langword="true"/> when the very same process is still alive.</returns>
    private static bool IsRunning(ProcessEntry entry)
    {
        try
        {
            using var process = Process.GetProcessById(entry.Id);
            return !process.HasExited && SameProcess(process, entry);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     Tests whether a running process owns a visible top-level window (a classic conhost
    ///     console), which is the hand-off-complete signal when no terminal delegation occurs.
    /// </summary>
    /// <param name="entry">The snapshot entry of the shell.</param>
    /// <returns><see langword="true"/> when the process currently has a main window.</returns>
    private static bool HasClassicConsoleWindow(ProcessEntry entry)
    {
        try
        {
            using var process = Process.GetProcessById(entry.Id);
            return !process.HasExited && SameProcess(process, entry) && process.MainWindowHandle != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     Verifies a live <see cref="Process"/> is the one the snapshot described, guarding
    ///     against process-id reuse between snapshot and action.
    /// </summary>
    /// <param name="process">The live process.</param>
    /// <param name="entry">The snapshot entry.</param>
    /// <returns><see langword="true"/> when start times agree within the tolerance.</returns>
    private static bool SameProcess(Process process, ProcessEntry entry) =>
        (process.StartTime - entry.Created).Duration() <= CreationTimeTolerance;

    /// <summary>
    ///     Terminates one owned process with exit code 0 (see the type remarks for why 0).
    /// </summary>
    /// <param name="entry">The owned process.</param>
    private void TerminateGracefully(ProcessEntry entry)
    {
        try
        {
            using var process = Process.GetProcessById(entry.Id);
            if (process.HasExited || !SameProcess(process, entry))
            {
                return;
            }

            if (!TerminateProcess(process.SafeHandle, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _log($"Shell cleanup: terminated {entry.Name}#{entry.Id} with exit code 0.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already exited, or not ours to terminate (access denied): best-effort only.
            _log($"Shell cleanup: could not terminate {entry.Name}#{entry.Id}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Gives Windows Terminal a moment to auto-close the tab of the terminated shell and, if a
    ///     matching window still lingers (for example an error tab), closes just that window.
    /// </summary>
    /// <param name="owned">The owned processes that were terminated.</param>
    private void CloseLingeringTerminalWindows(List<ProcessEntry> owned)
    {
        using var automation = new UIA3Automation();
        var deadline = DateTime.UtcNow + TerminalWindowSweepTimeout;
        var quietPolls = 0;
        while (DateTime.UtcNow < deadline && quietPolls < 5)
        {
            var windows = FindOwnedTerminalWindows(automation, owned, requireHealthy: false);
            if (windows.Count == 0)
            {
                quietPolls++;
            }
            else
            {
                quietPolls = 0;
                foreach (var window in windows)
                {
                    var handle = GetHandle(window);
                    _log($"Shell cleanup: closing lingering Windows Terminal window 0x{handle.ToInt64():X} ('{window.Name}').");
                    PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                }
            }

            Thread.Sleep(PollInterval * 2);
        }
    }

    /// <summary>
    ///     Enumerates Windows Terminal top-level windows.
    /// </summary>
    /// <param name="automation">The UI Automation instance.</param>
    /// <returns>All current Windows Terminal windows.</returns>
    private static AutomationElement[] FindTerminalWindows(UIA3Automation automation) =>
        automation.GetDesktop().FindAllChildren(cf => cf.ByClassName(TerminalWindowClassName));

    /// <summary>
    ///     Reads a window element's native window handle.
    /// </summary>
    /// <param name="window">The window element.</param>
    /// <returns>The handle, or <see cref="IntPtr.Zero"/> when unavailable.</returns>
    private static IntPtr GetHandle(AutomationElement window) =>
        window.Properties.NativeWindowHandle.ValueOrDefault;

    /// <summary>
    ///     Finds Windows Terminal windows that provably belong to this launch.
    /// </summary>
    /// <param name="automation">The UI Automation instance.</param>
    /// <param name="owned">The owned processes (used to recognize the tab title).</param>
    /// <param name="requireHealthy">When <see langword="true"/>, only windows showing a working
    ///     (non-error) terminal qualify; when <see langword="false"/>, error windows qualify too.</param>
    /// <returns>The matching windows.</returns>
    private List<AutomationElement> FindOwnedTerminalWindows(
        UIA3Automation automation,
        List<ProcessEntry> owned,
        bool requireHealthy)
    {
        var matches = new List<AutomationElement>();
        foreach (var window in FindTerminalWindows(automation))
        {
            try
            {
                if (IsOwnedTerminalWindow(window, owned, requireHealthy))
                {
                    matches.Add(window);
                }
            }
            catch (Exception ex) when (IsBestEffortFailure(ex))
            {
                // A window can vanish (or refuse UI Automation) while being inspected: skip it.
            }
        }

        return matches;
    }

    /// <summary>
    ///     Decides whether a Windows Terminal window is the one created for this launch. All of
    ///     the following must hold, so a user's own windows and tabs are never matched:
    ///     the window did not exist before the app was launched; it has exactly one tab (so
    ///     closing the window cannot discard any other tab); the tab title is the shell's
    ///     executable path or contains the stub path; and the terminal text contains the exact
    ///     stub path (either the shell's output or the hand-off error that quotes the command
    ///     line).
    /// </summary>
    /// <param name="window">The candidate window.</param>
    /// <param name="owned">The owned processes whose executable paths are acceptable titles.</param>
    /// <param name="requireHealthy">Whether to additionally require that the terminal text is not
    ///     a hand-off error.</param>
    /// <returns><see langword="true"/> when the window is provably this launch's.</returns>
    private bool IsOwnedTerminalWindow(AutomationElement window, List<ProcessEntry> owned, bool requireHealthy)
    {
        if (_baselineTerminalWindows.Contains(GetHandle(window)))
        {
            return false;
        }

        var tabs = window.FindAllDescendants(cf => cf.ByControlType(ControlType.TabItem));
        if (tabs.Length != 1)
        {
            return false;
        }

        var title = tabs[0].Name ?? string.Empty;
        var titleMatches = title.Contains(_stubExePath, StringComparison.OrdinalIgnoreCase) ||
                           owned.Any(p => string.Equals(p.ExecutablePath, title, StringComparison.OrdinalIgnoreCase));
        if (!titleMatches)
        {
            return false;
        }

        var terminal = window.FindFirstDescendant(cf => cf.ByClassName("TermControl"));
        var text = terminal?.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(MaxTerminalTextLength) ?? string.Empty;
        if (!text.Contains(_stubExePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !requireHealthy || !text.TrimStart().StartsWith("[error", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Win32 <c>TerminateProcess</c>: ends a process with a caller-chosen exit code.
    /// </summary>
    /// <param name="process">Handle with <c>PROCESS_TERMINATE</c> access.</param>
    /// <param name="exitCode">The exit code the process reports.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint exitCode);

    /// <summary>
    ///     Win32 <c>PostMessageW</c>: asynchronously posts a window message.
    /// </summary>
    /// <param name="window">Target window handle.</param>
    /// <param name="message">The message id.</param>
    /// <param name="wParam">First message parameter.</param>
    /// <param name="lParam">Second message parameter.</param>
    /// <returns><see langword="true"/> when the message was posted.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>
    ///     One row of the WMI process snapshot.
    /// </summary>
    /// <param name="Id">The process id.</param>
    /// <param name="ParentId">The recorded parent process id (kept by Windows after the parent
    ///     exits).</param>
    /// <param name="Name">The process name without extension.</param>
    /// <param name="CommandLine">The full command line, or <see langword="null"/> when not
    ///     readable.</param>
    /// <param name="ExecutablePath">The image path, or <see langword="null"/> when not
    ///     readable.</param>
    /// <param name="Created">The local creation time.</param>
    private sealed record ProcessEntry(
        int Id,
        int ParentId,
        string Name,
        string? CommandLine,
        string? ExecutablePath,
        DateTime Created);
}
