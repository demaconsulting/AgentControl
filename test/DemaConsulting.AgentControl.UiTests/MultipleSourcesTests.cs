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
///     End-to-end tests for several configured package sources used side by side: a zip package
///     folder and a NuGet package folder offering the same package name, with each repo's pin
///     resolved only against the source it names.
/// </summary>
/// <remarks>
///     Both tests drive the published application through the same "Upgrade" menu flow as
///     <see cref="UpgradeTests"/>, so they share its reliance on UI Automation popup timing.
/// </remarks>
public sealed class MultipleSourcesTests
{
    /// <summary>
    ///     A repo pinned to the NuGet folder source is upgraded from that source - never from the
    ///     zip source, which offers a higher version of the same package name - and the pin file
    ///     keeps recording the NuGet source's alias.
    /// </summary>
    [Fact]
    public void UpgradeMenuItem_Click_PinnedToNuGetFolderSource_UpgradesFromThatSourceOnly()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var zipDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-zip-" + Guid.NewGuid());
        var feedDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-feed-" + Guid.NewGuid());

        try
        {
            // The zip source offers a higher version of the same package name; it must be ignored
            FixturePackageBuilder.Create(zipDirectory, packageName, "9.0.0", "# zip 9.0.0\n");
            NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "1.0.0", "feed 1.0.0");
            NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "2.0.0", "feed 2.0.0", "# feed 2.0.0\n");

            using var context = new AgentControlTestContext(
                packageSourcePath: zipDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0",
                nuGetFeedPath: feedDirectory,
                nuGetPackageIds: [packageName],
                pinnedSource: "Feed");
            var mainWindow = context.Launch();

            // The badge appears once the NuGet source has been listed and 2.0.0 found newer
            var badgeText = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeAvailableBadge")),
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(badgeText);

            mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))?.AsButton()?.Invoke();
            var upgradeMenuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeMenuItem")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
            Assert.NotNull(upgradeMenuItem);
            upgradeMenuItem.Click();

            var releaseNotesWindow = context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(30));
            Assert.NotNull(releaseNotesWindow);

            // The repo holds the NuGet package's files, none of the zip package's, and the pin
            // still names the NuGet source at its new version
            var agentFile = Path.Combine(context.RepoPath, ".github", "agents", "feed.md");
            Assert.True(File.Exists(agentFile));
            Assert.Equal("feed 2.0.0", File.ReadAllText(agentFile));
            Assert.False(File.Exists(Path.Combine(context.RepoPath, ".github", "agents", "test.md")));

            var pinJson = File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json"));
            Assert.Contains("\"Source\": \"Feed\"", pinJson, StringComparison.Ordinal);
            Assert.Contains("\"Version\": \"2.0.0\"", pinJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(zipDirectory);
            TryDelete(feedDirectory);
        }
    }

    /// <summary>
    ///     A repo pinned to a NuGet folder source that requires signed packages is refused an upgrade
    ///     to an unsigned package with a message saying so: nothing is extracted and the pin keeps
    ///     its version.
    /// </summary>
    [Fact]
    public void UpgradeMenuItem_Click_NuGetSourceRequiresSignaturesAndPackageUnsigned_IsRefusedAndRepoUnchanged()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var feedDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-feed-" + Guid.NewGuid());

        try
        {
            NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "1.0.0", "feed 1.0.0");
            NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "2.0.0", "feed 2.0.0", "# feed 2.0.0\n");

            using var context = new AgentControlTestContext(
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0",
                nuGetFeedPath: feedDirectory,
                nuGetPackageIds: [packageName],
                pinnedSource: "Feed",
                nuGetRequireSigned: true);
            var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
            var pinBefore = File.ReadAllText(pinPath);
            var mainWindow = context.Launch();

            // The chip carries the signature shield (named for assistive technology with what it
            // means), and the newer unsigned version is still offered, because listing versions
            // does not look at signatures
            var shield = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PackageSignatureIcon")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(shield);
            Assert.Equal("Signature required (any trusted signer)", shield.Name);
            Assert.NotNull(Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeAvailableBadge")),
                timeout: TimeSpan.FromSeconds(30),
                interval: TimeSpan.FromMilliseconds(200)).Result);

            mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))?.AsButton()?.Invoke();
            var upgradeMenuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeMenuItem")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
            Assert.NotNull(upgradeMenuItem);
            upgradeMenuItem.Click();

            // The refusal is explained in a message box
            var messageWindow = context.WaitForWindowWithDescendant("MessageText", TimeSpan.FromSeconds(30));
            Assert.NotNull(messageWindow);
            var message = messageWindow.FindFirstDescendant(cf => cf.ByAutomationId("MessageText"))?.Name;
            Assert.NotNull(message);
            Assert.Contains("is not validly signed", message, StringComparison.Ordinal);
            Assert.Contains("'Feed'", message, StringComparison.Ordinal);

            // Nothing was extracted and the pin is untouched
            Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
            Assert.Equal(pinBefore, File.ReadAllText(pinPath));
        }
        finally
        {
            TryDelete(feedDirectory);
        }
    }

    /// <summary>
    ///     A repo pinned to a source that is not configured shows a package source problem and is
    ///     offered no upgrade from another source, even though the configured zip source has a
    ///     higher version of the same package name; its pin file is left unchanged.
    /// </summary>
    [Fact]
    public void RepoCard_PinnedToUnconfiguredSource_ShowsSourceProblemAndOffersNoUpgrade()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var zipDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-zip-" + Guid.NewGuid());

        try
        {
            FixturePackageBuilder.Create(zipDirectory, packageName, "9.0.0", "# zip 9.0.0\n");

            using var context = new AgentControlTestContext(
                packageSourcePath: zipDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0",
                pinnedSource: "Gone");
            var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
            var pinBefore = File.ReadAllText(pinPath);
            var mainWindow = context.Launch();

            // The card reports a package source problem for the unconfigured source, and no
            // upgrade badge appears even though another source has a higher version
            Assert.NotNull(Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result);
            var problemBadge = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SourceProblemBadge")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(problemBadge);
            Assert.Equal("Package source problem", problemBadge.Name);
            Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeAvailableBadge")));
            Assert.Equal(pinBefore, File.ReadAllText(pinPath));
            Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
        }
        finally
        {
            TryDelete(zipDirectory);
        }
    }

    /// <summary>
    ///     Best-effort recursive delete of a temporary fixture directory.
    /// </summary>
    /// <param name="path">The directory to delete.</param>
    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }
}
