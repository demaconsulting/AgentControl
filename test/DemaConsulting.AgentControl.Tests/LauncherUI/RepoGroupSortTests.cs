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

using System.Globalization;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests of sorting a group's repos by name in <see cref="MainWindowViewModel"/>: which repos are
///     sorted, how names are compared, that nothing else moves, when sorting is unavailable and why, and
///     that the new order is saved.
/// </summary>
/// <remarks>
///     Each test uses unique temporary repo and configuration directories. Runs in the
///     <c>RealProcess</c> collection because every card's repo state is read in the background (real git
///     subprocesses) from the moment the view model is built; every view model a test builds is tracked, and
///     its reads have finished (and it is disposed) before the folders are removed.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoGroupSortTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     The view models built by the running test, settled and disposed before the folders go.
    /// </summary>
    private readonly List<MainWindowViewModel> _viewModels = [];

    /// <summary>
    ///     Test that sorting puts the group's own repos in name order ignoring letter case, and that the
    ///     sorted repos take the places the group's repos had in the stored list, so the repos of other
    ///     sections interleaved with them do not move.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SortGroupByName_SortsDirectReposIntoTheirOwnSlots()
    {
        // Arrange: Work's repos are interleaved with a repo of another group and an ungrouped one
        var viewModel = CreateViewModel(
            new Spec("charlie", "Work"),
            new Spec("x1", "Home"),
            new Spec("Bravo", "Work"),
            new Spec("loose"),
            new Spec("alpha", "Work"));

        // Act
        var sorted = WithCulture("en-US", () => viewModel.SortGroupByName("work"));

        // Assert
        Assert.True(sorted);
        Assert.Equal(["alpha", "x1", "Bravo", "loose", "charlie"], viewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(
            ["alpha", "Bravo", "charlie"],
            viewModel.DisplayedRepoCards.Where(card => card.GroupName == "Work").Select(card => card.RepoName));
        Assert.Equal(
            viewModel.RepoCards.Select(card => card.RepoPath),
            viewModel.Settings.RecentRepos.Select(repo => repo.Path));
        Assert.True(viewModel.Settings.HasUserRepoOrder);
    }

    /// <summary>
    ///     Test that only the repos listed directly under the group are sorted: the repos of its subgroups
    ///     and its favorites (listed under Favorites) keep their places, and repos with the same name keep
    ///     their order.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SortGroupByName_LeavesSubgroupsFavoritesAndEqualNamesAlone()
    {
        // Arrange
        var viewModel = CreateViewModel(
            new Dictionary<string, string> { ["Client"] = string.Empty, ["Project"] = "Client" },
            new Spec("zulu", "Client"),
            new Spec("yankee", "Project"),
            new Spec("fav", "Client", IsFavorite: true),
            new Spec("alpha", "Client"),
            new Spec("Alpha", "Client"),
            new Spec("abe", "Project"));
        var firstAlpha = viewModel.RepoCards.Single(card => card.RepoName == "alpha");
        var secondAlpha = viewModel.RepoCards.Single(card => card.RepoName == "Alpha");

        // Act
        var sorted = WithCulture("en-US", () => viewModel.SortGroupByName("Client"));

        // Assert: Client's own repos alpha, Alpha (same name, kept in order), zulu; the rest stay put
        Assert.True(sorted);
        Assert.Equal(["alpha", "yankee", "fav", "Alpha", "zulu", "abe"], viewModel.RepoCards.Select(card => card.RepoName));
        Assert.Same(firstAlpha, viewModel.RepoCards[0]);
        Assert.Same(secondAlpha, viewModel.RepoCards[3]);
        Assert.Equal(
            ["Favorites", "Client", "Project"],
            viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Select(header => header.Name));
    }

    /// <summary>
    ///     Test that names are compared with the rules of the current culture: a name starting with an
    ///     accented letter sorts next to the plain letter in English but after Z in Swedish.
    /// </summary>
    [Theory]
    [InlineData("en-US", new[] { "alpha", "\u00C4rlig", "Zeta" })]
    [InlineData("sv-SE", new[] { "alpha", "Zeta", "\u00C4rlig" })]
    public void MainWindowViewModel_SortGroupByName_FollowsTheCurrentCulture(string culture, string[] expected)
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("Zeta", "Work"), new Spec("\u00C4rlig", "Work"), new Spec("alpha", "Work"));

        // Act
        var sorted = WithCulture(culture, () => viewModel.SortGroupByName("Work"));

        // Assert
        Assert.True(sorted);
        Assert.Equal(expected, viewModel.RepoCards.Select(card => card.RepoName));
    }

    /// <summary>
    ///     Test that the sorted order is saved: a view model started from the saved settings lists the
    ///     repos in the sorted order.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_SortGroupByName_SavesTheNewOrder()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var viewModel = CreateViewModel(configDir, new Spec("b", "Work"), new Spec("c", "Work"), new Spec("a", "Work"));

        // Act
        WithCulture("en-US", () => viewModel.SortGroupByName("Work"));
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir);

        // Assert
        Assert.Equal(["a", "b", "c"], saved.RecentRepos.Select(repo => Path.GetFileName(repo.Path)));
        Assert.True(saved.HasUserRepoOrder);
    }

    /// <summary>
    ///     Test that sorting is refused with an explanation, and changes nothing, for a group with fewer
    ///     than two repos of its own (favorites and subgroup repos do not count), for repos that are in
    ///     name order already, and for Favorites and Ungrouped.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SortGroupByName_NothingToSort_ExplainsAndChangesNothing()
    {
        // Arrange: Single has one repo of its own; Sorted is in order already
        var viewModel = CreateViewModel(
            new Spec("b", "Single"),
            new Spec("a", "Single", IsFavorite: true),
            new Spec("a", "Sorted"),
            new Spec("b", "Sorted"));
        var before = viewModel.RepoCards.Select(card => card.RepoPath).ToList();

        // Act
        var results = WithCulture("en-US", () => new[]
        {
            viewModel.SortGroupByName("Single"),
            viewModel.SortGroupByName("Sorted"),
            viewModel.SortGroupByName("Favorites"),
            viewModel.SortGroupByName("Ungrouped"),
            viewModel.SortGroupByName("No such group")
        });

        // Assert
        Assert.All(results, Assert.False);
        Assert.Equal(before, viewModel.RepoCards.Select(card => card.RepoPath));
        Assert.Contains("fewer than two", viewModel.GetGroupSortDisabledReason("Single"));
        Assert.Contains("already sorted", WithCulture("en-US", () => viewModel.GetGroupSortDisabledReason("Sorted")));
        Assert.Contains("cannot be sorted", viewModel.GetGroupSortDisabledReason("Favorites"));
        Assert.False(viewModel.CanSortGroup("Ungrouped"));
    }

    /// <summary>
    ///     Test that sorting is refused with the filter explanation while the filter has text, and is
    ///     available again when it is cleared.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_SortGroupByName_WhileFiltering_IsRefusedUntilFilterCleared()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("b", "Work"), new Spec("a", "Work"));
        viewModel.FilterText = "a";

        // Act
        var whileFiltering = WithCulture("en-US", () => viewModel.SortGroupByName("Work"));
        var reason = viewModel.GetGroupSortDisabledReason("Work");
        viewModel.FilterText = string.Empty;

        // Assert
        Assert.False(whileFiltering);
        Assert.Equal(MainWindowViewModel.GroupSortDisabledWhileFilteringMessage, reason);
        Assert.True(WithCulture("en-US", () => viewModel.CanSortGroup("Work")));
        Assert.Equal(["b", "a"], viewModel.RepoCards.Select(card => card.RepoName));
    }

    /// <summary>
    ///     Test that a group header's "Sort by name" is available only while sorting would change the
    ///     order, and that executing it sorts the group and then makes it unavailable.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GroupHeaderSortCommand_FollowsAvailabilityAndSorts()
    {
        // Arrange
        var viewModel = CreateViewModel(new Spec("b", "Work"), new Spec("a", "Work"), new Spec("c", "Home"));
        var work = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(header => header.Name == "Work");
        var home = viewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(header => header.Name == "Home");

        // Assert: Work can be sorted, Home has a single repo
        Assert.True(work.SortByNameCommand.CanExecute(null));
        Assert.False(home.SortByNameCommand.CanExecute(null));
        Assert.Contains("fewer than two", home.SortByNameTooltip);

        // Act
        WithCulture("en-US", () => work.SortByNameCommand.Execute(null));

        // Assert
        Assert.Equal(["a", "b", "c"], viewModel.RepoCards.Select(card => card.RepoName));
        Assert.Equal(["a", "b"], viewModel.DisplayedRepoCards.Where(card => card.GroupName == "Work").Select(card => card.RepoName));
        Assert.False(work.SortByNameCommand.CanExecute(null));
        Assert.Contains("already sorted", work.SortByNameTooltip);
    }

    /// <summary>
    ///     Runs an action with the current culture set to a culture, restoring the previous one after.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="culture">The culture's name.</param>
    /// <param name="action">The action.</param>
    /// <returns>What the action returned.</returns>
    private static T WithCulture<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    ///     Runs an action with the current culture set to a culture, restoring the previous one after.
    /// </summary>
    /// <param name="culture">The culture's name.</param>
    /// <param name="action">The action.</param>
    private static void WithCulture(string culture, Action action) =>
        WithCulture(culture, () =>
        {
            action();
            return true;
        });

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
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(params Spec[] specs) => CreateViewModel(CreateTempDirectory(), specs);

    /// <summary>
    ///     Creates a view model over repos described by <paramref name="specs"/> and a stored hierarchy.
    /// </summary>
    /// <param name="parents">The stored hierarchy.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(Dictionary<string, string> parents, params Spec[] specs) =>
        CreateViewModel(CreateTempDirectory(), parents, specs);

    /// <summary>
    ///     Creates a view model over repos described by <paramref name="specs"/>.
    /// </summary>
    /// <param name="configDir">The configuration directory settings are persisted to.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(string configDir, params Spec[] specs) =>
        CreateViewModel(configDir, new Dictionary<string, string>(), specs);

    /// <summary>
    ///     Creates a view model over repos described by <paramref name="specs"/>.
    /// </summary>
    /// <param name="configDir">The configuration directory settings are persisted to.</param>
    /// <param name="parents">The stored hierarchy.</param>
    /// <param name="specs">The repos, in the user's stored order.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(string configDir, Dictionary<string, string> parents, params Spec[] specs)
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
            GroupParents = new Dictionary<string, string>(parents, RepoGroupName.Comparer)
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
        var path = Path.Combine(Path.GetTempPath(), "AgentControlGroupSort-" + Guid.NewGuid().ToString("N"));
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
