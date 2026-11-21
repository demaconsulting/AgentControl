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
///     matching every subcommand <c>GitClient</c> issues. On Windows this is a <c>.bat</c> file
///     (directly runnable by <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/>
///     without <c>UseShellExecute</c>); elsewhere it is a POSIX shell script marked executable via
///     <c>File.SetUnixFileMode</c>.
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
    ///     Private constructor - use <see cref="Create"/> instead.
    /// </summary>
    /// <param name="path">The created stub script's path.</param>
    private GitStub(string path)
    {
        Path = path;
    }

    /// <summary>
    ///     Creates a stub script with the given canned responses for the "status", "pull",
    ///     "rev-parse HEAD", "rev-parse --abbrev-ref HEAD", and "ls-files" subcommands.
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
        int revParseHeadSleepSeconds = 0)
    {
        var sleeps = new StubSleeps(statusSleepSeconds, pullSleepSeconds, revParseHeadSleepSeconds);
        if (OperatingSystem.IsWindows())
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"agentcontrol_git_stub_{Guid.NewGuid()}.bat");
            File.WriteAllText(path, BuildBatchScript(
                statusOutput, statusError, statusExitCode, pullOutput, pullError, pullExitCode,
                revParseHeadOutput, revParseHeadExitCode, revParseHeadError,
                abbrevRefOutput, abbrevRefExitCode,
                lsFilesOutput, lsFilesExitCode, lsFilesError, invocationLogPath, sleeps, path + ".finished"));
            return new GitStub(path);
        }
        else
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"agentcontrol_git_stub_{Guid.NewGuid()}.sh");
            File.WriteAllText(path, BuildShellScript(
                statusOutput, statusError, statusExitCode, pullOutput, pullError, pullExitCode,
                revParseHeadOutput, revParseHeadExitCode, revParseHeadError,
                abbrevRefOutput, abbrevRefExitCode,
                lsFilesOutput, lsFilesExitCode, lsFilesError, invocationLogPath, sleeps, path + ".finished"));
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return new GitStub(path);
        }
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
    private sealed record StubSleeps(int Status, int Pull, int RevParseHead);

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
        string? invocationLogPath, StubSleeps sleeps, string markerPath)
    {
        var lines = new List<string> { "@echo off" };
        if (invocationLogPath is not null)
        {
            lines.Add($"echo %1 %2>>\"{invocationLogPath}\"");
        }

        lines.AddRange(
        [
            "if \"%1\"==\"status\" goto :status",
            "if \"%1\"==\"pull\" goto :pull",
            "if \"%1\"==\"ls-files\" goto :lsfiles",
            "if \"%1\"==\"rev-parse\" if \"%2\"==\"HEAD\" goto :revparsehead",
            "if \"%1\"==\"rev-parse\" if \"%2\"==\"--abbrev-ref\" goto :abbrevref",
            "exit /b 127",
            ":status"
        ]);
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
        string? invocationLogPath, StubSleeps sleeps, string markerPath)
    {
        var lines = new List<string> { "#!/bin/sh" };
        if (invocationLogPath is not null)
        {
            lines.Add($"echo \"$1 $2\" >> \"{invocationLogPath}\"");
        }

        lines.Add("case \"$1 $2\" in");
        lines.Add("  \"status --porcelain\")");
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
