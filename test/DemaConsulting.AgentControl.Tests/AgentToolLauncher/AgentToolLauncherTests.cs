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
///     process is ever spawned — exactly the isolation architecture.md calls for. The existing
///     tests use the default (keep the shell open); the <c>KeepShellOpen</c> tests pin both
///     values of the keep-open choice.
/// </remarks>
public class AgentToolLauncherTests
{
    /// <summary>
    ///     Test that asking explicitly to keep the shell open gives PowerShell Core the -NoExit
    ///     arguments, the same as the default.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellCore_KeepShellOpenTrue_HasNoExit()
    {
        // Arrange: a detected PowerShell Core shell
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info asking for the shell to stay open
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "copilot", @"C:\repos\example", keepShellOpen: true);

        // Assert: -NoExit is present and the result equals the default
        Assert.Equal(["-NoLogo", "-NoExit", "-Command", "copilot"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that asking explicitly to keep the shell open gives Windows PowerShell the -NoExit
    ///     arguments.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_WindowsPowerShell_KeepShellOpenTrue_HasNoExit()
    {
        // Arrange: a detected Windows PowerShell 5.x shell
        var shell = new DetectedShell(ShellKind.WindowsPowerShell, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        // Act: build the start info asking for the shell to stay open
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "cursor-agent", @"C:\repos\example", keepShellOpen: true);

        // Assert: -NoExit is present
        Assert.Equal(["-NoLogo", "-NoExit", "-Command", "cursor-agent"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that asking explicitly to keep the shell open gives cmd.exe the /K flag.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_Cmd_KeepShellOpenTrue_UsesSlashK()
    {
        // Arrange: a detected cmd.exe shell
        var shell = new DetectedShell(ShellKind.Cmd, @"C:\Windows\System32\cmd.exe");

        // Act: build the start info asking for the shell to stay open
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "claude", @"C:\repos\example", keepShellOpen: true);

        // Assert: /K keeps the window open
        Assert.Equal(["/K", "claude"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that not keeping the shell open leaves -NoExit out for PowerShell Core, so the
    ///     shell ends with the command.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellCore_KeepShellOpenFalse_OmitsNoExit()
    {
        // Arrange: a detected PowerShell Core shell
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info asking for the shell to end with the command
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "copilot", @"C:\repos\example", keepShellOpen: false);

        // Assert: no -NoExit, everything else as before
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", startInfo.FileName);
        Assert.Equal(@"C:\repos\example", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["-NoLogo", "-Command", "copilot"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that not keeping the shell open leaves -NoExit out for Windows PowerShell.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_WindowsPowerShell_KeepShellOpenFalse_OmitsNoExit()
    {
        // Arrange: a detected Windows PowerShell 5.x shell
        var shell = new DetectedShell(ShellKind.WindowsPowerShell, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        // Act: build the start info asking for the shell to end with the command
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "cursor-agent", @"C:\repos\example", keepShellOpen: false);

        // Assert: no -NoExit
        Assert.Equal(["-NoLogo", "-Command", "cursor-agent"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that not keeping the shell open makes cmd.exe run the command with /C, so the
    ///     shell ends with the command.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_Cmd_KeepShellOpenFalse_UsesSlashC()
    {
        // Arrange: a detected cmd.exe shell
        var shell = new DetectedShell(ShellKind.Cmd, @"C:\Windows\System32\cmd.exe");

        // Act: build the start info asking for the shell to end with the command
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "claude", @"C:\repos\example", keepShellOpen: false);

        // Assert: /C runs the command and ends the shell
        Assert.Equal(["/C", "claude"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a POSIX shell is unaffected by the keep-open choice.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_Posix_KeepShellOpenFalse_IsUnchanged()
    {
        // Arrange: a detected POSIX shell
        var shell = new DetectedShell(ShellKind.Posix, "/bin/bash");

        // Act: build the start info asking for the shell to end with the command
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "copilot", "/home/user/repos/example", keepShellOpen: false);

        // Assert: -c, exactly as with the default
        Assert.Equal(["-c", "copilot"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a quoted-path command still gets the PowerShell call operator when the shell
    ///     is not kept open.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellQuotedPath_KeepShellOpenFalse_StillPrefixesCallOperator()
    {
        // Arrange: a detected PowerShell Core shell and a quoted path with a flag
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info asking for the shell to end with the command
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "\"C:\\Program Files\\Tool\\tool.exe\" --flag", @"C:\repos\example", keepShellOpen: false);

        // Assert: no -NoExit, and the call operator is still applied so the tool is executed
        Assert.Equal(
            ["-NoLogo", "-Command", "& \"C:\\Program Files\\Tool\\tool.exe\" --flag"],
            startInfo.ArgumentList);
    }

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
    ///     Test that a PowerShell Core command beginning with a double-quoted executable path and
    ///     arguments gets the call operator, so the tool is executed rather than echoed.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellCoreQuotedPathWithArguments_PrefixesCallOperator()
    {
        // Arrange: a detected PowerShell Core shell and a quoted path (with spaces) plus a flag
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "\"C:\\Program Files\\Tool\\tool.exe\" --flag", @"C:\repos\example");

        // Assert: the command is invoked with the call operator
        Assert.Equal(
            ["-NoLogo", "-NoExit", "-Command", "& \"C:\\Program Files\\Tool\\tool.exe\" --flag"],
            startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a Windows PowerShell command beginning with a double-quoted executable path
    ///     and arguments gets the call operator.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_WindowsPowerShellQuotedPathWithArguments_PrefixesCallOperator()
    {
        // Arrange: a detected Windows PowerShell 5.x shell
        var shell = new DetectedShell(ShellKind.WindowsPowerShell, @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "\"C:\\Program Files\\Tool\\tool.exe\" --flag", @"C:\repos\example");

        // Assert: the command is invoked with the call operator
        Assert.Equal(
            ["-NoLogo", "-NoExit", "-Command", "& \"C:\\Program Files\\Tool\\tool.exe\" --flag"],
            startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a PowerShell command that is just a quoted executable path (no arguments)
    ///     gets the call operator.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellQuotedPathOnly_PrefixesCallOperator()
    {
        // Arrange: a detected PowerShell Core shell and a command that is only a quoted path
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "\"C:\\Program Files\\Tool\\tool.exe\"", @"C:\repos\example");

        // Assert: the lone quoted path is invoked with the call operator
        Assert.Equal(
            ["-NoLogo", "-NoExit", "-Command", "& \"C:\\Program Files\\Tool\\tool.exe\""],
            startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a PowerShell command beginning with a single-quoted executable path gets the
    ///     call operator.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellSingleQuotedPath_PrefixesCallOperator()
    {
        // Arrange: a detected PowerShell Core shell and a single-quoted path plus a flag
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "'C:\\Program Files\\Tool\\tool.exe' --flag", @"C:\repos\example");

        // Assert: the command is invoked with the call operator
        Assert.Equal(
            ["-NoLogo", "-NoExit", "-Command", "& 'C:\\Program Files\\Tool\\tool.exe' --flag"],
            startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that leading whitespace before a quoted path is ignored when deciding to add the
    ///     call operator, and that the operator is still added.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellLeadingWhitespaceBeforeQuote_PrefixesCallOperator()
    {
        // Arrange: a detected PowerShell Core shell and a quoted path preceded by whitespace
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "  \t\"C:\\Program Files\\Tool\\tool.exe\" --flag", @"C:\repos\example");

        // Assert: the call operator replaces the leading whitespace
        Assert.Equal(
            ["-NoLogo", "-NoExit", "-Command", "& \"C:\\Program Files\\Tool\\tool.exe\" --flag"],
            startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a PowerShell command that already starts with the call operator or the
    ///     dot-source operator is passed through verbatim (never double-prefixed).
    /// </summary>
    /// <param name="command">The command that already carries an invocation operator.</param>
    [Theory]
    [InlineData("& \"C:\\Program Files\\Tool\\tool.exe\" --flag")]
    [InlineData("&\"C:\\Program Files\\Tool\\tool.exe\" --flag")]
    [InlineData("  & 'C:\\Program Files\\Tool\\tool.exe' --flag")]
    [InlineData(". \"C:\\Program Files\\Tool\\tool.ps1\"")]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellCommandAlreadyHasOperator_IsNotDoublePrefixed(
        string command)
    {
        // Arrange: a detected PowerShell Core shell
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, command, @"C:\repos\example");

        // Assert: the command is passed to PowerShell exactly as supplied
        Assert.Equal(["-NoLogo", "-NoExit", "-Command", command], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that an unquoted PowerShell command, including one whose quote is not the first
    ///     character, is passed through unchanged.
    /// </summary>
    /// <param name="command">The unquoted-start command.</param>
    [Theory]
    [InlineData("copilot")]
    [InlineData("copilot --model \"gpt 5\"")]
    [InlineData(@"C:\Tools\tool.exe --flag")]
    public void AgentToolLauncher_BuildProcessStartInfo_PowerShellUnquotedCommand_IsUnchanged(string command)
    {
        // Arrange: a detected PowerShell Core shell
        var shell = new DetectedShell(ShellKind.PowerShellCore, @"C:\Program Files\PowerShell\7\pwsh.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, command, @"C:\repos\example");

        // Assert: no call operator is added
        Assert.Equal(["-NoLogo", "-NoExit", "-Command", command], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a quoted-path command for cmd.exe is passed through without a call operator.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_CmdQuotedPath_IsUnchanged()
    {
        // Arrange: a detected cmd.exe shell
        var shell = new DetectedShell(ShellKind.Cmd, @"C:\Windows\System32\cmd.exe");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "\"C:\\Program Files\\Tool\\tool.exe\" --flag", @"C:\repos\example");

        // Assert: cmd executes a quoted path directly, so the command is untouched
        Assert.Equal(["/K", "\"C:\\Program Files\\Tool\\tool.exe\" --flag"], startInfo.ArgumentList);
    }

    /// <summary>
    ///     Test that a quoted-path command for a POSIX shell is passed through without a call
    ///     operator.
    /// </summary>
    [Fact]
    public void AgentToolLauncher_BuildProcessStartInfo_PosixQuotedPath_IsUnchanged()
    {
        // Arrange: a detected POSIX shell
        var shell = new DetectedShell(ShellKind.Posix, "/bin/bash");

        // Act: build the start info
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "\"/opt/my tool/tool\" --flag", "/home/user/repos/example");

        // Assert: POSIX shells execute a quoted path directly, so the command is untouched
        Assert.Equal(["-c", "\"/opt/my tool/tool\" --flag"], startInfo.ArgumentList);
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
