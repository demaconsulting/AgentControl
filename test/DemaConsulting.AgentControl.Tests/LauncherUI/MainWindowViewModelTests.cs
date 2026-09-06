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
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
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
    ///     Test that AddRepo inserts a new card at the front of the list and persists the
    ///     updated settings to disk.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_AddRepo_ValidNewPath_InsertsCardAndPersistsSettings()
    {
        // Arrange: an empty recent-repos list and a config directory to persist to
        var configDir = CreateTempDirectory();
        var newRepo = CreateTempDirectory();
        var viewModel = new MainWindowViewModel(new AppSettings(), configDirectory: configDir);

        // Act: add the new repo
        var added = viewModel.AddRepo(newRepo);

        // Assert: the card was added at the front, and settings were persisted
        Assert.True(added);
        Assert.Single(viewModel.RepoCards);
        Assert.Equal(newRepo, viewModel.RepoCards[0].RepoPath);

        var reloaded = SettingsStore.Load(configDir);
        Assert.Single(reloaded.RecentRepos);
        Assert.Equal(newRepo, reloaded.RecentRepos[0].Path);
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
        Assert.Single(viewModel.RepoCards);
        Assert.Equal(repoPath, viewModel.RepoCards[0].RepoPath);
        Assert.Equal(@"\\share\packages", viewModel.Settings.PackageSourcePath);

        var reloaded = SettingsStore.Load(configDir);
        Assert.Equal(@"\\share\packages", reloaded.PackageSourcePath);
        Assert.Single(reloaded.RecentRepos);
        Assert.Equal(repoPath, reloaded.RecentRepos[0].Path);
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
        Assert.Single(viewModel.DisplayedRepoCards);
        Assert.Equal(alphaRepo, viewModel.DisplayedRepoCards[0].RepoPath);
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
    ///     Test that DisplayedRepoCards sorts favorites above non-favorites regardless of
    ///     LastLaunchedUtc.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedRepoCards_SortsFavoritesAboveNonFavorites()
    {
        // Arrange: a non-favorite repo launched more recently than a favorite repo
        var favoriteRepo = CreateTempDirectory();
        var recentNonFavoriteRepo = CreateTempDirectory();
        var settings = new AppSettings
        {
            RecentRepos =
            [
                new RecentRepo { Path = favoriteRepo, IsFavorite = true, LastLaunchedUtc = DateTimeOffset.UtcNow.AddDays(-10) },
                new RecentRepo { Path = recentNonFavoriteRepo, IsFavorite = false, LastLaunchedUtc = DateTimeOffset.UtcNow }
            ]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act / Assert: the favorite (older launch) sorts above the more-recently-launched
        // non-favorite
        Assert.Equal(favoriteRepo, viewModel.DisplayedRepoCards[0].RepoPath);
        Assert.Equal(recentNonFavoriteRepo, viewModel.DisplayedRepoCards[1].RepoPath);
    }

    /// <summary>
    ///     Test that DisplayedRepoCards sorts by LastLaunchedUtc descending within the same
    ///     favorite tier, with never-launched (null) repos sorted last.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_DisplayedRepoCards_SortsByLastLaunchedUtcDescendingWithNullsLast()
    {
        // Arrange: three non-favorite repos - one launched recently, one launched earlier, one
        // never launched
        var recentlyLaunched = CreateTempDirectory();
        var earlierLaunched = CreateTempDirectory();
        var neverLaunched = CreateTempDirectory();
        var settings = new AppSettings
        {
            RecentRepos =
            [
                new RecentRepo { Path = neverLaunched },
                new RecentRepo { Path = earlierLaunched, LastLaunchedUtc = DateTimeOffset.UtcNow.AddDays(-1) },
                new RecentRepo { Path = recentlyLaunched, LastLaunchedUtc = DateTimeOffset.UtcNow }
            ]
        };
        var viewModel = new MainWindowViewModel(settings, configDirectory: CreateTempDirectory());

        // Act / Assert
        Assert.Equal(recentlyLaunched, viewModel.DisplayedRepoCards[0].RepoPath);
        Assert.Equal(earlierLaunched, viewModel.DisplayedRepoCards[1].RepoPath);
        Assert.Equal(neverLaunched, viewModel.DisplayedRepoCards[2].RepoPath);
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
    ///     Test that ApplySettings invalidates the shared PackageVersionCache, so a changed
    ///     package-source path is reflected immediately rather than served from a stale cache.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_ApplySettings_InvalidatesSharedPackageVersionCache()
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
        Assert.False(viewModel.RepoCards[0].IsUpgradeAvailable);

        // Act: apply settings pointing at a new package source containing a newer version
        var newSourceDir = CreateTempDirectory();
        CreatePackageZip(newSourceDir, "contoso-agents", "2.0.0");
        var updated = new AppSettings { PackageSourcePath = newSourceDir };
        viewModel.ApplySettings(updated);

        // Assert: the new source's package is observed immediately, not masked by a cache entry
        // for the old (empty) source directory
        Assert.True(viewModel.RepoCards[0].IsUpgradeAvailable);
        Assert.Equal("2.0.0", viewModel.RepoCards[0].LatestAvailableVersion);
    }

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
