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
///     as part of each card's background repo-state read.
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
    ///     Test that a window whose data context is cleared and assigned again handles each request of the
    ///     view model once (not once per assignment), keeps the list's selection in step, and stops
    ///     handling a view model that another one replaced.
    /// </summary>
    [Fact]
    public void MainWindow_DataContextAssignedAgain_HandlesEachRequestOnceAndOnlyForTheCurrentViewModel() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("b", "Work", false));
        MainWindowViewModel? other = null;

        try
        {
            var header = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(item => item.Name == "Work");

            // Act: the same view model is assigned again, then asks to delete the group
            window.DataContext = null;
            window.DataContext = viewModel;
            Dispatcher.UIThread.RunJobs();
            viewModel.SetSelectedCards(viewModel.RepoCards);
            var selected = window.FindControl<ListBox>("RepoListBox")!.SelectedItems!.Count;
            header.RemoveGroupCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var questions = window.OwnedWindows.OfType<ConfirmationWindow>().ToList();
            foreach (var question in questions)
            {
                DialogWindowTests.Click(question, "ConfirmationNoButton");
            }

            Dispatcher.UIThread.RunJobs();

            // Assert: one question, and the selection followed
            Assert.Single(questions);
            Assert.Equal(2, selected);

            // Act: another view model replaces it, and the old header asks again
            other = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());
            window.DataContext = other;
            Dispatcher.UIThread.RunJobs();
            header.RemoveGroupCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert: the window no longer handles the old view model, nor tracks its cards
            Assert.Empty(window.OwnedWindows.OfType<ConfirmationWindow>());
            Assert.Equal(0, window.TrackedCardCount);
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
            other?.Dispose();
        }
    });

    /// <summary>
    ///     Test that the header's "..." menu renames a group through the prompt, which starts with the
    ///     group's own name, and that canceling the prompt changes nothing.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenu_RenameGroup_AsksForTheNameAndRenames() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("b", "Work", false));

        try
        {
            // Act: Rename group..., canceled
            var menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
            var menu = OpenMenu(menuButton);
            FindMenuItem(menu, "RenameGroupMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            Assert.Equal("Work", ((GroupNameViewModel)prompt.DataContext!).Text);
            ((GroupNameViewModel)prompt.DataContext).Cancel();
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["Work"], viewModel.GroupNames);

            // Act: Rename group..., accepted with a new name
            menu = OpenMenu(window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible));
            FindMenuItem(menu, "RenameGroupMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            ((GroupNameViewModel)prompt.DataContext!).Text = "Office";
            DialogWindowTests.Click(prompt, "GroupNameAcceptButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["Office"], viewModel.GroupNames);
            Assert.All(viewModel.RepoCards, card => Assert.Equal("Office", card.GroupName));
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
    ///     Test that the header's "..." menu sorts the group's repos by name, and that the "Sort by name"
    ///     item is then unavailable, explaining in its tooltip that the repos are already sorted.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenu_SortByName_SortsGroupThenIsUnavailable() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("b", "Work", false), ("c", "Work", false), ("a", "Work", false));

        try
        {
            var menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
            var menu = OpenMenu(menuButton);
            var item = FindMenuItem(menu, "GroupSortByNameMenuItem");
            Assert.True(item.IsEffectivelyEnabled);
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(item)?.ToString()));

            // Act
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Assert: sorted, shown in that order, and the item explains why it is unavailable now
            Assert.Equal(["a", "b", "c"], viewModel.RepoCards.Select(card => card.RepoName));
            Assert.Equal(["a", "b", "c"], RepoReorderWindowTests.RowNames(window));
            menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
            menu = OpenMenu(menuButton);
            item = FindMenuItem(menu, "GroupSortByNameMenuItem");
            Assert.False(item.IsEffectivelyEnabled);
            Assert.Contains("already sorted", ToolTip.GetTip(item)?.ToString());
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the header's "..." menu starts with "Add repository..." and "New subgroup..." (each with
    ///     an AutomationId and a tooltip), followed by a separator, the Move up, Move down, Move into group, Sort by
    ///     name and Group color items, another separator and then the rename and the two delete items - each item
    ///     identified and explained, the item that deletes the group with its contents only for a group that has
    ///     contents.
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

            // Assert: Add repository..., New subgroup..., separator, Move up, Move down, Move into group,
            // Sort by name, Group color..., separator, Rename group..., Delete group (keep contents),
            // Delete group and its contents
            Assert.Equal(12, items.Count);
            var add = Assert.IsType<MenuItem>(items[0]);
            Assert.Equal("GroupAddRepoMenuItem", AutomationProperties.GetAutomationId(add));
            Assert.Equal("Add repository...", add.Header);
            var subgroup = Assert.IsType<MenuItem>(items[1]);
            Assert.Equal("GroupNewSubgroupMenuItem", AutomationProperties.GetAutomationId(subgroup));
            Assert.Equal("New subgroup...", subgroup.Header);
            Assert.IsType<Separator>(items[2]);
            Assert.IsType<Separator>(items[8]);
            Assert.Equal(
                [
                    "GroupAddRepoMenuItem", "GroupNewSubgroupMenuItem", "MoveGroupUpMenuItem", "MoveGroupDownMenuItem",
                    "GroupMoveIntoMenuItem", "GroupSortByNameMenuItem", "GroupColorMenuItem", "RenameGroupMenuItem",
                    "RemoveGroupMenuItem", "RemoveGroupWithContentsMenuItem"
                ],
                items.OfType<MenuItem>().Select(AutomationProperties.GetAutomationId));
            Assert.All(items.OfType<MenuItem>(), item => Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(item)?.ToString())));
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
    public void MainWindow_HeaderMenuAddRepository_PickedFolderJoinsGroup_CancelChangesNothing() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a Work group and an ungrouped repo; the picker is answered by the test
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("c", null, false));
        var fresh = CreateRepoDirectory("fresh");
        IReadOnlyList<string> answer = [];
        window.RepoFolderPicker = () => Task.FromResult(answer);

        try
        {
            // Act: cancel the picker
            ClickHeaderAddRepository(window);
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: nothing changed and no message appeared
            Assert.Equal(2, viewModel.RepoCards.Count);
            Assert.Empty(window.OwnedWindows);

            // Act: pick a folder
            answer = [fresh];
            ClickHeaderAddRepository(window);
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

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
    public void MainWindow_HeaderMenuAddRepository_AlreadyListedRepo_MovesIntoGroupAndSaysSo() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("c", null, false));
        var loose = viewModel.RepoCards.Single(card => card.RepoName == "c");
        window.RepoFolderPicker = () => Task.FromResult<IReadOnlyList<string>>([loose.RepoPath]);

        try
        {
            // Act
            ClickHeaderAddRepository(window);
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

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
    public void MainWindow_AddRepoButton_UsesSamePickerAndAddsUngrouped() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false));
        var fresh = CreateRepoDirectory("fresh");
        var existing = viewModel.RepoCards.Single().RepoPath;
        IReadOnlyList<string> answer = [fresh];
        window.RepoFolderPicker = () => Task.FromResult(answer);
        var addButton = window.FindControl<Button>("AddRepoButton")!;

        try
        {
            // Act: a new folder
            addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: added, in no group
            Assert.Null(viewModel.RepoCards.Single(card => card.RepoName == "fresh").GroupName);
            Assert.Empty(window.OwnedWindows);

            // Act: a folder that is already listed
            answer = [existing];
            addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

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
    ///     Test that "Group color..." in a group header's menu opens the palette with a swatch for every
    ///     palette color and "None", each identified and explained; that choosing a swatch colors the
    ///     group (an accent on its header, named in its tooltip and accessible name, and the tiles of its
    ///     uncolored repos), marks the swatch and closes the palette; and that "None" takes it away.
    /// </summary>
    [Fact]
    public void MainWindow_GroupColorPalette_ChoosingSwatchColorsHeaderAndNoneClears() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("b", null, false));

        try
        {
            var menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
            var accent = window.GetVisualDescendantsOf<Border>("GroupColorAccent").Single(border => border.DataContext is RepoGroupHeaderViewModel { IsUserGroup: true });
            Assert.False(accent.IsVisible);

            // Act: Group color... from the header menu
            ClickHeaderMenuItem(window, "GroupColorMenuItem");

            // Assert: the palette is open, complete and names the color in effect
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(menuButton));
            Assert.True(flyout.IsOpen);
            var palette = Assert.IsType<ColorPalettePanel>(flyout.Content);
            Assert.Equal("GroupColorPalette", AutomationProperties.GetAutomationId(palette));
            var buttons = palette.GetLogicalDescendants().OfType<Button>().ToList();
            var swatches = buttons
                .Where(button => AutomationProperties.GetAutomationId(button)?.StartsWith("GroupColorSwatch_", StringComparison.Ordinal) == true)
                .ToList();
            Assert.Equal(CardColorPalette.All.Select(color => "GroupColorSwatch_" + color.Name), swatches.Select(AutomationProperties.GetAutomationId));
            var none = buttons.Single(button => AutomationProperties.GetAutomationId(button) == "GroupColorNoneButton");
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(none)?.ToString()));
            Assert.Equal("Group color: None", palette.GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Name == "GroupColorHeadingText").Text);

            // Act: choose Teal
            swatches.Single(swatch => (string?)swatch.Tag == "Teal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert: stored, the accent shows and says the color, the repo's tile follows, palette closed
            Assert.Equal("Teal", viewModel.Settings.FindGroupColor("Work"));
            Assert.True(accent.IsVisible);
            Assert.Contains("groupAccent5", accent.Classes);
            Assert.Equal("Group color: Teal", AutomationProperties.GetName(accent));
            Assert.Equal("Group color: Teal", ToolTip.GetTip(accent));
            var tile = window.GetVisualDescendantsOf<Border>("RepoAvatar").Single(border => border.DataContext is RepoCardViewModel { RepoName: "a" });
            Assert.Contains("avatar5", tile.Classes);
            Assert.False(flyout.IsOpen);

            // Act: reopen; the chosen swatch is marked; then None
            ClickHeaderMenuItem(window, "GroupColorMenuItem");
            Assert.Contains("selected", swatches.Single(swatch => (string?)swatch.Tag == "Teal").Classes);
            none.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Null(viewModel.Settings.FindGroupColor("Work"));
            Assert.False(accent.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the color palette has one swatch per palette color, in palette order, each with an
    ///     AutomationId made of the prefix and the color's name, the color's name as its accessible name,
    ///     tooltip and tag, and the swatch fill of its palette index; and a heading and a clear button
    ///     named from the prefix.
    /// </summary>
    [Fact]
    public void ColorPalettePanel_Swatches_OnePerPaletteColorWithPrefixedIdsNamesAndTooltips() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var panel = new ColorPalettePanel
        {
            IdPrefix = "GroupColor",
            ClearText = "None",
            ClearTooltip = "No color for this group",
            Heading = "Group color: None"
        };
        var window = new Window { Content = panel };
        window.Show();

        try
        {
            // Act
            var buttons = panel.GetLogicalDescendants().OfType<Button>().ToList();
            var swatches = buttons.Where(button => button.Classes.Contains("swatch")).ToList();

            // Assert
            Assert.Equal(CardColorPalette.All.Select(color => "GroupColorSwatch_" + color.Name), swatches.Select(AutomationProperties.GetAutomationId));
            Assert.All(CardColorPalette.All, color =>
            {
                var swatch = swatches[color.Index];
                Assert.Equal(color.Name, swatch.Tag);
                Assert.Equal(color.Name, AutomationProperties.GetName(swatch));
                Assert.Equal(color.Name, ToolTip.GetTip(swatch));
                Assert.Contains($"avatar{color.Index}", swatch.Classes);
            });
            var clear = buttons.Single(button => AutomationProperties.GetAutomationId(button) == "GroupColorNoneButton");
            Assert.Equal("No color for this group", ToolTip.GetTip(clear));
            Assert.Equal("Group color: None", panel.GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Name == "GroupColorHeadingText").Text);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the chosen color (in any letter case) marks only its own swatch, and that with no
    ///     color chosen no swatch is marked and the clear button shows its check mark.
    /// </summary>
    [Fact]
    public void ColorPalettePanel_SelectedColorName_MarksOnlyThatSwatch() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var panel = new ColorPalettePanel();
        var window = new Window { Content = panel };
        window.Show();

        try
        {
            var swatches = panel.GetLogicalDescendants().OfType<Button>().Where(button => button.Classes.Contains("swatch")).ToList();
            var clear = panel.GetLogicalDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "CardColorAutomaticButton");
            var clearCheck = clear.GetLogicalDescendants().OfType<Control>().Last();

            // Act
            panel.SelectedColorName = "teal";

            // Assert
            Assert.Equal(["Teal"], swatches.Where(swatch => swatch.Classes.Contains("selected")).Select(swatch => swatch.Tag));
            Assert.False(clearCheck.IsVisible);

            // Act
            panel.SelectedColorName = null;

            // Assert
            Assert.DoesNotContain(swatches, swatch => swatch.Classes.Contains("selected"));
            Assert.True(clearCheck.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that clicking a swatch reports its color's name and clicking the clear button reports
    ///     no color.
    /// </summary>
    [Fact]
    public void ColorPalettePanel_SwatchOrClearClicked_RaisesColorChosen() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var panel = new ColorPalettePanel();
        var window = new Window { Content = panel };
        window.Show();
        var chosen = new List<string?>();
        panel.ColorChosen += (_, e) => chosen.Add(e.ColorName);

        try
        {
            var buttons = panel.GetLogicalDescendants().OfType<Button>().ToList();

            // Act
            buttons.Single(button => (string?)button.Tag == "Red").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            buttons.Single(button => AutomationProperties.GetAutomationId(button) == "CardColorAutomaticButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // Assert
            Assert.Equal(["Red", null], chosen);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the tiles of a colored group's repos without a card color of their own (a favorite
    ///     in the group included) take the group's color, that a card color of its own wins, and that
    ///     the card palette's heading says when the tile follows the group's color.
    /// </summary>
    [Fact]
    public void MainWindow_GroupColor_TilesWithoutOwnColorFollowAndOwnColorWins() => HeadlessTestApp.Run(() =>
    {
        // Arrange: "own" has a card color of its own; "fav" is a favorite in the group
        var (window, viewModel) = CreateWindow(("plain", "Work", false), ("own", "Work", false), ("fav", "Work", true));
        Assert.True(viewModel.RepoCards.Single(card => card.RepoName == "own").SetCardColor("Red"));

        try
        {
            // Act
            Assert.True(viewModel.SetGroupColor("Work", "Teal"));
            Dispatcher.UIThread.RunJobs();

            // Assert
            Border Tile(string name) =>
                window.GetVisualDescendantsOf<Border>("RepoAvatar").Single(border => border.DataContext is RepoCardViewModel card && card.RepoName == name);
            Assert.Contains("avatar5", Tile("plain").Classes);
            Assert.Contains("avatar5", Tile("fav").Classes);
            Assert.Contains("avatar8", Tile("own").Classes);
            Assert.Contains("customColor", Tile("plain").Classes);

            // Act: the card palette of the repo that follows the group's color
            var menu = OpenCardMenu(window, "plain");
            FindMenuItem(menu, "CardColorMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var moreButton = window.GetVisualDescendantsOf<Button>("MoreActionsButton")
                .Single(button => button.DataContext is RepoCardViewModel { RepoName: "plain" });
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(moreButton));
            var heading = Assert.IsType<ColorPalettePanel>(flyout.Content).GetLogicalDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "CardColorHeadingText");

            // Assert
            Assert.Equal("Card color: Automatic (group color Teal)", heading.Text);
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the app bar's add takes several folders from the picker, adds each repository, and
    ///     tells the user in one message which folders were not added and why.
    /// </summary>
    [Fact]
    public void MainWindow_AddRepoButton_SeveralFoldersPicked_AddsEachAndSummarizesTheRest() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: two new repositories, a plain folder and a repo that is already listed
        var (window, viewModel) = CreateWindow(("a", "Work", false));
        var first = CreateRepoDirectory("first");
        var second = CreateRepoDirectory("second");
        var plain = Path.Combine(CreateTempDirectory(), "plain");
        Directory.CreateDirectory(plain);
        var existing = viewModel.RepoCards.Single().RepoPath;
        window.RepoFolderPicker = () => Task.FromResult<IReadOnlyList<string>>([first, plain, existing, second]);

        try
        {
            // Act
            window.FindControl<Button>("AddRepoButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert: both repositories added (ungrouped), and one message that names the others
            Assert.Equal(["a", "first", "second"], viewModel.RepoCards.Select(card => card.RepoName));
            Assert.Null(viewModel.RepoCards.Single(card => card.RepoName == "first").GroupName);
            var message = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
            var text = message.FindControl<TextBlock>("MessageText")!.Text!;
            Assert.StartsWith("Added 2 of 4 folders.", text, StringComparison.Ordinal);
            Assert.Contains("Already in the list: a.", text, StringComparison.Ordinal);
            Assert.Contains("Not the top folder of a Git repository (no .git folder or file): plain.", text, StringComparison.Ordinal);
            message.Close();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group header's "Add repository..." takes several folders from the picker and puts
    ///     every repository in that group, silently when all were added.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenuAddRepository_SeveralFolders_JoinTheGroup() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", "Work", false), ("loose", null, false));
        var first = CreateRepoDirectory("first");
        var second = CreateRepoDirectory("second");
        window.RepoFolderPicker = () => Task.FromResult<IReadOnlyList<string>>([first, second]);

        try
        {
            // Act
            ClickHeaderAddRepository(window);
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "first").GroupName);
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "second").GroupName);
            Assert.Equal(
                "Work, 3 repos, expanded",
                window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton")
                    .Select(AutomationProperties.GetName)
                    .Single(name => name!.StartsWith("Work", StringComparison.Ordinal)));
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a folder that is not the top folder of a Git repository is refused with a message
    ///     that says why, and nothing is added.
    /// </summary>
    [Fact]
    public void MainWindow_AddRepoButton_FolderThatIsNotAGitRepository_IsRefusedWithAMessage() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", null, false));
        var plain = Path.Combine(CreateTempDirectory(), "plain");
        Directory.CreateDirectory(plain);
        window.RepoFolderPicker = () => Task.FromResult<IReadOnlyList<string>>([plain]);

        try
        {
            // Act
            window.FindControl<Button>("AddRepoButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert
            Assert.Single(viewModel.RepoCards);
            var message = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
            Assert.Equal(
                $"'{plain}' could not be added: it is not the top folder of a Git repository (it has no .git folder or file).",
                message.FindControl<TextBlock>("MessageText")!.Text);
            message.Close();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a picked folder that does not exist (gone between the pick and the add) is refused
    ///     with a message that says why, and nothing is added.
    /// </summary>
    [Fact]
    public void MainWindow_AddRepoButton_FolderThatDoesNotExist_IsRefusedWithAMessage() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel) = CreateWindow(("a", null, false));
        var missing = Path.Combine(CreateTempDirectory(), "gone");
        window.RepoFolderPicker = () => Task.FromResult<IReadOnlyList<string>>([missing]);

        try
        {
            // Act
            window.FindControl<Button>("AddRepoButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await RepoReorderWindowTests.SettleAddsAsync(viewModel);

            // Assert
            Assert.Single(viewModel.RepoCards);
            Assert.Single(viewModel.Settings.RecentRepos);
            var message = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
            Assert.Equal(
                $"'{missing}' could not be added: it does not exist.",
                message.FindControl<TextBlock>("MessageText")!.Text);
            message.Close();
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
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A card's background git check may still have the folder as its working directory
                // when the window closes; leaving a temporary folder behind is harmless
            }
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

        // The cards' repo state is read in the background; the tests act on settled cards
        HeadlessTestApp.RunJobsUntilRepoStateSettled(viewModel);
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
    ///     Opens the user group header's menu and chooses an item of it.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="itemName">The item's XAML name.</param>
    private static void ClickHeaderMenuItem(MainWindow window, string itemName)
    {
        var menuButton = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton").Single(button => button.IsVisible);
        var menu = OpenMenu(menuButton);
        FindMenuItem(menu, itemName).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
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
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
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
