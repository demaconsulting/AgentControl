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

using System.Diagnostics;
using DemaConsulting.AgentControl.AgentToolLauncher;

namespace DemaConsulting.AgentControl.Tests.AgentToolLauncher;

/// <summary>
///     Unit tests for the <c>AgentToolProcessStarter</c> class.
/// </summary>
/// <remarks>
///     The only process these tests start is a tiny, windowless operating-system command that
///     creates a marker folder and exits; no shell window is ever opened and nothing is left
///     running.
/// </remarks>
public class AgentToolProcessStarterTests
{
    /// <summary>
    ///     How long the positive test waits for the started command to leave its marker.
    /// </summary>
    private static readonly TimeSpan MarkerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Test that null start information is rejected.
    /// </summary>
    [Fact]
    public void AgentToolProcessStarter_Start_NullStartInfo_ThrowsArgumentNullException()
    {
        // Arrange: the production starter
        var starter = new AgentToolProcessStarter();

        // Act and assert: null start information is rejected without starting anything
        Assert.Throws<ArgumentNullException>(() => starter.Start(null!));
    }

    /// <summary>
    ///     Test that an executable that does not exist is reported as an
    ///     <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void AgentToolProcessStarter_Start_MissingExecutable_ThrowsInvalidOperationException()
    {
        // Arrange: start information naming an executable that cannot exist
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol-missing-" + Guid.NewGuid().ToString("N"), "tool.exe");
        var startInfo = new ProcessStartInfo(missing)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var starter = new AgentToolProcessStarter();

        // Act and assert: the failure is reported, nothing is spawned
        Assert.Throws<InvalidOperationException>(() => starter.Start(startInfo));
    }

    /// <summary>
    ///     Test that an existing executable is started: a windowless command creates a marker
    ///     folder in its working directory.
    /// </summary>
    [Fact]
    public void AgentToolProcessStarter_Start_ExistingExecutable_StartsProcess()
    {
        // Arrange: a windowless command that creates a marker folder in a temp working directory
        var workingDirectory = Path.Combine(Path.GetTempPath(), "agentcontrol-starter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sh",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add("mkdir");
                startInfo.ArgumentList.Add("started");
            }
            else
            {
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("mkdir started");
            }

            var starter = new AgentToolProcessStarter();

            // Act: start the process
            starter.Start(startInfo);

            // Assert: the command ran (a bounded wait, because the starter does not wait for it)
            var marker = Path.Combine(workingDirectory, "started");
            SpinWait.SpinUntil(() => Directory.Exists(marker), MarkerTimeout);

            Assert.True(Directory.Exists(marker));
        }
        finally
        {
            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: the short-lived command may still hold the folder for a moment
            }
        }
    }
}
