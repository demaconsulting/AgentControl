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

namespace DemaConsulting.AgentControl.AgentToolLauncher;

/// <summary>
///     Detects the best available shell/terminal to launch the configured agentic CLI tool in,
///     preferring the highest installed PowerShell.
/// </summary>
/// <remarks>
///     Per architecture.md's <c>AgentToolLauncher</c> subsystem description, detection order on
///     Windows is: highest installed PowerShell (<c>pwsh</c> on <c>PATH</c> or well-known install
///     locations) &gt; Windows PowerShell 5.x &gt; <c>cmd.exe</c>. On macOS/Linux, the user's
///     default shell (<c>$SHELL</c>, falling back to <c>/bin/sh</c>) is used directly since
///     PowerShell is not assumed to be installed there. All environment probing (PATH resolution,
///     file existence, environment variables, and even which OS is "current") is injected via
///     constructor delegates so tests can exercise every branch deterministically without
///     depending on what is actually installed on the machine running the tests. Stateless
///     (beyond its injected delegates) and thread-safe.
/// </remarks>
internal sealed class ShellDetector
{
    /// <summary>
    ///     Well-known PowerShell 7+ install locations checked when <c>pwsh</c> cannot be resolved
    ///     via <c>PATH</c>.
    /// </summary>
    /// <remarks>
    ///     These are genuine, fixed Windows install locations for PowerShell 7+ MSI/EXE installs
    ///     (not environment-relative paths), so hardcoding them is intentional rather than a
    ///     configuration smell; they are only consulted after the <c>PATH</c>-based lookup fails.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1075:Refactor your code not to use hardcoded absolute paths or URIs.",
        Justification = "Fixed, well-known PowerShell 7+ install locations used only as a fallback after PATH resolution fails.")]
    private static readonly string[] WellKnownPwshPaths =
    [
        @"C:\Program Files\PowerShell\7\pwsh.exe",
        @"C:\Program Files\PowerShell\7-preview\pwsh.exe"
    ];

    /// <summary>
    ///     Well-known Windows PowerShell 5.x install location checked when <c>powershell.exe</c>
    ///     cannot be resolved via <c>PATH</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1075:Refactor your code not to use hardcoded absolute paths or URIs.",
        Justification = "Fixed, well-known Windows PowerShell 5.x install location used only as a fallback after PATH resolution fails.")]
    private static readonly string[] WellKnownWindowsPowerShellPaths =
    [
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    ];

    /// <summary>
    ///     Fallback command name for the Windows command interpreter, used when it cannot be
    ///     resolved via <c>PATH</c> (it always can be in practice, since it ships with Windows).
    /// </summary>
    private const string DefaultCmdExecutable = "cmd.exe";

    /// <summary>
    ///     Fallback POSIX shell used when the <c>$SHELL</c> environment variable is unset.
    /// </summary>
    private const string DefaultPosixShell = "/bin/sh";

    /// <summary>
    ///     Resolves a bare executable name to a full path via <c>PATH</c>, or returns
    ///     <see langword="null"/> if not found.
    /// </summary>
    private readonly Func<string, string?> _resolveOnPath;

    /// <summary>
    ///     Checks whether a file exists at an absolute path.
    /// </summary>
    private readonly Func<string, bool> _fileExists;

    /// <summary>
    ///     Retrieves the user's default POSIX shell (typically from the <c>$SHELL</c> environment
    ///     variable).
    /// </summary>
    private readonly Func<string?> _getShellEnvironmentVariable;

    /// <summary>
    ///     Indicates whether detection should follow the Windows branch (PowerShell/cmd) or the
    ///     POSIX branch (<c>$SHELL</c>).
    /// </summary>
    private readonly bool _isWindows;

    /// <summary>
    ///     Initializes a new <see cref="ShellDetector"/>.
    /// </summary>
    /// <param name="resolveOnPath">
    ///     Resolves a bare executable name to a full path via <c>PATH</c>, or <see langword="null"/>
    ///     when it cannot be found. Defaults to a real <c>PATH</c>-scanning implementation. Tests
    ///     supply a fake to simulate specific shells being "installed" without depending on the
    ///     test machine's actual configuration.
    /// </param>
    /// <param name="fileExists">
    ///     Checks whether a file exists at an absolute path. Defaults to <see cref="File.Exists(string)"/>.
    /// </param>
    /// <param name="getShellEnvironmentVariable">
    ///     Retrieves the user's default POSIX shell. Defaults to reading the <c>$SHELL</c>
    ///     environment variable.
    /// </param>
    /// <param name="isWindows">
    ///     Selects the Windows or POSIX detection branch. Defaults to <see cref="OperatingSystem.IsWindows"/>.
    /// </param>
    public ShellDetector(
        Func<string, string?>? resolveOnPath = null,
        Func<string, bool>? fileExists = null,
        Func<string?>? getShellEnvironmentVariable = null,
        bool? isWindows = null)
    {
        _resolveOnPath = resolveOnPath ?? DefaultResolveOnPath;
        _fileExists = fileExists ?? File.Exists;
        _getShellEnvironmentVariable =
            getShellEnvironmentVariable ?? (() => Environment.GetEnvironmentVariable("SHELL"));
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
    }

    /// <summary>
    ///     Detects the best available shell to launch the agent tool in.
    /// </summary>
    /// <returns>The detected shell and the path used to launch it.</returns>
    public DetectedShell Detect()
    {
        return _isWindows ? DetectWindowsShell() : DetectPosixShell();
    }

    /// <summary>
    ///     Runs the Windows detection order: highest installed PowerShell, then Windows
    ///     PowerShell 5.x, then <c>cmd.exe</c> as the final fallback.
    /// </summary>
    /// <returns>The detected shell.</returns>
    private DetectedShell DetectWindowsShell()
    {
        // Prefer PowerShell 7+ resolvable on PATH
        var pwshOnPath = _resolveOnPath("pwsh.exe");
        if (pwshOnPath is not null)
        {
            return new DetectedShell(ShellKind.PowerShellCore, pwshOnPath);
        }

        // Fall back to well-known PowerShell 7+ install locations
        var pwshWellKnown = WellKnownPwshPaths.FirstOrDefault(_fileExists);
        if (pwshWellKnown is not null)
        {
            return new DetectedShell(ShellKind.PowerShellCore, pwshWellKnown);
        }

        // Fall back to Windows PowerShell 5.x resolvable on PATH
        var legacyOnPath = _resolveOnPath("powershell.exe");
        if (legacyOnPath is not null)
        {
            return new DetectedShell(ShellKind.WindowsPowerShell, legacyOnPath);
        }

        // Fall back to the well-known Windows PowerShell 5.x install location
        var legacyWellKnown = WellKnownWindowsPowerShellPaths.FirstOrDefault(_fileExists);
        if (legacyWellKnown is not null)
        {
            return new DetectedShell(ShellKind.WindowsPowerShell, legacyWellKnown);
        }

        // Final fallback: cmd.exe, which ships with every supported Windows version
        var cmdOnPath = _resolveOnPath(DefaultCmdExecutable);
        return new DetectedShell(ShellKind.Cmd, cmdOnPath ?? DefaultCmdExecutable);
    }

    /// <summary>
    ///     Uses the user's default POSIX shell (<c>$SHELL</c>, falling back to <c>/bin/sh</c>).
    /// </summary>
    /// <returns>The detected shell.</returns>
    private DetectedShell DetectPosixShell()
    {
        var shell = _getShellEnvironmentVariable();
        return new DetectedShell(ShellKind.Posix, string.IsNullOrWhiteSpace(shell) ? DefaultPosixShell : shell);
    }

    /// <summary>
    ///     Default <c>PATH</c>-scanning implementation used when no <c>resolveOnPath</c> delegate
    ///     is supplied.
    /// </summary>
    /// <param name="executableName">Bare executable file name to search for, e.g. <c>"pwsh.exe"</c>.</param>
    /// <returns>The full path if found on <c>PATH</c>; otherwise <see langword="null"/>.</returns>
    private static string? DefaultResolveOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entries (e.g. containing invalid path characters) are skipped
                // rather than treated as a detection failure.
            }
        }

        return null;
    }
}
