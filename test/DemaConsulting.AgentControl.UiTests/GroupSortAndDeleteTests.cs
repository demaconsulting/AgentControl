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
        Assert.Contains("nothing on disk or in any repository is deleted", Find(question, "ConfirmationMessageText").Name, StringComparison.Ordinal);
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
    ///     Choosing "Delete group, keep contents" in a group header's menu and declining changes nothing (the
    ///     group is still listed and still stored); confirming removes the group header and the group from the
    ///     settings but leaves all the repos in the list (ungrouped), the group's repos after the repo that was
    ///     already ungrouped.
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
        Assert.Equal(["Alpha", "Bravo", "elsewhere"], RepoNames(mainWindow));

        // Act: ... on the Work header > Delete group, keep contents, declined first
        Find(mainWindow, "GroupHeaderMenuButton").AsButton().Invoke();
        Find(mainWindow, "RemoveGroupMenuItem").AsMenuItem().Invoke();
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        var keepMessage = Find(question, "ConfirmationMessageText").Name;
        Assert.Contains("Its 2 repo(s) stay in AgentControl's list, ungrouped.", keepMessage, StringComparison.Ordinal);
        Assert.Contains("nothing on disk or in any repository is changed", keepMessage, StringComparison.Ordinal);
        Find(question, "ConfirmationNoButton").AsButton().Invoke();

        // Assert: nothing changed - the group is still listed and still stored for its two repos
        Assert.Equal(["Work, 2 repos, expanded", "Ungrouped, 1 repo, expanded"], HeaderNames(mainWindow));
        Assert.Equal(["Alpha", "Bravo", "elsewhere"], RepoNames(mainWindow));
        Assert.Equal(3, ReadSettings(context).Split("\"Work\"").Length);

        // Act: confirm
        Find(mainWindow, "GroupHeaderMenuButton").AsButton().Invoke();
        Find(mainWindow, "RemoveGroupMenuItem").AsMenuItem().Invoke();
        question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Find(question, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: no group header any more, all three repos still listed by name, the repo that was ungrouped
        // first and the group's repos after it
        Assert.True(Retry.WhileFalse(
            () => mainWindow.FindAllDescendants(cf => cf.ByAutomationId("GroupHeaderToggleButton")).Length == 0 &&
                  RepoNames(mainWindow).SequenceEqual(["elsewhere", "Alpha", "Bravo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Assert: the settings no longer name the group, and the folders are still on disk
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("\"Work\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Alpha")));
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Bravo")));
    }

    /// <summary>
    ///     Choosing "Delete group, keep contents" for a group that is inside another group and has a group
    ///     inside it keeps the repos in the outer group and moves the inner group into the outer group, and
    ///     the settings no longer name the deleted group.
    /// </summary>
    [Fact]
    public void GroupHeaderMenu_DeleteNestedGroupKeepContents_RepoStaysInParentAndSubgroupMovesToParent()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: Gamma in "Outer", Alpha in "Work" (inside Outer), Bravo in "Sub" (inside Work)
        using var context = new AgentControlTestContext(customizeSettings: SeedThreeLevelGroups);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(
            [
                "Outer, 3 repos, expanded",
                "Work, 2 repos, expanded, inside Outer",
                "Sub, 1 repo, expanded, inside Work"
            ]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: ... on the Work header > Delete group, keep contents, confirmed
        Find(
            mainWindow,
            "GroupHeaderMenuButton",
            candidate => candidate.Name == "Actions for group Work").AsButton().Invoke();
        Find(mainWindow, "RemoveGroupMenuItem").AsMenuItem().Invoke();
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Assert.Contains(
            "Its 1 subgroup(s) move",
            Find(question, "ConfirmationMessageText").Name,
            StringComparison.Ordinal);
        Find(question, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: Sub is now inside Outer, Alpha stays in Outer; no Work any more
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(
                ["Outer, 3 repos, expanded", "Sub, 1 repo, expanded, inside Outer"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(3, RepoNames(mainWindow).Count);
        Assert.Contains("Alpha", RepoNames(mainWindow));

        // Assert: the settings no longer name the deleted group, and keep the subgroup inside Outer
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("\"Work\"", StringComparison.Ordinal) &&
                  ReadSettings(context).Contains("\"Sub\": \"Outer\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Seeds the settings with the groups "Outer", "Work" inside it and "Sub" inside Work, one repo in each.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context.</param>
    private static void SeedThreeLevelGroups(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var repos = new[] { ("Gamma", "Outer"), ("Alpha", "Work"), ("Bravo", "Sub") }
            .Select(entry => (Path: Path.Combine(context.RootDirectory, entry.Item1), Group: entry.Item2))
            .ToList();
        foreach (var repo in repos)
        {
            Directory.CreateDirectory(repo.Path);
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = repos.Select(repo => (object)new { repo.Path, GroupName = repo.Group }).ToArray();
        settings["GroupParents"] = new Dictionary<string, string?> { ["Work"] = "Outer", ["Sub"] = "Work" };
    }

    /// <summary>
    ///     Lists the names of the section headers currently shown, in order.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The name of each <c>GroupHeaderToggleButton</c>.</returns>
    private static List<string> HeaderNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("GroupHeaderToggleButton"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];


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
    /// <param name="filter">An extra condition the element has to meet, or <see langword="null"/> for none.</param>
    /// <returns>The element.</returns>
    private static AutomationElement Find(
        AutomationElement parent, string automationId, Func<AutomationElement, bool>? filter = null)
    {
        var element = Retry.WhileNull(
            () => parent.FindAllDescendants(cf => cf.ByAutomationId(automationId))
                .FirstOrDefault(candidate => filter is null || filter(candidate)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }
}
