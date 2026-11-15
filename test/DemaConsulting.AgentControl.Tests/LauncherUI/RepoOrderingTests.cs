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
using DemaConsulting.AgentControl.Tests.AgentToolLauncher;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests of the user-controlled repo order of <see cref="MainWindowViewModel"/>: the order
///     is stable (a launch, a refresh or a settings change never moves a card), a new repo goes to
///     the bottom of its section, the Move operations and the drop operations reorder within and
///     between sections, nothing reorders while the filter has text, the order is saved in the
///     user's settings, and an older settings file is arranged once the way older versions
///     displayed it.
/// </summary>
/// <remarks>
///     Each test uses unique temporary repo and configuration directories, and a fake process starter
///     so a launch starts nothing. A failing save is produced by making <c>settings.json</c> a
///     directory (see <c>Fixture.BreakSaving</c>). Runs in the <c>RealProcess</c> collection because
///     every card's eager refresh spawns real git subprocesses.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoOrderingTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that launching a repo records the launch but leaves every card where it was, in the
    ///     collection, in the displayed list and in the saved order.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Launch_DoesNotReorderCards()
    {
        // Arrange: three repos in the user's order, the last one about to be launched
        var fixture = CreateFixture(
            new AppSettings { AgentTool = AgentToolKind.Custom, CustomAgentCommand = "my-agent", ShellPreference = "pwsh" },
            Spec("a"), Spec("b"), Spec("c"));
        var launched = fixture.Card("c");

        // Act
        await launched.LaunchCommand.ExecuteAsync();

        // Assert: it was launched and recorded, and nothing moved
        Assert.Single(fixture.Starter.Started);
        Assert.NotNull(launched.LastLaunchedUtc);
        Assert.Equal(["a", "b", "c"], fixture.Names());
        Assert.Equal(["a", "b", "c"], fixture.Displayed());
        Assert.Equal(["a", "b", "c"], fixture.StoredNames());
        Assert.Equal(["a", "b", "c"], fixture.SavedNames());
    }

    /// <summary>
    ///     Test that refreshing the cards, applying settings and the background upgrade check leave the
    ///     order as it was, and that applying settings keeps the order in the saved file.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RefreshAndApplySettings_DoNotReorderCards()
    {
        // Arrange: a user order that differs from the order in which the repos were launched
        var now = DateTimeOffset.UtcNow;
        var fixture = CreateFixture(
            Spec("a", lastLaunched: now.AddDays(-3)), Spec("b", lastLaunched: now), Spec("c", lastLaunched: now.AddDays(-1)));

        // Act
        foreach (var card in fixture.ViewModel.RepoCards)
        {
            card.RefreshCheap();
        }

        fixture.ViewModel.ApplySettings(new AppSettings { PackageSourcePath = fixture.CreateDirectory("source") });
        await fixture.ViewModel.BeginUpgradeRefresh();

        // Assert
        Assert.Equal(["a", "b", "c"], fixture.Names());
        Assert.Equal(["a", "b", "c"], fixture.Displayed());
        Assert.Equal(["a", "b", "c"], fixture.SavedNames());
    }

    /// <summary>
    ///     Test that a repo added with the top-bar add goes to the end of the list, also below the
    ///     other repos of its tier in the flat list, and is saved there.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_NewRepoGoesToTheEndOfTheList()
    {
        // Arrange: two repos, the second a favorite
        var fixture = CreateFixture(Spec("a"), Spec("b", favorite: true));

        // Act
        Assert.True(fixture.ViewModel.AddRepo(fixture.CreateDirectory("c")));

        // Assert: last in the list and in the file; in the flat list favorites stay first
        Assert.Equal(["a", "b", "c"], fixture.Names());
        Assert.Equal(["a", "b", "c"], fixture.StoredNames());
        Assert.Equal(["a", "b", "c"], fixture.SavedNames());
        Assert.Equal(["b", "a", "c"], fixture.Displayed());
    }

    /// <summary>
    ///     Test that a repo added straight into a group goes to the bottom of that group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepoToGroup_NewRepoGoesToTheBottomOfTheGroup()
    {
        // Arrange: a group of two, and an ungrouped repo that is listed last
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("w2", "Work"), Spec("loose"));

        // Act
        var result = fixture.ViewModel.AddRepoToGroup(fixture.CreateDirectory("fresh"), "Work");

        // Assert
        Assert.Equal(AddRepoResult.Added, result);
        Assert.Equal(["[Work 3]", "w1", "w2", "fresh", "[Ungrouped 1]", "loose"], fixture.Describe());
    }

    /// <summary>
    ///     Test that a repo that is already listed and is added to a group from its header goes to the
    ///     bottom of that group, wherever it was in the list.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepoToGroup_RepoAlreadyListed_MovesToTheBottomOfTheGroup()
    {
        // Arrange: the ungrouped repo is listed between the two members of the group
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("loose"), Spec("w2", "Work"));

        // Act
        var result = fixture.ViewModel.AddRepoToGroup(fixture.Card("loose").RepoPath, "Work");

        // Assert: in the group, last, and the order is saved
        Assert.Equal(AddRepoResult.MovedExisting, result);
        Assert.Equal(["[Work 3]", "w1", "w2", "loose"], fixture.Describe());
        Assert.Equal(["w1", "w2", "loose"], fixture.SavedNames());
    }

    /// <summary>
    ///     Test that settings without a stored order are arranged once as favorites first, then the
    ///     most recently launched (never launched last), the way older versions displayed the list.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_NoStoredOrder_SeedsFavoritesFirstThenMostRecentlyLaunched()
    {
        // Arrange: settings as an older version (or a first run) leaves them
        var now = DateTimeOffset.UtcNow;
        var settings = new AppSettings
        {
            RecentRepos =
            [
                new RecentRepo { Path = CreateDirectory("never") },
                new RecentRepo { Path = CreateDirectory("old"), LastLaunchedUtc = now.AddDays(-5) },
                new RecentRepo { Path = CreateDirectory("fav"), IsFavorite = true, LastLaunchedUtc = now.AddDays(-9) },
                new RecentRepo { Path = CreateDirectory("new"), LastLaunchedUtc = now }
            ]
        };

        // Act
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateDirectory("config"));

        // Assert: the arrangement the user used to see, now the stored order
        Assert.True(settings.HasUserRepoOrder);
        Assert.Equal(
            ["fav", "new", "old", "never"], viewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(
            ["fav", "new", "old", "never"], viewModel.DisplayedRepoCards.Select(card => card.RepoName));
        Assert.Equal(
            ["fav", "new", "old", "never"], settings.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
    }

    /// <summary>
    ///     Test that the seeded order is written by the next save of any kind, so it is not seeded a
    ///     second time (and cannot change with later launches).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_SeededOrder_IsWrittenByTheNextSave()
    {
        // Arrange: settings without a stored order, whose launch times put "b" first
        var now = DateTimeOffset.UtcNow;
        var configDirectory = CreateDirectory("config");
        var settings = new AppSettings
        {
            RecentRepos =
            [
                new RecentRepo { Path = CreateDirectory("a"), LastLaunchedUtc = now.AddDays(-2) },
                new RecentRepo { Path = CreateDirectory("b"), LastLaunchedUtc = now }
            ]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDirectory);
        Assert.False(File.Exists(Path.Combine(configDirectory, "settings.json")));

        // Act: an unrelated change saves
        Assert.True(viewModel.RepoCards.Single(card => card.RepoName == "a").SetCardColor("Teal"));

        // Assert: the file holds the seeded order and the marker
        var saved = SettingsStore.Load(configDirectory);
        Assert.True(saved.HasUserRepoOrder);
        Assert.Equal(["b", "a"], saved.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
    }

    /// <summary>
    ///     Test that the launch times play no part in what is displayed: in the flat list (favorites
    ///     first) and in the sections the repos stay in the stored order, also after a launch.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_DisplayedRepoCards_LaunchTimesDoNotAffectStoredOrder()
    {
        // Arrange: launch times in the opposite order of the stored one, and no group yet
        var now = DateTimeOffset.UtcNow;
        var fixture = CreateFixture(
            new AppSettings { AgentTool = AgentToolKind.Custom, CustomAgentCommand = "my-agent", ShellPreference = "pwsh" },
            Spec("a", lastLaunched: now.AddDays(-9)), Spec("b", lastLaunched: now.AddDays(-5)),
            Spec("c", lastLaunched: now));

        // Act / Assert: the flat list, then after a launch
        Assert.Equal(["a", "b", "c"], fixture.Displayed());
        await fixture.Card("a").LaunchCommand.ExecuteAsync();
        Assert.Equal(["a", "b", "c"], fixture.Displayed());

        // Act / Assert: the sections, once a repo is in a group
        Assert.True(fixture.Card("c").MoveToGroup("Work"));
        Assert.True(fixture.Card("a").MoveToGroup("Work"));
        Assert.Equal(["[Work 2]", "a", "c", "[Ungrouped 1]", "b"], fixture.Describe());
    }

    /// <summary>
    ///     Test that a stored user order is kept whatever the launch times say.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_StoredOrder_IsKeptWhateverTheLaunchTimes()
    {
        // Arrange: a stored order that is the opposite of the launch order
        var now = DateTimeOffset.UtcNow;
        var fixture = CreateFixture(
            Spec("first", lastLaunched: now.AddDays(-9)), Spec("second", lastLaunched: now.AddDays(-5)),
            Spec("third", lastLaunched: now));

        // Assert
        Assert.Equal(["first", "second", "third"], fixture.Names());
        Assert.Equal(["first", "second", "third"], fixture.Displayed());
    }

    /// <summary>
    ///     Test that Move to top, up, down and bottom each put the card where the name says, within its
    ///     section, and that the new order is saved.
    /// </summary>
    /// <param name="moveName">The move.</param>
    /// <param name="repoName">The repo that is moved.</param>
    /// <param name="expected">The expected order of all four repos.</param>
    [Theory]
    [InlineData("Top", "c", "c,a,b,d")]
    [InlineData("Up", "c", "a,c,b,d")]
    [InlineData("Down", "b", "a,c,b,d")]
    [InlineData("Bottom", "b", "a,c,d,b")]
    public void MainWindowViewModel_MoveCard_TopUpDownBottom_ReordersWithinTheSection(
        string moveName, string repoName, string expected)
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"), Spec("d"));

        // Act
        var moved = fixture.ViewModel.MoveCard(fixture.Card(repoName), Enum.Parse<RepoMove>(moveName));

        // Assert
        Assert.True(moved);
        Assert.Equal(expected.Split(','), fixture.Names());
        Assert.Equal(expected.Split(','), fixture.Displayed());
        Assert.Equal(expected.Split(','), fixture.StoredNames());
        Assert.Equal(expected.Split(','), fixture.SavedNames());
    }

    /// <summary>
    ///     Test that the Reorder submenu is available when at least one move applies and otherwise names
    ///     why: the filter has text, or the repo is the only one of its section.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GetReorderDisabledReason_NamesTheReason()
    {
        // Arrange: a group of one repo and two ungrouped repos
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("u1"), Spec("u2"));

        // Act / Assert: a repo with a neighbor can be reordered, also at either end of its section
        Assert.Null(fixture.ViewModel.GetReorderDisabledReason(fixture.Card("u1")));
        Assert.Null(fixture.ViewModel.GetReorderDisabledReason(fixture.Card("u2")));

        // Act / Assert: the only repo of its section has nowhere to go
        Assert.Contains("only", fixture.ViewModel.GetReorderDisabledReason(fixture.Card("w1")), StringComparison.Ordinal);

        // Act / Assert: the filter has text
        fixture.ViewModel.FilterText = "u";
        Assert.Equal(
            MainWindowViewModel.ReorderDisabledWhileFilteringMessage,
            fixture.ViewModel.GetReorderDisabledReason(fixture.Card("u1")));
    }

    /// <summary>
    ///     Test that a move that leads nowhere (the first repo up or to the top, the last one down or to
    ///     the bottom) is refused, changes nothing and saves nothing.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveCard_AtBoundary_IsRefusedAndChangesNothing()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));

        // Act / Assert
        Assert.False(fixture.ViewModel.MoveCard(fixture.Card("a"), RepoMove.Top));
        Assert.False(fixture.ViewModel.MoveCard(fixture.Card("a"), RepoMove.Up));
        Assert.False(fixture.ViewModel.MoveCard(fixture.Card("c"), RepoMove.Down));
        Assert.False(fixture.ViewModel.MoveCard(fixture.Card("c"), RepoMove.Bottom));
        Assert.Equal(["a", "b", "c"], fixture.Names());
        Assert.Equal(["a", "b", "c"], fixture.StoredNames());
        Assert.False(File.Exists(Path.Combine(fixture.ConfigDirectory, "settings.json")));
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    ///     Test that when the sections are interleaved in the stored list, a move changes only the
    ///     moved card's place relative to its own section and the other section keeps its order.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveCard_SectionsInterleavedInTheList_KeepsOtherSectionsOrder()
    {
        // Arrange: Work and Home repos alternate in the stored list
        var fixture = CreateFixture(
            Spec("w1", "Work"), Spec("h1", "Home"), Spec("w2", "Work"), Spec("h2", "Home"), Spec("w3", "Work"));

        // Act: the last Work repo to the top of Work
        Assert.True(fixture.ViewModel.MoveCard(fixture.Card("w3"), RepoMove.Top));

        // Assert: Work is w3, w1, w2; Home is unchanged; the card sits right before the old top of Work
        Assert.Equal(["[Home 2]", "h1", "h2", "[Work 3]", "w3", "w1", "w2"], fixture.Describe());
        Assert.Equal(["w3", "w1", "h1", "w2", "h2"], fixture.StoredNames());

        // Act: the new top one place down
        Assert.True(fixture.ViewModel.MoveCard(fixture.Card("w1"), RepoMove.Down));

        // Assert: after w2, the Home repos still in their order
        Assert.Equal(["[Home 2]", "h1", "h2", "[Work 3]", "w3", "w2", "w1"], fixture.Describe());
        Assert.Equal(["w3", "h1", "w2", "w1", "h2"], fixture.StoredNames());
    }

    /// <summary>
    ///     Test that a move whose save fails changes nothing, tells the user once, and works once saving
    ///     works again (the trial change and the real one agree).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveCard_SaveFails_ChangesNothingAndReports()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));
        fixture.BreakSaving();

        // Act
        var moved = fixture.ViewModel.MoveCard(fixture.Card("c"), RepoMove.Top);

        // Assert
        Assert.False(moved);
        Assert.Equal(["a", "b", "c"], fixture.Names());
        Assert.Equal(["a", "b", "c"], fixture.Displayed());
        Assert.Equal(["a", "b", "c"], fixture.StoredNames());
        Assert.Single(fixture.Failures);
        Assert.Contains("not applied", fixture.Failures[0], StringComparison.Ordinal);

        // Act: saving works again
        fixture.RestoreSaving();
        Assert.True(fixture.ViewModel.MoveCard(fixture.Card("c"), RepoMove.Top));

        // Assert
        Assert.Equal(["c", "a", "b"], fixture.SavedNames());
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that availability follows the place in the section: the first repo cannot go to the top or
    ///     up, the last one cannot go down or to the bottom, a middle one can go anywhere.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CanMoveCard_FirstMiddleAndLast_ReportsEachItem()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));
        var viewModel = fixture.ViewModel;

        // Act / Assert: top, up, down, bottom
        Assert.Equal(
            [false, false, true, true],
            Availability(viewModel, fixture.Card("a")));
        Assert.Equal(
            [true, true, true, true],
            Availability(viewModel, fixture.Card("b")));
        Assert.Equal(
            [true, true, false, false],
            Availability(viewModel, fixture.Card("c")));
    }

    /// <summary>
    ///     Test that the reason for a disabled move says why: the filter, or the place in the section;
    ///     and that an allowed move has no reason.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GetMoveDisabledReason_NamesTheReason()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));
        var viewModel = fixture.ViewModel;

        // Act / Assert: the place in the section
        Assert.Contains("first", viewModel.GetMoveDisabledReason(fixture.Card("a"), RepoMove.Top), StringComparison.Ordinal);
        Assert.Contains("last", viewModel.GetMoveDisabledReason(fixture.Card("c"), RepoMove.Bottom), StringComparison.Ordinal);
        Assert.Null(viewModel.GetMoveDisabledReason(fixture.Card("b"), RepoMove.Up));

        // Act / Assert: the filter wins
        viewModel.FilterText = "b";
        Assert.Equal(
            MainWindowViewModel.ReorderDisabledWhileFilteringMessage,
            viewModel.GetMoveDisabledReason(fixture.Card("b"), RepoMove.Up));
    }

    /// <summary>
    ///     Test that dropping a card before or after another card of the same section puts it there and
    ///     saves the order.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_BeforeAndAfterAnAnchor_ReordersWithinTheSection()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"), Spec("d"));

        // Act: "a" after "c"
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("a"), fixture.After("c")));

        // Assert
        Assert.Equal(["b", "c", "a", "d"], fixture.Names());
        Assert.Equal(["b", "c", "a", "d"], fixture.SavedNames());

        // Act: "d" before "b"
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("d"), fixture.Before("b")));

        // Assert
        Assert.Equal(["d", "b", "c", "a"], fixture.Names());
        Assert.Equal(["d", "b", "c", "a"], fixture.Displayed());
        Assert.Equal(["d", "b", "c", "a"], fixture.SavedNames());
    }

    /// <summary>
    ///     Test that dropping a card next to itself, or where it already is, changes nothing and saves
    ///     nothing.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_OnItself_ChangesNothing()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));

        // Act
        var onItself = fixture.ViewModel.DropCard(fixture.Card("b"), fixture.Before("b"));
        var rightAfterPrevious = fixture.ViewModel.DropCard(fixture.Card("b"), fixture.After("a"));

        // Assert: accepted, nothing moved, nothing written
        Assert.True(onItself);
        Assert.True(rightAfterPrevious);
        Assert.Equal(["a", "b", "c"], fixture.Names());
        Assert.False(File.Exists(Path.Combine(fixture.ConfigDirectory, "settings.json")));
    }

    /// <summary>
    ///     Test that in the flat list (no groups) a card cannot be dropped into the other tier - no
    ///     favorite change happens - while reordering within its tier works.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_FlatListOtherTier_IsRefused()
    {
        // Arrange: a favorite and two other repos, no group anywhere
        var fixture = CreateFixture(Spec("fav", favorite: true), Spec("a"), Spec("b"));
        Assert.False(fixture.ViewModel.HasGroups);

        // Act / Assert: into the favorites tier, and out of it
        Assert.False(fixture.ViewModel.CanDrop(fixture.Card("a"), fixture.After("fav")));
        Assert.False(fixture.ViewModel.DropCard(fixture.Card("a"), fixture.After("fav")));
        Assert.False(fixture.ViewModel.DropCard(fixture.Card("fav"), fixture.After("b")));
        Assert.False(fixture.Card("a").IsFavorite);
        Assert.True(fixture.Card("fav").IsFavorite);
        Assert.Equal(["fav", "a", "b"], fixture.Names());

        // Act / Assert: within the tier it works
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("a"), fixture.After("b")));
        Assert.Equal(["fav", "b", "a"], fixture.Displayed());
    }

    /// <summary>
    ///     Test that a drop whose anchor is no longer in the list, or no longer in the target section,
    ///     is refused instead of guessing a place.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_StaleAnchor_IsRefused()
    {
        // Arrange: two groups
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("w2", "Work"), Spec("h1", "Home"), Spec("h2", "Home"));
        var removed = fixture.Card("h2");
        var stale = RepoDropTarget.ForCard(removed, insertBefore: true, RepoGroupKind.Group, "Home");
        fixture.ViewModel.RemoveRepo(removed);
        var wrongSection = RepoDropTarget.ForCard(fixture.Card("w1"), insertBefore: true, RepoGroupKind.Group, "Home");

        // Act / Assert
        Assert.False(fixture.ViewModel.DropCard(fixture.Card("w2"), stale));
        Assert.False(fixture.ViewModel.DropCard(fixture.Card("w2"), wrongSection));
        Assert.Equal(["Work", "Work", "Home"], fixture.Groups());
    }

    /// <summary>
    ///     Test that dropping a card between two cards of another group puts it in that group at that
    ///     place, through the group change of the card, and saves both.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_IntoAnotherGroup_SetsTheGroupAtTheDropPosition()
    {
        // Arrange
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("w2", "Work"), Spec("h1", "Home"), Spec("h2", "Home"));

        // Act: "h2" before "w2"
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("h2"), fixture.Before("w2")));

        // Assert
        Assert.Equal(["[Home 1]", "h1", "[Work 3]", "w1", "h2", "w2"], fixture.Describe());
        Assert.Equal("Work", fixture.Card("h2").GroupName);
        var saved = SettingsStore.Load(fixture.ConfigDirectory).RecentRepos;
        Assert.Equal("Work", saved.Single(repo => Path.GetFileName(repo.Path) == "h2").GroupName);
        Assert.Equal(["w1", "h2", "w2"], saved.Where(repo => repo.GroupName == "Work").Select(repo => Path.GetFileName(repo.Path)));
    }

    /// <summary>
    ///     Test that dropping a grouped card among the ungrouped ones takes it out of its group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_IntoUngrouped_RemovesTheGroup()
    {
        // Arrange
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("w2", "Work"), Spec("u1"), Spec("u2"));

        // Act: "w1" between "u1" and "u2"
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("w1"), fixture.After("u1")));

        // Assert
        Assert.Equal(["[Work 1]", "w2", "[Ungrouped 3]", "u1", "w1", "u2"], fixture.Describe());
        Assert.Null(fixture.Card("w1").GroupName);
        Assert.Null(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Single(repo => repo.Path == fixture.Card("w1").RepoPath).GroupName);
    }

    /// <summary>
    ///     Test that dropping a card into Favorites makes it a favorite, keeps its group, and puts it at
    ///     the drop position.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_IntoFavorites_MarksFavoriteAndKeepsTheGroup()
    {
        // Arrange
        var fixture = CreateFixture(Spec("f1", favorite: true), Spec("f2", favorite: true), Spec("w1", "Work"), Spec("w2", "Work"));

        // Act: "w2" between the two favorites
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("w2"), fixture.After("f1")));

        // Assert
        Assert.True(fixture.Card("w2").IsFavorite);
        Assert.Equal("Work", fixture.Card("w2").GroupName);
        Assert.Equal(["[Favorites 3]", "f1", "w2", "f2", "[Work 1]", "w1"], fixture.Describe());
        Assert.True(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Single(repo => repo.Path == fixture.Card("w2").RepoPath).IsFavorite);
    }

    /// <summary>
    ///     Test that dropping a favorite into a group takes it out of Favorites and into that group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_OutOfFavoritesIntoGroup_ClearsFavoriteAndSetsTheGroup()
    {
        // Arrange: a favorite that belongs to another group
        var fixture = CreateFixture(Spec("fav", "Home", favorite: true), Spec("w1", "Work"), Spec("w2", "Work"));

        // Act
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("fav"), fixture.After("w1")));

        // Assert
        Assert.False(fixture.Card("fav").IsFavorite);
        Assert.Equal("Work", fixture.Card("fav").GroupName);
        Assert.Equal(["[Work 3]", "w1", "fav", "w2"], fixture.Describe());
    }

    /// <summary>
    ///     Test that dropping a favorite among the ungrouped repos un-favorites it and clears its group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_OutOfFavoritesIntoUngrouped_ClearsFavoriteAndGroup()
    {
        // Arrange
        var fixture = CreateFixture(Spec("fav", "Work", favorite: true), Spec("w1", "Work"), Spec("u1"));

        // Act
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("fav"), fixture.Before("u1")));

        // Assert
        Assert.False(fixture.Card("fav").IsFavorite);
        Assert.Null(fixture.Card("fav").GroupName);
        Assert.Equal(["[Work 1]", "w1", "[Ungrouped 2]", "fav", "u1"], fixture.Describe());
    }

    /// <summary>
    ///     Test that when the section change cannot be saved nothing changes, the user is told once and
    ///     the order is not attempted.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_SectionChangeSaveFails_ChangesNothingAndReports()
    {
        // Arrange
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("h1", "Home"), Spec("h2", "Home"));
        fixture.BreakSaving();

        // Act
        var dropped = fixture.ViewModel.DropCard(fixture.Card("h1"), fixture.Before("w1"));

        // Assert
        Assert.False(dropped);
        Assert.Equal("Home", fixture.Card("h1").GroupName);
        Assert.Equal(["w1", "h1", "h2"], fixture.Names());
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that dropping a card on a group's header puts it at the bottom of that group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_OnGroupHeader_GoesToTheBottomOfTheGroup()
    {
        // Arrange
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("h1", "Home"), Spec("w2", "Work"), Spec("h2", "Home"));

        // Act: "h1" on the Work header
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("h1"), RepoDropTarget.ForHeader(fixture.Header("Work"))));

        // Assert
        Assert.Equal(["[Home 1]", "h2", "[Work 3]", "w1", "w2", "h1"], fixture.Describe());
        Assert.Equal(["w1", "w2", "h1", "h2"], fixture.SavedNames());
    }

    /// <summary>
    ///     Test that dropping a card on a collapsed group's header moves it into the group, leaves the
    ///     group collapsed and shows the new count.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_OnCollapsedGroupHeader_KeepsItCollapsedAndUpdatesTheCount()
    {
        // Arrange: a collapsed Work group
        var fixture = CreateFixture(Spec("w1", "Work"), Spec("h1", "Home"), Spec("h2", "Home"));
        fixture.ViewModel.ToggleGroupCollapsed("Work");
        var header = fixture.Header("Work");
        Assert.Equal(1, header.Count);

        // Act
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("h1"), RepoDropTarget.ForHeader(header)));

        // Assert: still collapsed, one more member, and the card is not shown
        Assert.True(fixture.ViewModel.IsGroupCollapsed("Work"));
        Assert.Same(header, fixture.Header("Work"));
        Assert.True(header.IsCollapsed);
        Assert.Equal(2, header.Count);
        Assert.Equal("Work", fixture.Card("h1").GroupName);
        Assert.DoesNotContain(fixture.ViewModel.DisplayedRepoCards, card => card.RepoName == "h1");
        Assert.Equal(["Work"], SettingsStore.Load(fixture.ConfigDirectory).CollapsedGroups);
    }

    /// <summary>
    ///     Test that dropping on the Favorites header favorites the card at the bottom of Favorites, and
    ///     dropping on the Ungrouped header un-favorites it and clears its group, at the bottom of
    ///     Ungrouped.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DropCard_OnFavoritesAndUngroupedHeaders_MoveTheCardThere()
    {
        // Arrange
        var fixture = CreateFixture(Spec("f1", favorite: true), Spec("w1", "Work"), Spec("u1"), Spec("u2"));

        // Act: "w1" onto Favorites
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("w1"), RepoDropTarget.ForHeader(fixture.Header("Favorites"))));

        // Assert: last of Favorites, still in its group
        Assert.Equal(["[Favorites 2]", "f1", "w1", "[Ungrouped 2]", "u1", "u2"], fixture.Describe());
        Assert.Equal("Work", fixture.Card("w1").GroupName);

        // Act: "f1" onto Ungrouped
        Assert.True(fixture.ViewModel.DropCard(fixture.Card("f1"), RepoDropTarget.ForHeader(fixture.Header("Ungrouped"))));

        // Assert: last of Ungrouped, not a favorite
        Assert.Equal(["[Favorites 1]", "w1", "[Ungrouped 3]", "u1", "u2", "f1"], fixture.Describe());
        Assert.False(fixture.Card("f1").IsFavorite);
    }

    /// <summary>
    ///     Test that while the filter has text reordering is off: the view model says so, and Move and
    ///     drop are refused without changing anything.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FilterText_DisablesReorderingAndRefusesMoveAndDrop()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));
        Assert.True(fixture.ViewModel.IsReorderEnabled);

        // Act
        fixture.ViewModel.FilterText = "b";

        // Assert
        Assert.False(fixture.ViewModel.IsReorderEnabled);
        Assert.False(fixture.ViewModel.CanMoveCard(fixture.Card("b"), RepoMove.Top));
        Assert.False(fixture.ViewModel.MoveCard(fixture.Card("b"), RepoMove.Top));
        Assert.False(fixture.ViewModel.CanDrop(fixture.Card("a"), fixture.After("c")));
        Assert.False(fixture.ViewModel.DropCard(fixture.Card("a"), fixture.After("c")));
        Assert.Equal(["a", "b", "c"], fixture.Names());
        Assert.False(File.Exists(Path.Combine(fixture.ConfigDirectory, "settings.json")));

        // Act: the filter is cleared
        fixture.ViewModel.FilterText = "  ";

        // Assert: blank text is no filter
        Assert.True(fixture.ViewModel.IsReorderEnabled);
        Assert.True(fixture.ViewModel.MoveCard(fixture.Card("b"), RepoMove.Top));
    }

    /// <summary>
    ///     Test that typing and clearing a filter never changes the stored order, and that the filtered
    ///     list shows the matches in that order.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FilterText_DoesNotChangeTheStoredOrder()
    {
        // Arrange: an order that is not alphabetical
        var fixture = CreateFixture(Spec("zeta-1"), Spec("alpha"), Spec("zeta-2"));

        // Act
        fixture.ViewModel.FilterText = "zeta";
        var filtered = fixture.Displayed();
        fixture.ViewModel.FilterText = null;

        // Assert
        Assert.Equal(["zeta-1", "zeta-2"], filtered);
        Assert.Equal(["zeta-1", "alpha", "zeta-2"], fixture.Displayed());
        Assert.Equal(["zeta-1", "alpha", "zeta-2"], fixture.StoredNames());
    }

    /// <summary>
    ///     Test that changing the filter text raises the change notification for
    ///     <see cref="MainWindowViewModel.IsReorderEnabled"/>, so the view can follow it.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_IsReorderEnabled_RaisesPropertyChangedWithTheFilter()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"));
        var raised = new List<string?>();
        fixture.ViewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        fixture.ViewModel.FilterText = "a";

        // Assert
        Assert.Contains(nameof(MainWindowViewModel.IsReorderEnabled), raised);
    }

    /// <summary>
    ///     Test that a move is saved in the user's settings and that a view model built from the saved
    ///     settings shows the same order.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveCard_OrderIsPersistedAndReloaded()
    {
        // Arrange
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));

        // Act
        Assert.True(fixture.ViewModel.MoveCard(fixture.Card("c"), RepoMove.Top));
        var reloaded = SettingsStore.Load(fixture.ConfigDirectory);
        var restarted = new MainWindowViewModel(reloaded, configDirectory: fixture.ConfigDirectory);

        // Assert: the marker is stored and the order survives
        Assert.True(reloaded.HasUserRepoOrder);
        Assert.Equal(["c", "a", "b"], reloaded.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        Assert.Equal(["c", "a", "b"], restarted.DisplayedRepoCards.Select(card => card.RepoName));
    }

    /// <summary>
    ///     Test that removing a repo takes it out of the order and leaves the others in the order the
    ///     user gave them, in the list and in the file.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveRepo_LeavesTheRestOfTheOrderAndPersistsIt()
    {
        // Arrange: an order the user made
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));
        Assert.True(fixture.ViewModel.MoveCard(fixture.Card("c"), RepoMove.Top));

        // Act
        fixture.ViewModel.RemoveRepo(fixture.Card("a"));

        // Assert
        Assert.Equal(["c", "b"], fixture.Names());
        Assert.Equal(["c", "b"], fixture.StoredNames());
        Assert.Equal(["c", "b"], fixture.SavedNames());
    }

    /// <summary>
    ///     Test that applying settings from the Settings window keeps the repo order and its marker, in
    ///     memory and in the file.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_KeepsTheRepoOrder()
    {
        // Arrange: a reordered list
        var fixture = CreateFixture(Spec("a"), Spec("b"), Spec("c"));
        Assert.True(fixture.ViewModel.MoveCard(fixture.Card("c"), RepoMove.Top));

        // Act: the Settings window hands over a fresh settings object without any order
        fixture.ViewModel.ApplySettings(new AppSettings { ShellPreference = "pwsh" });

        // Assert
        Assert.True(fixture.ViewModel.Settings.HasUserRepoOrder);
        Assert.Equal(["c", "a", "b"], fixture.StoredNames());
        Assert.Equal(["c", "a", "b"], fixture.Names());
        var reloaded = SettingsStore.Load(fixture.ConfigDirectory);
        Assert.True(reloaded.HasUserRepoOrder);
        Assert.Equal("pwsh", reloaded.ShellPreference);
        Assert.Equal(["c", "a", "b"], fixture.SavedNames());
    }

    /// <summary>
    ///     Gets whether each of the four moves is available for a card.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="card">The card.</param>
    /// <returns>Availability of top, up, down and bottom, in that order.</returns>
    private static List<bool> Availability(MainWindowViewModel viewModel, RepoCardViewModel card) =>
        [
            viewModel.CanMoveCard(card, RepoMove.Top),
            viewModel.CanMoveCard(card, RepoMove.Up),
            viewModel.CanMoveCard(card, RepoMove.Down),
            viewModel.CanMoveCard(card, RepoMove.Bottom)
        ];

    /// <summary>
    ///     Describes one repo to create for a test.
    /// </summary>
    /// <param name="name">The repo's folder name (also its display name).</param>
    /// <param name="group">The repo's group, or <see langword="null"/>.</param>
    /// <param name="favorite">Whether the repo is a favorite.</param>
    /// <param name="lastLaunched">When the repo was last launched, or <see langword="null"/>.</param>
    /// <returns>The description.</returns>
    private static RepoSpec Spec(
        string name, string? group = null, bool favorite = false, DateTimeOffset? lastLaunched = null) =>
        new(name, group, favorite, lastLaunched);

    /// <summary>
    ///     Creates a fixture with default settings over the described repos, stored in that order.
    /// </summary>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(params RepoSpec[] specs) => CreateFixture(new AppSettings(), specs);

    /// <summary>
    ///     Creates a fixture over the described repos, stored in that order.
    /// </summary>
    /// <param name="settings">The settings to start from; their repo list is replaced.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(AppSettings settings, params RepoSpec[] specs)
    {
        var configDirectory = CreateDirectory("config");
        settings.HasUserRepoOrder = true;
        settings.RecentRepos =
        [
            .. specs.Select(spec => new RecentRepo
            {
                Path = CreateDirectory(spec.Name),
                GroupName = spec.Group,
                IsFavorite = spec.Favorite,
                LastLaunchedUtc = spec.LastLaunched
            })
        ];
        var starter = new FakeAgentToolProcessStarter();
        var viewModel = new MainWindowViewModel(
            settings, configDirectory: configDirectory,
            // Launch-safe by construction: a fixture never starts a real process
            processStarter: starter, shellDetector: FakeAgentToolProcessStarter.CreateShellDetector());
        var failures = new List<string>();
        viewModel.SettingsSaveFailed += (_, message) => failures.Add(message);
        return new Fixture(this, viewModel, settings, configDirectory, failures, starter);
    }

    /// <summary>
    ///     Creates a folder named <paramref name="name"/> under a unique temporary parent that is
    ///     removed when the test class is disposed.
    /// </summary>
    /// <param name="name">The folder name (also the display name of a repo).</param>
    /// <returns>The folder's path.</returns>
    private string CreateDirectory(string name)
    {
        var parent = Path.Combine(Path.GetTempPath(), "agentcontrol_ordering_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        _tempPaths.Add(parent);
        return path;
    }

    /// <inheritdoc />
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
    ///     One repo to create for a test.
    /// </summary>
    /// <param name="Name">The folder name.</param>
    /// <param name="Group">The group, or <see langword="null"/>.</param>
    /// <param name="Favorite">Whether it is a favorite.</param>
    /// <param name="LastLaunched">When it was last launched, or <see langword="null"/>.</param>
    private sealed record RepoSpec(string Name, string? Group, bool Favorite, DateTimeOffset? LastLaunched);

    /// <summary>
    ///     The view model under test with what the tests need to read it and to break saving.
    /// </summary>
    private sealed class Fixture
    {
        private readonly RepoOrderingTests _owner;

        /// <summary>
        ///     Initializes a new <see cref="Fixture"/>.
        /// </summary>
        /// <param name="owner">The test class, which owns the temporary folders.</param>
        /// <param name="viewModel">The view model.</param>
        /// <param name="settings">The live settings it was created over.</param>
        /// <param name="configDirectory">The directory its settings are saved to.</param>
        /// <param name="failures">The messages of <see cref="MainWindowViewModel.SettingsSaveFailed"/>.</param>
        /// <param name="starter">The process starter every card launches through.</param>
        public Fixture(
            RepoOrderingTests owner, MainWindowViewModel viewModel, AppSettings settings, string configDirectory,
            List<string> failures, FakeAgentToolProcessStarter starter)
        {
            _owner = owner;
            ViewModel = viewModel;
            Settings = settings;
            ConfigDirectory = configDirectory;
            Failures = failures;
            Starter = starter;
        }

        /// <summary>Gets the view model.</summary>
        public MainWindowViewModel ViewModel { get; }

        /// <summary>Gets the live settings.</summary>
        public AppSettings Settings { get; }

        /// <summary>Gets the directory settings are saved to.</summary>
        public string ConfigDirectory { get; }

        /// <summary>Gets the messages of the save-failed event.</summary>
        public List<string> Failures { get; }

        /// <summary>Gets the process starter that records launches.</summary>
        public FakeAgentToolProcessStarter Starter { get; }

        /// <summary>
        ///     Creates a repo folder under a unique temporary parent.
        /// </summary>
        /// <param name="name">The folder name.</param>
        /// <returns>The folder's path.</returns>
        public string CreateDirectory(string name) => _owner.CreateDirectory(name);

        /// <summary>
        ///     Finds a repo's card by the repo's folder name.
        /// </summary>
        /// <param name="name">The folder name.</param>
        /// <returns>The card.</returns>
        public RepoCardViewModel Card(string name) => ViewModel.RepoCards.Single(card => card.RepoName == name);

        /// <summary>
        ///     Creates the drop place just before a card (in the card's own section).
        /// </summary>
        /// <param name="name">The anchor card's repo name.</param>
        /// <returns>The place.</returns>
        public RepoDropTarget Before(string name) => Place(name, insertBefore: true);

        /// <summary>
        ///     Creates the drop place just after a card (in the card's own section).
        /// </summary>
        /// <param name="name">The anchor card's repo name.</param>
        /// <returns>The place.</returns>
        public RepoDropTarget After(string name) => Place(name, insertBefore: false);

        /// <summary>
        ///     Finds the header of a section that is shown.
        /// </summary>
        /// <param name="name">The section's name.</param>
        /// <returns>The header.</returns>
        public RepoGroupHeaderViewModel Header(string name) =>
            ViewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(header => header.Name == name);

        /// <summary>
        ///     Lists the repo names of the card collection, in order.
        /// </summary>
        /// <returns>The names.</returns>
        public List<string> Names() => [.. ViewModel.RepoCards.Select(card => card.RepoName)];

        /// <summary>
        ///     Lists the repo names of the displayed cards, in order.
        /// </summary>
        /// <returns>The names.</returns>
        public List<string> Displayed() => [.. ViewModel.DisplayedRepoCards.Select(card => card.RepoName)];

        /// <summary>
        ///     Lists the repo names in the live settings' list, in order.
        /// </summary>
        /// <returns>The names.</returns>
        public List<string> StoredNames() =>
            [.. ViewModel.Settings.RecentRepos.Select(repo => Path.GetFileName(repo.Path))];

        /// <summary>
        ///     Lists the repo names in the saved settings file, in order.
        /// </summary>
        /// <returns>The names.</returns>
        public List<string> SavedNames() =>
            [.. SettingsStore.Load(ConfigDirectory).RecentRepos.Select(repo => Path.GetFileName(repo.Path))];

        /// <summary>
        ///     Lists the group of each card in the collection, in order (empty for none).
        /// </summary>
        /// <returns>The group names.</returns>
        public List<string> Groups() => [.. ViewModel.RepoCards.Select(card => card.GroupName ?? string.Empty)];

        /// <summary>
        ///     Describes the displayed items as text: a header as <c>[Name count]</c> and a card as its
        ///     repo name.
        /// </summary>
        /// <returns>One entry per displayed item, in order.</returns>
        public List<string> Describe() =>
            [.. ViewModel.DisplayedItems.Select(item => item switch
            {
                RepoGroupHeaderViewModel header => $"[{header.Name} {header.Count}]",
                RepoCardViewModel card => card.RepoName,
                _ => throw new InvalidOperationException("Unexpected list item.")
            })];

        /// <summary>
        ///     Makes every save fail by putting a directory where the settings file belongs (after
        ///     removing the file an earlier save may have written).
        /// </summary>
        public void BreakSaving()
        {
            var settingsPath = Path.Combine(ConfigDirectory, "settings.json");
            File.Delete(settingsPath);
            Directory.CreateDirectory(settingsPath);
        }

        /// <summary>
        ///     Makes saving work again.
        /// </summary>
        public void RestoreSaving() => Directory.Delete(Path.Combine(ConfigDirectory, "settings.json"), recursive: true);

        private RepoDropTarget Place(string name, bool insertBefore)
        {
            var anchor = Card(name);
            var section = MainWindowViewModel.GetSectionOf(anchor);
            return RepoDropTarget.ForCard(anchor, insertBefore, section.Kind, section.Name);
        }
    }
}
