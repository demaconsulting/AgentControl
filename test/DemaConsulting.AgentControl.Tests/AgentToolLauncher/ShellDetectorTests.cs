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

using DemaConsulting.AgentControl.AgentToolLauncher;

namespace DemaConsulting.AgentControl.Tests.AgentToolLauncher;

/// <summary>
///     Unit tests for <see cref="ShellDetector"/>.
/// </summary>
/// <remarks>
///     Every test injects fake PATH-resolution, file-existence, environment-variable, and
///     OS-selector delegates so detection is fully deterministic regardless of what shells are
///     actually installed on the machine running the tests.
/// </remarks>
public class ShellDetectorTests
{
    /// <summary>
    ///     Test that pwsh resolvable on PATH is preferred over every other shell on Windows.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WindowsWithPwshOnPath_ReturnsPowerShellCore()
    {
        // Arrange: pwsh.exe resolves on PATH
        var detector = new ShellDetector(
            resolveOnPath: name => name == "pwsh.exe" ? @"C:\Users\test\AppData\pwsh.exe" : null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: pwsh on PATH wins
        Assert.Equal(ShellKind.PowerShellCore, shell.Kind);
        Assert.Equal(@"C:\Users\test\AppData\pwsh.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that a well-known pwsh install location is used when pwsh cannot be resolved via
    ///     PATH.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WindowsWithPwshAtWellKnownPath_ReturnsPowerShellCore()
    {
        // Arrange: pwsh is not on PATH but exists at the well-known 7.x install location
        var detector = new ShellDetector(
            resolveOnPath: _ => null,
            fileExists: path => path == @"C:\Program Files\PowerShell\7\pwsh.exe",
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: the well-known install location is used
        Assert.Equal(ShellKind.PowerShellCore, shell.Kind);
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that Windows PowerShell 5.x on PATH is used when no pwsh is available anywhere.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WindowsWithOnlyLegacyPowerShellOnPath_ReturnsWindowsPowerShell()
    {
        // Arrange: no pwsh anywhere, but powershell.exe resolves on PATH
        var detector = new ShellDetector(
            resolveOnPath: name => name == "powershell.exe" ? @"C:\Windows\powershell.exe" : null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: legacy PowerShell on PATH is used
        Assert.Equal(ShellKind.WindowsPowerShell, shell.Kind);
        Assert.Equal(@"C:\Windows\powershell.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that the well-known Windows PowerShell 5.x install location is used when neither
    ///     pwsh nor powershell.exe can be resolved via PATH.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WindowsWithLegacyAtWellKnownPathOnly_ReturnsWindowsPowerShell()
    {
        // Arrange: nothing resolves on PATH, but the well-known Windows PowerShell 5.x path exists
        var detector = new ShellDetector(
            resolveOnPath: _ => null,
            fileExists: path => path == @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: the well-known install location is used
        Assert.Equal(ShellKind.WindowsPowerShell, shell.Kind);
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that cmd.exe is the final fallback when no PowerShell is available anywhere.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WindowsWithNoPowerShellAvailable_ReturnsCmd()
    {
        // Arrange: nothing resolves anywhere except cmd.exe on PATH
        var detector = new ShellDetector(
            resolveOnPath: name => name == "cmd.exe" ? @"C:\Windows\System32\cmd.exe" : null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: cmd.exe is the final fallback
        Assert.Equal(ShellKind.Cmd, shell.Kind);
        Assert.Equal(@"C:\Windows\System32\cmd.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that cmd.exe still resolves to a usable path even if PATH resolution somehow
    ///     fails for it too (it always ships with Windows in practice).
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WindowsWithNothingResolvable_ReturnsCmdWithDefaultName()
    {
        // Arrange: nothing resolves and no well-known files exist
        var detector = new ShellDetector(
            resolveOnPath: _ => null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: cmd.exe is used by bare name as a last resort
        Assert.Equal(ShellKind.Cmd, shell.Kind);
        Assert.Equal("cmd.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that the $SHELL environment variable is used directly on non-Windows platforms.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_NonWindowsWithShellVariableSet_ReturnsPosixShell()
    {
        // Arrange: $SHELL is set to a custom shell
        var detector = new ShellDetector(
            resolveOnPath: _ => null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => "/usr/bin/zsh",
            isWindows: false);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: the configured default shell is used verbatim
        Assert.Equal(ShellKind.Posix, shell.Kind);
        Assert.Equal("/usr/bin/zsh", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that /bin/sh is used on non-Windows platforms when $SHELL is unset.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_NonWindowsWithShellVariableUnset_ReturnsDefaultPosixShell()
    {
        // Arrange: $SHELL is unset (null)
        var detector = new ShellDetector(
            resolveOnPath: _ => null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: false);

        // Act: detect the shell
        var shell = detector.Detect();

        // Assert: /bin/sh is the documented fallback
        Assert.Equal(ShellKind.Posix, shell.Kind);
        Assert.Equal("/bin/sh", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that a "pwsh" shell preference resolves PowerShell Core on Windows, overriding
    ///     auto-detection.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WithPwshPreference_ReturnsPowerShellCore()
    {
        // Arrange: pwsh resolves on PATH; nothing else matters since the preference is explicit
        var detector = new ShellDetector(
            resolveOnPath: name => name == "pwsh.exe" ? @"C:\Users\test\AppData\pwsh.exe" : null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect with an explicit "pwsh" preference (mixed case, to also verify
        // case-insensitive matching)
        var shell = detector.Detect("Pwsh");

        // Assert: PowerShell Core is used
        Assert.Equal(ShellKind.PowerShellCore, shell.Kind);
        Assert.Equal(@"C:\Users\test\AppData\pwsh.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that a "powershell" shell preference resolves Windows PowerShell 5.x, even when
    ///     pwsh would otherwise have been auto-detected.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WithPowershellPreference_ReturnsWindowsPowerShell()
    {
        // Arrange: both pwsh and powershell.exe resolve on PATH
        var detector = new ShellDetector(
            resolveOnPath: name => name switch
            {
                "pwsh.exe" => @"C:\pwsh.exe",
                "powershell.exe" => @"C:\Windows\powershell.exe",
                _ => null
            },
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect with an explicit "powershell" preference
        var shell = detector.Detect("powershell");

        // Assert: Windows PowerShell 5.x is used despite pwsh being available
        Assert.Equal(ShellKind.WindowsPowerShell, shell.Kind);
        Assert.Equal(@"C:\Windows\powershell.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that a "cmd" shell preference resolves the Command shell, even when PowerShell
    ///     would otherwise have been auto-detected.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WithCmdPreference_ReturnsCmd()
    {
        // Arrange: pwsh resolves on PATH, but the preference explicitly asks for cmd
        var detector = new ShellDetector(
            resolveOnPath: name => name switch
            {
                "pwsh.exe" => @"C:\pwsh.exe",
                "cmd.exe" => @"C:\Windows\System32\cmd.exe",
                _ => null
            },
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect with an explicit "cmd" preference
        var shell = detector.Detect("cmd");

        // Assert: cmd.exe is used despite pwsh being available
        Assert.Equal(ShellKind.Cmd, shell.Kind);
        Assert.Equal(@"C:\Windows\System32\cmd.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that a recognized POSIX keyword preference (e.g. "zsh") resolves via PATH on
    ///     non-Windows platforms.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WithPosixKeywordPreference_ReturnsPosixShell()
    {
        // Arrange: zsh resolves on PATH
        var detector = new ShellDetector(
            resolveOnPath: name => name == "zsh" ? "/usr/bin/zsh" : null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => "/bin/bash",
            isWindows: false);

        // Act: detect with an explicit "zsh" preference, overriding $SHELL
        var shell = detector.Detect("zsh");

        // Assert: zsh is used instead of the $SHELL-configured bash
        Assert.Equal(ShellKind.Posix, shell.Kind);
        Assert.Equal("/usr/bin/zsh", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that an unrecognized custom shell preference is launched directly, using POSIX
    ///     invocation semantics, rather than being rejected.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WithCustomShellPreference_ReturnsPosixShellWithThatPath()
    {
        // Arrange: a custom shell path not matching any recognized keyword
        var detector = new ShellDetector(
            resolveOnPath: _ => null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect with a custom Git Bash path as the preference
        var shell = detector.Detect(@"C:\Program Files\Git\bin\bash.exe");

        // Assert: the custom path is launched verbatim with POSIX "-c" semantics
        Assert.Equal(ShellKind.Posix, shell.Kind);
        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", shell.ExecutablePath);
    }

    /// <summary>
    ///     Test that a blank/whitespace-only shell preference falls back to ordinary
    ///     auto-detection rather than being treated as a custom shell.
    /// </summary>
    [Fact]
    public void ShellDetector_Detect_WithBlankPreference_FallsBackToAutoDetection()
    {
        // Arrange: pwsh resolves on PATH, auto-detection should find it
        var detector = new ShellDetector(
            resolveOnPath: name => name == "pwsh.exe" ? @"C:\pwsh.exe" : null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => null,
            isWindows: true);

        // Act: detect with a whitespace-only preference
        var shell = detector.Detect("   ");

        // Assert: auto-detection still runs and finds pwsh
        Assert.Equal(ShellKind.PowerShellCore, shell.Kind);
        Assert.Equal(@"C:\pwsh.exe", shell.ExecutablePath);
    }
}
