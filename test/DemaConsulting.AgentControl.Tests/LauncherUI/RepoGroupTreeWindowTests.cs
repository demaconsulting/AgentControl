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

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the main window's tree of nested groups as the user meets it: how the groups
///     and their repos are listed, how deep each row is drawn, how a group is created inside another,
///     and how a group header is dragged into, out of and among other groups.
/// </summary>
/// <remarks>
///     The headless platform has no drag source, so a header is grabbed with a real pointer press and
///     move on its toggle (which calls the window's <c>DragStarter</c> seam, here a fake that records
///     the dragged data) and finished with the platform's drag events raised on the window at the
///     pointer position, as the operating system would raise them. Runs in the <c>RealProcess</c>
///     collection because showing cards spawns real git subprocesses as part of each card's
///     background repo-state read.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoGroupTreeWindowTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that nested groups are listed as a tree - each group's header followed by its subgroups
    ///     and then by its own repos, every header saying which group it is inside - and that collapsing a
    ///     group hides its whole subtree.
    /// </summary>
    [Fact]
    public void MainWindow_NestedGroups_ListSubgroupsBeforeOwnReposAndCollapseHidesSubtree() => HeadlessTestApp.Run(() =>
    {
        // Arrange: Client holds c1 and the subgroups Alpha (a1) and Project (p1)
        var (window, viewModel, _) = CreateClientWindow();

        try
        {
            // Act
            var headers = window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").ToList();

            // Assert: the tree, depth first, with the group's own repos after its subgroups
            Assert.Equal(
                [
                    "Client, 3 repos, expanded",
                    "Alpha, 1 repo, expanded, inside Client",
                    "Project, 1 repo, expanded, inside Client",
                    "Ungrouped, 1 repo, expanded"
                ],
                headers.Select(AutomationProperties.GetName));
            Assert.Equal(
                ["a1", "p1", "c1", "loose"],
                window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Select(text => text.Text));

            // Act: collapse the top group
            headers[0].Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert: its subgroups and their repos are hidden with it
            Assert.Equal(["loose"], window.GetVisualDescendantsOf<TextBlock>("RepoNameText").Select(text => text.Text));
            Assert.Equal(
                ["Client, 3 repos, collapsed", "Ungrouped, 1 repo, expanded"],
                window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton").Select(AutomationProperties.GetName));
            Assert.True(viewModel.IsGroupCollapsed("Client"));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that every row is drawn as far in as the groups it is inside: a nested group's header and
    ///     the repos listed under it start further right than a top-level group's, level by level, and
    ///     the repos line up with the header of the group that lists them.
    /// </summary>
    [Fact]
    public void MainWindow_NestedGroups_RowsAreIndentedByDepth() => HeadlessTestApp.Run(() =>
    {
        // Arrange: Client > Project > Deep (d1); c1 is a repo of Client itself
        var (window, _, _) = CreateWindow(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Project"] = "Client", ["Deep"] = "Project" },
            ("c1", "Client"), ("d1", "Deep"));

        try
        {
            // Act
            var indent = RepoGroupHeaderViewModel.IndentPerLevel;

            // Assert: headers by depth, with the unchanged top margin
            Assert.Equal(new Thickness(0, 8, 0, 0), HeaderGrid(window, "Client").Margin);
            Assert.Equal(new Thickness(indent, 8, 0, 0), HeaderGrid(window, "Project").Margin);
            Assert.Equal(new Thickness(2 * indent, 8, 0, 0), HeaderGrid(window, "Deep").Margin);

            // Assert: a repo is as far in as its group's header
            Assert.Equal(new Thickness(0), CardBorder(window, "c1").Margin);
            Assert.Equal(new Thickness(2 * indent, 0, 0, 0), CardBorder(window, "d1").Margin);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group header's menu has "New subgroup...", which asks for a name in the prompt,
    ///     refuses the name of any existing group, creates the group inside the header's group when a name
    ///     is accepted (the new header appears, with no repos, saying which group it is inside) and
    ///     changes nothing when canceled.
    /// </summary>
    [Fact]
    public void MainWindow_HeaderMenu_NewSubgroup_PromptsAndCreatesGroupInside() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, viewModel, _) = CreateClientWindow();

        try
        {
            // Act: choose New subgroup... on Project, then cancel
            ClickHeaderMenuItem(window, "Project", "GroupNewSubgroupMenuItem");
            var prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            var promptModel = (GroupNameViewModel)prompt.DataContext!;
            Assert.Equal("New subgroup", promptModel.Title);
            DialogWindowTests.Click(prompt, "GroupNameCancelButton");
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["Alpha", "Client", "Project"], viewModel.GroupNames);

            // Act: choose it again and try the name of an existing group (another one's, other letter
            // case) and then a new name
            ClickHeaderMenuItem(window, "Project", "GroupNewSubgroupMenuItem");
            prompt = Assert.Single(window.OwnedWindows.OfType<GroupNameWindow>());
            promptModel = (GroupNameViewModel)prompt.DataContext!;
            promptModel.Text = "alpha";

            // Assert: refused, not "joined"
            Assert.True(promptModel.HasError);
            Assert.False(promptModel.CanAccept);
            Assert.False(promptModel.HasHint);

            // Act
            promptModel.Text = "Phase 1";
            DialogWindowTests.Click(prompt, "GroupNameAcceptButton");
            Dispatcher.UIThread.RunJobs();

            // Assert: a header with no repos, inside Project
            Assert.Equal(["Alpha", "Client", "Phase 1", "Project"], viewModel.GroupNames);
            Assert.Equal(
                [
                    "Client, 3 repos, expanded",
                    "Alpha, 1 repo, expanded, inside Client",
                    "Project, 1 repo, expanded, inside Client",
                    "Phase 1, 0 repos, expanded, inside Project",
                    "Ungrouped, 1 repo, expanded"
                ],
                HeaderAccessibleNames(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the card menu's "Move to group" submenu lists the groups that have no repo and the
    ///     nested ones too, a nested group's tooltip giving its path, and that choosing an empty group
    ///     moves the repo into it.
    /// </summary>
    [Fact]
    public void MainWindow_CardMenu_MoveToGroup_ListsEmptyAndNestedGroupsWithPath() => HeadlessTestApp.Run(() =>
    {
        // Arrange: Empty is a group inside Client with no repo
        var (window, viewModel, _) = CreateWindow(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Empty"] = "Client" },
            ("c1", "Client"), ("loose", null));

        try
        {
            // Act
            var menu = OpenCardMenu(window, "loose");
            var submenu = menu.Items.OfType<MenuItem>().Single(item => item.Name == "MoveToGroupMenuItem");
            var entries = submenu.Items.OfType<MenuItem>()
                .Where(item => AutomationProperties.GetAutomationId(item) == "MoveToGroupItem")
                .ToList();

            // Assert
            Assert.Equal(["Client", "Empty"], entries.Select(AutomationProperties.GetName));
            Assert.Equal("Move loose to the group Client.", ToolTip.GetTip(entries[0])?.ToString());
            Assert.Equal("Move loose to the group Client > Empty.", ToolTip.GetTip(entries[1])?.ToString());

            // Act: choose the empty group
            entries[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide();
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal("Empty", viewModel.RepoCards.Single(card => card.RepoName == "loose").GroupName);
            Assert.Contains("Empty, 1 repo, expanded, inside Client", HeaderAccessibleNames(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group header is dropped in one of three places by where the pointer is over another
    ///     group's header - the upper third puts it before that group, the middle third inside it (the
    ///     header is tinted), the lower third after it - and that a group dropped next to a nested group
    ///     leaves the group it was in.
    /// </summary>
    [Fact]
    public void MainWindow_DragGroupHeader_ThreeZones_NestBeforeAndAfter() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: Client > Alpha, Client > Project; Home is a top-level group
        var (window, viewModel, _) = CreateWindow(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Alpha"] = "Client", ["Project"] = "Client" },
            ("c1", "Client"), ("a1", "Alpha"), ("p1", "Project"), ("h1", "Home"));

        try
        {
            // Act: Home over the middle of Client's header
            var drag = BeginGroupDrag(window, "Home");
            var middle = PointIn(window, FindHeaderRow(window, "Client"), 0.5);
            Raise(window, drag, RawDragEventType.DragEnter, middle);
            Raise(window, drag, RawDragEventType.DragOver, middle);

            // Assert: the header is tinted, no line is shown
            Assert.Contains("dropHeader", FindHeaderRow(window, "Client").Classes);
            Assert.False(IsVisible(FindHeaderRow(window, "Client"), "GroupDropIndicatorAbove"));
            Assert.False(IsVisible(FindHeaderRow(window, "Client"), "GroupDropIndicatorBelow"));

            // Act: drop
            Raise(window, drag, RawDragEventType.Drop, middle);
            await drag.CompleteAsync();

            // Assert: Home is Client's last subgroup
            Assert.Equal(["Client", "Alpha", "Project", "Home"], HeaderNames(window));
            Assert.Equal("Client", viewModel.GetGroupParent("Home"));
            Assert.Contains("Home, 1 repo, expanded, inside Client", HeaderAccessibleNames(window));

            // Act: Home over the upper third of Alpha's header
            drag = BeginGroupDrag(window, "Home");
            var upper = PointIn(window, FindHeaderRow(window, "Alpha"), 0.15);
            Raise(window, drag, RawDragEventType.DragEnter, upper);
            Raise(window, drag, RawDragEventType.DragOver, upper);

            // Assert: the line above Alpha
            Assert.True(IsVisible(FindHeaderRow(window, "Alpha"), "GroupDropIndicatorAbove"));
            Assert.DoesNotContain("dropHeader", FindHeaderRow(window, "Alpha").Classes);

            // Act
            Raise(window, drag, RawDragEventType.Drop, upper);
            await drag.CompleteAsync();

            // Assert: first inside Client
            Assert.Equal(["Client", "Home", "Alpha", "Project"], HeaderNames(window));

            // Act: Home over the lower third of Client's own header: next to Client, at the top level
            drag = BeginGroupDrag(window, "Home");
            var lower = PointIn(window, FindHeaderRow(window, "Client"), 0.85);
            Raise(window, drag, RawDragEventType.DragEnter, lower);
            Raise(window, drag, RawDragEventType.DragOver, lower);
            Assert.True(IsVisible(FindHeaderRow(window, "Client"), "GroupDropIndicatorBelow"));
            Raise(window, drag, RawDragEventType.Drop, lower);
            await drag.CompleteAsync();

            // Assert: Home left Client
            Assert.Null(viewModel.GetGroupParent("Home"));
            Assert.Equal(["Client", "Alpha", "Project", "Home"], HeaderNames(window));
            Assert.Contains("Home, 1 repo, expanded", HeaderAccessibleNames(window));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a group header dragged onto a group inside itself - over any part of the header - is
    ///     refused (no feedback, the drop is not accepted) and nothing changes.
    /// </summary>
    [Fact]
    public void MainWindow_DragGroupHeader_OntoOwnDescendant_IsRefused() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, _) = CreateClientWindow();

        try
        {
            var before = HeaderNames(window);

            // Act / Assert: Client over each zone of its own subgroup's header
            foreach (var fraction in new[] { 0.15, 0.5, 0.85 })
            {
                var drag = BeginGroupDrag(window, "Client");
                var point = PointIn(window, FindHeaderRow(window, "Project"), fraction);
                Raise(window, drag, RawDragEventType.DragEnter, point);
                Raise(window, drag, RawDragEventType.DragOver, point);
                Assert.DoesNotContain("dropHeader", FindHeaderRow(window, "Project").Classes);
                Assert.False(IsVisible(FindHeaderRow(window, "Project"), "GroupDropIndicatorAbove"));
                Assert.False(IsVisible(FindHeaderRow(window, "Project"), "GroupDropIndicatorBelow"));
                Raise(window, drag, RawDragEventType.Drop, point);
                await drag.CompleteAsync();
            }

            Assert.Equal(before, HeaderNames(window));
            Assert.Null(viewModel.GetGroupParent("Client"));
            Assert.Equal("Client", viewModel.GetGroupParent("Project"));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that a tree made in the window - a group created inside another and a group dropped into
    ///     another - is what the next session shows: the settings written for it are loaded into a new
    ///     window that lists the same tree.
    /// </summary>
    [Fact]
    public void MainWindow_NestedGroups_TreeSurvivesAWindowRestart() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var (window, viewModel, configDirectory) = CreateWindow(
            new Dictionary<string, string>(), ("w1", "Work"), ("h1", "Home"));
        string[] expected;
        try
        {
            // Act: Work gets a new subgroup, and Home is dropped inside Work
            Assert.True(viewModel.CreateSubgroup("Work", "Admin"));
            var drag = BeginGroupDrag(window, "Home");
            Drop(window, drag, PointIn(window, FindHeaderRow(window, "Work"), 0.5));
            await drag.CompleteAsync();
            expected = [.. HeaderAccessibleNames(window)];
            await viewModel.FlushSettingsAsync();
        }
        finally
        {
            window.Close();
        }

        // Assert: the first session's tree
        Assert.Equal(
            ["Work, 2 repos, expanded", "Admin, 0 repos, expanded, inside Work", "Home, 1 repo, expanded, inside Work"],
            expected);

        // Act: a new session over the saved settings
        var saved = SettingsStore.Load(configDirectory);
        var restarted = new MainWindowViewModel(saved, configDirectory: configDirectory);
        var second = new MainWindow { DataContext = restarted, Height = 1600 };
        second.Show();
        Dispatcher.UIThread.RunJobs();
        HeadlessTestApp.RunJobsUntilRepoStateSettled(restarted);
        try
        {
            // Assert
            Assert.Equal(expected, HeaderAccessibleNames(second));
        }
        finally
        {
            second.Close();
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
    ///     Creates the window most tests use: Client holds c1 and the subgroups Project (p1) and
    ///     Alpha (a1); one repo is in no group.
    /// </summary>
    /// <returns>The window, its view model and its configuration directory.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel, string ConfigDirectory) CreateClientWindow() =>
        CreateWindow(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Project"] = "Client", ["Alpha"] = "Client" },
            ("c1", "Client"), ("p1", "Project"), ("a1", "Alpha"), ("loose", null));

    /// <summary>
    ///     Creates a shown main window over repos with the given names and groups, and a group hierarchy.
    /// </summary>
    /// <param name="parents">The stored hierarchy.</param>
    /// <param name="repos">The repos as (folder name, group or <see langword="null"/>).</param>
    /// <returns>The window, its view model and its configuration directory.</returns>
    private (MainWindow Window, MainWindowViewModel ViewModel, string ConfigDirectory) CreateWindow(
        Dictionary<string, string> parents, params (string Name, string? Group)[] repos)
    {
        var settings = new AppSettings
        {
            HasUserRepoOrder = true,
            RecentRepos =
            [
                .. repos.Select(repo => new RecentRepo { Path = CreateRepoDirectory(repo.Name), GroupName = repo.Group })
            ],
            GroupParents = new Dictionary<string, string>(parents, RepoGroupName.Comparer)
        };
        var configDirectory = CreateTempDirectory();
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDirectory);
        var window = new MainWindow { DataContext = viewModel, Height = 1600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The cards' repo state is read in the background; the tests act on settled cards
        HeadlessTestApp.RunJobsUntilRepoStateSettled(viewModel);
        return (window, viewModel, configDirectory);
    }

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
    ///     Finds the grid that lays out a section's header.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The section's name.</param>
    /// <returns>The grid.</returns>
    private static Grid HeaderGrid(MainWindow window, string sectionName) =>
        FindHeaderRow(window, sectionName).GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "GroupHeader");

    /// <summary>
    ///     Finds the card border of a repo.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The border that draws the card.</returns>
    private static Border CardBorder(MainWindow window, string repoName) =>
        window.FindControl<ListBox>("RepoListBox")!.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(row => row.DataContext is RepoCardViewModel card && card.RepoName == repoName)
            .GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("RepoCard"));

    /// <summary>
    ///     Lists the names of the section headers, top to bottom.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The names.</returns>
    private static List<string> HeaderNames(MainWindow window) =>
        [.. window.GetVisualDescendantsOf<TextBlock>("GroupHeaderNameText")
            .OrderBy(text => text.TranslatePoint(new Point(0, 0), window)!.Value.Y)
            .Select(text => text.Text!)];

    /// <summary>
    ///     Lists the accessible names of the section headers, top to bottom.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <returns>The accessible names.</returns>
    private static List<string> HeaderAccessibleNames(MainWindow window) =>
        [.. window.GetVisualDescendantsOf<Button>("GroupHeaderToggleButton")
            .OrderBy(button => button.TranslatePoint(new Point(0, 0), window)!.Value.Y)
            .Select(AutomationProperties.GetName)
            .OfType<string>()];

    /// <summary>
    ///     Gets whether a named drop indicator inside a row is shown.
    /// </summary>
    /// <param name="row">The list row.</param>
    /// <param name="indicatorName">The indicator's XAML name.</param>
    /// <returns><see langword="true"/> when it is visible.</returns>
    private static bool IsVisible(ListBoxItem row, string indicatorName) =>
        row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == indicatorName).IsVisible;

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
    ///     Presses the left button on a section header's toggle (as a user grabs a header) and moves the
    ///     pointer far enough to start a drag, with the window's drag starter replaced by one that
    ///     records the dragged data and leaves the drag open.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="sectionName">The section whose header is dragged.</param>
    /// <returns>The drag, whose data has been handed to the drag starter.</returns>
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
        Assert.NotNull(session.Transfer);
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
    ///     Opens the "..." menu of a group's header and chooses an item of it.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="groupName">The group's name.</param>
    /// <param name="itemName">The item's XAML name.</param>
    private static void ClickHeaderMenuItem(MainWindow window, string groupName, string itemName)
    {
        var button = window.GetVisualDescendantsOf<Button>("GroupHeaderMenuButton")
            .Single(candidate => candidate.DataContext is RepoGroupHeaderViewModel header && header.Name == groupName);
        var menu = OpenMenu(button);
        menu.Items.OfType<MenuItem>().Single(item => item.Name == itemName)
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    ///     Opens the "..." menu of the named repo's card.
    /// </summary>
    /// <param name="window">The main window.</param>
    /// <param name="repoName">The repo's folder name.</param>
    /// <returns>The open menu.</returns>
    private static MenuFlyout OpenCardMenu(MainWindow window, string repoName) =>
        OpenMenu(window.GetVisualDescendantsOf<Button>("MoreActionsButton")
            .Single(candidate => candidate.DataContext is RepoCardViewModel card && card.RepoName == repoName));

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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlGroupTreeWindow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
