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
using FlaUI.Core.Definitions;
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
    ///     Choosing "Move to group" and then an existing group puts the repo in that group's section: the
    ///     section now counts both repos, the section the repo left is gone, and the choice is stored in
    ///     the settings file only.
    /// </summary>
    [Fact]
    public void CardMenu_MoveToExistingGroup_JoinsThatSectionAndStoresInSettingsOnly()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: one repo in the group "Work", another in no group
        using var context = new AgentControlTestContext(customizeSettings: SeedWorkGroupAndUngroupedRepo);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["repo", "elsewhere"], RepoNames(mainWindow));

        // Act: ... > Move to group > Work, on the second (ungrouped) repo's card
        Find(mainWindow, "MoreActionsButton", index: 1).AsButton().Invoke();
        ExpandMenuItem(Find(mainWindow, "MoveToGroupMenuItem"));
        Find(mainWindow, "MoveToGroupItem", candidate => candidate.Name == "Work").AsMenuItem().Invoke();

        // Assert: one section for both repos, and no section for the ungrouped ones any more
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Work, 2 repos, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(2, RepoNames(mainWindow).Count);

        // Assert: stored for both repos in the settings, and nothing was written into either repository
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Split("\"GroupName\": \"Work\"").Length == 3,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(context.RepoPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(context.RootDirectory, "elsewhere")));
    }

    /// <summary>
    ///     Choosing "Remove from group" takes the repo out of its group: with no repo left in any group the
    ///     list is flat again (no section headers), and the group is no longer stored for the repo.
    /// </summary>
    [Fact]
    public void CardMenu_RemoveFromGroup_ReturnsRepoToUngroupedAndRemovesTheHeader()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the only repo is in the group "Work", so there is one section header
        using var context = new AgentControlTestContext(customizeSettings: (settings, self) =>
            settings["RecentRepos"] = new object[] { new { Path = self.RepoPath, GroupName = "Work" } });
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Assert.Equal(["Work, 1 repo, expanded"], HeaderNames(mainWindow));

        // Act: ... > Move to group > Remove from group
        Find(mainWindow, "MoreActionsButton").AsButton().Invoke();
        ExpandMenuItem(Find(mainWindow, "MoveToGroupMenuItem"));
        Find(mainWindow, "RemoveFromGroupMenuItem").AsMenuItem().Invoke();

        // Assert: a flat list again, with the repo still listed, and no group stored for it
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).Count == 0,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(["repo"], RepoNames(mainWindow));
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("\"GroupName\": \"Work\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     The sections are listed favorites first, then the groups alphabetically, then the repos in no
    ///     group, each with its repos under it (a favorite appears only under Favorites).
    /// </summary>
    [Fact]
    public void RepoList_FavoritesAndGroups_SectionsAreFavoritesThenGroupsAlphabeticallyThenUngrouped()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a favorite in "Zeta", another repo in "Zeta", one in "Alpha" and one in no group
        using var context = new AgentControlTestContext(customizeSettings: (settings, self) =>
        {
            string[] names = ["zeta-favorite", "zeta-plain", "alpha-one", "ungrouped-one"];
            foreach (var name in names)
            {
                Directory.CreateDirectory(Path.Combine(self.RootDirectory, name));
            }

            settings["RecentRepos"] = new object[]
            {
                new { Path = Path.Combine(self.RootDirectory, "zeta-favorite"), GroupName = "Zeta", IsFavorite = true },
                new { Path = Path.Combine(self.RootDirectory, "zeta-plain"), GroupName = "Zeta" },
                new { Path = Path.Combine(self.RootDirectory, "alpha-one"), GroupName = "Alpha" },
                new { Path = Path.Combine(self.RootDirectory, "ungrouped-one") }
            };
        });

        // Act
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // The list shows only the rows that fit, so give it the whole screen to show all eight rows
        mainWindow.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);

        // Assert: the sections in order, and each repo under its own section
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).Count == 4,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(
            [
                "Favorites, 1 repo, expanded",
                "Alpha, 1 repo, expanded",
                "Zeta, 1 repo, expanded",
                "Ungrouped, 1 repo, expanded"
            ],
            HeaderNames(mainWindow));
        Assert.Equal(["zeta-favorite", "alpha-one", "zeta-plain", "ungrouped-one"], RepoNames(mainWindow));
    }

    /// <summary>
    ///     Clicking a section header collapses it (its repos leave the list) and stores that choice; the
    ///     section is still collapsed after a restart, and clicking the header again expands it.
    /// </summary>
    [Fact]
    public void GroupHeaderToggleButton_Click_CollapsesSectionAndRemembersItAfterRestart()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: one repo in the group "Work", another in no group
        using var context = new AgentControlTestContext(customizeSettings: SeedWorkGroupAndUngroupedRepo);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        var work = Find(
            mainWindow,
            "GroupHeaderToggleButton",
            header => header.Name.StartsWith("Work", StringComparison.Ordinal));
        Assert.Equal("Work, 1 repo, expanded", work.Name);

        // Act: collapse the section
        work.AsButton().Invoke();

        // Assert: its repo left the list, and the collapsed section is stored
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["elsewhere"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Contains("Work, 1 repo, collapsed", HeaderNames(mainWindow));
        Assert.True(Retry.WhileFalse(
            () => CollapsedGroupsInSettings(context).SequenceEqual(["Work"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: restart the application against the same settings
        context.CloseApplication();
        var restarted = context.Launch();
        restarted.SetForeground();

        // Assert: the section is still collapsed
        Assert.Contains("Work, 1 repo, collapsed", HeaderNames(restarted));
        Assert.Equal(["elsewhere"], RepoNames(restarted));

        // Act: expand the section again
        Find(restarted, "GroupHeaderToggleButton", header => header.Name.StartsWith("Work", StringComparison.Ordinal))
            .AsButton().Invoke();

        // Assert: both repos are listed and the collapsed state is no longer stored
        Retry.WhileFalse(
            () => RepoNames(restarted).Count == 2,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200));
        Assert.Equal(["repo", "elsewhere"], RepoNames(restarted));
        Assert.True(Retry.WhileFalse(
            () => CollapsedGroupsInSettings(context).Count == 0,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     The card color palette offers every color, each by its name, and Automatic; choosing a swatch
    ///     recolors the card's tile and choosing Automatic clears the stored choice again, with the status
    ///     accent unchanged and nothing written into the repository.
    /// </summary>
    [Fact]
    public void CardMenu_CardColor_PaletteOffersEveryColorAndAutomaticClearsTheChoice()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        string[] colors = ["Blue", "Violet", "Magenta", "Green", "Amber", "Teal", "Orange", "Indigo", "Red", "Lime"];
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        var statusBefore = Find(mainWindow, "RepoStatusAccent").Name;

        // Act: ... > Card color...
        OpenCardColorPalette(mainWindow);

        // Assert: one swatch per color, named for it, and Automatic
        foreach (var color in colors)
        {
            Assert.Equal(color, Find(mainWindow, $"CardColorSwatch_{color}").Name);
        }

        Assert.NotNull(Find(mainWindow, "CardColorAutomaticButton"));

        // Act: choose Teal, then Red
        Find(mainWindow, "CardColorSwatch_Teal").AsButton().Invoke();
        var teal = SettledTileColor(mainWindow);
        OpenCardColorPalette(mainWindow);
        Find(mainWindow, "CardColorSwatch_Red").AsButton().Invoke();

        // Assert: the tile changes with the choice, and Red is stored in place of Teal
        var red = SettledTileColor(mainWindow);
        Assert.NotEqual(teal, red);
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"CardColor\": \"Red\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: choose Automatic
        OpenCardColorPalette(mainWindow);
        Find(mainWindow, "CardColorAutomaticButton").AsButton().Invoke();

        // Assert: no color is stored any more, the status is unchanged and the repository is untouched
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("\"CardColor\": \"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(statusBefore, Find(mainWindow, "RepoStatusAccent").Name);
        Assert.Empty(Directory.EnumerateFileSystemEntries(context.RepoPath));
    }

    /// <summary>
    ///     Seeds the settings with the repo in the group "Work" and a second repo in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context, whose repo is the first one.</param>
    private static void SeedWorkGroupAndUngroupedRepo(
        Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var other = Path.Combine(context.RootDirectory, "elsewhere");
        Directory.CreateDirectory(other);
        settings["RecentRepos"] = new object[]
        {
            new { Path = context.RepoPath, GroupName = "Work" },
            new { Path = other }
        };
    }

    /// <summary>
    ///     Opens the first card's "..." menu and then its Card color palette.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    private static void OpenCardColorPalette(Window mainWindow)
    {
        Find(mainWindow, "MoreActionsButton").AsButton().Invoke();
        Find(mainWindow, "CardColorMenuItem").AsMenuItem().Invoke();
        Assert.NotNull(Find(mainWindow, "CardColorPalette"));
    }

    /// <summary>
    ///     Reads the first card's tile color once it has stopped changing, so a color read right after a
    ///     choice is not the one drawn before the card was redrawn.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The tile color.</returns>
    private static System.Drawing.Color SettledTileColor(Window mainWindow)
    {
        Thread.Sleep(600);
        var color = default(System.Drawing.Color);
        var settled = Retry.WhileFalse(
            () =>
            {
                var previous = color;
                color = RenderedTextProbe.SurfaceColor(mainWindow, Find(mainWindow, "RepoAvatarText"));
                return color == previous;
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(600)).Result;
        Assert.True(settled, "The tile color never settled.");
        return color;
    }

    /// <summary>
    ///     Lists the names of the section headers currently shown, in order; each names the section, its
    ///     repo count and whether it is expanded.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The name of each <c>GroupHeaderToggleButton</c>.</returns>
    private static List<string> HeaderNames(Window mainWindow) =>
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("GroupHeaderToggleButton"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];

    /// <summary>
    ///     Reads the collapsed sections stored in the settings file.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The names in <c>CollapsedGroups</c>, or none when the key is missing.</returns>
    private static List<string> CollapsedGroupsInSettings(AgentControlTestContext context)
    {
        using var document = System.Text.Json.JsonDocument.Parse(ReadSettings(context));
        return document.RootElement.TryGetProperty("CollapsedGroups", out var groups)
            && groups.ValueKind == System.Text.Json.JsonValueKind.Array
            ? [.. groups.EnumerateArray().Select(group => group.GetString() ?? string.Empty)]
            : [];
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
        [.. mainWindow.FindAllDescendants(cf => cf.ByAutomationId("RepoNameText"))
            .OrderBy(element => element.BoundingRectangle.Top).Select(element => element.Name)];

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
                .ElementAtOrDefault(index),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }
}
