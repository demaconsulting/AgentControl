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

using System.IO.Compression;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of package content integrity: the pin records which agent files a package version
///     delivered, and a version that is later republished with different files is not synced
///     silently.
/// </summary>
public sealed class PackageContentIntegrityTests
{
    /// <summary>
    ///     A republished package version (same version, different agent files) must not be re-synced
    ///     at launch: the launch tells the user the agent files were not synced, extracts nothing
    ///     and leaves the pin file exactly as it was - and the launch itself continues: the
    ///     configured agent tool is still invoked in the repo's working directory.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_PinnedVersionRepublished_ExplainsAndSyncsNothing()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            // Arrange: a repo pinned to 1.0.0 with the identity of the original files, whose managed
            // folders are missing, and a source whose 1.0.0 zip has since been republished with other agent files
            var originalZip = FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# original\n");
            var identity = TestFilesIdentity.OfArchive(originalZip);
            FixturePackageBuilder.Create(
                sourceDirectory, packageName, "1.0.0", "# republished with other content\n",
                agentFileContent: "# Republished Agent\n");

            using var context = new AgentControlTestContext(
                packageSourcePath: sourceDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0",
                pinnedSource: "Default",
                pinnedContentIdentity: identity);
            var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
            var pinBefore = File.ReadAllText(pinPath);
            var mainWindow = context.Launch();

            // Act: launch the agent tool, which first tries to restore the missing agent files
            var launchButton = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LaunchButton"))?.AsButton(),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(launchButton);
            launchButton.Invoke();

            // Assert: the refusal is explained in plain language in a question that offers to launch anyway
            // (the default is Cancel), nothing was extracted, and the pin is untouched
            var messageWindow = context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(30));
            Assert.NotNull(messageWindow);
            var message = messageWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationMessageText"))?.Name;
            Assert.NotNull(message);
            Assert.Contains("not synced", message, StringComparison.Ordinal);
            Assert.Contains("now contains different files", message, StringComparison.Ordinal);
            Assert.Equal(
                "Launch anyway",
                messageWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton().Name);
            Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
            Assert.Equal(pinBefore, File.ReadAllText(pinPath));

            // Act: choose to launch anyway
            messageWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton().Invoke();

            // Assert: the launch goes on - the configured agent tool (the arg-logger
            // stub, recognized by its marker argument, which git invocations never carry) was invoked,
            // in the repo's working directory
            var invocation = ArgLoggerLog.WaitForInvocation(
                context.ArgLoggerOutputFile,
                record => record.Arguments.Contains(TestSettingsWriter.AgentToolMarkerArgument),
                TimeSpan.FromSeconds(30));
            Assert.NotNull(invocation);
            Assert.Equal([TestSettingsWriter.AgentToolMarkerArgument], invocation.Arguments);
            Assert.True(PathsEqual(invocation.WorkingDirectory, context.RepoPath));

            // Assert: and nothing was synced or written even though the launch went on
            Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
            Assert.Equal(pinBefore, File.ReadAllText(pinPath));
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Selecting a package for a never-pinned repo must record the identity of the package's agent
    ///     files in the repo's pin file, so a later republished copy of the version can be told apart.
    /// </summary>
    [Fact]
    public void SelectPackageMenuItem_Click_RecordsFilesIdentityInPinFile()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            var zip = FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# v1.0.0\n");
            using var context = new AgentControlTestContext(packageSourcePath: sourceDirectory);
            var mainWindow = context.Launch();

            // Act: choose the package in the Select Package dialog
            mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))?.AsButton()?.Invoke();
            var selectPackageMenuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageMenuItem")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
            Assert.NotNull(selectPackageMenuItem);
            selectPackageMenuItem.Click();
            var dialog = context.WaitForWindowWithDescendant("SelectPackageNameList", TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);
            var nameItem = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageNameList"))
                    ?.FindFirstDescendant(cf => cf.ByName(packageName)),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsListBoxItem();
            Assert.NotNull(nameItem);
            nameItem.Select();
            var confirmButton = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageConfirmButton")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsButton();
            Assert.NotNull(confirmButton);
            Assert.True(Retry.WhileFalse(
                () => confirmButton.IsEnabled,
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result);
            confirmButton.Invoke();
            Assert.NotNull(context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)));

            // Assert: the pin names the identity of the files of the zip that was applied
            using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json")));
            Assert.Equal(TestFilesIdentity.OfArchive(zip), pin.RootElement.GetProperty("ContentIdentity").GetString());
            Assert.Equal(4, pin.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Upgrading a repo whose pin predates content identities must not warn: the upgrade is
    ///     applied and the pin adopts the identity of the files of the newly applied package.
    /// </summary>
    [Fact]
    public void UpgradeChip_Click_PinWithoutIdentity_AdoptsIdentityOfNewVersionWithoutWarning()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            // Arrange: a repo pinned to 1.0.0 with no identity recorded (a pin from before
            // identities existed), and a source that now also offers 1.1.0
            FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# v1.0.0\n");
            var newerZip = FixturePackageBuilder.Create(sourceDirectory, packageName, "1.1.0", "# v1.1.0\n");
            using var context = new AgentControlTestContext(
                packageSourcePath: sourceDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0",
                pinnedSource: "Default");
            var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
            using (var before = JsonDocument.Parse(File.ReadAllText(pinPath)))
            {
                Assert.False(before.RootElement.TryGetProperty("ContentIdentity", out _));
            }

            var mainWindow = context.Launch();
            var chip = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeAvailableBadge"))?.AsButton(),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(chip);

            // Act: upgrade
            chip.Invoke();

            // Assert: applied (release notes open) with no refusal, and the pin now records the
            // identity of the zip that was applied
            Assert.NotNull(context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15)));
            Assert.Null(context.WaitForWindowWithDescendant("MessageText", TimeSpan.FromSeconds(2)));
            Assert.True(Retry.WhileFalse(
                () =>
                {
                    using var pin = JsonDocument.Parse(File.ReadAllText(pinPath));
                    return pin.RootElement.TryGetProperty("ContentIdentity", out var identity) &&
                           identity.GetString() == TestFilesIdentity.OfArchive(newerZip);
                },
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result);
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Selecting a package from a NuGet-package folder source for a never-pinned repository must record
    ///     the identity of the package's agent files in the repo's pin file (schema 4) - not of the
    ///     <c>.nupkg</c> file - so that a version republished in the feed can be told apart later.
    /// </summary>
    [Fact]
    public void SelectPackage_FromNuGetFolderSource_RecordsFilesIdentityInPinFile()
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
            // Arrange: a NuGet folder feed with one package, and a never-pinned repo (the feed is the only source)
            var nupkg = NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "1.0.0", "from feed");
            using var context = new AgentControlTestContext(
                nuGetFeedPath: feedDirectory, nuGetPackageIds: [packageName]);
            var mainWindow = context.Launch();

            // Act: choose the package in the Select Package dialog
            GitSourceTests.OpenCardMenuItem(mainWindow, "SelectPackageMenuItem").AsMenuItem().Invoke();
            var dialog = context.WaitForWindowWithDescendant("SelectPackageNameList", TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);
            GitSourceTests.SelectListItem(dialog, "SelectPackageNameList", packageName);
            var confirm = GitSourceTests.Find(dialog, "SelectPackageConfirmButton").AsButton();
            Assert.True(Retry.WhileFalse(
                () => confirm.IsEnabled, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200)).Result);
            confirm.Invoke();
            Assert.NotNull(context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(30)));

            // Assert: the files came from the NuGet package, and the pin names the identity of its agent files
            Assert.Equal("from feed", File.ReadAllText(Path.Combine(context.RepoPath, ".github", "agents", "feed.md")));
            using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json")));
            Assert.Equal(TestFilesIdentity.OfArchive(nupkg, "content/"), pin.RootElement.GetProperty("ContentIdentity").GetString());
            Assert.Equal(4, pin.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally
        {
            TryDelete(feedDirectory);
        }
    }

    /// <summary>
    ///     A NuGet package version republished in the feed with different content must not be re-synced at
    ///     launch: the launch tells the user the agent files were not synced, extracts nothing and leaves the
    ///     pin file exactly as it was - and the launch itself continues.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_NuGetPinnedVersionRepublished_ExplainsAndSyncsNothing()
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
            // Arrange: a repo pinned to 1.0.0 with the identity of the original package's files, whose managed
            // folders are missing, and a feed whose 1.0.0 package has since been republished
            var identity = TestFilesIdentity.OfArchive(
                NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "1.0.0", "original"), "content/");
            NuGetFixturePackageBuilder.Create(feedDirectory, packageName, "1.0.0", "republished with other content");
            using var context = new AgentControlTestContext(
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0",
                nuGetFeedPath: feedDirectory,
                nuGetPackageIds: [packageName],
                pinnedSource: "Feed",
                pinnedContentIdentity: identity);
            var mainWindow = context.Launch();

            // Act / Assert: the launch explains the refusal, applies nothing and goes on
            AssertLaunchRefusesRepublishedPin(context, mainWindow);
        }
        finally
        {
            TryDelete(feedDirectory);
        }
    }

    /// <summary>
    ///     Clicks Launch on a repo whose agent files differ from the pin and chooses Keep current files (the
    ///     default answer): the agent tool starts in the repo and the files are exactly as the user left them.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_AgentFilesDiffer_KeepChosen_LaunchesWithFilesUntouched()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            // Arrange: a repo holding the pinned package's files, with one of them edited
            var zipPath = FixturePackageBuilder.Create(sourceDirectory, "contoso-agents", "1.0.0");
            using var context = CreateContext(sourceDirectory, TestFilesIdentity.OfArchive(zipPath));
            InstallPackageFiles(zipPath, context.RepoPath);
            var edited = Path.Combine(context.RepoPath, ".github", "agents", "test.md");
            File.WriteAllText(edited, "# my experiment\n");
            var mainWindow = context.Launch();

            // Act: launch; the files differ, so the user is asked
            ClickLaunch(mainWindow);
            var question = context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(30));
            Assert.NotNull(question);

            // Assert: the question offers both choices, and nothing has launched or changed yet
            Assert.Contains(
                "do not match",
                question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationMessageText"))?.Name,
                StringComparison.Ordinal);
            Assert.Equal(
                "Update from package",
                question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton().Name);
            Assert.Equal(
                "Keep current files",
                question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationNoButton"))?.AsButton().Name);
            Assert.Equal("# my experiment\n", File.ReadAllText(edited));

            // Act: keep the current files
            question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationNoButton"))?.AsButton().Invoke();

            // Assert: the tool was launched in the repo and the edited file is still there
            var invocation = WaitForAgentToolInvocation(context);
            Assert.NotNull(invocation);
            Assert.True(PathsEqual(invocation.WorkingDirectory, context.RepoPath));
            Assert.Equal("# my experiment\n", File.ReadAllText(edited));
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Clicks Launch on a repo whose agent files differ from the pin and chooses Update from package: the
    ///     files are replaced by the pinned package's files and the agent tool starts.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_AgentFilesDiffer_UpdateChosen_ReplacesFilesAndLaunches()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            // Arrange: a repo holding about half of the pinned package's files (all four folders still exist)
            var zipPath = FixturePackageBuilder.Create(sourceDirectory, "contoso-agents", "1.0.0");
            using var context = CreateContext(sourceDirectory, TestFilesIdentity.OfArchive(zipPath));
            InstallPackageFiles(zipPath, context.RepoPath);
            File.Delete(Path.Combine(context.RepoPath, ".github", "standards", "test.md"));
            File.Delete(Path.Combine(context.RepoPath, ".github", "skills", "test.md"));
            var mainWindow = context.Launch();

            // Act: launch and answer Update from package
            ClickLaunch(mainWindow);
            var question = context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(30));
            Assert.NotNull(question);
            question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton().Invoke();

            // Assert: the tool was launched and every file of the package is back
            var invocation = WaitForAgentToolInvocation(context);
            Assert.NotNull(invocation);
            Assert.True(PathsEqual(invocation.WorkingDirectory, context.RepoPath));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "standards", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "skills", "test.md")));
            Assert.Equal("# Test Agent\n", File.ReadAllText(Path.Combine(context.RepoPath, ".github", "agents", "test.md")));
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Clicks Launch on a repo whose agent files are exactly the pinned package's: the agent tool starts
    ///     at once, without any question.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_AgentFilesMatchPin_LaunchesWithoutPrompt()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            // Arrange: a repo holding exactly the pinned package's files
            var zipPath = FixturePackageBuilder.Create(sourceDirectory, "contoso-agents", "1.0.0");
            using var context = CreateContext(sourceDirectory, TestFilesIdentity.OfArchive(zipPath));
            InstallPackageFiles(zipPath, context.RepoPath);
            var mainWindow = context.Launch();

            // Act
            ClickLaunch(mainWindow);

            // Assert: launched, and no question was ever shown
            var invocation = WaitForAgentToolInvocation(context);
            Assert.NotNull(invocation);
            Assert.True(PathsEqual(invocation.WorkingDirectory, context.RepoPath));
            Assert.Null(context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(2)));
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Clicks Launch on a repo that has no agent files when the pinned package cannot be obtained, and
    ///     chooses Cancel (the default answer): the agent tool is not started.
    /// </summary>
    [Fact]
    public void LaunchButton_Click_RestoreFails_CancelChosen_DoesNotLaunch()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            // Arrange: a pinned repo without agent files, and a source that does not offer the package
            var zipPath = FixturePackageBuilder.Create(sourceDirectory, "contoso-agents", "1.0.0");
            var identity = TestFilesIdentity.OfArchive(zipPath);
            File.Delete(zipPath);
            using var context = CreateContext(sourceDirectory, identity);
            var mainWindow = context.Launch();

            // Act: launch; the restore fails, so the user is asked
            ClickLaunch(mainWindow);
            var question = context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(30));
            Assert.NotNull(question);
            Assert.Equal(
                "Launch anyway",
                question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton().Name);
            Assert.Equal(
                "Cancel",
                question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationNoButton"))?.AsButton().Name);
            question.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationNoButton"))?.AsButton().Invoke();

            // Assert: the agent tool was not started, and nothing was written to the repo
            Assert.Null(ArgLoggerLog.WaitForInvocation(
                context.ArgLoggerOutputFile,
                record => record.Arguments.Contains(TestSettingsWriter.AgentToolMarkerArgument),
                TimeSpan.FromSeconds(5)));
            Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
        }
        finally
        {
            TryDelete(sourceDirectory);
        }
    }

    /// <summary>
    ///     Creates a test context for a repo pinned to <c>contoso-agents</c> 1.0.0 with the given recorded
    ///     identity and a zip source folder.
    /// </summary>
    /// <param name="sourceDirectory">The zip source folder.</param>
    /// <param name="identity">The recorded files identity.</param>
    /// <returns>The context, which the caller disposes.</returns>
    private static AgentControlTestContext CreateContext(string sourceDirectory, string identity) =>
        new(
            packageSourcePath: sourceDirectory,
            pinnedPackageName: "contoso-agents",
            pinnedPackageVersion: "1.0.0",
            pinnedSource: "Default",
            pinnedContentIdentity: identity);

    /// <summary>
    ///     Writes the managed-folder files of a package zip into a repo exactly as extraction would (raw
    ///     bytes), so the repo's agent files are the package's.
    /// </summary>
    /// <param name="zipPath">The package zip.</param>
    /// <param name="repoPath">The repo folder.</param>
    private static void InstallPackageFiles(string zipPath, string repoPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries.Where(
                     entry => entry.Name.Length > 0 && entry.FullName.StartsWith(".github/", StringComparison.Ordinal)))
        {
            var target = Path.Combine(repoPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>
    ///     Clicks the repo card's Launch button.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    private static void ClickLaunch(Window mainWindow)
    {
        var launchButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LaunchButton"))?.AsButton(),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(launchButton);
        launchButton.Invoke();
    }

    /// <summary>
    ///     Waits for the invocation of the configured agent tool (the arg-logger stub with its marker argument).
    /// </summary>
    /// <param name="context">The test context whose log to poll.</param>
    /// <returns>The invocation, or <see langword="null"/> if none was recorded in time.</returns>
    private static ArgLoggerInvocation? WaitForAgentToolInvocation(AgentControlTestContext context) =>
        ArgLoggerLog.WaitForInvocation(
            context.ArgLoggerOutputFile,
            record => record.Arguments.Contains(TestSettingsWriter.AgentToolMarkerArgument),
            TimeSpan.FromSeconds(30));

    /// <summary>
    ///     Clicks Launch on a repo whose pinned version was republished, and checks that the refusal is explained
    ///     in plain language, that nothing was extracted and the pin is untouched, and that the launch went on.
    /// </summary>
    /// <param name="context">The running test context, whose repo has the pin and no managed folders.</param>
    /// <param name="mainWindow">The main window.</param>
    private static void AssertLaunchRefusesRepublishedPin(AgentControlTestContext context, Window mainWindow)
    {
        var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
        var pinBefore = File.ReadAllText(pinPath);

        // Act: launch the agent tool, which first tries to restore the missing agent files
        var launchButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LaunchButton"))?.AsButton(),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(launchButton);
        launchButton.Invoke();

        // Assert: the refusal is explained in a question offering to launch anyway (Cancel is the default),
        // nothing was extracted, and the pin is untouched
        var messageWindow = context.WaitForWindowWithDescendant("ConfirmationMessageText", TimeSpan.FromSeconds(30));
        Assert.NotNull(messageWindow);
        var message = messageWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationMessageText"))?.Name;
        Assert.NotNull(message);
        Assert.Contains("not synced", message, StringComparison.Ordinal);
        Assert.Contains("now contains different files", message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
        Assert.Equal(pinBefore, File.ReadAllText(pinPath));

        // Act: choose to launch anyway
        messageWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton().Invoke();

        // Assert: the launch goes on - the configured agent tool was invoked in the repo
        var invocation = ArgLoggerLog.WaitForInvocation(
            context.ArgLoggerOutputFile,
            record => record.Arguments.Contains(TestSettingsWriter.AgentToolMarkerArgument),
            TimeSpan.FromSeconds(30));
        Assert.NotNull(invocation);
        Assert.True(PathsEqual(invocation.WorkingDirectory, context.RepoPath));
        Assert.False(Directory.Exists(Path.Combine(context.RepoPath, ".github")));
        Assert.Equal(pinBefore, File.ReadAllText(pinPath));
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
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
    }
}
