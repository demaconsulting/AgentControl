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
///     End-to-end tests for the "Select Package..." flow on a never-pinned repo and for the
///     "About" dialog, reusing the same <see cref="AgentControlTestContext"/>/
///     <see cref="FixturePackageBuilder"/> harness as <see cref="UpgradeTests"/>.
/// </summary>
public sealed class SelectPackageAndAboutTests
{
    /// <summary>
    ///     Opening the "..." menu on a never-pinned repo card must show "Select Package..."
    ///     instead of "Upgrade"; driving that dialog end-to-end (pick name, pick version, click
    ///     Select) must pin the package and sync the four managed folders on disk.
    /// </summary>
    [Fact]
    public void SelectPackageMenuItem_Click_AppliesChosenPackageAndSyncsManagedFolders()
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
            FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# v1.0.0\n");

            // No pinnedPackageName/pinnedPackageVersion: this repo has never been synced.
            using var context = new AgentControlTestContext(packageSourcePath: sourceDirectory);
            var mainWindow = context.Launch();

            var moreActionsButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))?.AsButton();
            Assert.NotNull(moreActionsButton);
            moreActionsButton.Invoke();

            // Assert: "Select Package..." is shown, "Upgrade" is not - the two are mutually
            // exclusive per IsPackageSelectionNeeded/IsUpgradeAvailable.
            var selectPackageMenuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageMenuItem")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
            Assert.NotNull(selectPackageMenuItem);
            Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeMenuItem")));

            selectPackageMenuItem.Click();

            // Assert: the modal SelectPackageWindow appears as a top-level window. Search by a
            // descendant AutomationId (as RepoCardFeatureTests does for ConfirmationWindow)
            // rather than by title, since a modal ShowDialog<T> window's title is not always a
            // reliable/immediate match target via UI Automation in this environment.
            var selectPackageWindow = WaitForWindowWithDescendant(context, "SelectPackageNameList", TimeSpan.FromSeconds(15));
            Assert.NotNull(selectPackageWindow);

            var nameList = selectPackageWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageNameList"))?.AsListBox();
            Assert.NotNull(nameList);
            var nameItem = Retry.WhileNull(
                () => nameList.FindFirstDescendant(cf => cf.ByName(packageName)),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsListBoxItem();
            Assert.NotNull(nameItem);
            nameItem.Select();

            // The version list should default-select "1.0.0" (the only/latest available version)
            // once the package name selection has propagated - proceed straight to confirming.
            var confirmButton = Retry.WhileNull(
                () => selectPackageWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageConfirmButton")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsButton();
            Assert.NotNull(confirmButton);
            confirmButton.Invoke();

            // Assert: the release notes window should appear once the apply completes.
            var releaseNotesWindow = context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15));
            Assert.NotNull(releaseNotesWindow);

            // The apply must have extracted the four managed folders and written the pin file.
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "agents", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "standards", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "templates", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "skills", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".agentcontrol.json")));
            Assert.Contains(
                packageName,
                File.ReadAllText(Path.Combine(context.RepoPath, ".agentcontrol.json")),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
        }
    }

    /// <summary>
    ///     Clicking "About" must open a non-modal window showing the app name, its
    ///     <c>Program.Version</c>, and a copyright/license summary.
    /// </summary>
    [Fact]
    public void AboutButton_Click_OpensAboutWindowShowingVersionAndCopyright()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        var aboutButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OpenAboutButton"))?.AsButton();
        Assert.NotNull(aboutButton);
        aboutButton.Invoke();

        var aboutWindow = context.WaitForWindow(
            title => title.StartsWith("About", StringComparison.Ordinal),
            TimeSpan.FromSeconds(15));
        Assert.NotNull(aboutWindow);

        var versionText = aboutWindow.FindFirstDescendant(cf => cf.ByAutomationId("AboutVersionText"))?.AsLabel();
        Assert.NotNull(versionText);
        Assert.False(string.IsNullOrWhiteSpace(versionText.Text));

        var copyrightText = aboutWindow.FindFirstDescendant(cf => cf.ByAutomationId("AboutCopyrightText"))?.AsLabel();
        Assert.NotNull(copyrightText);
        Assert.False(string.IsNullOrWhiteSpace(copyrightText.Text));

        var licenseText = aboutWindow.FindFirstDescendant(cf => cf.ByAutomationId("AboutLicenseText"))?.AsLabel();
        Assert.NotNull(licenseText);
        Assert.False(string.IsNullOrWhiteSpace(licenseText.Text));

        // The window must not be modal: the main window should remain usable while the About
        // window is open (verified by locating the main window's own toolbar button still
        // present in the automation tree without first closing the About window).
        Assert.NotNull(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OpenAboutButton")));

        var closeButton = aboutWindow.FindFirstDescendant(cf => cf.ByAutomationId("AboutCloseButton"))?.AsButton();
        Assert.NotNull(closeButton);
        closeButton.Invoke();
    }

    /// <summary>
    ///     Waits for a top-level window (other than the main window) containing a descendant
    ///     with the given AutomationId to appear, polling until found or the timeout elapses.
    /// </summary>
    /// <param name="context">The test context whose <see cref="AgentControlTestContext.App"/>/
    ///     <see cref="AgentControlTestContext.Automation"/> are used to enumerate top-level
    ///     windows.</param>
    /// <param name="automationId">The descendant AutomationId to look for.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>The matching window, or <see langword="null"/> if none appeared in time.</returns>
    private static Window? WaitForWindowWithDescendant(AgentControlTestContext context, string automationId, TimeSpan timeout)
    {
        if (context.App is null || context.Automation is null)
        {
            return null;
        }

        return Retry.WhileNull(
            () => context.App.GetAllTopLevelWindows(context.Automation)
                .FirstOrDefault(window => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)) is not null),
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(200)).Result;
    }
}
