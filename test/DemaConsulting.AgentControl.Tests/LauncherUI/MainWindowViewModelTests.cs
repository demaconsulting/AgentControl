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

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;
using DemaConsulting.AgentControl.Tests.AgentToolLauncher;
using DemaConsulting.AgentControl.Tests.GitIntegration;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="MainWindowViewModel"/>.
/// </summary>
/// <remarks>
///     Every test uses a unique temporary configuration directory for settings persistence and
///     unique temporary repo directories, so tests are hermetic and can run in parallel without
///     interfering with each other or with a real <c>%APPDATA%\AgentControl\</c>. Runs in the
///     <c>RealProcess</c> collection (disabled parallelization) because constructing a view
///     model with recent repos now spawns real git subprocesses via <see cref="GitStub"/> or the
///     system git executable as part of each card's eager <c>RefreshCheap()</c>.
/// </remarks>
[Collection("RealProcess")]
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that the constructor populates RepoCards from the settings' recent-repos list.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_SettingsHaveRecentRepos_PopulatesRepoCards()
    {
        // Arrange: settings with two recent repos
        var repoA = CreateTempDirectory();
        var repoB = CreateTempDirectory();
        var settings = new AppSettings
        {
            RecentRepos =
            [
                new RecentRepo { Path = repoA },
                new RecentRepo { Path = repoB }
            ]
        };

        // Act: construct the view model
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Assert: both repos appear as cards, in order
        Assert.Equal(2, viewModel.RepoCards.Count);
        Assert.Equal(repoA, viewModel.RepoCards[0].RepoPath);
        Assert.Equal(repoB, viewModel.RepoCards[1].RepoPath);
    }

    /// <summary>
    ///     Test that AddRepo appends a new card at the end of the list and persists the
    ///     updated settings to disk.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_ValidNewPath_InsertsCardAndPersistsSettings()
    {
        // Arrange: an empty recent-repos list and a config directory to persist to
        var configDir = CreateTempDirectory();
        var newRepo = CreateRepoDirectory();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: configDir);

        // Act: add the new repo
        var added = viewModel.AddRepo(newRepo);

        // Assert: the card was added, and settings were persisted
        Assert.True(added);
        var card = Assert.Single(viewModel.RepoCards);
        Assert.Equal(newRepo, card.RepoPath);

        var reloaded = SettingsStore.Load(configDir);
        var recentRepo = Assert.Single(reloaded.RecentRepos);
        Assert.Equal(newRepo, recentRepo.Path);
    }

    /// <summary>
    ///     Test that AddRepo returns false and does not modify the list when the path does not
    ///     exist.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_PathDoesNotExist_ReturnsFalse()
    {
        // Arrange: a path that does not exist on disk
        var missingPath = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_repo_" + Guid.NewGuid());
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());

        // Act: attempt to add the missing path
        var added = viewModel.AddRepo(missingPath);

        // Assert: not added
        Assert.False(added);
        Assert.Empty(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that AddRepo returns false for a path already present in the recent-repos list.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_DuplicatePath_ReturnsFalse()
    {
        // Arrange: a repo already in the recent-repos list
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act: attempt to add the same path again
        var added = viewModel.AddRepo(repoPath);

        // Assert: not added again
        Assert.False(added);
        Assert.Single(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that a folder without a <c>.git</c> folder or file is refused as not a Git repository
    ///     and nothing is added.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_FolderWithoutGit_IsRefusedAsNotAGitRepository()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var plainFolder = CreateTempDirectory();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: configDir);

        // Act
        var added = viewModel.AddRepo(plainFolder);
        var outcomes = viewModel.AddRepos([plainFolder], null);

        // Assert
        Assert.False(added);
        Assert.Equal(AddRepoResult.NotAGitRepository, Assert.Single(outcomes).Result);
        Assert.Empty(viewModel.RepoCards);
        Assert.Empty(viewModel.Settings.RecentRepos);
    }

    /// <summary>
    ///     Test that a subfolder of a Git repository is refused: only the repository's top folder can be
    ///     added, whatever group it is added to.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_SubfolderOfARepository_IsRefused()
    {
        // Arrange
        var repo = CreateRepoDirectory();
        var subfolder = Path.Combine(repo, "src");
        Directory.CreateDirectory(subfolder);
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());

        // Act
        var plain = viewModel.AddRepo(subfolder);
        var grouped = viewModel.AddRepoToGroup(subfolder, "Work");

        // Assert
        Assert.False(plain);
        Assert.Equal(AddRepoResult.NotAGitRepository, grouped);
        Assert.Empty(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that a folder with a <c>.git</c> file (a linked worktree or a submodule) is the top
    ///     folder of a Git repository and is added.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_FolderWithGitFile_IsAdded()
    {
        // Arrange
        var worktree = CreateTempDirectory();
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../main/.git/worktrees/feature\n");
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());

        // Act
        var added = viewModel.AddRepo(worktree);

        // Assert
        Assert.True(added);
        Assert.Equal(worktree, Assert.Single(viewModel.RepoCards).RepoPath);
    }

    /// <summary>
    ///     Test that a file (as dropped from the file manager) is not added and is reported as not a Git
    ///     repository, while a path that does not exist is reported as missing.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepos_ExistingFile_IsNotAGitRepository()
    {
        // Arrange
        var file = Path.Combine(CreateTempDirectory(), "notes.txt");
        File.WriteAllText(file, "not a repository");
        var missing = Path.Combine(CreateTempDirectory(), "gone");
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());

        // Act
        var outcomes = viewModel.AddRepos([file, missing], null);

        // Assert
        Assert.Equal(
            [AddRepoResult.NotAGitRepository, AddRepoResult.Missing],
            outcomes.Select(outcome => outcome.Result));
        Assert.Empty(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that repos already in the list are kept as they are even when they are not (or no
    ///     longer) Git repositories: the check applies only to folders being added.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_ListedFolderWithoutGit_IsKept()
    {
        // Arrange: a listed repo whose folder has no .git
        var plainFolder = CreateTempDirectory();
        var configDir = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = plainFolder }] };

        // Act
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);
        var again = viewModel.AddRepos([plainFolder], null);
        Assert.True(viewModel.AddRepo(CreateRepoDirectory()));

        // Assert: still listed (and saved with the next change), and reported as already listed
        Assert.Equal(plainFolder, viewModel.RepoCards[0].RepoPath);
        Assert.Equal(AddRepoResult.AlreadyListed, Assert.Single(again).Result);
        Assert.Contains(SettingsStore.Load(configDir).RecentRepos, repo => repo.Path == plainFolder);
    }

    /// <summary>
    ///     Test that adding several folders reports an outcome for each, in order, adds the repositories
    ///     and skips the others, and handles a folder given twice once.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepos_MixedPaths_ReportsAnOutcomePerPath()
    {
        // Arrange: a listed repo, two new repositories, a plain folder and a missing path
        var listed = CreateRepoDirectory();
        var first = CreateRepoDirectory();
        var second = CreateRepoDirectory();
        var plainFolder = CreateTempDirectory();
        var missing = Path.Combine(CreateTempDirectory(), "gone");
        var configDir = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = listed }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);

        // Act
        var outcomes = viewModel.AddRepos([first, listed, plainFolder, missing, second, first.ToUpperInvariant(), "  "], null);

        // Assert
        Assert.Equal([first, listed, plainFolder, missing, second], outcomes.Select(outcome => outcome.Path));
        Assert.Equal(
            [
                AddRepoResult.Added, AddRepoResult.AlreadyListed, AddRepoResult.NotAGitRepository, AddRepoResult.Missing,
                AddRepoResult.Added
            ],
            outcomes.Select(outcome => outcome.Result));
        Assert.Equal([listed, first, second], viewModel.RepoCards.Select(card => card.RepoPath));
        Assert.Equal([listed, first, second], SettingsStore.Load(configDir).RecentRepos.Select(repo => repo.Path));
    }

    /// <summary>
    ///     Test that the same folder given with and without a trailing directory separator in one
    ///     multi-folder add is handled once and gives one card, stored without the separator.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepos_SameFolderWithAndWithoutTrailingSeparator_AddsOneCard()
    {
        // Arrange: one repository, written both ways
        var repo = CreateRepoDirectory();
        var withSeparator = repo + Path.DirectorySeparatorChar;
        var configDir = CreateTempDirectory();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: configDir);

        // Act
        var outcomes = viewModel.AddRepos([withSeparator, repo], null);

        // Assert: one outcome, one card and one saved entry, all for the folder without the separator
        Assert.Equal(AddRepoResult.Added, Assert.Single(outcomes).Result);
        Assert.Equal(repo, Assert.Single(viewModel.RepoCards).RepoPath);
        Assert.Equal(repo, Assert.Single(SettingsStore.Load(configDir).RecentRepos).Path);
    }

    /// <summary>
    ///     Test that a listed folder added again with a trailing directory separator is reported as
    ///     already listed and not added twice.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepos_ListedFolderWithTrailingSeparator_IsAlreadyListed()
    {
        // Arrange: a repository already in the list
        var repo = CreateRepoDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repo }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act
        var outcomes = viewModel.AddRepos([repo + Path.DirectorySeparatorChar], null);
        var added = viewModel.AddRepo(repo + Path.DirectorySeparatorChar);

        // Assert
        Assert.Equal(AddRepoResult.AlreadyListed, Assert.Single(outcomes).Result);
        Assert.False(added);
        Assert.Single(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that a folder stored before paths were normalized, with a trailing directory separator,
    ///     is kept as stored and still recognized when the folder is added again without it.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_StoredWithTrailingSeparator_IsAlreadyListed()
    {
        // Arrange: an entry saved in the old form, with a trailing separator
        var repo = CreateRepoDirectory();
        var stored = repo + Path.DirectorySeparatorChar;
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = stored }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act
        var added = viewModel.AddRepo(repo);

        // Assert: not added again, and the stored entry is left as it was
        Assert.False(added);
        Assert.Equal(stored, Assert.Single(viewModel.RepoCards).RepoPath);
    }

    /// <summary>
    ///     Test that the repo-path identity ignores a trailing directory separator and letter case but
    ///     keeps a file-system root valid.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_NormalizeRepoPath_TrailingSeparatorAndRoot_TrimsOnlyTheSeparator()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "agentcontrol_identity_" + Guid.NewGuid());
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        // Act
        var trimmed = MainWindowViewModel.NormalizeRepoPath(folder + Path.DirectorySeparatorChar);
        var normalizedRoot = MainWindowViewModel.NormalizeRepoPath(root);
        var same = MainWindowViewModel.IsSameRepoPath(folder.ToUpperInvariant(), folder + Path.DirectorySeparatorChar);

        // Assert
        Assert.Equal(Path.GetFullPath(folder), trimmed);
        Assert.Equal(Path.GetFullPath(root), normalizedRoot);
        Assert.True(same);
    }

    /// <summary>
    ///     Test that adding several folders into a collapsed group adds them all to the group, moves a
    ///     listed repo into it, and opens the group once so the repos can be seen.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepos_IntoCollapsedGroup_OpensItOnce()
    {
        // Arrange: Work holds one repo and is collapsed; another repo is ungrouped
        var member = CreateRepoDirectory();
        var loose = CreateRepoDirectory();
        var settings = new AppSettings
        {
            RecentRepos = [new RecentRepo { Path = member, GroupName = "Work" }, new RecentRepo { Path = loose }],
            CollapsedGroups = ["Work"]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var fresh = CreateRepoDirectory();

        // Act: the group in other letter case
        var outcomes = viewModel.AddRepos([fresh, loose], "work");

        // Assert
        Assert.Equal([AddRepoResult.Added, AddRepoResult.MovedExisting], outcomes.Select(outcome => outcome.Result));
        Assert.All(viewModel.RepoCards, card => Assert.Equal("Work", card.GroupName));
        Assert.False(viewModel.IsGroupCollapsed("Work"));
        Assert.Equal(3, viewModel.DisplayedRepoCards.Count);
        Assert.Throws<ArgumentException>(() => viewModel.AddRepos([fresh], RepoGroupName.FavoritesName));
    }

    /// <summary>
    ///     Test that an add in which every folder was added needs no message.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DescribeAddOutcomes_AllAdded_ReturnsNull()
    {
        // Arrange
        AddRepoOutcome[] outcomes = [new(@"C:\repos\a", AddRepoResult.Added), new(@"C:\repos\b", AddRepoResult.Added)];

        // Act / Assert
        Assert.Null(MainWindowViewModel.DescribeAddOutcomes(outcomes, null));
        Assert.Null(MainWindowViewModel.DescribeAddOutcomes([outcomes[0]], null));
        Assert.Null(MainWindowViewModel.DescribeAddOutcomes([], null));
    }

    /// <summary>
    ///     Test that adding one folder keeps the single-add messages, each naming the folder and saying
    ///     why it was not added (or that it was moved into the group).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DescribeAddOutcomes_Single_KeepsTheSingleAddMessages()
    {
        // Arrange
        const string path = @"C:\repos\a";

        // Act
        static string? Describe(AddRepoResult result, string? problem = null) =>
            MainWindowViewModel.DescribeAddOutcomes([new AddRepoOutcome(path, result, problem)], "Work");

        // Assert
        Assert.Equal($"'{path}' is already in the list, so it was moved into the group \"Work\".", Describe(AddRepoResult.MovedExisting));
        Assert.Equal($"'{path}' could not be added: it is already in the list.", Describe(AddRepoResult.AlreadyListed));
        Assert.Equal($"'{path}' could not be added: it does not exist.", Describe(AddRepoResult.Missing));
        Assert.Equal(
            $"'{path}' could not be added: it is not the top folder of a Git repository (it has no .git folder or file).",
            Describe(AddRepoResult.NotAGitRepository));
        Assert.Equal($"'{path}' could not be added. Disk full.", Describe(AddRepoResult.SaveFailed, "Disk full."));
        Assert.Null(Describe(AddRepoResult.MoveFailed));
    }

    /// <summary>
    ///     Test that several folders get one summary: how many were added, then one line per kind of
    ///     outcome naming the folders, at most five names per line followed by how many more.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DescribeAddOutcomes_SeveralMixed_OneSummaryByOutcomeWithAtMostFiveNames()
    {
        // Arrange: one added, seven not repositories (given with a trailing separator), one missing,
        // one already listed; the paths are native to the platform so their last segment is the name
        var repos = Path.Combine(Path.GetTempPath(), "repos");
        var docs = Path.Combine(Path.GetTempPath(), "docs");
        var outcomes = new List<AddRepoOutcome> { new(Path.Combine(repos, "added"), AddRepoResult.Added) };
        outcomes.AddRange(Enumerable.Range(1, 7).Select(
            i => new AddRepoOutcome(Path.Combine(docs, $"plain{i}") + Path.DirectorySeparatorChar, AddRepoResult.NotAGitRepository)));
        outcomes.Add(new AddRepoOutcome(Path.Combine(repos, "gone"), AddRepoResult.Missing));
        outcomes.Add(new AddRepoOutcome(Path.Combine(repos, "known"), AddRepoResult.AlreadyListed));

        // Act
        var message = MainWindowViewModel.DescribeAddOutcomes(outcomes, null);

        // Assert
        Assert.NotNull(message);
        var lines = message.Split('\n');
        Assert.Equal("Added 1 of 10 folders.", lines[0]);
        Assert.Contains("Already in the list: known.", lines);
        Assert.Contains(
            "Not the top folder of a Git repository (no .git folder or file): plain1, plain2, plain3, plain4, plain5 and 2 more.",
            lines);
        Assert.Contains("Not found: gone.", lines);
        Assert.Equal(4, lines.Length);
    }

    /// <summary>
    ///     Test that ApplySettings preserves the existing recent-repos list while adopting the
    ///     updated settings' other fields, and persists the result.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields()
    {
        // Arrange: a view model with one recent repo
        var configDir = CreateTempDirectory();
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);

        // Act: apply an updated settings object with a new package source path
        var updated = new AppSettings { PackageSourcePath = @"\\share\packages" };
        viewModel.ApplySettings(updated);

        // Assert: the recent-repos list survived, the new field was adopted, and both were
        // persisted to disk
        var card = Assert.Single(viewModel.RepoCards);
        Assert.Equal(repoPath, card.RepoPath);
        Assert.Equal(@"\\share\packages", Assert.Single(viewModel.Settings.GetEffectiveSources()).Location);

        var reloaded = SettingsStore.Load(configDir);
        Assert.Equal(@"\\share\packages", Assert.Single(reloaded.Sources).Location);
        Assert.Null(reloaded.PackageSourcePath);
        var recentRepo = Assert.Single(reloaded.RecentRepos);
        Assert.Equal(repoPath, recentRepo.Path);
    }

    /// <summary>
    ///     Test that a change of the keep-shell-open setting applied through ApplySettings is used by
    ///     the next launch of an existing card, and that the view model hands its process starter and
    ///     shell detector to its cards (so no process is started).
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettings_KeepShellOpenChanged_NextLaunchUsesIt()
    {
        // Arrange: a view model with a recording starter, a deterministic shell and one repo
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings
        {
            AgentTool = AgentToolKind.Custom,
            CustomAgentCommand = "my-agent",
            ShellPreference = "pwsh",
            KeepShellOpen = true,
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };
        var starter = new FakeAgentToolProcessStarter();
        var viewModel = new MainWindowViewModel(
            settings, configDirectory: CreateTempDirectory(),
            processStarter: starter, shellDetector: FakeAgentToolProcessStarter.CreateShellDetector());
        var card = Assert.Single(viewModel.RepoCards);

        // Act: launch, apply settings that clear the choice (as the Settings window's Save does), launch again
        await card.LaunchCommand.ExecuteAsync();
        viewModel.ApplySettings(new AppSettings
        {
            AgentTool = AgentToolKind.Custom,
            CustomAgentCommand = "my-agent",
            ShellPreference = "pwsh",
            KeepShellOpen = false
        });
        await card.LaunchCommand.ExecuteAsync();

        // Assert: the first launch kept the shell open, the next one did not
        Assert.Equal(2, starter.Started.Count);
        Assert.Equal(["-NoLogo", "-NoExit", "-Command", "my-agent"], starter.Started[0].ArgumentList);
        Assert.Equal(["-NoLogo", "-Command", "my-agent"], starter.Started[1].ArgumentList);
    }

    /// <summary>
    ///     Test that DisplayedRepoCards filters by a case-insensitive substring match against the
    ///     repo's display name.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FilterText_MatchesRepoNameCaseInsensitive_FiltersDisplayedRepoCards()
    {
        // Arrange: two repos with distinctive names
        var alphaRepo = CreateNamedTempDirectory("Alpha-Project");
        var betaRepo = CreateNamedTempDirectory("Beta-Project");
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = alphaRepo }, new RecentRepo { Path = betaRepo }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act: filter for "alpha" (mixed case, substring)
        viewModel.FilterText = "aLpHa";

        // Assert: only the alpha repo remains displayed
        var displayed = Assert.Single(viewModel.DisplayedRepoCards);
        Assert.Equal(alphaRepo, displayed.RepoPath);
    }

    /// <summary>
    ///     Test that DisplayedRepoCards filters by a case-insensitive substring match against the
    ///     repo's full path (not just its display name).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FilterText_MatchesRepoPath_FiltersDisplayedRepoCards()
    {
        // Arrange
        var repoPath = CreateTempDirectory();
        var otherRepo = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }, new RecentRepo { Path = otherRepo }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act: filter using a substring unique to repoPath's full path (its temp-path prefix segment)
        viewModel.FilterText = Path.GetFileName(repoPath);

        // Assert
        Assert.Contains(viewModel.DisplayedRepoCards, c => c.RepoPath == repoPath);
        Assert.DoesNotContain(viewModel.DisplayedRepoCards, c => c.RepoPath == otherRepo);
    }

    /// <summary>
    ///     Test that HasNoDisplayedRepos is true and EmptyStateMessage prompts adding a repo
    ///     when no repos have ever been added (as opposed to a filter excluding all of them).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_NoRepoCardsAtAll_HasNoDisplayedReposTrueWithAddRepoMessage()
    {
        // Arrange / Act: a view model with no recent repos configured
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());

        // Assert
        Assert.True(viewModel.HasNoDisplayedRepos);
        Assert.Contains("add your first repository", viewModel.EmptyStateMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that HasNoDisplayedRepos is true and EmptyStateMessage references the filter
    ///     text when repos exist but the current filter matches none of them.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_FilterMatchesNoRepos_HasNoDisplayedReposTrueWithFilterMessage()
    {
        // Arrange: a repo that exists, but a filter that cannot match it
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act
        viewModel.FilterText = "no-such-repo-exists";

        // Assert
        Assert.True(viewModel.HasNoDisplayedRepos);
        Assert.Contains("no-such-repo-exists", viewModel.EmptyStateMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that HasNoDisplayedRepos is false whenever at least one card is displayed.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_HasDisplayedRepos_HasNoDisplayedReposFalse()
    {
        // Arrange / Act
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = CreateTempDirectory() }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Assert
        Assert.False(viewModel.HasNoDisplayedRepos);
    }

    /// <summary>
    ///     Test that DisplayedRepoCards lists favorites above non-favorites and otherwise keeps the
    ///     stored order, whatever the launch times are.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedRepoCards_FavoritesFirstThenStoredOrder()
    {
        // Arrange: a stored user order whose launch times point the other way round
        var firstNonFavorite = CreateTempDirectory();
        var favoriteRepo = CreateTempDirectory();
        var secondNonFavorite = CreateTempDirectory();
        var settings = new AppSettings
        {
            HasUserRepoOrder = true,
            RecentRepos =
            [
                new RecentRepo { Path = firstNonFavorite, LastLaunchedUtc = DateTimeOffset.UtcNow.AddDays(-5) },
                new RecentRepo { Path = favoriteRepo, IsFavorite = true, LastLaunchedUtc = DateTimeOffset.UtcNow.AddDays(-10) },
                new RecentRepo { Path = secondNonFavorite, LastLaunchedUtc = DateTimeOffset.UtcNow }
            ]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act / Assert: the favorite first, then the others in the stored order (not by launch time)
        Assert.Equal(
            [favoriteRepo, firstNonFavorite, secondNonFavorite],
            viewModel.DisplayedRepoCards.Select(card => card.RepoPath));
    }

    /// <summary>
    ///     Test that RemoveRepo removes a card from both RepoCards/DisplayedRepoCards and the
    ///     persisted settings.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_RemoveRepo_RemovesFromCollectionsAndPersistsSettings()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);
        var card = viewModel.RepoCards[0];

        // Act
        viewModel.RemoveRepo(card);

        // Assert
        Assert.Empty(viewModel.RepoCards);
        Assert.Empty(viewModel.DisplayedRepoCards);

        var reloaded = SettingsStore.Load(configDir);
        Assert.Empty(reloaded.RecentRepos);
    }

    /// <summary>
    ///     Test that a card raising RemoveRequested does <em>not</em> by itself remove the card
    ///     from MainWindowViewModel - RemoveRequested is a view-layer wiring point only (the view
    ///     shows a confirmation dialog and calls <see cref="MainWindowViewModel.RemoveRepo"/>
    ///     itself only if the user confirms); the view model must not subscribe to it directly,
    ///     or a decline would be bypassed and the card removed unconditionally.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CardRaisesRemoveRequested_DoesNotRemoveCardWithoutConfirmation()
    {
        // Arrange
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        var card = viewModel.RepoCards[0];

        // Act
        card.RemoveCommand.Execute(null);

        // Assert: raising RemoveRequested alone must not remove the card - only an explicit
        // RemoveRepo call (made by the view, after confirmation) does.
        Assert.Single(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that the constructor's initial refresh pass does not invoke the working-tree
    ///     dirty-check subprocess for any card (only the cheap checks run eagerly at launch).
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_DoesNotInvokeWorkingTreeDirtyCheckSubprocess()
    {
        // Arrange: a git stub that logs every invocation it receives
        var invocationLog = Path.Combine(Path.GetTempPath(), "agentcontrol_main_vm_invocation_log_" + Guid.NewGuid() + ".txt");
        _tempPaths.Add(invocationLog);
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        _tempPaths.Add(stub.Path);
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path, RecentRepos = [new RecentRepo { Path = repoPath }] };

        // Act
        _ = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Assert: "status --porcelain" was never invoked during construction
        var invocations = File.Exists(invocationLog) ? File.ReadAllLines(invocationLog) : [];
        Assert.DoesNotContain(invocations, line => line.StartsWith("status", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that ApplySettings refuses a source location embedding a credential: it throws
    ///     without leaking the secret, keeps the current settings, and writes nothing.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_SourceLocationWithEmbeddedCredential_ThrowsAndKeepsCurrentSettings()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var settings = new AppSettings { PackageSourcePath = CreateTempDirectory() };
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);
        var updated = new AppSettings { PackageSourcePath = "https://user:SentinelSecretValue@feeds.example.com/nuget" };

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => viewModel.ApplySettings(updated));

        // Assert: refused, the live settings are unchanged, and nothing was written
        Assert.DoesNotContain("SentinelSecretValue", ex.Message, StringComparison.Ordinal);
        Assert.Same(settings, viewModel.Settings);
        Assert.False(File.Exists(Path.Combine(configDir, "settings.json")));
    }

    /// <summary>
    ///     Test that a saved theme choice is persisted and handed to the theme applier straight
    ///     away (so it takes effect without a restart), once per save.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_ThemeChanged_PersistsAndAppliesTheme()
    {
        // Arrange: a view model with a recording theme applier
        var configDir = CreateTempDirectory();
        var applied = new List<ThemePreference>();
        var viewModel = new MainWindowViewModel(
            new AppSettings(), configDirectory: configDir, applyTheme: applied.Add);

        // Act: save a settings object that chooses the dark theme
        viewModel.ApplySettings(new AppSettings { Theme = ThemePreference.Dark });

        // Assert: the applier received the choice and it was persisted
        Assert.Equal([ThemePreference.Dark], applied);
        Assert.Equal(ThemePreference.Dark, viewModel.Settings.Theme);
        Assert.Equal(ThemePreference.Dark, SettingsStore.Load(configDir).Theme);
    }

    /// <summary>
    ///     Test that a rejected save never applies the theme it carried.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_SaveFails_DoesNotApplyTheme()
    {
        // Arrange: a configuration "directory" that is really a file, so every write fails
        var blockingFile = Path.Combine(CreateTempDirectory(), "not-a-directory");
        File.WriteAllText(blockingFile, "blocker");
        var applied = new List<ThemePreference>();
        var viewModel = new MainWindowViewModel(
            new AppSettings(), configDirectory: blockingFile, applyTheme: applied.Add);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(
            () => viewModel.ApplySettings(new AppSettings { Theme = ThemePreference.Light }));
        Assert.Empty(applied);
        Assert.Equal(ThemePreference.System, viewModel.Settings.Theme);
    }

    /// <summary>
    ///     Test that a failed settings write leaves the previous settings live, so a later save
    ///     (for example when a repo is marked as a favorite) can never persist the rejected candidate.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive()
    {
        // Arrange: a configuration "directory" that is really a file, so every write fails; a later
        // save (such as toggling a favorite) would otherwise persist the rejected candidate
        var repoPath = CreateTempDirectory();
        var blockingFile = Path.Combine(CreateTempDirectory(), "not-a-directory");
        File.WriteAllText(blockingFile, "blocker");
        var settings = new AppSettings
        {
            GitExecutablePath = "previous-git",
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: blockingFile);
        var rejected = new AppSettings { GitExecutablePath = "rejected-git" };

        // Act
        Assert.Throws<InvalidOperationException>(() => viewModel.ApplySettings(rejected));

        // Assert: the previous instance is still live and keeps its values
        Assert.Same(settings, viewModel.Settings);
        Assert.Equal("previous-git", viewModel.Settings.GitExecutablePath);
        Assert.Single(viewModel.Settings.RecentRepos);
    }

    /// <summary>
    ///     Test that ApplySettings invalidates the shared PackageVersionCache, so a changed
    ///     package-source path is reflected immediately rather than served from a stale cache.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache()
    {
        // Arrange: a repo pinned to 1.0.0, and an initial (empty) package source directory
        var repoPath = CreateTempDirectory();
        RepoPinStore.Save(repoPath, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var initialSourceDir = CreateTempDirectory();
        var settings = new AppSettings
        {
            PackageSourcePath = initialSourceDir,
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        await viewModel.BeginUpgradeRefresh();
        Assert.False(viewModel.RepoCards[0].IsUpgradeAvailable);

        // Act: apply settings pointing at a new package source containing a newer version
        var newSourceDir = CreateTempDirectory();
        CreatePackageZip(newSourceDir, "contoso-agents", "2.0.0");
        var updated = new AppSettings { PackageSourcePath = newSourceDir };
        viewModel.ApplySettings(updated);
        await viewModel.PendingRefresh;

        // Assert: the new source's package is observed immediately, not masked by a cache entry
        // for the old (empty) source directory
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);
        Assert.Equal("2.0.0", viewModel.RepoCards[0].LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that ApplySettings only drops cached results for sources that were edited or
    ///     removed: a card pinned to an unchanged source keeps its cached upgrade answer even
    ///     after that source's folder disappears, because settings for a different source changed.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettings_OnlyEditedSourceIsInvalidated()
    {
        // Arrange: a repo pinned to 1.0.0, source "Default" offering 2.0.0, and a second, unrelated source
        var repoPath = CreateTempDirectory();
        RepoPinStore.Save(repoPath, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var defaultDir = CreateTempDirectory();
        CreatePackageZip(defaultDir, "contoso-agents", "2.0.0");
        var otherDir = CreateTempDirectory();
        var settings = new AppSettings
        {
            Sources =
            [
                PackageSourceDefinition.CreateDefaultZip(defaultDir),
                new PackageSourceDefinition { Alias = "Other", Type = PackageSourceType.Zip, Location = otherDir }
            ],
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        await viewModel.BeginUpgradeRefresh();
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);

        // Act: the Default source's folder vanishes, and only the Other source is edited
        Directory.Delete(defaultDir, recursive: true);
        var updated = new AppSettings
        {
            Sources =
            [
                PackageSourceDefinition.CreateDefaultZip(defaultDir),
                new PackageSourceDefinition { Alias = "Other", Type = PackageSourceType.Zip, Location = CreateTempDirectory() }
            ]
        };
        viewModel.ApplySettings(updated);
        await viewModel.PendingRefresh;

        // Assert: Default's cached answer survived (an invalidated cache would now fail to read
        // the missing folder and report no upgrade)
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that ApplySettings requests a fresh Git status check specifically for a card that    ///     reappeared (was missing, now present) since it was last checked, so it is not left
    ///     showing a stale or indefinitely "not yet checked" Pull-disabled state - an
    ///     already-realized card's one-time lazy Git-status-check trigger will not fire again for
    ///     it. Cards that were already present only get the cheap refresh, avoiding a redundant
    ///     synchronous <c>git status</c> call for every repo on every settings save.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_RepoReappeared_RefreshesGitStatusImmediately()
    {
        // Arrange: a card for a repo that currently does not exist on disk
        var repoPath = Path.Combine(Path.GetTempPath(), "agentcontrol_reappear_" + Guid.NewGuid());
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        Assert.True(viewModel.RepoCards[0].IsMissing);

        // Act: the repo path now exists with a clean working tree, and settings are reapplied
        // (e.g. the user just saved the Settings dialog)
        Directory.CreateDirectory(repoPath);
        _tempPaths.Add(repoPath);
        var stub = GitStub.Create(statusOutput: "", statusExitCode: 0);
        _tempPaths.Add(stub.Path);
        var updated = new AppSettings { GitExecutablePath = stub.Path };
        viewModel.ApplySettings(updated);

        // Assert: the card immediately reflects a fresh (clean) Git status, not a stale or
        // "not yet checked" state
        Assert.False(viewModel.RepoCards[0].IsMissing);
        Assert.True(viewModel.RepoCards[0].GitStatusChecked);
        Assert.True(viewModel.RepoCards[0].CanPull);
        Assert.False(viewModel.RepoCards[0].IsWorkingTreeDirty);
    }

    /// <summary>
    ///     Test that ApplySettings does not re-run a <c>git status</c> check for a card whose repo
    ///     was already present (never missing) and already had a Git status check completed -
    ///     only reappeared repos need an eager recheck, so re-saving Settings does not block the
    ///     UI thread with a synchronous status check for every tracked repo.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_RepoAlreadyPresentAndChecked_DoesNotRecheckGitStatus()
    {
        // Arrange: a card for a repo that exists on disk and already has a completed Git status
        // check (simulating the one-time lazy trigger having already fired)
        var repoPath = CreateTempDirectory();
        var invocationLog = Path.Combine(Path.GetTempPath(), "agentcontrol_invocations_" + Guid.NewGuid() + ".log");
        _tempPaths.Add(invocationLog);
        var stub = GitStub.Create(statusOutput: "", statusExitCode: 0, invocationLogPath: invocationLog);
        _tempPaths.Add(stub.Path);
        var settings = new AppSettings
        {
            GitExecutablePath = stub.Path,
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        viewModel.RepoCards[0].RefreshDirtyStatus();
        Assert.True(viewModel.RepoCards[0].GitStatusChecked);
        var statusCallsBeforeApply = CountStatusInvocations(invocationLog);

        // Act: settings are reapplied without the repo ever having gone missing
        var updated = new AppSettings { GitExecutablePath = stub.Path };
        viewModel.ApplySettings(updated);

        // Assert: no additional "git status" invocation was made for the already-checked,
        // still-present repo
        Assert.Equal(statusCallsBeforeApply, CountStatusInvocations(invocationLog));
    }

    /// <summary>
    ///     Test that ApplySettings refreshes a present card's Git status when
    ///     <see cref="AppSettings.GitExecutablePath"/> itself changes, even though the repo was
    ///     never missing - a previously-cached clean/dirty result computed against the old
    ///     executable must not keep being shown once a different executable is configured.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_GitExecutablePathChanged_RefreshesGitStatus()
    {
        // Arrange: a card for a present repo whose cached Git status (from the first, "clean"
        // executable) is already checked
        var repoPath = CreateTempDirectory();
        var cleanStub = GitStub.Create(statusOutput: "", statusExitCode: 0);
        _tempPaths.Add(cleanStub.Path);
        var settings = new AppSettings
        {
            GitExecutablePath = cleanStub.Path,
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        viewModel.RepoCards[0].RefreshDirtyStatus();
        Assert.True(viewModel.RepoCards[0].CanPull);

        // Act: settings are reapplied pointing at a different Git executable that reports a
        // dirty working tree
        var dirtyStub = GitStub.Create(statusOutput: " M file.txt", statusExitCode: 0);
        _tempPaths.Add(dirtyStub.Path);
        var updated = new AppSettings { GitExecutablePath = dirtyStub.Path };
        viewModel.ApplySettings(updated);

        // Assert: the card reflects the new executable's status immediately, not the stale
        // result cached against the old one
        Assert.True(viewModel.RepoCards[0].GitStatusChecked);
        Assert.False(viewModel.RepoCards[0].CanPull);
        Assert.True(viewModel.RepoCards[0].IsWorkingTreeDirty);
    }

    /// <summary>
    ///     Test that building the view model does not read any package source (upgrade checks are
    ///     started separately, in the background), and that the background check then updates the
    ///     card.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Constructor_DoesNotReadSourcesUntilUpgradeRefreshBegins()
    {
        // Arrange: a repo pinned to 1.0.0 whose source offers 2.0.0
        var repoPath = CreateTempDirectory();
        RepoPinStore.Save(repoPath, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var settings = new AppSettings
        {
            PackageSourcePath = sourceDir,
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };

        // Act: construct
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Assert: the pin is known but no upgrade has been determined yet
        Assert.Equal("1.0.0", viewModel.RepoCards[0].PinnedPackageVersion);
        Assert.False(viewModel.RepoCards[0].IsUpgradeAvailable);

        // Act: begin the background check and wait for it
        var pending = viewModel.BeginUpgradeRefresh();
        await pending;

        // Assert
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);
        Assert.Equal("2.0.0", viewModel.RepoCards[0].LatestAvailableVersion);
        Assert.Same(pending, viewModel.PendingRefresh);
    }

    /// <summary>
    ///     Test that one unreachable source does not stop the cards' upgrade checks: the sources that
    ///     can be read still offer what they have to every card.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_BeginUpgradeRefresh_OneSourceUnreachable_ReadableSourceStillOffersUpgrades()
    {
        // Arrange: two repos pinned to 1.0.0, one source whose folder does not exist, one working source with
        // the pinned version and a newer one
        var firstRepo = CreateTempDirectory();
        var secondRepo = CreateTempDirectory();
        RepoPinStore.Save(firstRepo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        RepoPinStore.Save(secondRepo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var upDir = CreateTempDirectory();
        CreatePackageZip(upDir, "contoso-agents", "1.0.0");
        CreatePackageZip(upDir, "contoso-agents", "2.0.0");
        var settings = new AppSettings
        {
            Sources =
            [
                new PackageSourceDefinition
                {
                    Alias = "Down",
                    Type = PackageSourceType.Zip,
                    Location = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_" + Guid.NewGuid())
                },
                new PackageSourceDefinition { Alias = "Up", Type = PackageSourceType.Zip, Location = upDir }
            ],
            RecentRepos = [new RecentRepo { Path = firstRepo }, new RecentRepo { Path = secondRepo }]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act
        await viewModel.BeginUpgradeRefresh();

        // Assert: the failing source hides nothing the working source offers, for either card
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);
        Assert.True(viewModel.RepoCards[1].IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that a repo added while a package source offers a newer version gets its upgrade
    ///     check in the background without delaying AddRepo.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddRepo_PinnedRepo_UpgradeCheckRunsInBackground()
    {
        // Arrange: a pinned repo that is not yet in the list, and a source with a newer version
        var repoPath = CreateRepoDirectory();
        RepoPinStore.Save(repoPath, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var viewModel = new MainWindowViewModel(
            new AppSettings { PackageSourcePath = sourceDir }, configDirectory: CreateTempDirectory());

        // Act
        Assert.True(viewModel.AddRepo(repoPath));
        await viewModel.PendingRefresh;

        // Assert
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that the connection-test provider for a Git source is a Git provider that runs the
    ///     git executable from the startup override, checks that git is new enough first, and lets
    ///     git show its sign-in (the test is started by the user).
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CreateConnectionTestProvider_GitSource_ReturnsInteractiveGitProvider()
    {
        // Arrange: the stub stands in for git, from the startup override; a config directory isolates the cache
        var stub = GitRemoteStub.Create();
        try
        {
            var options = StartupOptions.Parse(["--config-dir", CreateTempDirectory(), "--git-path", stub.Path]);
            var viewModel = new MainWindowViewModel(new AppSettings(), options, configDirectory: CreateTempDirectory());
            var definition = new PackageSourceDefinition
            {
                Alias = "Repo",
                Type = PackageSourceType.Git,
                Location = "https://git.example.com/org/agents.git",
                TagPattern = "v{version}",
                PackageIds = ["contoso-agents"]
            };

            // Act
            var provider = viewModel.CreateConnectionTestProvider(definition);
            await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

            // Assert: a Git provider; the version gate ran before the listing; git may prompt
            Assert.IsType<GitSourceProvider>(provider);
            Assert.Equal("Repo", provider.Alias);
            var invocations = stub.ReadInvocations();
            Assert.Equal(["version", "ls-remote"], invocations.Select(item => item.Command));
            Assert.All(invocations, invocation =>
            {
                Assert.NotEqual("never", invocation.Environment["GCM_INTERACTIVE"]);
                Assert.DoesNotContain("core.askPass=", invocation.Arguments, StringComparison.Ordinal);
            });
        }
        finally
        {
            stub.Delete();
        }
    }

    /// <summary>
    ///     Test that the connection-test provider for a Git source runs the git executable from the
    ///     startup override (here a stub that records every call) and not a git found some other way.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CreateConnectionTestProvider_GitSource_RunsTheConfiguredGitExecutable()
    {
        // Arrange: the stub stands in for the configured git
        var stub = GitRemoteStub.Create();
        try
        {
            var options = StartupOptions.Parse(["--config-dir", CreateTempDirectory(), "--git-path", stub.Path]);
            var viewModel = new MainWindowViewModel(new AppSettings(), options, configDirectory: CreateTempDirectory());

            // Act
            var provider = viewModel.CreateConnectionTestProvider(CreateGitDefinition());
            await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);

            // Assert: every git call of the listing went through the configured executable
            Assert.IsType<GitSourceProvider>(provider);
            Assert.Contains("ls-remote", stub.ReadInvocations().Select(item => item.Command));
        }
        finally
        {
            stub.Delete();
        }
    }

    /// <summary>
    ///     Test that a git that is older than the supported version is refused by the connection-test
    ///     provider before any tag is listed: the failure says git is too old and the stub saw only
    ///     the version query, never <c>ls-remote</c>.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CreateConnectionTestProvider_GitSource_OldGit_IsRefusedBeforeAnyTagIsListed()
    {
        // Arrange: a git that reports an old version
        var stub = GitRemoteStub.Create().Respond("version", "git version 2.20.1\n");
        try
        {
            var options = StartupOptions.Parse(["--config-dir", CreateTempDirectory(), "--git-path", stub.Path]);
            var viewModel = new MainWindowViewModel(new AppSettings(), options, configDirectory: CreateTempDirectory());
            var provider = viewModel.CreateConnectionTestProvider(CreateGitDefinition());

            // Act
            var failure = await Assert.ThrowsAsync<GitRemoteException>(
                () => provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));

            // Assert: refused as too old, and only the version query ran
            Assert.Equal(GitFailureKind.GitTooOld, failure.Kind);
            Assert.Contains("Git 2.29 or newer", failure.Message, StringComparison.Ordinal);
            Assert.All(stub.ReadInvocations(), invocation => Assert.Equal("version", invocation.Command));
            Assert.DoesNotContain("ls-remote", stub.ReadInvocations().Select(item => item.Command));
        }
        finally
        {
            stub.Delete();
        }
    }

    /// <summary>
    ///     Creates a Git source definition for the connection-test provider tests.
    /// </summary>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition CreateGitDefinition() => new()
    {
        Alias = "Repo",
        Type = PackageSourceType.Git,
        Location = "https://git.example.com/org/agents.git",
        TagPattern = "v{version}",
        PackageIds = ["contoso-agents"]
    };

    /// <summary>
    ///     Test that a NuGet source still gets a NuGet provider, with or without startup options that
    ///     name a git executable.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_CreateConnectionTestProvider_NuGetSource_ReturnsNuGetProvider()
    {
        // Arrange
        var options = StartupOptions.Parse(["--config-dir", CreateTempDirectory(), "--git-path", "git-not-used"]);
        var viewModel = new MainWindowViewModel(new AppSettings(), options, configDirectory: CreateTempDirectory());
        var definition = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = "https://feeds.example.com/v3/index.json",
            PackageIds = ["contoso-agents"]
        };

        // Act
        var provider = viewModel.CreateConnectionTestProvider(definition);

        // Assert
        Assert.IsType<NuGetSourceProvider>(provider);
    }

    /// <summary>
    ///     Counts how many "status" subcommand invocations are recorded in a
    ///     <see cref="GitStub"/> invocation log file.
    /// </summary>
    private static int CountStatusInvocations(string invocationLogPath) =>
        File.Exists(invocationLogPath)
            ? File.ReadAllLines(invocationLogPath).Count(line => line.StartsWith("status", StringComparison.Ordinal))
            : 0;

    /// <summary>
    ///     Creates a minimal package zip named <c>{packageName}-{version}.zip</c> at
    ///     <paramref name="sourceDir"/>.
    /// </summary>
    private void CreatePackageZip(string sourceDir, string packageName, string version)
    {
        var zipPath = Path.Combine(sourceDir, $"{packageName}-{version}.zip");
        using var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
        var entry = archive.CreateEntry(".github/agents/copilot.md");
        using (var writer = new StreamWriter(entry.Open()))
        {
            writer.Write("agents content");
        }

        _tempPaths.Add(zipPath);
    }

    /// <summary>
    ///     Creates a unique temporary directory with a distinctive, caller-supplied name suffix,
    ///     tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    private string CreateNamedTempDirectory(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_main_vm_test_" + Guid.NewGuid() + "_" + suffix);
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     Creates a unique temporary directory tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_main_vm_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     Creates a unique temporary folder that is the top folder of a Git repository as far as the
    ///     add is concerned (a <c>.git</c> folder whose <c>HEAD</c> names a branch), removed on disposal.
    /// </summary>
    /// <returns>The folder's path.</returns>
    private string CreateRepoDirectory()
    {
        var path = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
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
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp directories do not fail the test.
            }
        }
    }
}
