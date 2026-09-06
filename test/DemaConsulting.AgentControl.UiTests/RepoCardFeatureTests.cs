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
///     End-to-end tests for the repo-card feature set's filter box and remove-with-confirmation
///     flow, reusing the same <see cref="AgentControlTestContext"/>/<see cref="TestSettingsWriter"/>
///     harness (and its single pre-seeded repo) as <see cref="LaunchAndPullTests"/> and
///     <see cref="UpgradeTests"/>.
/// </summary>
public sealed class RepoCardFeatureTests
{
    /// <summary>
    ///     Typing a substring that does not match the seeded repo's name/path into the filter box
    ///     must hide its card, and clearing the filter must bring it back.
    /// </summary>
    [Fact]
    public void FilterTextBox_TypingNonMatchingText_HidesRepoCard_ClearingRestoresIt()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        var repoNameText = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText"));
        Assert.NotNull(repoNameText);

        var filterTextBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FilterTextBox"))?.AsTextBox();
        Assert.NotNull(filterTextBox);

        // Act: type a substring guaranteed not to appear in the seeded repo's directory name
        // (a GUID-suffixed temp folder) or its display name.
        filterTextBox.Text = "no-such-repo-should-match-this-filter-text";

        // Assert: the card disappears from the automation tree.
        var hidden = Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200));
        Assert.True(hidden.Success);

        // Act: clear the filter
        filterTextBox.Text = string.Empty;

        // Assert: the card reappears.
        var restored = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200));
        Assert.NotNull(restored.Result);
    }

    /// <summary>
    ///     Clicking "Remove from list" then confirming "No" must leave the repo card in place;
    ///     confirming "Yes" must remove it and persist the removal.
    /// </summary>
    /// <remarks>
    ///     Uses two independent <see cref="AgentControlTestContext"/> launches (one per outcome)
    ///     rather than reusing a single app instance for both the decline and confirm
    ///     interactions - reopening the same <c>MenuFlyout</c> a second time in one session
    ///     proved unreliable to drive via UI Automation in this environment (the flyout's own
    ///     light-dismiss/close bookkeeping did not reliably allow a second open), so each outcome
    ///     gets its own hermetic launch instead.
    /// </remarks>
    [Fact]
    public void RemoveMenuItem_ClickThenConfirm_RemovesCard_DecliningLeavesItInPlace()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Declining must leave the card in place.
        using (var declineContext = new AgentControlTestContext())
        {
            var mainWindow = declineContext.Launch();

            InvokeRemoveMenuItem(mainWindow);
            var confirmationWindow = WaitForConfirmationWindow(declineContext);
            Assert.NotNull(confirmationWindow);

            var noButton = confirmationWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationNoButton"))?.AsButton();
            Assert.NotNull(noButton);
            noButton.Invoke();

            var stillPresent = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200));
            Assert.NotNull(stillPresent.Result);
        }

        // Confirming must remove the card and persist the removal.
        using (var confirmContext = new AgentControlTestContext())
        {
            var mainWindow = confirmContext.Launch();

            InvokeRemoveMenuItem(mainWindow);
            var confirmationWindow = WaitForConfirmationWindow(confirmContext);
            Assert.NotNull(confirmationWindow);

            var yesButton = confirmationWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmationYesButton"))?.AsButton();
            Assert.NotNull(yesButton);
            yesButton.Invoke();

            var removed = Retry.WhileNotNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200));
            Assert.True(removed.Success);

            var settingsJson = Retry.WhileTrue(
                () => File.ReadAllText(Path.Combine(confirmContext.ConfigDirectory, "settings.json"))
                    .Contains(confirmContext.RepoPath, StringComparison.OrdinalIgnoreCase),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(200));
            Assert.True(settingsJson.Success);
        }
    }

    /// <summary>
    ///     Opens the seeded repo card's "..." menu flyout and clicks its "Remove from list" item.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    private static void InvokeRemoveMenuItem(Window mainWindow)
    {
        // Ensure this window (rather than whatever else may currently have focus, e.g. a
        // previous test's now-closing window) is the one that receives the click below.
        mainWindow.SetForeground();

        var moreActionsButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result?.AsButton();
        Assert.NotNull(moreActionsButton);
        moreActionsButton.Invoke();

        var removeMenuItem = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RemoveMenuItem")),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
        Assert.NotNull(removeMenuItem);
        removeMenuItem.Click();
    }

    /// <summary>
    ///     Waits for the modal <c>ConfirmationWindow</c> to appear as a top-level window.
    /// </summary>
    /// <param name="context">The test context whose <see cref="AgentControlTestContext.App"/>/
    ///     <see cref="AgentControlTestContext.Automation"/> are used to enumerate top-level
    ///     windows.</param>
    /// <param name="timeout">Maximum time to wait; defaults to 10 seconds.</param>
    /// <returns>The confirmation window, or <see langword="null"/> if it did not appear in time.</returns>
    private static Window? WaitForConfirmationWindow(AgentControlTestContext context, TimeSpan? timeout = null)
    {
        if (context.App is null || context.Automation is null)
        {
            return null;
        }

        return Retry.WhileNull(
            () => context.App.GetAllTopLevelWindows(context.Automation)
                .FirstOrDefault(window => window.FindFirstDescendant(
                    cf => cf.ByAutomationId("ConfirmationMessageText")) is not null),
            timeout: timeout ?? TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200)).Result;
    }
}
