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

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Creates a small, platform-appropriate stub script that stands in for the real git
///     executable in <see cref="GitClientTests"/>, so tests never depend on git being installed
///     or on a real repository existing.
/// </summary>
/// <remarks>
///     The stub recognizes five invocation shapes: <c>status --porcelain</c>, <c>pull</c>,
///     <c>rev-parse HEAD</c>, <c>rev-parse --abbrev-ref HEAD</c>, and <c>ls-files -- ...</c>,
///     matching the subcommands <c>GitClient</c> issues for its fixed queries, and any number of further
///     canned commands (<see cref="Command"/>: <c>for-each-ref</c>, <c>remote</c>, <c>switch</c>,
///     <c>clone</c>, <c>rev-parse --short</c>) whose output is read from files. On Windows this is a <c>.bat</c> file
///     (directly runnable by <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/>
///     without <c>UseShellExecute</c>); elsewhere it is a POSIX shell script marked executable via
///     <c>File.SetUnixFileMode</c>. On request, <c>status --porcelain</c> also starts a background copy
///     of the stub (the hidden <c>__holder</c> invocation) that keeps the stub's output open after
///     the stub exited, standing in for a helper git started that outlives git.
/// </remarks>
internal sealed class GitStub
{
    /// <summary>
    ///     How long a command answered with a sleep hangs in the tests that check it is stopped: well
    ///     past their one-second limit or cancellation, yet short enough that a command that was not
    ///     stopped finishes while <see cref="RanToTheEndOfItsHangAsync"/> waits, so a missing kill is
    ///     detected.
    /// </summary>
    public const int HangSeconds = 4;

    /// <summary>
    ///     The line a command that sleeps appends to <see cref="FinishedMarkerPath"/> once its sleep
    ///     is over, i.e. when it was not killed while hanging.
    /// </summary>
    private const string FinishedMarker = "FINISHED";

    /// <summary>
    ///     Gets the path to the created stub script, suitable for passing as the
    ///     <c>GitClient</c>'s executable path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Gets the file a command that sleeps appends <c>FINISHED</c> to once its sleep is over.
    /// </summary>
    public string FinishedMarkerPath => Path + ".finished";

    /// <summary>
    ///     The files that hold the output texts of the extra commands.
    /// </summary>
    private readonly List<string> _sidecars;

    /// <summary>
    ///     Private constructor - use <see cref="Create"/> instead.
    /// </summary>
    /// <param name="path">The created stub script's path.</param>
    /// <param name="sidecars">The files that hold the output texts of the extra commands.</param>
    private GitStub(string path, List<string> sidecars)
    {
        Path = path;
        _sidecars = sidecars;
    }

    /// <summary>
    ///     Creates a stub script with the given canned responses for the "status", "pull",
    ///     "rev-parse HEAD", "rev-parse --abbrev-ref HEAD", and "ls-files" subcommands, and for any
    ///     number of further subcommands (see <see cref="Command"/>).
    /// </summary>
    /// <param name="statusOutput">Stdout text the stub prints for <c>status --porcelain</c>.</param>
    /// <param name="statusError">Stderr text the stub prints for <c>status --porcelain</c>.</param>
    /// <param name="statusExitCode">Exit code the stub returns for <c>status --porcelain</c>.</param>
    /// <param name="pullOutput">Stdout text the stub prints for <c>pull</c>.</param>
    /// <param name="pullError">Stderr text the stub prints for <c>pull</c>.</param>
    /// <param name="pullExitCode">Exit code the stub returns for <c>pull</c>.</param>
    /// <param name="revParseHeadOutput">Stdout text the stub prints for <c>rev-parse HEAD</c>.</param>
    /// <param name="revParseHeadExitCode">Exit code the stub returns for <c>rev-parse HEAD</c>.</param>
    /// <param name="revParseHeadError">Stderr text the stub prints for <c>rev-parse HEAD</c>.</param>
    /// <param name="abbrevRefOutput">Stdout text the stub prints for
    ///     <c>rev-parse --abbrev-ref HEAD</c>.</param>
    /// <param name="abbrevRefExitCode">Exit code the stub returns for
    ///     <c>rev-parse --abbrev-ref HEAD</c>.</param>
    /// <param name="lsFilesOutput">Stdout text the stub prints for <c>ls-files</c>.</param>
    /// <param name="lsFilesExitCode">Exit code the stub returns for <c>ls-files</c>.</param>
    /// <param name="lsFilesError">Stderr text the stub prints for <c>ls-files</c>.</param>
    /// <param name="invocationLogPath">When supplied, the stub appends one line per invocation
    ///     (the full argument list it was called with) to this file, so tests can assert on
    ///     which subcommands were actually invoked and how many times, independent of the
    ///     stub's canned responses.</param>
    /// <param name="statusSleepSeconds">How many seconds <c>status --porcelain</c> sleeps before it
    ///     answers (then appends to <see cref="FinishedMarkerPath"/>); 0 answers at once.</param>
    /// <param name="pullSleepSeconds">How many seconds <c>pull</c> sleeps before it answers (then
    ///     appends to <see cref="FinishedMarkerPath"/>); 0 answers at once.</param>
    /// <param name="revParseHeadSleepSeconds">How many seconds <c>rev-parse HEAD</c> sleeps before it
    ///     answers (then appends to <see cref="FinishedMarkerPath"/>); 0 answers at once.</param>
    /// <param name="statusOutputHolderSeconds">When above 0, <c>status --porcelain</c> first starts a
    ///     background copy of the stub that inherits its standard output and error and keeps them open
    ///     for that many seconds (then appends to <see cref="FinishedMarkerPath"/>), while the stub
    ///     itself answers and exits at once - a helper git started that outlives git. 0 starts
    ///     none.</param>
    /// <param name="extraCommands">Canned answers to further subcommands; they are matched before the
    ///     fixed ones. <see langword="null"/> for none.</param>
    /// <param name="argumentLogPath">When supplied, the stub appends one line per invocation holding
    ///     all of its arguments (the invocation log holds only the first two) to this file.</param>
    /// <param name="environmentLogPath">When supplied together with <paramref name="environmentNames"/>,
    ///     the stub appends, on every invocation, one <c>NAME=value</c> line per named environment
    ///     variable (<c>NAME=&lt;unset&gt;</c> when it is not set) to this file.</param>
    /// <param name="environmentNames">The names of the environment variables to log; see
    ///     <paramref name="environmentLogPath"/>. They must be plain upper-case names.</param>
    /// <returns>A new <see cref="GitStub"/> whose <see cref="Path"/> is ready to use.</returns>
    public static GitStub Create(
        string statusOutput = "",
        string statusError = "",
        int statusExitCode = 0,
        string pullOutput = "",
        string pullError = "",
        int pullExitCode = 0,
        string revParseHeadOutput = "abc1234",
        int revParseHeadExitCode = 0,
        string revParseHeadError = "",
        string abbrevRefOutput = "main",
        int abbrevRefExitCode = 0,
        string lsFilesOutput = "",
        int lsFilesExitCode = 0,
        string lsFilesError = "",
        string? invocationLogPath = null,
        int statusSleepSeconds = 0,
        int pullSleepSeconds = 0,
        int revParseHeadSleepSeconds = 0,
        int statusOutputHolderSeconds = 0,
        IReadOnlyList<Command>? extraCommands = null,
        string? argumentLogPath = null,
        string? environmentLogPath = null,
        IReadOnlyList<string>? environmentNames = null)
    {
        var sleeps = new StubSleeps(
            statusSleepSeconds, pullSleepSeconds, revParseHeadSleepSeconds, statusOutputHolderSeconds);
        var windows = OperatingSystem.IsWindows();
        var environmentLines = BuildEnvironmentLines(windows, environmentLogPath, environmentNames ?? []);
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"agentcontrol_git_stub_{Guid.NewGuid()}" + (windows ? ".bat" : ".sh"));
        var prepared = PrepareCommands(path, extraCommands ?? []);
        var stub = new GitStub(path, prepared.SelectMany(command => new[] { command.OutputPath, command.ErrorPath })
            .OfType<string>().ToList());
        if (windows)
        {
            File.WriteAllText(path, BuildBatchScript(
                statusOutput, statusError, statusExitCode, pullOutput, pullError, pullExitCode,
                revParseHeadOutput, revParseHeadExitCode, revParseHeadError,
                abbrevRefOutput, abbrevRefExitCode,
                lsFilesOutput, lsFilesExitCode, lsFilesError, invocationLogPath, sleeps, path + ".finished",
                prepared, argumentLogPath, environmentLines));
        }
        else
        {
            File.WriteAllText(path, BuildShellScript(
                statusOutput, statusError, statusExitCode, pullOutput, pullError, pullExitCode,
                revParseHeadOutput, revParseHeadExitCode, revParseHeadError,
                abbrevRefOutput, abbrevRefExitCode,
                lsFilesOutput, lsFilesExitCode, lsFilesError, invocationLogPath, sleeps, path + ".finished",
                prepared, argumentLogPath, environmentLines));
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return stub;
    }

    /// <summary>
    ///     Builds the script lines that log the named environment variables on every invocation.
    /// </summary>
    /// <param name="windows">Whether the script is a batch file.</param>
    /// <param name="logPath">The log file, or <see langword="null"/> for no lines.</param>
    /// <param name="names">The variable names.</param>
    /// <returns>The lines.</returns>
    private static List<string> BuildEnvironmentLines(bool windows, string? logPath, IReadOnlyList<string> names)
    {
        var lines = new List<string>();
        if (logPath is null)
        {
            return lines;
        }

        foreach (var name in names)
        {
            if (windows)
            {
                lines.Add($"if defined {name} >>\"{logPath}\" echo {name}=%{name}%");
                lines.Add($"if not defined {name} >>\"{logPath}\" echo {name}=^<unset^>");
            }
            else
            {
                lines.Add($"echo \"{name}=${{{name}-<unset>}}\" >> \"{logPath}\"");
            }
        }

        return lines;
    }

    /// <summary>
    ///     A canned answer of the stub to one more git subcommand (<c>for-each-ref</c>, <c>remote</c>,
    ///     <c>switch</c>, <c>clone</c>, <c>rev-parse --short</c>), for the commands the fixed
    ///     parameters of <see cref="Create"/> do not cover.
    /// </summary>
    /// <param name="First">The subcommand (the first argument).</param>
    /// <param name="Second">The second argument that must match too (for example <c>--short</c>), or
    ///     <see langword="null"/> to match on the subcommand alone.</param>
    /// <param name="Output">Text the stub prints to standard output; it may have several lines and any
    ///     characters, because it is read from a file.</param>
    /// <param name="Error">Text the stub prints to standard error.</param>
    /// <param name="ExitCode">The exit code the stub returns.</param>
    /// <param name="SleepSeconds">How many seconds the stub sleeps before it answers (then appends to
    ///     <see cref="FinishedMarkerPath"/>); 0 answers at once.</param>
    /// <param name="CreatesDestination">When <see langword="true"/> the stub first creates the folder
    ///     named by its fourth argument, with a read-only file in <c>.git/objects/pack</c>, like a clone
    ///     that was started: <c>git clone -- &lt;url&gt; &lt;destination&gt;</c>.</param>
    public sealed record Command(
        string First,
        string? Second = null,
        string Output = "",
        string Error = "",
        int ExitCode = 0,
        int SleepSeconds = 0,
        bool CreatesDestination = false);

    /// <summary>
    ///     A <see cref="Command"/> whose output texts were written to files the script prints.
    /// </summary>
    /// <param name="Command">The command.</param>
    /// <param name="OutputPath">The file with the standard output text, or <see langword="null"/> for
    ///     none.</param>
    /// <param name="ErrorPath">The file with the standard error text, or <see langword="null"/> for
    ///     none.</param>
    private sealed record PreparedCommand(Command Command, string? OutputPath, string? ErrorPath);

    /// <summary>
    ///     Writes the output texts of the extra commands to files next to the stub, so any characters
    ///     (several lines, <c>|</c>, <c>%</c>) reach git's caller unchanged.
    /// </summary>
    /// <param name="stubPath">The stub script's path.</param>
    /// <param name="commands">The extra commands.</param>
    /// <returns>The commands with their files.</returns>
    private static List<PreparedCommand> PrepareCommands(string stubPath, IReadOnlyList<Command> commands)
    {
        var prepared = new List<PreparedCommand>();
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            string? outputPath = null;
            string? errorPath = null;
            if (command.Output.Length > 0)
            {
                outputPath = $"{stubPath}.{index}.out";
                File.WriteAllText(outputPath, command.Output);
            }

            if (command.Error.Length > 0)
            {
                errorPath = $"{stubPath}.{index}.err";
                File.WriteAllText(errorPath, command.Error);
            }

            prepared.Add(new PreparedCommand(command, outputPath, errorPath));
        }

        return prepared;
    }

    /// <summary>
    ///     Deletes the stub script file.
    /// </summary>
    public void Delete()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }

        if (File.Exists(FinishedMarkerPath))
        {
            File.Delete(FinishedMarkerPath);
        }

        foreach (var sidecar in _sidecars.Where(File.Exists))
        {
            File.Delete(sidecar);
        }
    }

    /// <summary>
    ///     Gets a value indicating whether a command that sleeps got as far as its post-sleep marker,
    ///     i.e. was not killed while hanging.
    /// </summary>
    /// <returns><see langword="true"/> when the marker was written.</returns>
    public bool WroteFinishedMarker() =>
        File.Exists(FinishedMarkerPath) &&
        File.ReadAllLines(FinishedMarkerPath).Any(line => line.Trim() == FinishedMarker);

    /// <summary>
    ///     Waits as long as a command answered with a <see cref="HangSeconds"/> sleep takes to finish on
    ///     its own, then reports whether one got as far as its post-sleep marker. The command started
    ///     before this is called, so one that was not killed has written the marker by the time the
    ///     wait ends.
    /// </summary>
    /// <returns><see langword="true"/> when the marker was written.</returns>
    public async Task<bool> RanToTheEndOfItsHangAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(HangSeconds), TestContext.Current.CancellationToken);
        return WroteFinishedMarker();
    }

    /// <summary>
    ///     How long each command that can hang sleeps before it answers.
    /// </summary>
    /// <param name="Status">Seconds for <c>status --porcelain</c>.</param>
    /// <param name="Pull">Seconds for <c>pull</c>.</param>
    /// <param name="RevParseHead">Seconds for <c>rev-parse HEAD</c>.</param>
    /// <param name="StatusOutputHolder">Seconds the background copy started by
    ///     <c>status --porcelain</c> holds the output open; 0 starts none.</param>
    private sealed record StubSleeps(int Status, int Pull, int RevParseHead, int StatusOutputHolder);

    /// <summary>
    ///     Builds the batch lines that make a command sleep and then record that it finished.
    /// </summary>
    /// <param name="seconds">How long to sleep; 0 adds nothing.</param>
    /// <param name="markerPath">The file the finished marker is appended to.</param>
    /// <returns>The lines.</returns>
    private static IEnumerable<string> BatchSleep(int seconds, string markerPath)
    {
        if (seconds <= 0)
        {
            yield break;
        }

        // ping waits one second between echo requests, so N + 1 requests wait N seconds
        yield return $"ping -n {seconds + 1} 127.0.0.1 >nul";
        yield return $">>\"{markerPath}\" echo {FinishedMarker}";
    }

    /// <summary>
    ///     Builds the shell line that makes a command sleep and then record that it finished.
    /// </summary>
    /// <param name="seconds">How long to sleep; 0 adds nothing.</param>
    /// <param name="markerPath">The file the finished marker is appended to.</param>
    /// <returns>The lines.</returns>
    private static IEnumerable<string> ShellSleep(int seconds, string markerPath)
    {
        if (seconds <= 0)
        {
            yield break;
        }

        yield return $"    sleep {seconds}; echo {FinishedMarker} >> \"{markerPath}\"";
    }

    /// <summary>
    ///     Builds the Windows batch script body dispatching on the first two arguments.
    /// </summary>
    private static string BuildBatchScript(
        string statusOutput, string statusError, int statusExitCode,
        string pullOutput, string pullError, int pullExitCode,
        string revParseHeadOutput, int revParseHeadExitCode, string revParseHeadError,
        string abbrevRefOutput, int abbrevRefExitCode,
        string lsFilesOutput, int lsFilesExitCode, string lsFilesError,
        string? invocationLogPath, StubSleeps sleeps, string markerPath,
        IReadOnlyList<PreparedCommand> extraCommands, string? argumentLogPath, IReadOnlyList<string> environmentLines)
    {
        var lines = new List<string> { "@echo off" };
        if (invocationLogPath is not null)
        {
            lines.Add($"echo %1 %2>>\"{invocationLogPath}\"");
        }

        if (argumentLogPath is not null)
        {
            // Two stub processes can append at the same moment (the refresh queue runs two reads at once);
            // cmd's append then fails for one of them, so retry rather than lose the line
            lines.Add(":agentcontrol_argument_log");
            lines.Add($"2>nul >>\"{argumentLogPath}\" echo %* || goto agentcontrol_argument_log");
        }

        lines.AddRange(environmentLines);

        for (var index = 0; index < extraCommands.Count; index++)
        {
            var command = extraCommands[index].Command;
            var second = command.Second is null ? string.Empty : $" if \"%2\"==\"{command.Second}\"";
            lines.Add($"if \"%1\"==\"{command.First}\"{second} goto :extra{index}");
        }

        lines.AddRange(
        [
            "if \"%1\"==\"__holder\" goto :holder",
            "if \"%1\"==\"status\" goto :status",
            "if \"%1\"==\"pull\" goto :pull",
            "if \"%1\"==\"ls-files\" goto :lsfiles",
            "if \"%1\"==\"rev-parse\" if \"%2\"==\"HEAD\" goto :revparsehead",
            "if \"%1\"==\"rev-parse\" if \"%2\"==\"--abbrev-ref\" goto :abbrevref",
            "exit /b 127",
            ":holder"
        ]);
        lines.AddRange(BatchSleep(sleeps.StatusOutputHolder, markerPath));
        lines.Add("exit /b 0");
        lines.Add(":status");
        if (sleeps.StatusOutputHolder > 0)
        {
            // A background copy that inherits this stub's output and keeps it open after it exits
            lines.Add("start \"\" /b cmd /c call \"%~f0\" __holder");
        }

        lines.AddRange(BatchSleep(sleeps.Status, markerPath));
        if (!string.IsNullOrEmpty(statusOutput))
        {
            lines.Add($"echo {statusOutput}");
        }

        if (!string.IsNullOrEmpty(statusError))
        {
            lines.Add($"echo {statusError} 1>&2");
        }

        lines.Add($"exit /b {statusExitCode}");
        lines.Add(":pull");
        lines.AddRange(BatchSleep(sleeps.Pull, markerPath));
        if (!string.IsNullOrEmpty(pullOutput))
        {
            lines.Add($"echo {pullOutput}");
        }

        if (!string.IsNullOrEmpty(pullError))
        {
            lines.Add($"echo {pullError} 1>&2");
        }

        lines.Add($"exit /b {pullExitCode}");
        lines.Add(":revparsehead");
        lines.AddRange(BatchSleep(sleeps.RevParseHead, markerPath));
        if (!string.IsNullOrEmpty(revParseHeadOutput))
        {
            lines.Add($"echo {revParseHeadOutput}");
        }

        if (!string.IsNullOrEmpty(revParseHeadError))
        {
            lines.Add($"echo {revParseHeadError} 1>&2");
        }

        lines.Add($"exit /b {revParseHeadExitCode}");
        lines.Add(":abbrevref");
        if (!string.IsNullOrEmpty(abbrevRefOutput))
        {
            lines.Add($"echo {abbrevRefOutput}");
        }

        lines.Add($"exit /b {abbrevRefExitCode}");
        lines.Add(":lsfiles");
        if (!string.IsNullOrEmpty(lsFilesOutput))
        {
            lines.Add($"echo {lsFilesOutput}");
        }

        if (!string.IsNullOrEmpty(lsFilesError))
        {
            lines.Add($"echo {lsFilesError} 1>&2");
        }

        lines.Add($"exit /b {lsFilesExitCode}");
        for (var index = 0; index < extraCommands.Count; index++)
        {
            var (command, outputPath, errorPath) = extraCommands[index];
            lines.Add($":extra{index}");
            if (command.CreatesDestination)
            {
                // What a started clone leaves: the folder and a read-only pack file in it
                lines.Add("if not exist \"%~4\" mkdir \"%~4\"");
                lines.Add("mkdir \"%~4\\.git\\objects\\pack\" 2>nul");
                lines.Add(">\"%~4\\.git\\objects\\pack\\pack-1.pack\" echo x");
                lines.Add("attrib +R \"%~4\\.git\\objects\\pack\\pack-1.pack\"");
            }

            lines.AddRange(BatchSleep(command.SleepSeconds, markerPath));
            if (outputPath is not null)
            {
                lines.Add($"type \"{outputPath}\"");
            }

            if (errorPath is not null)
            {
                lines.Add($"type \"{errorPath}\" 1>&2");
            }

            lines.Add($"exit /b {command.ExitCode}");
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    ///     Builds the POSIX shell script body dispatching on the first two arguments.
    /// </summary>
    private static string BuildShellScript(
        string statusOutput, string statusError, int statusExitCode,
        string pullOutput, string pullError, int pullExitCode,
        string revParseHeadOutput, int revParseHeadExitCode, string revParseHeadError,
        string abbrevRefOutput, int abbrevRefExitCode,
        string lsFilesOutput, int lsFilesExitCode, string lsFilesError,
        string? invocationLogPath, StubSleeps sleeps, string markerPath,
        IReadOnlyList<PreparedCommand> extraCommands, string? argumentLogPath, IReadOnlyList<string> environmentLines)
    {
        var lines = new List<string> { "#!/bin/sh" };
        if (invocationLogPath is not null)
        {
            lines.Add($"echo \"$1 $2\" >> \"{invocationLogPath}\"");
        }

        if (argumentLogPath is not null)
        {
            lines.Add($"echo \"$*\" >> \"{argumentLogPath}\"");
        }

        lines.AddRange(environmentLines);

        lines.Add("case \"$1 $2\" in");
        for (var index = 0; index < extraCommands.Count; index++)
        {
            var (command, outputPath, errorPath) = extraCommands[index];
            lines.Add(command.Second is null ? $"  \"{command.First} \"*)" : $"  \"{command.First} {command.Second}\")");
            if (command.CreatesDestination)
            {
                // What a started clone leaves: the folder and a read-only pack file in it
                lines.Add("    mkdir -p \"$4/.git/objects/pack\"");
                lines.Add("    echo x > \"$4/.git/objects/pack/pack-1.pack\"");
                lines.Add("    chmod a-w \"$4/.git/objects/pack/pack-1.pack\"");
            }

            lines.AddRange(ShellSleep(command.SleepSeconds, markerPath));
            if (outputPath is not null)
            {
                lines.Add($"    cat \"{outputPath}\"");
            }

            if (errorPath is not null)
            {
                lines.Add($"    cat \"{errorPath}\" 1>&2");
            }

            lines.Add($"    exit {command.ExitCode}");
            lines.Add("    ;;");
        }

        lines.Add("  \"__holder \")");
        lines.AddRange(ShellSleep(sleeps.StatusOutputHolder, markerPath));
        lines.Add("    exit 0");
        lines.Add("    ;;");
        lines.Add("  \"status --porcelain\")");
        if (sleeps.StatusOutputHolder > 0)
        {
            // A background copy that inherits this stub's output and keeps it open after it exits
            lines.Add("    \"$0\" __holder &");
        }

        lines.AddRange(ShellSleep(sleeps.Status, markerPath));
        if (!string.IsNullOrEmpty(statusOutput))
        {
            lines.Add($"    echo \"{statusOutput}\"");
        }

        if (!string.IsNullOrEmpty(statusError))
        {
            lines.Add($"    echo \"{statusError}\" 1>&2");
        }

        lines.Add($"    exit {statusExitCode}");
        lines.Add("    ;;");
        lines.Add("  \"pull \")");
        lines.AddRange(ShellSleep(sleeps.Pull, markerPath));
        if (!string.IsNullOrEmpty(pullOutput))
        {
            lines.Add($"    echo \"{pullOutput}\"");
        }

        if (!string.IsNullOrEmpty(pullError))
        {
            lines.Add($"    echo \"{pullError}\" 1>&2");
        }

        lines.Add($"    exit {pullExitCode}");
        lines.Add("    ;;");
        lines.Add("  \"rev-parse HEAD\")");
        lines.AddRange(ShellSleep(sleeps.RevParseHead, markerPath));
        if (!string.IsNullOrEmpty(revParseHeadOutput))
        {
            lines.Add($"    echo \"{revParseHeadOutput}\"");
        }

        if (!string.IsNullOrEmpty(revParseHeadError))
        {
            lines.Add($"    echo \"{revParseHeadError}\" 1>&2");
        }

        lines.Add($"    exit {revParseHeadExitCode}");
        lines.Add("    ;;");
        lines.Add("  \"rev-parse --abbrev-ref\")");
        if (!string.IsNullOrEmpty(abbrevRefOutput))
        {
            lines.Add($"    echo \"{abbrevRefOutput}\"");
        }

        lines.Add($"    exit {abbrevRefExitCode}");
        lines.Add("    ;;");
        lines.Add("  \"ls-files --\")");
        if (!string.IsNullOrEmpty(lsFilesOutput))
        {
            lines.Add($"    echo \"{lsFilesOutput}\"");
        }

        if (!string.IsNullOrEmpty(lsFilesError))
        {
            lines.Add($"    echo \"{lsFilesError}\" 1>&2");
        }

        lines.Add($"    exit {lsFilesExitCode}");
        lines.Add("    ;;");
        lines.Add("  *)");
        lines.Add("    exit 127");
        lines.Add("    ;;");
        lines.Add("esac");
        return string.Join("\n", lines) + "\n";
    }
}
