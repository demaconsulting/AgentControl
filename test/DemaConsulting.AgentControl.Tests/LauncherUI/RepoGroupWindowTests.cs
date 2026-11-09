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

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the main window's repo groups and card colors as the user meets them: the
///     section headers, collapsing, the card menu's "Move to group" submenu, the name prompt, the
///     header menu, and the card color palette pop-up.
/// </summary>
/// <remarks>
///     Runs in the <c>RealProcess</c> collection because showing cards spawns real git subprocesses
///     as part of each card's eager refresh.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoGroupWindowTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that grouped repos are shown under headers that name the section, say how many repos
    ///     it holds and whether it is collapsed, and that a flat list has no header.
    /// </summary>
    [Fact]
    public void MainWindow_GroupedRepos_ShowHeadersWithNameCountAndState() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _) = CreateWindow(
            ("a", "Work", false), ("b", "Work", false), ("c", null, false), ("d", null, true));

        try
        {
            // Act
            var headers = window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").ToList();

            // Assert: Favorites, Work, Ungrouped - each with an accessible name that says it all
            Assert.Equal(
                ["Favorites, 1 repo, expanded", "Work, 2 repos, expanded", "Ungrouped, 1 repo, expanded"],
                headers.Select(AutomationProperties.GetName));
            Assert.Equal(
                ["Favorites", "Work", "Ungrouped"],
                window.GetVisualDescendantsOf<TextBlock>("GroupHeaderNameText").Select(text => text.Text));
            Assert.Equal(
                ["1", "2", "1"],
                window.GetVisualDescendantsOf<TextBlock>("GroupHeaderCountText").Select(text => text.Text));
            Assert.All(headers, header => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(header)?.ToString())));
            Assert.Equal(4, window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Count());

            // Only the user's own group has the "..." menu
            Assert.Single(window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton"), button => button.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a list without any group shows no header.
    /// </summary>
    [Fact]
    public void MainWindow_NoGroups_ShowsNoHeader() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _) = CreateWindow(("a", null, false), ("b", null, true));

        try
        {
            // Assert
            Assert.Empty(window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton"));
            Assert.Equal(2, window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Count());
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that clicking a header collapses its section (the cards disappear, the header stays
    ///     and says so), and clicking again expands it.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderToggle_CollapsesAndExpandsSection() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("b", "Work", false), ("c", null, false));

        try
        {
            var work = window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton")
                .Single(button => AutomationProperties.GetName(button)?.StartsWith("Work", StringComparison.Ordinal) == true);

            // Act: collapse
            work.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert: only the ungrouped card is left, the header says collapsed
            Assert.Equal(["c"], window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Select(text => text.Text));
            Assert.Equal("Work, 2 repos, collapsed", AutomationProperties.GetName(work));
            Assert.True(viewModel.IsGroupCollapsed("Work"));

            // Act: expand
            work.Command.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(3, window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Count());
            Assert.Equal("Work, 2 repos, expanded", AutomationProperties.GetName(work));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the card menu's "Move to group" submenu lists the existing groups (marking the
    ///     repo's own), "New group...", and "Remove from group" only for a grouped repo - each with an
    ///     AutomationId and a tooltip - and that choosing a group moves the repo.
    /// </summary>
    [Fact]
    public void MainWindow_CardMenu_MoveToGroupSubmenu_ListsChoicesAndMovesRepo() => HeadlessTestApp.Run(() =>
    {
        // Arrange: "a" is in Work, "c" is in no group
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("b", "Home", false), ("c", null, false));

        try
        {
            // Act: open the menu of the grouped repo
            var menu = OpenCardMenu(window, "a");
            var move = FindMenuItem(menu, "MoveToGroupMenuItem");
            var entries = move.Items.OfType<MenuItem>().ToList();

            // Assert: both groups, New group, Remove from group; every entry identified and explained
            Assert.Equal(
                ["Home", "Work", "New group...", "Remove from group"],
                entries.Select(entry => entry.Header is TextBlock text ? text.Text : entry.Header?.ToString()));
            Assert.Equal(
                ["MoveToGroupItem", "MoveToGroupItem", "NewGroupMenuItem", "RemoveFromGroupMenuItem"],
                entries.Select(AutomationProperties.GetAutomationId));
            Assert.All(entries, entry => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(entry)?.ToString())));
            Assert.NotNull(entries.Single(entry => AutomationProperties.GetName(entry) == "Work").Icon);
            Assert.Null(entries.Single(entry => AutomationProperties.GetName(entry) == "Home").Icon);
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(move)?.ToString()));

            // Act: move the repo to the other group
            entries.Single(entry => AutomationProperties.GetName(entry) == "Home")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal("Home", viewModel.RepoCards.Single(card => card.RepoName == "a").GroupName);

            // Act: the ungrouped repo's submenu has no "Remove from group"
            menu = OpenCardMenu(window, "c");
            var ungroupedEntries = FindMenuItem(menu, "MoveToGroupMenuItem").Items.OfType<MenuItem>().ToList();
            menu.Hide();

            // Assert
            Assert.DoesNotContain(ungroupedEntries, entry => AutomationProperties.GetAutomationId(entry) == "RemoveFromGroupMenuItem");
            Assert.Contains(ungroupedEntries, entry => AutomationProperties.GetAutomationId(entry) == "NewGroupMenuItem");
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that "New group..." asks for a name in a small prompt and, once a valid name is
    ///     entered, moves the repo into the new group and shows its header.
    /// </summary>
    [Fact]
    public void MainWindow_NewGroupMenuItem_PromptsForNameAndCreatesGroup() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", null, false), ("b", null, false));

        try
        {
            // Act: choose "New group..." from the menu of repo "a"
            var menu = OpenCardMenu(window, "a");
            FindMenuItem(FindMenuItem(menu, "MoveToGroupMenuItem"), "NewGroupMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Assert: the prompt is open, empty and cannot be accepted yet
            var prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            var promptModel = Assert.IsType<GroupNameViewModel>(prompt.DataContext);
            Assert.False(promptModel.CanAccept);

            // Act: an invalid name first, then a valid one
            promptModel.Text = "Favorites";
            Dispatcher.UIThread.RunJobs();
            Assert.True(prompt.FindControl<TextBlock>("GroupNameErrorText")!.IsVisible);
            DialogWindowTests.Click(prompt, "GroupNameAcceptButton");
            Assert.Empty(viewModel.GroupNames);

            promptModel.Text = "Client X";
            Dispatcher.UIThread.RunJobs();
            Assert.False(prompt.FindControl<TextBlock>("GroupNameErrorText")!.IsVisible);
            DialogWindowTests.Click(prompt, "GroupNameAcceptButton");
            Dispatcher.UIThread.RunJobs();

            // Assert: the repo is in the group and the header shows
            Assert.Equal("Client X", viewModel.RepoCards.Single(card => card.RepoName == "a").GroupName);
            Assert.Contains(
                "Client X, 1 repo, expanded",
                window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").Select(AutomationProperties.GetName));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that canceling the name prompt changes nothing.
    /// </summary>
    [Fact]
    public void MainWindow_NewGroupPrompt_Canceled_ChangesNothing() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", null, false));

        try
        {
            var menu = OpenCardMenu(window, "a");
            FindMenuItem(FindMenuItem(menu, "MoveToGroupMenuItem"), "NewGroupMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            ((GroupNameViewModel)prompt.DataContext!).Text = "Never";

            // Act
            DialogWindowTests.Click(prompt, "GroupNameCancelButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Empty(viewModel.GroupNames);
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the header's "..." menu renames a group through the prompt and removes it after a
    ///     confirmation, leaving its repos ungrouped.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenu_RenameAndRemoveGroup() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("b", "Work", false));

        try
        {
            var menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(menuButton)?.ToString()));

            // Act: Rename group...
            var menu = OpenMenu(menuButton);
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(FindMenuItem(menu, "RenameGroupMenuItem"))?.ToString()));
            FindMenuItem(menu, "RenameGroupMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            var promptModel = (GroupNameViewModel)prompt.DataContext!;
            Assert.Equal("Work", promptModel.Text);
            promptModel.Text = "Office";
            DialogWindowTests.Click(prompt, "GroupNameAcceptButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["Office"], viewModel.GroupNames);

            // Act: Remove group, declined and then confirmed
            menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
            menu = OpenMenu(menuButton);
            FindMenuItem(menu, "RemoveGroupMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            DialogWindowTests.Click(confirmation, "ConfirmationNoButton");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Office"], viewModel.GroupNames);

            menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
            menu = OpenMenu(menuButton);
            FindMenuItem(menu, "RemoveGroupMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            DialogWindowTests.Click(confirmation, "ConfirmationYesButton");
            Dispatcher.UIThread.RunJobs();

            // Assert: no group, so no header; both repos still listed
            Assert.Empty(viewModel.GroupNames);
            Assert.Empty(window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton"));
            Assert.Equal(2, window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Count());
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the header's "..." menu starts with "Add repository..." (with an AutomationId and
    ///     a tooltip), followed by a separator and then the rename and remove items.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenu_AddRepositoryIsFirstItemBeforeSeparator() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _) = CreateWindow(("a", "Work", false));

        try
        {
            var menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);

            // Act
            var menu = OpenMenu(menuButton);
            var items = menu.Items.OfType<Control>().ToList();

            // Assert: Add repository..., separator, Rename group..., Remove group
            Assert.Equal(4, items.Count);
            var add = Assert.IsType<MenuItem>(items[0]);
            Assert.Equal("GroupAddRepoMenuItem", AutomationProperties.GetAutomationId(add));
            Assert.Equal("Add repository...", add.Header);
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(add)?.ToString()));
            Assert.IsType<Separator>(items[1]);
            Assert.Equal("RenameGroupMenuItem", Assert.IsType<MenuItem>(items[2]).Name);
            Assert.Equal("RemoveGroupMenuItem", Assert.IsType<MenuItem>(items[3]).Name);
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that choosing "Add repository..." and picking a folder adds the repo as a new card in
    ///     that group, and that canceling the picker changes nothing.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenuAddRepository_PickedFolderJoinsGroup_CancelChangesNothing() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a Work group and an ungrouped repo; the picker is answered by the test
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("c", null, false));
        var fresh = CreateRepoDirectory("fresh");
        string? answer = null;
        window.RepoFolderPicker = () => Task.FromResult(answer);

        try
        {
            // Act: cancel the picker
            ClickHeaderAddRepository(window);

            // Assert: nothing changed and no message appeared
            Assert.Equal(2, viewModel.RepoCards.Count);
            Assert.Empty(window.OwnedWindows);

            // Act: pick a folder
            answer = fresh;
            ClickHeaderAddRepository(window);

            // Assert: a new card, in Work, shown under the Work header
            Assert.Equal(3, viewModel.RepoCards.Count);
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "fresh").GroupName);
            Assert.Equal(3, window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Count());
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that picking a repo that is already listed (ungrouped) moves it into the group and
    ///     tells the user it was already in the list, rather than adding a second entry.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenuAddRepository_AlreadyListedRepo_MovesIntoGroupAndSaysSo() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("c", null, false));
        var loose = viewModel.RepoCards.Single(card => card.RepoName == "c");
        window.RepoFolderPicker = () => Task.FromResult<string?>(loose.RepoPath);

        try
        {
            // Act
            ClickHeaderAddRepository(window);

            // Assert: still two repos, "c" is in Work, and a message says it was already listed
            Assert.Equal(2, viewModel.RepoCards.Count);
            Assert.Equal("Work", loose.GroupName);
            var message = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
            message.Close();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the top-bar add still adds the folder the picker returns, ungrouped, and refuses
    ///     one that is already listed with a message.
    /// </summary>
    [Fact]
    public void MainWindow_AddRepoButton_UsesSamePickerAndAddsUngrouped() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false));
        var fresh = CreateRepoDirectory("fresh");
        var existing = viewModel.RepoCards.Single().RepoPath;
        string answer = fresh;
        window.RepoFolderPicker = () => Task.FromResult<string?>(answer);
        var addButton = window.FindControl<Button>("AddRepoButton")!;

        try
        {
            // Act: a new folder
            addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert: added, in no group
            Assert.Null(viewModel.RepoCards.Single(card => card.RepoName == "fresh").GroupName);
            Assert.Empty(window.OwnedWindows);

            // Act: a folder that is already listed
            answer = existing;
            addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert: refused with a message, and still in its group
            Assert.Equal(2, viewModel.RepoCards.Count);
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoPath == existing).GroupName);
            Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>()).Close();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that "Card color..." opens the palette pop-up with a swatch for every palette color and
    ///     "Automatic", each identified and explained; that choosing a swatch colors the tile (and a
    ///     picture's ring), marks the swatch as chosen and closes the pop-up; and that "Automatic"
    ///     goes back to the path color.
    /// </summary>
    [Fact]
    public void MainWindow_CardColorPalette_ChoosingSwatchColorsTileAndAutomaticClears() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", null, false));
        var card = viewModel.RepoCards.Single();
        var automaticClass = card.AvatarStyleClass;

        try
        {
            // Act: Card color... from the card menu
            var menu = OpenCardMenu(window, "a");
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(FindMenuItem(menu, "CardColorMenuItem"))?.ToString()));
            FindMenuItem(menu, "CardColorMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Assert: the palette is open and complete
            var moreButton = window.GetVisualDescendantsOf<Button>("MoreActionsButton").Single();
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(moreButton));
            Assert.True(flyout.IsOpen);
            var palette = Assert.IsAssignableFrom<Control>(flyout.Content);
            var buttons = palette.GetLogicalDescendants().OfType<Button>().ToList();
            var swatches = buttons
                .Where(button => AutomationProperties.GetAutomationId(button)?.StartsWith("CardColorSwatch_", StringComparison.Ordinal) == true)
                .ToList();
            Assert.Equal(
                CardColorPalette.All.Select(color => "CardColorSwatch_" + color.Name),
                swatches.Select(AutomationProperties.GetAutomationId));
            Assert.All(swatches, swatch =>
            {
                Assert.Equal(swatch.Tag, AutomationProperties.GetName(swatch));
                Assert.Equal(swatch.Tag, ToolTip.GetTip(swatch));
            });
            var automatic = buttons.Single(button => AutomationProperties.GetAutomationId(button) == "CardColorAutomaticButton");
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(automatic)?.ToString()));

            // Act: choose Teal
            swatches.Single(swatch => (string?)swatch.Tag == "Teal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert: the card has the color, the tile shows it, the pop-up closed
            Assert.Equal("Teal", card.CardColorName);
            var tile = window.GetVisualDescendantsOf<Border>("RepoAvatar").Single();
            Assert.Contains("avatar5", tile.Classes);
            Assert.Contains("customColor", tile.Classes);
            Assert.False(flyout.IsOpen);

            // Act: reopen; the chosen swatch is marked
            FlyoutBase.ShowAttachedFlyout(moreButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("selected", swatches.Single(swatch => (string?)swatch.Tag == "Teal").Classes);
            Assert.DoesNotContain("selected", swatches.Single(swatch => (string?)swatch.Tag == "Blue").Classes);
            flyout.Hide();

            // Act: Automatic (through the menu again so the handler knows the card)
            menu = OpenCardMenu(window, "a");
            FindMenuItem(menu, "CardColorMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            automatic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Null(card.CardColorName);
            Assert.Contains(automaticClass, tile.Classes);
            Assert.DoesNotContain("customColor", tile.Classes);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a favorite repo that is in a group shows the group's name as a chip, and a repo
    ///     that is not a favorite does not.
    /// </summary>
    [Fact]
    public void MainWindow_FavoriteInGroup_ShowsGroupChip() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _) = CreateWindow(("a", "Work", true), ("b", "Work", false));

        try
        {
            // Assert: a chip is visible on the favorite only
            var visibleChips = window.GetVisualDescendantsOf<Border>("GroupChip").Where(chip => chip.IsVisible).ToList();
            Assert.Single(visibleChips);
            Assert.Equal("Work", visibleChips[0].GetVisualDescendantsOf<TextBlock>("GroupChipText").Single().Text);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the name prompt shows its message for an unusable name, enables its accept button
    ///     only for a usable one and reports the name.
    /// </summary>
    [Fact]
    public void GroupNameWindow_ValidatesWhileTyping_AndAccepts() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var viewModel = new GroupNameViewModel("New group", "Name:", "Create", ["Work"]);
        string? result = null;
        viewModel.Completed += (_, name) => result = name;
        var window = new GroupNameWindow(viewModel);
        window.Show();

        try
        {
            var accept = window.FindControl<Button>("GroupNameAcceptButton")!;
            var error = window.FindControl<TextBlock>("GroupNameErrorText")!;
            var hint = window.FindControl<TextBlock>("GroupNameHintText")!;

            // Assert: empty is neutral
            Assert.False(accept.IsEffectivelyEnabled);
            Assert.False(error.IsVisible);
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(window.FindControl<TextBox>("GroupNameTextBox")!)?.ToString()));

            // Act / Assert: unusable
            viewModel.Text = "ungrouped";
            Dispatcher.UIThread.RunJobs();
            Assert.True(error.IsVisible);
            Assert.False(accept.IsEffectivelyEnabled);

            // Act / Assert: an existing group gives a hint, not an error
            viewModel.Text = "work";
            Dispatcher.UIThread.RunJobs();
            Assert.False(error.IsVisible);
            Assert.True(hint.IsVisible);
            Assert.True(accept.IsEffectivelyEnabled);

            // Act
            DialogWindowTests.Click(window, "GroupNameAcceptButton");

            // Assert
            Assert.Equal("Work", result);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Deletes the temporary folders the tests created.
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a shown main window over repos with the given names, groups and favorite flags.
    /// </summary>
    /// <param name="repos">The repos as (folder name, group or <see langword="null"/>, favorite).</param>
    /// <returns>The window and its view model.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel) CreateWindow(
        params (string Name, string? Group, bool IsFavorite)[] repos)
    {
        var settings = new AppSettings
        {
            RecentRepos =
            [
                .. repos.Select(repo => new RecentRepo
                {
                    Path = CreateRepoDirectory(repo.Name),
                    GroupName = repo.Group,
                    IsFavorite = repo.IsFavorite
                })
            ]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var window = new MainWindow { DataContext = viewModel, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    /// <summary>
    ///     Opens the Work group header's menu and chooses "Add repository...".
    /// </summary>
    /// <param name="window">The main window.</param>
    private static void ClickHeaderAddRepository(MainWindow window)
    {
        var menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
        var menu = OpenMenu(menuButton);
        FindMenuItem(menu, "GroupAddRepoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Opens the "..." menu of the named repo's card.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenCardMenu(MainWindow window, string repoName)
    {
        var button = window.GetVisualDescendantsOf<Button>("MoreActionsButton")
            .Single(candidate => candidate.DataContext is RepoCardViewModel card && card.RepoName == repoName);
        return OpenMenu(button);
    }

    /// <summary>
    ///     Opens the menu of a button the way a click does.
    /// </summary>
    /// <param name="button">The button whose flyout to open.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenMenu(Button button)
    {
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return flyout;
    }

    /// <summary>
    ///     Finds an item of an open menu by its XAML name.
    /// </summary>
    /// <param name="menu">The open menu.</param>
    /// <param name="name">The item's XAML name.</param>
    /// <returns>The item.</returns>
    private static MenuItem FindMenuItem(MenuFlyout menu, string name) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Name == name);

    /// <summary>
    ///     Finds a sub-item of a menu item by its AutomationId.
    /// </summary>
    /// <param name="parent">The menu item that has the sub-items.</param>
    /// <param name="automationId">The wanted AutomationId.</param>
    /// <returns>The sub-item.</returns>
    private static MenuItem FindMenuItem(MenuItem parent, string automationId) =>
        parent.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == automationId);

    /// <summary>
    ///     Creates an empty repo folder with the given name under a unique parent.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <returns>The folder's path.</returns>
    private string CreateRepoDirectory(string name)
    {
        var path = Path.Combine(CreateTempDirectory(), name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     Creates a unique temporary directory that is removed on disposal.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlGroupWindow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
