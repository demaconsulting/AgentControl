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

using System.ComponentModel;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests for the AGENTS.md template offer flow: a package sync (via "Upgrade")
///     that includes a root-level AGENTS.md template and finds none already present in the repo
///     must raise the modal <c>ConfirmationWindow</c> offer, and the user's answer must be routed
///     through to <c>RepoCardViewModel.AcceptAgentsMdTemplate</c>/<c>DeclineAgentsMdTemplate</c>
///     exactly as the direct view-model unit tests already verify in isolation - this exercises
///     the real <c>MainWindow</c> event subscription/dialog-routing wiring itself, mirroring
///     <see cref="RepoCardFeatureTests.RemoveMenuItem_ClickThenConfirm_RemovesCard_DecliningLeavesItInPlace"/>'s
///     structure/conventions as closely as possible for its own
///     <c>ConfirmationWindow</c>-driven flow.
/// </summary>
public sealed class AgentsMdTemplateOfferTests
{
    /// <summary>
    ///     Clicking "Upgrade" on a repo card with no AGENTS.md, where the newer package includes
    ///     an AGENTS.md template, must raise the modal offer; confirming "Yes" must write the
    ///     template verbatim to the repo root.
    /// </summary>
    [Fact]
    public void AgentsMdTemplateOffer_Accept_WritesTemplateToRepoRoot()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        const string templateContent = "# AGENTS\n\nCustomize me for this repo.\n";
        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());

        try
        {
            FixturePackageBuilder.Create(sourceDirectory, packageName, "1.0.0", "# v1.0.0\n");
            FixturePackageBuilder.Create(sourceDirectory, packageName, "1.1.0", "# v1.1.0\n", templateContent);

            using var context = new AgentControlTestContext(
                packageSourcePath: sourceDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0");
            var mainWindow = context.Launch();

            InvokeUpgradeMenuItem(mainWindow);

            // The non-modal release-notes window and the modal AGENTS.md offer are both raised
            // from the same ApplyPackageAndShowReleaseNotes call; only the latter blocks further
            // interaction, so wait for the offer's ConfirmationWindow specifically.
            var confirmationWindow = WaitForAgentsMdOfferWindow(context);
            Assert.NotNull(confirmationWindow);

            var yesButton = confirmationWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton();
            Assert.NotNull(yesButton);
            yesButton.Invoke();

            // The template must be written verbatim to the repo root, not into any managed folder.
            var agentsMdPath = Path.Combine(context.RepoPath, "AGENTS.md");
            var written = Retry.WhileFalse(
                () => File.Exists(agentsMdPath),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200));
            Assert.True(written.Success);
            Assert.Equal(templateContent, File.ReadAllText(agentsMdPath));
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
    ///     Clicking "Upgrade" on a repo card with no AGENTS.md, where the newer package includes
    ///     an AGENTS.md template, must raise the modal offer; confirming "No" must leave the repo
    ///     without an AGENTS.md file, and a subsequent sync for the same repo must never
    ///     re-prompt, since the decline is persisted in the repo's pin file regardless of which
    ///     package version is later applied.
    /// </summary>
    [Fact]
    public void AgentsMdTemplateOffer_Decline_DoesNotWriteFileAndDoesNotReprompt()
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
            FixturePackageBuilder.Create(
                sourceDirectory, packageName, "1.1.0", "# v1.1.0\n", "# AGENTS\n\nFirst offer.\n");

            using var context = new AgentControlTestContext(
                packageSourcePath: sourceDirectory,
                pinnedPackageName: packageName,
                pinnedPackageVersion: "1.0.0");
            var mainWindow = context.Launch();

            InvokeUpgradeMenuItem(mainWindow);

            var confirmationWindow = WaitForAgentsMdOfferWindow(context);
            Assert.NotNull(confirmationWindow);

            var noButton = confirmationWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationNoButton"))?.AsButton();
            Assert.NotNull(noButton);
            noButton.Invoke();

            // No AGENTS.md must ever be written when the offer is declined.
            var agentsMdPath = Path.Combine(context.RepoPath, "AGENTS.md");
            var pinFileReflectsDecline = Retry.WhileTrue(
                () => !PinFileRecordsDecline(context.RepoPath),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200));
            Assert.True(pinFileReflectsDecline.Success);
            Assert.False(File.Exists(agentsMdPath));

            // Close this launch (not disposing the context's temp directories yet) before
            // relaunching against the exact same config/repo directory, so this proves the
            // decline persists across a fresh app session - re-opening the same MenuFlyout a
            // second time in one session is unreliable to drive via UI Automation, per
            // RepoCardFeatureTests' own precedent, so a second hermetic launch is used instead.
            CloseCurrentLaunch(context);

            // Stage a newer version (also with a template) so the second launch's Upgrade has
            // something to sync - the prior decline must still suppress the offer even though a
            // genuine re-extraction occurs.
            FixturePackageBuilder.Create(
                sourceDirectory, packageName, "1.2.0", "# v1.2.0\n", "# AGENTS\n\nSecond offer.\n");

            var mainWindow2 = context.Launch();
            InvokeUpgradeMenuItem(mainWindow2);

            // The release notes window for the second upgrade must still appear (proving the
            // sync/extraction genuinely ran), but no AGENTS.md offer this time.
            var releaseNotesWindow = context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15));
            Assert.NotNull(releaseNotesWindow);

            var offerReappeared = Retry.WhileNull(
                () => context.App!.GetAllTopLevelWindows(context.Automation!)
                    .FirstOrDefault(window => window.FindFirstDescendant(
                        cf => cf.ByAutomationId("ConfirmationMessageText")) is not null),
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(200));
            Assert.False(offerReappeared.Success);
            Assert.False(File.Exists(agentsMdPath));
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
    ///     Opens the seeded repo card's "..." menu flyout and clicks its "Upgrade" item.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    private static void InvokeUpgradeMenuItem(Window mainWindow)
    {
        mainWindow.SetForeground();

        var moreActionsButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result?.AsButton();
        Assert.NotNull(moreActionsButton);
        moreActionsButton.Invoke();

        var upgradeMenuItem = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UpgradeMenuItem")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
        Assert.NotNull(upgradeMenuItem);
        upgradeMenuItem.Click();
    }

    /// <summary>
    ///     Waits for the modal AGENTS.md template offer's <c>ConfirmationWindow</c> to appear as
    ///     a top-level window.
    /// </summary>
    /// <param name="context">The test context whose <see cref="AgentControlTestContext.App"/>/
    ///     <see cref="AgentControlTestContext.Automation"/> are used to enumerate top-level
    ///     windows.</param>
    /// <returns>The confirmation window, or <see langword="null"/> if it did not appear in time.</returns>
    private static Window? WaitForAgentsMdOfferWindow(AgentControlTestContext context)
    {
        if (context.App is null || context.Automation is null)
        {
            return null;
        }

        return Retry.WhileNull(
            () => context.App.GetAllTopLevelWindows(context.Automation)
                .FirstOrDefault(window => window.FindFirstDescendant(
                    cf => cf.ByAutomationId("ConfirmationMessageText")) is not null),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(200)).Result;
    }

    /// <summary>
    ///     Reads <paramref name="repoPath"/>'s <c>.agentcontrol.json</c> pin file and checks
    ///     whether it records a persisted AGENTS.md template decline, tolerant of whitespace
    ///     differences from <c>JsonSerializerOptions.WriteIndented</c> formatting.
    /// </summary>
    /// <param name="repoPath">The repo whose pin file to inspect.</param>
    /// <returns><see langword="true"/> if the pin file's <c>AgentsMdTemplateDeclined</c>
    ///     property is present and set to <see langword="true"/>.</returns>
    private static bool PinFileRecordsDecline(string repoPath)
    {
        var pinFilePath = Path.Combine(repoPath, ".agentcontrol.json");
        if (!File.Exists(pinFilePath))
        {
            return false;
        }

        var normalized = string.Concat(File.ReadAllText(pinFilePath).Where(c => !char.IsWhiteSpace(c)));
        return normalized.Contains("\"AgentsMdTemplateDeclined\":true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Closes the current app/automation instance owned by <paramref name="context"/> without
    ///     deleting its temp directories, so a fresh <see cref="AgentControlTestContext.Launch"/>
    ///     call can start a new hermetic session against the exact same config/repo directories -
    ///     mirrors the cleanup steps <see cref="AgentControlTestContext.Dispose"/> itself performs
    ///     for the app/automation pair.
    /// </summary>
    /// <param name="context">The context whose current launch should be closed.</param>
    private static void CloseCurrentLaunch(AgentControlTestContext context)
    {
        try
        {
            context.App?.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Best-effort: the process may have already exited.
        }

        try
        {
            context.App?.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Best-effort: the process may have already exited.
        }

        // Dispose the FlaUI Application wrapper itself (not just Close/Kill the OS process),
        // mirroring AgentControlTestContext.Dispose()'s own precedent - otherwise this wrapper
        // leaks across the context's second Launch() call below.
        context.App?.Dispose();
        context.Automation?.Dispose();
    }
}
