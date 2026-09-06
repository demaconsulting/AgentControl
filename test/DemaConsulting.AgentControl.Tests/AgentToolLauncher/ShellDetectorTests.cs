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
}
