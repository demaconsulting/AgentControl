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
///     system git executable as part of each card's background repo-state read.
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
    public async Task MainWindowViewModel_AddRepo_ValidNewPath_InsertsCardAndPersistsSettings()
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

        await viewModel.FlushSettingsAsync();
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
    public async Task MainWindowViewModel_Constructor_ListedFolderWithoutGit_IsKept()
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
        await viewModel.FlushSettingsAsync();
        Assert.Contains(SettingsStore.Load(configDir).RecentRepos, repo => repo.Path == plainFolder);
    }

    /// <summary>
    ///     Test that adding several folders reports an outcome for each, in order, adds the repositories
    ///     and skips the others, and handles a folder given twice once.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddRepos_MixedPaths_ReportsAnOutcomePerPath()
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
        await viewModel.FlushSettingsAsync();
        Assert.Equal([listed, first, second], SettingsStore.Load(configDir).RecentRepos.Select(repo => repo.Path));
    }

    /// <summary>
    ///     Test that the same folder given with and without a trailing directory separator in one
    ///     multi-folder add is handled once and gives one card, stored without the separator.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddRepos_SameFolderWithAndWithoutTrailingSeparator_AddsOneCard()
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
        await viewModel.FlushSettingsAsync();
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
    public async Task MainWindowViewModel_ApplySettings_PreservesRecentReposAndPersistsUpdatedFields()
    {
        // Arrange: a view model with one recent repo
        var configDir = CreateTempDirectory();
        var repoPath = CreateTempDirectory();
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);

        // Act: apply an updated settings object with a new package source path
        var updated = new AppSettings { PackageSourcePath = @"\\share\packages" };
        await viewModel.ApplySettingsAsync(updated);

        // Assert: the recent-repos list survived, the new field was adopted, and both were
        // persisted to disk
        var card = Assert.Single(viewModel.RepoCards);
        Assert.Equal(repoPath, card.RepoPath);
        Assert.Equal(@"\\share\packages", Assert.Single(viewModel.Settings.GetEffectiveSources()).Location);

        await viewModel.FlushSettingsAsync();
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
        await viewModel.ApplySettingsAsync(new AppSettings
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
    public async Task MainWindowViewModel_RemoveRepo_RemovesFromCollectionsAndPersistsSettings()
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

        await viewModel.FlushSettingsAsync();
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
    ///     dirty-check subprocess for any card (only the cheap facts are read at launch, in the background).
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
    public async Task MainWindowViewModel_ApplySettings_SourceLocationWithEmbeddedCredential_ThrowsAndKeepsCurrentSettings()
    {
        // Arrange
        var configDir = CreateTempDirectory();
        var settings = new AppSettings { PackageSourcePath = CreateTempDirectory() };
        var viewModel = new MainWindowViewModel(settings, configDirectory: configDir);
        var updated = new AppSettings { PackageSourcePath = "https://user:SentinelSecretValue@feeds.example.com/nuget" };

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.ApplySettingsAsync(updated));

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
    public async Task MainWindowViewModel_ApplySettings_ThemeChanged_PersistsAndAppliesTheme()
    {
        // Arrange: a view model with a recording theme applier
        var configDir = CreateTempDirectory();
        var applied = new List<ThemePreference>();
        var viewModel = new MainWindowViewModel(
            new AppSettings(), configDirectory: configDir, applyTheme: applied.Add);

        // Act: save a settings object that chooses the dark theme
        await viewModel.ApplySettingsAsync(new AppSettings { Theme = ThemePreference.Dark });

        // Assert: the applier received the choice and it was persisted
        Assert.Equal([ThemePreference.Dark], applied);
        Assert.Equal(ThemePreference.Dark, viewModel.Settings.Theme);
        await viewModel.FlushSettingsAsync();
        Assert.Equal(ThemePreference.Dark, SettingsStore.Load(configDir).Theme);
    }

    /// <summary>
    ///     Test that a rejected save never applies the theme it carried.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettings_SaveFails_DoesNotApplyTheme()
    {
        // Arrange: a configuration "directory" that is really a file, so every write fails
        var blockingFile = Path.Combine(CreateTempDirectory(), "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "blocker", TestContext.Current.CancellationToken);
        var applied = new List<ThemePreference>();
        var viewModel = new MainWindowViewModel(
            new AppSettings(), configDirectory: blockingFile, applyTheme: applied.Add);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.ApplySettingsAsync(new AppSettings { Theme = ThemePreference.Light }));
        Assert.Empty(applied);
        Assert.Equal(ThemePreference.System, viewModel.Settings.Theme);
    }

    /// <summary>
    ///     Test that a failed settings write leaves the previous settings live, so a later save
    ///     (for example when a repo is marked as a favorite) can never persist the rejected candidate.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettings_SaveFails_KeepsPreviousSettingsLive()
    {
        // Arrange: a configuration "directory" that is really a file, so every write fails; a later
        // save (such as toggling a favorite) would otherwise persist the rejected candidate
        var repoPath = CreateTempDirectory();
        var blockingFile = Path.Combine(CreateTempDirectory(), "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "blocker", TestContext.Current.CancellationToken);
        var settings = new AppSettings
        {
            GitExecutablePath = "previous-git",
            RecentRepos = [new RecentRepo { Path = repoPath }]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: blockingFile);
        var rejected = new AppSettings { GitExecutablePath = "rejected-git" };

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.ApplySettingsAsync(rejected));

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
        await viewModel.ApplySettingsAsync(updated);
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
        await viewModel.ApplySettingsAsync(updated);
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
    public async Task MainWindowViewModel_ApplySettings_RepoReappeared_RefreshesGitStatusImmediately()
    {
        // Arrange: a card for a repo that currently does not exist on disk
        var repoPath = Path.Combine(Path.GetTempPath(), "agentcontrol_reappear_" + Guid.NewGuid());
        var settings = new AppSettings { RecentRepos = [new RecentRepo { Path = repoPath }] };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        await viewModel.WhenRepoStateSettledAsync();
        Assert.True(viewModel.RepoCards[0].IsMissing);

        // Act: the repo path now exists with a clean working tree, and settings are reapplied
        // (e.g. the user just saved the Settings dialog)
        Directory.CreateDirectory(repoPath);
        _tempPaths.Add(repoPath);
        var stub = GitStub.Create(statusOutput: "", statusExitCode: 0);
        _tempPaths.Add(stub.Path);
        var updated = new AppSettings { GitExecutablePath = stub.Path };
        await viewModel.ApplySettingsAsync(updated);
        await viewModel.PendingRefresh;

        // Assert: once the background refresh is done, the card reflects a fresh (clean) Git
        // status, not a stale or "not yet checked" state
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
    public async Task MainWindowViewModel_ApplySettings_RepoAlreadyPresentAndChecked_DoesNotRecheckGitStatus()
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
        await viewModel.ApplySettingsAsync(updated);
        await viewModel.PendingRefresh;

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
    public async Task MainWindowViewModel_ApplySettings_GitExecutablePathChanged_RefreshesGitStatus()
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
        await viewModel.ApplySettingsAsync(updated);
        await viewModel.PendingRefresh;

        // Assert: the card reflects the new executable's status, not the stale
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

        // Act: construct, and let the cards' repo state be read in the background
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());
        await viewModel.WhenRepoStateSettledAsync();

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
    ///     Test that a background add returns to its caller while the folders are still being checked
    ///     (held at a gate) and adds nothing until they have been, then adds them all.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_WhileFoldersAreChecked_ReturnsAndAddsNothingYet()
    {
        // Arrange: two repo folders and a background runner that holds every check
        var gate = new GatedBackgroundWorkRunner();
        var repoA = CreateRepoDirectory();
        var repoB = CreateRepoDirectory();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);

        // Act: start the add; the checks are held
        var add = viewModel.EnqueueAddReposAsync([repoA, repoB], null);
        await gate.FirstCall;

        // Assert: the caller got control back, nothing is added yet, and progress is shown
        Assert.False(add.IsCompleted);
        Assert.Empty(viewModel.RepoCards);
        Assert.True(viewModel.GlobalProgress.IsActive);
        Assert.True(viewModel.GlobalProgress.CanCancel);

        // Act: let the checks run
        gate.Release();
        var result = await add;

        // Assert: both added, and the strip is gone
        Assert.False(result.Canceled);
        Assert.Equal([AddRepoResult.Added, AddRepoResult.Added], result.Outcomes.Select(outcome => outcome.Result));
        Assert.Equal([repoA, repoB], viewModel.RepoCards.Select(card => card.RepoPath));
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that a background add of several folders saves the settings once and rebuilds the
    ///     displayed list once for the whole batch.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_SeveralFolders_SavesOnceAndUpdatesTheListOnce()
    {
        // Arrange: a counting save seam, and a counter of list rebuilds
        var configDir = CreateTempDirectory();
        var saves = 0;
        var viewModel = new MainWindowViewModel(
            new AppSettings(),
            configDirectory: configDir,
            saveSettings: settings =>
            {
                Interlocked.Increment(ref saves);
                SettingsStore.Save(settings, configDir);
            });
        var rebuilds = 0;
        viewModel.PropertyChanged += (_, e) =>
            rebuilds += e.PropertyName == nameof(MainWindowViewModel.HasNoDisplayedRepos) ? 1 : 0;
        string[] repos = [CreateRepoDirectory(), CreateRepoDirectory(), CreateRepoDirectory(), CreateRepoDirectory()];

        // Act
        var result = await viewModel.EnqueueAddReposAsync(repos, null);

        // Assert: four added with one save and one rebuild, and the file holds all four
        Assert.All(result.Outcomes, outcome => Assert.Equal(AddRepoResult.Added, outcome.Result));
        Assert.Equal(4, viewModel.RepoCards.Count);
        Assert.Equal(1, saves);
        Assert.Equal(1, rebuilds);
        await viewModel.FlushSettingsAsync();
        Assert.Equal(4, SettingsStore.Load(configDir).RecentRepos.Count);
    }

    /// <summary>
    ///     Test that a background add reports its progress: the total at once, then each checked folder
    ///     up to the total.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_ReportsProgressCounts()
    {
        // Arrange
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory());
        var reports = new List<AddReposProgress>();
        var progress = new RecordingProgress(reports);
        string[] repos = [CreateRepoDirectory(), CreateRepoDirectory(), CreateRepoDirectory()];

        // Act
        await viewModel.AddReposAsync(repos, null, progress, CancellationToken.None);

        // Assert: 0 of 3 first, then 1, 2 and 3 of 3 (in whatever order the checks finished)
        lock (reports)
        {
            Assert.Equal(new AddReposProgress(0, 3), reports[0]);
            Assert.All(reports, report => Assert.Equal(3, report.Total));
            Assert.Equal([0, 1, 2, 3], reports.Select(report => report.Completed).Order());
        }
    }

    /// <summary>
    ///     Test that a repo listed while a background add was checking it is not added twice: the add
    ///     re-checks the list when it commits.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_RepoListedMeanwhile_IsAlreadyListed()
    {
        // Arrange: a background add of one repo, held while its folder is checked
        var gate = new GatedBackgroundWorkRunner();
        var repo = CreateRepoDirectory();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);
        var add = viewModel.EnqueueAddReposAsync([repo], null);
        await gate.FirstCall;

        // Act: the same repo is added another way meanwhile, then the background add completes
        Assert.True(viewModel.AddRepo(repo));
        gate.Release();
        var result = await add;

        // Assert: reported as already listed, and listed once
        Assert.Equal(AddRepoResult.AlreadyListed, Assert.Single(result.Outcomes).Result);
        Assert.Single(viewModel.RepoCards);
        Assert.Single(viewModel.Settings.RecentRepos);
    }

    /// <summary>
    ///     Test that a repo listed when a background add was planned but removed (together with the
    ///     add's target group) while its folders were checked is added again: both folders go into
    ///     the target group, which is created again, with one save, and the re-added card reads its
    ///     pin in the background.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_ListedRepoAndTargetGroupRemovedMeanwhile_AddsBothToRecreatedGroupWithOneSave()
    {
        // Arrange: an anchor repo that makes "Work" exist, a listed ungrouped repo with a pin, a new
        // repo, a counting save seam and a runner that holds every check
        var gate = new GatedBackgroundWorkRunner();
        var configDir = CreateTempDirectory();
        var anchor = CreateRepoDirectory();
        var listed = CreateRepoDirectory();
        RepoPinStore.Save(listed, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var fresh = CreateRepoDirectory();
        var saves = 0;
        var viewModel = new MainWindowViewModel(
            new AppSettings
            {
                RecentRepos =
                [
                    new RecentRepo { Path = anchor, GroupName = "Work" },
                    new RecentRepo { Path = listed }
                ]
            },
            configDirectory: configDir,
            saveSettings: settings =>
            {
                Interlocked.Increment(ref saves);
                SettingsStore.Save(settings, configDir);
            },
            backgroundWork: gate);
        var add = viewModel.EnqueueAddReposAsync([listed, fresh], "Work");
        await gate.FirstCall;

        // Act: while the folders are checked, the listed repo and the target group are removed
        viewModel.RemoveRepo(viewModel.RepoCards.Single(card => card.RepoPath == listed));
        Assert.True(viewModel.RemoveGroup("Work"));

        // Assert: both removals took effect at once, and the add is still held
        Assert.DoesNotContain("Work", viewModel.GroupNames);
        Assert.DoesNotContain(viewModel.RepoCards, card => card.RepoPath == listed);
        Assert.False(add.IsCompleted);

        // The removals are written in the background; written now, so the add's own save is counted alone
        await viewModel.FlushSettingsAsync();
        var savesBefore = saves;

        // Act: let the checks run, then wait for the re-added card's background read
        gate.Release();
        var result = await add;
        await viewModel.PendingAddRepos;
        var reAdded = viewModel.RepoCards.Single(card => card.RepoPath == listed);
        await reAdded.CheapRefreshTask;

        // Assert: both added into the re-created, expanded group with one save; the anchor stays
        // ungrouped; the re-added card shows its pin; the file holds both in the group
        Assert.False(result.Canceled);
        Assert.Equal([AddRepoResult.Added, AddRepoResult.Added], result.Outcomes.Select(outcome => outcome.Result));
        Assert.Equal(savesBefore + 1, saves);
        Assert.Equal("Work", reAdded.GroupName);
        Assert.Equal("Work", viewModel.RepoCards.Single(card => card.RepoPath == fresh).GroupName);
        Assert.Contains("Work", viewModel.GroupNames);
        Assert.False(viewModel.IsGroupCollapsed("Work"));
        Assert.Null(viewModel.RepoCards.Single(card => card.RepoPath == anchor).GroupName);
        Assert.Equal("1.0.0", reAdded.PinnedPackageVersion);
        await viewModel.FlushSettingsAsync();
        var saved = SettingsStore.Load(configDir).RecentRepos;
        Assert.Equal("Work", saved.Single(repo => repo.Path == listed).GroupName);
        Assert.Equal("Work", saved.Single(repo => repo.Path == fresh).GroupName);
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that a target group collapsed while a background add was checking its folders is
    ///     opened by the add, so the new repo is shown, and is saved as expanded.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_TargetGroupCollapsedMeanwhile_OpensItWithTheNewRepo()
    {
        // Arrange: an expanded "Work" group, a counting save seam and an add into it held at a gate
        var gate = new GatedBackgroundWorkRunner();
        var configDir = CreateTempDirectory();
        var anchor = CreateRepoDirectory();
        var fresh = CreateRepoDirectory();
        var saves = 0;
        var viewModel = new MainWindowViewModel(
            new AppSettings { RecentRepos = [new RecentRepo { Path = anchor, GroupName = "Work" }] },
            configDirectory: configDir,
            saveSettings: settings =>
            {
                Interlocked.Increment(ref saves);
                SettingsStore.Save(settings, configDir);
            },
            backgroundWork: gate);
        var add = viewModel.EnqueueAddReposAsync([fresh], "Work");
        await gate.FirstCall;

        // Act: the user collapses the target group while the folder is checked
        viewModel.ToggleGroupCollapsed("Work");

        // Assert: the collapse took effect at once
        Assert.True(viewModel.IsGroupCollapsed("Work"));
        Assert.DoesNotContain(viewModel.DisplayedRepoCards, card => card.RepoPath == anchor);
        Assert.False(add.IsCompleted);

        // The collapse is written in the background; written now, so the add's own save is counted alone
        await viewModel.FlushSettingsAsync();
        var savesBefore = saves;

        // Act: let the check run
        gate.Release();
        var result = await add;

        // Assert: added with one save, and the group is opened (and saved so) showing both repos
        Assert.Equal(AddRepoResult.Added, Assert.Single(result.Outcomes).Result);
        Assert.Equal(savesBefore + 1, saves);
        Assert.False(viewModel.IsGroupCollapsed("Work"));
        var anchorCard = Assert.Single(viewModel.DisplayedRepoCards, card => card.RepoPath == anchor);
        var newCard = Assert.Single(viewModel.DisplayedRepoCards, card => card.RepoPath == fresh);
        Assert.Equal("Work", anchorCard.GroupName);
        Assert.Equal("Work", newCard.GroupName);
        await viewModel.FlushSettingsAsync();
        Assert.DoesNotContain("Work", SettingsStore.Load(configDir).CollapsedGroups ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that adding a repo does not check its working tree: the card shows its Git status as not
    ///     checked yet and no <c>git status</c> ran (the check is deferred until the card is shown).
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_NewRepo_GitStatusNotCheckedAndNoStatusRun()
    {
        // Arrange: a git stub that logs every invocation
        var invocationLog = Path.Combine(Path.GetTempPath(), "agentcontrol_add_invocations_" + Guid.NewGuid() + ".log");
        _tempPaths.Add(invocationLog);
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        _tempPaths.Add(stub.Path);
        var viewModel = new MainWindowViewModel(
            new AppSettings { GitExecutablePath = stub.Path }, configDirectory: CreateTempDirectory());
        var repo = CreateRepoDirectory();

        // Act
        await viewModel.EnqueueAddReposAsync([repo], null);
        await viewModel.PendingRefresh;

        // Assert: the card's branch was read (cheap facts), but not its working tree
        var card = Assert.Single(viewModel.RepoCards);
        Assert.Equal("main", card.CurrentBranch);
        Assert.False(card.GitStatusChecked);
        Assert.False(card.IsWorkingTreeDirty);
        Assert.Equal(0, CountStatusInvocations(invocationLog));
    }

    /// <summary>
    ///     Test that canceling a background add before it saves adds nothing and saves nothing.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_Canceled_SavesNothingAndAddsNothing()
    {
        // Arrange: an add held while its folders are checked, and a counting save seam
        var gate = new GatedBackgroundWorkRunner();
        var configDir = CreateTempDirectory();
        var saves = 0;
        var viewModel = new MainWindowViewModel(
            new AppSettings(),
            configDirectory: configDir,
            saveSettings: settings =>
            {
                Interlocked.Increment(ref saves);
                SettingsStore.Save(settings, configDir);
            },
            backgroundWork: gate);
        var add = viewModel.EnqueueAddReposAsync([CreateRepoDirectory(), CreateRepoDirectory()], null);
        await gate.FirstCall;

        // Act: Cancel on the progress strip, then the held checks finish
        viewModel.GlobalProgress.CancelCommand.Execute(null);
        gate.Release();
        var result = await add;

        // Assert: canceled, nothing added or saved, and the strip is gone
        Assert.True(result.Canceled);
        Assert.Empty(result.Outcomes);
        Assert.Empty(viewModel.RepoCards);
        Assert.Empty(viewModel.Settings.RecentRepos);
        Assert.Equal(0, saves);
        Assert.False(File.Exists(Path.Combine(configDir, "settings.json")));
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that an add requested while another runs waits for it (it checks nothing meanwhile),
    ///     then runs, and that a folder both adds name is added once.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_SecondBatchWhileFirstRuns_RunsAfterItWithoutDuplicates()
    {
        // Arrange: the first add is held while its two folders are checked
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);
        var repoA = CreateRepoDirectory();
        var repoB = CreateRepoDirectory();
        var repoC = CreateRepoDirectory();
        var first = viewModel.EnqueueAddReposAsync([repoA, repoB], null);
        await gate.FirstCall;

        // Act: a second add (sharing one folder) is requested while the first one runs
        var second = viewModel.EnqueueAddReposAsync([repoB, repoC], null);

        // Assert: the second one waits - it has not checked any folder yet
        Assert.False(second.IsCompleted);
        Assert.Equal(2, gate.Calls);

        // Act: let everything run
        gate.Release();
        var firstResult = await first;
        var secondResult = await second;
        await viewModel.PendingAddRepos;

        // Assert: the first added both, the second found its shared folder listed and added the other
        Assert.Equal([AddRepoResult.Added, AddRepoResult.Added], firstResult.Outcomes.Select(outcome => outcome.Result));
        Assert.Equal([AddRepoResult.AlreadyListed, AddRepoResult.Added], secondResult.Outcomes.Select(outcome => outcome.Result));
        Assert.Equal([repoA, repoB, repoC], viewModel.RepoCards.Select(card => card.RepoPath));
        Assert.Equal(4, gate.Calls);
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that the global progress shows the add's count while folders are checked, and clears
    ///     when the add has ended.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_EnqueueAddRepos_ShowsAndClearsGlobalProgress()
    {
        // Arrange
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);
        Assert.False(viewModel.GlobalProgress.IsActive);

        // Act: the add is held while its folders are checked
        var add = viewModel.EnqueueAddReposAsync([CreateRepoDirectory(), CreateRepoDirectory()], null);
        await gate.FirstCall;

        // Assert: determinate, with the count
        Assert.True(viewModel.GlobalProgress.IsActive);
        Assert.False(viewModel.GlobalProgress.IsIndeterminate);
        Assert.Equal("Adding 0 of 2 repositories...", viewModel.GlobalProgress.Text);
        Assert.Equal(2, viewModel.GlobalProgress.Maximum);

        // Act
        gate.Release();
        await add;

        // Assert
        Assert.False(viewModel.GlobalProgress.IsActive);
        Assert.Equal(string.Empty, viewModel.GlobalProgress.Text);
    }

    /// <summary>
    ///     Test that applying settings returns while the cards' refresh is still reading (held at a
    ///     gate), showing "Refreshing repositories 0 of 1..." without Cancel, and that the refresh
    ///     completes and clears the strip once the reads run.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettings_CardRefreshHeld_ReturnsAndShowsRefreshingProgress()
    {
        // Arrange: one listed repo and a runner that holds the cards' background reads
        var gate = new GatedBackgroundWorkRunner();
        var repo = CreateTempDirectory();
        var viewModel = new MainWindowViewModel(
            new AppSettings { RecentRepos = [new RecentRepo { Path = repo }] },
            configDirectory: CreateTempDirectory(),
            backgroundWork: gate);

        // The window's startup read of the card runs first; the next reads are held
        gate.Release();
        await viewModel.WhenRepoStateSettledAsync();
        gate.Close();

        // Act: apply settings; the card refresh is held
        await viewModel.ApplySettingsAsync(new AppSettings());
        await gate.FirstCall;

        // Assert: returned with the refresh still running, shown in the strip with its count and
        // without Cancel
        Assert.False(viewModel.PendingRefresh.IsCompleted);
        Assert.True(viewModel.GlobalProgress.IsActive);
        Assert.Equal("Refreshing repositories 0 of 1...", viewModel.GlobalProgress.Text);
        Assert.False(viewModel.GlobalProgress.CanCancel);

        // Act
        gate.Release();
        await viewModel.PendingRefresh;

        // Assert
        Assert.False(viewModel.GlobalProgress.IsActive);
        Assert.False(viewModel.RepoCards[0].IsMissing);
    }

    /// <summary>
    ///     Test that a background add of many folders checks at most two of them at the same time (the
    ///     shared refresh queue's limit), and still adds every one.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_AddReposAsync_ManyFolders_AtMostTwoInspectedAtOnce()
    {
        // Arrange: six repo folders and a runner that holds every check
        var gate = new GatedBackgroundWorkRunner();
        var repos = Enumerable.Range(0, 6).Select(_ => CreateRepoDirectory()).ToList();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: CreateTempDirectory(), backgroundWork: gate);

        // Act: start the add; the checks are held
        var add = viewModel.EnqueueAddReposAsync(repos, null);
        await gate.FirstCall;
        var handedOverWhileHeld = gate.Calls;

        // Assert: only two checks were started
        Assert.Equal(RepoRefreshQueue.DefaultMaxConcurrentReads, handedOverWhileHeld);

        // Act: let them all run
        gate.Release();
        var result = await add;

        // Assert: every folder checked and added, never more than two at once
        Assert.All(result.Outcomes, outcome => Assert.Equal(AddRepoResult.Added, outcome.Result));
        Assert.Equal(repos, viewModel.RepoCards.Select(card => card.RepoPath));
        Assert.Equal(6, gate.Calls);
        Assert.Equal(2, gate.MaxInFlight);
    }

    /// <summary>
    ///     Test that the cards' refresh after a settings save shares one limit with an add that runs at
    ///     the same time: however many repos are read, at most two reads run at once.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_ApplySettings_RefreshUsesSharedQueueLimit()
    {
        // Arrange: four listed repos and a runner that holds every read
        var gate = new GatedBackgroundWorkRunner();
        var listed = Enumerable.Range(0, 4).Select(_ => CreateRepoDirectory()).ToList();
        var viewModel = new MainWindowViewModel(
            new AppSettings { RecentRepos = [.. listed.Select(path => new RecentRepo { Path = path })] },
            configDirectory: CreateTempDirectory(),
            backgroundWork: gate);

        // The window's startup reads of the cards run first; the next reads are held
        gate.Release();
        await viewModel.WhenRepoStateSettledAsync();
        gate.Close();
        var callsBefore = gate.Calls;

        // Act: settings are saved (four card reads) and two folders are added (two folder checks)
        await viewModel.ApplySettingsAsync(new AppSettings());
        var add = viewModel.EnqueueAddReposAsync([CreateRepoDirectory(), CreateRepoDirectory()], null);
        await gate.FirstCall;
        var handedOverWhileHeld = gate.Calls - callsBefore;

        // Assert: two reads in all, not two per caller, and the strip counts the cards' refresh
        Assert.Equal(2, handedOverWhileHeld);
        Assert.True(viewModel.GlobalProgress.IsActive);

        // Act: let everything run
        gate.Release();
        await add;
        await viewModel.PendingRefresh;

        // Assert: every read ran, never more than two at once, and the strip is gone
        Assert.Equal(6, viewModel.RepoCards.Count);
        Assert.Equal(6, gate.Calls - callsBefore);
        Assert.Equal(2, gate.MaxInFlight);
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that the view model is built at once, without waiting for the disk or git: while the
    ///     cards' startup reads are held, it is usable, only two reads run, the strip shows "Refreshing
    ///     repositories 0 of 3..." without Cancel, and every card shows its git-derived facts as not known
    ///     yet rather than as values.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Constructor_GitHeld_ReturnsWithCardsRefreshingAndProgressShown()
    {
        // Arrange: three listed repos (stub git) and a runner that holds every read
        var stub = GitStub.Create(lsFilesOutput: ".github/agents/a.md");
        _tempPaths.Add(stub.Path);
        var gate = new GatedBackgroundWorkRunner();
        var repos = Enumerable.Range(0, 3).Select(_ => CreateTempDirectory()).ToList();
        var settings = new AppSettings
        {
            GitExecutablePath = stub.Path,
            RecentRepos = [.. repos.Select(path => new RecentRepo { Path = path })]
        };

        // Act: build the view model while the reads are held
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory(), backgroundWork: gate);
        await gate.FirstCall;
        viewModel.FilterText = Path.GetFileName(repos[1]);

        // Assert: built and usable (the filter applied) with two reads running and the progress shown
        Assert.Equal(3, viewModel.RepoCards.Count);
        Assert.Equal(repos[1], Assert.Single(viewModel.DisplayedRepoCards).RepoPath);
        Assert.Equal(RepoRefreshQueue.DefaultMaxConcurrentReads, gate.Calls);
        Assert.False(viewModel.PendingStartupRefresh.IsCompleted);
        Assert.True(viewModel.GlobalProgress.IsActive);
        Assert.Equal("Refreshing repositories 0 of 3...", viewModel.GlobalProgress.Text);
        Assert.False(viewModel.GlobalProgress.CanCancel);

        // Assert: every card shows that it is refreshing, and nothing git-derived as a fact
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.True(card.IsRefreshing);
            Assert.False(card.HasRepoState);
            Assert.Equal(RepoCardStatus.Refreshing, card.Status);
            Assert.True(card.ShowBranchChip);
            Assert.Equal("...", card.BranchChipText);
            Assert.False(card.ShowNoPackageChip);
            Assert.False(card.HasCommittedAgentFiles);
            Assert.False(card.IsMissing);
        });

        // Act: let the reads run
        gate.Release();
        await viewModel.PendingStartupRefresh;

        // Assert: the facts are shown and the strip is gone
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.False(card.IsRefreshing);
            Assert.True(card.HasRepoState);
            Assert.Equal("main", card.BranchChipText);
            Assert.True(card.HasCommittedAgentFiles);
        });
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that closing the window (disposing the view model) while the cards' startup reads are
    ///     held gives the reads up: those still waiting never start, those running are not applied when
    ///     they end, no card shows a read state or keeps refreshing, and the strip is gone.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Dispose_StartupReadsHeld_GivesThemUpAndAppliesNothing()
    {
        // Arrange: three listed repos (stub git), and a runner that holds every read; two reads run
        var stub = GitStub.Create(lsFilesOutput: ".github/agents/a.md");
        _tempPaths.Add(stub.Path);
        var gate = new GatedBackgroundWorkRunner();
        var settings = new AppSettings
        {
            GitExecutablePath = stub.Path,
            RecentRepos = [.. Enumerable.Range(0, 3).Select(_ => new RecentRepo { Path = CreateTempDirectory() })]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory(), backgroundWork: gate);
        await gate.FirstCall;
        Assert.True(viewModel.GlobalProgress.IsActive);

        // Act: close the window, then let the held reads end
        viewModel.Dispose();
        gate.Release();
        await viewModel.PendingStartupRefresh;

        // Assert: only the two reads that were running ever started, and nothing was applied
        Assert.Equal(RepoRefreshQueue.DefaultMaxConcurrentReads, gate.Calls);
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.False(card.HasRepoState);
            Assert.False(card.IsRefreshing);
            Assert.False(card.HasCommittedAgentFiles);
        });
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that the cards fill in one by one as their startup reads complete, top of the list first,
    ///     with the strip counting them, and that a missing folder and a pin are shown once read.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Constructor_ReadsComplete_CardsFillInAndProgressClears()
    {
        // Arrange: a pinned repo, a plain repo and a folder that does not exist; reads released one by one
        var stub = GitStub.Create();
        _tempPaths.Add(stub.Path);
        var pinned = CreateTempDirectory();
        RepoPinStore.Save(pinned, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_repo_" + Guid.NewGuid());
        var settings = new AppSettings
        {
            GitExecutablePath = stub.Path,
            RecentRepos =
            [
                new RecentRepo { Path = pinned },
                new RecentRepo { Path = CreateTempDirectory() },
                new RecentRepo { Path = missing }
            ]
        };
        var runner = new SteppedBackgroundWorkRunner();
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory(), backgroundWork: runner);
        var cards = viewModel.RepoCards.ToList();
        await runner.WaitForCallsAsync(2);

        // Act: the first card's read completes
        runner.ReleaseNext();
        await cards[0].CheapRefreshTask;

        // Assert: the first card is filled in, the others still refreshing, and the strip counts it
        Assert.True(cards[0].HasRepoState);
        Assert.Equal("contoso-agents", cards[0].PinnedPackageName);
        Assert.Equal("main", cards[0].CurrentBranch);
        Assert.False(cards[0].IsRefreshing);
        Assert.False(cards[1].HasRepoState);
        Assert.False(cards[2].HasRepoState);
        Assert.Equal("Refreshing repositories 1 of 3...", viewModel.GlobalProgress.Text);

        // Act: the rest complete
        runner.ReleaseAll();
        await viewModel.PendingStartupRefresh;

        // Assert: every card is filled in (the missing folder shown as missing) and the strip is gone
        Assert.All(cards, card => Assert.True(card.HasRepoState));
        Assert.True(cards[2].IsMissing);
        Assert.Equal(RepoCardStatus.Problem, cards[2].Status);
        Assert.False(viewModel.GlobalProgress.IsActive);
    }
    /// <summary>
    ///     An <see cref="IProgress{T}"/> that records every report at once (thread-safe), unlike
    ///     <see cref="Progress{T}"/>, which posts.
    /// </summary>
    /// <param name="reports">The list the reports are added to; locked while adding.</param>
    private sealed class RecordingProgress(List<AddReposProgress> reports) : IProgress<AddReposProgress>
    {
        /// <inheritdoc />
        public void Report(AddReposProgress value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
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
