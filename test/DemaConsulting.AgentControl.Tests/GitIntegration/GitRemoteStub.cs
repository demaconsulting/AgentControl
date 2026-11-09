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

using System.Text.RegularExpressions;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Creates a stub script that stands in for the git executable in the
///     <c>GitRemoteClient</c> tests: it records, for every invocation, the arguments, the
///     environment variables that matter and the working directory, and answers each git command
///     (<c>ls-remote</c>, <c>init</c>, <c>fetch</c>, <c>rev-parse</c>, <c>ls-tree</c>,
///     <c>cat-file</c>, <c>config</c>, <c>--version</c> recorded as <c>version</c>) with canned standard output, standard
///     error and exit code set per command.
/// </summary>
/// <remarks>
///     On Windows the stub is a <c>.bat</c> file; elsewhere a POSIX shell script marked executable.
///     The canned answers live in files next to the log so that tabs, NUL separators and quotes
///     reach the client byte for byte. A per-command sleep makes the command hang (and append
///     <c>FINISHED</c> to the log when it is not killed first). The batch form drops a <c>^</c>
///     from the recorded argument text (the command processor treats it as an escape), so tests
///     assert the commands and options rather than <c>^{commit}</c>.
/// </remarks>
internal sealed partial class GitRemoteStub
{
    /// <summary>
    ///     The marker the stub appends to the log after its sleep, when it was not killed first.
    /// </summary>
    public const string FinishedMarker = "FINISHED";

    /// <summary>
    ///     The environment variables the stub records.
    /// </summary>
    private static readonly string[] RecordedVariables =
    [
        "GIT_TERMINAL_PROMPT", "GCM_INTERACTIVE", "GIT_ASKPASS", "SSH_ASKPASS", "SSH_ASKPASS_REQUIRE",
        "GIT_SSH_COMMAND", "GIT_ALLOW_PROTOCOL", "GIT_LFS_SKIP_SMUDGE", "GIT_LITERAL_PATHSPECS",
        "GIT_OPTIONAL_LOCKS", "GIT_NO_LAZY_FETCH", "LC_ALL", "GIT_CEILING_DIRECTORIES", "GIT_TRACE",
        "GIT_TRACE_PACKET", "GIT_CURL_VERBOSE", "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE"
    ];

    /// <summary>
    ///     Private constructor - use <see cref="Create"/>.
    /// </summary>
    /// <param name="path">The script path.</param>
    /// <param name="logPath">The invocation log path.</param>
    private GitRemoteStub(string path, string logPath)
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
    ///     Gets the pattern of one recorded <c>NAME=[value]</c> pair.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"([A-Z_]+)=\[([^\]]*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentPair();

    /// <summary>
    ///     Creates a stub that answers every command with success and no output.
    /// </summary>
    /// <returns>The stub.</returns>
    public static GitRemoteStub Create()
    {
        var token = Guid.NewGuid().ToString("N");
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentcontrol_git_remote_{token}.log");
        if (OperatingSystem.IsWindows())
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentcontrol_git_remote_{token}.bat");
            File.WriteAllText(path, BuildBatchScript(logPath));
            return new GitRemoteStub(path, logPath);
        }
        else
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"agentcontrol_git_remote_{token}.sh");
            File.WriteAllText(path, BuildShellScript(logPath));
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return new GitRemoteStub(path, logPath);
        }
    }

    /// <summary>
    ///     Sets the canned answer of one git command.
    /// </summary>
    /// <param name="command">The git command: <c>ls-remote</c>, <c>init</c>, <c>fetch</c>,
    ///     <c>rev-parse</c>, <c>ls-tree</c>, <c>cat-file</c>, <c>config</c> or <c>version</c>.</param>
    /// <param name="standardOutput">What the command prints to standard output.</param>
    /// <param name="standardError">What the command prints to standard error.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="sleepSeconds">How long the command hangs before answering; zero for no wait.</param>
    /// <returns>This stub.</returns>
    public GitRemoteStub Respond(
        string command, string standardOutput = "", string standardError = "", int exitCode = 0, int sleepSeconds = 0)
    {
        File.WriteAllText(LogPath + ".out." + command, standardOutput);
        File.WriteAllText(LogPath + ".err." + command, standardError);
        File.WriteAllText(LogPath + ".code." + command, exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (sleepSeconds > 0)
        {
            // The batch form waits with ping, which sends one request a second: N + 1 waits N seconds
            File.WriteAllText(
                LogPath + ".sleep." + command,
                (OperatingSystem.IsWindows() ? sleepSeconds + 1 : sleepSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return this;
    }

    /// <summary>
    ///     Reads the invocations recorded so far.
    /// </summary>
    /// <returns>One entry per invocation, in order; empty when the stub never ran.</returns>
    public IReadOnlyList<GitRemoteInvocation> ReadInvocations()
    {
        if (!File.Exists(LogPath))
        {
            return [];
        }

        var invocations = new List<GitRemoteInvocation>();
        string arguments = string.Empty;
        var environment = new Dictionary<string, string>();
        var command = string.Empty;
        foreach (var rawLine in File.ReadAllLines(LogPath))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("ARGS:", StringComparison.Ordinal))
            {
                arguments = line["ARGS:".Length..].Trim();
            }
            else if (line.StartsWith("ENV:", StringComparison.Ordinal))
            {
                environment = EnvironmentPair().Matches(line).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            }
            else if (line.StartsWith("CMD:", StringComparison.Ordinal))
            {
                command = line["CMD:".Length..].Trim();
            }
            else if (line.StartsWith("CWD:", StringComparison.Ordinal))
            {
                invocations.Add(new GitRemoteInvocation(command, arguments, environment, line["CWD:".Length..]));
            }
        }

        return invocations;
    }

    /// <summary>
    ///     Gets a value indicating whether a command got as far as its post-sleep marker, i.e. was
    ///     not killed while hanging.
    /// </summary>
    /// <returns><see langword="true"/> when the marker was written.</returns>
    public bool WroteFinishedMarker() =>
        File.Exists(LogPath) && File.ReadAllLines(LogPath).Any(line => line.Trim() == FinishedMarker);

    /// <summary>
    ///     Deletes the stub, its log and its canned answers.
    /// </summary>
    public void Delete()
    {
        var folder = System.IO.Path.GetDirectoryName(LogPath)!;
        var prefix = System.IO.Path.GetFileName(LogPath);
        foreach (var file in Directory.EnumerateFiles(folder, prefix + "*").Append(Path))
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
    private static string BuildBatchScript(string logPath)
    {
        var environment = string.Join(";", RecordedVariables.Select(name => $"{name}=[%{name}%]"));
        var lines = new List<string>
        {
            "@echo off",
            $"set \"LOG={logPath}\"",
            ">>\"%LOG%\" echo ARGS:%*",
            $">>\"%LOG%\" echo ENV:{environment}",
            "set \"ALL= %* \"",
            "set \"CMD=other\""
        };
        foreach (var command in new[] { "ls-remote", "init", "fetch", "rev-parse", "ls-tree", "cat-file", "config", "version" })
        {
            var word = command == "version" ? "--version" : command;
            lines.Add($"echo %ALL% | findstr /C:\" {word} \" >nul && set \"CMD={command}\"");
        }

        lines.AddRange(
        [
            ">>\"%LOG%\" echo CMD:%CMD%",
            ">>\"%LOG%\" echo CWD:%CD%",
            "if exist \"%LOG%.sleep.%CMD%\" goto :sleep",
            ":answer",
            "if exist \"%LOG%.out.%CMD%\" type \"%LOG%.out.%CMD%\"",
            "if exist \"%LOG%.err.%CMD%\" type \"%LOG%.err.%CMD%\" 1>&2",
            "set \"CODE=0\"",
            "if exist \"%LOG%.code.%CMD%\" set /p CODE=<\"%LOG%.code.%CMD%\"",
            "exit /b %CODE%",
            ":sleep",
            "set /p SECS=<\"%LOG%.sleep.%CMD%\"",
            "ping -n %SECS% 127.0.0.1 >nul",
            $">>\"%LOG%\" echo {FinishedMarker}",
            "goto :answer"
        ]);
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    ///     Builds the POSIX shell script.
    /// </summary>
    private static string BuildShellScript(string logPath)
    {
        var environment = string.Join(";", RecordedVariables.Select(name => $"{name}=[${{{name}}}]"));
        var lines = new List<string>
        {
            "#!/bin/sh",
            $"LOG='{logPath}'",
            "echo \"ARGS:$*\" >> \"$LOG\"",
            $"echo \"ENV:{environment}\" >> \"$LOG\"",
            "case \" $* \" in"
        };
        foreach (var command in new[] { "ls-remote", "init", "fetch", "rev-parse", "ls-tree", "cat-file", "config", "version" })
        {
            var word = command == "version" ? "--version" : command;
            lines.Add($"  *\" {word} \"*) CMD={command} ;;");
        }

        lines.AddRange(
        [
            "  *) CMD=other ;;",
            "esac",
            "echo \"CMD:$CMD\" >> \"$LOG\"",
            "echo \"CWD:$PWD\" >> \"$LOG\"",
            "if [ -f \"$LOG.sleep.$CMD\" ]; then sleep \"$(cat \"$LOG.sleep.$CMD\")\"; echo " + FinishedMarker + " >> \"$LOG\"; fi",
            "if [ -f \"$LOG.out.$CMD\" ]; then cat \"$LOG.out.$CMD\"; fi",
            "if [ -f \"$LOG.err.$CMD\" ]; then cat \"$LOG.err.$CMD\" >&2; fi",
            "CODE=0",
            "if [ -f \"$LOG.code.$CMD\" ]; then CODE=$(cat \"$LOG.code.$CMD\"); fi",
            "exit $CODE"
        ]);
        return string.Join("\n", lines) + "\n";
    }
}

/// <summary>
///     One recorded invocation of <see cref="GitRemoteStub"/>.
/// </summary>
/// <param name="Command">The git command the stub recognized, or <c>other</c>.</param>
/// <param name="Arguments">The arguments, as one string.</param>
/// <param name="Environment">The recorded environment variables (empty text for one that is not
///     set).</param>
/// <param name="WorkingDirectory">The working directory.</param>
internal sealed record GitRemoteInvocation(
    string Command, string Arguments, IReadOnlyDictionary<string, string> Environment, string WorkingDirectory);
