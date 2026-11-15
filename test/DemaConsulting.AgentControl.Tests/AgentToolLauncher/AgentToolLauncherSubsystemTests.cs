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
///     Subsystem tests for the AgentToolLauncher subsystem: shell detection, building the start
///     information and starting the process work together as the launch flow uses them.
/// </summary>
/// <remarks>
///     The process is started through a <see cref="FakeAgentToolProcessStarter"/>, so no process
///     is ever spawned.
/// </remarks>
public class AgentToolLauncherSubsystemTests
{
    /// <summary>
    ///     Test that PowerShell and the Command shell stay open after the tool when asked to and
    ///     end with the tool otherwise.
    /// </summary>
    /// <param name="preference">The shell preference to detect.</param>
    /// <param name="keepShellOpen">Whether the shell stays open after the tool exits.</param>
    /// <param name="expectedArguments">The arguments the started shell receives.</param>
    [Theory]
    [InlineData("pwsh", true, new[] { "-NoLogo", "-NoExit", "-Command", "copilot" })]
    [InlineData("pwsh", false, new[] { "-NoLogo", "-Command", "copilot" })]
    [InlineData("powershell", true, new[] { "-NoLogo", "-NoExit", "-Command", "copilot" })]
    [InlineData("powershell", false, new[] { "-NoLogo", "-Command", "copilot" })]
    [InlineData("cmd", true, new[] { "/K", "copilot" })]
    [InlineData("cmd", false, new[] { "/C", "copilot" })]
    public void AgentToolLauncherSubsystem_KeepShellOpen_PowerShellAndCmd_OnKeepsOpenOffEndsWithTool(
        string preference, bool keepShellOpen, string[] expectedArguments)
    {
        // Arrange: a deterministic Windows shell detector and a recording starter
        var detector = FakeAgentToolProcessStarter.CreateShellDetector();
        var starter = new FakeAgentToolProcessStarter();

        // Act: detect the shell, build the start information and start the process
        var shell = detector.Detect(preference);
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "copilot", @"C:\repos\example", keepShellOpen);
        starter.Start(startInfo);

        // Assert: exactly one process was requested, in the repo, with the expected arguments
        var started = Assert.Single(starter.Started);
        Assert.Equal(shell.ExecutablePath, started.FileName);
        Assert.Equal(@"C:\repos\example", started.WorkingDirectory);
        Assert.Equal(expectedArguments, started.ArgumentList);
    }

    /// <summary>
    ///     Test that a POSIX shell is started the same way whether or not the shell is kept open.
    /// </summary>
    /// <param name="keepShellOpen">Whether the shell stays open after the tool exits.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AgentToolLauncherSubsystem_KeepShellOpen_PosixShell_IsUnaffected(bool keepShellOpen)
    {
        // Arrange: a detector for a POSIX machine and a recording starter
        var detector = new ShellDetector(
            resolveOnPath: _ => null,
            fileExists: _ => false,
            getShellEnvironmentVariable: () => "/bin/bash",
            isWindows: false);
        var starter = new FakeAgentToolProcessStarter();

        // Act: detect the shell, build the start information and start the process
        var shell = detector.Detect();
        var startInfo = DemaConsulting.AgentControl.AgentToolLauncher.AgentToolLauncher.BuildProcessStartInfo(
            shell, "copilot", "/home/user/repos/example", keepShellOpen);
        starter.Start(startInfo);

        // Assert: -c either way
        var started = Assert.Single(starter.Started);
        Assert.Equal("/bin/bash", started.FileName);
        Assert.Equal(["-c", "copilot"], started.ArgumentList);
    }
}
