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
///     End-to-end tests of the user-specific repo groups and card colors in the real application:
///     moving a repo to a new group through the "..." menu, choosing a card color from the palette,
///     and finding a repo with the filter although its group is collapsed. Every choice is checked
///     to be stored in AgentControl's settings only, never in the repository.
/// </summary>
public sealed class RepoGroupsAndColorsTests
{
    /// <summary>
    ///     Choosing "Move to group" then "New group...", typing a name and confirming shows a header
    ///     for the new group with the repo under it, and stores the group in the settings file only.
    /// </summary>
    [Fact]
    public void CardMenu_MoveToNewGroup_ShowsGroupHeaderAndStoresInSettingsOnly()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Assert: a flat list to begin with, so no header
        Assert.NotNull(Find(mainWindow, "RepoNameText"));
        Assert.Null(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("GroupHeaderToggleButton")));

        // Act: ... > Move to group > New group...
        Find(mainWindow, "MoreActionsButton").AsButton().Invoke();
        var moveToGroup = Find(mainWindow, "MoveToGroupMenuItem");
        ExpandMenuItem(moveToGroup);
        Find(mainWindow, "NewGroupMenuItem").AsMenuItem().Invoke();

        // Act: type a name in the prompt and confirm
        var prompt = context.WaitForWindowWithDescendant("GroupNameTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(prompt);
        Find(prompt, "GroupNameTextBox").AsTextBox().Text = "Client X";
        Find(prompt, "GroupNameAcceptButton").AsButton().Invoke();

        // Assert: the header names the group, counts its repo, and the repo is still listed
        var header = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("GroupHeaderToggleButton")),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(header);
        Assert.Equal("Client X, 1 repo, expanded", header.Name);
        Assert.NotNull(mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText")));

        // Assert: stored in the settings, and nothing was written into the repository
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"GroupName\": \"Client X\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(context.RepoPath));
    }

    /// <summary>
    ///     Choosing "Card color..." opens the palette; choosing a swatch stores the color by name in
    ///     the settings file only, and the status accent keeps its meaning.
    /// </summary>
    [Fact]
    public void CardMenu_CardColor_ChooseSwatchStoresColorInSettingsOnly()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        var statusBefore = Find(mainWindow, "RepoStatusAccent").Name;

        // Act: ... > Card color...
        Find(mainWindow, "MoreActionsButton").AsButton().Invoke();
        Find(mainWindow, "CardColorMenuItem").AsMenuItem().Invoke();

        // Assert: the palette offers every color and Automatic
        Assert.NotNull(Find(mainWindow, "CardColorPalette"));
        Assert.NotNull(Find(mainWindow, "CardColorAutomaticButton"));
        var teal = Find(mainWindow, "CardColorSwatch_Teal");
        Assert.Equal("Teal", teal.Name);

        // Act: choose Teal
        teal.AsButton().Invoke();

        // Assert: stored by name, the repository is untouched, and the status is unchanged
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"CardColor\": \"Teal\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(context.RepoPath));
        Assert.Equal(statusBefore, Find(mainWindow, "RepoStatusAccent").Name);
    }

    /// <summary>
    ///     A repo in a collapsed group is not listed, but typing its path in the filter box finds it
    ///     (the filter expands the group), and clearing the filter collapses the group again.
    /// </summary>
    [Fact]
    public void FilterTextBox_RepoInCollapsedGroup_IsFoundByFilterAndHiddenAgainAfterwards()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the seeded repo is in the collapsed group "Work"; another repo is ungrouped
        using var context = new AgentControlTestContext(customizeSettings: (settings, self) =>
        {
            var other = Path.Combine(self.RootDirectory, "elsewhere");
            Directory.CreateDirectory(other);
            settings["RecentRepos"] = new object[]
            {
                new { Path = self.RepoPath, GroupName = "Work" },
                new { Path = other }
            };
            settings["CollapsedGroups"] = new[] { "Work" };
        });
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Assert: the group's header is shown collapsed, and only the ungrouped repo is listed
        var work = Find(mainWindow, "GroupHeaderToggleButton", header => header.Name.StartsWith("Work", StringComparison.Ordinal));
        Assert.Equal("Work, 1 repo, collapsed", work.Name);
        Assert.Equal(["elsewhere"], RepoNames(mainWindow));

        // Act: filter on the seeded repo's path
        var filterTextBox = Find(mainWindow, "FilterTextBox").AsTextBox();
        filterTextBox.Text = context.RepoPath;

        // Assert: the repo inside the collapsed group is found, and the other one is filtered out
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["repo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: clear the filter
        filterTextBox.Text = string.Empty;

        // Assert: the remembered collapsed state applies again
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["elsewhere"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Contains("\"Work\"", ReadSettings(context), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Favoriting an unfavorited repo (the star turns on, the repo is listed under a new Favorites
    ///     section and the choice is stored) and unfavoriting it again (back under its group) both work:
    ///     one toggle changes the state once, and the repo moves between the sections.
    /// </summary>
    [Fact]
    public void FavoriteToggleButton_ToggledOnThenOff_MovesRepoBetweenSectionsAndStoresChoice()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the seeded repo is in the group "Work"; another repo is ungrouped
        using var context = new AgentControlTestContext(customizeSettings: (settings, self) =>
        {
            var other = Path.Combine(self.RootDirectory, "elsewhere");
            Directory.CreateDirectory(other);
            settings["RecentRepos"] = new object[]
            {
                new { Path = self.RepoPath, GroupName = "Work" },
                new { Path = other }
            };
        });
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["repo", "elsewhere"], RepoNames(mainWindow));
        Assert.Equal("Favorite", Find(mainWindow, "FavoriteToggleButton").Name);

        // Act: favorite the repo in the group (the first card in the list)
        Find(mainWindow, "FavoriteToggleButton").Patterns.Toggle.Pattern.Toggle();

        // Assert: a Favorites section now holds it, its star is on, and the choice is stored
        var favorites = Find(mainWindow, "GroupHeaderToggleButton", header => header.Name.StartsWith("Favorites", StringComparison.Ordinal));
        Assert.Equal("Favorites, 1 repo, expanded", favorites.Name);
        Assert.Equal(
            FlaUI.Core.Definitions.ToggleState.On,
            Find(mainWindow, "FavoriteToggleButton").Patterns.Toggle.Pattern.ToggleState.Value);
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"IsFavorite\": true", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: unfavorite it again
        Find(mainWindow, "FavoriteToggleButton").Patterns.Toggle.Pattern.Toggle();

        // Assert: the Favorites section is gone, the star is off, the choice is stored
        Assert.True(Retry.WhileFalse(
            () => mainWindow.FindAllDescendants(cf => cf.ByAutomationId("GroupHeaderToggleButton"))
                .All(header => !header.Name.StartsWith("Favorites", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(
            FlaUI.Core.Definitions.ToggleState.Off,
            Find(mainWindow, "FavoriteToggleButton").Patterns.Toggle.Pattern.ToggleState.Value);
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("\"IsFavorite\": true", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
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
    ///     Lists the names of the repo cards currently shown, in order.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The text of each <c>RepoNameText</c>.</returns>
    private static List<string> RepoNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoNameText")).Select(element => element.Name)];

    /// <summary>
    ///     Reads the persisted settings file, or an empty string when it does not exist yet.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The file's text.</returns>
    private static string ReadSettings(AgentControlTestContext context)
    {
        var path = Path.Combine(context.ConfigDirectory, "settings.json");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>
    ///     Finds an element by AutomationId (optionally the first one that also passes a filter),
    ///     waiting for it to appear.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <param name="filter">An extra condition the element must meet, or <see langword="null"/>.</param>
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
