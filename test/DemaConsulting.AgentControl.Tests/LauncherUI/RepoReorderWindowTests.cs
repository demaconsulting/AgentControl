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

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentToolLauncher;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the main window's repo order as the user meets it: cards that stay where they
///     are, the card menu's Move items, drag and drop within and between sections and onto headers,
///     the insertion line and header tint, the filter turning reordering off, and the order surviving a
///     restart; the user's order of the groups (the header menu's Move items and dragging a header);
///     a group's color and place following a rename, being forgotten when the group goes and being
///     kept when the Settings window is saved; and folders dropped from the file manager.
/// </summary>
/// <remarks>
///     The headless platform has no drag source, so a drag is started with a real pointer press and
///     move on the card (which calls the window's <c>DragStarter</c> seam, here a fake that records the
///     dragged data) and finished with the platform's drag events raised on the window at the pointer
///     position, exactly as the operating system would raise them. Folders from the file manager are
///     dropped the same way, with a data transfer that carries storage items for their paths. The real
///     operating-system drag is
///     covered only by the UI tests. Runs in the <c>RealProcess</c> collection because showing cards
///     spawns real git subprocesses as part of each card's background repo-state read.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoReorderWindowTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that existing repos are shown in the stored order, favorites first, whatever their
    ///     launch times.
    /// </summary>
    [Fact]
    public void MainWindow_ExistingRepos_ShownInStoredOrder_FavoritesFirst() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a stored order whose launch times point the other way round
        var now = DateTimeOffset.UtcNow;
        var (window, _, _) = CreateWindow(
            Spec("a", lastLaunched: now.AddDays(-9)), Spec("b", favorite: true, lastLaunched: now.AddDays(-5)),
            Spec("c", lastLaunched: now));

        try
        {
            // Assert
            Assert.Equal(["b", "a", "c"], RowNames(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that launching a repo does not move its card.
    /// </summary>
    [Fact]
    public void MainWindow_Launch_DoesNotMoveTheCard() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("a"), Spec("b"), Spec("c"));

        try
        {
            var card = viewModel.RepoCards.Single(candidate => candidate.RepoName == "c");

            // Act
            await card.LaunchCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.NotNull(card.LastLaunchedUtc);
            Assert.Equal(["a", "b", "c"], RowNames(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a repo added to the list, or straight into a group, appears at the bottom of its
    ///     section.
    /// </summary>
    [Fact]
    public void MainWindow_RepoAdded_AppearsAtTheBottomOfItsSection() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a group and ungrouped repos
        var (window, viewModel, _) = CreateWindow(Spec("w1", "Work"), Spec("u1"), Spec("u2"));

        try
        {
            // Act
            Assert.True(viewModel.AddRepo(CreateRepoDirectory("u3")));
            Assert.Equal(AddRepoResult.Added, viewModel.AddRepoToGroup(CreateRepoDirectory("w2"), "Work"));
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["w1", "w2", "u1", "u2", "u3"], RowNames(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the four Move items reorder the card within its section, are disabled where the move
    ///     leads nowhere (with the reason as their tooltip), and that the order is stored.
    /// </summary>
    [Fact]
    public void MainWindow_CardMenu_MoveItems_ReorderAndDisableAtTheBoundaries() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel, configDirectory) = CreateWindow(Spec("a"), Spec("b"), Spec("c"));

        try
        {
            // Act / Assert: the first repo cannot go to the top or up
            var menu = OpenCardMenu(window, "a");
            Assert.False(FindMenuItem(menu, "MoveToTopMenuItem").IsEnabled);
            Assert.False(FindMenuItem(menu, "MoveUpMenuItem").IsEnabled);
            Assert.True(FindMenuItem(menu, "MoveDownMenuItem").IsEnabled);
            Assert.True(FindMenuItem(menu, "MoveToBottomMenuItem").IsEnabled);
            Assert.Contains("first", ToolTip.GetTip(FindMenuItem(menu, "MoveUpMenuItem"))?.ToString(), StringComparison.Ordinal);

            // Act: Move down
            Click(FindMenuItem(menu, "MoveDownMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["b", "a", "c"], RowNames(window));
            Assert.Equal(["b", "a", "c"], viewModel.RepoCards.Select(card => card.RepoName));

            // Act / Assert: the last repo cannot go down or to the bottom, but can go to the top
            menu = OpenCardMenu(window, "c");
            Assert.False(FindMenuItem(menu, "MoveDownMenuItem").IsEnabled);
            Assert.False(FindMenuItem(menu, "MoveToBottomMenuItem").IsEnabled);
            Assert.Contains("last", ToolTip.GetTip(FindMenuItem(menu, "MoveDownMenuItem"))?.ToString(), StringComparison.Ordinal);
            Click(FindMenuItem(menu, "MoveToTopMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["c", "b", "a"], RowNames(window));

            // Act / Assert: a middle repo moves up, then to the bottom
            menu = OpenCardMenu(window, "b");
            Click(FindMenuItem(menu, "MoveUpMenuItem"));
            menu.Hide();
            menu = OpenCardMenu(window, "b");
            Click(FindMenuItem(menu, "MoveToBottomMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["c", "a", "b"], RowNames(window));
            HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
            Assert.Equal(
                RowNames(window), SettingsStore.Load(configDirectory).RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the Organize group of the card menu has exactly Move to group, Reorder and "Card
    ///     color...", and that the four Move items are in the Reorder submenu, each with an AutomationId,
    ///     an icon and a tooltip.
    /// </summary>
    [Fact]
    public void MainWindow_CardMenu_OrganizeGroupHasReorderSubmenuWithMoveItems() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _, _) = CreateWindow(Spec("a"), Spec("b"), Spec("c"));

        try
        {
            // Act
            var menu = OpenCardMenu(window, "b");
            var items = menu.Items.OfType<MenuItem>().ToList();
            var names = items.Select(item => item.Name).ToList();
            var organize = items.FindIndex(item => item.Header?.ToString() == "Organize");

            // Assert: Organize, Move to group, Reorder, Card color, then a separator and Remove
            string[] moves = ["MoveToTopMenuItem", "MoveUpMenuItem", "MoveDownMenuItem", "MoveToBottomMenuItem"];
            Assert.True(organize >= 0);
            Assert.Equal("MoveToGroupMenuItem", names[organize + 1]);
            Assert.Equal("ReorderMenuItem", names[organize + 2]);
            Assert.Equal("CardColorMenuItem", names[organize + 3]);
            Assert.Equal("RemoveMenuItem", names[organize + 4]);
            Assert.DoesNotContain(items, item => moves.Contains(item.Name));

            var reorder = items[organize + 2];
            Assert.Equal("Reorder", reorder.Header?.ToString());
            Assert.Equal("ReorderMenuItem", AutomationProperties.GetAutomationId(reorder));
            Assert.NotNull(reorder.Icon);
            Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(reorder)?.ToString()));
            var children = reorder.Items.OfType<MenuItem>().ToList();
            Assert.Equal(moves, children.Select(item => item.Name));
            foreach (var item in children)
            {
                Assert.Equal(item.Name, AutomationProperties.GetAutomationId(item));
                Assert.NotNull(item.Icon);
                Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(item)?.ToString()));
            }

            Assert.Equal(
                ["Move to top", "Move up", "Move down", "Move to bottom"],
                children.Select(item => item.Header?.ToString()));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the Reorder submenu is disabled, with the reason as its tooltip, for the only repo of
    ///     its section, and enabled for a repo that has a section neighbor.
    /// </summary>
    [Fact]
    public void MainWindow_CardMenu_Reorder_DisabledForTheOnlyRepoOfItsSection() => HeadlessTestApp.Run(() =>
    {
        // Arrange: one repo in a group, two ungrouped repos
        var (window, _, _) = CreateWindow(Spec("w1", "Work"), Spec("u1"), Spec("u2"));

        try
        {
            // Act / Assert: the only repo of its group has nothing to move past
            var menu = OpenCardMenu(window, "w1");
            var reorder = FindMenuItem(menu, "ReorderMenuItem");
            Assert.False(reorder.IsEnabled);
            Assert.Contains("only", ToolTip.GetTip(reorder)?.ToString(), StringComparison.Ordinal);
            menu.Hide();

            // Act / Assert: a repo with a neighbor can be reordered
            menu = OpenCardMenu(window, "u1");
            Assert.True(FindMenuItem(menu, "ReorderMenuItem").IsEnabled);
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that dragging a card and dropping it on the upper or lower half of another card of the
    ///     same section puts it before or after that card.
    /// </summary>
    [Fact]
    public void MainWindow_DragCard_WithinSection_ReordersAtDropPosition() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a group of three and an ungrouped repo
        var (window, viewModel, _) = CreateWindow(Spec("w1", "Work"), Spec("w2", "Work"), Spec("w3", "Work"), Spec("u1"));

        try
        {
            // Act: "w1" onto the lower half of "w2"
            var drag = BeginDrag(window, "w1");
            Assert.NotNull(drag.Transfer);
            Drop(window, drag, LowerHalf(window, "w2"));
            await drag.CompleteAsync();

            // Assert
            Assert.Equal(["w2", "w1", "w3", "u1"], RowNames(window));
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "w1").GroupName);

            // Act: "w3" onto the upper half of "w2"
            drag = BeginDrag(window, "w3");
            Drop(window, drag, UpperHalf(window, "w2"));
            await drag.CompleteAsync();

            // Assert
            Assert.Equal(["w3", "w2", "w1", "u1"], RowNames(window));
            Assert.DoesNotContain("dragging", FindRow(window, "w3").Classes);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that in the flat list (no groups) a drag only reorders within the card's tier: a drop in
    ///     the other tier is refused, and nothing is favorited or unfavorited.
    /// </summary>
    [Fact]
    public void MainWindow_DragCard_FlatList_OnlyReordersWithinItsTier() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a favorite and three other repos, no group anywhere
        var (window, viewModel, _) = CreateWindow(Spec("fav", favorite: true), Spec("a"), Spec("b"), Spec("c"));

        try
        {
            // Act: "a" onto the favorite (the other tier)
            var drag = BeginDrag(window, "a");
            var effects = DropAndGetEffects(window, drag, LowerHalf(window, "fav"));
            await drag.CompleteAsync();

            // Assert: refused
            Assert.Equal(DragDropEffects.None, effects);
            Assert.Equal(["fav", "a", "b", "c"], RowNames(window));
            Assert.False(viewModel.RepoCards.Single(card => card.RepoName == "a").IsFavorite);

            // Act: "a" within its own tier
            drag = BeginDrag(window, "a");
            Drop(window, drag, LowerHalf(window, "c"));
            await drag.CompleteAsync();

            // Assert
            Assert.Equal(["fav", "b", "c", "a"], RowNames(window));
            Assert.Empty(window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton"));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that dragging a card into another section changes its group or favorite state at the drop
    ///     position: group to group, group to Ungrouped, Ungrouped to a group, into Favorites (keeping the
    ///     group), and out of Favorites into a group or into Ungrouped.
    /// </summary>
    /// <param name="scenario">The case to run.</param>
    [Theory]
    [InlineData("GroupToGroup")]
    [InlineData("GroupToUngrouped")]
    [InlineData("UngroupedToGroup")]
    [InlineData("IntoFavoritesKeepsGroup")]
    [InlineData("OutOfFavoritesIntoGroup")]
    [InlineData("OutOfFavoritesIntoUngrouped")]
    public void MainWindow_DragCard_IntoOtherSection_ChangesGroupOrFavoriteAtDropPosition(string scenario) =>
        HeadlessTestApp.RunAsync(async () =>
        {
            // Arrange
            var plan = Plan(scenario);
            var (window, viewModel, configDirectory) = CreateWindow(plan.Specs);

            try
            {
                // Act
                var drag = BeginDrag(window, plan.Dragged);
                Drop(window, drag, plan.Upper ? UpperHalf(window, plan.Anchor) : LowerHalf(window, plan.Anchor));
                await drag.CompleteAsync();

                // Assert: the card has the expected state and place, and it was stored
                var card = viewModel.RepoCards.Single(candidate => candidate.RepoName == plan.Dragged);
                Assert.Equal(plan.ExpectedGroup, card.GroupName);
                Assert.Equal(plan.ExpectedFavorite, card.IsFavorite);
                Assert.Equal(plan.ExpectedRows, RowNames(window));
                HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
                var saved = SettingsStore.Load(configDirectory).RecentRepos.Single(repo => Path.GetFileName(repo.Path) == plan.Dragged);
                Assert.Equal(plan.ExpectedGroup, saved.GroupName);
                Assert.Equal(plan.ExpectedFavorite, saved.IsFavorite);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    ///     Test that dropping a card on a group's header moves it to the bottom of that group.
    /// </summary>
    [Fact]
    public void MainWindow_DropOnGroupHeader_MovesCardToTheGroupBottom() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("w1", "Work"), Spec("w2", "Work"), Spec("h1", "Home"), Spec("h2", "Home"));

        try
        {
            // Act: "h1" on the Work header
            var drag = BeginDrag(window, "h1");
            Drop(window, drag, OnHeader(window, "Work"));
            await drag.CompleteAsync();

            // Assert
            Assert.Equal(["h2", "w1", "w2", "h1"], RowNames(window));
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "h1").GroupName);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that dropping a card on a collapsed group's header moves it into the group and leaves
    ///     the group collapsed, with the header's count updated.
    /// </summary>
    [Fact]
    public void MainWindow_DropOnCollapsedGroupHeader_MovesCardAndKeepsTheGroupCollapsed() =>
        HeadlessTestApp.RunAsync(async () =>
        {
            // Arrange: a collapsed Work group
            var (window, viewModel, _) = CreateWindow(Spec("w1", "Work"), Spec("h1", "Home"), Spec("h2", "Home"));
            viewModel.ToggleGroupCollapsed("Work");
            Dispatcher.UIThread.RunJobs();

            try
            {
                Assert.Equal(["h1", "h2"], RowNames(window));

                // Act
                var drag = BeginDrag(window, "h1");
                Drop(window, drag, OnHeader(window, "Work"));
                await drag.CompleteAsync();

                // Assert: still collapsed, the count says 2, the card is not shown
                Assert.True(viewModel.IsGroupCollapsed("Work"));
                Assert.Equal(["h2"], RowNames(window));
                Assert.Equal(
                    ["Home, 1 repo, expanded", "Work, 2 repos, collapsed"],
                    window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton")
                        .OrderBy(button => button.TranslatePoint(new Point(0, 0), window)!.Value.Y)
                        .Select(AutomationProperties.GetName));
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    ///     Test that while a card is dragged a line shows above or below the card under the pointer (above
    ///     for its upper half), a header under the pointer is tinted, nothing is shown where a drop is
    ///     refused or next to the dragged card itself, and everything is cleared when the pointer leaves
    ///     and after the drop.
    /// </summary>
    [Fact]
    public void MainWindow_DragOver_ShowsInsertionIndicatorAboveOrBelowAndClearsItAfterwards() =>
        HeadlessTestApp.RunAsync(async () =>
        {
            // Arrange
            var (window, _, _) = CreateWindow(Spec("w1", "Work"), Spec("w2", "Work"), Spec("w3", "Work"), Spec("u1"));

            try
            {
                var drag = BeginDrag(window, "w1");
                Assert.Contains("dragging", FindRow(window, "w1").Classes);

                // Act / Assert: the upper half of "w2" shows the line above it
                Raise(window, drag, RawDragEventType.DragEnter, UpperHalf(window, "w2"));
                Raise(window, drag, RawDragEventType.DragOver, UpperHalf(window, "w2"));
                Assert.True(IsVisible(FindRow(window, "w2"), "DropIndicatorAbove"));
                Assert.False(IsVisible(FindRow(window, "w2"), "DropIndicatorBelow"));

                // The lower half shows it below
                Raise(window, drag, RawDragEventType.DragOver, LowerHalf(window, "w2"));
                Assert.False(IsVisible(FindRow(window, "w2"), "DropIndicatorAbove"));
                Assert.True(IsVisible(FindRow(window, "w2"), "DropIndicatorBelow"));

                // Over another card the first one is cleared
                Raise(window, drag, RawDragEventType.DragOver, UpperHalf(window, "w3"));
                Assert.False(IsVisible(FindRow(window, "w2"), "DropIndicatorBelow"));
                Assert.True(IsVisible(FindRow(window, "w3"), "DropIndicatorAbove"));

                // Over the dragged card itself nothing is shown
                Raise(window, drag, RawDragEventType.DragOver, UpperHalf(window, "w1"));
                Assert.False(IsVisible(FindRow(window, "w3"), "DropIndicatorAbove"));
                Assert.False(IsVisible(FindRow(window, "w1"), "DropIndicatorAbove"));

                // A header is tinted
                Raise(window, drag, RawDragEventType.DragOver, OnHeader(window, "Ungrouped"));
                Assert.Contains("dropHeader", FindHeaderRow(window, "Ungrouped").Classes);

                // Leaving the list clears it
                Raise(window, drag, RawDragEventType.DragLeave, OnHeader(window, "Ungrouped"));
                Assert.DoesNotContain("dropHeader", FindHeaderRow(window, "Ungrouped").Classes);

                // Act: show the line again, then drop
                Raise(window, drag, RawDragEventType.DragOver, LowerHalf(window, "w3"));
                Assert.True(IsVisible(FindRow(window, "w3"), "DropIndicatorBelow"));
                Raise(window, drag, RawDragEventType.Drop, LowerHalf(window, "w3"));
                await drag.CompleteAsync();

                // Assert: nothing is left on any row
                Assert.Equal(["w2", "w3", "w1", "u1"], RowNames(window));
                Assert.All(
                    window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>(),
                    row => Assert.DoesNotContain(row.Classes, name => name is "dropAbove" or "dropBelow" or "dropHeader" or "dragging"));
                Assert.All(
                    window.GetVisualDescendantsOf<Border>("DropIndicatorAbove").Concat(window.GetVisualDescendantsOf<Border>("DropIndicatorBelow")),
                    indicator => Assert.False(indicator.IsVisible));
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    ///     Test that while the filter has text a press and move on a card starts no drag and the Move items
    ///     are disabled with the explanation as their tooltip, and that clearing the filter turns both
    ///     back on.
    /// </summary>
    [Fact]
    public void MainWindow_FilterActive_DragStartsNothingAndMoveItemsAreDisabledWithTooltip() =>
        HeadlessTestApp.Run(() =>
        {
            // Arrange
            var (window, viewModel, _) = CreateWindow(Spec("alpha"), Spec("alpine"), Spec("beta"));

            try
            {
                // Act: filter, press and move on a card
                viewModel.FilterText = "al";
                Dispatcher.UIThread.RunJobs();
                var drag = BeginDrag(window, "alpine");

                // Assert: no drag, no dimming, Move items disabled with the explanation
                Assert.Null(drag.Transfer);
                Assert.DoesNotContain("dragging", FindRow(window, "alpine").Classes);
                var menu = OpenCardMenu(window, "alpine");
                var reorder = FindMenuItem(menu, "ReorderMenuItem");
                Assert.False(reorder.IsEnabled);
                Assert.Equal(MainWindowViewModel.ReorderDisabledWhileFilteringMessage, ToolTip.GetTip(reorder)?.ToString());
                foreach (var name in new[] { "MoveToTopMenuItem", "MoveUpMenuItem", "MoveDownMenuItem", "MoveToBottomMenuItem" })
                {
                    var item = FindMenuItem(menu, name);
                    Assert.False(item.IsEnabled, name);
                    Assert.Equal(MainWindowViewModel.ReorderDisabledWhileFilteringMessage, ToolTip.GetTip(item)?.ToString());
                }

                menu.Hide();
                Assert.Equal(["alpha", "alpine", "beta"], viewModel.RepoCards.Select(card => card.RepoName));

                // Act: clear the filter
                viewModel.FilterText = string.Empty;
                Dispatcher.UIThread.RunJobs();
                drag = BeginDrag(window, "alpine");

                // Assert: the drag starts and the items are enabled where they apply
                Assert.NotNull(drag.Transfer);
                drag.Completion.SetResult(DragDropEffects.None);
                Dispatcher.UIThread.RunJobs();
                menu = OpenCardMenu(window, "alpine");
                Assert.True(FindMenuItem(menu, "ReorderMenuItem").IsEnabled);
                Assert.True(FindMenuItem(menu, "MoveUpMenuItem").IsEnabled);
                menu.Hide();
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    ///     Test that an order made in the window is what a new window over the saved settings shows (a
    ///     restart).
    /// </summary>
    [Fact]
    public void MainWindow_ReorderedList_OrderSurvivesAWindowRestart() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, _, configDirectory) = CreateWindow(Spec("a"), Spec("b"), Spec("c"), Spec("d"));
        MainWindow? restarted = null;

        try
        {
            // Act: a drag and a menu move, then a "restart" from the saved settings
            var drag = BeginDrag(window, "d");
            Drop(window, drag, UpperHalf(window, "b"));
            await drag.CompleteAsync();
            var menu = OpenCardMenu(window, "a");
            Click(FindMenuItem(menu, "MoveToBottomMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var expected = RowNames(window);

            HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
            var viewModel = new MainWindowViewModel(SettingsStore.Load(configDirectory), configDirectory: configDirectory);
            restarted = new MainWindow { DataContext = viewModel, Width = 900, Height = 1600 };
            restarted.Show();
            Dispatcher.UIThread.RunJobs();
            HeadlessTestApp.RunJobsUntilRepoStateSettled(viewModel);

            // Assert
            Assert.Equal(["d", "b", "c", "a"], expected);
            Assert.Equal(expected, RowNames(restarted));
        }
        finally
        {
            restarted?.Close();
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the window lists Favorites first, then the groups in the user's stored order, then
    ///     the groups without a stored position alphabetically, then Ungrouped.
    /// </summary>
    [Fact]
    public void MainWindow_GroupOrder_StoredOrderThenAlphabeticalBetweenFavoritesAndUngrouped() => HeadlessTestApp.Run(() =>
    {
        // Arrange / Act: Gamma and Beta have stored positions, Alpha and Delta do not
        var (window, _, _) = CreateWindow(
            ["Gamma", "beta"],
            Spec("a", "Alpha"), Spec("b", "Beta"), Spec("u"), Spec("d", "Delta"), Spec("g", "Gamma"), Spec("f", favorite: true));

        try
        {
            // Assert
            Assert.Equal(["Favorites", "Gamma", "Beta", "Alpha", "Delta", "Ungrouped"], HeaderNames(window));
            Assert.Equal(["f", "g", "b", "a", "d", "u"], RowNames(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group header's menu moves the group up and down, that Move up is disabled for the
    ///     first group and Move down for the last (with the reason as the tooltip), and that both are
    ///     disabled while the filter has text.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenu_MoveUpDown_ReorderGroupsAndDisableAtTheEnds() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("a", "Alpha"), Spec("b", "Beta"), Spec("g", "Gamma"));

        try
        {
            // Assert: the first group cannot move up, the last cannot move down
            var menu = OpenHeaderMenu(window, "Alpha");
            var up = FindMenuItem(menu, "MoveGroupUpMenuItem");
            Assert.False(up.IsEffectivelyEnabled);
            Assert.Equal("This group is already the first group.", ToolTip.GetTip(up)?.ToString());
            Assert.True(FindMenuItem(menu, "MoveGroupDownMenuItem").IsEffectivelyEnabled);

            // Act: Alpha down
            Click(FindMenuItem(menu, "MoveGroupDownMenuItem"));
            menu.Hide();

            // Assert
            Assert.Equal(["Beta", "Alpha", "Gamma"], HeaderNames(window));

            // Act: Gamma up
            menu = OpenHeaderMenu(window, "Gamma");
            Assert.False(FindMenuItem(menu, "MoveGroupDownMenuItem").IsEffectivelyEnabled);
            Click(FindMenuItem(menu, "MoveGroupUpMenuItem"));
            menu.Hide();

            // Assert
            Assert.Equal(["Beta", "Gamma", "Alpha"], HeaderNames(window));
            Assert.Equal(["b", "g", "a"], RowNames(window));

            // Act: a filter turns both off
            viewModel.FilterText = "a";
            Dispatcher.UIThread.RunJobs();
            menu = OpenHeaderMenu(window, "Gamma");

            // Assert
            Assert.False(FindMenuItem(menu, "MoveGroupUpMenuItem").IsEffectivelyEnabled);
            Assert.False(FindMenuItem(menu, "MoveGroupDownMenuItem").IsEffectivelyEnabled);
            Assert.Equal(
                MainWindowViewModel.GroupReorderDisabledWhileFilteringMessage,
                ToolTip.GetTip(FindMenuItem(menu, "MoveGroupUpMenuItem"))?.ToString());
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group header can be dragged (pressed on its toggle, without collapsing or expanding
    ///     the section) below another group's header: the header is dimmed, the line shows below the
    ///     target header, the group lands there, it keeps its collapsed state, and the feedback is
    ///     cleared afterwards.
    /// </summary>
    [Fact]
    public void MainWindow_DragGroupHeader_BelowAnotherGroup_ReordersGroupsWithLineFeedback() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: Alpha is collapsed
        var (window, viewModel, _) = CreateWindow(Spec("a", "Alpha"), Spec("b", "Beta"), Spec("g", "Gamma"), Spec("u"));
        viewModel.ToggleGroupCollapsed("Alpha");
        Dispatcher.UIThread.RunJobs();

        try
        {
            // Act: start dragging Alpha's header
            var drag = BeginGroupDrag(window, "Alpha");

            // Assert: the group's name is dragged, as a group (not a card); the header is dimmed and
            // the press did not toggle the section
            Assert.NotNull(drag.Transfer);
            Assert.Equal("Alpha", drag.Transfer.TryGetValue(MainWindow.RepoGroupDataFormat));
            Assert.False(drag.Transfer.Contains(MainWindow.RepoCardDataFormat));
            Assert.Contains("dragging", FindHeaderRow(window, "Alpha").Classes);
            Assert.True(viewModel.IsGroupCollapsed("Alpha"));

            // Act: over the lower half of Gamma's header
            var point = PointIn(window, FindHeaderRow(window, "Gamma"), 0.75);
            Raise(window, drag, RawDragEventType.DragEnter, point);
            Raise(window, drag, RawDragEventType.DragOver, point);

            // Assert: the line below Gamma's header
            Assert.True(IsVisible(FindHeaderRow(window, "Gamma"), "GroupDropIndicatorBelow"));
            Assert.False(IsVisible(FindHeaderRow(window, "Gamma"), "GroupDropIndicatorAbove"));

            // Act: drop
            Raise(window, drag, RawDragEventType.Drop, point);
            await drag.CompleteAsync();

            // Assert: Alpha is last, still collapsed, and nothing is left shown
            Assert.Equal(["Beta", "Gamma", "Alpha", "Ungrouped"], HeaderNames(window));
            Assert.True(viewModel.IsGroupCollapsed("Alpha"));
            Assert.Equal(["Beta", "Gamma", "Alpha"], viewModel.Settings.GroupOrder);
            Assert.All(
                window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>(),
                row => Assert.DoesNotContain(row.Classes, name => name is "dropAbove" or "dropBelow" or "dropHeader" or "dragging"));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a dragged group header cannot be dropped on the Favorites or Ungrouped header or on a
    ///     card (the drop is refused and nothing changes), while the card drag keeps working as before.
    /// </summary>
    [Fact]
    public void MainWindow_DragGroupHeader_OntoBuiltInSectionOrCard_IsRefused() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("f", favorite: true), Spec("a", "Alpha"), Spec("b", "Beta"), Spec("u"));

        try
        {
            // Act / Assert: onto Ungrouped, Favorites and a card - refused
            var drag = BeginGroupDrag(window, "Beta");
            Assert.Equal(DragDropEffects.None, DropAndGetEffects(window, drag, OnHeader(window, "Ungrouped")));
            Assert.Equal(DragDropEffects.None, DropAndGetEffects(window, drag, OnHeader(window, "Favorites")));
            Assert.Equal(DragDropEffects.None, DropAndGetEffects(window, drag, UpperHalf(window, "a")));
            await drag.CompleteAsync();

            // Assert
            Assert.Equal(["Favorites", "Alpha", "Beta", "Ungrouped"], HeaderNames(window));
            Assert.Empty(viewModel.Settings.GroupOrder);

            // Act: a card is still dragged as a card
            var cardDrag = BeginDrag(window, "u");
            Drop(window, cardDrag, LowerHalf(window, "a"));
            await cardDrag.CompleteAsync();

            // Assert
            Assert.Equal("Alpha", viewModel.RepoCards.Single(card => card.RepoName == "u").GroupName);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that while the filter has text a press and move on a group header starts no drag, and
    ///     that Favorites and Ungrouped headers never start one.
    /// </summary>
    [Fact]
    public void MainWindow_FilterActive_GroupHeaderDragStartsNothing() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("f", favorite: true), Spec("alpha", "Alpha"), Spec("u"));

        try
        {
            // Act / Assert: the built-in headers never drag
            Assert.Null(BeginGroupDrag(window, "Favorites").Transfer);
            Assert.Null(BeginGroupDrag(window, "Ungrouped").Transfer);

            // Act: a filter
            viewModel.FilterText = "a";
            Dispatcher.UIThread.RunJobs();
            var drag = BeginGroupDrag(window, "Alpha");

            // Assert
            Assert.Null(drag.Transfer);
            Assert.DoesNotContain("dragging", FindHeaderRow(window, "Alpha").Classes);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group order made in the window (a header drag and a menu move) is what a new
    ///     window over the saved settings shows (a restart).
    /// </summary>
    [Fact]
    public void MainWindow_ReorderedGroups_OrderSurvivesAWindowRestart() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, _, configDirectory) = CreateWindow(Spec("a", "Alpha"), Spec("b", "Beta"), Spec("g", "Gamma"));
        MainWindow? restarted = null;

        try
        {
            // Act: Gamma dragged above Alpha, then Beta moved up; then a "restart"
            var drag = BeginGroupDrag(window, "Gamma");
            Drop(window, drag, PointIn(window, FindHeaderRow(window, "Alpha"), 0.25));
            await drag.CompleteAsync();
            var menu = OpenHeaderMenu(window, "Beta");
            Click(FindMenuItem(menu, "MoveGroupUpMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var expected = HeaderNames(window);

            HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
            var viewModel = new MainWindowViewModel(SettingsStore.Load(configDirectory), configDirectory: configDirectory);
            restarted = new MainWindow { DataContext = viewModel, Width = 900, Height = 1600 };
            restarted.Show();
            Dispatcher.UIThread.RunJobs();
            HeadlessTestApp.RunJobsUntilRepoStateSettled(viewModel);

            // Assert
            Assert.Equal(["Gamma", "Beta", "Alpha"], expected);
            Assert.Equal(expected, HeaderNames(restarted));
        }
        finally
        {
            restarted?.Close();
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group renamed through its header menu and the name prompt keeps its color accent
    ///     and its place in the list, in the window and in the saved settings.
    /// </summary>
    [Fact]
    public void MainWindow_RenameGroup_KeepsItsColorAccentAndItsPlace() => HeadlessTestApp.Run(() =>
    {
        // Arrange: Work is first only because of its stored place (alphabetically it would be last)
        var (window, viewModel, configDirectory) = CreateWindow(
            ["Work"], Spec("a", "Alpha"), Spec("w", "Work"), Spec("z", "Zed"));

        try
        {
            Assert.True(viewModel.SetGroupColor("Work", "Teal"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Work", "Alpha", "Zed"], HeaderNames(window));
            Assert.Equal("Group color: Teal", GroupAccentName(window, "Work"));

            // Act: Rename group... and a new name in the prompt
            ClickHeaderMenuItem(window, "Work", "RenameGroupMenuItem");
            var prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            ((GroupNameViewModel)prompt.DataContext!).Text = "Office";
            DialogWindowTests.Click(prompt, "GroupNameAcceptButton");
            Dispatcher.UIThread.RunJobs();

            // Assert: still first (alphabetically it would be between Alpha and Zed) and still teal
            Assert.Equal(["Office", "Alpha", "Zed"], HeaderNames(window));
            Assert.Equal("Group color: Teal", GroupAccentName(window, "Office"));
            HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
            var saved = SettingsStore.Load(configDirectory);
            Assert.Equal(["Office"], saved.GroupColors.Keys);
            Assert.Equal("Teal", saved.GroupColors["Office"]);
            Assert.Equal(["Office"], saved.GroupOrder);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group removed through its header menu, and a group whose last repo leaves it
    ///     through "Remove from group", lose their color and their place, and that a new group of the
    ///     same name is listed alphabetically and without a color accent.
    /// </summary>
    [Fact]
    public void MainWindow_RemoveGroupOrItsLastRepo_ForgetsItsColorAccentAndItsPlace() => HeadlessTestApp.Run(() =>
    {
        // Arrange: two colored groups with stored places ahead of Alpha
        var (window, viewModel, configDirectory) = CreateWindow(
            ["Work", "Home"], Spec("a", "Alpha"), Spec("w", "Work"), Spec("h", "Home"));

        try
        {
            Assert.True(viewModel.SetGroupColor("Work", "Teal"));
            Assert.True(viewModel.SetGroupColor("Home", "Red"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Work", "Home", "Alpha"], HeaderNames(window));

            // Act: Remove group, confirmed
            ClickHeaderMenuItem(window, "Work", "RemoveGroupMenuItem");
            var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            DialogWindowTests.Click(confirmation, "ConfirmationYesButton");
            Dispatcher.UIThread.RunJobs();

            // Assert: Work's color and place are gone
            Assert.Equal(["Home", "Alpha", "Ungrouped"], HeaderNames(window));
            HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
            var saved = SettingsStore.Load(configDirectory);
            Assert.Equal(["Home"], saved.GroupColors.Keys);
            Assert.Equal(["Home"], saved.GroupOrder);

            // Act: the last repo leaves Home
            var menu = OpenCardMenu(window, "h");
            Click(FindMenuItem(menu, "MoveToGroupMenuItem").Items.OfType<MenuItem>()
                .Single(item => AutomationProperties.GetAutomationId(item) == "RemoveFromGroupMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Assert: Home's color and place are gone
            Assert.Equal(["Alpha", "Ungrouped"], HeaderNames(window));
            HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
            saved = SettingsStore.Load(configDirectory);
            Assert.Empty(saved.GroupColors);
            Assert.Empty(saved.GroupOrder);

            // Act: a new group of the old name
            menu = OpenCardMenu(window, "w");
            Click(FindMenuItem(menu, "MoveToGroupMenuItem").Items.OfType<MenuItem>()
                .Single(item => AutomationProperties.GetAutomationId(item) == "NewGroupMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            var prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            ((GroupNameViewModel)prompt.DataContext!).Text = "Work";
            DialogWindowTests.Click(prompt, "GroupNameAcceptButton");
            Dispatcher.UIThread.RunJobs();

            // Assert: listed alphabetically (no old place) and without an accent (no old color)
            Assert.Equal(["Alpha", "Work", "Ungrouped"], HeaderNames(window));
            Assert.Null(GroupAccentName(window, "Work"));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that saving the Settings window (whose settings carry no group colors or order) keeps
    ///     the groups' color accents and their order, in the window and in the saved settings.
    /// </summary>
    [Fact]
    public void MainWindow_SettingsSaved_KeepsGroupColorAccentsAndOrder() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: Work is first only because of its stored place, and teal
        var (window, viewModel, configDirectory) = CreateWindow(["Work"], Spec("a", "Alpha"), Spec("w", "Work"));

        try
        {
            Assert.True(viewModel.SetGroupColor("Work", "Teal"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Work", "Alpha"], HeaderNames(window));

            // Act: open Settings, change the agent command (not the theme, which is application-wide) and save
            DialogWindowTests.Click(window, "OpenSettingsButton");
            Dispatcher.UIThread.RunJobs();
            var settingsWindow = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
            Assert.IsType<SettingsWindowViewModel>(settingsWindow.DataContext).CustomAgentCommand = "other-agent";
            DialogWindowTests.Click(settingsWindow, "SaveSettingsButton");
            await WaitUntilAsync(() => window.OwnedWindows.Count == 0 && viewModel.PendingRefresh.IsCompleted);
            Dispatcher.UIThread.RunJobs();

            // Assert: the save happened, and the accent and the order are kept
            Assert.Equal(["Work", "Alpha"], HeaderNames(window));
            Assert.Equal("Group color: Teal", GroupAccentName(window, "Work"));
            var saved = SettingsStore.Load(configDirectory);
            Assert.Equal("other-agent", saved.CustomAgentCommand);
            Assert.Equal("Teal", saved.GroupColors["Work"]);
            Assert.Equal(["Work"], saved.GroupOrder);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the user's organizing choices made through the window - moving a repo into a group,
    ///     a card color, a group color, moving a group down and collapsing a section - are written to the
    ///     settings file only, and every repo folder keeps exactly the entries it had.
    /// </summary>
    [Fact]
    public void MainWindow_GroupsColorsAndOrder_WriteNothingIntoTheRepository() => HeadlessTestApp.Run(() =>
    {
        // Arrange: Alpha and Beta (alphabetical, no stored order) and an ungrouped repo
        var (window, viewModel, configDirectory) = CreateWindow(Spec("a", "Alpha"), Spec("b", "Beta"), Spec("u"));
        var before = viewModel.RepoCards.ToDictionary(card => card.RepoPath, card => ListEntries(card.RepoPath));

        try
        {
            // Act: "u" into Beta from its card menu's Move to group
            var menu = OpenCardMenu(window, "u");
            Click(FindMenuItem(menu, "MoveToGroupMenuItem").Items.OfType<MenuItem>()
                .Single(item => AutomationProperties.GetName(item) == "Beta"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Act: Card color... > Teal on "a"
            menu = OpenCardMenu(window, "a");
            Click(FindMenuItem(menu, "CardColorMenuItem"));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();
            ChooseInPalette(
                window.GetVisualDescendantsOf<Button>("MoreActionsButton")
                    .Single(button => button.DataContext is RepoCardViewModel { RepoName: "a" }),
                "CardColorSwatch_Teal");

            // Act: Group color... > Red on Beta
            ClickHeaderMenuItem(window, "Beta", "GroupColorMenuItem");
            ChooseInPalette(
                window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton")
                    .Single(button => button.DataContext is RepoGroupHeaderViewModel { Name: "Beta" }),
                "GroupColorSwatch_Red");

            // Act: Alpha moved down, then collapsed from its header
            ClickHeaderMenuItem(window, "Alpha", "MoveGroupDownMenuItem");
            FindHeaderRow(window, "Alpha").GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "GroupHeaderToggleButton").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert: the window shows the choices
            Assert.Equal(["Beta", "Alpha"], HeaderNames(window));
            Assert.Equal("Group color: Red", GroupAccentName(window, "Beta"));

            // Assert: every repo folder keeps exactly the entries it had
            foreach (var (path, entries) in before)
            {
                Assert.Equal(entries, ListEntries(path));
            }

            // Assert: the settings file holds every choice
            HeadlessTestApp.RunJobsUntilSettingsWritten((MainWindowViewModel)window.DataContext!);
            var saved = SettingsStore.Load(configDirectory);
            Assert.Equal("Beta", saved.RecentRepos.Single(repo => Path.GetFileName(repo.Path) == "u").GroupName);
            Assert.Equal("Teal", saved.RecentRepos.Single(repo => Path.GetFileName(repo.Path) == "a").CardColor);
            Assert.Equal("Red", saved.GroupColors["Beta"]);
            Assert.Equal(["Beta", "Alpha"], saved.GroupOrder);
            Assert.Equal(["Alpha"], saved.CollapsedGroups);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that folders dropped from the file manager on a group's header, or on a card in a group's
    ///     section, are added to that group (and no card is moved).
    /// </summary>
    [Fact]
    public void MainWindow_DropFolders_OnGroupHeader_AddsThemToTheGroup() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("w1", "Work"), Spec("h1", "Home"), Spec("u1"));
        var first = CreateRepoDirectory("first");
        var second = CreateRepoDirectory("second");
        var third = CreateRepoDirectory("third");

        try
        {
            // Act: two folders onto the Work header, one onto the Home card
            var effects = DropExternal(window, OnHeader(window, "Work"), first, second);
            await SettleAddsAsync(viewModel);
            DropExternal(window, UpperHalf(window, "h1"), third);
            await SettleAddsAsync(viewModel);

            // Assert
            Assert.Equal(DragDropEffects.Copy, effects);
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "first").GroupName);
            Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoName == "second").GroupName);
            Assert.Equal("Home", viewModel.RepoCards.Single(card => card.RepoName == "third").GroupName);
            Assert.Equal(["h1", "third", "w1", "first", "second", "u1"], RowNames(window));
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that folders dropped on the Ungrouped header, on the Favorites header (a favorite is a mark
    ///     on a repo, not a place to add to) or on empty space in the list are added without a group.
    /// </summary>
    [Fact]
    public void MainWindow_DropFolders_OnUngroupedFavoritesOrEmptySpace_AddsThemUngrouped() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("f", "Work", favorite: true), Spec("w1", "Work"), Spec("u1"));
        var onUngrouped = CreateRepoDirectory("dropUngrouped");
        var onFavorites = CreateRepoDirectory("dropFavorites");
        var onEmpty = CreateRepoDirectory("dropEmpty");

        try
        {
            // Act
            DropExternal(window, OnHeader(window, "Ungrouped"), onUngrouped);
            await SettleAddsAsync(viewModel);
            DropExternal(window, OnHeader(window, "Favorites"), onFavorites);
            await SettleAddsAsync(viewModel);
            DropExternal(window, BelowTheRows(window), onEmpty);
            await SettleAddsAsync(viewModel);

            // Assert: all three ungrouped, none a favorite
            foreach (var name in new[] { "dropUngrouped", "dropFavorites", "dropEmpty" })
            {
                var card = viewModel.RepoCards.Single(other => other.RepoName == name);
                Assert.Null(card.GroupName);
                Assert.False(card.IsFavorite);
            }
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that folders dropped on the empty-state panel (no repo listed yet) are added.
    /// </summary>
    [Fact]
    public void MainWindow_DropFolders_OnEmptyStatePanel_AddsThem() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: no repos, so the empty-state panel is shown
        var (window, viewModel, _) = CreateWindow();
        var repo = CreateRepoDirectory("first");
        var panel = window.FindControl<Border>("EmptyStatePanel")!;
        Assert.True(panel.IsVisible);
        var center = panel.TranslatePoint(new Point(panel.Bounds.Width / 2, panel.Bounds.Height / 2), window)!.Value;

        try
        {
            // Act
            var effects = DropExternal(window, center, repo);
            await SettleAddsAsync(viewModel);

            // Assert
            Assert.Equal(DragDropEffects.Copy, effects);
            Assert.Equal(repo, Assert.Single(viewModel.RepoCards).RepoPath);
            Assert.False(panel.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that folders dragged from the file manager are answered with the copy effect, that a group
    ///     header they would join is tinted and the list area is outlined where they would be added
    ///     ungrouped, and that the feedback is cleared when the drag leaves and after the drop.
    /// </summary>
    [Fact]
    public void MainWindow_DropFolders_ShowsCopyFeedbackAndClearsIt() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("w1", "Work"), Spec("u1"));
        var transfer = CreateFileTransfer(window, CreateRepoDirectory("fresh"));
        var area = window.FindControl<Grid>("RepoListArea")!;
        var overlay = window.FindControl<Border>("ExternalDropOverlay")!;

        try
        {
            // Act / Assert: over the Work header - copy, the header is tinted, no outline
            var effects = RaiseExternal(window, transfer, RawDragEventType.DragEnter, OnHeader(window, "Work"));
            Assert.Equal(DragDropEffects.Copy, effects);
            Assert.Contains("dropHeader", FindHeaderRow(window, "Work").Classes);
            Assert.False(overlay.IsVisible);

            // Over the Ungrouped header: the outline instead of the tint
            effects = RaiseExternal(window, transfer, RawDragEventType.DragOver, OnHeader(window, "Ungrouped"));
            Assert.Equal(DragDropEffects.Copy, effects);
            Assert.DoesNotContain("dropHeader", FindHeaderRow(window, "Work").Classes);
            Assert.True(overlay.IsVisible);

            // Leaving clears everything
            RaiseExternal(window, transfer, RawDragEventType.DragLeave, OnHeader(window, "Ungrouped"));
            Assert.False(overlay.IsVisible);
            Assert.DoesNotContain("externalDrop", area.Classes);

            // A drop clears too
            RaiseExternal(window, transfer, RawDragEventType.DragEnter, OnHeader(window, "Work"));
            RaiseExternal(window, transfer, RawDragEventType.Drop, OnHeader(window, "Work"));
            await SettleAddsAsync(viewModel);
            Assert.False(overlay.IsVisible);
            Assert.All(
                window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>(),
                row => Assert.DoesNotContain("dropHeader", row.Classes));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a dropped file and a dropped folder that is not a Git repository are skipped and the
    ///     user is told so in one message, while the repository dropped with them is added; no card is
    ///     moved by the drop.
    /// </summary>
    [Fact]
    public void MainWindow_DropFile_IsSkippedAsNotARepository() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateWindow(Spec("u1"), Spec("u2"));
        var repo = CreateRepoDirectory("fresh");
        var file = Path.Combine(CreateTempDirectory(), "notes.txt");
        File.WriteAllText(file, "a file, not a folder");
        var plain = Path.Combine(CreateTempDirectory(), "plain");
        Directory.CreateDirectory(plain);

        try
        {
            // Act: dropped on a card, which must not be taken for a card move
            DropExternal(window, UpperHalf(window, "u1"), file, repo, plain);
            await SettleAddsAsync(viewModel);

            // Assert
            Assert.Equal(["u1", "u2", "fresh"], viewModel.RepoCards.Select(card => card.RepoName));
            var message = Assert.Single(window.OwnedWindows.OfType<MessageBoxWindow>());
            var text = message.FindControl<TextBlock>("MessageText")!.Text!;
            Assert.StartsWith("Added 1 of 3 folders.", text, StringComparison.Ordinal);
            Assert.Contains("notes.txt", text, StringComparison.Ordinal);
            Assert.Contains("plain", text, StringComparison.Ordinal);
            message.Close();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test the auto-scroll step for pointer positions relative to the list's viewport: nothing in
    ///     the middle or at a zone's inner boundary, slowly just inside a zone, faster deeper in and
    ///     beyond the edge (over the app bar, below the list), capped, the nearer edge for a short
    ///     viewport, and nothing for an unusable height or position.
    /// </summary>
    /// <param name="pointerY">The pointer's height relative to the viewport's top.</param>
    /// <param name="viewportHeight">The viewport's height.</param>
    /// <param name="expected">The expected step per tick (negative scrolls up).</param>
    [Theory]
    [InlineData(200.0, 400.0, 0.0)]
    [InlineData(32.0, 400.0, 0.0)]
    [InlineData(31.0, 400.0, -4.25)]
    [InlineData(0.0, 400.0, -12.0)]
    [InlineData(-96.0, 400.0, -36.0)]
    [InlineData(-500.0, 400.0, -36.0)]
    [InlineData(368.0, 400.0, 0.0)]
    [InlineData(369.0, 400.0, 4.25)]
    [InlineData(400.0, 400.0, 12.0)]
    [InlineData(496.0, 400.0, 36.0)]
    [InlineData(10.0, 40.0, -6.5)]
    [InlineData(20.0, 40.0, 0.0)]
    [InlineData(30.0, 40.0, 6.5)]
    [InlineData(10.0, 0.0, 0.0)]
    [InlineData(10.0, -5.0, 0.0)]
    [InlineData(double.NaN, 400.0, 0.0)]
    public void MainWindow_ComputeAutoScrollStep_ZonesAndDepth_GiveExpectedSteps(
        double pointerY, double viewportHeight, double expected)
    {
        // Act
        var step = MainWindow.ComputeAutoScrollStep(pointerY, viewportHeight);

        // Assert
        Assert.Equal(expected, step, 6);
    }

    /// <summary>
    ///     Test that the auto-scroll speed never drops as the pointer goes deeper into a zone and beyond
    ///     the edge, and never exceeds the cap.
    /// </summary>
    [Fact]
    public void MainWindow_ComputeAutoScrollStep_DeeperIsNeverSlower()
    {
        // Arrange / Act: from the inner boundary of the top zone to far above the list
        var steps = Enumerable.Range(0, 300)
            .Select(offset => Math.Abs(MainWindow.ComputeAutoScrollStep(31.0 - offset, 400.0)))
            .ToList();

        // Assert
        Assert.All(steps.Zip(steps.Skip(1)), pair => Assert.True(pair.Second >= pair.First));
        Assert.All(steps, step => Assert.InRange(step, MainWindow.AutoScrollMinStep, MainWindow.AutoScrollMaxStep));
    }

    /// <summary>
    ///     Test that a card held still near the bottom edge of the list keeps the list scrolling (every
    ///     tick scrolls further, without a new drag event) and that the scroll stops when the drag
    ///     leaves the list.
    /// </summary>
    [Fact]
    public void MainWindow_DragCardNearListEdge_KeepsScrollingWhileHeldAndStopsOnLeave() =>
        HeadlessTestApp.RunAsync(async () =>
        {
            // Arrange: a short window over a long list
            var (window, _, _) = CreateShortWindow(16);

            try
            {
                var drag = BeginDrag(window, "r00");
                var scroller = ListScroller(window);
                var nearBottom = NearListEdge(window, top: false);

                // Act: the card is dragged near the bottom edge and held there
                Raise(window, drag, RawDragEventType.DragEnter, nearBottom);
                Raise(window, drag, RawDragEventType.DragOver, nearBottom);

                // Assert: the auto-scroll runs, and each tick scrolls further down
                Assert.True(window.IsAutoScrolling);
                var before = scroller.Offset.Y;
                window.AutoScrollTick();
                var afterOne = scroller.Offset.Y;
                window.AutoScrollTick();
                Assert.True(afterOne > before, $"first tick: {before} -> {afterOne}");
                Assert.True(scroller.Offset.Y > afterOne, $"second tick: {afterOne} -> {scroller.Offset.Y}");

                // Act: the drag leaves the list (and the window)
                Raise(window, drag, RawDragEventType.DragLeave, new Point(-50, -50));

                // Assert
                Assert.False(window.IsAutoScrolling);
                await drag.CompleteAsync();
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    ///     Test that a group header held over the app bar (above the list) scrolls the list up, that a
    ///     drop there is refused (the groups keep their order) and that the drop stops the scroll.
    /// </summary>
    [Fact]
    public void MainWindow_DragGroupHeaderOverAppBar_ScrollsUpAndDropIsRefused() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a short window over many groups, scrolled to the bottom
        var (window, viewModel, _) = CreateShortWindow(12, grouped: true);

        try
        {
            var scroller = ListScroller(window);
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            var orderBefore = GroupOrder(viewModel);
            var lastGroup = orderBefore[^1];
            var drag = BeginGroupDrag(window, lastGroup);
            var overAppBar = OverAppLogo(window);

            // Act: the header is held over the app bar
            var effects = RaiseAndGetEffects(window, drag, RawDragEventType.DragEnter, overAppBar);
            var offsetBefore = scroller.Offset.Y;
            window.AutoScrollTick();

            // Assert: nothing can be dropped there, and the list scrolls up
            Assert.Equal(DragDropEffects.None, effects);
            Assert.True(window.IsAutoScrolling);
            Assert.True(scroller.Offset.Y < offsetBefore, $"tick: {offsetBefore} -> {scroller.Offset.Y}");

            // Act: dropped on the app bar
            Raise(window, drag, RawDragEventType.Drop, overAppBar);
            await drag.CompleteAsync();

            // Assert: refused, nothing reordered, the scroll stopped
            Assert.False(window.IsAutoScrolling);
            Assert.Equal(orderBefore, GroupOrder(viewModel));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the drop zone of a group header is worked out in thirds (before, inside, after) and,
    ///     in an auto-scroll zone, in halves that never nest (before, after).
    /// </summary>
    /// <param name="y">The pointer's height within the row.</param>
    /// <param name="inAutoScrollZone">Whether the pointer is in an auto-scroll zone.</param>
    /// <param name="insertBefore">The expected "before" answer.</param>
    /// <param name="inside">The expected "inside" answer.</param>
    [Theory]
    [InlineData(0.0, false, true, false)]
    [InlineData(19.0, false, true, false)]
    [InlineData(21.0, false, false, true)]
    [InlineData(40.0, false, false, true)]
    [InlineData(42.0, false, false, false)]
    [InlineData(61.0, false, false, false)]
    [InlineData(0.0, true, true, false)]
    [InlineData(25.0, true, true, false)]
    [InlineData(31.0, true, true, false)]
    [InlineData(31.5, true, false, false)]
    [InlineData(40.0, true, false, false)]
    [InlineData(61.0, true, false, false)]
    public void MainWindow_GetGroupDropZone_ThirdsOrHalvesInAutoScrollZone_NestsOnlyOutsideTheZone(
        double y, bool inAutoScrollZone, bool insertBefore, bool inside)
    {
        // Act
        var zone = MainWindow.GetGroupDropZone(y, 63.0, inAutoScrollZone);

        // Assert
        Assert.Equal((insertBefore, inside), zone);
    }

    /// <summary>
    ///     Test that a group header dropped over the middle of another group's header nests inside it
    ///     in the middle of the list, but never in an auto-scroll zone at the bottom or the top edge of a
    ///     list that scrolls: there the header is split in halves, the feedback shows a line instead of
    ///     the tint and the drop reorders. The groups are collapsed, so the list ends in a header and
    ///     starts with one, and the pointer held at an end of the list stays over it.
    /// </summary>
    [Fact]
    public void MainWindow_DragGroupHeaderInAutoScrollZone_NeverNestsAndReorders() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a short window over many collapsed groups
        var (window, viewModel, _) = CreateShortWindow(12, grouped: true);

        try
        {
            for (var index = 0; index < 12; index++)
            {
                viewModel.ToggleGroupCollapsed($"g{index:00}");
            }

            Dispatcher.UIThread.RunJobs();
            var scroller = ListScroller(window);

            // Act: g00 over the middle of g05's header, with g05 in the middle of the list
            var drag = BeginGroupDrag(window, "g00");
            var row = ScrollHeaderTo(window, "g05", 0.5, scroller.Viewport.Height / 2);
            var point = PointIn(window, row, 0.5);
            Raise(window, drag, RawDragEventType.DragEnter, point);
            Raise(window, drag, RawDragEventType.DragOver, point);

            // Assert: not in an auto-scroll zone, so the header is tinted, no line shows, and the drop nests
            Assert.False(window.IsAutoScrolling);
            Assert.Contains("dropHeader", row.Classes);
            Assert.False(IsVisible(row, "GroupDropIndicatorAbove"));
            Assert.False(IsVisible(row, "GroupDropIndicatorBelow"));
            Raise(window, drag, RawDragEventType.Drop, point);
            await drag.CompleteAsync();
            Assert.Equal("g05", viewModel.GetGroupParent("g00"));

            // Act: g01 over what would be the middle third of the last header (its lower half), at the end of the
            // list, in the bottom auto-scroll zone
            ScrollHeaderTo(window, "g01", 0.5, scroller.Viewport.Height / 2);
            drag = BeginGroupDrag(window, "g01");
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            scroller.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            HoldOverHeaderInAutoScrollZone(window, drag, "g11", 0.62, expectLineBelow: true);
            await drag.CompleteAsync();

            // Assert: g01 is the last group, not inside g11
            Assert.Null(viewModel.GetGroupParent("g01"));
            Assert.Null(viewModel.GetGroupParent("g11"));
            var order = GroupOrder(viewModel);
            Assert.Equal("g01", order[order.IndexOf("g11") + 1]);

            // Act: g10 over what would be the middle third of the first header (its upper half), at the start of the
            // list, in the top auto-scroll zone
            ScrollHeaderTo(window, "g10", 0.5, scroller.Viewport.Height / 2);
            drag = BeginGroupDrag(window, "g10");
            scroller.Offset = new Vector(0, 0);
            scroller.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            HoldOverHeaderInAutoScrollZone(window, drag, order[0], 0.4, expectLineBelow: false);
            await drag.CompleteAsync();

            // Assert: g10 is the first group
            Assert.Null(viewModel.GetGroupParent("g10"));
            Assert.Equal("g10", GroupOrder(viewModel)[0]);
        }
        finally
        {
            window.Close();
        }
    });
    /// <summary>
    ///     Test that a list that fits its window has no auto-scroll zones, so a group header dropped over
    ///     the middle of a header at the list's edge still nests.
    /// </summary>
    [Fact]
    public void MainWindow_DragGroupHeaderAtEdgeOfListThatFits_StillNests() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: two groups in a window that shows them all
        var (window, viewModel, _) = CreateWindow(Spec("r00", "g00"), Spec("r01", "g01"));

        try
        {
            var scroller = ListScroller(window);
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height, "the list should fit");
            var drag = BeginGroupDrag(window, "g01");
            var row = FindHeaderRow(window, "g00");

            // Act: g01 over the middle of g00's header, which is at the top edge of the list
            var point = PointIn(window, row, 0.5);
            Assert.InRange(window.TranslatePoint(point, scroller)!.Value.Y, 0, 31);
            Raise(window, drag, RawDragEventType.DragEnter, point);
            Raise(window, drag, RawDragEventType.DragOver, point);

            // Assert: the header is tinted and the drop nests
            Assert.Contains("dropHeader", row.Classes);
            Raise(window, drag, RawDragEventType.Drop, point);
            await drag.CompleteAsync();
            Assert.Equal("g00", viewModel.GetGroupParent("g01"));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Holds a dragged group header over another group's header at an end of the list, where it is in
    ///     an auto-scroll zone, checks the feedback (the line of the half the pointer is in, never the tint
    ///     of nesting) and drops it there.
    /// </summary>
    /// <remarks>
    ///     The list is at the end the pointer is held at, so a tick of the auto-scroll cannot move the
    ///     row under the pointer: the feedback and the drop are those of the place the test arranged.
    /// </remarks>
    /// <param name="window">The main window.</param>
    /// <param name="drag">The drag of a group's header.</param>
    /// <param name="target">The group whose header is at the end of the list.</param>
    /// <param name="fraction">The fraction of the target header's row height the pointer is at.</param>
    /// <param name="expectLineBelow">Whether the pointer is in the lower half, so the line is below.</param>
    private static void HoldOverHeaderInAutoScrollZone(
        MainWindow window, DragSession drag, string target, double fraction, bool expectLineBelow)
    {
        var scroller = ListScroller(window);
        var row = FindHeaderRow(window, target);
        var point = PointIn(window, row, fraction);

        // The pointer is in a zone that scrolls
        var inScroller = window.TranslatePoint(point, scroller)!.Value.Y;
        Assert.NotEqual(0.0, MainWindow.ComputeAutoScrollStep(inScroller, scroller.Viewport.Height));
        Raise(window, drag, RawDragEventType.DragEnter, point);
        Raise(window, drag, RawDragEventType.DragOver, point);

        // The feedback is a line, and no row is tinted as the place to nest
        Assert.DoesNotContain(
            window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>(),
            item => item.Classes.Contains("dropHeader"));
        Assert.Equal(expectLineBelow, IsVisible(row, "GroupDropIndicatorBelow"));
        Assert.Equal(!expectLineBelow, IsVisible(row, "GroupDropIndicatorAbove"));

        // Dropped there
        Raise(window, drag, RawDragEventType.Drop, point);
        Assert.False(window.IsAutoScrolling);
    }
    /// <summary>
    ///     Scrolls the list so that a point of a group's header is at a height of the list's viewport.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The group's name.</param>
    /// <param name="fraction">The fraction of the header row's height, from its top.</param>
    /// <param name="viewportY">The wanted height, from the top of the list's viewport.</param>
    /// <returns>The header's row.</returns>
    private static ListBoxItem ScrollHeaderTo(MainWindow window, string sectionName, double fraction, double viewportY)
    {
        var scroller = ListScroller(window);

        // Rows are realized as they scroll into view: look for the header from the top
        for (var offset = 0.0; offset <= scroller.Extent.Height; offset += 100)
        {
            scroller.Offset = new Vector(0, offset);
            scroller.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var row = window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>()
                .SingleOrDefault(item => item.DataContext is RepoGroupHeaderViewModel header && header.Name == sectionName);
            if (row is null)
            {
                continue;
            }

            var current = row.TranslatePoint(new Point(0, row.Bounds.Height * fraction), scroller)!.Value.Y;
            scroller.Offset = new Vector(0, Math.Max(0, scroller.Offset.Y + current - viewportY));
            scroller.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            return FindHeaderRow(window, sectionName);
        }

        throw new InvalidOperationException($"The header of {sectionName} was not found.");
    }

    /// <summary>
    ///     Test that folders from the file manager held near the bottom edge of the list scroll it, and
    ///     that dropping them stops the scroll (and adds them).
    /// </summary>
    [Fact]
    public void MainWindow_DropFoldersNearBottomEdge_AutoScrollsAndDropStopsIt() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateShortWindow(16);
        var transfer = CreateFileTransfer(window, CreateRepoDirectory("fresh"));

        try
        {
            var scroller = ListScroller(window);
            var nearBottom = NearListEdge(window, top: false);

            // Act: held near the bottom edge
            RaiseExternal(window, transfer, RawDragEventType.DragEnter, nearBottom);
            RaiseExternal(window, transfer, RawDragEventType.DragOver, nearBottom);
            var before = scroller.Offset.Y;
            window.AutoScrollTick();

            // Assert
            Assert.True(window.IsAutoScrolling);
            Assert.True(scroller.Offset.Y > before, $"tick: {before} -> {scroller.Offset.Y}");

            // Act: dropped
            RaiseExternal(window, transfer, RawDragEventType.Drop, nearBottom);
            await SettleAddsAsync(viewModel);

            // Assert: the scroll stopped and the folder was added
            Assert.False(window.IsAutoScrolling);
            Assert.Contains(viewModel.RepoCards, card => card.RepoName == "fresh");
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that after an auto-scroll tick the insertion line is shown for the card that is now
    ///     under the (unmoved) pointer, on exactly one row.
    /// </summary>
    [Fact]
    public void MainWindow_AutoScrollTick_UpdatesDropIndicatorForThePointer() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a card dragged near the bottom edge, with the line on the card under the pointer
        var (window, _, _) = CreateShortWindow(16);

        try
        {
            var drag = BeginDrag(window, "r00");
            var nearBottom = NearListEdge(window, top: false);
            Raise(window, drag, RawDragEventType.DragEnter, nearBottom);
            Raise(window, drag, RawDragEventType.DragOver, nearBottom);
            var before = RowsWithLine(window);

            // Act: scroll until another card is under the pointer
            for (var tick = 0; tick < 40 && RowsWithLine(window).SequenceEqual(before); tick++)
            {
                window.AutoScrollTick();
            }

            // Assert: the line moved to the card now under the pointer, and is shown once
            var after = RowsWithLine(window);
            Assert.Single(after);
            Assert.NotEqual(before, after);
            await drag.CompleteAsync();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a card drag that ends (however it ends, here without a drop) stops the auto-scroll.
    /// </summary>
    [Fact]
    public void MainWindow_CardDragEnds_StopsAutoScroll() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, _, _) = CreateShortWindow(16);

        try
        {
            var drag = BeginDrag(window, "r00");
            var nearBottom = NearListEdge(window, top: false);
            Raise(window, drag, RawDragEventType.DragEnter, nearBottom);
            Raise(window, drag, RawDragEventType.DragOver, nearBottom);
            Assert.True(window.IsAutoScrolling);

            // Act: the drag ends without a drop (Escape)
            await drag.CompleteAsync();

            // Assert
            Assert.False(window.IsAutoScrolling);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Creates a shown main window, short enough that its list scrolls, over repos named
    ///     <c>r00</c>, <c>r01</c>, ... (each in its own group <c>g00</c>, <c>g01</c>, ... when
    ///     <paramref name="grouped"/>).
    /// </summary>
    /// <param name="count">How many repos.</param>
    /// <param name="grouped">Whether each repo is in a group of its own.</param>
    /// <returns>The window, its view model and its configuration directory.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel, string ConfigDirectory) CreateShortWindow(
        int count, bool grouped = false)
    {
        var created = CreateWindow(
        [
            .. Enumerable.Range(0, count).Select(index => Spec(
                $"r{index:00}", grouped ? $"g{index:00}" : null))
        ]);
        created.Window.Height = 420;
        Dispatcher.UIThread.RunJobs();
        return created;
    }

    /// <summary>
    ///     Gets the repo list's scroll viewer.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The scroll viewer.</returns>
    private static ScrollViewer ListScroller(MainWindow window) =>
        window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ScrollViewer>().First();

    /// <summary>
    ///     Gets a point inside the list, 8 DIP from its top or bottom edge (in the auto-scroll zone).
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="top">Whether near the top edge rather than the bottom.</param>
    /// <returns>The point, in window coordinates.</returns>
    private static Point NearListEdge(MainWindow window, bool top)
    {
        var scroller = ListScroller(window);
        var y = top ? 8 : scroller.Bounds.Height - 8;
        return scroller.TranslatePoint(new Point(scroller.Bounds.Width / 2, y), window)
               ?? throw new InvalidOperationException("The list is not in the window.");
    }

    /// <summary>
    ///     Gets a point over the app bar's logo, above the list.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The point, in window coordinates.</returns>
    private static Point OverAppLogo(MainWindow window)
    {
        var logo = window.FindControl<Border>("AppLogo")!;
        return logo.TranslatePoint(new Point(logo.Bounds.Width / 2, logo.Bounds.Height / 2), window)
               ?? throw new InvalidOperationException("The logo is not in the window.");
    }

    /// <summary>
    ///     Lists the user's groups in the order the list shows them (realized or not).
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <returns>The group names.</returns>
    private static List<string> GroupOrder(MainWindowViewModel viewModel) =>
        [.. viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Where(header => header.IsUserGroup).Select(header => header.Name)];

    /// <summary>
    ///     Lists the repos whose rows show the insertion line.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The repo names.</returns>
    private static List<string> RowsWithLine(MainWindow window) =>
        [.. window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(row => row.DataContext is RepoCardViewModel &&
                          (row.Classes.Contains("dropAbove") || row.Classes.Contains("dropBelow")))
            .Select(row => ((RepoCardViewModel)row.DataContext!).RepoName)];

    /// <summary>
    ///     Raises one platform drag event of an in-window drag and returns the effect the window
    ///     answered with (whichever element handled it).
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="drag">The drag.</param>
    /// <param name="type">The drag event.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    /// <returns>The effect.</returns>
    private static DragDropEffects RaiseAndGetEffects(MainWindow window, DragSession drag, RawDragEventType type, Point point)
    {
        var effects = DragDropEffects.Move;
        EventHandler<DragEventArgs> record = (_, e) => effects = e.DragEffects;
        RoutedEvent<DragEventArgs>[] events = [DragDrop.DragEnterEvent, DragDrop.DragOverEvent];
        foreach (var routedEvent in events)
        {
            window.AddHandler(routedEvent, record, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        try
        {
            Raise(window, drag, type, point);
            return effects;
        }
        finally
        {
            foreach (var routedEvent in events)
            {
                window.RemoveHandler(routedEvent, record);
            }
        }
    }

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
                // A leftover temporary folder is harmless
            }
        }
    }

    /// <summary>
    ///     What one section-change case drags, where it drops it and what must be true afterwards.
    /// </summary>
    /// <param name="Specs">The repos to show.</param>
    /// <param name="Dragged">The dragged repo.</param>
    /// <param name="Anchor">The card the drop is on.</param>
    /// <param name="Upper">Whether the drop is on the upper half of the anchor.</param>
    /// <param name="ExpectedGroup">The dragged repo's group afterwards.</param>
    /// <param name="ExpectedFavorite">Whether the dragged repo is a favorite afterwards.</param>
    /// <param name="ExpectedRows">The cards from top to bottom afterwards.</param>
    private sealed record DragPlan(
        RepoSpec[] Specs, string Dragged, string Anchor, bool Upper, string? ExpectedGroup, bool ExpectedFavorite,
        string[] ExpectedRows);

    /// <summary>
    ///     Gets the section-change case with the given name.
    /// </summary>
    /// <param name="scenario">The case's name.</param>
    /// <returns>The case.</returns>
    private static DragPlan Plan(string scenario) => scenario switch
    {
        "GroupToGroup" => new DragPlan(
            [Spec("w1", "Work"), Spec("w2", "Work"), Spec("h1", "Home"), Spec("h2", "Home")],
            "h2", "w2", Upper: true, "Work", false, ["h1", "w1", "h2", "w2"]),
        "GroupToUngrouped" => new DragPlan(
            [Spec("w1", "Work"), Spec("w2", "Work"), Spec("u1"), Spec("u2")],
            "w1", "u1", Upper: false, null, false, ["w2", "u1", "w1", "u2"]),
        "UngroupedToGroup" => new DragPlan(
            [Spec("w1", "Work"), Spec("w2", "Work"), Spec("u1"), Spec("u2")],
            "u1", "w1", Upper: true, "Work", false, ["u1", "w1", "w2", "u2"]),
        "IntoFavoritesKeepsGroup" => new DragPlan(
            [Spec("f1", favorite: true), Spec("f2", favorite: true), Spec("w1", "Work"), Spec("w2", "Work")],
            "w2", "f1", Upper: false, "Work", true, ["f1", "w2", "f2", "w1"]),
        "OutOfFavoritesIntoGroup" => new DragPlan(
            [Spec("fav", "Home", favorite: true), Spec("w1", "Work"), Spec("w2", "Work")],
            "fav", "w1", Upper: false, "Work", false, ["w1", "fav", "w2"]),
        "OutOfFavoritesIntoUngrouped" => new DragPlan(
            [Spec("fav", "Work", favorite: true), Spec("w1", "Work"), Spec("u1")],
            "fav", "u1", Upper: true, null, false, ["w1", "fav", "u1"]),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.")
    };

    /// <summary>
    ///     One repo to create for a test.
    /// </summary>
    /// <param name="Name">The folder name (also the display name).</param>
    /// <param name="Group">The group, or <see langword="null"/>.</param>
    /// <param name="Favorite">Whether it is a favorite.</param>
    /// <param name="LastLaunched">When it was last launched, or <see langword="null"/>.</param>
    private sealed record RepoSpec(string Name, string? Group, bool Favorite, DateTimeOffset? LastLaunched);

    /// <summary>
    ///     Describes one repo to create for a test.
    /// </summary>
    /// <param name="name">The folder name.</param>
    /// <param name="group">The group, or <see langword="null"/>.</param>
    /// <param name="favorite">Whether it is a favorite.</param>
    /// <param name="lastLaunched">When it was last launched, or <see langword="null"/>.</param>
    /// <returns>The description.</returns>
    private static RepoSpec Spec(
        string name, string? group = null, bool favorite = false, DateTimeOffset? lastLaunched = null) =>
        new(name, group, favorite, lastLaunched);

    /// <summary>
    ///     One drag in progress: the data the window handed to the drag starter and the completion that
    ///     ends the drag.
    /// </summary>
    private sealed class DragSession
    {
        /// <summary>Gets or sets the data being dragged, or <see langword="null"/> before a drag started.</summary>
        public IDataTransfer? Transfer { get; set; }

        /// <summary>Gets the completion that ends the drag, as the operating system does when the button is released.</summary>
        public TaskCompletionSource<DragDropEffects> Completion { get; } = new();

        /// <summary>
        ///     Ends the drag (if it has not ended) and lets the window finish its clean-up.
        /// </summary>
        /// <returns>A task that completes when the window has handled the end of the drag.</returns>
        public async Task CompleteAsync()
        {
            Completion.TrySetResult(DragDropEffects.Move);
            await Completion.Task;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    ///     Creates a shown main window over repos in the given (stored) order, tall enough that every
    ///     card is realized and visible, with a fake process starter so a launch starts nothing.
    /// </summary>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The window, its view model and its configuration directory.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel, string ConfigDirectory) CreateWindow(
        params RepoSpec[] specs) => CreateWindow([], specs);

    /// <summary>
    ///     Creates a shown main window over repos in the given (stored) order and with a stored order of
    ///     the groups, like <see cref="CreateWindow(RepoSpec[])"/>.
    /// </summary>
    /// <param name="groupOrder">The user's stored order of the groups.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The window, its view model and its configuration directory.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel, string ConfigDirectory) CreateWindow(
        IReadOnlyList<string> groupOrder, params RepoSpec[] specs)
    {
        var configDirectory = CreateTempDirectory();
        var settings = new AppSettings
        {
            GroupOrder = [.. groupOrder],
            AgentTool = AgentToolKind.Custom,
            CustomAgentCommand = "my-agent",
            ShellPreference = "pwsh",
            HasUserRepoOrder = true,
            RecentRepos =
            [
                .. specs.Select(spec => new RecentRepo
                {
                    Path = CreateRepoDirectory(spec.Name),
                    GroupName = spec.Group,
                    IsFavorite = spec.Favorite,
                    LastLaunchedUtc = spec.LastLaunched
                })
            ]
        };
        var viewModel = new MainWindowViewModel(
            settings, configDirectory: configDirectory,
            processStarter: new FakeAgentToolProcessStarter(), shellDetector: FakeAgentToolProcessStarter.CreateShellDetector());
        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The cards' repo state is read in the background; the tests act on settled cards
        HeadlessTestApp.RunJobsUntilRepoStateSettled(viewModel);
        return (window, viewModel, configDirectory);
    }

    /// <summary>
    ///     Finds the list row of a repo's card.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The row.</returns>
    private static ListBoxItem FindRow(MainWindow window, string repoName) =>
        window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(row => row.DataContext is RepoCardViewModel card && card.RepoName == repoName);

    /// <summary>
    ///     Finds the list row of a section's header.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The section's name.</param>
    /// <returns>The row.</returns>
    private static ListBoxItem FindHeaderRow(MainWindow window, string sectionName) =>
        window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(row => row.DataContext is RepoGroupHeaderViewModel header && header.Name == sectionName);

    /// <summary>
    ///     Lists the repos whose cards are shown, top to bottom as the user sees them.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The repo names.</returns>
    internal static List<string> RowNames(MainWindow window) =>
        [.. window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(row => row.DataContext is RepoCardViewModel)
            .OrderBy(row => row.TranslatePoint(new Point(0, 0), window)!.Value.Y)
            .Select(row => ((RepoCardViewModel)row.DataContext!).RepoName)];

    /// <summary>
    ///     Gets whether a named drop indicator inside a row is shown.
    /// </summary>
    /// <param name="row">The list row.</param>
    /// <param name="indicatorName">The indicator's XAML name.</param>
    /// <returns><see langword="true"/> when it is visible.</returns>
    private static bool IsVisible(ListBoxItem row, string indicatorName) =>
        row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == indicatorName).IsVisible;

    /// <summary>
    ///     Gets a point in the window within the upper half of a card's row.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The point, in window coordinates.</returns>
    private static Point UpperHalf(MainWindow window, string repoName) => PointIn(window, FindRow(window, repoName), 0.25);

    /// <summary>
    ///     Gets a point in the window within the lower half of a card's row.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The point, in window coordinates.</returns>
    private static Point LowerHalf(MainWindow window, string repoName) => PointIn(window, FindRow(window, repoName), 0.75);

    /// <summary>
    ///     Gets a point in the window on a section's header row.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The section's name.</param>
    /// <returns>The point, in window coordinates.</returns>
    internal static Point OnHeader(MainWindow window, string sectionName) => PointIn(window, FindHeaderRow(window, sectionName), 0.5);

    /// <summary>
    ///     Gets a point at a fraction of a row's height, in window coordinates.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="row">The row.</param>
    /// <param name="fraction">The fraction of the row's height, from its top.</param>
    /// <returns>The point.</returns>
    private static Point PointIn(MainWindow window, ListBoxItem row, double fraction) =>
        row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height * fraction), window)
        ?? throw new InvalidOperationException("The row is not in the window.");

    /// <summary>
    ///     Presses the left button on a card (on its status bar, which is not one of its controls) and
    ///     moves the pointer far enough to start a drag, with the window's drag starter replaced by one
    ///     that records the dragged data and leaves the drag open.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo whose card is dragged.</param>
    /// <returns>The drag; its <see cref="DragSession.Transfer"/> is <see langword="null"/> when no drag
    ///     started.</returns>
    private static DragSession BeginDrag(MainWindow window, string repoName)
    {
        var session = new DragSession();
        window.DragStarter = (_, transfer, _) =>
        {
            session.Transfer = transfer;
            return session.Completion.Task;
        };

        var row = FindRow(window, repoName);
        var start = row.TranslatePoint(new Point(3, row.Bounds.Height / 2), window)
                    ?? throw new InvalidOperationException("The row is not in the window.");
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X + 20, start.Y + 2), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X + 20, start.Y + 2), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        return session;
    }

    /// <summary>
    ///     Lists the section headers that are shown, top to bottom.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The sections' names.</returns>
    private static List<string> HeaderNames(MainWindow window) =>
        [.. window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(row => row.DataContext is RepoGroupHeaderViewModel)
            .OrderBy(row => row.TranslatePoint(new Point(0, 0), window)!.Value.Y)
            .Select(row => ((RepoGroupHeaderViewModel)row.DataContext!).Name)];

    /// <summary>
    ///     Gets a point in the list below its last row (empty space).
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The point, in window coordinates.</returns>
    private static Point BelowTheRows(MainWindow window)
    {
        var list = window.FindControl<ListBox>("RepoListBox")!;
        return list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height - 20), window)
               ?? throw new InvalidOperationException("The list is not in the window.");
    }

    /// <summary>
    ///     Opens the "..." menu of a group's header.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="groupName">The group's name.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenHeaderMenu(MainWindow window, string groupName)
    {
        var button = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton")
            .Single(candidate => candidate.DataContext is RepoGroupHeaderViewModel header && header.Name == groupName);
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return flyout;
    }

    /// <summary>
    ///     Presses the left button on a section header's toggle (as a user grabs a header) and moves the
    ///     pointer far enough to start a drag, with the window's drag starter replaced by one that
    ///     records the dragged data and leaves the drag open.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The section whose header is dragged.</param>
    /// <returns>The drag; its <see cref="DragSession.Transfer"/> is <see langword="null"/> when no drag
    ///     started.</returns>
    private static DragSession BeginGroupDrag(MainWindow window, string sectionName)
    {
        var session = new DragSession();
        window.DragStarter = (_, transfer, _) =>
        {
            session.Transfer = transfer;
            return session.Completion.Task;
        };

        var toggle = FindHeaderRow(window, sectionName).GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "GroupHeaderToggleButton");
        var start = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)
                    ?? throw new InvalidOperationException("The header is not in the window.");
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(start.X + 2, start.Y + 20), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(start.X + 2, start.Y + 20), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        return session;
    }

    /// <summary>
    ///     Builds the data the file manager hands over when files or folders are dragged: one storage
    ///     item per path, as the window's storage provider describes the file or folder.
    /// </summary>
    /// <param name="window">The window whose storage provider describes the items.</param>
    /// <param name="paths">The dragged files' and folders' paths.</param>
    /// <returns>The data transfer.</returns>
    internal static DataTransfer CreateFileTransfer(MainWindow window, params string[] paths)
    {
        var transfer = new DataTransfer();
        foreach (var path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? window.StorageProvider.TryGetFolderFromPathAsync(path).GetAwaiter().GetResult()
                : window.StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult();
            transfer.Add(DataTransferItem.CreateFile(item ?? throw new InvalidOperationException($"No storage item for '{path}'.")));
        }

        return transfer;
    }

    /// <summary>
    ///     Raises one platform drag event of a drag from the file manager (which offers copy, move and
    ///     link) on the window, and returns the effect the window answered with.
    /// </summary>
    /// <remarks>
    ///     The platform raises a drag-enter instead of a drag-over when the element under the pointer
    ///     has changed (a header that is tinted is hit itself), so the effect of whichever drag event
    ///     was raised last is returned.
    /// </remarks>
    /// <param name="window">The main window.</param>
    /// <param name="transfer">The dragged data.</param>
    /// <param name="type">The drag event.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    /// <returns>The effect the window set on the last drag event it handled.</returns>
    internal static DragDropEffects RaiseExternal(MainWindow window, IDataTransfer transfer, RawDragEventType type, Point point)
    {
        var effects = DragDropEffects.None;
        EventHandler<DragEventArgs> record = (_, e) => effects = e.DragEffects;
        RoutedEvent<DragEventArgs>[] events = [DragDrop.DragEnterEvent, DragDrop.DragOverEvent, DragDrop.DropEvent];
        foreach (var routedEvent in events)
        {
            window.AddHandler(routedEvent, record, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        try
        {
            window.DragDrop(point, type, transfer, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            Dispatcher.UIThread.RunJobs();
            return effects;
        }
        finally
        {
            foreach (var routedEvent in events)
            {
                window.RemoveHandler(routedEvent, record);
            }
        }
    }

    /// <summary>
    ///     Drops files or folders from the file manager at a point: the pointer enters, moves and releases.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    /// <param name="paths">The dropped files' and folders' paths.</param>
    /// <returns>The effect the window answered the drag over the point with.</returns>
    internal static DragDropEffects DropExternal(MainWindow window, Point point, params string[] paths)
    {
        var transfer = CreateFileTransfer(window, paths);
        RaiseExternal(window, transfer, RawDragEventType.DragEnter, point);
        var effects = RaiseExternal(window, transfer, RawDragEventType.DragOver, point);
        RaiseExternal(window, transfer, RawDragEventType.Drop, point);
        return effects;
    }

    /// <summary>
    ///     Raises one platform drag event on the window at a point, carrying the dragged data.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="drag">The drag.</param>
    /// <param name="type">The drag event.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    private static void Raise(MainWindow window, DragSession drag, RawDragEventType type, Point point)
    {
        window.DragDrop(point, type, drag.Transfer!, DragDropEffects.Move);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Raises the events of a drop at a point: the pointer enters, moves over the list, and releases.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="drag">The drag.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    private static void Drop(MainWindow window, DragSession drag, Point point)
    {
        Raise(window, drag, RawDragEventType.DragEnter, point);
        Raise(window, drag, RawDragEventType.DragOver, point);
        Raise(window, drag, RawDragEventType.Drop, point);
    }

    /// <summary>
    ///     Raises the events of a drop at a point and returns the effect the window reported for the drop
    ///     (the operating system learns from it whether the card was accepted).
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="drag">The drag.</param>
    /// <param name="point">The pointer position, in window coordinates.</param>
    /// <returns>The effect the window set while the pointer was over the list.</returns>
    private static DragDropEffects DropAndGetEffects(MainWindow window, DragSession drag, Point point)
    {
        // The headless platform reports the effect of the last event back
        var effects = DragDropEffects.Move;
        var list = window.FindControl<ListBox>("RepoListBox")!;
        EventHandler<DragEventArgs> record = (_, e) => effects = e.DragEffects;
        list.AddHandler(DragDrop.DragOverEvent, record, RoutingStrategies.Bubble, handledEventsToo: true);
        try
        {
            Raise(window, drag, RawDragEventType.DragEnter, point);
            Raise(window, drag, RawDragEventType.DragOver, point);
            var overEffects = effects;
            Raise(window, drag, RawDragEventType.Drop, point);
            return overEffects;
        }
        finally
        {
            list.RemoveHandler(DragDrop.DragOverEvent, record);
        }
    }

    /// <summary>
    ///     Opens the "..." menu of the named repo's card, which fires the menu's opening event.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenCardMenu(MainWindow window, string repoName)
    {
        var button = window.GetVisualDescendantsOf<Button>("MoreActionsButton")
            .Single(candidate => candidate.DataContext is RepoCardViewModel card && card.RepoName == repoName);
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        return flyout;
    }

    /// <summary>
    ///     Finds an item of an open menu by its XAML name; the four Move items are found inside the
    ///     Reorder submenu.
    /// </summary>
    /// <param name="menu">The open menu.</param>
    /// <param name="name">The item's XAML name.</param>
    /// <returns>The item.</returns>
    private static MenuItem FindMenuItem(MenuFlyout menu, string name) =>
        menu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Name == name) ??
        menu.Items.OfType<MenuItem>().Single(item => item.Name == "ReorderMenuItem")
            .Items.OfType<MenuItem>().Single(item => item.Name == name);

    /// <summary>
    ///     Clicks a menu item the way a user's click does and lets the work it starts run.
    /// </summary>
    /// <param name="item">The item.</param>
    private static void Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Opens a group header's "..." menu, clicks one of its items and closes the menu, as a user does.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="groupName">The group's name.</param>
    /// <param name="itemName">The item's XAML name.</param>
    private static void ClickHeaderMenuItem(MainWindow window, string groupName, string itemName)
    {
        var menu = OpenHeaderMenu(window, groupName);
        Click(FindMenuItem(menu, itemName));
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Gets the accessible name of a section header's color accent, or <see langword="null"/> when
    ///     the accent is not shown.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="groupName">The group's name.</param>
    /// <returns>The accent's accessible name, for example <c>Group color: Teal</c>, or
    ///     <see langword="null"/>.</returns>
    private static string? GroupAccentName(MainWindow window, string groupName)
    {
        var accent = FindHeaderRow(window, groupName).GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "GroupColorAccent");
        return accent.IsVisible ? AutomationProperties.GetName(accent) : null;
    }

    /// <summary>
    ///     Chooses a swatch (or the clear button) of the color palette pop-up attached to a button, as a
    ///     click does.
    /// </summary>
    /// <param name="owner">The button the palette pop-up is attached to (a card's or a header's "...").</param>
    /// <param name="automationId">The AutomationId of the palette button to click.</param>
    private static void ChooseInPalette(Button owner, string automationId)
    {
        var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(owner));
        Assert.True(flyout.IsOpen);
        var palette = Assert.IsAssignableFrom<Control>(flyout.Content);
        palette.GetLogicalDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == automationId)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Lists every file and folder inside a folder, at any depth, in ordinal order.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns>The entries' paths.</returns>
    private static List<string> ListEntries(string path) =>
        [.. Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    /// <summary>
    ///     Waits for the folders dropped (or picked) so far to be added: runs the posted add, awaits the
    ///     view model's queued adds (their background checks resume on the UI thread while this waits),
    ///     then runs what the add posted afterwards (its message).
    /// </summary>
    /// <param name="viewModel">The window's view model.</param>
    /// <returns>A task that completes when the adds have ended.</returns>
    internal static async Task SettleAddsAsync(MainWindowViewModel viewModel)
    {
        Dispatcher.UIThread.RunJobs();
        await viewModel.PendingAddRepos;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Runs the UI thread's jobs until the condition holds (or about thirty seconds passed), then asserts it.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 1200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlReorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
