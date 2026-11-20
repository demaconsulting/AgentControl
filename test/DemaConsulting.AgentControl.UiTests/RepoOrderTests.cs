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

using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of the user's own repo order in the real application: a card keeps its place
///     when its repo is launched, the card menu's Reorder items reorder it, a real mouse drag reorders
///     it (and moves it into a group when dropped on the group's header), and the order is stored only
///     in AgentControl's settings and is still there after a restart; and the order of the groups,
///     changed from a group header's menu or by dragging a header.
/// </summary>
public sealed class RepoOrderTests
{
    /// <summary>
    ///     Choosing Reorder > "Move down" in the first card's menu swaps it with the second card, stores the order in
    ///     the settings file and keeps it after a restart.
    /// </summary>
    [Fact]
    public void CardMenu_MoveDown_ReordersCardAndStoresOrderInSettings()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: three repos in the user's order
        using var context = new AgentControlTestContext(customizeSettings: SeedThreeRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["repo", "second", "third"], RepoNames(mainWindow));

        // Act: ... > Organize > Reorder > Move down on the first card
        Find(mainWindow, "MoreActionsButton", index: 0).AsButton().Invoke();
        ExpandMenuItem(Find(mainWindow, "ReorderMenuItem"));
        Assert.False(Find(mainWindow, "MoveUpMenuItem").IsEnabled);
        Find(mainWindow, "MoveDownMenuItem").AsMenuItem().Invoke();

        // Assert: swapped, stored in the settings and not written into the repository
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["second", "repo", "third"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => StoredOrder(context).SequenceEqual(["second", "repo", "third"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(context.RepoPath));

        // Act: restart against the same settings
        context.CloseApplication();
        var restarted = context.Launch();
        restarted.SetForeground();

        // Assert
        Assert.Equal(["second", "repo", "third"], RepoNames(restarted));
    }

    /// <summary>
    ///     Launching the second repo records the launch but leaves the card where it is.
    /// </summary>
    [Fact]
    public void RepoList_LaunchingARepo_KeepsTheCardWhereItIs()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: launch times that the old "most recently launched first" order would have used
        using var context = new AgentControlTestContext(customizeSettings: (settings, self) =>
        {
            SeedThreeRepos(settings, self);
            settings["RecentRepos"] = new object[]
            {
                new { Path = self.RepoPath, LastLaunchedUtc = DateTimeOffset.UtcNow.AddDays(-1) },
                new { Path = Path.Combine(self.RootDirectory, "second"), LastLaunchedUtc = DateTimeOffset.UtcNow.AddDays(-9) },
                new { Path = Path.Combine(self.RootDirectory, "third") }
            };
        });
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["repo", "second", "third"], RepoNames(mainWindow));

        // Act: launch the last card
        Find(mainWindow, "LaunchButton", index: 2).AsButton().Invoke();

        // Assert: the launch happened (the stub recorded the marker) and nothing moved
        Assert.True(Retry.WhileFalse(
            () => File.Exists(context.ArgLoggerOutputFile)
                  && File.ReadAllText(context.ArgLoggerOutputFile).Contains(
                      TestSettingsWriter.AgentToolMarkerArgument, StringComparison.Ordinal),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(250)).Result);
        Assert.Equal(["repo", "second", "third"], RepoNames(mainWindow));
        Assert.Equal(["repo", "second", "third"], StoredOrder(context));
    }

    /// <summary>
    ///     Dragging the first card with the real mouse and dropping it on the lower half of the last card
    ///     puts it last, stores the order and keeps it after a restart.
    /// </summary>
    [Fact]
    public void RepoList_DragCardBelowAnother_ReordersAndKeepsOrderAfterRestart()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange
        using var context = new AgentControlTestContext(customizeSettings: SeedThreeRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["repo", "second", "third"], RepoNames(mainWindow));

        // Act: grab the first card by its status bar and drop it below the last card
        var accents = Accents(mainWindow);
        var grab = Center(accents[0]);
        var below = new System.Drawing.Point(grab.X, accents[2].BoundingRectangle.Bottom - 6);
        Drag(grab, below);

        // Assert: last, stored, and still last after a restart
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["second", "third", "repo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => StoredOrder(context).SequenceEqual(["second", "third", "repo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        context.CloseApplication();
        var restarted = context.Launch();
        restarted.SetForeground();
        Assert.Equal(["second", "third", "repo"], RepoNames(restarted));
    }

    /// <summary>
    ///     Dragging an ungrouped card with the real mouse and dropping it on a group's header puts it in
    ///     that group, and the group is stored for the repo in the settings only.
    /// </summary>
    [Fact]
    public void RepoList_DragCardOntoGroupHeader_MovesItIntoTheGroup()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the first repo is in the group "Work"; the other two are in no group
        using var context = new AgentControlTestContext(customizeSettings: (settings, self) =>
        {
            SeedThreeRepos(settings, self);
            settings["RecentRepos"] = new object[]
            {
                new { Path = self.RepoPath, GroupName = "Work" },
                new { Path = Path.Combine(self.RootDirectory, "second") },
                new { Path = Path.Combine(self.RootDirectory, "third") }
            };
        });
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["Work, 1 repo, expanded", "Ungrouped, 2 repos, expanded"], HeaderNames(mainWindow));

        // Act: grab the middle card (the last card's status bar sits in the list's bottom auto-scroll zone in the
        // default window, which would scroll the list during the drag) and drop it on the Work header
        var accents = Accents(mainWindow);
        var header = Find(mainWindow, "GroupHeaderToggleButton", candidate => candidate.Name.StartsWith("Work", StringComparison.Ordinal));
        Drag(Center(accents[1]), Center(header));

        // Assert: Work now has two repos, Ungrouped one, and the group is stored for the dropped repo
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Work, 2 repos, expanded", "Ungrouped, 1 repo, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(["repo", "second", "third"], RepoNames(mainWindow));
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Split("\"GroupName\": \"Work\"").Length == 3,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(context.RootDirectory, "second")));
    }

    /// <summary>
    ///     Choosing "Move down" in the first group header's menu puts that group after the next one,
    ///     stores the order of the groups in the settings file and keeps it after a restart.
    /// </summary>
    [Fact]
    public void GroupHeaderMenu_MoveDown_ReordersGroupsAndKeepsOrderAfterRestart()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: two groups (Alpha, Beta) and an ungrouped repo
        using var context = new AgentControlTestContext(customizeSettings: SeedTwoGroups);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["Alpha, 1 repo, expanded", "Beta, 1 repo, expanded", "Ungrouped, 1 repo, expanded"], HeaderNames(mainWindow));

        // Act: ... on the Alpha header > Move down
        Find(mainWindow, "GroupHeaderMenuButton", candidate => candidate.Name == "Actions for group Alpha").AsButton().Invoke();
        Assert.False(Find(mainWindow, "MoveGroupUpMenuItem").IsEnabled);
        Find(mainWindow, "MoveGroupDownMenuItem").AsMenuItem().Invoke();

        // Assert: Beta first, stored, and still so after a restart
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Beta, 1 repo, expanded", "Alpha, 1 repo, expanded", "Ungrouped, 1 repo, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => StoredGroupOrder(context).SequenceEqual(["Beta", "Alpha"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        context.CloseApplication();
        var restarted = context.Launch();
        restarted.SetForeground();
        Assert.Equal(["Beta, 1 repo, expanded", "Alpha, 1 repo, expanded", "Ungrouped, 1 repo, expanded"], HeaderNames(restarted));
    }

    /// <summary>
    ///     Dragging the first group's header with the real mouse and dropping it on the lower half of the
    ///     next group's header puts that group after it, without collapsing it.
    /// </summary>
    [Fact]
    public void RepoList_DragGroupHeaderBelowAnother_ReordersGroups()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange
        using var context = new AgentControlTestContext(customizeSettings: SeedTwoGroups);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Act: grab the Alpha header and drop it on the lower half of the Beta header
        var alpha = Find(mainWindow, "GroupHeaderToggleButton", candidate => candidate.Name.StartsWith("Alpha", StringComparison.Ordinal));
        var beta = Find(mainWindow, "GroupHeaderToggleButton", candidate => candidate.Name.StartsWith("Beta", StringComparison.Ordinal));
        var below = new System.Drawing.Point(Center(beta).X, beta.BoundingRectangle.Bottom - 3);
        Drag(Center(alpha), below);

        // Assert: Beta first, Alpha still expanded, and the order stored
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Beta, 1 repo, expanded", "Alpha, 1 repo, expanded", "Ungrouped, 1 repo, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => StoredGroupOrder(context).SequenceEqual(["Beta", "Alpha"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Seeds the settings with the test repo in the group "Alpha", a second repo in the group "Beta"
    ///     and a third in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context, whose repo is the first one.</param>
    private static void SeedTwoGroups(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        SeedThreeRepos(settings, context);
        settings["RecentRepos"] = new object[]
        {
            new { Path = context.RepoPath, GroupName = "Alpha" },
            new { Path = Path.Combine(context.RootDirectory, "second"), GroupName = "Beta" },
            new { Path = Path.Combine(context.RootDirectory, "third") }
        };
    }

    /// <summary>
    ///     Reads the stored order of the groups from the settings file.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The group names in <c>GroupOrder</c>, or none when the file or the list is missing.</returns>
    private static List<string> StoredGroupOrder(AgentControlTestContext context)
    {
        var text = ReadSettings(context);
        if (text.Length == 0)
        {
            return [];
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("GroupOrder", out var order) && order.ValueKind == JsonValueKind.Array
            ? [.. order.EnumerateArray().Select(name => name.GetString() ?? string.Empty)]
            : [];
    }

    /// <summary>
    ///     Seeds the settings with three repos in the user's own order (the test repo, "second" and
    ///     "third"), stored as such.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context, whose repo is the first one.</param>
    private static void SeedThreeRepos(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        foreach (var name in new[] { "second", "third" })
        {
            Directory.CreateDirectory(Path.Combine(context.RootDirectory, name));
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = new object[]
        {
            new { Path = context.RepoPath },
            new { Path = Path.Combine(context.RootDirectory, "second") },
            new { Path = Path.Combine(context.RootDirectory, "third") }
        };
    }

    /// <summary>
    ///     Presses the left mouse button at a point, moves to another point in small steps (an
    ///     operating-system drag needs a moving pointer) and releases it there.
    /// </summary>
    /// <param name="from">Where to press.</param>
    /// <param name="to">Where to release.</param>
    private static void Drag(System.Drawing.Point from, System.Drawing.Point to)
    {
        Mouse.MoveTo(from);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(150);
        const int steps = 12;
        for (var step = 1; step <= steps; step++)
        {
            Mouse.MoveTo(new System.Drawing.Point(
                from.X + ((to.X - from.X) * step / steps), from.Y + ((to.Y - from.Y) * step / steps)));
            Thread.Sleep(40);
        }

        Thread.Sleep(150);
        Mouse.Up(MouseButton.Left);
        Thread.Sleep(300);
    }

    /// <summary>
    ///     Gets the status bars of the cards (each as tall as its card and not one of its controls), top
    ///     to bottom; a drag is started on one.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The status bars.</returns>
    private static List<AutomationElement> Accents(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoStatusAccent"))
            .OrderBy(element => element.BoundingRectangle.Top)];

    /// <summary>
    ///     Gets the center of an element on the screen.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The center point.</returns>
    private static System.Drawing.Point Center(AutomationElement element)
    {
        var bounds = element.BoundingRectangle;
        return new System.Drawing.Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }

    /// <summary>
    ///     Expands a menu item that has a submenu, through its UI Automation expand pattern when it
    ///     has one, else by invoking it.
    /// </summary>
    /// <param name="item">The menu item.</param>
    private static void ExpandMenuItem(AutomationElement item)
    {
        var expand = item.Patterns.ExpandCollapse.PatternOrDefault;
        if (expand is not null)
        {
            expand.Expand();
        }
        else
        {
            item.AsMenuItem().Invoke();
        }
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
    ///     Lists the names of the repo cards currently shown, top to bottom.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The text of each <c>RepoNameText</c>.</returns>
    private static List<string> RepoNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoNameText"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];

    /// <summary>
    ///     Reads the folder names of the repos in the settings file, in the stored order.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The folder names, or none when the file or the list is missing.</returns>
    private static List<string> StoredOrder(AgentControlTestContext context)
    {
        var text = ReadSettings(context);
        if (text.Length == 0)
        {
            return [];
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("RecentRepos", out var repos) && repos.ValueKind == JsonValueKind.Array
            ? [.. repos.EnumerateArray().Select(repo => Path.GetFileName(repo.GetProperty("Path").GetString() ?? string.Empty))]
            : [];
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
    ///     Finds an element by AutomationId (optionally one that also passes a filter, and optionally the
    ///     n-th such element, as repeated card controls exist once per card), waiting for it to appear.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <param name="filter">An extra condition the element must meet, or <see langword="null"/>.</param>
    /// <param name="index">Which of the matching elements to return, counting from zero.</param>
    /// <returns>The element.</returns>
    private static AutomationElement Find(
        AutomationElement parent, string automationId, Func<AutomationElement, bool>? filter = null, int index = 0)
    {
        var element = Retry.WhileNull(
            () => parent.FindAllDescendants(cf => cf.ByAutomationId(automationId))
                .Where(candidate => filter is null || filter(candidate))
                .OrderBy(candidate => candidate.BoundingRectangle.Top)
                .ElementAtOrDefault(index),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }
}
