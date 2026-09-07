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
    ///     Clicking "Launch" on a repo card must invoke the configured (custom) agent-tool
    ///     command - here the arg-logger stub - with the repo's path as the working directory.
    /// </summary>
    /// <remarks>
    ///     Per <c>AgentToolLauncher</c>, the agent-tool command runs inside a detected shell
    ///     (PowerShell/cmd) rather than being exec'd directly, so no arguments are recorded for
    ///     this invocation - only the working directory is asserted here, which is exactly what
    ///     the shell process (and therefore the arg-logger stub it runs) inherits from
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

        // Assert: the arg-logger stub (launched inside a shell by AgentToolLauncher) recorded an
        // invocation whose working directory is the repo path.
        var invocation = ArgLoggerLog.WaitForInvocation(
            context.ArgLoggerOutputFile,
            record => PathsEqual(record.WorkingDirectory, context.RepoPath),
            TimeSpan.FromSeconds(15));

        Assert.NotNull(invocation);
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

        // Assert: the arg-logger stub recorded an invocation whose working directory is the repo
        // path, despite this repo never having had a package pinned.
        var invocation = ArgLoggerLog.WaitForInvocation(
            context.ArgLoggerOutputFile,
            record => PathsEqual(record.WorkingDirectory, context.RepoPath),
            TimeSpan.FromSeconds(15));

        Assert.NotNull(invocation);
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

        // The "Pull" button is only rendered (IsVisible="{Binding CanPull}") once
        // RefreshGitStatus() has run "git status --porcelain" (via the arg-logger stub, which
        // always exits 0 with no output - i.e. a clean working tree) during startup; wait for it
        // to appear in the automation tree before clicking.
        var pullButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PullButton")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result?.AsButton();

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
