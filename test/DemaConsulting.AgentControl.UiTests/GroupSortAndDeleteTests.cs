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
///     End-to-end tests of sorting a group's repos by name from the group header's menu in the real
///     application, and of deleting a group - keeping what is in it, and together with its repos.
/// </summary>
public sealed class GroupSortAndDeleteTests
{
    /// <summary>
    ///     Choosing "Sort by name" in a group header's menu lists the group's repos in name order, saves
    ///     that order in the settings file, and shows the same order after a restart.
    /// </summary>
    [Fact]
    public void GroupHeaderMenu_SortByName_ListsRepoCardsInNameOrderAndKeepsItAfterRestart()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: three repos in the group "Work", stored in the order Charlie, Alpha, Bravo
        using var context = new AgentControlTestContext(customizeSettings: SeedUnsortedWorkGroup);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Charlie", "Alpha", "Bravo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: ... on the Work header > Sort by name
        Find(mainWindow, "GroupHeaderMenuButton").AsButton().Invoke();
        Find(mainWindow, "GroupSortByNameMenuItem").AsMenuItem().Invoke();

        // Assert: the cards are in name order
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Alpha", "Bravo", "Charlie"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: restart the application against the same settings
        context.CloseApplication();
        var restarted = context.Launch();
        restarted.SetForeground();

        // Assert: the same order
        Assert.True(Retry.WhileFalse(
            () => RepoNames(restarted).SequenceEqual(["Alpha", "Bravo", "Charlie"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Choosing "Delete group and its contents" in a group header's menu and confirming removes the
    ///     group and its repos from the list and from the settings file, leaves the repos' folders on disk
    ///     and the ungrouped repo in the list; declining first changes nothing.
    /// </summary>
    [Fact]
    public void GroupHeaderMenu_DeleteGroupAndItsContents_RemovesGroupAndReposFromTheListAndKeepsFoldersOnDisk()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: two repos in the group "Work" and one repo in no group
        using var context = new AgentControlTestContext(customizeSettings: SeedWorkGroupWithTwoRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Act: ... on the Work header > Delete group and its contents, declined first
        Find(mainWindow, "GroupHeaderMenuButton").AsButton().Invoke();
        Find(mainWindow, "RemoveGroupWithContentsMenuItem").AsMenuItem().Invoke();
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Assert.Contains("2 repo(s)", Find(question, "ConfirmationMessageText").Name, StringComparison.Ordinal);
        Find(question, "ConfirmationNoButton").AsButton().Invoke();

        // Assert: nothing changed
        Assert.Equal(3, RepoNames(mainWindow).Count);

        // Act: confirm
        Find(mainWindow, "GroupHeaderMenuButton").AsButton().Invoke();
        Find(mainWindow, "RemoveGroupWithContentsMenuItem").AsMenuItem().Invoke();
        question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Find(question, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: only the ungrouped repo is listed, the settings no longer name the repos or the group, and
        // the folders are still on disk
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["elsewhere"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("\"Work\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Alpha")));
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Bravo")));
    }

    /// <summary>
    ///     Choosing "Delete group, keep contents" in a group header's menu and confirming removes the group
    ///     header but leaves all the repos in the list (ungrouped).
    /// </summary>
    [Fact]
    public void GroupHeaderMenu_DeleteGroupKeepContents_RemovesTheHeaderAndKeepsAllRepos()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: two repos in the group "Work" and one repo in no group
        using var context = new AgentControlTestContext(customizeSettings: SeedWorkGroupWithTwoRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Act: ... on the Work header > Delete group, keep contents, confirmed
        Find(mainWindow, "GroupHeaderMenuButton").AsButton().Invoke();
        Find(mainWindow, "RemoveGroupMenuItem").AsMenuItem().Invoke();
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Find(question, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: no group header any more, all three repos still listed
        Assert.True(Retry.WhileFalse(
            () => mainWindow.FindAllDescendants(cf => cf.ByAutomationId("GroupHeaderToggleButton")).Length == 0 &&
                  RepoNames(mainWindow).Count == 3,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Seeds the settings with two repos in the group "Work" and a third repo in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context.</param>
    private static void SeedWorkGroupWithTwoRepos(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var alpha = Path.Combine(context.RootDirectory, "Alpha");
        var bravo = Path.Combine(context.RootDirectory, "Bravo");
        var other = Path.Combine(context.RootDirectory, "elsewhere");
        foreach (var path in new[] { alpha, bravo, other })
        {
            Directory.CreateDirectory(path);
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = new object[]
        {
            new { Path = alpha, GroupName = "Work" },
            new { Path = bravo, GroupName = "Work" },
            new { Path = other }
        };
    }

    /// <summary>
    ///     Reads the persisted settings file, or an empty string when it does not exist yet.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The file's text.</returns>
    private static string ReadSettings(AgentControlTestContext context)
    {
        var path = Path.Combine(context.ConfigDirectory, "settings.json");
        return File.Exists(path) ? SharedFile.ReadAllText(path) : string.Empty;
    }

    /// <summary>
    ///     Seeds the settings with three repos in the group "Work", stored out of name order.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context.</param>
    private static void SeedUnsortedWorkGroup(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var paths = new[] { "Charlie", "Alpha", "Bravo" }
            .Select(name => Path.Combine(context.RootDirectory, name))
            .ToList();
        foreach (var path in paths)
        {
            Directory.CreateDirectory(path);
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = paths.Select(path => (object)new { Path = path, GroupName = "Work" }).ToArray();
    }

    /// <summary>
    ///     Lists the names of the repo cards currently shown, from the top of the list down.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The text of each <c>RepoNameText</c>.</returns>
    private static List<string> RepoNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoNameText"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];

    /// <summary>
    ///     Finds an element by AutomationId, waiting for it to appear.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <returns>The element.</returns>
    private static AutomationElement Find(AutomationElement parent, string automationId)
    {
        var element = Retry.WhileNull(
            () => parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }
}
