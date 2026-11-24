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

using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests of the tree of groups in <see cref="MainWindowViewModel"/>: how nested groups are
///     listed, created, reordered, dropped into and out of each other, renamed and removed, and that a
///     list without a hierarchy is shown exactly as before.
/// </summary>
/// <remarks>
///     Each test uses unique temporary repo and configuration directories. Runs in the
///     <c>RealProcess</c> collection because every card's repo state is read in the background (real git
///     subprocesses) from the moment the view model is built; every view model a test builds is tracked, and
///     its reads have finished (and it is disposed) before the folders are removed.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoGroupTreeTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     The view models built by the running test, settled and disposed before the folders go.
    /// </summary>
    private readonly List<MainWindowViewModel> _viewModels = [];

    /// <summary>
    ///     Test that nested groups are listed depth first, each group's subgroups before its own repos,
    ///     the subgroups in alphabetical order, with each header counting the repos of its whole subtree,
    ///     and that every header and card knows how deep it is.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedItems_NestedGroups_ListedDepthFirstSubgroupsBeforeOwnRepos()
    {
        // Arrange: Client holds c1 and the subgroups Project (p1) and Alpha (a1)
        var viewModel = CreateClientTree();

        // Act
        var shown = Describe(viewModel);
        var headers = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().ToList();

        // Assert
        Assert.Equal(
            ["[Client 3]", "[Alpha 1]", "a1", "[Project 1]", "p1", "c1", "[Ungrouped 1]", "loose"],
            shown);
        Assert.Multiple(
            () => Assert.Equal([0, 1, 1, 0], headers.Select(header => header.Depth)),
            () => Assert.Equal([null, "Client", "Client", null], headers.Select(header => header.ParentName)),
            () => Assert.Equal(
                [1, 1, 0, 0],
                viewModel.DisplayedRepoCards.Select(card => card.Depth)),
            () => Assert.Equal("3 repos (1 directly in this group)", headers[0].CountTooltip),
            () => Assert.Equal("Alpha, 1 repo, expanded, inside Client", headers[1].AccessibleName),
            () => Assert.Equal("Client, 3 repos, expanded", headers[0].AccessibleName),
            () => Assert.Equal(RepoGroupHeaderViewModel.IndentPerLevel, headers[1].IndentWidth),
            () => Assert.Equal(RepoGroupHeaderViewModel.IndentPerLevel, viewModel.DisplayedRepoCards[0].IndentWidth));
    }

    /// <summary>
    ///     Test that settings with no hierarchy are shown exactly as a flat list of groups was: every group
    ///     top level, no depth, no parent, and the same sections and counts.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedItems_NoHierarchy_FlatGroupsShownAsBefore()
    {
        // Arrange
        var viewModel = CreateViewModel(
            parents: null,
            new Spec("w1", Group: "Work"),
            new Spec("h1", Group: "Home"),
            new Spec("w2", Group: "Work"),
            new Spec("loose"));

        // Act
        var headers = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().ToList();

        // Assert
        Assert.Equal(["[Home 1]", "h1", "[Work 2]", "w1", "w2", "[Ungrouped 1]", "loose"], Describe(viewModel));
        Assert.All(headers, header => Assert.Equal(0, header.Depth));
        Assert.All(headers, header => Assert.Null(header.ParentName));
        Assert.All(viewModel.DisplayedRepoCards, card => Assert.Equal(0, card.Depth));
        Assert.Empty(viewModel.Settings.GroupParents);
    }

    /// <summary>
    ///     Test that collapsing a group hides its whole subtree - its subgroups and their repos too - and
    ///     that a filter shows every match, the groups that hold it included, whatever is collapsed.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedItems_CollapsedGroup_HidesSubtreeAndFilterShowsAncestors()
    {
        // Arrange
        var viewModel = CreateClientTree();

        // Act: collapse the top group
        viewModel.ToggleGroupCollapsed("Client");

        // Assert
        Assert.Equal(["[Client 3 collapsed]", "[Ungrouped 1]", "loose"], Describe(viewModel));

        // Act: filter for a repo deep in the tree
        viewModel.FilterText = "p1";

        // Assert: the ancestors are listed with only the match counted, nothing collapsed
        Assert.Equal(["[Client 1]", "[Project 1]", "p1"], Describe(viewModel));
    }

    /// <summary>
    ///     Test that a group that holds only a favorite is not listed under its parent (the repo is under
    ///     Favorites with its group as a chip) but that a nested group stays listed, with no repo, because
    ///     it takes part in the hierarchy.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedItems_NestedGroupWithOnlyFavorite_StaysListedWithNoRepos()
    {
        // Arrange
        var viewModel = CreateClientTree();
        var project = viewModel.RepoCards.Single(card => card.RepoName == "p1");

        // Act
        project.IsFavorite = true;

        // Assert
        Assert.Equal(
            [
                "[Favorites 1]", "p1",
                "[Client 2]", "[Alpha 1]", "a1", "[Project 0]", "c1",
                "[Ungrouped 1]", "loose"
            ],
            Describe(viewModel));
        Assert.True(project.ShowGroupChip);
    }

    /// <summary>
    ///     Test that a group created inside another group is listed with no repos, expands its collapsed
    ///     parent, registers the parent (and the groups the parent is inside), and is saved with the
    ///     hierarchy only in the settings file.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CreateSubgroup_ListsEmptyGroupRegistersAncestorsAndSaves()
    {
        // Arrange: Project is inside Client; Client is collapsed
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir,
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Project"] = "Client" },
            new Spec("p1", Group: "Project"),
            new Spec("w1", Group: "Work"));
        viewModel.ToggleGroupCollapsed("Client");

        // Act: create a group inside Project, and one inside the (unregistered) Work group
        Assert.True(viewModel.CreateSubgroup("project", "  Phase  1 "));
        Assert.True(viewModel.CreateSubgroup("Work", "Admin"));

        // Assert
        Assert.Equal(
            ["[Client 1]", "[Project 1]", "[Phase 1 0]", "p1", "[Work 1]", "[Admin 0]", "w1"],
            Describe(viewModel));
        Assert.False(viewModel.IsGroupCollapsed("Client"));
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Project"] = "Client",
                ["Phase 1"] = "Project",
                ["Work"] = string.Empty,
                ["Admin"] = "Work"
            },
            saved.GroupParents);
    }

    /// <summary>
    ///     Test that a group's name must be unique across the whole tree (also against an empty group and
    ///     in another letter case) and valid, and that the parent must exist; nothing changes when a
    ///     creation is refused.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CreateSubgroup_RefusesTakenInvalidAndUnknownParent()
    {
        // Arrange
        var viewModel = CreateClientTree();
        var before = Describe(viewModel);

        // Act / Assert
        Assert.Multiple(
            () => Assert.False(viewModel.CreateSubgroup("Client", "project")),
            () => Assert.False(viewModel.CreateSubgroup("Alpha", "CLIENT")),
            () => Assert.False(viewModel.CreateSubgroup("Client", "Favorites")),
            () => Assert.False(viewModel.CreateSubgroup("Client", "ungrouped")),
            () => Assert.False(viewModel.CreateSubgroup("Client", "   ")),
            () => Assert.False(viewModel.CreateSubgroup("Client", new string('x', RepoGroupName.MaxLength + 1))),
            () => Assert.False(viewModel.CreateSubgroup("Missing", "Fresh")),
            () => Assert.False(viewModel.CreateSubgroup("Favorites", "Fresh")));
        Assert.Equal(before, Describe(viewModel));
    }

    /// <summary>
    ///     Test the life of a group that was created empty: it stays when a repo is put in it and taken out
    ///     again, shows with a count of 0, is hidden while a filter has text, and is gone once it is
    ///     removed.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RegisteredEmptyGroup_StaysUntilRemoved()
    {
        // Arrange
        var viewModel = CreateViewModel(parents: null, new Spec("w1", Group: "Work"), new Spec("loose"));
        Assert.True(viewModel.CreateSubgroup("Work", "Admin"));
        var loose = viewModel.RepoCards.Single(card => card.RepoName == "loose");

        // Act / Assert: a repo goes in and out, the group stays
        Assert.True(loose.MoveToGroup("admin"));
        Assert.Equal(["[Work 2]", "[Admin 1]", "loose", "w1"], Describe(viewModel));
        Assert.True(loose.MoveToGroup(null));
        Assert.Equal(["[Work 1]", "[Admin 0]", "w1", "[Ungrouped 1]", "loose"], Describe(viewModel));

        // Act / Assert: hidden while filtering
        viewModel.FilterText = "w1";
        Assert.Equal(["[Work 1]", "w1"], Describe(viewModel));
        viewModel.FilterText = string.Empty;

        // Act / Assert: removing it forgets it
        Assert.True(viewModel.RemoveGroup("Admin"));
        Assert.Equal(["[Work 1]", "w1", "[Ungrouped 1]", "loose"], Describe(viewModel));
        Assert.DoesNotContain("Admin", viewModel.GroupNames);
    }

    /// <summary>
    ///     Test that Move up and Move down move a group among its siblings only, naming the parent group in
    ///     the reason when it is the first or the last of its parent's groups, while the wording at the top
    ///     level stays as it was.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveGroup_NestedGroups_SwapWithinTheirParent()
    {
        // Arrange: Alpha, Project inside Client; Home next to Client at the top level
        var viewModel = CreateViewModel(
            parents: new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Alpha"] = "Client",
                ["Project"] = "Client"
            },
            new Spec("a1", Group: "Alpha"),
            new Spec("p1", Group: "Project"),
            new Spec("h1", Group: "Home"));

        // Assert: the disabled reasons say which level
        Assert.Multiple(
            () => Assert.Equal(
                "This group is already the first group in its parent group.",
                viewModel.GetGroupMoveDisabledReason("Alpha", RepoGroupMove.Up)),
            () => Assert.Equal(
                "This group is already the last group in its parent group.",
                viewModel.GetGroupMoveDisabledReason("Project", RepoGroupMove.Down)),
            () => Assert.Equal(
                "This group is already the first group.",
                viewModel.GetGroupMoveDisabledReason("Client", RepoGroupMove.Up)),
            () => Assert.Equal(
                "This group is already the last group.",
                viewModel.GetGroupMoveDisabledReason("Home", RepoGroupMove.Down)),
            () => Assert.Null(viewModel.GetGroupMoveDisabledReason("Client", RepoGroupMove.Down)),
            () => Assert.Null(viewModel.GetGroupMoveDisabledReason("Home", RepoGroupMove.Up)));

        // Act: Project goes up before Alpha
        Assert.True(viewModel.MoveGroup("Project", RepoGroupMove.Up));

        // Assert: only the children of Client swapped; Home and Client keep their places
        Assert.Equal(
            ["[Client 2]", "[Project 1]", "p1", "[Alpha 1]", "a1", "[Home 1]", "h1"],
            Describe(viewModel));
    }

    /// <summary>
    ///     Test that moving a flat group up or down (nothing nested) does not register it in the hierarchy,
    ///     so a group that loses its last repo disappears as it always did.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveGroup_FlatGroups_DoesNotCreateAHierarchy()
    {
        // Arrange
        var viewModel = CreateViewModel(parents: null, new Spec("a", Group: "Alpha"), new Spec("w", Group: "Work"));

        // Act
        Assert.True(viewModel.MoveGroup("Work", RepoGroupMove.Up));
        Assert.True(viewModel.DropGroup("Alpha", new GroupDropTarget("Work", InsertBefore: true)));

        // Assert
        Assert.Equal(["[Alpha 1]", "a", "[Work 1]", "w"], Describe(viewModel));
        Assert.Empty(viewModel.Settings.GroupParents);
    }

    /// <summary>
    ///     Test dropping a group header: before and after a sibling it reorders, inside another group it
    ///     becomes that group's last subgroup, and next to a nested group it leaves its old parent for that
    ///     group's parent (or the top level) - all saved with the hierarchy in one write.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_DropGroup_NestsReordersAndLeavesTheOldParent()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir,
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Alpha"] = "Client",
                ["Project"] = "Client"
            },
            new Spec("a1", Group: "Alpha"),
            new Spec("p1", Group: "Project"),
            new Spec("h1", Group: "Home"),
            new Spec("z1", Group: "Zeta"));

        // Act / Assert: Home goes inside Client as the last subgroup
        Assert.True(viewModel.DropGroup("Home", new GroupDropTarget("Client", InsertBefore: false, Inside: true)));
        Assert.Equal(
            ["[Client 3]", "[Alpha 1]", "a1", "[Project 1]", "p1", "[Home 1]", "h1", "[Zeta 1]", "z1"],
            Describe(viewModel));

        // Act / Assert: Zeta goes before Alpha: it becomes a child of Client, first
        Assert.True(viewModel.DropGroup("Zeta", new GroupDropTarget("Alpha", InsertBefore: true)));
        Assert.Equal(
            ["[Client 4]", "[Zeta 1]", "z1", "[Alpha 1]", "a1", "[Project 1]", "p1", "[Home 1]", "h1"],
            Describe(viewModel));

        // Act / Assert: Project goes after Client: it leaves Client for the top level
        Assert.True(viewModel.DropGroup("Project", new GroupDropTarget("Client", InsertBefore: false)));
        Assert.Equal(
            ["[Client 3]", "[Zeta 1]", "z1", "[Alpha 1]", "a1", "[Home 1]", "h1", "[Project 1]", "p1"],
            Describe(viewModel));

        // Assert: one write holds the hierarchy and the order
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Alpha"] = "Client",
                ["Project"] = string.Empty,
                ["Home"] = "Client",
                ["Zeta"] = "Client"
            },
            saved.GroupParents);
        Assert.Equal(["Client", "Project"], saved.GroupOrder.Where(group => group is "Client" or "Project"));
    }

    /// <summary>
    ///     Test that a drop that would put a group inside itself or one of its own subgroups, onto itself,
    ///     onto a built-in section or an unknown group, or while the filter has text, is refused and
    ///     changes nothing.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropGroup_RefusesLoopsBuiltInSectionsAndFilter()
    {
        // Arrange: Client > Project > Deep
        var viewModel = CreateViewModel(
            parents: new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Project"] = "Client",
                ["Deep"] = "Project"
            },
            new Spec("d1", Group: "Deep"),
            new Spec("h1", Group: "Home"),
            new Spec("star", IsFavorite: true));
        var before = Describe(viewModel);

        // Act / Assert
        Assert.Multiple(
            () => Assert.False(viewModel.CanDropGroup("Client", new GroupDropTarget("Client", true))),
            () => Assert.False(viewModel.CanDropGroup("Client", new GroupDropTarget("Project", true, Inside: true))),
            () => Assert.False(viewModel.CanDropGroup("Client", new GroupDropTarget("Deep", false))),
            () => Assert.False(viewModel.CanDropGroup("Client", new GroupDropTarget("Project", true))),
            () => Assert.False(viewModel.CanDropGroup("Project", new GroupDropTarget("Deep", true, Inside: true))),
            () => Assert.False(viewModel.CanDropGroup("Home", new GroupDropTarget("Favorites", true))),
            () => Assert.False(viewModel.CanDropGroup("Home", new GroupDropTarget("Ungrouped", false, Inside: true))),
            () => Assert.False(viewModel.CanDropGroup("Favorites", new GroupDropTarget("Home", true))),
            () => Assert.False(viewModel.CanDropGroup("Home", new GroupDropTarget("Missing", true))),
            () => Assert.False(viewModel.DropGroup("Client", new GroupDropTarget("Deep", false, Inside: true))),
            () => Assert.True(viewModel.CanDropGroup("Home", new GroupDropTarget("Deep", true, Inside: true))));
        Assert.Equal(before, Describe(viewModel));

        // Act: with a filter nothing can be dropped
        viewModel.FilterText = "h1";
        Assert.False(viewModel.CanDropGroup("Home", new GroupDropTarget("Client", true)));
    }

    /// <summary>
    ///     Test that renaming a group keeps its place in the hierarchy, as a group and as a parent, and
    ///     its collapsed state and color.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RenameGroup_KeepsTheHierarchy()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir,
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Project"] = "Client" },
            new Spec("p1", Group: "Project"));
        Assert.True(viewModel.SetGroupColor("Client", "Teal"));

        // Act
        Assert.True(viewModel.RenameGroup("Client", "Customer"));
        Assert.True(viewModel.RenameGroup("project", "Phase"));

        // Assert
        Assert.Equal(["[Customer 1]", "[Phase 1]", "p1"], Describe(viewModel));
        Assert.Equal("Teal", viewModel.Settings.FindGroupColor("Customer"));
        await viewModel.FlushSettingsAsync();
        Assert.Equal(
            new Dictionary<string, string> { ["Customer"] = string.Empty, ["Phase"] = "Customer" },
            SettingsStore.Load(configDir).GroupParents);
    }

    /// <summary>
    ///     Test that removing a group keeps what is in it: its repos go to the group it was inside (or
    ///     become ungrouped), its subgroups go there too in the group's place, and the group and its
    ///     hierarchy entry are forgotten.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RemoveGroup_NestedGroups_KeepsRepoAndSubgroupsInTheParent()
    {
        // Arrange: Client holds c1 and the subgroups Alpha (a1) and Project (p1); Home comes after Client
        var configDir = CreateTempDirectory();
        var viewModel = CreateClientTree(configDir);

        // Act: remove the nested Project group
        Assert.True(viewModel.RemoveGroup("Project"));

        // Assert: p1 moved to Client, listed with Client's own repos in their stored order
        Assert.Equal(
            ["[Client 3]", "[Alpha 1]", "a1", "c1", "p1", "[Ungrouped 1]", "loose"],
            Describe(viewModel));
        Assert.Equal("Client", viewModel.RepoCards.Single(card => card.RepoName == "p1").GroupName);

        // Act: remove the top-level Client group; its subgroup and repos go up a level
        Assert.True(viewModel.RemoveGroup("Client"));

        // Assert: Alpha stays a (registered) group of its own, the repos are ungrouped
        Assert.Equal(["[Alpha 1]", "a1", "[Ungrouped 3]", "c1", "p1", "loose"], Describe(viewModel));
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);
        Assert.Equal(new Dictionary<string, string> { ["Alpha"] = string.Empty }, saved.GroupParents);
        Assert.DoesNotContain("Client", saved.GroupOrder);
    }

    /// <summary>
    ///     Test that a group that is only a favorite's group but is nested with other groups keeps the
    ///     chip and that the names of empty and nested groups are offered to repos' Move to group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GroupNames_IncludeEmptyAndNestedGroups()
    {
        // Arrange
        var viewModel = CreateViewModel(
            parents: new Dictionary<string, string> { ["Client"] = string.Empty, ["Empty"] = "Client" },
            new Spec("loose"));
        var card = viewModel.RepoCards.Single();

        // Act
        var names = card.GetExistingGroupNames();

        // Assert
        Assert.Equal(["Client", "Empty"], names);
        Assert.Equal(["Client", "Empty"], viewModel.GroupNames);
        Assert.True(viewModel.HasGroups);
        Assert.True(card.MoveToGroup("EMPTY"));
        Assert.Equal("Empty", card.GroupName);
    }

    /// <summary>
    ///     Creates the tree used by most tests: Client holds c1 and the subgroups Project (p1) and
    ///     Alpha (a1); one repo is in no group.
    /// </summary>
    /// <param name="configDir">The configuration directory, or <see langword="null"/> for a new one.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateClientTree(string? configDir = null) =>
        CreateViewModel(
            configDir ?? CreateTempDirectory(),
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Project"] = "Client",
                ["Alpha"] = "Client"
            },
            new Spec("c1", Group: "Client"),
            new Spec("p1", Group: "Project"),
            new Spec("a1", Group: "Alpha"),
            new Spec("loose"));

    /// <summary>
    ///     Describes the displayed items as text: a header as <c>[Name count]</c> (with
    ///     <c>collapsed</c> when its cards are hidden) and a card as its repo name.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <returns>One entry per displayed item, in order.</returns>
    private static List<string> Describe(MainWindowViewModel viewModel) => Describe(viewModel.DisplayedItems);

    /// <summary>
    ///     Describes some displayed items as text: a header as <c>[Name count]</c> (with
    ///     <c>collapsed</c> when its cards are hidden) and a card as its repo name.
    /// </summary>
    /// <param name="items">The items.</param>
    /// <returns>One entry per item, in order.</returns>
    private static List<string> Describe(IEnumerable<object> items) =>
        [.. items.Select(item => item switch
        {
            RepoGroupHeaderViewModel header => $"[{header.Name} {header.Count}{(header.IsCollapsed ? " collapsed" : string.Empty)}]",
            RepoCardViewModel card => card.RepoName,
            _ => throw new InvalidOperationException("Unexpected list item.")
        })];

    /// <summary>
    ///     Describes one repo to create for a test.
    /// </summary>
    /// <param name="Name">The repo's folder name (also its display name).</param>
    /// <param name="Group">The repo's group, or <see langword="null"/>.</param>
    /// <param name="IsFavorite">Whether the repo is a favorite.</param>
    private sealed record Spec(string Name, string? Group = null, bool IsFavorite = false);

    /// <summary>
    ///     Creates a view model over repos described by <paramref name="specs"/>, persisting to a new
    ///     configuration directory.
    /// </summary>
    /// <param name="parents">The stored hierarchy, or <see langword="null"/> for none.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(Dictionary<string, string>? parents, params Spec[] specs) =>
        CreateViewModel(CreateTempDirectory(), parents, specs);

    /// <summary>
    ///     Creates a view model over repos described by <paramref name="specs"/>.
    /// </summary>
    /// <param name="configDir">The configuration directory settings are persisted to.</param>
    /// <param name="parents">The stored hierarchy, or <see langword="null"/> for none.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(string configDir, Dictionary<string, string>? parents, params Spec[] specs)
    {
        var settings = new AppSettings
        {
            // The list is the user's own order, so it is not re-arranged by launch time
            HasUserRepoOrder = true,
            RecentRepos =
            [
                .. specs.Select(spec => new RecentRepo
                {
                    Path = CreateRepoDirectory(spec.Name),
                    GroupName = spec.Group,
                    IsFavorite = spec.IsFavorite
                })
            ],
            GroupParents = new Dictionary<string, string>(parents ?? new Dictionary<string, string>(), RepoGroupName.Comparer)
        };
        settings.RepairGroupSettings();
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);
        _viewModels.Add(viewModel);

        // The cards' repo state is read in the background at construction; these tests are about the
        // list, so they start once it has settled (and no git still runs in a repo folder when the
        // folders are removed). Waited for on a pool thread, so no synchronization context is needed.
        Task.Run(viewModel.WhenRepoStateSettledAsync).GetAwaiter().GetResult();
        return viewModel;
    }

    /// <summary>
    ///     Creates an empty repo folder with the given name under a unique parent, so the card's
    ///     display name is the name.
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
    ///     Creates a unique temporary directory that is removed when the test class is disposed.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "AgentControlGroupTree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No read may still hold a file in a repo folder when the folders are removed. Waited for on a
        // pool thread, so no synchronization context is needed.
        foreach (var viewModel in _viewModels)
        {
            Task.Run(viewModel.WhenRepoStateSettledAsync).GetAwaiter().GetResult();
            viewModel.Dispose();
        }

        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
