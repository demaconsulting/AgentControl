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
///     Unit tests of the two ways of deleting a group in <see cref="MainWindowViewModel"/>: deleting it but
///     keeping what is in it, and deleting it together with its repos and the groups inside it.
/// </summary>
/// <remarks>
///     Each test uses unique temporary repo and configuration directories. Runs in the
///     <c>RealProcess</c> collection because every card's repo state is read in the background (real git
///     subprocesses) from the moment the view model is built; every view model a test builds is tracked, and
///     its reads have finished (and it is disposed) before the folders are removed.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoGroupDeleteTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     The view models built by the running test, settled and disposed before the folders go.
    /// </summary>
    private readonly List<MainWindowViewModel> _viewModels = [];

    /// <summary>
    ///     Test that deleting a top-level group but keeping its contents leaves its repos, favorites
    ///     included, ungrouped where they were stored, and forgets the group's color, collapsed state and place in the
    ///     order - exactly what removing a group did before groups could be nested.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroup_TopLevel_LeavesReposUngroupedInPlaceAndForgetsTheGroup()
    {
        // Arrange
        var viewModel = CreateViewModel(
            parents: null,
            new Spec("w1", "Work"),
            new Spec("h1", "Home"),
            new Spec("w2", "Work", IsFavorite: true));
        Assert.True(viewModel.SetGroupColor("Work", "Teal"));
        viewModel.ToggleGroupCollapsed("Work");
        Assert.True(viewModel.MoveGroup("Work", RepoGroupMove.Up));

        // Act
        var removed = viewModel.RemoveGroup("work");

        // Assert
        Assert.True(removed);
        Assert.Equal(["w1", "h1", "w2"], viewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal([null, "Home", null], viewModel.RepoCards.Select(card => card.GroupName));
        Assert.Equal(["Home"], viewModel.GroupNames);
        Assert.Empty(viewModel.Settings.GroupColors);
        Assert.DoesNotContain("Work", viewModel.Settings.CollapsedGroups, RepoGroupName.Comparer);
        Assert.DoesNotContain("Work", viewModel.Settings.GroupOrder, RepoGroupName.Comparer);
    }

    /// <summary>
    ///     Test that deleting a nested group but keeping its contents moves its repos into its parent,
    ///     keeps their order, and puts its subgroups, in their own order, in the group's place in the order
    ///     of the parent's groups.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroup_Nested_KeepsRepoOrderAndSubgroupsTakeTheGroupsPlace()
    {
        // Arrange: Client holds the groups Zed, Mid (with Beta and Alpha, ordered Beta then Alpha) and Last
        var viewModel = CreateViewModel(
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Zed"] = "Client",
                ["Mid"] = "Client",
                ["Last"] = "Client",
                ["Beta"] = "Mid",
                ["Alpha"] = "Mid"
            },
            new Spec("m2", "Mid"),
            new Spec("z1", "Zed"),
            new Spec("m1", "Mid"),
            new Spec("b1", "Beta"),
            new Spec("a1", "Alpha"),
            new Spec("l1", "Last"));
        viewModel.Settings.GroupOrder = ["Zed", "Mid", "Last", "Beta", "Alpha"];

        // Act
        var removed = viewModel.RemoveGroup("Mid");

        // Assert: Mid's repos are in Client in their stored order; its subgroups are in Client where Mid was
        Assert.True(removed);
        Assert.Equal("Client", viewModel.RepoCards.Single(card => card.RepoName == "m2").GroupName);
        Assert.Equal(["m2", "z1", "m1"], viewModel.RepoCards.Take(3).Select(card => card.RepoName));
        Assert.Equal(
            ["Zed", "Beta", "Alpha", "Last"],
            viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>()
                .Where(header => header.ParentName == "Client").Select(header => header.Name));
        Assert.Equal("Client", viewModel.GetGroupParent("Beta"));
        Assert.Equal("Client", viewModel.GetGroupParent("Alpha"));
        Assert.DoesNotContain("Mid", viewModel.GroupNames);
    }

    /// <summary>
    ///     Test that deleting a group with its contents removes the group, every group inside it and every
    ///     repo in them - favorites included - and forgets the colors, collapsed state, order and hierarchy
    ///     of every deleted group, while a group next to it and its repos are untouched.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroupWithContents_RemovesSubtreeReposAndForgetsItsSettings()
    {
        // Arrange: Client holds c1, a favorite cf and the subgroups Project (p1) and Alpha (a1, which also has
        // the group Deep with d1); Home is next to Client
        var viewModel = CreateViewModel(
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Project"] = "Client",
                ["Alpha"] = "Client",
                ["Deep"] = "Alpha",
                ["Home"] = string.Empty
            },
            new Spec("c1", "Client"),
            new Spec("h1", "Home"),
            new Spec("cf", "Client", IsFavorite: true),
            new Spec("p1", "Project"),
            new Spec("a1", "Alpha"),
            new Spec("d1", "Deep"),
            new Spec("loose"));
        foreach (var group in new[] { "Client", "Alpha", "Deep", "Home" })
        {
            Assert.True(viewModel.SetGroupColor(group, "Teal"));
        }

        viewModel.ToggleGroupCollapsed("Alpha");
        viewModel.ToggleGroupCollapsed("Home");
        viewModel.Settings.GroupOrder = ["Client", "Home", "Project", "Alpha", "Deep"];

        // Act
        var removed = viewModel.RemoveGroupWithContents("CLIENT");

        // Assert: only Home, its repo and the ungrouped repo are left
        Assert.True(removed);
        Assert.Equal(["h1", "loose"], viewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(["h1", "loose"], viewModel.Settings.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        Assert.Equal(["Home"], viewModel.GroupNames);
        Assert.Equal(["Home"], viewModel.Settings.GroupColors.Keys.ToList());
        Assert.Equal(["Home"], viewModel.Settings.CollapsedGroups);
        Assert.Equal(["Home"], viewModel.Settings.GroupOrder);
        Assert.Equal(new Dictionary<string, string> { ["Home"] = string.Empty }, viewModel.Settings.GroupParents);
        Assert.Equal(["[Home 1 collapsed]", "[Ungrouped 1]", "loose"], DescribeItems(viewModel));
    }

    /// <summary>
    ///     Test that deleting a group with its contents is saved in the settings file, and that the repos'
    ///     folders are not touched: they and everything in them are still on disk.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RemoveGroupWithContents_SavesAndLeavesFoldersOnDisk()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir,
            parents: null,
            new Spec("w1", "Work"),
            new Spec("w2", "Work"),
            new Spec("loose"));
        var folders = viewModel.RepoCards.Select(card => card.RepoPath).ToList();
        await File.WriteAllTextAsync(Path.Combine(folders[0], "keep.txt"), "kept", TestContext.Current.CancellationToken);

        // Act
        Assert.True(viewModel.RemoveGroupWithContents("Work"));
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);

        // Assert
        Assert.Equal(["loose"], saved.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        Assert.All(folders, folder => Assert.True(Directory.Exists(folder)));
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(folders[0], "keep.txt"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that every card removed with its group is retired as a card removed on its own is: it no
    ///     longer raises its remove request.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroupWithContents_RetiresTheRemovedCards()
    {
        // Arrange
        var viewModel = CreateViewModel(parents: null, new Spec("w1", "Work"), new Spec("w2", "Work"));
        var cards = viewModel.RepoCards.ToList();
        var requests = 0;
        foreach (var card in cards)
        {
            card.RemoveRequested += (_, _) => requests++;
        }

        // Act
        Assert.True(viewModel.RemoveGroupWithContents("Work"));
        foreach (var card in cards)
        {
            card.RemoveCommand.Execute(null);
        }

        // Assert
        Assert.Equal(0, requests);
        Assert.Empty(viewModel.RepoCards);
        Assert.Empty(viewModel.DisplayedItems);
    }

    /// <summary>
    ///     Test that a group with no repo and no subgroup (a registered empty group) can be deleted either
    ///     way, that nothing else changes, and that its contents are counted as none.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroupWithContents_EmptyGroup_IsDeletedAndNothingElseChanges()
    {
        // Arrange: Empty is a group inside Client with nothing in it
        var viewModel = CreateViewModel(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Empty"] = "Client" },
            new Spec("c1", "Client"));

        // Act
        var contents = viewModel.GetGroupContents("Empty");
        var removed = viewModel.RemoveGroupWithContents("empty");

        // Assert
        Assert.True(contents.IsEmpty);
        Assert.True(removed);
        Assert.Equal(["Client"], viewModel.GroupNames);
        Assert.Equal(["c1"], viewModel.RepoCards.Select(card => card.RepoName));
    }

    /// <summary>
    ///     Test that the contents of a group are counted for the confirmation: its own repos, the repos in
    ///     the whole subtree (favorites included, and how many are favorites), and its subgroups directly and
    ///     in all.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GetGroupContents_CountsReposAndSubgroups()
    {
        // Arrange
        var viewModel = CreateViewModel(
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Project"] = "Client",
                ["Deep"] = "Project"
            },
            new Spec("c1", "Client"),
            new Spec("cf", "Client", IsFavorite: true),
            new Spec("p1", "Project"),
            new Spec("d1", "Deep", IsFavorite: true),
            new Spec("d2", "Deep"));

        // Act
        var client = viewModel.GetGroupContents("Client");
        var deep = viewModel.GetGroupContents("deep");
        var unknown = viewModel.GetGroupContents("No such group");

        // Assert
        Assert.Equal(new GroupContents(2, 1, 5, 2, 2), client);
        Assert.Equal(new GroupContents(2, 0, 2, 0, 1), deep);
        Assert.Equal(new GroupContents(0, 0, 0, 0, 0), unknown);
    }

    /// <summary>
    ///     Test that Favorites, Ungrouped and a group that does not exist cannot be deleted either way, and
    ///     that nothing changes.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroupWithContents_BuiltInOrUnknown_IsRefused()
    {
        // Arrange
        var viewModel = CreateViewModel(parents: null, new Spec("w1", "Work"), new Spec("loose"));

        // Act
        var results = new[]
        {
            viewModel.RemoveGroupWithContents("Favorites"),
            viewModel.RemoveGroupWithContents("Ungrouped"),
            viewModel.RemoveGroupWithContents("No such group"),
            viewModel.RemoveGroup("Favorites"),
            viewModel.RemoveGroup("Ungrouped")
        };

        // Assert
        Assert.All(results, Assert.False);
        Assert.Equal(["w1", "loose"], viewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(["Work"], viewModel.GroupNames);
    }

    /// <summary>
    ///     Test that a group header offers deleting with contents only while the group has a repo (a
    ///     favorite too) or a subgroup, and that its requests carry the group's name to the window.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GroupHeader_DeleteCommands_FollowContentsAndRaiseRequests()
    {
        // Arrange: Empty has nothing in it; Holder has only a favorite
        var viewModel = CreateViewModel(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Empty"] = "Client", ["Holder"] = "Client" },
            new Spec("c1", "Client"),
            new Spec("hf", "Holder", IsFavorite: true));
        var headers = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().ToDictionary(header => header.Name);
        var keep = new List<string>();
        var withContents = new List<string>();
        viewModel.GroupRemoveRequested += (_, name) => keep.Add(name);
        viewModel.GroupRemoveWithContentsRequested += (_, name) => withContents.Add(name);

        // Act
        headers["Client"].RemoveGroupWithContentsCommand.Execute(null);
        headers["Client"].RemoveGroupCommand.Execute(null);

        // Assert
        Assert.True(headers["Client"].HasContents);
        Assert.True(headers["Holder"].HasContents);
        Assert.False(headers["Empty"].HasContents);
        Assert.False(headers["Empty"].RemoveGroupWithContentsCommand.CanExecute(null));
        Assert.Equal("Delete group", headers["Empty"].RemoveGroupText);
        Assert.Equal("Delete group, keep contents", headers["Client"].RemoveGroupText);
        Assert.Equal(["Client"], withContents);
        Assert.Equal(["Client"], keep);
    }

    /// <summary>
    ///     Describes the displayed items as text: a header as <c>[Name count]</c> (with <c>collapsed</c> when
    ///     its cards are hidden) and a card as its repo name.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <returns>One entry per displayed item, in order.</returns>
    private static List<string> DescribeItems(MainWindowViewModel viewModel) =>
        [.. viewModel.DisplayedItems.Select(item => item switch
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
        // list, so they start once it has settled. Waited for on a pool thread, so no synchronization
        // context is needed.
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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlGroupDelete-" + Guid.NewGuid().ToString("N"));
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
