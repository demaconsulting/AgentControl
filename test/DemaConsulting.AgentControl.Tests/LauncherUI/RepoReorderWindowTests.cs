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
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
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
///     restart.
/// </summary>
/// <remarks>
///     The headless platform has no drag source, so a drag is started with a real pointer press and
///     move on the card (which calls the window's <c>DragStarter</c> seam, here a fake that records the
///     dragged data) and finished with the platform's drag events raised on the window at the pointer
///     position, exactly as the operating system would raise them. The real operating-system drag is
///     covered only by the UI tests. Runs in the <c>RealProcess</c> collection because showing cards
///     spawns real git subprocesses as part of each card's eager refresh.
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
            Assert.Equal(
                RowNames(window), SettingsStore.Load(configDirectory).RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the Move items are in the Organize group of the card menu, after "Move to group" and
    ///     before "Card color...", each with an AutomationId, an icon and a tooltip.
    /// </summary>
    [Fact]
    public void MainWindow_CardMenu_MoveItemsInOrganizeGroupWithIdsAndTooltips() => HeadlessTestApp.Run(() =>
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

            // Assert: Organize, Move to group, the four Move items, Card color
            string[] moves = ["MoveToTopMenuItem", "MoveUpMenuItem", "MoveDownMenuItem", "MoveToBottomMenuItem"];
            Assert.True(organize >= 0);
            Assert.Equal("MoveToGroupMenuItem", names[organize + 1]);
            Assert.Equal(moves, names.Skip(organize + 2).Take(4));
            Assert.Equal("CardColorMenuItem", names[organize + 6]);
            foreach (var name in moves)
            {
                var item = items.Single(candidate => candidate.Name == name);
                Assert.Equal(name, AutomationProperties.GetAutomationId(item));
                Assert.NotNull(item.Icon);
                Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(item)?.ToString()));
            }

            Assert.Equal(
                ["Move to top", "Move up", "Move down", "Move to bottom"],
                items.Where(item => moves.Contains(item.Name)).Select(item => item.Header?.ToString()));
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

            var viewModel = new MainWindowViewModel(SettingsStore.Load(configDirectory), configDirectory: configDirectory);
            restarted = new MainWindow { DataContext = viewModel, Width = 900, Height = 1600 };
            restarted.Show();
            Dispatcher.UIThread.RunJobs();

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
        params RepoSpec[] specs)
    {
        var configDirectory = CreateTempDirectory();
        var settings = new AppSettings
        {
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
    private static List<string> RowNames(MainWindow window) =>
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
    private static Point OnHeader(MainWindow window, string sectionName) => PointIn(window, FindHeaderRow(window, sectionName), 0.5);

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
    ///     Finds an item of an open menu by its XAML name.
    /// </summary>
    /// <param name="menu">The open menu.</param>
    /// <param name="name">The item's XAML name.</param>
    /// <returns>The item.</returns>
    private static MenuItem FindMenuItem(MenuFlyout menu, string name) =>
        menu.Items.OfType<MenuItem>().Single(item => item.Name == name);

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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlReorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
