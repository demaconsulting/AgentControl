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
using DemaConsulting.AgentControl.Logging;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.AgentToolLauncher;

/// <summary>
///     Builds the <see cref="ProcessStartInfo"/> needed to launch an arbitrary agent tool command
///     string inside a detected (or user-overridden) shell, and isolates the actual process spawn
///     so it can be substituted in tests.
/// </summary>
/// <remarks>
///     Splitting <see cref="BuildProcessStartInfo"/> from <see cref="Launch"/> lets tests assert
///     on the constructed <see cref="ProcessStartInfo"/> (file name, arguments, working
///     directory) without ever spawning a real terminal process, per architecture.md's
///     testability requirements. Stateless and thread-safe.
/// </remarks>
internal static class AgentToolLauncher
{
    /// <summary>
    ///     Fully qualified logger category name used for this static class's diagnostics.
    /// </summary>
    /// <remarks>
    ///     A string category is used (rather than the generic <c>ILogger&lt;AgentToolLauncher&gt;</c>
    ///     pattern used by non-static classes like <c>GitClient</c>) because C# does not permit a
    ///     static class to be used as a generic type argument.
    /// </remarks>
    private const string LoggerCategoryName = "DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher";

    /// <summary>
    ///     Builds a <see cref="ProcessStartInfo"/> that launches <paramref name="command"/> inside
    ///     <paramref name="shell"/>, in <paramref name="workingDirectory"/>.
    /// </summary>
    /// <param name="shell">The shell to launch the command in (from <see cref="ShellDetector"/>
    ///     or a user override).</param>
    /// <param name="command">The command line to run inside the shell, e.g. the configured agent
    ///     tool's invocation string.</param>
    /// <param name="workingDirectory">The directory the shell process starts in (the target
    ///     repo's root).</param>
    /// <returns>
    ///     A <see cref="ProcessStartInfo"/> ready to pass to <see cref="Launch"/>. The shell is
    ///     started with <c>-NoExit</c> (PowerShell) or <c>/K</c> (cmd) so the window remains open
    ///     for the user to interact with the launched tool; POSIX shells run the command via
    ///     <c>-c</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="shell"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="command"/> or
    ///     <paramref name="workingDirectory"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="shell"/> has an
    ///     unrecognized <see cref="ShellKind"/> value.</exception>
    public static ProcessStartInfo BuildProcessStartInfo(DetectedShell shell, string command, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = shell.ExecutablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        switch (shell.Kind)
        {
            case ShellKind.PowerShellCore:
            case ShellKind.WindowsPowerShell:
                // -NoExit keeps the window open after the command completes so the user can
                // interact with the launched agent tool; -NoLogo suppresses the PowerShell banner.
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoExit");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(command);
                break;

            case ShellKind.Cmd:
                // /K keeps the cmd window open after the command completes, mirroring -NoExit.
                startInfo.ArgumentList.Add("/K");
                startInfo.ArgumentList.Add(command);
                break;

            case ShellKind.Posix:
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(command);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(shell), shell.Kind, "Unrecognized shell kind.");
        }

        return startInfo;
    }

    /// <summary>
    ///     Starts a process from a previously built <see cref="ProcessStartInfo"/>.
    /// </summary>
    /// <param name="startInfo">The process start information, typically from
    ///     <see cref="BuildProcessStartInfo"/>.</param>
    /// <param name="logger">
    ///     Logger for process-launch diagnostics, or <see langword="null"/> to fall back to
    ///     <see cref="AppLogging.Factory"/>. Instrumented for consistency with
    ///     <c>GitClient.RunGit</c> since this method spawns a real OS process and could
    ///     theoretically hit the same class of intermittent-launch-failure issue documented in
    ///     <c>.agent-logs/implementation-agentcontrol-v1-final-3e91c7.md</c>.
    /// </param>
    /// <returns>The started <see cref="Process"/>; the caller owns its lifetime.</returns>
    /// <remarks>
    ///     Isolated from <see cref="BuildProcessStartInfo"/> specifically so unit tests can verify
    ///     the constructed <see cref="ProcessStartInfo"/> without invoking this method (which
    ///     spawns a real OS process).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startInfo"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process fails to start.</exception>
    public static Process Launch(ProcessStartInfo startInfo, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var effectiveLogger = logger ?? AppLogging.Factory.CreateLogger(LoggerCategoryName);
        var argumentsText = string.Join(' ', startInfo.ArgumentList);

        effectiveLogger.LogDebug(
            "Starting shell process '{FileName}' with arguments '{Arguments}' in working directory '{WorkingDirectory}'",
            startInfo.FileName, argumentsText, startInfo.WorkingDirectory);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var process = Process.Start(startInfo)
                           ?? throw new InvalidOperationException(
                               $"Failed to start shell process '{startInfo.FileName}'.");

            stopwatch.Stop();
            effectiveLogger.LogInformation(
                "Started shell process '{FileName} {Arguments}' as PID {ProcessId} after {ElapsedMilliseconds}ms",
                startInfo.FileName, argumentsText, process.Id, stopwatch.ElapsedMilliseconds);

            return process;
        }
        catch (Win32Exception ex)
        {
            stopwatch.Stop();

            // NativeErrorCode is the actual OS error code behind a Win32Exception - the same
            // critical diagnostic data highlighted as missing for GitClient's intermittent
            // failures; captured here too since this is another real-process-spawning path.
            effectiveLogger.LogError(
                ex,
                "Failed to start shell process '{FileName} {Arguments}' after {ElapsedMilliseconds}ms (Win32 NativeErrorCode={NativeErrorCode})",
                startInfo.FileName, argumentsText, stopwatch.ElapsedMilliseconds, ex.NativeErrorCode);

            throw new InvalidOperationException(
                $"Failed to start shell process '{startInfo.FileName} {argumentsText}': {ex.Message}", ex);
        }
    }
}
