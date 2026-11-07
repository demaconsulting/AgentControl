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

using System.Text;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Creates a stub script that stands in for the git executable in the
///     <c>GitCredentialClient</c> tests: it records, for every invocation, the arguments, the
///     environment variables that matter, the working directory and everything written to its
///     standard input, and answers <c>config --get-urlmatch</c>, <c>credential fill</c>,
///     <c>credential approve</c> and <c>credential reject</c> with canned output and exit codes.
/// </summary>
/// <remarks>
///     On Windows the stub is a <c>.bat</c> file; elsewhere it is a POSIX shell script marked
///     executable. A <c>sleepSeconds</c> setting makes the credential operations hang (and then
///     append <c>FINISHED</c> to the log) so timeout and kill behavior can be observed: a killed
///     process never gets to write it.
/// </remarks>
internal sealed class GitCredentialStub
{
    /// <summary>
    ///     The marker the stub appends to the log after its sleep, when it was not killed first.
    /// </summary>
    public const string FinishedMarker = "FINISHED";

    /// <summary>
    ///     Private constructor - use <see cref="Create"/>.
    /// </summary>
    /// <param name="path">The script path.</param>
    /// <param name="logPath">The invocation log path.</param>
    private GitCredentialStub(string path, string logPath)
    {
        Path = path;
        LogPath = logPath;
    }

    /// <summary>
    ///     Gets the path of the stub, suitable as the git executable path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Gets the path of the log the stub appends to.
    /// </summary>
    public string LogPath { get; }

    /// <summary>
    ///     Creates a stub.
    /// </summary>
    /// <param name="configOutput">What <c>config --get-urlmatch</c> prints (the helper).</param>
    /// <param name="configExitCode">The exit code of <c>config --get-urlmatch</c>.</param>
    /// <param name="fillOutput">What <c>credential fill</c> prints, as lines of <c>key=value</c>
    ///     joined by a pipe character (no spaces or shell metacharacters).</param>
    /// <param name="fillExitCode">The exit code of <c>credential fill</c>.</param>
    /// <param name="approveExitCode">The exit code of <c>credential approve</c>.</param>
    /// <param name="rejectExitCode">The exit code of <c>credential reject</c>.</param>
    /// <param name="sleepSeconds">How long every credential operation hangs before answering.</param>
    /// <returns>The stub.</returns>
    public static GitCredentialStub Create(
        string configOutput = "manager",
        int configExitCode = 0,
        string fillOutput = "",
        int fillExitCode = 0,
        int approveExitCode = 0,
        int rejectExitCode = 0,
        int sleepSeconds = 0)
    {
        var token = Guid.NewGuid().ToString("N");
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentcontrol_git_credential_{token}.log");
        if (OperatingSystem.IsWindows())
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentcontrol_git_credential_{token}.bat");
            File.WriteAllText(path, BuildBatchScript(
                logPath, configOutput, configExitCode, fillOutput, fillExitCode, approveExitCode, rejectExitCode, sleepSeconds));
            return new GitCredentialStub(path, logPath);
        }
        else
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentcontrol_git_credential_{token}.sh");
            File.WriteAllText(path, BuildShellScript(
                logPath, configOutput, configExitCode, fillOutput, fillExitCode, approveExitCode, rejectExitCode, sleepSeconds));
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return new GitCredentialStub(path, logPath);
        }
    }

    /// <summary>
    ///     Reads the invocations recorded so far.
    /// </summary>
    /// <returns>One entry per invocation, in order; empty when the stub never ran.</returns>
    public IReadOnlyList<StubInvocation> ReadInvocations()
    {
        if (!File.Exists(LogPath))
        {
            return [];
        }

        var invocations = new List<StubInvocation>();
        string? arguments = null;
        string environment = string.Empty;
        string workingDirectory = string.Empty;
        var input = new StringBuilder();
        var inInput = false;
        foreach (var rawLine in File.ReadAllLines(LogPath))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("ARGS:", StringComparison.Ordinal))
            {
                arguments = line["ARGS:".Length..].Trim();
            }
            else if (line.StartsWith("ENV:", StringComparison.Ordinal))
            {
                environment = line["ENV:".Length..];
            }
            else if (line.StartsWith("CWD:", StringComparison.Ordinal))
            {
                workingDirectory = line["CWD:".Length..];
            }
            else if (line == "STDIN-BEGIN")
            {
                inInput = true;
                input.Clear();
            }
            else if (line == "STDIN-END")
            {
                inInput = false;
                invocations.Add(new StubInvocation(arguments ?? string.Empty, environment, workingDirectory, input.ToString()));
            }
            else if (inInput)
            {
                input.Append(line).Append('\n');
            }
        }

        return invocations;
    }

    /// <summary>
    ///     Gets a value indicating whether the stub got as far as its post-sleep marker, i.e. was
    ///     not killed while hanging.
    /// </summary>
    /// <returns><see langword="true"/> when the marker was written.</returns>
    public bool WroteFinishedMarker() =>
        File.Exists(LogPath) && File.ReadAllLines(LogPath).Any(line => line.Trim() == FinishedMarker);

    /// <summary>
    ///     Deletes the stub and its log.
    /// </summary>
    public void Delete()
    {
        foreach (var file in new[] { Path, LogPath })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    ///     Builds the Windows batch script.
    /// </summary>
    private static string BuildBatchScript(
        string logPath, string configOutput, int configExitCode, string fillOutput, int fillExitCode,
        int approveExitCode, int rejectExitCode, int sleepSeconds)
    {
        var lines = new List<string>
        {
            "@echo off",
            $">>\"{logPath}\" echo ARGS:%*",
            $">>\"{logPath}\" echo ENV:GIT_TERMINAL_PROMPT=%GIT_TERMINAL_PROMPT%;GCM_INTERACTIVE=%GCM_INTERACTIVE%;GIT_ASKPASS=[%GIT_ASKPASS%];SSH_ASKPASS=[%SSH_ASKPASS%]",
            $">>\"{logPath}\" echo CWD:%CD%",
            $">>\"{logPath}\" echo STDIN-BEGIN",
            $"findstr \"^\" >>\"{logPath}\"",
            $">>\"{logPath}\" echo STDIN-END",
            "echo %* | findstr /C:\" fill\" >nul && goto :fill",
            "echo %* | findstr /C:\" approve\" >nul && goto :approve",
            "echo %* | findstr /C:\" reject\" >nul && goto :reject",
            "echo %* | findstr /C:\"--get-urlmatch\" >nul && goto :config",
            "exit /b 127",
            ":config"
        };
        if (configOutput.Length > 0)
        {
            lines.Add($"echo {configOutput}");
        }

        lines.Add($"exit /b {configExitCode}");
        lines.Add(":fill");
        lines.AddRange(BatchSleep(logPath, sleepSeconds));
        foreach (var pair in fillOutput.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            lines.Add($"echo {pair}");
        }

        lines.Add($"exit /b {fillExitCode}");
        lines.Add(":approve");
        lines.AddRange(BatchSleep(logPath, sleepSeconds));
        lines.Add($"exit /b {approveExitCode}");
        lines.Add(":reject");
        lines.AddRange(BatchSleep(logPath, sleepSeconds));
        lines.Add($"exit /b {rejectExitCode}");
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    ///     Builds the batch lines that hang for a while and then write the finished marker.
    /// </summary>
    private static IEnumerable<string> BatchSleep(string logPath, int sleepSeconds)
    {
        if (sleepSeconds <= 0)
        {
            yield break;
        }

        // ping waits one second between echo requests, so N + 1 requests wait N seconds
        yield return $"ping -n {sleepSeconds + 1} 127.0.0.1 >nul";
        yield return $">>\"{logPath}\" echo {FinishedMarker}";
    }

    /// <summary>
    ///     Builds the POSIX shell script.
    /// </summary>
    private static string BuildShellScript(
        string logPath, string configOutput, int configExitCode, string fillOutput, int fillExitCode,
        int approveExitCode, int rejectExitCode, int sleepSeconds)
    {
        var sleep = sleepSeconds > 0 ? $"sleep {sleepSeconds}; echo {FinishedMarker} >> \"{logPath}\"" : ":";
        var fillLines = string.Join(
            "; ",
            fillOutput.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(pair => $"echo '{pair}'"));
        var lines = new List<string>
        {
            "#!/bin/sh",
            "{",
            "  echo \"ARGS:$*\"",
            "  echo \"ENV:GIT_TERMINAL_PROMPT=$GIT_TERMINAL_PROMPT;GCM_INTERACTIVE=$GCM_INTERACTIVE;GIT_ASKPASS=[$GIT_ASKPASS];SSH_ASKPASS=[$SSH_ASKPASS]\"",
            "  echo \"CWD:$PWD\"",
            "  echo STDIN-BEGIN",
            "  cat",
            "  echo STDIN-END",
            $"}} >> \"{logPath}\"",
            "case \" $* \" in",
            $"  *\" fill \"*) {sleep}; {(fillLines.Length > 0 ? fillLines : ":")}; exit {fillExitCode} ;;",
            $"  *\" approve \"*) {sleep}; exit {approveExitCode} ;;",
            $"  *\" reject \"*) {sleep}; exit {rejectExitCode} ;;",
            $"  *\"--get-urlmatch\"*) {(configOutput.Length > 0 ? $"echo '{configOutput}'" : ":")}; exit {configExitCode} ;;",
            "  *) exit 127 ;;",
            "esac"
        };
        return string.Join("\n", lines) + "\n";
    }
}

/// <summary>
///     One recorded invocation of <see cref="GitCredentialStub"/>.
/// </summary>
/// <param name="Arguments">The arguments, as one string.</param>
/// <param name="Environment">The recorded environment variables.</param>
/// <param name="WorkingDirectory">The working directory.</param>
/// <param name="StandardInput">Everything written to the standard input, one line per
///     <c>\n</c>.</param>
internal sealed record StubInvocation(string Arguments, string Environment, string WorkingDirectory, string StandardInput);
