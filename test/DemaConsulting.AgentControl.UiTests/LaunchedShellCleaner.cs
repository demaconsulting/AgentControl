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

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     A short, bounded safety net that ends the shell (and arg-logger stub) one test's AgentControl
///     "Launch" click spawned, when it is still alive after the test is over. Nothing outside the
///     one launch this instance is responsible for is ever touched.
/// </summary>
/// <remarks>
///     <para>
///     The UI tests run AgentControl with the "keep the shell open" setting off (see
///     <c>TestSettingsWriter</c>), so the launched shell runs the arg-logger stub and ends by itself,
///     and the terminal window it used closes with it. This class only matters on a failure path,
///     for example a test that failed before the shell finished or one that deliberately keeps the
///     shell open. It never waits for anything: <see cref="Cleanup"/> takes one process snapshot and
///     returns at once when nothing owned is alive.
///     </para>
///     <para>
///     A process is owned by this cleaner only when <em>all</em> of these hold: it descends from
///     this context's AgentControl process (parent-process-id chain, with a creation-time sanity
///     check against process-id reuse), <em>and</em> it is either a shell whose command line
///     contains this context's exact arg-logger stub path or the stub executable itself. Command
///     lines and parent ids are read through WMI (<c>Win32_Process</c>), because
///     <see cref="Process"/> exposes neither. Survivors are ended with exit code <c>0</c> through
///     <c>TerminateProcess</c> (managed <see cref="Process.Kill()"/> cannot choose the exit code),
///     which makes Windows Terminal close the tab as if the user had typed <c>exit</c>.
///     <c>conhost</c>, <c>OpenConsole</c> and <c>WindowsTerminal</c> processes are never ended,
///     because they may host the user's other tabs.
///     </para>
///     <para>
///     Every step is best-effort: failures are reported through the supplied log callback and never
///     propagate, so they cannot fail a test. Not thread-safe; one instance per test context.
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
    ///     Tolerance when comparing a child's creation time with its supposed ancestor's, to
    ///     absorb timestamp-resolution differences between WMI and <see cref="Process.StartTime"/>.
    /// </summary>
    private static readonly TimeSpan CreationTimeTolerance = TimeSpan.FromSeconds(2);

    private readonly string _stubExePath;
    private readonly Action<string> _log;

    /// <summary>
    ///     Initializes a new cleaner scoped to one arg-logger stub executable path.
    /// </summary>
    /// <param name="stubExePath">The exact absolute path of the arg-logger stub this test
    ///     context configured as the agent-tool command; used to recognize this context's shell
    ///     command lines. Must not be null or empty.</param>
    /// <param name="log">Callback receiving human-readable diagnostics about what was found or
    ///     terminated. Must not be null; must not throw.</param>
    public LaunchedShellCleaner(string stubExePath, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stubExePath);
        ArgumentNullException.ThrowIfNull(log);
        _stubExePath = stubExePath;
        _log = log;
    }

    /// <summary>
    ///     Tells whether any process this launch is responsible for is still alive.
    /// </summary>
    /// <param name="appProcessId">Process id of this context's AgentControl process (which may
    ///     already have exited - children keep the recorded parent id).</param>
    /// <param name="appStartTime">Local start time of that AgentControl process, used to reject
    ///     coincidental matches caused by process-id reuse.</param>
    /// <returns><see langword="true"/> when at least one owned process is running; also
    ///     <see langword="false"/> when the processes cannot be inspected (the failure is logged).</returns>
    public bool AnyOwnedAlive(int appProcessId, DateTime appStartTime)
    {
        try
        {
            return FindOwnedProcesses(appProcessId, appStartTime).Any(IsRunning);
        }
        catch (Exception ex) when (IsBestEffortFailure(ex))
        {
            _log($"Shell cleanup: could not inspect processes: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Terminates the shell (and any stub) this context's AgentControl process launched that is
    ///     still alive, without touching anything else.
    /// </summary>
    /// <param name="appProcessId">Process id of this context's AgentControl process (which may
    ///     already have exited - children keep the recorded parent id).</param>
    /// <param name="appStartTime">Local start time of that AgentControl process, used to reject
    ///     coincidental matches caused by process-id reuse.</param>
    /// <remarks>
    ///     Takes one process snapshot and returns immediately when nothing owned is alive (no fixed
    ///     wait). Never throws for environmental failures (WMI unavailable, access denied,
    ///     processes exiting concurrently); each is logged and skipped.
    /// </remarks>
    public void Cleanup(int appProcessId, DateTime appStartTime)
    {
        try
        {
            var survivors = FindOwnedProcesses(appProcessId, appStartTime).Where(IsRunning).ToList();
            if (survivors.Count == 0)
            {
                return;
            }

            _log($"Shell cleanup: still running: {string.Join(", ", survivors.Select(p => $"{p.Name}#{p.Id}"))}");

            // Parents first, so the shell ends before anything it spawned
            foreach (var process in survivors)
            {
                TerminateGracefully(process);
            }
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
    /// <returns><see langword="true"/> for WMI, COM, Win32, I/O and process-state failures.</returns>
    private static bool IsBestEffortFailure(Exception ex) =>
        ex is ManagementException or COMException or Win32Exception or InvalidOperationException
            or UnauthorizedAccessException or TimeoutException or IOException or ArgumentException
            or NotSupportedException;

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
