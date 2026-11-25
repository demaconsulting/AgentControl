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
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of selecting several repo cards in the real application: the list reports that it
///     allows several selected items, the selection is made through UI Automation (as a screen reader or a
///     test tool does) or with the real mouse and keyboard (Ctrl+click, Shift+click, Ctrl+A), and the
///     selected cards are removed with the real Delete key or moved from a card's menu. Removing leaves the
///     repos' folders on disk.
/// </summary>
public sealed class RepoMultiSelectTests
{
    /// <summary>
    ///     Selecting two of three repo cards through UI Automation marks exactly those two as selected,
    ///     pressing Delete asks one question that names the count, answering No changes nothing, and answering
    ///     Yes removes the two selected cards from the list and from the settings file while their folders
    ///     stay on disk.
    /// </summary>
    [Fact]
    public void RepoList_SelectSeveralCardsAndPressDelete_ConfirmsOnceAndRemovesOnlyTheSelectedRepos()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: three repos, none in a group
        using var context = new AgentControlTestContext(customizeSettings: SeedThreeRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        var list = Find(mainWindow, "RepoListBox");

        // Assert: the list allows several selected items
        Assert.True(list.Patterns.Selection.Pattern.CanSelectMultiple.Value);
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Alpha", "Bravo", "Charlie"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: select Alpha, then add Charlie to the selection
        Item(list, "Alpha").Patterns.SelectionItem.Pattern.Select();
        Item(list, "Charlie").Patterns.SelectionItem.Pattern.AddToSelection();

        // Assert: exactly those two are selected
        Assert.True(Item(list, "Alpha").Patterns.SelectionItem.Pattern.IsSelected.Value);
        Assert.False(Item(list, "Bravo").Patterns.SelectionItem.Pattern.IsSelected.Value);
        Assert.True(Item(list, "Charlie").Patterns.SelectionItem.Pattern.IsSelected.Value);

        // Act: Delete, answered No
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        DialogInputProbe.Press(VirtualKeyShort.DELETE);
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Assert.Contains("2 repos", Find(question, "ConfirmationMessageText").Name, StringComparison.Ordinal);
        Find(question, "ConfirmationNoButton").AsButton().Invoke();

        // Assert: nothing changed
        Assert.Equal(3, RepoNames(mainWindow).Count);

        // Act: Delete, answered Yes
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        DialogInputProbe.Press(VirtualKeyShort.DELETE);
        question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Find(question, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: only Bravo is left in the list and in the settings, and every folder is still on disk
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Bravo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("Alpha", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Alpha")));
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Charlie")));
    }

    /// <summary>
    ///     With the real mouse and keyboard: a click selects one card, Ctrl+click adds a card to (or takes
    ///     it out of) the selection, Shift+click selects the range from the clicked card, and Ctrl+A
    ///     selects every card that is shown. A section header is never selected, and a card in a collapsed
    ///     group, which is not shown, is never part of the selection (the confirmation that Delete asks
    ///     counts only the shown cards).
    /// </summary>
    [Fact]
    public void RepoList_CtrlClickShiftClickAndCtrlA_SelectCardsInListOrderNeverHeadersOrHiddenCards()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: Alpha and Bravo in the group "Work", Charlie in no group, Delta in the collapsed group
        // "Hidden"; so the list shows the headers "Hidden", "Work" and "Ungrouped" and three cards
        using var context = new AgentControlTestContext(customizeSettings: SeedGroupsWithACollapsedOne);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        EnlargeWindow(mainWindow);
        var list = Find(mainWindow, "RepoListBox");
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Alpha", "Bravo", "Charlie"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: click Alpha
        ClickCard(context, mainWindow, "Alpha");

        // Assert: only Alpha is selected
        Assert.Equal(["Alpha"], SelectedNames(list));

        // Act: Ctrl+click Charlie
        ClickCard(context, mainWindow, "Charlie", VirtualKeyShort.CONTROL);

        // Assert: Alpha and Charlie are selected, Bravo between them is not
        Assert.Equal(["Alpha", "Charlie"], SelectedNames(list));

        // Act: Ctrl+click Alpha again
        ClickCard(context, mainWindow, "Alpha", VirtualKeyShort.CONTROL);

        // Assert: Alpha left the selection
        Assert.Equal(["Charlie"], SelectedNames(list));

        // Act: click Alpha, then Shift+click Charlie
        ClickCard(context, mainWindow, "Alpha");
        ClickCard(context, mainWindow, "Charlie", VirtualKeyShort.SHIFT);

        // Assert: the whole range is selected, in list order, without the "Ungrouped" header in between
        Assert.Equal(["Alpha", "Bravo", "Charlie"], SelectedNames(list));
        Assert.Empty(SelectedHeaders(list));

        // Act: click Bravo, then Ctrl+A
        ClickCard(context, mainWindow, "Bravo");
        Assert.Equal(["Bravo"], SelectedNames(list));
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);

        // Assert: every shown card is selected, no header is
        Assert.True(Retry.WhileFalse(
            () => SelectedNames(list).SequenceEqual(["Alpha", "Bravo", "Charlie"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(SelectedHeaders(list));

        // Act: Delete, answered No
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        DialogInputProbe.Press(VirtualKeyShort.DELETE);
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);

        // Assert: the question counts the three shown cards, not Delta in the collapsed group; nothing is removed
        Assert.Contains("3 repos", Find(question, "ConfirmationMessageText").Name, StringComparison.Ordinal);
        Find(question, "ConfirmationNoButton").AsButton().Invoke();
        Assert.Equal(["Alpha", "Bravo", "Charlie"], RepoNames(mainWindow));
    }

    /// <summary>
    ///     With two cards selected, the menu of one of them offers to remove the selected repos; it asks one
    ///     question that names the count, answering No changes nothing, and answering Yes removes both
    ///     selected cards from the list and from the settings file and leaves their folders on disk.
    /// </summary>
    [Fact]
    public void CardMenu_RemoveOnSelectedCard_RemovesTheWholeSelectionAfterOneConfirmation()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: three repos, none in a group, Alpha and Charlie selected
        using var context = new AgentControlTestContext(customizeSettings: SeedThreeRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        var list = Find(mainWindow, "RepoListBox");
        Item(list, "Alpha").Patterns.SelectionItem.Pattern.Select();
        Item(list, "Charlie").Patterns.SelectionItem.Pattern.AddToSelection();
        Assert.Equal(["Alpha", "Charlie"], SelectedNames(list));

        // Act: the menu of Alpha, which is one of the selected cards, > Remove, declined first
        var remove = OpenCardMenuItem(mainWindow, list, "Alpha", "RemoveMenuItem");
        Assert.Equal("Remove 2 selected repos from list", remove.Name);
        remove.AsMenuItem().Invoke();
        var question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Assert.Contains("2 repos", Find(question, "ConfirmationMessageText").Name, StringComparison.Ordinal);
        Assert.Contains("it does not delete or", Find(question, "ConfirmationMessageText").Name, StringComparison.Ordinal);
        Find(question, "ConfirmationNoButton").AsButton().Invoke();

        // Assert: nothing changed
        Assert.Equal(["Alpha", "Bravo", "Charlie"], RepoNames(mainWindow));
        Assert.Equal(["Alpha", "Charlie"], SelectedNames(list));

        // Act: the same menu item, confirmed
        OpenCardMenuItem(mainWindow, list, "Alpha", "RemoveMenuItem").AsMenuItem().Invoke();
        question = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(question);
        Find(question, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: only Bravo is left in the list and in the settings, and every folder is still on disk
        Assert.True(Retry.WhileFalse(
            () => RepoNames(mainWindow).SequenceEqual(["Bravo"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => !ReadSettings(context).Contains("Alpha", StringComparison.Ordinal) &&
                  !ReadSettings(context).Contains("Charlie", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Alpha")));
        Assert.True(Directory.Exists(Path.Combine(context.RootDirectory, "Charlie")));
    }

    /// <summary>
    ///     With two cards selected, the "Move to group" item of one of them says how many repos it moves,
    ///     and choosing an existing group in its submenu moves both selected cards into that group together,
    ///     stores the group for both in the settings file and keeps the selection.
    /// </summary>
    [Fact]
    public void CardMenu_MoveToGroupOnSelectedCard_MovesAllSelectedCardsAndStoresInSettings()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: Delta in the group "Work", Alpha and Charlie in no group, both selected
        using var context = new AgentControlTestContext(customizeSettings: SeedWorkGroupAndTwoRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        EnlargeWindow(mainWindow);
        var list = Find(mainWindow, "RepoListBox");
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Work, 1 repo, expanded", "Ungrouped, 2 repos, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Item(list, "Alpha").Patterns.SelectionItem.Pattern.Select();
        Item(list, "Charlie").Patterns.SelectionItem.Pattern.AddToSelection();

        // Act: the menu of Alpha, which is one of the selected cards, > Move to group > Work
        var moveToGroup = OpenCardMenuItem(mainWindow, list, "Alpha", "MoveToGroupMenuItem");
        Assert.Equal("Move 2 selected repos to group", moveToGroup.Name);
        ExpandMenuItem(moveToGroup);
        Find(mainWindow, "MoveToGroupItem", candidate => candidate.Name == "Work").AsMenuItem().Invoke();

        // Assert: Work now holds the three repos and nothing is left ungrouped, and the two moved cards are
        // still selected
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Work, 3 repos, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Equal(["Alpha", "Charlie"], SelectedNames(list));

        // Assert: stored for the three repos in the settings, and nothing was written into a repository
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Split("\"GroupName\": \"Work\"").Length == 4,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(context.RootDirectory, "Alpha")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(context.RootDirectory, "Charlie")));
    }

    /// <summary>
    ///     With two cards selected, dragging one of them with the real mouse onto a group's header moves both
    ///     selected cards into that group together and stores the group for both in the settings file.
    /// </summary>
    [Fact]
    public void RepoList_DragSelectedCard_MovesAllSelectedCardsOntoTheGroupHeader()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: Delta in the group "Work", Alpha and Charlie in no group, both selected
        using var context = new AgentControlTestContext(customizeSettings: SeedWorkGroupAndTwoRepos);
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        EnlargeWindow(mainWindow);
        Assert.True(CardRefreshWait.WaitUntilCardsAreRead(mainWindow, TimeSpan.FromSeconds(60)));
        var list = Find(mainWindow, "RepoListBox");
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Work, 1 repo, expanded", "Ungrouped, 2 repos, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Item(list, "Alpha").Patterns.SelectionItem.Pattern.Select();
        Item(list, "Charlie").Patterns.SelectionItem.Pattern.AddToSelection();

        // Act: grab Alpha by its status bar (not one of its controls) and drop it on the Work header
        var accent = Item(list, "Alpha").FindFirstDescendant(cf => cf.ByAutomationId("RepoStatusAccent"));
        Assert.NotNull(accent);
        var header = Find(mainWindow, "GroupHeaderToggleButton", candidate => candidate.Name.StartsWith("Work", StringComparison.Ordinal));
        DialogInputProbe.Drag(mainWindow, DialogInputProbe.CenterOf(accent), DialogInputProbe.CenterOf(header));

        // Assert: Work now holds all three repos, nothing is left ungrouped, and the group is stored for the
        // three of them
        Assert.True(Retry.WhileFalse(
            () => HeaderNames(mainWindow).SequenceEqual(["Work, 3 repos, expanded"]),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Split("\"GroupName\": \"Work\"").Length == 4,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Makes the main window tall enough for all the cards and headers of a test to be on screen
    ///     (a list shows only what fits, so a taller list is needed to select across many cards).
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    private static void EnlargeWindow(Window mainWindow)
    {
        var transform = mainWindow.Patterns.Transform.PatternOrDefault;
        Assert.NotNull(transform);
        transform.Move(0, 0);
        transform.Resize(900, 800);
        Assert.True(Retry.WhileFalse(
            () => mainWindow.BoundingRectangle.Height >= 700,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result, $"The window is {mainWindow.BoundingRectangle}.");
    }

    /// <summary>
    ///     Seeds the settings with a repo in the group "Work" and two repos in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context.</param>
    private static void SeedWorkGroupAndTwoRepos(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var repos = new (string, string?)[] { ("Delta", "Work"), ("Alpha", null), ("Charlie", null) }
            .Select(entry => (Path: Path.Combine(context.RootDirectory, entry.Item1), Group: entry.Item2))
            .ToList();
        foreach (var repo in repos)
        {
            Directory.CreateDirectory(repo.Path);
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = repos.Select(repo => (object)new { repo.Path, GroupName = repo.Group }).ToArray();
    }

    /// <summary>
    ///     Seeds the settings with two repos in the group "Work", one in the collapsed group "Hidden" and
    ///     one in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context.</param>
    private static void SeedGroupsWithACollapsedOne(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var repos = new (string, string?)[] { ("Alpha", "Work"), ("Bravo", "Work"), ("Delta", "Hidden"), ("Charlie", null) }
            .Select(entry => (Path: Path.Combine(context.RootDirectory, entry.Item1), Group: entry.Item2))
            .ToList();
        foreach (var repo in repos)
        {
            Directory.CreateDirectory(repo.Path);
        }

        settings["HasUserRepoOrder"] = true;
        settings["CollapsedGroups"] = new[] { "Hidden" };
        settings["RecentRepos"] = repos.Select(repo => (object)new { repo.Path, GroupName = repo.Group }).ToArray();
    }

    /// <summary>
    ///     Clicks a repo card with the real mouse, holding the given modifier keys.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="name">The repo's folder name.</param>
    /// <param name="modifiers">The keys to hold down during the click (Ctrl or Shift), if any.</param>
    private static void ClickCard(
        AgentControlTestContext context, Window mainWindow, string name, params VirtualKeyShort[] modifiers)
    {
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        var list = Find(mainWindow, "RepoListBox");

        // Click the repo's name: text, not a button, so the click selects the card; and wait out the
        // double-click time so two clicks on one card are never taken as a double click
        var point = DialogInputProbe.CenterOf(Item(list, name).FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText"))!);
        Thread.Sleep(700);
        using (Keyboard.Pressing(modifiers))
        {
            DialogInputProbe.ClickAt(context, point);
        }

        Thread.Sleep(200);
    }

    /// <summary>
    ///     Lists the names of the repo cards that are selected, from the top of the list down.
    /// </summary>
    /// <param name="list">The repo list.</param>
    /// <returns>The repo name of each selected card.</returns>
    private static List<string> SelectedNames(AutomationElement list) =>
        [.. list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Where(item => item.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault == true)
            .OrderBy(item => item.BoundingRectangle.Top)
            .Select(item => item.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText"))?.Name)
            .OfType<string>()];

    /// <summary>
    ///     Lists the section headers that are selected; there must never be one.
    /// </summary>
    /// <param name="list">The repo list.</param>
    /// <returns>The header toggle button of each selected header row.</returns>
    private static List<AutomationElement> SelectedHeaders(AutomationElement list) =>
        [.. list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Where(item => item.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault == true &&
                           item.FindFirstDescendant(cf => cf.ByAutomationId("GroupHeaderToggleButton")) is not null)];

    /// <summary>
    ///     Opens the "..." menu of one repo card and finds one of its items.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="list">The repo list.</param>
    /// <param name="name">The repo's folder name.</param>
    /// <param name="menuItemAutomationId">The AutomationId of the wanted item.</param>
    /// <returns>The menu item.</returns>
    private static AutomationElement OpenCardMenuItem(
        Window mainWindow, AutomationElement list, string name, string menuItemAutomationId)
    {
        AutomationElement? menuItem = null;
        for (var attempt = 0; attempt < 3 && menuItem is null; attempt++)
        {
            Item(list, name).FindFirstDescendant(cf => cf.ByAutomationId("MoreActionsButton"))!.AsButton().Invoke();
            menuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(menuItemAutomationId)),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(200)).Result;
        }

        Assert.NotNull(menuItem);
        return menuItem;
    }

    /// <summary>
    ///     Opens a menu item that has a submenu (or clicks it when it does not expose the expand pattern).
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
    ///     Seeds the settings with three repos in no group.
    /// </summary>
    /// <param name="settings">The settings dictionary to edit.</param>
    /// <param name="context">The test context.</param>
    private static void SeedThreeRepos(Dictionary<string, object?> settings, AgentControlTestContext context)
    {
        var paths = new[] { "Alpha", "Bravo", "Charlie" }
            .Select(name => Path.Combine(context.RootDirectory, name))
            .ToList();
        foreach (var path in paths)
        {
            Directory.CreateDirectory(path);
        }

        settings["HasUserRepoOrder"] = true;
        settings["RecentRepos"] = paths.Select(path => (object)new { Path = path }).ToArray();
    }

    /// <summary>
    ///     Finds the list item of a repo card, by the repo's name.
    /// </summary>
    /// <param name="list">The repo list.</param>
    /// <param name="name">The repo's folder name.</param>
    /// <returns>The list item.</returns>
    private static AutomationElement Item(AutomationElement list, string name)
    {
        var item = Retry.WhileNull(
            () => list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(candidate => candidate.FindFirstDescendant(cf => cf.ByAutomationId("RepoNameText"))?.Name == name),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(item);
        return item;
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
