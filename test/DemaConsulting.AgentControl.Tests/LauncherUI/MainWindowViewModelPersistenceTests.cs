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
///     Tests that every change the user makes in the main window is saved before it is applied: when
///     the settings file cannot be written, the in-memory settings, the displayed list and the file
///     all keep the state they had, the user is told, and a later, unrelated save never writes the
///     change that failed.
/// </summary>
/// <remarks>
///     A failing save is produced by making <c>settings.json</c> a directory, which no write can
///     replace; deleting that directory makes saving work again. Runs in the <c>RealProcess</c>
///     collection because every card's eager refresh spawns real git subprocesses.
/// </remarks>
[Collection("RealProcess")]
public sealed class MainWindowViewModelPersistenceTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that marking a favorite whose save fails changes nothing - not the card, the settings
    ///     or the list - reports the failure, tells the star to show the real state, and that a later
    ///     unrelated save does not write the favorite that failed.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FavoriteSaveFails_ChangesNothingAndLaterSaveDoesNotPersistIt()
    {
        // Arrange: two repos, and a settings file that cannot be replaced
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        var beta = fixture.Card("beta");
        var shownBefore = Describe(fixture.ViewModel);
        var starNotifications = 0;
        alpha.PropertyChanged += (_, e) => starNotifications += e.PropertyName == nameof(alpha.IsFavorite) ? 1 : 0;
        fixture.BreakSaving();

        // Act
        alpha.IsFavorite = true;

        // Assert: nothing changed, the user was told once, and the star was told to re-read its value
        Assert.False(alpha.IsFavorite);
        Assert.False(fixture.Settings.RecentRepos.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Equal(shownBefore, Describe(fixture.ViewModel));
        Assert.Single(fixture.Failures);
        Assert.Contains("not applied", fixture.Failures[0], StringComparison.Ordinal);
        Assert.True(starNotifications > 0);

        // Act: saving works again and an unrelated change is saved
        fixture.RestoreSaving();
        Assert.True(beta.SetCardColor("Teal"));

        // Assert: the file holds the color but not the favorite that failed earlier
        var saved = SettingsStore.Load(fixture.ConfigDirectory);
        Assert.Equal("Teal", saved.RecentRepos.Single(repo => repo.Path == beta.RepoPath).CardColor);
        Assert.False(saved.RecentRepos.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.False(alpha.IsFavorite);

        // Act: the same change now succeeds
        alpha.IsFavorite = true;

        // Assert
        Assert.True(alpha.IsFavorite);
        Assert.True(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Single(repo => repo.Path == alpha.RepoPath).IsFavorite);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that moving a repo into a group whose save fails changes nothing and reports the
    ///     failure.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_MoveToGroupSaveFails_ChangesNothingAndReports()
    {
        // Arrange
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        fixture.BreakSaving();

        // Act
        var moved = alpha.MoveToGroup("Work");

        // Assert
        Assert.False(moved);
        Assert.Null(alpha.GroupName);
        Assert.All(fixture.Settings.RecentRepos, repo => Assert.Null(repo.GroupName));
        Assert.False(fixture.ViewModel.HasGroups);
        Assert.Empty(fixture.ViewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>());
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that choosing a card color whose save fails changes nothing and reports the failure.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CardColorSaveFails_ChangesNothingAndReports()
    {
        // Arrange
        var fixture = CreateFixture("alpha");
        var alpha = fixture.Card("alpha");
        fixture.BreakSaving();

        // Act
        var chosen = alpha.SetCardColor("Teal");

        // Assert
        Assert.False(chosen);
        Assert.Null(alpha.CardColorName);
        Assert.Null(fixture.Settings.RecentRepos.Single().CardColor);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that a recorded launch whose save fails is not recorded (the stamp stays as it was,
    ///     the list is not re-sorted) and that the failure is reported as a settings problem.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_LaunchRecordSaveFails_StampNotRecordedAndReports()
    {
        // Arrange: a repo with no pin (the launch does not need a package source) and a custom tool
        var fixture = CreateFixture(
            new AppSettings
            {
                AgentTool = AgentToolKind.Custom,
                CustomAgentCommand = OperatingSystem.IsWindows() ? "cmd /c exit 0" : "true"
            },
            "alpha");
        var alpha = fixture.Card("alpha");
        var recorded = false;
        alpha.LaunchRecorded += (_, _) => recorded = true;
        fixture.BreakSaving();

        // Act
        await alpha.LaunchCommand.ExecuteAsync();

        // Assert
        Assert.Null(alpha.LastLaunchedUtc);
        Assert.Null(fixture.Settings.RecentRepos.Single().LastLaunchedUtc);
        Assert.False(recorded);
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that collapsing a section whose save fails leaves it expanded, whether the request
    ///     comes from the view model API (which throws, documented) or from the header's own button
    ///     (which reports instead, because it is a button the user pressed).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CollapseSaveFails_SectionStaysExpanded()
    {
        // Arrange: one group with one repo
        var fixture = CreateFixture(("alpha", "Work"), ("beta", null));
        var header = fixture.ViewModel.DisplayedItems.OfType<RepoGroupHeaderViewModel>().Single(h => h.Name == "Work");
        fixture.BreakSaving();

        // Act / Assert: the API throws and changes nothing
        Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.ToggleGroupCollapsed("Work"));
        Assert.False(fixture.ViewModel.IsGroupCollapsed("Work"));
        Assert.Empty(fixture.Settings.CollapsedGroups);

        // Act: the header's button reports instead of throwing
        header.ToggleCommand.Execute(null);

        // Assert
        Assert.False(header.IsCollapsed);
        Assert.False(fixture.ViewModel.IsGroupCollapsed("Work"));
        Assert.Single(fixture.Failures);
    }

    /// <summary>
    ///     Test that renaming a group whose save fails leaves the group, its repos and its collapsed
    ///     state as they were.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RenameGroupSaveFails_GroupUnchanged()
    {
        // Arrange: a collapsed group
        var fixture = CreateFixture(("alpha", "Work"), ("beta", "Work"));
        fixture.ViewModel.ToggleGroupCollapsed("Work");
        fixture.BreakSaving();

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.RenameGroup("Work", "Office"));
        Assert.Equal(["Work"], fixture.ViewModel.GroupNames);
        Assert.All(fixture.Settings.RecentRepos, repo => Assert.Equal("Work", repo.GroupName));
        Assert.Equal(["Work"], fixture.Settings.CollapsedGroups);
        Assert.True(fixture.ViewModel.IsGroupCollapsed("Work"));
    }

    /// <summary>
    ///     Test that removing a group whose save fails leaves the group and its collapsed state as they
    ///     were.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveGroupSaveFails_GroupUnchanged()
    {
        // Arrange
        var fixture = CreateFixture(("alpha", "Work"), ("beta", null));
        fixture.ViewModel.ToggleGroupCollapsed("Work");
        fixture.BreakSaving();

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.RemoveGroup("Work"));
        Assert.Equal(["Work"], fixture.ViewModel.GroupNames);
        Assert.Equal("Work", fixture.Card("alpha").GroupName);
        Assert.Equal(["Work"], fixture.Settings.CollapsedGroups);
    }

    /// <summary>
    ///     Test that adding a repo whose save fails adds nothing: not to the cards, the displayed list
    ///     or the settings.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepoSaveFails_NothingAdded()
    {
        // Arrange
        var fixture = CreateFixture("alpha");
        var newRepo = CreateDirectory("gamma");
        fixture.BreakSaving();

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.AddRepo(newRepo));
        Assert.Single(fixture.ViewModel.RepoCards);
        Assert.Single(fixture.Settings.RecentRepos);
        Assert.Single(fixture.ViewModel.DisplayedRepoCards);

        // Act: saving works again, so the same repo can be added
        fixture.RestoreSaving();

        // Assert
        Assert.True(fixture.ViewModel.AddRepo(newRepo));
        Assert.Equal(2, fixture.ViewModel.RepoCards.Count);
        Assert.Equal(2, SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Count);
    }

    /// <summary>
    ///     Test that removing a repo whose save fails keeps it in the list, still working (it is not
    ///     retired), and that a successful removal retires the card so that it can no longer report
    ///     anything.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveRepoSaveFails_RepoStaysListedAndWorking()
    {
        // Arrange
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        fixture.BreakSaving();

        // Act / Assert: a failed removal throws and changes nothing
        Assert.Throws<InvalidOperationException>(() => fixture.ViewModel.RemoveRepo(alpha));
        Assert.Contains(alpha, fixture.ViewModel.RepoCards);
        Assert.Equal(2, fixture.Settings.RecentRepos.Count);

        // Act: saving works again; the card was not retired, so it still works and can be removed
        fixture.RestoreSaving();
        alpha.IsFavorite = true;
        fixture.ViewModel.RemoveRepo(alpha);

        // Assert
        Assert.True(alpha.IsFavorite);
        Assert.DoesNotContain(alpha, fixture.ViewModel.RepoCards);
        Assert.Single(SettingsStore.Load(fixture.ConfigDirectory).RecentRepos);
    }

    /// <summary>
    ///     Test that a removed repo's card is retired: after the removal it can no longer report
    ///     anything to the user (here: an error that the same action raised before the removal).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveRepo_RetiresTheCard()
    {
        // Arrange: a card whose select-package action reports an error (no package source configured)
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");
        var errors = new List<string>();
        alpha.ErrorOccurred += (_, message) => errors.Add(message);
        alpha.SelectPackageCommand.Execute(null);
        Assert.Single(errors);

        // Act: the repo is removed, then the same action is tried again
        fixture.ViewModel.RemoveRepo(alpha);
        alpha.SelectPackageCommand.Execute(null);

        // Assert: nothing was reported for the removed card
        Assert.Single(errors);
        Assert.DoesNotContain(alpha, fixture.ViewModel.RepoCards);
    }

    /// <summary>
    ///     Test that every change that does save is written to the file as well as applied (the
    ///     success side of the failure tests above).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ChangesThatSave_AreAppliedAndWritten()
    {
        // Arrange
        var fixture = CreateFixture("alpha", "beta");
        var alpha = fixture.Card("alpha");

        // Act
        alpha.IsFavorite = true;
        Assert.True(alpha.MoveToGroup("Work"));
        Assert.True(alpha.SetCardColor("teal"));

        // Assert
        var saved = SettingsStore.Load(fixture.ConfigDirectory).RecentRepos.Single(repo => repo.Path == alpha.RepoPath);
        Assert.True(saved.IsFavorite);
        Assert.Equal("Work", saved.GroupName);
        Assert.Equal("Teal", saved.CardColor);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>
    ///     Describes the displayed items as text, to compare the list before and after a change.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <returns>One entry per displayed item: a header's name, or a card's repo name.</returns>
    private static List<string> Describe(MainWindowViewModel viewModel) =>
        [.. viewModel.DisplayedItems.Select(item => item switch
        {
            RepoGroupHeaderViewModel header => $"[{header.Name}]",
            RepoCardViewModel card => card.RepoName,
            _ => throw new InvalidOperationException("Unexpected list item.")
        })];

    /// <summary>
    ///     Creates a fixture over repos with the given names and no group.
    /// </summary>
    /// <param name="names">The repo folder names.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(params string[] names) =>
        CreateFixture(new AppSettings(), [.. names.Select(name => (name, (string?)null))]);

    /// <summary>
    ///     Creates a fixture over repos in the given groups.
    /// </summary>
    /// <param name="repos">The repo folder names and their groups.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(params (string Name, string? Group)[] repos) =>
        CreateFixture(new AppSettings(), repos);

    /// <summary>
    ///     Creates a fixture over repos with the given names and no group, and the given settings.
    /// </summary>
    /// <param name="settings">The settings to start from (without repos).</param>
    /// <param name="names">The repo folder names.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(AppSettings settings, params string[] names) =>
        CreateFixture(settings, [.. names.Select(name => (name, (string?)null))]);

    /// <summary>
    ///     Creates a fixture.
    /// </summary>
    /// <param name="settings">The settings to start from; its repo list is replaced.</param>
    /// <param name="repos">The repo folder names and their groups.</param>
    /// <returns>The fixture.</returns>
    private Fixture CreateFixture(AppSettings settings, (string Name, string? Group)[] repos)
    {
        var configDirectory = CreateDirectory("config");
        settings.RecentRepos =
        [
            .. repos.Select(repo => new RecentRepo { Path = CreateDirectory(repo.Name), GroupName = repo.Group })
        ];
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDirectory);
        var failures = new List<string>();
        viewModel.SettingsSaveFailed += (_, message) => failures.Add(message);
        return new Fixture(viewModel, settings, configDirectory, failures);
    }

    /// <summary>
    ///     Creates a folder named <paramref name="name"/> under a unique temporary parent that is
    ///     removed when the test class is disposed.
    /// </summary>
    /// <param name="name">The folder name (also the display name of a repo).</param>
    /// <returns>The folder's path.</returns>
    private string CreateDirectory(string name)
    {
        var parent = Path.Combine(Path.GetTempPath(), "agentcontrol_persistence_" + Guid.NewGuid().ToString("N"));
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
                // A terminal started by a launch test may still have the folder as its working
                // directory; leaving a temporary folder behind is harmless
            }
        }
    }

    /// <summary>
    ///     The view model under test with what the tests need to observe and to break saving.
    /// </summary>
    /// <param name="ViewModel">The view model.</param>
    /// <param name="Settings">The live settings it was created over.</param>
    /// <param name="ConfigDirectory">The directory its settings are saved to.</param>
    /// <param name="Failures">The messages of <see cref="MainWindowViewModel.SettingsSaveFailed"/>.</param>
    private sealed record Fixture(
        MainWindowViewModel ViewModel, AppSettings Settings, string ConfigDirectory, List<string> Failures)
    {
        /// <summary>
        ///     Finds a repo's card by the repo's folder name.
        /// </summary>
        /// <param name="name">The folder name.</param>
        /// <returns>The card.</returns>
        public RepoCardViewModel Card(string name) => ViewModel.RepoCards.Single(card => card.RepoName == name);

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
    }
}
