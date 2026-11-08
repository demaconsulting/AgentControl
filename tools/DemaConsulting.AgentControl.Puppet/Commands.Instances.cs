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
using DemaConsulting.AgentControl.Puppet.Automation;

namespace DemaConsulting.AgentControl.Puppet;

/// <content>
///     Instance management: <c>launch</c>, <c>instances</c> and <c>quit</c>.
/// </content>
internal static partial class Commands
{
    /// <summary>
    ///     The error label reported when an instance cannot be launched.
    /// </summary>
    private const string LaunchFailedCode = "LAUNCH_FAILED";

    /// <summary>
    ///     The title of AgentControl's main window; <c>launch</c> is ready once it is showing.
    /// </summary>
    private const string MainWindowTitle = AttachContext.MainWindowTitle;

    /// <summary>
    ///     How long a launched instance gets to show its main window.
    /// </summary>
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     How long a closed instance gets to exit.
    /// </summary>
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Starts an AgentControl instance with an isolated config folder and waits until its main window is showing.
    /// </summary>
    /// <param name="parsed">The parsed command: <c>--instance</c>, <c>--exe</c>, <c>--config-dir</c>, <c>--git-path</c>, <c>--agent-tool-command</c>, <c>--allow-existing</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Launch(ParsedCommand parsed)
    {
        if (!LaunchOptions.TryBuild(parsed, InstanceRegistry.DefaultRoot, out var plan, out var optionError))
        {
            throw PuppetException.Usage(optionError);
        }

        var registry = InstanceRegistry.CreateDefault();
        var alias = plan.Alias;
        if (!parsed.HasOption("allow-existing") && registry.TryGetLive(alias, out _) is { } existing)
        {
            throw new PuppetException(ExitCodes.LaunchFailed, LaunchFailedCode, $"Instance '{alias}' is already running (pid={existing.Pid}). Use quit first, another --instance alias, or --allow-existing.");
        }

        if (!AppLocator.TryResolveExePath(parsed.GetOption("exe"), AppContext.BaseDirectory, out var exe, out var resolveError))
        {
            throw new PuppetException(ExitCodes.LaunchFailed, LaunchFailedCode, resolveError);
        }

        Directory.CreateDirectory(plan.ConfigDir);

        // Shell execution gives the application no inherited standard handles: a caller that captures the Puppet's output
        // (a pipeline, a build log) would otherwise wait for the application to exit before it saw the end of the output.
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            Arguments = LaunchOptions.JoinArguments(plan.Arguments),
        };
        using var process = Process.Start(startInfo)
            ?? throw new PuppetException(ExitCodes.LaunchFailed, LaunchFailedCode, $"Starting '{exe}' returned no process.");

        // The id and start time are read once: FlaUI takes over (and may dispose) the Process object it attaches to
        var pid = process.Id;
        registry.Save(new InstanceRecord(alias, pid, process.StartTime.ToUniversalTime(), exe, plan.ConfigDir));
        try
        {
            var title = WaitUntilReady(alias, process, pid);
            return Ok($"OK instance={alias} pid={pid} exe=\"{exe}\" config=\"{plan.ConfigDir}\" title=\"{title}\"");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A half-started instance would hold the alias and a window; do not leave it behind
            KillQuietly(process);
            registry.Delete(alias);
            throw;
        }
    }

    /// <summary>
    ///     Lists the live instances that were launched through the Puppet and forgets the records of the ones that are gone.
    /// </summary>
    /// <returns>The exit code (always success; an empty list is not an error).</returns>
    internal static int Instances()
    {
        var live = InstanceRegistry.CreateDefault().ListAndPrune(out var pruned);
        foreach (var (alias, state) in pruned)
        {
            Console.WriteLine($"PRUNED alias={alias} reason={state}");
        }

        if (live.Count == 0)
        {
            return Ok("NO_INSTANCES");
        }

        foreach (var record in live)
        {
            Console.WriteLine($"INSTANCE alias={record.Alias} pid={record.Pid} title=\"{ReadTitle(record.Pid)}\" exe=\"{record.ExePath}\" config=\"{record.ConfigDir}\" started={record.StartTimeUtc:O}");
        }

        return ExitCodes.Success;
    }

    /// <summary>
    ///     Closes an instance's windows gracefully and waits for the process to exit.
    /// </summary>
    /// <param name="parsed">The parsed command: <c>--instance</c>, <c>--kill</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Quit(ParsedCommand parsed)
    {
        var alias = ValidAlias(parsed);
        var registry = InstanceRegistry.CreateDefault();

        // The validated handle stays open for the whole command, so the process id cannot be reused meanwhile:
        // waiting and killing act on the recorded instance only
        using var instance = registry.TryOpenLive(alias, out var record, out var state);
        if (instance is null || record is null)
        {
            // A record that no longer describes a live instance is stale: forget it
            registry.Delete(alias);
            throw new PuppetException(ExitCodes.NotRunning, NotRunningCode, NotRunningMessage(alias, state));
        }

        RequestClose(alias, record.Pid);
        if (WaitForExit(instance, QuitTimeout))
        {
            registry.Delete(alias);
            return Ok($"OK quit instance={alias} pid={record.Pid}");
        }

        if (parsed.HasOption("kill"))
        {
            KillQuietly(instance);
            WaitForExit(instance, QuitTimeout);
            registry.Delete(alias);
            return Ok($"OK killed instance={alias} pid={record.Pid}");
        }

        throw new PuppetException(ExitCodes.Timeout, "TIMEOUT", $"pid={record.Pid} did not exit after the close request. Use --kill to end it.");
    }

    /// <summary>
    ///     Asks an instance to close: its dialogs first (an open modal dialog would block the main window), then the main window,
    ///     through the UI Automation Window pattern (the same request as the close button).
    /// </summary>
    /// <param name="alias">The instance alias.</param>
    /// <param name="pid">The process id.</param>
    private static void RequestClose(string alias, int pid)
    {
        // A fresh Process object: FlaUI disposes the one it is given
        using var process = Process.GetProcessById(pid);
        using var context = AttachContext.TryAttach(alias, process, out _);
        if (context is null)
        {
            return;
        }

        // Innermost first: a dialog that has a dialog open of its own (modal) does not close until that one is gone
        foreach (var dialog in context.Roots().Skip(1).Where(w => w.Patterns.Window.IsSupported).Reverse())
        {
            _ = UiActions.Safe(() => { dialog.Patterns.Window.Pattern.Close(); return true; }, false);
            Thread.Sleep(150);
        }

        Thread.Sleep(200);
        if (!UiActions.Safe(() => { context.MainWindow.Patterns.Window.Pattern.Close(); return true; }, false))
        {
            _ = UiActions.Safe(() => { context.MainWindow.Close(); return true; }, false);
        }
    }

    /// <summary>
    ///     Waits until the instance shows its main window.
    /// </summary>
    /// <param name="alias">The instance alias.</param>
    /// <param name="process">The launched process (to notice an early exit).</param>
    /// <param name="pid">The process id.</param>
    /// <returns>The window title.</returns>
    /// <exception cref="PuppetException">Thrown when the process exits early or the time runs out.</exception>
    private static string WaitUntilReady(string alias, Process process, int pid)
    {
        var deadline = DateTime.UtcNow + LaunchTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new PuppetException(ExitCodes.LaunchFailed, LaunchFailedCode, $"AgentControl exited with code {process.ExitCode} before it was ready.");
            }

            if (TryReadTitle(alias, pid) is { } title)
            {
                return title;
            }

            Thread.Sleep(250);
        }

        throw new PuppetException(ExitCodes.LaunchFailed, LaunchFailedCode, $"Instance '{alias}' (pid={pid}) did not show its \"{MainWindowTitle}\" window within {LaunchTimeout.TotalSeconds:0}s.");
    }

    /// <summary>
    ///     Reads the main window title once the window is visible and has the expected title.
    /// </summary>
    /// <param name="alias">The instance alias.</param>
    /// <param name="pid">The process id.</param>
    /// <returns>The title, or <see langword="null"/> while the window is not there, not visible or not titled yet.</returns>
    private static string? TryReadTitle(string alias, int pid)
    {
        // A fresh Process object per attempt: FlaUI disposes the one it is given
        using var process = Process.GetProcessById(pid);
        using var context = AttachContext.TryAttach(alias, process, out _);
        if (context is null || UiActions.Safe(() => context.MainWindow.Properties.IsOffscreen.ValueOrDefault, true))
        {
            return null;
        }

        var title = context.MainWindow.Title;
        return string.Equals(title, MainWindowTitle, StringComparison.Ordinal) ? title : null;
    }

    /// <summary>
    ///     Reads the main window title of a process without UI Automation.
    /// </summary>
    /// <param name="pid">The process id.</param>
    /// <returns>The title, or an empty string.</returns>
    private static string ReadTitle(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainWindowTitle;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    ///     Waits for a validated process to exit.
    /// </summary>
    /// <param name="process">The validated process handle (it keeps the process id from being reused).</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns><see langword="true"/> when the process is gone.</returns>
    private static bool WaitForExit(Process process, TimeSpan timeout)
    {
        try
        {
            return process.WaitForExit(timeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process object no longer describes a running process: it is gone
            return true;
        }
    }

    /// <summary>
    ///     Ends a validated process and its children, ignoring the case that it is already gone.
    /// </summary>
    /// <remarks>
    ///     The process is addressed through its handle, never through its numeric id, so a process id that was reused by an
    ///     unrelated program is never touched.
    /// </remarks>
    /// <param name="process">The validated process handle.</param>
    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already exited: that is what was wanted
        }
    }
}
