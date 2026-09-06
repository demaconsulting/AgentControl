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
///     Unit tests for the <c>AgentToolLauncher</c> class's <c>BuildProcessStartInfo</c> method.
/// </summary>
/// <remarks>
///     These tests only exercise <c>BuildProcessStartInfo</c>, never <c>Launch</c>, so no real
///     process is ever spawned — exactly the isolation architecture.md calls for.
/// </remarks>
public class AgentToolLauncherTests
{
    /// <summary>
    ///     Test that building start info for PowerShell Core produces the expected -NoExit
    ///     command invocation.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellCore_ProducesNoExitCommandArguments()
    {
        // Arrange: a detected PowerShell Core shell
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "copilot", @"C:\repos\example");

        // Assert: FileName/WorkingDirectory match, and arguments keep the window open
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", startInfo.FileName);
        Assert.Equal(@"C:\repos\example", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["-NoLogo", "-NoExit", "-Command", "copilot"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that building start info for Windows PowerShell 5.x produces the same -NoExit
    ///     command invocation shape as PowerShell Core.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_WindowsPowerShell_ProducesNoExitCommandArguments()
    {
        // Arrange: a detected Windows PowerShell 5.x shell
        var shell = new DetectedShell(ShellKind.WindowsPowerShell, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "cursor-agent", @"C:\repos\example");

        // Assert: same invocation shape as PowerShell Core
        Assert.Equal(["-NoLogo", "-NoExit", "-Command", "cursor-agent"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that building start info for cmd.exe uses the /K flag to keep the window open.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_Cmd_ProducesKeepOpenArguments()
    {
        // Arrange: a detected cmd.exe shell
        var shell = new DetectedShell(ShellKind.Cmd, @"C:\Windows\System32\cmd.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "claude", @"C:\repos\example");

        // Assert: /K keeps the cmd window open after the command runs
        Assert.Equal(["/K", "claude"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that building start info for a POSIX shell uses -c to run the command.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_Posix_ProducesDashCArgument()
    {
        // Arrange: a detected POSIX shell
        var shell = new DetectedShell(ShellKind.Posix, "/bin/bash");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "copilot", "/home/user/repos/example");

        // Assert: -c runs the command in the POSIX shell
        Assert.Equal(["-c", "copilot"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a null shell throws an ArgumentNullException.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_NullShell_ThrowsArgumentNullException()
    {
        // Act / Assert: a null shell is rejected
        Assert.Throws<ArgumentNullException>(() =>
            DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
                null!, "copilot", @"C:\repos\example"));
    }

    /// <summary>
    ///     Test that an empty command throws an ArgumentException.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_EmptyCommand_ThrowsArgumentException()
    {
        // Arrange: a valid shell
        var shell = new DetectedShell(ShellKind.Cmd, "cmd.exe");

        // Act / Assert: an empty command is rejected
        Assert.Throws<ArgumentException>(() =>
            DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
                shell, "", @"C:\repos\example"));
    }

    /// <summary>
    ///     Test that an empty working directory throws an ArgumentException.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_EmptyWorkingDirectory_ThrowsArgumentException()
    {
        // Arrange: a valid shell
        var shell = new DetectedShell(ShellKind.Cmd, "cmd.exe");

        // Act / Assert: an empty working directory is rejected
        Assert.Throws<ArgumentException>(() =>
            DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
                shell, "copilot", ""));
    }

    /// <summary>
    ///     Test that an unrecognized shell kind throws an ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_UnrecognizedShellKind_ThrowsArgumentOutOfRangeException()
    {
        // Arrange: a shell with an out-of-range enum value
        var shell = new DetectedShell((ShellKind)999, "unknown-shell");

        // Act / Assert: the unrecognized kind is rejected
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
                shell, "copilot", @"C:\repos\example"));
    }
}
