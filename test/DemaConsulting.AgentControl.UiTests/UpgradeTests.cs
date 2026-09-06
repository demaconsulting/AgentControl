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
///     End-to-end test for the blind-delete-and-replace upgrade flow: clicking "Upgrade" on a
///     repo card with a newer package available at the configured source must sync the four
///     managed folders and open the non-modal <c>ReleaseNotesViewer</c>.
/// </summary>
/// <remarks>
///     This is the "(d)" scenario the Phase 3 task brief calls out as acceptable to document as a
///     follow-up rather than ship flaky: it depends on successfully driving an Avalonia
///     <c>MenuFlyout</c> popup via UI Automation, which is a materially less proven FlaUI/Avalonia
///     interaction than the direct button clicks in <see cref="LaunchAndPullTests"/> and
///     <see cref="MainWindowSmokeTests"/>. It is retained (rather than deleted) because it passed
///     reliably in this environment's own verification run; see the Phase 3 completion report for
///     the actual pass/fail evidence and the flakiness risk this carries on other machines/CI
///     runners with different Avalonia/UIA popup timing.
/// </remarks>
public sealed class UpgradeTests
{
    /// <summary>
    ///     Clicking "Upgrade" on a repo card with a newer package version available performs the
    ///     blind-delete-and-replace sync and opens a non-modal release-notes window.
    /// </summary>
    [Fact]
    public void UpgradeMenuItem_Click_SyncsManagedFoldersAndShowsReleaseNotes()
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
            FixturePackageBuilder.Create(sourceDirectory, packageName, "1.1.0", "# v1.1.0\n\nNew things.\n");

            using var context = new AgentControlTestContext(
                packageSourcePath: sourceDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0");
            var mainWindow = context.Launch();

            // The upgrade-available badge itself is an Avalonia Border, which (per this
            // environment's own verification) does not surface as a distinct UI Automation
            // element even with AutomationProperties.AutomationId set - only its child TextBlock
            // does. Wait for that child text to appear as the signal that
            // RefreshUpgradeStatus() has compared the pinned "1.0.0" against the "1.1.0" fixture
            // package at the configured source and found it newer.
            var badgeText = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Update available")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(badgeText);

            var moreActionsButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))?.AsButton();
            Assert.NotNull(moreActionsButton);
            moreActionsButton.Invoke();

            // The MenuFlyout's items render as descendants of the same top-level window (per
            // this environment's own verification), so search mainWindow rather than the
            // desktop root.
            var upgradeMenuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeMenuItem")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
            Assert.NotNull(upgradeMenuItem);
            upgradeMenuItem.Click();

            // Act/Assert: the release notes window should appear once the upgrade completes.
            var releaseNotesWindow = context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15));
            Assert.NotNull(releaseNotesWindow);

            // The upgrade must have blind-deleted-and-replaced the managed folders on disk.
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "agents", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "standards", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "templates", "test.md")));
            Assert.True(File.Exists(Path.Combine(context.RepoPath, ".github", "skills", "test.md")));
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
}
