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
///     Unit tests of selecting several repo cards in <see cref="MainWindowViewModel"/>: the selection and its
///     order, dropping a selection in one change, and removing a selection.
/// </summary>
/// <remarks>
///     The list used by most tests shows, top to bottom: Favorites with <c>f1</c>; the group Client with its
///     subgroups Alpha (<c>a1</c>) and Project (<c>p1</c>, <c>p2</c>) and then its own <c>c1</c>; the group
///     Home with <c>h1</c>; and Ungrouped with <c>l1</c> and <c>l2</c>. Each test uses unique temporary repo and
///     configuration directories. Runs in the <c>RealProcess</c> collection because every card's repo state is
///     read in the background (real git subprocesses) from the moment the view model is built; every view
///     model a test builds is tracked, and its reads have finished (and it is disposed) before the folders are
///     removed.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoMultiSelectTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     The view models built by the running test, settled and disposed before the folders go.
    /// </summary>
    private readonly List<MainWindowViewModel> _viewModels = [];

    /// <summary>
    ///     Test that the selection is kept in the order the list shows the cards - favorites first, then the
    ///     groups as a tree, then the repos in no group - whatever order the cards are given in, and that a
    ///     card given twice counts once.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SetSelectedCards_KeepsVisualOrderAcrossNestedGroups()
    {
        // Arrange
        var viewModel = CreateTree();

        // Act
        viewModel.SetSelectedCards([Card(viewModel, "l2"), Card(viewModel, "c1"), Card(viewModel, "a1"), Card(viewModel, "c1"), Card(viewModel, "f1")]);

        // Assert
        Assert.Equal(["f1", "a1", "c1", "l2"], Names(viewModel.SelectedCards));
        Assert.Equal(4, viewModel.SelectedCount);
    }

    /// <summary>
    ///     Test that a card that is not shown cannot be selected, and that a selected card leaves the
    ///     selection when its group is collapsed or the filter hides it, while the others stay selected.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SelectedCards_CollapseAndFilterPruneTheSelection()
    {
        // Arrange
        var viewModel = CreateTree();
        viewModel.SetSelectedCards([Card(viewModel, "p1"), Card(viewModel, "h1"), Card(viewModel, "l1")]);
        Assert.Equal(["p1", "h1", "l1"], Names(viewModel.SelectedCards));

        // Act: collapse Client (and so Project), which hides p1
        viewModel.ToggleGroupCollapsed("Client");

        // Assert
        Assert.Equal(["h1", "l1"], Names(viewModel.SelectedCards));

        // Act: a hidden card cannot be selected
        viewModel.SetSelectedCards([Card(viewModel, "p2"), Card(viewModel, "h1")]);

        // Assert
        Assert.Equal(["h1"], Names(viewModel.SelectedCards));

        // Act: the filter hides what does not match
        viewModel.FilterText = "l1";

        // Assert
        Assert.Empty(viewModel.SelectedCards);
    }

    /// <summary>
    ///     Test that the cards of a card's own selection are what a drag started on it moves when the card is
    ///     selected, and the card alone when it is not.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GetDragCards_SelectedCardMovesTheSelectionUnselectedCardMovesAlone()
    {
        // Arrange
        var viewModel = CreateTree();
        viewModel.SetSelectedCards([Card(viewModel, "a1"), Card(viewModel, "h1"), Card(viewModel, "l1")]);

        // Act
        var fromSelected = viewModel.GetDragCards(Card(viewModel, "h1"));
        var fromOther = viewModel.GetDragCards(Card(viewModel, "l2"));

        // Assert
        Assert.Equal(["a1", "h1", "l1"], Names(fromSelected));
        Assert.Equal(["l2"], Names(fromOther));
    }

    /// <summary>
    ///     Test that dropping a selection on a group's header moves every card into the group, at its end, in
    ///     the order the list showed them - a favorite stops being a favorite and takes the group - in one
    ///     saved change, and that the cards stay selected.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_DropCards_OnNestedGroupHeader_MovesAllInVisualOrderAndSavesOnce()
    {
        // Arrange: a favorite, a repo of Home and an ungrouped one, dropped on the nested group Project
        var configDir = CreateTempDirectory();
        var viewModel = CreateTree(configDir);
        var cards = new[] { Card(viewModel, "l2"), Card(viewModel, "h1"), Card(viewModel, "f1") };
        viewModel.SetSelectedCards(cards);
        var project = Header(viewModel, "Project");

        // Act
        var dropped = viewModel.DropCards(viewModel.GetDragCards(cards[0]), RepoDropTarget.ForHeader(project));
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);

        // Assert: in the order they were shown (f1, h1, l2), after p1 and p2, all in Project, none a favorite
        Assert.True(dropped);
        Assert.Equal(["a1", "p1", "p2", "f1", "h1", "l2", "c1"], Names(viewModel.DisplayedRepoCards.Where(card => card.GroupName is "Alpha" or "Project" or "Client")));
        Assert.All(new[] { "f1", "h1", "l2" }, name => Assert.Equal("Project", Card(viewModel, name).GroupName));
        Assert.All(new[] { "f1", "h1", "l2" }, name => Assert.False(Card(viewModel, name).IsFavorite));
        Assert.Equal(["f1", "h1", "l2"], Names(viewModel.SelectedCards));
        Assert.Equal(
            Names(viewModel.RepoCards),
            saved.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        Assert.Equal("Project", saved.RecentRepos.Single(repo => Path.GetFileName(repo.Path) == "l2").GroupName);
        Assert.False(saved.RecentRepos.Single(repo => Path.GetFileName(repo.Path) == "f1").IsFavorite);
    }

    /// <summary>
    ///     Test that dropping a selection before a card of the same section puts the cards together before
    ///     it, in list order, and that a drop next to one of the dragged cards is refused and changes nothing.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCards_OnCard_InsertsTogetherAndRefusesAnchorInTheSet()
    {
        // Arrange: l1 and l2 are the only ungrouped repos; add l3 and drop l3 and l1 before l2
        var viewModel = CreateTree(extra: ["l3"]);
        var l1 = Card(viewModel, "l1");
        var l2 = Card(viewModel, "l2");
        var l3 = Card(viewModel, "l3");
        viewModel.SetSelectedCards([l1, l3]);
        var before = RepoDropTarget.ForCard(l2, insertBefore: true, RepoGroupKind.Ungrouped, "Ungrouped");
        var onSelf = RepoDropTarget.ForCard(l3, insertBefore: false, RepoGroupKind.Ungrouped, "Ungrouped");

        // Act
        var refused = viewModel.DropCards([l1, l3], onSelf);
        var unchanged = Names(viewModel.RepoCards);
        var accepted = viewModel.DropCards([l1, l3], before);

        // Assert
        Assert.False(refused);
        Assert.Equal(["l1", "l2", "l3"], unchanged.Where(name => name.StartsWith('l')));
        Assert.True(accepted);
        Assert.Equal(["l1", "l3", "l2"], Names(viewModel.RepoCards).Where(name => name.StartsWith('l')));
        Assert.False(viewModel.CanDropCards([l1, l3], onSelf));
    }

    /// <summary>
    ///     Test that dropping a selection on the Favorites header marks every card a favorite and keeps its
    ///     group, and dropping on the Ungrouped header un-favorites them and takes them out of their groups.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCards_OnFavoritesAndUngrouped_ChangeEveryCard()
    {
        // Arrange
        var viewModel = CreateTree();
        var c1 = Card(viewModel, "c1");
        var l1 = Card(viewModel, "l1");
        var f1 = Card(viewModel, "f1");
        var favorites = Header(viewModel, "Favorites");
        var ungrouped = Header(viewModel, "Ungrouped");

        // Act: Favorites for a group repo and an ungrouped one
        var toFavorites = viewModel.DropCards([c1, l1], RepoDropTarget.ForHeader(favorites));

        // Assert: both are favorites, c1 keeps its group
        Assert.True(toFavorites);
        Assert.True(c1.IsFavorite);
        Assert.True(l1.IsFavorite);
        Assert.Equal("Client", c1.GroupName);
        Assert.True(c1.ShowGroupChip);

        // Act: Ungrouped for two favorites
        var toUngrouped = viewModel.DropCards([c1, f1], RepoDropTarget.ForHeader(ungrouped));

        // Assert
        Assert.True(toUngrouped);
        Assert.False(c1.IsFavorite);
        Assert.False(f1.IsFavorite);
        Assert.Null(c1.GroupName);
        Assert.Null(f1.GroupName);
        Assert.Equal(["l2", "f1", "c1"], Names(viewModel.DisplayedRepoCards.Where(card => !card.HasGroup && !card.IsFavorite)));
    }

    /// <summary>
    ///     Test that a drop is refused as a whole, changing nothing, while the filter has text, for a place that
    ///     is not in the list, and in the flat list (no group anywhere) for a place in the other tier.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCards_FilterOrFlatListOtherTier_IsRefused()
    {
        // Arrange: a flat list with one favorite and two others
        var flat = CreateViewModel(parents: null, new Spec("fav", IsFavorite: true), new Spec("a"), new Spec("b"));
        var a = Card(flat, "a");
        var b = Card(flat, "b");
        var fav = Card(flat, "fav");

        // Assert: reordering within the tier works, a move to the other tier is refused
        Assert.True(flat.CanDropCards([a], RepoDropTarget.ForCard(b, insertBefore: false, RepoGroupKind.Ungrouped, "Ungrouped")));
        Assert.False(flat.CanDropCards([a], RepoDropTarget.ForCard(fav, insertBefore: false, RepoGroupKind.Favorites, "Favorites")));
        Assert.False(flat.DropCards([a, b], RepoDropTarget.ForCard(fav, insertBefore: true, RepoGroupKind.Favorites, "Favorites")));
        Assert.Equal(["fav", "a", "b"], Names(flat.RepoCards));

        // Act: with filter text nothing can be dropped
        var viewModel = CreateTree();
        viewModel.FilterText = "l";
        var refused = viewModel.DropCards(
            [Card(viewModel, "l1"), Card(viewModel, "l2")],
            RepoDropTarget.ForCard(Card(viewModel, "l1"), insertBefore: true, RepoGroupKind.Ungrouped, "Ungrouped"));

        // Assert
        Assert.False(refused);
        Assert.False(viewModel.CanDropCards([], RepoDropTarget.ForHeader(Header(viewModel, "Ungrouped"))));
    }

    /// <summary>
    ///     Test that removing several cards takes them - and only them - out of the list and the saved
    ///     settings in one change, stops the cards, clears them from the selection and leaves the repo
    ///     folders on disk.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RemoveRepos_RemovesOnlyTheGivenCardsAndSaves()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateTree(configDir);
        var removed = new[] { Card(viewModel, "a1"), Card(viewModel, "f1"), Card(viewModel, "l2") };
        var folders = removed.Select(card => card.RepoPath).ToList();
        var requests = 0;
        foreach (var card in removed)
        {
            card.RemoveRequested += (_, _) => requests++;
        }

        viewModel.SetSelectedCards([.. removed, Card(viewModel, "h1")]);

        // Act
        var count = viewModel.RemoveRepos([.. removed, removed[0]]);
        foreach (var card in removed)
        {
            card.RemoveCommand.Execute(null);
        }

        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(["l1", "h1", "c1", "p1", "p2"], Names(viewModel.RepoCards));
        Assert.DoesNotContain(removed, card => viewModel.RepoCards.Contains(card));
        Assert.Equal(Names(viewModel.RepoCards), saved.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        Assert.Equal(["h1"], Names(viewModel.SelectedCards));
        Assert.Equal(0, requests);
        Assert.All(folders, folder => Assert.True(Directory.Exists(folder)));
        Assert.Equal(0, viewModel.RemoveRepos([]));
    }

    /// <summary>
    ///     Test that a card's "Remove from list" text and tooltip say how many repos they remove while the
    ///     card is one of several selected cards, and are the plain text for a card that is alone or not
    ///     selected.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RemoveMenuText_FollowsTheSelectionSize()
    {
        // Arrange
        var viewModel = CreateTree();
        var a1 = Card(viewModel, "a1");
        var h1 = Card(viewModel, "h1");
        var l1 = Card(viewModel, "l1");
        var changed = new List<string?>();
        a1.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act: three selected
        viewModel.SetSelectedCards([a1, h1, l1]);

        // Assert
        Assert.Equal("Remove 3 selected repos from list", a1.RemoveMenuText);
        Assert.Equal("Remove 3 selected repos from list", l1.RemoveMenuText);
        Assert.Equal("Remove from list", Card(viewModel, "l2").RemoveMenuText);
        Assert.Contains("3 selected repos", a1.RemoveMenuTooltip);
        Assert.Contains(nameof(RepoCardViewModel.RemoveMenuText), changed);

        // Act: one selected
        viewModel.SetSelectedCards([a1]);

        // Assert
        Assert.Equal("Remove from list", a1.RemoveMenuText);
        Assert.Equal("Remove from list", h1.RemoveMenuText);
    }

    /// <summary>
    ///     Test that moving several cards to a group puts every one in it - a favorite stays a favorite and no
    ///     stored position changes - under the group's own spelling for a name in another letter case, in one
    ///     saved change, that the cards that are still shown stay selected, and that a null name takes them
    ///     out of their groups.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_MoveCardsToGroup_MovesAllSelectedKeepsFavoritesAndOrderAndSavesOnce()
    {
        // Arrange: a favorite of Home, an ungrouped repo and a repo of Home go to Project
        var configDir = CreateTempDirectory();
        var viewModel = CreateTree(configDir);
        var cards = new[] { Card(viewModel, "l2"), Card(viewModel, "f1"), Card(viewModel, "h1") };
        viewModel.SetSelectedCards(cards);
        var storedOrder = Names(viewModel.RepoCards);

        // Act
        var moved = viewModel.MoveCardsToGroup(viewModel.GetDragCards(cards[0]), "project");
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);

        // Assert: all in Project, f1 still a favorite, the stored order untouched, still selected
        Assert.True(moved);
        Assert.All(new[] { "l2", "f1", "h1" }, name => Assert.Equal("Project", Card(viewModel, name).GroupName));
        Assert.True(Card(viewModel, "f1").IsFavorite);
        Assert.Equal(storedOrder, Names(viewModel.RepoCards));
        Assert.Equal(["f1", "h1", "l2"], Names(viewModel.SelectedCards));
        Assert.Equal(storedOrder, saved.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        Assert.All(
            saved.RecentRepos.Where(repo => Path.GetFileName(repo.Path) is "l2" or "f1" or "h1"),
            repo => Assert.Equal("Project", repo.GroupName));
        Assert.True(saved.RecentRepos.Single(repo => Path.GetFileName(repo.Path) == "f1").IsFavorite);
        Assert.Equal(
            ["h1", "p1", "p2", "l2"],
            Names(viewModel.DisplayedRepoCards.Where(card => card.GroupName == "Project" && !card.IsFavorite)));

        // Act: taking them out of their groups
        Assert.True(viewModel.MoveCardsToGroup(cards, null));

        // Assert
        Assert.All(cards, card => Assert.Null(card.GroupName));
        Assert.True(Card(viewModel, "f1").IsFavorite);

        // Act: a new name makes a new group, the same group again changes nothing
        Assert.True(viewModel.MoveCardsToGroup([Card(viewModel, "l1")], "  Brand   New "));
        Assert.True(viewModel.MoveCardsToGroup([Card(viewModel, "l1")], "brand new"));

        // Assert
        Assert.Equal("Brand New", Card(viewModel, "l1").GroupName);
        Assert.Contains("Brand New", viewModel.GroupNames);
    }

    /// <summary>
    ///     Test that a name that is not a valid group name (a built-in section's name or one that is too
    ///     long), or no card in the list, changes nothing and returns <see langword="false"/>.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveCardsToGroup_InvalidNameChangesNothing()
    {
        // Arrange
        var viewModel = CreateTree();
        var cards = new[] { Card(viewModel, "l1"), Card(viewModel, "l2") };
        var stray = CreateViewModel(parents: null, new Spec("other")).RepoCards.Single();

        // Act / Assert
        Assert.Multiple(
            () => Assert.False(viewModel.MoveCardsToGroup(cards, "Favorites")),
            () => Assert.False(viewModel.MoveCardsToGroup(cards, "ungrouped")),
            () => Assert.False(viewModel.MoveCardsToGroup(cards, new string('x', 41))),
            () => Assert.False(viewModel.MoveCardsToGroup([], "Client")),
            () => Assert.False(viewModel.MoveCardsToGroup([stray], "Client")));
        Assert.All(cards, card => Assert.Null(card.GroupName));
        Assert.Throws<ArgumentNullException>(() => viewModel.MoveCardsToGroup(null!, "Client"));
    }

    /// <summary>
    ///     Test that a card's "Move to group" text and tooltip say how many repos they move while the card is
    ///     one of several selected cards, and are the plain text for a card that is alone or not selected.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_MoveToGroupMenuText_FollowsTheSelectionSize()
    {
        // Arrange
        var viewModel = CreateTree();
        var a1 = Card(viewModel, "a1");
        var h1 = Card(viewModel, "h1");
        var l1 = Card(viewModel, "l1");
        var changed = new List<string?>();
        a1.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act: three selected
        viewModel.SetSelectedCards([a1, h1, l1]);

        // Assert
        Assert.Equal("Move 3 selected repos to group", a1.MoveToGroupMenuText);
        Assert.Equal("Move 3 selected repos to group", l1.MoveToGroupMenuText);
        Assert.Equal("Move to group", Card(viewModel, "l2").MoveToGroupMenuText);
        Assert.Contains("3 selected repos", a1.MoveToGroupMenuTooltip);
        Assert.DoesNotContain("selected", Card(viewModel, "l2").MoveToGroupMenuTooltip);
        Assert.Contains(nameof(RepoCardViewModel.MoveToGroupMenuText), changed);
        Assert.Contains(nameof(RepoCardViewModel.MoveToGroupMenuTooltip), changed);

        // Act: one selected
        viewModel.SetSelectedCards([a1]);

        // Assert
        Assert.Equal("Move to group", a1.MoveToGroupMenuText);
        Assert.Equal("Move to group", h1.MoveToGroupMenuText);
    }

    /// <summary>
    ///     Gets a card by the folder name of its repo.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="name">The repo's folder name.</param>
    /// <returns>The card.</returns>
    private static RepoCardViewModel Card(MainWindowViewModel viewModel, string name) =>
        viewModel.RepoCards.Single(card => card.RepoName == name);

    /// <summary>
    ///     Gets a section's header.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="name">The section's name.</param>
    /// <returns>The header.</returns>
    private static RepoGroupHeaderViewModel Header(MainWindowViewModel viewModel, string name) =>
        viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(header => header.Name == name);

    /// <summary>
    ///     Lists the names of cards.
    /// </summary>
    /// <param name="cards">The cards.</param>
    /// <returns>The repos' folder names, in order.</returns>
    private static List<string> Names(IEnumerable<RepoCardViewModel> cards) => [.. cards.Select(card => card.RepoName)];

    /// <summary>
    ///     Creates the list described in the class remarks: stored order l1, h1, c1, p1, a1, f1, p2, l2 (plus
    ///     <paramref name="extra"/> ungrouped repos).
    /// </summary>
    /// <param name="configDir">The configuration directory, or <see langword="null"/> for a new one.</param>
    /// <param name="extra">More ungrouped repos, stored last.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateTree(string? configDir = null, string[]? extra = null) =>
        CreateViewModel(
            configDir ?? CreateTempDirectory(),
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Alpha"] = "Client", ["Project"] = "Client" },
            [
                new Spec("l1"),
                new Spec("h1", "Home"),
                new Spec("c1", "Client"),
                new Spec("p1", "Project"),
                new Spec("a1", "Alpha"),
                new Spec("f1", "Home", IsFavorite: true),
                new Spec("p2", "Project"),
                new Spec("l2"),
                .. (extra ?? []).Select(name => new Spec(name))
            ]);

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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlMultiSelect-" + Guid.NewGuid().ToString("N"));
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
