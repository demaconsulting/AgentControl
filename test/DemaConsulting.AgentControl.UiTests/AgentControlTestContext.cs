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
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Owns one hermetic, isolated launch of <c>AgentControl.exe</c> under FlaUI for a single
///     test: an isolated temp config directory, an isolated temp repo directory, a pre-seeded
///     <c>settings.json</c> pointing git/agent-tool commands at the arg-logger stub, and the
///     FlaUI <see cref="Application"/>/<see cref="UIA3Automation"/> instances needed to drive it.
/// </summary>
/// <remarks>
///     Implements <see cref="IDisposable"/> so every test can use a <c>using</c> block (or a
///     <c>try</c>/<c>finally</c>) to guarantee the spawned AgentControl process, any shell process
///     it in turn launches for the agent-tool command (see <c>AgentToolLauncher</c>'s
///     <c>-NoExit</c>/<c>/K</c> "leave the window open" behavior, which this context must
///     specifically hunt down and kill since the shell is not a direct child FlaUI tracks), and
///     every temp directory are cleaned up even if the test fails an assertion or throws.
/// </remarks>
internal sealed class AgentControlTestContext : IDisposable
{
    /// <summary>
    ///     Candidate process names for the shell <c>AgentToolLauncher</c> may launch to run the
    ///     configured agent-tool command; used to hunt down and kill leftover windows this
    ///     context's own launch is responsible for.
    /// </summary>
    private static readonly string[] ShellProcessNames = ["pwsh", "powershell", "cmd"];

    private readonly HashSet<int> _preExistingShellProcessIds;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new isolated test context: creates temp directories, writes
    ///     <c>settings.json</c>, and records a baseline of already-running shell processes.
    /// </summary>
    /// <param name="packageSourcePath">The package source directory to configure, or
    ///     <see langword="null"/> to leave it unconfigured.</param>
    /// <param name="pinnedPackageName">The pinned package name to seed for
    ///     <see cref="RepoPath"/>, or <see langword="null"/> for a never-synced repo.</param>
    /// <param name="pinnedPackageVersion">The pinned package version to seed for
    ///     <see cref="RepoPath"/> (also written as the repo's actual <c>.agentcontrol.json</c>
    ///     pin file when <paramref name="pinnedPackageName"/> is supplied, since
    ///     <c>RepoCardViewModel.Refresh()</c> always re-reads the pin from disk), or
    ///     <see langword="null"/>.</param>
    public AgentControlTestContext(
        string? packageSourcePath = null,
        string? pinnedPackageName = null,
        string? pinnedPackageVersion = null)
    {
        var root = Directory.CreateTempSubdirectory("AgentControlUiTests-");
        RootDirectory = root.FullName;
        ConfigDirectory = Path.Combine(RootDirectory, "config");
        RepoPath = Path.Combine(RootDirectory, "repo");
        ArgLoggerOutputFile = Path.Combine(RootDirectory, "arglogger.jsonl");

        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(RepoPath);

        var argLoggerStubExe = TestPaths.ResolveArgLoggerStubExe();
        TestSettingsWriter.Write(
            ConfigDirectory,
            argLoggerStubExe,
            packageSourcePath,
            RepoPath,
            pinnedPackageName,
            pinnedPackageVersion);

        if (pinnedPackageName is not null && pinnedPackageVersion is not null)
        {
            TestRepoPinWriter.Write(RepoPath, pinnedPackageName, pinnedPackageVersion);
        }

        _preExistingShellProcessIds = SnapshotShellProcessIds();
    }

    /// <summary>
    ///     Gets the temp root directory containing this context's config/repo/log paths.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    ///     Gets the isolated <c>--config-dir</c> this context launches AgentControl against.
    /// </summary>
    public string ConfigDirectory { get; }

    /// <summary>
    ///     Gets the isolated repo directory pre-seeded into the recent-repos list.
    /// </summary>
    public string RepoPath { get; }

    /// <summary>
    ///     Gets the file the arg-logger stub appends its invocation records to.
    /// </summary>
    public string ArgLoggerOutputFile { get; }

    /// <summary>
    ///     Gets the launched FlaUI <see cref="Application"/>, or <see langword="null"/> before
    ///     <see cref="Launch"/> is called.
    /// </summary>
    public Application? App { get; private set; }

    /// <summary>
    ///     Gets the <see cref="UIA3Automation"/> instance used to find elements, or
    ///     <see langword="null"/> before <see cref="Launch"/> is called.
    /// </summary>
    public UIA3Automation? Automation { get; private set; }

    /// <summary>
    ///     Launches AgentControl.exe against this context's isolated config directory and waits
    ///     for its main window to appear.
    /// </summary>
    /// <returns>The main window, ready to be driven by FlaUI.</returns>
    /// <exception cref="TimeoutException">Thrown when the main window does not appear within a
    ///     reasonable time.</exception>
    public Window Launch()
    {
        var exePath = TestPaths.ResolveAgentControlExe();

        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--config-dir");
        startInfo.ArgumentList.Add(ConfigDirectory);
        startInfo.EnvironmentVariables["ARGLOGGER_OUTPUT_FILE"] = ArgLoggerOutputFile;

        App = Application.Launch(startInfo);
        Automation = new UIA3Automation();

        var mainWindow = Retry.WhileNull(
            () => App.GetMainWindow(Automation),
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(200),
            ignoreException: true).Result;

        return mainWindow ?? throw new TimeoutException(
            "AgentControl's main window did not appear within the timeout.");
    }

    /// <summary>
    ///     Finds a currently-open top-level window (other than the main window) whose title
    ///     matches <paramref name="titlePredicate"/>, polling until found or the timeout elapses.
    /// </summary>
    /// <param name="titlePredicate">Predicate matched against each top-level window's title.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>The matching window, or <see langword="null"/> if none appeared in time.</returns>
    public Window? WaitForWindow(Func<string, bool> titlePredicate, TimeSpan timeout)
    {
        if (App is null || Automation is null)
        {
            return null;
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = App.GetAllTopLevelWindows(Automation)
                .FirstOrDefault(window => titlePredicate(window.Title ?? string.Empty));
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    /// <summary>
    ///     Closes/kills the launched AgentControl process, disposes the automation instance,
    ///     kills any new shell process spawned by the agent-tool launch, and best-effort deletes
    ///     this context's temp directory tree.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            App?.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Best-effort: the process may have already exited.
        }

        try
        {
            App?.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Best-effort: the process may have already exited.
        }

        App?.Dispose();
        Automation?.Dispose();

        KillNewShellProcesses();

        try
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup only; a locked temp file must not fail the test itself.
        }
    }

    /// <summary>
    ///     Kills any shell process (see <see cref="ShellProcessNames"/>) that started after this
    ///     context's construction, i.e. one this context's AgentControl launch is responsible for
    ///     spawning via the agent-tool "Launch" action.
    /// </summary>
    private void KillNewShellProcesses()
    {
        foreach (var processId in SnapshotShellProcessIds())
        {
            if (_preExistingShellProcessIds.Contains(processId))
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or ArgumentException)
            {
                // Process may have already exited on its own, or we may lack permission to kill
                // a process owned by another session - either way, best-effort only.
            }
        }
    }

    /// <summary>
    ///     Snapshots the process IDs of every currently-running candidate shell process.
    /// </summary>
    /// <returns>The set of matching process IDs.</returns>
    private static HashSet<int> SnapshotShellProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var name in ShellProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                ids.Add(process.Id);
                process.Dispose();
            }
        }

        return ids;
    }
}
