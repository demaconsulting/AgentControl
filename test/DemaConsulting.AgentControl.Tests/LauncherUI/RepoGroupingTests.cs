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
///     Unit tests of the sectioned repo list of <see cref="MainWindowViewModel"/>: the user's own
///     groups, the built-in Favorites and Ungrouped sections, collapsing, filtering, and creating,
///     moving, renaming and removing groups - all user-specific and stored only in the settings.
/// </summary>
/// <remarks>
///     Each test uses unique temporary repo and configuration directories. Runs in the
///     <c>RealProcess</c> collection because every card's repo state is read in the background (real git
///     subprocesses) from the moment the view model is built; every view model a test builds is tracked, and
///     its reads have finished (and it is disposed) before the folders are removed.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoGroupingTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     The view models built by the running test, settled and disposed before the folders go.
    /// </summary>
    private readonly List<MainWindowViewModel> _viewModels = [];

    /// <summary>
    ///     Test that with no repo in a group the list is flat and header-free, exactly as before
    ///     groups existed: favorites first, then the user's own order.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedItems_NoGroups_FlatListWithoutHeaders()
    {
        // Arrange: launch times that point the other way round than the stored order
        var now = DateTimeOffset.UtcNow;
        var viewModel = CreateViewModel(
            new Spec("old", LastLaunched: now.AddDays(-5)),
            new Spec("fav", IsFavorite: true, LastLaunched: now.AddDays(-9)),
            new Spec("new", LastLaunched: now));

        // Act
        var shown = Describe(viewModel);

        // Assert: no header at all, not even Favorites; the others in the stored order
        Assert.Equal(["fav", "old", "new"], shown);
        Assert.False(viewModel.HasGroups);
        Assert.Empty(viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>());
    }

    /// <summary>
    ///     Test that grouped repos are shown in sections: Favorites first, the user's groups
    ///     alphabetically (ignoring case), Ungrouped last, each header with its count, and repos
    ///     within a section in the user's own order (not by launch time).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedItems_Groups_AreSectionedAndOrdered()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var viewModel = CreateViewModel(
            new Spec("loose", LastLaunched: now),
            new Spec("w-old", Group: "Work", LastLaunched: now.AddDays(-3)),
            new Spec("h1", Group: "home", LastLaunched: now.AddDays(-1)),
            new Spec("w-new", Group: "Work", LastLaunched: now.AddDays(-1)),
            new Spec("a1", Group: "Alpha"),
            new Spec("star", IsFavorite: true));

        // Act
        var shown = Describe(viewModel);

        // Assert: "w-old" stays before "w-new" although it was launched earlier
        Assert.Equal(
            [
                "[Favorites 1]", "star",
                "[Alpha 1]", "a1",
                "[home 1]", "h1",
                "[Work 2]", "w-old", "w-new",
                "[Ungrouped 1]", "loose"
            ],
            shown);
        Assert.Equal(
            ["star", "a1", "h1", "w-old", "w-new", "loose"],
            viewModel.DisplayedRepoCards.Select(card => card.RepoName));
    }

    /// <summary>
    ///     Test that a favorite repo is listed only under Favorites (not again under its group), shows
    ///     its group as a chip there, and returns to its group when it is no longer a favorite.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Favorite_IsListedOnlyUnderFavoritesAndKeepsItsGroup()
    {
        // Arrange
        var viewModel = CreateViewModel(
            new Spec("fav", Group: "Work", IsFavorite: true),
            new Spec("other", Group: "Work"));
        var favorite = viewModel.RepoCards.Single(card => card.RepoName == "fav");

        // Assert: under Favorites only, with the group chip; the Work group lists the other repo
        Assert.Equal(["[Favorites 1]", "fav", "[Work 1]", "other"], Describe(viewModel));
        Assert.True(favorite.ShowGroupChip);
        Assert.Equal("Work", favorite.GroupName);

        // Act: no longer a favorite
        favorite.IsFavorite = false;

        // Assert: back in its group, no Favorites header, no chip
        Assert.Equal(["[Work 2]", "fav", "other"], Describe(viewModel));
        Assert.False(favorite.ShowGroupChip);
    }

    /// <summary>
    ///     Test that a section without a repo has no header: no Favorites header without a favorite
    ///     and no Ungrouped header when every repo is grouped.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedItems_EmptySections_HaveNoHeader()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("a", Group: "G1"), new Spec("b", Group: "G2"));

        // Act
        var shown = Describe(viewModel);

        // Assert
        Assert.Equal(["[G1 1]", "a", "[G2 1]", "b"], shown);
    }

    /// <summary>
    ///     Test that collapsing a group hides its cards but keeps its header (with the count),
    ///     remembers the state in the settings file, and that a view model built from those settings
    ///     shows the group collapsed.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ToggleGroupCollapsed_HidesCardsAndPersists()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("a", Group: "Work"), new Spec("b", Group: "Work"), new Spec("c"));

        // Act
        viewModel.ToggleGroupCollapsed("Work");

        // Assert: header stays, its cards are gone, and the choice reached the file
        Assert.Equal(["[Work 2 collapsed]", "[Ungrouped 1]", "c"], Describe(viewModel));
        Assert.Equal(["c"], viewModel.DisplayedRepoCards.Select(card => card.RepoName));
        Assert.Equal(["Work"], SettingsStore.Load(configDir).CollapsedGroups);

        // Act: a new session reads the remembered state
        var reopened = Track(new MainWindowViewModel(SettingsStore.Load(configDir), configDirectory: configDir));

        // Assert
        Assert.Equal(["[Work 2 collapsed]", "[Ungrouped 1]", "c"], Describe(reopened));

        // Act: expand again
        viewModel.ToggleGroupCollapsed("work");

        // Assert: letter case does not matter, and the state is forgotten
        Assert.Equal(["[Work 2]", "a", "b", "[Ungrouped 1]", "c"], Describe(viewModel));
        Assert.Empty(SettingsStore.Load(configDir).CollapsedGroups);
    }

    /// <summary>
    ///     Test that the built-in Favorites and Ungrouped sections can be collapsed and remembered too.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ToggleGroupCollapsed_BuiltInSections_CollapseAndPersist()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir, new Spec("star", IsFavorite: true), new Spec("g", Group: "G"), new Spec("loose"));

        // Act
        viewModel.ToggleGroupCollapsed(RepoGroupName.FavoritesName);
        viewModel.ToggleGroupCollapsed(RepoGroupName.UngroupedName);

        // Assert
        Assert.Equal(["[Favorites 1 collapsed]", "[G 1]", "g", "[Ungrouped 1 collapsed]"], Describe(viewModel));
        Assert.Equal(
            ["Favorites", "Ungrouped"], SettingsStore.Load(configDir).CollapsedGroups.Order());
    }

    /// <summary>
    ///     Test that filtering shows every match even inside a collapsed group, hides sections with no
    ///     match, counts only the matches, leaves the remembered collapsed state alone, and that
    ///     clearing the filter brings the collapsed state back.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FilterText_ShowsMatchesInCollapsedGroupsAndHidesEmptyOnes()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir,
            new Spec("alpha-one", Group: "Work"),
            new Spec("alpha-two", Group: "Work"),
            new Spec("beta", Group: "Work"),
            new Spec("alpha-home", Group: "Home"),
            new Spec("gamma"));
        viewModel.ToggleGroupCollapsed("Work");
        Assert.Equal(["[Home 1]", "alpha-home", "[Work 3 collapsed]", "[Ungrouped 1]", "gamma"], Describe(viewModel));

        // Act: filter on a name that matches inside the collapsed group and another group
        viewModel.FilterText = "ALPHA";

        // Assert: matches are expanded, counts are of the matches, empty sections are gone
        Assert.Equal(["[Home 1]", "alpha-home", "[Work 2]", "alpha-one", "alpha-two"], Describe(viewModel));
        Assert.All(viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>(), header => Assert.False(header.IsToggleEnabled));
        Assert.Equal(["Work"], SettingsStore.Load(configDir).CollapsedGroups);

        // Act: clear the filter
        viewModel.FilterText = string.Empty;

        // Assert: the remembered state applies again
        Assert.Equal(["[Home 1]", "alpha-home", "[Work 3 collapsed]", "[Ungrouped 1]", "gamma"], Describe(viewModel));
        Assert.All(viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>(), header => Assert.True(header.IsToggleEnabled));
    }

    /// <summary>
    ///     Test that a filter that matches nothing shows no header at all and the empty state.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FilterText_NoMatch_ShowsNothing()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("a", Group: "G"), new Spec("b"));

        // Act
        viewModel.FilterText = "zzz-nothing";

        // Assert
        Assert.Empty(viewModel.DisplayedItems);
        Assert.True(viewModel.HasNoDisplayedRepos);
    }

    /// <summary>
    ///     Test that a list whose sections are all collapsed still shows the headers (it is not the
    ///     empty state).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AllGroupsCollapsed_HeadersRemainAndListIsNotEmpty()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("a", Group: "G"));

        // Act
        viewModel.ToggleGroupCollapsed("G");

        // Assert
        Assert.Equal(["[G 1 collapsed]"], Describe(viewModel));
        Assert.False(viewModel.HasNoDisplayedRepos);
        Assert.Empty(viewModel.DisplayedRepoCards);
    }

    /// <summary>
    ///     Test that moving a repo into a new group shows the header, persists only in the settings,
    ///     joins an existing group ignoring case, refuses invalid names, and that taking the last repo
    ///     out of a group removes the header (and the headers altogether when no group is left).
    /// </summary>
    [Fact]
    public void RepoCardViewModel_MoveToGroup_NewExistingInvalidAndRemove()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("a"), new Spec("b"));
        var a = viewModel.RepoCards.Single(card => card.RepoName == "a");
        var b = viewModel.RepoCards.Single(card => card.RepoName == "b");

        // Act: a new group, with a name that needs cleaning up
        Assert.True(a.MoveToGroup("  Client   X "));

        // Assert
        Assert.Equal(["[Client X 1]", "a", "[Ungrouped 1]", "b"], Describe(viewModel));
        Assert.Equal("Client X", SettingsStore.Load(configDir).RecentRepos.Single(r => r.Path == a.RepoPath).GroupName);

        // Act: the other repo joins the group using different letter case
        Assert.True(b.MoveToGroup("client x"));

        // Assert: one group, spelled as it was first
        Assert.Equal(["[Client X 2]", "a", "b"], Describe(viewModel));
        Assert.Equal(["Client X"], viewModel.GroupNames);
        Assert.Equal("Client X", b.GroupName);

        // Act / Assert: invalid names change nothing
        Assert.False(b.MoveToGroup(new string('x', RepoGroupName.MaxLength + 1)));
        Assert.False(b.MoveToGroup("Favorites"));
        Assert.False(b.MoveToGroup("ungrouped"));
        Assert.Equal("Client X", b.GroupName);

        // Act: take both out
        Assert.True(a.MoveToGroup(null));
        Assert.True(b.MoveToGroup(string.Empty));

        // Assert: no group is left, so no header is shown any more
        Assert.False(viewModel.HasGroups);
        Assert.Empty(viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>());
        Assert.Empty(viewModel.GroupNames);
        Assert.All(SettingsStore.Load(configDir).RecentRepos, repo => Assert.Null(repo.GroupName));
    }

    /// <summary>
    ///     Test that groups, card colors and group colors are never written into the repository: the
    ///     repo folder keeps exactly the entries it had and only the settings file changes.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_GroupAndColor_NeverWriteToTheRepository()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("a"));
        var card = viewModel.RepoCards.Single();
        var before = Directory.EnumerateFileSystemEntries(card.RepoPath, "*", SearchOption.AllDirectories).Order().ToList();

        // Act
        card.MoveToGroup("Work");
        card.SetCardColor("Teal");
        viewModel.ToggleGroupCollapsed("Work");
        viewModel.RenameGroup("Work", "Office");
        viewModel.SetGroupColor("Office", "Lime");

        // Assert: the repository has no new file or folder, the settings do
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(card.RepoPath, "*", SearchOption.AllDirectories).Order());
        var json = File.ReadAllText(Path.Combine(configDir, "settings.json"));
        Assert.Contains("Office", json, StringComparison.Ordinal);
        Assert.Contains("Teal", json, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that renaming a group moves all its repos, carries its collapsed state, accepts a
    ///     change of letter case only, and refuses an unknown group, an invalid name and the name of
    ///     another group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RenameGroup_MovesMembersAndRefusesBadNames()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir, new Spec("a", Group: "Work"), new Spec("b", Group: "Work"), new Spec("c", Group: "Home"));
        viewModel.ToggleGroupCollapsed("Work");

        // Act / Assert: refused
        Assert.False(viewModel.RenameGroup("Nope", "Anything"));
        Assert.False(viewModel.RenameGroup("Work", "  "));
        Assert.False(viewModel.RenameGroup("Work", "Favorites"));
        Assert.False(viewModel.RenameGroup("Work", new string('x', RepoGroupName.MaxLength + 1)));
        Assert.False(viewModel.RenameGroup("Work", "home"));
        Assert.Equal(["Home", "Work"], viewModel.GroupNames);

        // Act: a real rename
        Assert.True(viewModel.RenameGroup("Work", "Office"));

        // Assert: members, collapsed state and file all follow
        Assert.Equal(["[Home 1]", "c", "[Office 2 collapsed]"], Describe(viewModel));
        Assert.Equal(["Office"], SettingsStore.Load(configDir).CollapsedGroups);
        Assert.Equal(
            ["Office", "Office", "Home"],
            SettingsStore.Load(configDir).RecentRepos.OrderBy(r => Path.GetFileName(r.Path)).Select(r => r.GroupName));
        Assert.Equal("Office", viewModel.RepoCards.Single(card => card.RepoName == "a").GroupName);

        // Act: only the letter case changes
        Assert.True(viewModel.RenameGroup("Office", "office"));

        // Assert
        Assert.Equal(["Home", "office"], viewModel.GroupNames);
    }

    /// <summary>
    ///     Test that removing a group leaves its repos ungrouped, forgets its collapsed state and does not
    ///     touch other groups; removing an unknown group does nothing.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroup_LeavesMembersUngrouped()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir, new Spec("a", Group: "Work"), new Spec("b", Group: "Work"), new Spec("c", Group: "Home"));
        viewModel.ToggleGroupCollapsed("Work");

        // Act
        Assert.False(viewModel.RemoveGroup("Nope"));
        Assert.True(viewModel.RemoveGroup("work"));

        // Assert
        Assert.Equal(["[Home 1]", "c", "[Ungrouped 2]", "a", "b"], Describe(viewModel));
        Assert.Equal(["Home"], viewModel.GroupNames);
        Assert.Empty(SettingsStore.Load(configDir).CollapsedGroups);
        Assert.All(viewModel.RepoCards.Where(card => card.RepoName != "c"), card => Assert.False(card.HasGroup));
    }

    /// <summary>
    ///     Test that toggling a group changes the list in place: the header and the cards that stay are
    ///     the same objects, so the view keeps their controls (and keyboard focus).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ToggleGroupCollapsed_KeepsHeaderAndUnchangedCardsInPlace()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("a", Group: "Work"), new Spec("b", Group: "Work"), new Spec("c"));
        var header = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(h => h.Name == "Work");
        var ungroupedHeader = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(h => h.Name == "Ungrouped");
        var loose = viewModel.RepoCards.Single(card => card.RepoName == "c");
        var changes = 0;
        viewModel.DisplayedItems.CollectionChanged += (_, _) => changes++;

        // Act
        header.ToggleCommand.Execute(null);

        // Assert: same instances, and only the two hidden cards were removed (no reset)
        Assert.Same(header, viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(h => h.Name == "Work"));
        Assert.Same(ungroupedHeader, viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(h => h.Name == "Ungrouped"));
        Assert.Contains(loose, viewModel.DisplayedItems);
        Assert.True(header.IsCollapsed);
        Assert.Equal(2, changes);
    }

    /// <summary>
    ///     Test that a header's rename and remove commands are forwarded by the view model with the
    ///     group's name, for the window to prompt and confirm.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_HeaderCommands_RaiseGroupRequestsWithTheGroupName()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("a", Group: "Work"));
        var header = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single();
        var renamed = new List<string>();
        var removed = new List<string>();
        viewModel.GroupRenameRequested += (_, name) => renamed.Add(name);
        viewModel.GroupRemoveRequested += (_, name) => removed.Add(name);

        // Act
        header.RenameCommand.Execute(null);
        header.RemoveGroupCommand.Execute(null);

        // Assert: requests only; nothing changed yet
        Assert.Equal(["Work"], renamed);
        Assert.Equal(["Work"], removed);
        Assert.Equal(["Work"], viewModel.GroupNames);
    }

    /// <summary>
    ///     Test that applying new settings (as the Settings window does) keeps the remembered collapsed
    ///     sections.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_KeepsCollapsedGroups()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("a", Group: "Work"));
        viewModel.ToggleGroupCollapsed("Work");

        // Act
        viewModel.ApplySettings(new AppSettings { Theme = ThemePreference.Dark });

        // Assert
        Assert.Equal(["[Work 1 collapsed]"], Describe(viewModel));
        Assert.Equal(["Work"], SettingsStore.Load(configDir).CollapsedGroups);
    }

    /// <summary>
    ///     Test that settings built in memory with missing, null or invalid organizing values (as a
    ///     damaged or hand-edited file could produce) never fail: such repos fall back to no group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_NullAndInvalidOrganizingSettings_FallBack()
    {
        // Arrange
        var tooLong = new string('x', RepoGroupName.MaxLength + 5);
        var settings = new AppSettings
        {
            CollapsedGroups = null!,
            RecentRepos =
            [
                new RecentRepo { Path = CreateRepoDirectory("blank"), GroupName = "   ", CardColor = "no-such-color" },
                new RecentRepo { Path = CreateRepoDirectory("reserved"), GroupName = "Favorites" },
                new RecentRepo { Path = CreateRepoDirectory("long"), GroupName = tooLong },
                new RecentRepo { Path = CreateRepoDirectory("ok"), GroupName = "  Team  " }
            ]
        };

        // Act
        var viewModel = Track(new MainWindowViewModel(settings, configDirectory: CreateTempDirectory()));

        // Assert: repaired on the fly, no collapse state, nothing thrown
        Assert.Equal(
            [
                "[Team 1]", "ok",
                "[" + new string('x', RepoGroupName.MaxLength) + " 1]", "long",
                "[Ungrouped 2]", "blank", "reserved"
            ],
            Describe(viewModel));
        Assert.False(viewModel.IsGroupCollapsed("Team"));
        Assert.Null(viewModel.RepoCards.Single(card => card.RepoName == "blank").CardColorName);
    }

    /// <summary>
    ///     Test that the header's section name is also spelled the way the settings of the first
    ///     member spell it, when look-alike names reach the view model without having been repaired.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_NamesDifferingInCase_FormOneSection()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("a", Group: "Work"), new Spec("b", Group: "work"));

        // Assert
        Assert.Single(viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>());
        Assert.Single(viewModel.GroupNames);
    }

    /// <summary>
    ///     Test that adding a repo to a group creates its card directly in that group (spelled as the
    ///     existing group, whatever the letter case asked for), persists the group, and puts the card
    ///     at the bottom of the group's section.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepoToGroup_NewRepo_AddsCardInTheGroupAndPersistsIt()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("w1", Group: "Work"), new Spec("loose"));
        var newRepo = CreateRepoDirectory("fresh");

        // Act: the group is asked for in different letter case
        var result = viewModel.AddRepoToGroup(newRepo, "work");

        // Assert: a new card, in "Work" (as spelled), saved, and listed in the Work section
        Assert.Equal(AddRepoResult.Added, result);
        var card = Assert.Single(viewModel.RepoCards, candidate => candidate.RepoName == "fresh");
        Assert.Equal("Work", card.GroupName);
        Assert.Equal(["Work"], viewModel.GroupNames);
        Assert.Equal(["[Work 2]", "w1", "fresh", "[Ungrouped 1]", "loose"], Describe(viewModel));
        var reloaded = SettingsStore.Load(configDir);
        Assert.Equal("Work", reloaded.RecentRepos.Single(repo => repo.Path == newRepo).GroupName);
    }

    /// <summary>
    ///     Test that adding a repo that is already listed moves its existing entry into the group
    ///     instead of adding a second one.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepoToGroup_RepoAlreadyListed_MovesItIntoTheGroup()
    {
        // Arrange: "loose" is ungrouped, "w1" holds the Work group
        var viewModel = CreateViewModel(new Spec("w1", Group: "Work"), new Spec("loose"));
        var loose = viewModel.RepoCards.Single(card => card.RepoName == "loose");

        // Act
        var result = viewModel.AddRepoToGroup(loose.RepoPath, "Work");

        // Assert: still two repos, and the existing one is now in Work
        Assert.Equal(AddRepoResult.MovedExisting, result);
        Assert.Equal(2, viewModel.RepoCards.Count);
        Assert.Equal("Work", loose.GroupName);
        Assert.Equal(["[Work 2]", "w1", "loose"], Describe(viewModel));
    }

    /// <summary>
    ///     Test that a repo whose folder does not exist is not added, and nothing changes.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepoToGroup_FolderMissing_AddsNothing()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("w1", Group: "Work"));
        var missing = Path.Combine(CreateTempDirectory(), "does-not-exist");

        // Act
        var result = viewModel.AddRepoToGroup(missing, "Work");

        // Assert
        Assert.Equal(AddRepoResult.Missing, result);
        Assert.Single(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that adding to a collapsed group opens it, so the repo that was just added is visible.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepoToGroup_CollapsedGroup_ExpandsIt()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("w1", Group: "Work"));
        viewModel.ToggleGroupCollapsed("Work");
        Assert.True(viewModel.IsGroupCollapsed("Work"));

        // Act
        viewModel.AddRepoToGroup(CreateRepoDirectory("fresh"), "Work");

        // Assert
        Assert.False(viewModel.IsGroupCollapsed("Work"));
        Assert.Equal(["[Work 2]", "w1", "fresh"], Describe(viewModel));
    }

    /// <summary>
    ///     Test that a group name the rules refuse (blank, or a built-in section's name) is rejected
    ///     before anything is added.
    /// </summary>
    /// <param name="groupName">The refused group name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Favorites")]
    [InlineData("Ungrouped")]
    public void MainWindowViewModel_AddRepoToGroup_InvalidGroupName_ThrowsAndAddsNothing(string groupName)
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("w1", Group: "Work"));

        // Act / Assert
        Assert.Throws<ArgumentException>(() => viewModel.AddRepoToGroup(CreateRepoDirectory("fresh"), groupName));
        Assert.Single(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that a group color is stored (under the group's own spelling, as the palette names the
    ///     color) and saved only in the settings, and that choosing no color clears it.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SetGroupColor_StoresColorAndNoneClears()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("w1", Group: "Work"), new Spec("loose"));

        // Act: letter case of the group and of the color does not matter
        var colored = viewModel.SetGroupColor("work", "teal");

        // Assert
        Assert.True(colored);
        Assert.Equal("Teal", viewModel.Settings.FindGroupColor("Work"));
        Assert.Equal("Teal", SettingsStore.Load(configDir).GroupColors["Work"]);
        Assert.True(viewModel.SetGroupColor("Work", "Teal"));

        // Act: no color
        var cleared = viewModel.SetGroupColor("Work", null);

        // Assert
        Assert.True(cleared);
        Assert.Null(viewModel.Settings.FindGroupColor("Work"));
        Assert.Empty(SettingsStore.Load(configDir).GroupColors);
    }

    /// <summary>
    ///     Test that a color the palette does not know, a group that does not exist and the built-in
    ///     Favorites and Ungrouped sections are refused, and nothing changes.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SetGroupColor_UnknownColorGroupOrBuiltInSection_ChangesNothing()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("w1", Group: "Work"), new Spec("fav", IsFavorite: true), new Spec("loose"));

        // Act / Assert
        Assert.False(viewModel.SetGroupColor("Work", "Plaid"));
        Assert.False(viewModel.SetGroupColor("Nope", "Teal"));
        Assert.False(viewModel.SetGroupColor(RepoGroupName.FavoritesName, "Teal"));
        Assert.False(viewModel.SetGroupColor(RepoGroupName.UngroupedName, "Teal"));
        Assert.Empty(viewModel.Settings.GroupColors);
        Assert.All(viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>(), header => Assert.False(header.HasColor));
    }

    /// <summary>
    ///     Test that a group color shows on the group's header (not on Favorites or Ungrouped) and on
    ///     the tiles of the group's repos that have no card color of their own - a favorite in the group
    ///     included - while a card color of its own wins and other repos keep their colors.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SetGroupColor_UpdatesHeaderAndCardsOfTheGroup()
    {
        // Arrange
        var viewModel = CreateViewModel(
            new Spec("plain", Group: "Work"), new Spec("own", Group: "Work"), new Spec("fav", Group: "Work", IsFavorite: true),
            new Spec("loose"));
        var plain = viewModel.RepoCards.Single(card => card.RepoName == "plain");
        var own = viewModel.RepoCards.Single(card => card.RepoName == "own");
        var favorite = viewModel.RepoCards.Single(card => card.RepoName == "fav");
        var loose = viewModel.RepoCards.Single(card => card.RepoName == "loose");
        Assert.True(own.SetCardColor("Red"));
        var looseClass = loose.AvatarStyleClass;
        var changed = new List<string?>();
        plain.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        Assert.True(viewModel.SetGroupColor("Work", "Teal"));

        // Assert: the header names the color; only the user's group has one
        var headers = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().ToList();
        var work = headers.Single(header => header.IsUserGroup);
        Assert.Equal("Teal", work.ColorName);
        Assert.Equal("groupAccent5", work.ColorStyleClass);
        Assert.Equal("Group color: Teal", work.ColorDescription);
        Assert.All(headers.Where(header => !header.IsUserGroup), header => Assert.False(header.HasColor));

        // The tiles: the group color, except for the card with its own color and the ungrouped repo
        Assert.Equal("avatar5", plain.AvatarStyleClass);
        Assert.Equal("avatar5", favorite.AvatarStyleClass);
        Assert.Equal("avatar8", own.AvatarStyleClass);
        Assert.Equal(looseClass, loose.AvatarStyleClass);
        Assert.Contains(nameof(RepoCardViewModel.AvatarStyleClass), changed);
        Assert.True(plain.IsTileColorChosen);
    }

    /// <summary>
    ///     Test that renaming a group keeps its color and its place in the user's group order under
    ///     the new name, in memory and in the saved settings.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RenameGroup_KeepsColorAndPosition()
    {
        // Arrange: Work is colored and moved before Alpha
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(
            configDir, new Spec("a", Group: "Alpha"), new Spec("w", Group: "Work"), new Spec("z", Group: "Zed"));
        Assert.True(viewModel.SetGroupColor("Work", "Teal"));
        Assert.True(viewModel.MoveGroup("Work", RepoGroupMove.Up));

        // Act
        Assert.True(viewModel.RenameGroup("Work", "Office"));

        // Assert: same place, same color, under the new name
        Assert.Equal(["[Office 1]", "w", "[Alpha 1]", "a", "[Zed 1]", "z"], Describe(viewModel));
        Assert.Equal("Teal", viewModel.Settings.FindGroupColor("Office"));
        Assert.Equal("Teal", viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().First().ColorName);
        var saved = SettingsStore.Load(configDir);
        Assert.Equal(["Office"], saved.GroupColors.Keys);
        Assert.Equal(["Office", "Alpha", "Zed"], saved.GroupOrder);
    }

    /// <summary>
    ///     Test that removing a group forgets its color and its place in the group order, so a new group
    ///     of the same name starts without them.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroup_ForgetsColorAndPosition()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("a", Group: "Alpha"), new Spec("w", Group: "Work"));
        Assert.True(viewModel.SetGroupColor("Work", "Teal"));
        Assert.True(viewModel.MoveGroup("Work", RepoGroupMove.Up));

        // Act
        Assert.True(viewModel.RemoveGroup("Work"));

        // Assert
        Assert.Empty(viewModel.Settings.GroupColors);
        Assert.Equal(["Alpha"], viewModel.Settings.GroupOrder);
        var saved = SettingsStore.Load(configDir);
        Assert.Empty(saved.GroupColors);
        Assert.Equal(["Alpha"], saved.GroupOrder);

        // Act: a new group of the same name
        Assert.True(viewModel.RepoCards.Single(card => card.RepoName == "w").MoveToGroup("Work"));

        // Assert: no color, alphabetical place
        Assert.Equal(["[Alpha 1]", "a", "[Work 1]", "w"], Describe(viewModel));
        Assert.False(viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Last().HasColor);
    }

    /// <summary>
    ///     Test that a group whose last repo leaves it forgets its color and its place in the group
    ///     order, like a removed group.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_LastRepoLeavesGroup_ForgetsColorAndPosition()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("a", Group: "Alpha"), new Spec("w", Group: "Work"));
        Assert.True(viewModel.SetGroupColor("Work", "Teal"));
        Assert.True(viewModel.MoveGroup("Work", RepoGroupMove.Up));
        var last = viewModel.RepoCards.Single(card => card.RepoName == "w");

        // Act
        Assert.True(last.MoveToGroup(null));

        // Assert
        Assert.Null(viewModel.Settings.FindGroupColor("Work"));
        Assert.Equal(["Alpha"], viewModel.Settings.GroupOrder);
        Assert.Empty(SettingsStore.Load(configDir).GroupColors);

        // Act: back into a group of that name
        Assert.True(last.MoveToGroup("Work"));

        // Assert: it starts afresh
        Assert.Null(last.GroupColorName);
        Assert.Equal(["[Alpha 1]", "a", "[Work 1]", "w"], Describe(viewModel));
    }

    /// <summary>
    ///     Test that applying new settings (as the Settings window does) keeps the group colors and the
    ///     group order.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_KeepsGroupColorsAndOrder()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("a", Group: "Alpha"), new Spec("w", Group: "Work"));
        Assert.True(viewModel.SetGroupColor("Work", "Teal"));
        Assert.True(viewModel.MoveGroup("Work", RepoGroupMove.Up));

        // Act
        viewModel.ApplySettings(new AppSettings { Theme = ThemePreference.Dark });

        // Assert
        Assert.Equal(["[Work 1]", "w", "[Alpha 1]", "a"], Describe(viewModel));
        Assert.Equal("Teal", viewModel.Settings.FindGroupColor("Work"));
        var saved = SettingsStore.Load(configDir);
        Assert.Equal("Teal", saved.GroupColors["Work"]);
        Assert.Equal(["Work", "Alpha"], saved.GroupOrder);
    }

    /// <summary>
    ///     Describes the displayed items as text: a header as <c>[Name count]</c> (with
    ///     <c>collapsed</c> when its cards are hidden) and a card as its repo name.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <returns>One entry per displayed item, in order.</returns>
    private static List<string> Describe(MainWindowViewModel viewModel) =>
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
    /// <param name="LastLaunched">When the repo was last launched, or <see langword="null"/>.</param>
    private sealed record Spec(
        string Name, string? Group = null, bool IsFavorite = false, DateTimeOffset? LastLaunched = null);

    /// <summary>
    ///     Creates a view model over repos described by <paramref name="specs"/>, persisting to a new
    ///     configuration directory.
    /// </summary>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(params Spec[] specs) => CreateViewModel(CreateTempDirectory(), specs);

    /// <summary>
    ///     Creates a view model over repos described by <paramref name="specs"/>.
    /// </summary>
    /// <param name="configDir">The configuration directory settings are persisted to.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(string configDir, params Spec[] specs)
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
                    IsFavorite = spec.IsFavorite,
                    LastLaunchedUtc = spec.LastLaunched
                })
            ]
        };
        var viewModel = Track(new MainWindowViewModel(settings, configDirectory: configDir));

        // The cards' repo state is read in the background at construction; these tests are about the
        // list, so they start once it has settled (and no git still runs in a repo folder when the
        // folders are removed). Waited for on a pool thread, so no synchronization context is needed.
        Task.Run(viewModel.WhenRepoStateSettledAsync).GetAwaiter().GetResult();
        return viewModel;
    }

    /// <summary>
    ///     Remembers a view model the test built, so its background reads have finished and it is disposed
    ///     before the test's folders are removed.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <returns>The same view model.</returns>
    private MainWindowViewModel Track(MainWindowViewModel viewModel)
    {
        _viewModels.Add(viewModel);
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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlGroups-" + Guid.NewGuid().ToString("N"));
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
