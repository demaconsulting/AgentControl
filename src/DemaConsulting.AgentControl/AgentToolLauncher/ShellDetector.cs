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
    ///     Detects the shell to launch the agent tool in: either the user's configured
    ///     <paramref name="shellPreference"/> (from <c>AppSettings.ShellPreference</c> /
    ///     <c>SettingsWindowViewModel.AvailableShellPreferences</c>), or - when that is
    ///     <see langword="null"/>, empty, or all-whitespace (the "auto-detect" choice) - the best
    ///     available shell for the current OS.
    /// </summary>
    /// <param name="shellPreference">
    ///     The user's shell preference: blank/<see langword="null"/> for auto-detection, one of
    ///     the recognized keywords for the current OS (<c>pwsh</c>/<c>powershell</c>/<c>cmd</c>
    ///     on Windows, <c>bash</c>/<c>zsh</c>/<c>sh</c> elsewhere - matched case-insensitively),
    ///     or an arbitrary custom shell executable name/path (e.g. a Git Bash install not on
    ///     <c>PATH</c>), which is launched using POSIX <c>-c</c> invocation semantics since that
    ///     is the broadly compatible convention for an arbitrary shell executable.
    /// </param>
    /// <returns>The detected shell and the path used to launch it.</returns>
    public DetectedShell Detect(string? shellPreference = null)
    {
        if (!string.IsNullOrWhiteSpace(shellPreference))
        {
            return DetectFromPreference(shellPreference.Trim());
        }

        return _isWindows ? DetectWindowsShell() : DetectPosixShell();
    }

    /// <summary>
    ///     Resolves a non-blank user shell preference to a <see cref="DetectedShell"/>, honoring
    ///     the recognized keywords for the current OS and falling back to launching the raw
    ///     preference text as a custom shell command for anything else.
    /// </summary>
    /// <param name="preference">The trimmed, non-blank shell preference text.</param>
    /// <returns>The resolved shell.</returns>
    private DetectedShell DetectFromPreference(string preference)
    {
        if (_isWindows)
        {
            if (string.Equals(preference, "pwsh", StringComparison.OrdinalIgnoreCase))
            {
                return new DetectedShell(ShellKind.PowerShellCore, ResolvePwshPath() ?? "pwsh.exe");
            }

            if (string.Equals(preference, "powershell", StringComparison.OrdinalIgnoreCase))
            {
                return new DetectedShell(ShellKind.WindowsPowerShell, ResolveWindowsPowerShellPath() ?? "powershell.exe");
            }

            if (string.Equals(preference, "cmd", StringComparison.OrdinalIgnoreCase))
            {
                return new DetectedShell(ShellKind.Cmd, _resolveOnPath(DefaultCmdExecutable) ?? DefaultCmdExecutable);
            }
        }
        else if (string.Equals(preference, "bash", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(preference, "zsh", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(preference, "sh", StringComparison.OrdinalIgnoreCase))
        {
            return new DetectedShell(ShellKind.Posix, _resolveOnPath(preference) ?? preference);
        }

        // An unrecognized preference is treated as a custom shell executable name/path supplied
        // directly by the user (the combo box remains editable for this reason), launched with
        // POSIX "-c" invocation semantics.
        return new DetectedShell(ShellKind.Posix, preference);
    }

    /// <summary>
    ///     Runs the Windows detection order: highest installed PowerShell, then Windows
    ///     PowerShell 5.x, then <c>cmd.exe</c> as the final fallback.
    /// </summary>
    /// <returns>The detected shell.</returns>
    private DetectedShell DetectWindowsShell()
    {
        // Prefer PowerShell 7+, whether resolvable on PATH or at a well-known install location
        var pwsh = ResolvePwshPath();
        if (pwsh is not null)
        {
            return new DetectedShell(ShellKind.PowerShellCore, pwsh);
        }

        // Fall back to Windows PowerShell 5.x, whether resolvable on PATH or well-known
        var legacy = ResolveWindowsPowerShellPath();
        if (legacy is not null)
        {
            return new DetectedShell(ShellKind.WindowsPowerShell, legacy);
        }

        // Final fallback: cmd.exe, which ships with every supported Windows version
        var cmdOnPath = _resolveOnPath(DefaultCmdExecutable);
        return new DetectedShell(ShellKind.Cmd, cmdOnPath ?? DefaultCmdExecutable);
    }

    /// <summary>
    ///     Resolves PowerShell 7+'s path, preferring <c>PATH</c> resolution and falling back to
    ///     <see cref="WellKnownPwshPaths"/>.
    /// </summary>
    /// <returns>The resolved path, or <see langword="null"/> if not found anywhere.</returns>
    private string? ResolvePwshPath() =>
        _resolveOnPath("pwsh.exe") ?? WellKnownPwshPaths.FirstOrDefault(_fileExists);

    /// <summary>
    ///     Resolves Windows PowerShell 5.x's path, preferring <c>PATH</c> resolution and falling
    ///     back to <see cref="WellKnownWindowsPowerShellPaths"/>.
    /// </summary>
    /// <returns>The resolved path, or <see langword="null"/> if not found anywhere.</returns>
    private string? ResolveWindowsPowerShellPath() =>
        _resolveOnPath("powershell.exe") ?? WellKnownWindowsPowerShellPaths.FirstOrDefault(_fileExists);

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
