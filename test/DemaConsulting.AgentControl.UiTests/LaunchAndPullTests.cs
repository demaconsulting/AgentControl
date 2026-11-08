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

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests driving the Launch and Pull actions on a repo card, asserting on what was
///     actually invoked via the arg-logger stub rather than on view-model state alone.
/// </summary>
public sealed class LaunchAndPullTests
{
    /// <summary>
    ///     Clicking "Launch" on a repo card must actually execute the configured (custom)
    ///     agent-tool command - here the quoted arg-logger stub path followed by a marker
    ///     argument - with the repo's path as the working directory.
    /// </summary>
    /// <remarks>
    ///     Per <c>AgentToolLauncher</c>, the agent-tool command runs inside a detected shell
    ///     (PowerShell/cmd) rather than being exec'd directly. The command starts with a quoted
    ///     path, which PowerShell would merely echo unless <c>AgentToolLauncher</c> adds the call
    ///     operator; the recorded invocation (arguments exactly the marker, which git
    ///     invocations of the stub never carry) therefore proves the stub really ran, and its
    ///     working directory is what the shell inherits from
    ///     <c>AgentToolLauncher.BuildProcessStartInfo</c>'s <c>WorkingDirectory</c>.
    /// </remarks>
    /// <remarks>
    ///     Per architecture.md's "ensure-synced-before-launch" behavior (as amended, launch is
    ///     never blocked by sync state), this context is seeded with a pin and the four managed
    ///     folders are pre-created directly on disk (bypassing any package source) purely to
    ///     exercise the "already synced" fast path - not to avoid a block, since a never-pinned
    ///     repo now launches successfully too (see
    ///     <see cref="LaunchButton_Click_NoPin_StillInvokesConfiguredAgentTool"/>).
    /// </remarks>
    [Fact]
    public void LaunchButton_Click_InvokesConfiguredAgentToolInRepoWorkingDirectory()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext(
            pinnedPackageName: "contoso-agents",
            pinnedPackageVersion: "1.0.0");
        CreateManagedFolders(context.RepoPath);
        var mainWindow = context.Launch();

        // Act
        var launchButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LaunchButton"))?.AsButton();
        Assert.NotNull(launchButton);
        launchButton.Invoke();

        // Assert: the arg-logger stub, executed from the quoted agent-tool command inside the
        // launched shell, recorded an invocation carrying the agent-tool marker argument (which
        // no git invocation does) in the repo path.
        var invocation = WaitForAgentToolInvocation(context);

        Assert.NotNull(invocation);
        Assert.Equal([TestSettingsWriter.AgentToolMarkerArgument], invocation.Arguments);
        Assert.True(PathsEqual(invocation.WorkingDirectory, context.RepoPath));
    }

    /// <summary>
    ///     Clicking "Launch" on a repo card with no pinned package at all must still invoke the
    ///     configured agent-tool command - per the amended "ensure-synced-before-launch" contract,
    ///     an agentic CLI tool remains useful even with zero managed agent files present, so the
    ///     lack of a pin must never block the launch.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_NoPin_StillInvokesConfiguredAgentTool()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // Act
        var launchButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LaunchButton"))?.AsButton();
        Assert.NotNull(launchButton);
        launchButton.Invoke();

        // Assert: the arg-logger stub recorded the agent-tool invocation (marker argument) in
        // the repo path, despite this repo never having had a package pinned.
        var invocation = WaitForAgentToolInvocation(context);

        Assert.NotNull(invocation);
        Assert.Equal([TestSettingsWriter.AgentToolMarkerArgument], invocation.Arguments);
        Assert.True(PathsEqual(invocation.WorkingDirectory, context.RepoPath));
    }

    /// <summary>
    ///     Clicking "Pull" on a repo card with a clean git status (as reported by the arg-logger
    ///     stub substituted for git) must invoke the configured git executable with pull-like
    ///     arguments.
    /// </summary>
    [Fact]
    public void PullButton_Click_InvokesConfiguredGitExecutableWithPullArgument()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // The "Pull" button is always rendered, but only becomes enabled once
        // RefreshGitStatus() has run "git status --porcelain" (via the arg-logger stub, which
        // always exits 0 with no output - i.e. a clean working tree) during startup; wait for it
        // to appear and become enabled before clicking.
        var pullButtonElement = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PullButton")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(pullButtonElement);

        var pullButton = pullButtonElement.AsButton();
        Retry.WhileFalse(
            () => pullButton.IsEnabled,
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(pullButton);

        // Act
        pullButton.Invoke();

        // Assert: a later "pull" invocation was recorded, distinct from the earlier "status
        // --porcelain" status check(s) also recorded against the arg-logger stub.
        var invocation = ArgLoggerLog.WaitForInvocation(
            context.ArgLoggerOutputFile,
            record => record.Arguments.Contains("pull") && PathsEqual(record.WorkingDirectory, context.RepoPath),
            TimeSpan.FromSeconds(15));

        Assert.NotNull(invocation);
    }

    /// <summary>
    ///     Waits for the arg-logger stub invocation that originated from the Launch action - the
    ///     one carrying the agent-tool marker argument, which git invocations never do.
    /// </summary>
    /// <param name="context">The test context whose log file to poll.</param>
    /// <returns>The invocation, or <see langword="null"/> if none was recorded in time.</returns>
    private static ArgLoggerInvocation? WaitForAgentToolInvocation(AgentControlTestContext context) =>
        ArgLoggerLog.WaitForInvocation(
            context.ArgLoggerOutputFile,
            record => record.Arguments.Contains(TestSettingsWriter.AgentToolMarkerArgument),
            TimeSpan.FromSeconds(15));

    /// <summary>
    ///     Directly creates the four managed folders (empty) under a repo root, without going
    ///     through a package zip/extraction, so <c>EnsureAgentFilesSyncedBeforeLaunch</c> treats
    ///     the repo as already synced and proceeds straight to launching.
    /// </summary>
    /// <param name="repoRoot">The repo root directory to create the managed folders under.</param>
    private static void CreateManagedFolders(string repoRoot)
    {
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "agents"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "standards"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "templates"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "skills"));
    }

    /// <summary>
    ///     Compares two filesystem paths for equality, tolerating Windows' case-insensitivity and
    ///     a trailing directory separator difference.
    /// </summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns><see langword="true"/> if the paths refer to the same location.</returns>
    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
