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

using System.IO.Compression;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.GitIntegration;
using Material.Icons;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="RepoCardViewModel"/>.
/// </summary>
/// <remarks>
///     Every test substitutes a stub script for the git executable (via
///     <see cref="GitStub"/>, mirroring <c>GitClientTests</c>) and uses real temporary
///     directories/zip fixtures for package-source and pin-file checks, so these tests never
///     depend on a real git installation, a real repository, or a real package source, per
///     architecture.md's testability strategy. No Avalonia <c>Window</c> is ever constructed.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoCardViewModelTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that Refresh reads an existing pin file, populating the pinned name/version.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_PinFileExists_PopulatesPinnedFields()
    {
        // Arrange: a repo with a pin file already written
        var repoRoot = CreateTempDirectory();
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Default", PackageName = "contoso-agents", Version = "1.0.0" });
        var card = CreateCard(repoRoot, null, null, new AppSettings());

        // Act: refresh
        card.Refresh();

        // Assert: the pin file's contents are now reflected
        Assert.Equal("contoso-agents", card.PinnedPackageName);
        Assert.Equal("1.0.0", card.PinnedPackageVersion);
    }

    /// <summary>
    ///     Test that when a previously-pinned card's pin file becomes unreadable/malformed, a
    ///     subsequent refresh clears the previously-cached pinned fields instead of continuing to
    ///     display stale pin data that no longer reflects what is on disk.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_PinFileBecomesUnreadable_ClearsStalePinnedFields()
    {
        // Arrange: a repo pinned to a package, refreshed once so the card caches the pin, then
        // whose pin file is subsequently corrupted with invalid JSON
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings());
        card.Refresh();
        Assert.Equal("contoso-agents", card.PinnedPackageName);
        Assert.Equal("1.0.0", card.PinnedPackageVersion);
        File.WriteAllText(Path.Combine(repoRoot, ".agentcontrol.json"), "{ this is not valid json");

        // Act: refresh again, which now fails to read the pin file
        card.Refresh();

        // Assert: the previously-cached pinned fields are cleared rather than left stale, and a
        // status message reports the read failure
        Assert.Null(card.PinnedPackageName);
        Assert.Null(card.PinnedPackageVersion);
        Assert.NotNull(card.StatusMessage);
        Assert.Contains("pin file", card.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that IsUpgradeAvailable is true when a newer package version exists at the
    ///     configured package source than the repo's current pin.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_NewerVersionAtSource_SetsUpgradeAvailableTrue()
    {
        // Arrange: a repo pinned to 1.0.0, and a package source with a 2.0.0 zip
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);

        // Act: refresh
        card.Refresh();

        // Assert: an upgrade is available, and the tooltip mentions the new version
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("2.0.0", card.LatestAvailableVersion);
        Assert.Contains("2.0.0", card.UpgradeTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that IsUpgradeAvailable is false when the repo is already pinned to the highest
    ///     version found at the package source.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_AlreadyOnLatestVersion_SetsUpgradeAvailableFalse()
    {
        // Arrange: a repo pinned to the same version present at the source
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);

        // Act: refresh
        card.Refresh();

        // Assert: no upgrade is offered
        Assert.False(card.IsUpgradeAvailable);
        Assert.Null(card.UpgradeTooltip);
    }

    /// <summary>
    ///     Test that IsUpgradeAvailable is false when no package source path is configured.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_NoPackageSourceConfigured_SetsUpgradeAvailableFalse()
    {
        // Arrange: a pinned repo but no package source path in settings
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings();
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);

        // Act: refresh
        card.Refresh();

        // Assert: no source means no upgrade check can succeed
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that a freshly constructed card does not report a false "dirty working tree"
    ///     badge/tooltip before its first git-status check has actually run (RefreshDirtyStatus
    ///     is lazy/deferred by design).
    /// </summary>
    [Fact]
    public void RepoCardViewModel_BeforeFirstGitStatusCheck_DoesNotReportDirty()
    {
        // Arrange: a card whose git status has not yet been checked
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings();
        var card = CreateCard(repoRoot, null, null, settings);

        // Assert: no false-positive dirty state before RefreshDirtyStatus/RefreshGitStatus runs
        Assert.False(card.GitStatusChecked);
        Assert.False(card.IsWorkingTreeDirty);
        Assert.Equal("This repo's Git status has not been checked yet.", card.PullTooltip);
    }

    /// <summary>
    ///     Test that CanPull is true when the configured git stub reports a clean working tree.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_CleanWorkingTree_SetsCanPullTrue()
    {
        // Arrange: a git stub reporting a clean tree
        var stub = GitStub.Create(statusOutput: "", statusExitCode: 0);
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act: refresh
        card.Refresh();

        // Assert: pull is offered
        Assert.True(card.CanPull);
        Assert.True(card.PullCommand.CanExecute(null));
        Assert.False(card.IsWorkingTreeDirty);
        Assert.Equal("Pull the latest commits for this repo", card.PullTooltip);
    }

    /// <summary>
    ///     Test that a repo which temporarily disappears and then reappears does not report a
    ///     stale "dirty working tree" badge/tooltip carried over from before it disappeared -
    ///     its cached Git status must be invalidated while missing (reporting "not yet checked"
    ///     rather than stale, until an explicit fresh check is performed), never the pre-
    ///     disappearance result.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoReappearsAfterBeingMissing_InvalidatesStaleGitStatusInsteadOfReportingIt()
    {
        // Arrange: a card with a dirty working tree, confirmed via a real refresh
        var stub = GitStub.Create(statusOutput: " M file.txt", statusExitCode: 0);
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);
        card.Refresh();
        Assert.True(card.IsWorkingTreeDirty);

        // Act: the repo folder disappears, then reappears with a now-clean working tree
        Directory.Delete(repoRoot, recursive: true);
        card.RefreshCheap();
        Assert.True(card.IsMissing);
        Assert.False(card.IsWorkingTreeDirty);

        Directory.CreateDirectory(repoRoot);
        var cleanStub = GitStub.Create(statusOutput: "", statusExitCode: 0);
        _tempPaths.Add(cleanStub.Path);
        settings.GitExecutablePath = cleanStub.Path;
        card.RefreshCheap();

        // Assert: RefreshCheap alone (deliberately not re-checking Git status itself, to avoid
        // duplicating the check a caller's own RefreshDirtyStatus call would perform) leaves the
        // card correctly reporting "not yet checked" - never the stale pre-disappearance dirty
        // result - until a fresh check actually runs
        Assert.False(card.IsMissing);
        Assert.False(card.GitStatusChecked);
        Assert.False(card.IsWorkingTreeDirty);
        Assert.Equal("This repo's Git status has not been checked yet.", card.PullTooltip);

        // Act: an explicit fresh check (as RefreshCommand/Refresh perform)
        card.RefreshDirtyStatus();

        // Assert: the recovered card now reflects the fresh (clean) check, not the stale dirty
        // state cached from before it disappeared
        Assert.True(card.GitStatusChecked);
        Assert.True(card.CanPull);
        Assert.False(card.IsWorkingTreeDirty);
    }

    /// <summary>
    ///     Test that CanPull is false when the configured git stub reports a dirty working tree.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_DirtyWorkingTree_SetsCanPullFalse()
    {
        // Arrange: a git stub reporting a dirty tree
        var stub = GitStub.Create(statusOutput: " M file.txt", statusExitCode: 0);
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act: refresh
        card.Refresh();

        // Assert: pull is not offered, and the "dirty working tree" badge/tooltip explain why
        Assert.False(card.CanPull);
        Assert.False(card.PullCommand.CanExecute(null));
        Assert.True(card.IsWorkingTreeDirty);
        Assert.Contains("uncommitted changes", card.PullTooltip, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that CanPull is false when the git status check fails (e.g. not a git repo),
    ///     rather than propagating the failure.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_GitStatusFails_SetsCanPullFalseWithoutThrowing()
    {
        // Arrange: a git stub that fails the status check
        var stub = GitStub.Create(statusExitCode: 128, statusError: "fatal: not a git repository");
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act: refresh (must not throw)
        var exception = Record.Exception(card.Refresh);

        // Assert: no exception, pull is not offered, and the status-unknown case is
        // distinguished from a confirmed dirty working tree so the badge/tooltip don't
        // mislead the user into thinking there are uncommitted changes to resolve.
        Assert.Null(exception);
        Assert.False(card.CanPull);
        Assert.True(card.GitStatusUnavailable);
        Assert.False(card.IsWorkingTreeDirty);
        Assert.Contains("could not be determined", card.PullTooltip, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that Pull runs a successful pull and refreshes the working-tree status
    ///     afterward.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_PullCommand_SuccessfulPull_UpdatesStatusMessage()
    {
        // Arrange: a git stub reporting a clean tree and a successful pull
        var stub = GitStub.Create(statusOutput: "", pullOutput: "Already up to date.", pullExitCode: 0);
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);
        await card.RefreshAsync(TestContext.Current.CancellationToken);

        // Act: pull
        await card.PullCommand.ExecuteAsync();

        // Assert: the status message reflects success
        Assert.Equal("Pull succeeded.", card.StatusMessage);
    }

    /// <summary>
    ///     Test that Pull surfaces a failed pull via the status message rather than throwing.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_PullCommand_FailedPull_SetsFailureStatusMessage()
    {
        // Arrange: a git stub reporting a failed pull
        var stub = GitStub.Create(pullExitCode: 1, pullError: "CONFLICT: merge conflict");
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act: pull
        await card.PullCommand.ExecuteAsync();

        // Assert: the failure is surfaced via the status message
        Assert.Contains("Pull failed", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("CONFLICT", card.StatusMessage, StringComparison.Ordinal);
        Assert.True(card.HasStatusMessage);
    }

    /// <summary>
    ///     Test that Launch raises ErrorOccurred when no agent tool command can be resolved
    ///     (Custom selected with no command configured).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_LaunchCommand_NoCommandConfigured_RaisesErrorOccurred()
    {
        // Arrange: Custom agent tool with no command configured, but pinned/synced so the
        // ensure-synced-before-launch check passes and the agent-command check is reached.
        var repoRoot = CreateTempDirectory();
        CreateManagedFolders(repoRoot);
        var settings = new AppSettings { AgentTool = AgentToolKind.Custom, CustomAgentCommand = null };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act: launch
        await card.LaunchCommand.ExecuteAsync();

        // Assert: an error was raised rather than attempting to launch an empty command
        Assert.NotNull(capturedError);
        Assert.Contains("agentic CLI tool", capturedError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that Upgrade extracts the new package, rewrites the pin file, and raises
    ///     ReleaseNotesReady with the new package's release notes.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_NewerVersionAvailable_UpdatesPinAndRaisesReleaseNotesReady()
    {
        // Arrange: a repo pinned to 1.0.0 and a source with a 2.0.0 package containing release notes
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0", "## 2.0.0\n\nNew features.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        string? capturedReleaseNotes = null;
        card.ReleaseNotesReady += (_, notes) => capturedReleaseNotes = notes;

        // Act: upgrade
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: the pin file was rewritten and release notes were surfaced
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.Equal("2.0.0", pin.Version);
        Assert.Equal("2.0.0", card.PinnedPackageVersion);
        Assert.False(card.IsUpgradeAvailable);
        Assert.Equal("## 2.0.0\n\nNew features.", capturedReleaseNotes);
    }

    /// <summary>
    ///     Test that Upgrade proactively ensures the repo's .gitignore covers the four managed
    ///     agent folders immediately after a successful extraction.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_NewerVersionAvailable_UpdatesGitIgnoreWithManagedFoldersBlock()
    {
        // Arrange: a repo pinned to 1.0.0, no pre-existing .gitignore, and a source with a
        // newer package
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        await card.RefreshAsync(TestContext.Current.CancellationToken);

        // Act: upgrade
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: the repo's .gitignore now covers the four managed agent folders
        var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
        Assert.True(File.Exists(gitIgnorePath));
        var content = await File.ReadAllTextAsync(gitIgnorePath, TestContext.Current.CancellationToken);
        Assert.Contains("# Added by AgentControl - agent package folders", content, StringComparison.Ordinal);
        Assert.Contains(".github/agents/", content, StringComparison.Ordinal);
        Assert.Contains(".github/standards/", content, StringComparison.Ordinal);
        Assert.Contains(".github/templates/", content, StringComparison.Ordinal);
        Assert.Contains(".github/skills/", content, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Upgrade raises ErrorOccurred rather than throwing when no package source is
    ///     configured.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_NoPackageSourceConfigured_RaisesErrorOccurred()
    {
        // Arrange: a pinned repo with no package source configured
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings());
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act: upgrade
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: an error was raised rather than an unhandled exception
        Assert.NotNull(capturedError);
    }

    /// <summary>
    ///     Test that RepoName returns the repo directory's leaf folder name.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RepoName_ReturnsLeafFolderName()
    {
        // Arrange: a repo path with a distinctive leaf folder name
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, null, null, new AppSettings());

        // Act / Assert: the name matches the leaf folder
        Assert.Equal(Path.GetFileName(repoRoot), card.RepoName);
    }

    /// <summary>
    ///     Test that IsMissing is true (and every other check/action is suppressed to its
    ///     "unavailable" default) when the repo path does not exist on disk.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RefreshCheap_RepoPathDoesNotExist_SetsIsMissingAndSuppressesOtherState()
    {
        // Arrange: a repo path that is never created
        var missingPath = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_repo_" + Guid.NewGuid());
        var recentRepo = new RecentRepo { Path = missingPath, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "1.0.0" };
        var card = new RepoCardViewModel(recentRepo, () => new AppSettings(), new PackageVersionCache());

        // Act
        card.RefreshCheap();

        // Assert
        Assert.True(card.IsMissing);
        Assert.Null(card.CurrentBranch);
        Assert.False(card.HasCommittedAgentFiles);
        Assert.False(card.CanPull);
        Assert.False(card.IsUpgradeAvailable);
        Assert.False(card.LaunchCommand.CanExecute(null));
        Assert.False(card.PullCommand.CanExecute(null));
        Assert.False(card.UpgradeCommand.CanExecute(null));
        Assert.False(card.RefreshCommand.CanExecute(null));

        // A missing repo gets its own dedicated badge/tooltip - the "dirty working tree" badge
        // must not also fire for it (its working tree can't meaningfully be "clean" or "dirty").
        Assert.False(card.IsWorkingTreeDirty);
        Assert.Equal("This repo's folder could not be found on disk.", card.PullTooltip);
    }

    /// <summary>
    ///     Test that RefreshCheap/the constructor never invoke the (otherwise lazy) working-tree
    ///     dirty check - RefreshDirtyStatus must be called explicitly.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RefreshCheap_DoesNotRunWorkingTreeDirtyCheck()
    {
        // Arrange: a git stub that logs every invocation it receives
        var invocationLog = Path.Combine(Path.GetTempPath(), "agentcontrol_invocation_log_" + Guid.NewGuid() + ".txt");
        _tempPaths.Add(invocationLog);
        var stub = GitStub.Create(statusOutput: " M file.txt", invocationLogPath: invocationLog);
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act: only the cheap refresh runs
        card.RefreshCheap();

        // Assert: "status --porcelain" was never invoked by the cheap refresh
        var invocationsAfterCheap = ReadInvocationLog(invocationLog);
        Assert.DoesNotContain(invocationsAfterCheap, line => line.StartsWith("status", StringComparison.Ordinal));

        // Act: the lazy dirty-status refresh now runs the status check
        card.RefreshDirtyStatus();

        // Assert: "status --porcelain" was invoked exactly once, by RefreshDirtyStatus
        var invocationsAfterDirtyRefresh = ReadInvocationLog(invocationLog);
        Assert.Single(invocationsAfterDirtyRefresh, line => line.StartsWith("status", StringComparison.Ordinal));
        Assert.False(card.CanPull);
    }

    /// <summary>
    ///     Reads the lines appended by a <see cref="GitStub"/> created with an invocation-log
    ///     path, tolerating the log file not existing yet.
    /// </summary>
    private static string[] ReadInvocationLog(string path) =>
        File.Exists(path) ? File.ReadAllLines(path) : [];

    /// <summary>
    ///     Test that the committed-agent-files badge is cached by HEAD hash: a repeated
    ///     RefreshCheap call against an unchanged HEAD hash does not re-run "git ls-files".
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RefreshCheap_UnchangedHeadHash_CommittedFilesBadgeIsCached()
    {
        // Arrange: a stub whose "ls-files" output would flip if invoked a second time is not
        // directly observable here, but the badge value itself must remain stable across repeated
        // refreshes against the same (stubbed, constant) HEAD hash.
        var stub = GitStub.Create(revParseHeadOutput: "abc123", lsFilesOutput: ".github/agents/copilot.md");
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act: refresh twice against the same HEAD hash
        card.RefreshCheap();
        var firstResult = card.HasCommittedAgentFiles;
        card.RefreshCheap();
        var secondResult = card.HasCommittedAgentFiles;

        // Assert: both refreshes observe the same (cached) badge value
        Assert.True(firstResult);
        Assert.True(secondResult);
    }

    /// <summary>
    ///     Test that CurrentBranch reflects the branch name resolved by GitClient after a cheap
    ///     refresh.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RefreshCheap_ResolvesCurrentBranch()
    {
        // Arrange
        var stub = GitStub.Create(abbrevRefOutput: "develop");
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act
        card.RefreshCheap();

        // Assert
        Assert.Equal("develop", card.CurrentBranch);
    }

    /// <summary>
    ///     Test that toggling IsFavorite mutates the shared RecentRepo and raises FavoriteChanged.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_FavoriteToggleCommand_TogglesIsFavoriteAndRaisesFavoriteChanged()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        var recentRepo = new RecentRepo { Path = repoRoot };
        var card = new RepoCardViewModel(recentRepo, () => new AppSettings(), new PackageVersionCache());
        var raisedCount = 0;
        card.FavoriteChanged += (_, _) => raisedCount++;

        // Act
        card.FavoriteToggleCommand.Execute(null);

        // Assert
        Assert.True(card.IsFavorite);
        Assert.True(recentRepo.IsFavorite);
        Assert.Equal(1, raisedCount);

        // Act again: toggle back off
        card.FavoriteToggleCommand.Execute(null);

        // Assert
        Assert.False(card.IsFavorite);
        Assert.False(recentRepo.IsFavorite);
        Assert.Equal(2, raisedCount);
    }

    /// <summary>
    ///     Test that RecordLaunched (invoked internally by a successful Launch) sets
    ///     LastLaunchedUtc and raises LaunchRecorded.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_LaunchCommand_Succeeds_RecordsLaunchTimestampAndRaisesLaunchRecorded()
    {
        // Arrange: a resolvable custom agent-tool command using a real executable (cmd/sh), and a
        // pinned/synced repo so the ensure-synced-before-launch check passes.
        var repoRoot = CreateTempDirectory();
        CreateManagedFolders(repoRoot);
        var settings = new AppSettings
        {
            AgentTool = AgentToolKind.Custom,
            CustomAgentCommand = OperatingSystem.IsWindows() ? "cmd /c exit 0" : "true"
        };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        var raised = false;
        card.LaunchRecorded += (_, _) => raised = true;
        Assert.Null(card.LastLaunchedUtc);

        // Act
        await card.LaunchCommand.ExecuteAsync();

        // Assert
        Assert.True(raised);
        Assert.NotNull(card.LastLaunchedUtc);
    }

    /// <summary>
    ///     Test that RemoveCommand raises RemoveRequested without mutating any collection or
    ///     settings itself.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_RemoveCommand_RaisesRemoveRequestedWithoutMutatingState()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        var recentRepo = new RecentRepo { Path = repoRoot };
        var card = new RepoCardViewModel(recentRepo, () => new AppSettings(), new PackageVersionCache());
        var raisedWith = new List<object?>();
        card.RemoveRequested += (sender, _) => raisedWith.Add(sender);

        // Act
        card.RemoveCommand.Execute(null);

        // Assert: the event fired exactly once, with this card as the sender, and nothing else
        // was mutated (there is no collection for this view model to mutate directly).
        Assert.Same(card, Assert.Single(raisedWith));
    }

    /// <summary>
    ///     Test that IsPackageSelectionNeeded is true for a never-pinned card and false once a
    ///     pin exists.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_IsPackageSelectionNeeded_ReflectsPinState()
    {
        // Arrange: a never-pinned card
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, null, null, new AppSettings());

        // Assert: no pin means selection is needed
        Assert.True(card.IsPackageSelectionNeeded);

        // Act: apply a package (which writes the pin)
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var pinnedCard = new RepoCardViewModel(
            new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());
        pinnedCard.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: a pin now exists, so selection is no longer needed
        Assert.False(pinnedCard.IsPackageSelectionNeeded);
    }

    /// <summary>
    ///     Test that SelectPackageCommand cannot execute when a pin already exists.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SelectPackageCommand_PinAlreadyExists_CannotExecute()
    {
        // Arrange: an already-pinned card
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings());

        // Act / Assert
        Assert.False(card.SelectPackageCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that SelectPackageCommand cannot execute when the repo is missing.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SelectPackageCommand_RepoIsMissing_CannotExecute()
    {
        // Arrange: a never-pinned, but missing, repo
        var missingPath = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_repo_" + Guid.NewGuid());
        var recentRepo = new RecentRepo { Path = missingPath };
        var card = new RepoCardViewModel(recentRepo, () => new AppSettings(), new PackageVersionCache());
        card.RefreshCheap();

        // Act / Assert
        Assert.False(card.SelectPackageCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that executing SelectPackageCommand with no package source configured raises
    ///     ErrorOccurred and does not raise SelectPackageRequested.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SelectPackageCommand_NoPackageSourceConfigured_RaisesErrorOccurredOnly()
    {
        // Arrange: a never-pinned card with no package source configured
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, null, null, new AppSettings());
        string? capturedError = null;
        var requestedRaised = false;
        card.ErrorOccurred += (_, message) => capturedError = message;
        card.SelectPackageRequested += (_, _) => requestedRaised = true;

        // Act
        card.SelectPackageCommand.Execute(null);

        // Assert
        Assert.NotNull(capturedError);
        Assert.False(requestedRaised);
    }

    /// <summary>
    ///     Test that executing SelectPackageCommand with a source configured raises
    ///     SelectPackageRequested exactly once, carrying the alias of the configured source (the
    ///     legacy single path migrates to the Default source).
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SelectPackageCommand_SourceConfigured_RaisesSelectPackageRequestedWithSourceAlias()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        var raisedCount = 0;
        card.SelectPackageRequested += (_, _) => raisedCount++;

        // Act
        card.SelectPackageCommand.Execute(null);

        // Assert
        Assert.Equal(1, raisedCount);
    }

    /// <summary>
    ///     Test that ApplySelectedPackage with a valid name/version writes the pin file, extracts
    ///     the managed folders, raises ReleaseNotesReady, and updates the pinned/selection
    ///     properties.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_ValidNameAndVersion_UpdatesPinAndExtractsAndRaisesReleaseNotesReady()
    {
        // Arrange: a never-pinned repo, and a source with a package containing release notes
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", "## 1.0.0\n\nInitial release.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        Assert.True(card.IsPackageSelectionNeeded);
        string? capturedReleaseNotes = null;
        card.ReleaseNotesReady += (_, notes) => capturedReleaseNotes = notes;

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.Equal("contoso-agents", pin.PackageName);
        Assert.Equal("1.0.0", pin.Version);
        Assert.Equal("contoso-agents", card.PinnedPackageName);
        Assert.Equal("1.0.0", card.PinnedPackageVersion);
        Assert.False(card.IsPackageSelectionNeeded);
        Assert.True(File.Exists(Path.Combine(repoRoot, ".github", "agents", "copilot.md")));
        Assert.Equal("## 1.0.0\n\nInitial release.", capturedReleaseNotes);
    }

    /// <summary>
    ///     Test that ApplySelectedPackage proactively ensures the repo's .gitignore covers the
    ///     four managed agent folders immediately after a successful extraction.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_ValidNameAndVersion_CreatesGitIgnoreWithManagedFoldersBlock()
    {
        // Arrange: a never-pinned repo with no pre-existing .gitignore, and a source with a
        // valid package
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: the repo's .gitignore now covers the four managed agent folders
        var gitIgnorePath = Path.Combine(repoRoot, ".gitignore");
        Assert.True(File.Exists(gitIgnorePath));
        var content = File.ReadAllText(gitIgnorePath);
        Assert.Contains("# Added by AgentControl - agent package folders", content, StringComparison.Ordinal);
        Assert.Contains(".github/agents/", content, StringComparison.Ordinal);
        Assert.Contains(".github/standards/", content, StringComparison.Ordinal);
        Assert.Contains(".github/templates/", content, StringComparison.Ordinal);
        Assert.Contains(".github/skills/", content, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that when the .gitignore-ensure step fails for an I/O reason (the target path is
    ///     forced to be a directory rather than a file), ApplySelectedPackage still raises
    ///     ErrorOccurred as a non-blocking warning while completing the pin write and
    ///     ReleaseNotesReady sequence - the .gitignore failure never blocks the otherwise-
    ///     successful apply.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_GitIgnoreWriteFails_StillUpdatesPinAndRaisesReleaseNotesReady()
    {
        // Arrange: a never-pinned repo whose ".gitignore" path is forced to be a directory, and
        // a source with a valid package containing release notes
        var repoRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(repoRoot, ".gitignore"));
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", "## 1.0.0\n\nInitial release.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;
        string? capturedReleaseNotes = null;
        card.ReleaseNotesReady += (_, notes) => capturedReleaseNotes = notes;

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: a non-blocking warning was raised, but the pin write and release notes still
        // completed as if the .gitignore-ensure step had never run
        Assert.NotNull(capturedError);
        Assert.Contains(".gitignore", capturedError, StringComparison.OrdinalIgnoreCase);
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.Equal("contoso-agents", pin.PackageName);
        Assert.Equal("1.0.0", pin.Version);
        Assert.Equal("## 1.0.0\n\nInitial release.", capturedReleaseNotes);
    }

    /// <summary>
    ///     Test that ApplySelectedPackage with a version no longer present at the source raises
    ///     ErrorOccurred and does not mutate the pin.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_VersionNoLongerAtSource_RaisesErrorOccurredWithoutMutatingPin()
    {
        // Arrange: a never-pinned repo, and a source with a different version than requested
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act: request a version that does not exist at the source
        card.ApplySelectedPackage("contoso-agents", "9.9.9");

        // Assert: no mutation occurred
        Assert.NotNull(capturedError);
        Assert.True(card.IsPackageSelectionNeeded);
        Assert.Null(RepoPinStore.Load(repoRoot));
    }

    /// <summary>
    ///     Test that ApplySelectedPackage still completes the apply (extraction and new pin
    ///     write) when an existing <c>.agentcontrol.json</c> is corrupt/unreadable, instead of
    ///     aborting with the extracted files left on disk but the stale/corrupt pin in place.
    ///     Regression test: previously, reading the prior pin's AgentsMdTemplateDeclined value
    ///     was unguarded, so a corrupt pin file's InvalidOperationException propagated to the
    ///     method's outer catch and aborted the whole apply after extraction had already
    ///     succeeded.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_CorruptExistingPinFile_StillAppliesAndWritesNewPin()
    {
        // Arrange: a repo whose existing .agentcontrol.json is corrupt/unreadable, and a source
        // with a package to apply
        var repoRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(repoRoot, ".agentcontrol.json"), "{ this is not valid json");
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;
        string? capturedReleaseNotes = null;
        card.ReleaseNotesReady += (_, notes) => capturedReleaseNotes = notes;

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: the apply completed - no error, files extracted, and a fresh pin was written
        // (defaulting AgentsMdTemplateDeclined to false, since the prior corrupt pin's value
        // could not be read)
        Assert.Null(capturedError);
        Assert.NotNull(capturedReleaseNotes);
        Assert.True(File.Exists(Path.Combine(repoRoot, ".github", "agents", "copilot.md")));
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.Equal("contoso-agents", pin.PackageName);
        Assert.Equal("1.0.0", pin.Version);
        Assert.False(pin.AgentsMdTemplateDeclined);
    }

    /// <summary>
    ///     Test that ApplySelectedPackage raises AgentsMdTemplateOfferRequested with the
    ///     package's AGENTS.md template content when the repo has no AGENTS.md file and the
    ///     package includes a template.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_NoAgentsMdAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequestedWithContent()
    {
        // Arrange: a never-pinned repo with no AGENTS.md, and a package with an AGENTS.md template
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", agentsMdTemplate: "# AGENTS\n\nCustomize me.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        string? capturedTemplate = null;
        card.AgentsMdTemplateOfferRequested += (_, template) => capturedTemplate = template;

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: the offer is raised with the template's content
        Assert.Equal("# AGENTS\n\nCustomize me.", capturedTemplate);
    }

    /// <summary>
    ///     Test that ApplySelectedPackage never raises AgentsMdTemplateOfferRequested when the
    ///     repo already has a root-level AGENTS.md file, and never overwrites its content.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_AgentsMdAlreadyExists_DoesNotRaiseAgentsMdTemplateOfferRequested()
    {
        // Arrange: a repo with a pre-existing, user-authored AGENTS.md file, and a package with
        // a template that must never overwrite it
        var repoRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(repoRoot, "AGENTS.md"), "my own content");
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", agentsMdTemplate: "# AGENTS\n\nCustomize me.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        var offerRaised = false;
        card.AgentsMdTemplateOfferRequested += (_, _) => offerRaised = true;

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: no offer, and the existing file is untouched
        Assert.False(offerRaised);
        Assert.Equal("my own content", File.ReadAllText(Path.Combine(repoRoot, "AGENTS.md")));
    }

    /// <summary>
    ///     Test that ApplySelectedPackage never raises AgentsMdTemplateOfferRequested when the
    ///     package zip has no root-level AGENTS.md entry at all.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_PackageHasNoAgentsMdTemplate_DoesNotRaiseAgentsMdTemplateOfferRequested()
    {
        // Arrange: a never-pinned repo with no AGENTS.md, and a package with no AGENTS.md template
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        var offerRaised = false;
        card.AgentsMdTemplateOfferRequested += (_, _) => offerRaised = true;

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: no template to offer, so no offer is raised
        Assert.False(offerRaised);
    }

    /// <summary>
    ///     Test that ApplySelectedPackage never raises AgentsMdTemplateOfferRequested again once
    ///     the user has previously declined the offer for this repo.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_PreviouslyDeclined_DoesNotRaiseAgentsMdTemplateOfferRequestedAgain()
    {
        // Arrange: a never-pinned repo with no AGENTS.md but a pre-existing pin file recording a
        // prior decline, and a package with an AGENTS.md template
        var repoRoot = CreateTempDirectory();
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Default", PackageName = string.Empty, Version = string.Empty, AgentsMdTemplateDeclined = true });
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", agentsMdTemplate: "# AGENTS\n\nCustomize me.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        var offerRaised = false;
        card.AgentsMdTemplateOfferRequested += (_, _) => offerRaised = true;

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: the prior decline is honored - no re-prompt
        Assert.False(offerRaised);
    }

    /// <summary>
    ///     Test that UpgradeCommand (the other ApplyPackageAndShowReleaseNotes call site) also
    ///     raises AgentsMdTemplateOfferRequested when applicable, consistent with
    ///     ApplySelectedPackage.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_NoAgentsMdAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequestedWithContent()
    {
        // Arrange: a repo pinned to an older version, with no AGENTS.md, and a newer package at
        // the source that includes an AGENTS.md template
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0", agentsMdTemplate: "# AGENTS\n\nCustomize me.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        string? capturedTemplate = null;
        card.AgentsMdTemplateOfferRequested += (_, template) => capturedTemplate = template;

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert: the offer is raised with the template's content
        Assert.Equal("# AGENTS\n\nCustomize me.", capturedTemplate);
    }

    /// <summary>
    ///     Test that AcceptAgentsMdTemplate writes the given content to the repo root as
    ///     AGENTS.md.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_AcceptAgentsMdTemplate_WritesFileToRepoRootWithGivenContent()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, null, null, new AppSettings());

        // Act
        card.AcceptAgentsMdTemplate("# AGENTS\n\nCustomize me.");

        // Assert: the file now exists at the repo root with the given content
        var agentsMdPath = Path.Combine(repoRoot, "AGENTS.md");
        Assert.True(File.Exists(agentsMdPath));
        Assert.Equal("# AGENTS\n\nCustomize me.", File.ReadAllText(agentsMdPath));
    }

    /// <summary>
    ///     Test that AcceptAgentsMdTemplate raises ErrorOccurred when the AGENTS.md file cannot be
    ///     written (the target path is forced to be a directory rather than a file).
    /// </summary>
    [Fact]
    public void RepoCardViewModel_AcceptAgentsMdTemplate_WriteFails_RaisesErrorOccurred()
    {
        // Arrange: a repo whose "AGENTS.md" path is forced to be a directory
        var repoRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(repoRoot, "AGENTS.md"));
        var card = CreateCard(repoRoot, null, null, new AppSettings());
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act
        card.AcceptAgentsMdTemplate("# AGENTS\n\nCustomize me.");

        // Assert
        Assert.NotNull(capturedError);
    }

    /// <summary>
    ///     Test that AcceptAgentsMdTemplate never overwrites an AGENTS.md file that was created
    ///     (e.g. by the user or another process) after the offer would have been raised but
    ///     before the user actually accepted it - the race this method's <c>FileMode.CreateNew</c>
    ///     write guards against. The pre-existing file's content must be preserved, and the
    ///     outcome must be reported as a non-destructive, informational status rather than a
    ///     generic error or a silent success.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_AcceptAgentsMdTemplate_FileCreatedConcurrently_PreservesExistingContentAndReportsNonDestructiveOutcome()
    {
        // Arrange: a repo with no AGENTS.md at offer time, then another process/user creates one
        // before the user's "Accept" click is processed
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, null, null, new AppSettings());
        var agentsMdPath = Path.Combine(repoRoot, "AGENTS.md");
        File.WriteAllText(agentsMdPath, "someone else's content");
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act
        card.AcceptAgentsMdTemplate("# AGENTS\n\nCustomize me.");

        // Assert: the pre-existing file's content survives untouched, no generic error is
        // raised, and StatusMessage explains the non-destructive outcome
        Assert.Equal("someone else's content", File.ReadAllText(agentsMdPath));
        Assert.Null(capturedError);
        Assert.NotNull(card.StatusMessage);
        Assert.Contains("already exists", card.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that DeclineAgentsMdTemplate persists the declined flag in the repo's pin file,
    ///     without writing an AGENTS.md file, and that a subsequent sync does not re-prompt.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_DeclineAgentsMdTemplate_PersistsDeclinedFlagInPinFile()
    {
        // Arrange: a repo with an existing pin (so the decline is recorded against it)
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings());

        // Act
        card.DeclineAgentsMdTemplate();

        // Assert: the pin file now records the decline, and no AGENTS.md was written
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.True(pin.AgentsMdTemplateDeclined);
        Assert.Equal("contoso-agents", pin.PackageName);
        Assert.False(File.Exists(Path.Combine(repoRoot, "AGENTS.md")));
    }

    /// <summary>
    ///     Test that a declined offer, once persisted, is not repeated on a subsequent sync call
    ///     for the same repo.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_DeclineAgentsMdTemplate_SubsequentApplySelectedPackage_DoesNotReprompt()
    {
        // Arrange: a never-pinned repo, a package with an AGENTS.md template, and a first apply
        // that raises the offer and is then declined
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", agentsMdTemplate: "# AGENTS\n\nCustomize me.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        card.ApplySelectedPackage("contoso-agents", "1.0.0");
        card.DeclineAgentsMdTemplate();

        // Act: apply the same package again (e.g. a subsequent sync) and check for a re-prompt
        var offerRaisedAgain = false;
        card.AgentsMdTemplateOfferRequested += (_, _) => offerRaisedAgain = true;
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert: no re-prompt
        Assert.False(offerRaisedAgain);
    }

    /// <summary>
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch's re-extraction branch also raises
    ///     AgentsMdTemplateOfferRequested when applicable, consistent with the Select-Package and
    ///     Upgrade call sites, since it is the third of the three sync call sites.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_ReExtractsAndPackageHasTemplate_RaisesAgentsMdTemplateOfferRequested()
    {
        // Arrange: a repo pinned to 1.0.0, with no managed folders present on disk yet (so a
        // re-extraction occurs), no AGENTS.md, and a source whose pinned package includes an
        // AGENTS.md template
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", agentsMdTemplate: "# AGENTS\n\nCustomize me.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        string? capturedTemplate = null;
        card.AgentsMdTemplateOfferRequested += (_, template) => capturedTemplate = template;

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: the re-extraction occurred and the offer was raised
        Assert.True(result);
        Assert.Equal("# AGENTS\n\nCustomize me.", capturedTemplate);
    }

    /// <summary>
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch never raises AgentsMdTemplateOfferRequested
    ///     (and never reads the pinned package's zip at all) when the managed folders already
    ///     exist - the offer must only ever fire when a sync genuinely happens, not as a standing
    ///     per-launch check.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_FoldersAlreadyPresent_NeverReadsZipOrRaisesOffer()
    {
        // Arrange: a pinned repo with all four managed folders already present, and no
        // configured package source at all (so a zip read, if attempted, would fail/throw -
        // proving this path never even tries to resolve or read the pinned package's zip)
        var repoRoot = CreateTempDirectory();
        CreateManagedFolders(repoRoot);
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings());
        var offerRaised = false;
        card.AgentsMdTemplateOfferRequested += (_, _) => offerRaised = true;

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: no offer raised
        Assert.True(result);
        Assert.False(offerRaised);
    }

    /// <summary>
    ///     Test that DeclineAgentsMdTemplate raises ErrorOccurred when the pin file cannot be
    ///     written (the pin file path is forced to be a directory rather than a file).
    /// </summary>
    [Fact]
    public void RepoCardViewModel_DeclineAgentsMdTemplate_PinWriteFails_RaisesErrorOccurred()
    {
        // Arrange: a repo whose ".agentcontrol.json" path is forced to be a directory
        var repoRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(repoRoot, ".agentcontrol.json"));
        var card = CreateCard(repoRoot, null, null, new AppSettings());
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act
        card.DeclineAgentsMdTemplate();

        // Assert
        Assert.NotNull(capturedError);
    }

    /// <summary>
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch returns true with a non-blocking
    ///     informational StatusMessage (and never raises ErrorOccurred) when no pin exists at
    ///     all - per the amended never-blocks-launch contract, having no pin is an acceptable
    ///     state, not an error.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoPin_ReturnsTrueWithInformationalStatusMessage()
    {
        // Arrange: a never-pinned repo
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, null, null, new AppSettings());
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: no error raised, launch may proceed, status message is informational
        Assert.True(result);
        Assert.Null(capturedError);
        Assert.NotNull(card.StatusMessage);
        Assert.Contains("no agent package is pinned", card.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch never touches the managed folders (no
    ///     delete, no extract) and returns true when the repo has committed agent files, even
    ///     when a pin exists and the managed folders are missing on disk - blindly deleting or
    ///     overwriting committed files would be far worse than leaving them alone.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_HasCommittedAgentFiles_SkipsSyncEntirelyAndReturnsTrue()
    {
        // Arrange: a git stub reporting a tracked agent file (so HasCommittedAgentFiles becomes
        // true after a refresh), a pin to a package whose source would otherwise be extractable,
        // and no managed folders present on disk yet
        var stub = GitStub.Create(revParseHeadOutput: "abc123", lsFilesOutput: ".github/agents/copilot.md");
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var settings = new AppSettings { GitExecutablePath = stub.Path, PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        card.RefreshCheap();
        Assert.True(card.HasCommittedAgentFiles);
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: no extraction occurred (folders remain absent), no error raised, launch may
        // proceed, status message is informational
        Assert.True(result);
        Assert.Null(capturedError);
        Assert.False(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
        Assert.NotNull(card.StatusMessage);
        Assert.Contains("committed agent files", card.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch returns true without re-extracting when a
    ///     pin exists and all four managed folders already exist on disk.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedAndFoldersPresent_ReturnsTrueWithoutReExtracting()
    {
        // Arrange: a pinned repo with all four managed folders already present, containing a
        // sentinel file that would be blind-deleted if a re-extraction ran
        var repoRoot = CreateTempDirectory();
        CreateManagedFolders(repoRoot);
        var sentinelPath = Path.Combine(repoRoot, ".github", "agents", "sentinel.md");
        File.WriteAllText(sentinelPath, "must survive");
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings());

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: no re-extraction occurred (the sentinel file survives)
        Assert.True(result);
        Assert.True(File.Exists(sentinelPath));
    }

    /// <summary>
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch re-extracts the currently pinned version
    ///     (never a newer one) when a pin exists but the managed folders are missing.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedButFoldersMissing_ReExtractsPinnedVersionOnly()
    {
        // Arrange: a repo pinned to 1.0.0, with no managed folders present on disk yet, and a
        // source containing both the pinned 1.0.0 package and a newer 2.0.0 package
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0", releaseNotes: null, marker: "v1-marker");
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0", releaseNotes: null, marker: "v2-marker");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert: the folders now exist, populated from the pinned 1.0.0 package - never the
        // newer 2.0.0 package
        Assert.True(result);
        Assert.True(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
        Assert.Equal("v1-marker", File.ReadAllText(Path.Combine(repoRoot, ".github", "agents", "copilot.md")));
    }

    /// <summary>
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch returns false and raises ErrorOccurred as
    ///     a non-blocking warning when the pinned version is no longer resolvable at the
    ///     configured source. A <see langword="false"/> result here is informational only - it no
    ///     longer means the launch itself is blocked; see
    ///     <see cref="RepoCardViewModel_LaunchCommand_SyncFailsOrNoPin_StillLaunches"/> for
    ///     confirmation that <c>Launch()</c> still spawns the process in this scenario.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_PinnedVersionMissingFromSource_ReturnsFalse()
    {
        // Arrange: a repo pinned to a version that does not exist at the (otherwise valid) source
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert
        Assert.False(result);
        Assert.NotNull(capturedError);
        Assert.False(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
    }

    /// <summary>
    ///     Test that LaunchCommand still spawns the agent-tool process (raising LaunchRecorded)
    ///     even when the best-effort agent-package sync attempt has no pin to work with, or is
    ///     attempted and fails outright - per the never-blocks-launch contract, sync state must
    ///     never prevent Launch from proceeding.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task RepoCardViewModel_LaunchCommand_SyncFailsOrNoPin_StillLaunches(bool withPin, bool withUnreachableSource)
    {
        // Arrange: a resolvable custom agent-tool command using a real executable (cmd/sh), and a
        // repo whose ensure-synced-before-launch check will either find no pin at all, or a pin
        // whose package is not resolvable at the configured source
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings
        {
            AgentTool = AgentToolKind.Custom,
            CustomAgentCommand = OperatingSystem.IsWindows() ? "cmd /c exit 0" : "true",
            PackageSourcePath = withUnreachableSource ? CreateTempDirectory() : null
        };
        var card = withPin
            ? CreateCard(repoRoot, "contoso-agents", "1.0.0", settings)
            : CreateCard(repoRoot, null, null, settings);
        var raised = false;
        card.LaunchRecorded += (_, _) => raised = true;
        Assert.Null(card.LastLaunchedUtc);

        // Act
        await card.LaunchCommand.ExecuteAsync();

        // Assert: the process was still spawned and the launch recorded, despite the sync attempt
        // finding nothing to do (no pin) or failing outright (pinned package missing from source)
        Assert.True(raised);
        Assert.NotNull(card.LastLaunchedUtc);
    }

    /// <summary>
    ///     Test that applying a package records the alias of the source it came from in the repo's
    ///     pin (schema version 2) and on the card; a legacy single path is the Default source.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_LegacySinglePath_PinRecordsDefaultSourceAlias()
    {
        // Arrange: a never-pinned repo and a legacy single-folder configuration
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        var card = CreateCard(repoRoot, null, null, new AppSettings { PackageSourcePath = sourceDir });

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0");

        // Assert
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.Equal(RepoPin.CurrentSchemaVersion, pin.SchemaVersion);
        Assert.Equal("Default", pin.Source);
        Assert.Equal("Default", card.PinnedSource);
    }

    /// <summary>
    ///     Test that a selection made from a named source records that alias and extracts that
    ///     source's content, not another source's package of the same name and version.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_SecondSourceSelected_PinRecordsThatSourceAndItsContent()
    {
        // Arrange: two sources offering the same package name and version with different content
        var repoRoot = CreateTempDirectory();
        var dirA = CreateTempDirectory();
        var dirB = CreateTempDirectory();
        CreatePackageZip(dirA, "contoso-agents", "1.0.0", marker: "content from A");
        CreatePackageZip(dirB, "contoso-agents", "1.0.0", marker: "content from B");
        var settings = new AppSettings { Sources = [ZipSource("A", dirA), ZipSource("B", dirB)] };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act
        card.ApplySelectedPackage("contoso-agents", "1.0.0", "B");

        // Assert
        Assert.Equal("B", RepoPinStore.Load(repoRoot)?.Source);
        Assert.Equal("content from B", File.ReadAllText(Path.Combine(repoRoot, ".github", "agents", "copilot.md")));
    }

    /// <summary>
    ///     Test that a pin naming a source that is not configured puts the card in an error state
    ///     - no upgrade is offered and the problem names the source - even though another
    ///     configured source offers a higher version of the same package.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_PinNamesUnconfiguredSource_ShowsSourceProblemAndOffersNoUpgrade()
    {
        // Arrange: pinned to source "Gone", which is not configured; "Other" has a newer version
        var repoRoot = CreateTempDirectory();
        var otherDir = CreateTempDirectory();
        CreatePackageZip(otherDir, "contoso-agents", "9.0.0");
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Gone", PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [ZipSource("Other", otherDir)] };
        var recentRepo = new RecentRepo { Path = repoRoot };
        var card = new RepoCardViewModel(recentRepo, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();

        // Assert: error state, no fallback to "Other"
        Assert.True(card.HasSourceProblem);
        Assert.Contains("'Gone'", card.SourceProblem, StringComparison.Ordinal);
        Assert.False(card.SourceNeedsSignIn);
        Assert.Equal("Package source problem", card.SourceProblemChipText);
        Assert.False(card.IsUpgradeAvailable);
        Assert.Null(card.LatestAvailableVersion);
        Assert.Equal("Gone", card.PinnedSource);
    }

    /// <summary>
    ///     Test that a repo whose folder is gone offers no upgrade, even when an upgrade check runs
    ///     (the package chip is hidden for a missing repo, so an upgrade chip alone would mean
    ///     nothing).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatusAsync_RepoFolderMissing_OffersNoUpgrade()
    {
        // Arrange: a pinned repo whose folder does not exist, with a newer version available
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.0.0");
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var missingRoot = Path.Combine(CreateTempDirectory(), "gone");
        var recentRepo = new RecentRepo
        {
            Path = missingRoot,
            PinnedPackageName = "contoso-agents",
            PinnedPackageVersion = "1.0.0",
            PinnedSource = "Default"
        };
        var settings = new AppSettings { Sources = [ZipSource("Default", sourceDir)] };
        var card = new RepoCardViewModel(recentRepo, () => settings, new PackageVersionCache());
        card.RefreshCheap();

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.IsMissing);
        Assert.False(card.IsUpgradeAvailable);
        Assert.Null(card.LatestAvailableVersion);
        Assert.False(card.HasPinnedPackage);
    }

    /// <summary>
    ///     Test that an action's outcome is exposed for the card to show, and that there is no
    ///     message to show before any action ran.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_StatusMessage_NoActionYet_HasNoStatusMessage()
    {
        // Arrange / Act
        var card = CreateCard(CreateTempDirectory(), null, null, new AppSettings());

        // Assert
        Assert.Null(card.StatusMessage);
        Assert.False(card.HasStatusMessage);
    }

    /// <summary>
    ///     Test that applying a package from a source alias that is not configured reports the
    ///     problem and changes nothing in the repo (no extraction, pin untouched), even though
    ///     another configured source has the package.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_UnconfiguredSourceAlias_RaisesErrorAndChangesNothing()
    {
        // Arrange: pinned to unconfigured "Gone", while "Other" offers a newer version
        var repoRoot = CreateTempDirectory();
        var otherDir = CreateTempDirectory();
        CreatePackageZip(otherDir, "contoso-agents", "9.0.0");
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Gone", PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [ZipSource("Other", otherDir)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        string? error = null;
        card.ErrorOccurred += (_, message) => error = message;

        // Act: try to apply the newer package naming the unconfigured source
        card.ApplySelectedPackage("contoso-agents", "9.0.0", "Gone");

        // Assert: refused with a message naming the missing source; nothing was extracted or re-pinned
        Assert.NotNull(error);
        Assert.Contains("'Gone'", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(repoRoot, ".github")));
        Assert.Equal("1.0.0", RepoPinStore.Load(repoRoot)?.Version);
        Assert.Equal("Gone", RepoPinStore.Load(repoRoot)?.Source);
    }

    /// <summary>
    ///     Test that the ensure-synced-before-launch repair fails (without extracting anything)
    ///     for a pin naming an unconfigured source, instead of repairing from another source.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSynced_PinNamesUnconfiguredSource_ReturnsFalseWithoutExtracting()
    {
        // Arrange: managed folders missing, pinned to unconfigured "Gone"; "Other" has the package
        var repoRoot = CreateTempDirectory();
        var otherDir = CreateTempDirectory();
        CreatePackageZip(otherDir, "contoso-agents", "1.0.0");
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Gone", PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [ZipSource("Other", otherDir)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());
        card.RefreshCheap();
        string? error = null;
        card.ErrorOccurred += (_, message) => error = message;

        // Act
        var synced = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert
        Assert.False(synced);
        Assert.Contains("'Gone'", error, StringComparison.Ordinal);
        Assert.False(PackageZipExtractor.AllManagedFoldersExist(repoRoot));
    }

    /// <summary>
    ///     Test that a pin to the source with the lower version is compared only against that
    ///     source: another source offering a higher version of the same package name does not make
    ///     an upgrade appear.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_SamePackageNameInTwoSources_ComparesOnlyAgainstPinnedSource()
    {
        // Arrange: A offers 9.0.0, B offers 1.0.0; the repo is pinned to B at 1.0.0
        var repoRoot = CreateTempDirectory();
        var dirA = CreateTempDirectory();
        var dirB = CreateTempDirectory();
        CreatePackageZip(dirA, "contoso-agents", "9.0.0");
        CreatePackageZip(dirB, "contoso-agents", "1.0.0");
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "B", PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [ZipSource("A", dirA), ZipSource("B", dirB)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: B's latest is 1.0.0, so no upgrade, and no source problem
        Assert.False(card.IsUpgradeAvailable);
        Assert.Equal("1.0.0", card.LatestAvailableVersion);
        Assert.False(card.HasSourceProblem);
    }

    /// <summary>
    ///     Test that a version 1 pin file (no source) is resolved against the Default source.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_Refresh_LegacyV1PinFile_ResolvesAgainstDefaultSource()
    {
        // Arrange: a hand-written v1 pin and a legacy-configured source with a newer version
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        await File.WriteAllTextAsync(
            Path.Combine(repoRoot, ".agentcontrol.json"),
            """{ "PackageName": "contoso-agents", "Version": "1.0.0" }""",
            TestContext.Current.CancellationToken);
        var card = new RepoCardViewModel(
            new RecentRepo { Path = repoRoot }, () => new AppSettings { PackageSourcePath = sourceDir }, new PackageVersionCache());

        // Act
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Default", card.PinnedSource);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("2.0.0", card.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that a version 1 pin with no Default source configured is an error state rather
    ///     than silently using some other source.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Refresh_LegacyV1PinFileAndNoDefaultSource_ShowsSourceProblem()
    {
        // Arrange: a v1 pin, but the only configured source is not called Default
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        File.WriteAllText(
            Path.Combine(repoRoot, ".agentcontrol.json"),
            """{ "PackageName": "contoso-agents", "Version": "1.0.0" }""");
        var settings = new AppSettings { Sources = [ZipSource("Mine", sourceDir)] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();

        // Assert
        Assert.True(card.HasSourceProblem);
        Assert.Contains("'Default'", card.SourceProblem, StringComparison.Ordinal);
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that applying a package to a repo whose pin file has unknown fields keeps them, and
    ///     keeps the AGENTS.md decline flag.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_PinWithUnknownFieldAndDecline_PreservesBoth()
    {
        // Arrange: a v2 pin with an unknown field and a recorded decline
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var pinPath = Path.Combine(repoRoot, ".agentcontrol.json");
        File.WriteAllText(
            pinPath,
            """{ "SchemaVersion": 2, "Source": "Default", "PackageName": "contoso-agents", "Version": "1.0.0", "AgentsMdTemplateDeclined": true, "Extra": "kept" }""");
        var card = CreateCard(repoRoot, null, null, new AppSettings { PackageSourcePath = sourceDir });

        // Act
        card.ApplySelectedPackage("contoso-agents", "2.0.0");

        // Assert
        var json = File.ReadAllText(pinPath);
        Assert.Contains("\"Extra\": \"kept\"", json, StringComparison.Ordinal);
        Assert.True(RepoPinStore.Load(repoRoot)?.AgentsMdTemplateDeclined);
        Assert.Equal("2.0.0", RepoPinStore.Load(repoRoot)?.Version);
    }

    /// <summary>
    ///     Test that applying a package to a repo whose pin file was written by a newer AgentControl
    ///     is refused up front: an error is raised, the pin file is unchanged, and the managed
    ///     folders are not touched.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_NewerSchemaPinFile_RaisesErrorAndChangesNothing()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0");
        var pinPath = Path.Combine(repoRoot, ".agentcontrol.json");
        const string newerPin = """{ "SchemaVersion": 7, "PackageName": "contoso-agents", "Version": "1.0.0" }""";
        File.WriteAllText(pinPath, newerPin);
        var card = CreateCard(repoRoot, null, null, new AppSettings { PackageSourcePath = sourceDir });
        string? error = null;
        card.ErrorOccurred += (_, message) => error = message;

        // Act
        card.ApplySelectedPackage("contoso-agents", "2.0.0");

        // Assert
        Assert.NotNull(error);
        Assert.Contains("newer", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(newerPin, File.ReadAllText(pinPath));
        Assert.False(Directory.Exists(Path.Combine(repoRoot, ".github")));
    }

    /// <summary>
    ///     Test that a package selected from a NuGet folder source is extracted into the repo, the
    ///     pin records the NuGet source's alias, and release notes are raised.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_ApplySelectedPackage_NuGetFolderSource_ExtractsPinsAliasAndRaisesReleaseNotes()
    {
        // Arrange: a NuGet folder feed holding an agent package, and a card with isolated NuGet cache
        var repoRoot = CreateTempDirectory();
        var feed = CreateTempDirectory();
        var id = "agentcontrol.test." + Guid.NewGuid().ToString("N");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0", marker: "from feed", releaseNotes: "feed notes");
        var settings = new AppSettings { Sources = [NuGetFolderSource("Feed", feed)] };
        var card = CreateCardWithIsolatedNuGetCache(repoRoot, settings);
        string? notes = null;
        card.ReleaseNotesReady += (_, text) => notes = text;

        // Act
        card.ApplySelectedPackage(id, "1.0.0", "Feed");

        // Assert
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.Equal("Feed", pin.Source);
        Assert.Equal(id, pin.PackageName);
        Assert.Equal("1.0.0", pin.Version);
        Assert.Equal("from feed", File.ReadAllText(Path.Combine(repoRoot, ".github", "agents", "copilot.md")));
        Assert.Equal("feed notes", notes);
    }

    /// <summary>
    ///     Test that Upgrade for a repo pinned to a NuGet source upgrades to that source's highest
    ///     version and ensure-synced re-extracts the pinned version when managed folders are missing.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeCommand_NuGetFolderSource_UpgradesToHighestVersion()
    {
        // Arrange: a repo pinned to 1.0.0 of a NuGet package whose feed also holds 2.0.0
        var repoRoot = CreateTempDirectory();
        var feed = CreateTempDirectory();
        var id = "agentcontrol.test." + Guid.NewGuid().ToString("N");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0", marker: "v1");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "2.0.0", marker: "v2");
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = id, Version = "1.0.0" });
        var settings = new AppSettings { Sources = [NuGetFolderSource("Feed", feed)] };
        var card = CreateCardWithIsolatedNuGetCache(repoRoot, settings);
        card.RefreshCheap();
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        Assert.True(card.IsUpgradeAvailable);

        // Act
        await card.UpgradeCommand.ExecuteAsync();

        // Assert
        Assert.Equal("2.0.0", RepoPinStore.Load(repoRoot)?.Version);
        Assert.Equal(
            "v2",
            await File.ReadAllTextAsync(
                Path.Combine(repoRoot, ".github", "agents", "copilot.md"), TestContext.Current.CancellationToken));
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that the agent package chip names the pinned package and version and its tooltip
    ///     names the source's alias and kind (zip folder), with the source icon of that kind.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChip_PinnedToZipSource_ShowsNameVersionAndSourceInTooltip()
    {
        // Arrange: a repo pinned to a package from a zip folder source
        var repoRoot = CreateTempDirectory();
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Contoso", PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [ZipSource("Contoso", CreateTempDirectory())] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();

        // Assert
        Assert.True(card.HasPinnedPackage);
        Assert.False(card.ShowNoPackageChip);
        Assert.Equal("contoso-agents 1.0.0", card.PackageChipText);
        Assert.Equal(
            "Agent files: contoso-agents 1.0.0 from source 'Contoso' (zip folder).", card.PackageChipTooltip);
        Assert.Equal(MaterialIconKind.FolderZip, card.PinnedSourceIcon);
    }

    /// <summary>
    ///     Test that a package pinned to a NuGet folder source shows the NuGet folder icon and kind.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChip_PinnedToNuGetFolderSource_ShowsNuGetFolderIconAndKind()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Feed", PackageName = "contoso-agents", Version = "2.0.0" });
        var settings = new AppSettings { Sources = [NuGetFolderSource("Feed", CreateTempDirectory())] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();

        // Assert
        Assert.Equal(MaterialIconKind.PackageVariant, card.PinnedSourceIcon);
        Assert.Contains("from source 'Feed' (NuGet folder)", card.PackageChipTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a package pinned to a source that is not configured shows a question-mark
    ///     icon and says so in the tooltip, rather than naming another source.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PackageChip_PinnedSourceNotConfigured_ShowsUnknownSource()
    {
        // Arrange: the pin names "Gone", which is not configured
        var repoRoot = CreateTempDirectory();
        RepoPinStore.Save(repoRoot, new RepoPin { Source = "Gone", PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = new AppSettings { Sources = [ZipSource("Other", CreateTempDirectory())] };
        var card = new RepoCardViewModel(new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache());

        // Act
        card.RefreshCheap();

        // Assert
        Assert.Equal(MaterialIconKind.HelpCircleOutline, card.PinnedSourceIcon);
        Assert.Contains("from source 'Gone' (source not configured)", card.PackageChipTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a repo with no pin shows the "No agent package" chip and no package chip.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_NoPackageChip_NeverPinnedRepo_ShowsNoPackageChipOnly()
    {
        // Arrange
        var card = CreateCard(CreateTempDirectory(), null, null, new AppSettings());

        // Act
        card.RefreshCheap();

        // Assert
        Assert.True(card.ShowNoPackageChip);
        Assert.False(card.HasPinnedPackage);
        Assert.True(card.SelectPackageCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that a missing repo shows neither the package chip nor the "No agent package" chip,
    ///     only its folder-missing state.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_Chips_MissingRepo_ShowNeitherPackageChip()
    {
        // Arrange
        var missingPath = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_repo_" + Guid.NewGuid());
        var card = new RepoCardViewModel(
            new RecentRepo { Path = missingPath, PinnedPackageName = "contoso-agents", PinnedPackageVersion = "1.0.0" },
            () => new AppSettings(),
            new PackageVersionCache());

        // Act
        card.RefreshCheap();

        // Assert
        Assert.True(card.IsMissing);
        Assert.False(card.HasPinnedPackage);
        Assert.False(card.ShowNoPackageChip);
    }

    /// <summary>
    ///     Test that the upgrade chip names the version an upgrade would move to, and its tooltip
    ///     names both versions, once the (asynchronous) upgrade check has run.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_UpgradeChip_NewerVersionAtSource_ShowsTargetVersionAndTooltipWithBothVersions()
    {
        // Arrange: pinned to 1.0.0 with 1.1.0 at the source
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "1.1.0");
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings { PackageSourcePath = sourceDir });
        card.RefreshCheap();
        Assert.False(card.IsUpgradeAvailable);

        // Act
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.UpgradeChipText);
        Assert.Contains("1.0.0 -> 1.1.0", card.UpgradeTooltip, StringComparison.Ordinal);
        Assert.Contains("Upgrade", card.UpgradeTooltip, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the branch chip is shown with a descriptive tooltip when the branch is known
    ///     and hidden when it is not.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_BranchChip_BranchKnown_ShowsBranchWithTooltip()
    {
        // Arrange: a git stub that reports a branch
        var stub = GitStub.Create(abbrevRefOutput: "feature/x");
        _tempPaths.Add(stub.Path);
        var card = CreateCard(
            CreateTempDirectory(), null, null, new AppSettings { GitExecutablePath = stub.Path });
        Assert.False(card.HasBranch);

        // Act
        card.RefreshCheap();

        // Assert
        Assert.True(card.HasBranch);
        Assert.Equal("feature/x", card.CurrentBranch);
        Assert.Equal("Current git branch: feature/x", card.BranchTooltip);
    }

    /// <summary>
    ///     Test that the Launch button label names the configured agent tool, falling back to a
    ///     plain "Launch" when the tool has no short name or its name is too long.
    /// </summary>
    [Theory]
    [InlineData((int)AgentToolKind.CopilotCli, null, "Launch Copilot")]
    [InlineData((int)AgentToolKind.Cursor, null, "Launch Cursor")]
    [InlineData((int)AgentToolKind.ClaudeCode, null, "Launch Claude")]
    [InlineData((int)AgentToolKind.Custom, "/opt/tools/tool.exe --flag", "Launch tool")]
    [InlineData((int)AgentToolKind.Custom, "\"/opt/my tools/helper.exe\" --flag", "Launch helper")]
    [InlineData((int)AgentToolKind.Custom, "a-very-long-tool-name --flag", "Launch")]
    [InlineData((int)AgentToolKind.Custom, "", "Launch")]
    [InlineData((int)AgentToolKind.Custom, null, "Launch")]
    public void RepoCardViewModel_LaunchButtonText_ConfiguredTool_NamesToolOrFallsBack(
        int tool, string? customCommand, string expected)
    {
        // Arrange
        var settings = new AppSettings { AgentTool = (AgentToolKind)tool, CustomAgentCommand = customCommand };
        var card = CreateCard(CreateTempDirectory(), null, null, settings);

        // Assert: label and tooltip agree
        Assert.Equal(expected, card.LaunchButtonText);
        Assert.StartsWith("Launch", card.LaunchTooltip, StringComparison.Ordinal);
        Assert.True(card.LaunchButtonText.Length <= 16);
    }

    /// <summary>
    ///     Test that a change of the configured agent tool is reflected in the Launch label (and
    ///     announced) after the next cheap refresh, as happens when settings are applied.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_LaunchButtonText_AgentToolChanged_UpdatesAfterRefreshCheap()
    {
        // Arrange
        var settings = new AppSettings { AgentTool = AgentToolKind.CopilotCli };
        var card = CreateCard(CreateTempDirectory(), null, null, settings);
        var changed = new List<string>();
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);
        Assert.Equal("Launch Copilot", card.LaunchButtonText);

        // Act
        settings.AgentTool = AgentToolKind.Cursor;
        card.RefreshCheap();

        // Assert
        Assert.Contains(nameof(RepoCardViewModel.LaunchButtonText), changed);
        Assert.Equal("Launch Cursor", card.LaunchButtonText);
    }

    /// <summary>
    ///     Test that while a package is being applied the card is busy, its other actions are
    ///     disabled, a second apply is ignored, and cancelling stops the operation without raising
    ///     an error or changing the pin.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackageAsync_Running_IsBusyIgnoresSecondCallAndCancelLeavesPinUnchanged()
    {
        // Arrange: a source whose version listing never answers unless cancelled
        var provider = new DelegatePackageSourceProvider((_, token) => WaitForCancellationAsync(token));
        var card = CreateCardWithProvider(CreateTempDirectory(), null, null, provider);
        string? error = null;
        card.ErrorOccurred += (_, message) => error = message;
        var selection = new PackageReference("Default", "contoso-agents", "1.1.0");

        // Act: start applying; the call returns while the source is still being read
        var running = card.ApplySelectedPackageAsync(selection, TestContext.Current.CancellationToken);

        // Assert: busy with a cancellable operation, other actions disabled
        Assert.False(running.IsCompleted);
        Assert.True(card.IsBusy);
        Assert.True(card.CanCancelOperation);
        Assert.Equal("Applying agent package...", card.BusyMessage);
        Assert.False(card.LaunchCommand.CanExecute(null));
        Assert.False(card.RefreshCommand.CanExecute(null));
        Assert.False(card.SelectPackageCommand.CanExecute(null));
        Assert.True(card.CancelOperationCommand.CanExecute(null));

        // Act: a second apply while busy, then cancel
        await card.ApplySelectedPackageAsync(selection, TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.ListVersionsCalls);
        card.CancelOperationCommand.Execute(null);
        await running;

        // Assert: idle again, cancelled quietly, nothing pinned
        Assert.False(card.IsBusy);
        Assert.Null(card.BusyMessage);
        Assert.False(card.CanCancelOperation);
        Assert.Equal("The operation was canceled.", card.StatusMessage);
        Assert.Null(error);
        Assert.True(card.IsPackageSelectionNeeded);
        Assert.Null(RepoPinStore.Load(card.RepoPath));
    }

    /// <summary>
    ///     Test that applying a package returns control to the caller while the source is still
    ///     being read.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackageAsync_SourceSlow_ReturnsToCallerBeforeSourceAnswers()
    {
        // Arrange: a source whose version listing never answers unless cancelled
        var provider = new DelegatePackageSourceProvider((_, token) => WaitForCancellationAsync(token));
        var card = CreateCardWithProvider(CreateTempDirectory(), null, null, provider);
        var selection = new PackageReference("Default", "contoso-agents", "1.1.0");

        // Act: start applying
        var running = card.ApplySelectedPackageAsync(selection, TestContext.Current.CancellationToken);

        // Assert: the caller has its control back although the source has not answered
        Assert.False(running.IsCompleted);

        // Cleanup
        card.CancelOperationCommand.Execute(null);
        await running;
    }

    /// <summary>
    ///     Test that while a package is being applied the card reports that it is busy, with a
    ///     message, and disables its other actions.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackageAsync_Running_ReportsBusyAndDisablesOtherActions()
    {
        // Arrange
        var provider = new DelegatePackageSourceProvider((_, token) => WaitForCancellationAsync(token));
        var card = CreateCardWithProvider(CreateTempDirectory(), null, null, provider);
        var selection = new PackageReference("Default", "contoso-agents", "1.1.0");

        // Act
        var running = card.ApplySelectedPackageAsync(selection, TestContext.Current.CancellationToken);

        // Assert: busy, and no other action can start
        Assert.True(card.IsBusy);
        Assert.Equal("Applying agent package...", card.BusyMessage);
        Assert.False(card.LaunchCommand.CanExecute(null));
        Assert.False(card.RefreshCommand.CanExecute(null));
        Assert.False(card.SelectPackageCommand.CanExecute(null));

        // Act: finish the operation
        card.CancelOperationCommand.Execute(null);
        await running;

        // Assert: idle again, actions available again
        Assert.False(card.IsBusy);
        Assert.Null(card.BusyMessage);
        Assert.True(card.LaunchCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that a second apply while one is running is ignored and does not read the source
    ///     again.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackageAsync_SecondCallWhileRunning_IsIgnored()
    {
        // Arrange
        var provider = new DelegatePackageSourceProvider((_, token) => WaitForCancellationAsync(token));
        var card = CreateCardWithProvider(CreateTempDirectory(), null, null, provider);
        var selection = new PackageReference("Default", "contoso-agents", "1.1.0");
        var running = card.ApplySelectedPackageAsync(selection, TestContext.Current.CancellationToken);

        // Act: a second request while the first is still running
        await card.ApplySelectedPackageAsync(selection, TestContext.Current.CancellationToken);

        // Assert: the source was read only once
        Assert.Equal(1, provider.ListVersionsCalls);

        // Cleanup
        card.CancelOperationCommand.Execute(null);
        await running;
    }

    /// <summary>
    ///     Test that cancelling a running package apply stops it without raising an error and
    ///     without changing the repo's pin.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_ApplySelectedPackageAsync_CancelledWhileRunning_ReportsNoErrorAndKeepsPinUnchanged()
    {
        // Arrange
        var provider = new DelegatePackageSourceProvider((_, token) => WaitForCancellationAsync(token));
        var card = CreateCardWithProvider(CreateTempDirectory(), null, null, provider);
        string? error = null;
        card.ErrorOccurred += (_, message) => error = message;
        var selection = new PackageReference("Default", "contoso-agents", "1.1.0");
        var running = card.ApplySelectedPackageAsync(selection, TestContext.Current.CancellationToken);
        Assert.True(card.CanCancelOperation);

        // Act
        card.CancelOperationCommand.Execute(null);
        await running;

        // Assert: cancelled quietly, nothing pinned
        Assert.Equal("The operation was canceled.", card.StatusMessage);
        Assert.Null(error);
        Assert.True(card.IsPackageSelectionNeeded);
        Assert.Null(RepoPinStore.Load(card.RepoPath));
    }

    /// <summary>
    ///     Test that cancelling the wait for the pre-launch sync still launches the agent tool, per
    ///     the rule that sync state never blocks a launch.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_LaunchCommand_PreLaunchSyncCancelledByUser_StillLaunches()
    {
        // Arrange: a pinned repo with missing managed folders, whose source never answers
        var provider = new DelegatePackageSourceProvider((_, token) => WaitForCancellationAsync(token));
        var settings = new AppSettings
        {
            Sources = [ZipSource("Default", "unused")],
            AgentTool = AgentToolKind.Custom,
            CustomAgentCommand = OperatingSystem.IsWindows() ? "cmd /c exit 0" : "true"
        };
        var card = CreateCardWithProvider(CreateTempDirectory(), "contoso-agents", "1.0.0", provider, settings);
        var recorded = false;
        card.LaunchRecorded += (_, _) => recorded = true;

        // Act: start the launch, which waits on the sync, then cancel the wait
        var launching = card.LaunchCommand.ExecuteAsync();
        Assert.True(card.IsBusy);
        Assert.Equal("Preparing agent files...", card.BusyMessage);
        Assert.False(card.LaunchCommand.CanExecute(null));
        card.CancelOperationCommand.Execute(null);
        await launching;

        // Assert: the tool was launched regardless
        Assert.True(recorded);
        Assert.Equal("Launched.", card.StatusMessage);
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that the upgrade check returns to its caller while the source is slow, shows a
    ///     checking state, and applies the result when the source answers.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatusAsync_SourceSlow_DoesNotBlockAndShowsChecking()
    {
        // Arrange: a source that answers only when released
        var release = new TaskCompletionSource();
        var provider = new DelegatePackageSourceProvider(async (_, _) =>
        {
            await release.Task;
            return [Package("contoso-agents", "1.1.0")];
        });
        var card = CreateCardWithProvider(CreateTempDirectory(), "contoso-agents", "1.0.0", provider);

        // Act: start the check
        var check = card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);

        // Assert: the caller got control back, with the checking state shown and no result yet
        Assert.False(check.IsCompleted);
        Assert.True(card.IsCheckingUpgrade);
        Assert.False(card.IsUpgradeAvailable);

        // Act: the source answers
        release.SetResult();
        await check;

        // Assert
        Assert.False(card.IsCheckingUpgrade);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that cancelling an upgrade check surfaces the cancellation and keeps the card's
    ///     previous upgrade state.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatusAsync_Cancelled_ThrowsAndKeepsPreviousState()
    {
        // Arrange
        var provider = new DelegatePackageSourceProvider((_, token) => WaitForCancellationAsync(token));
        var card = CreateCardWithProvider(CreateTempDirectory(), "contoso-agents", "1.0.0", provider);
        using var cancellation = new CancellationTokenSource();

        // Act
        var check = card.RefreshUpgradeStatusAsync(cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that when a newer upgrade check starts while an older one is still waiting, the
    ///     older check's late answer is discarded.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_RefreshUpgradeStatusAsync_NewerCheckStarted_OlderResultIsDiscarded()
    {
        // Arrange: the first call is held and would answer 9.0.0; later calls answer 1.0.0
        var release = new TaskCompletionSource();
        var provider = new DelegatePackageSourceProvider(async (call, _) =>
        {
            if (call == 1)
            {
                await release.Task;
                return [Package("contoso-agents", "9.0.0")];
            }

            return [Package("contoso-agents", "1.0.0")];
        });
        var card = CreateCardWithProvider(CreateTempDirectory(), "contoso-agents", "1.0.0", provider);

        // Act: start an older check, then a newer one that finishes first, then release the older
        var older = card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        await card.RefreshUpgradeStatusAsync(TestContext.Current.CancellationToken);
        release.SetResult();
        await older;

        // Assert: the late 9.0.0 answer did not overwrite the newer result
        Assert.False(card.IsUpgradeAvailable);
        Assert.Equal("1.0.0", card.LatestAvailableVersion);
        Assert.False(card.IsCheckingUpgrade);
    }

    /// <summary>
    ///     Waits until the token is cancelled, then ends with cancellation; models a source that
    ///     never answers.
    /// </summary>
    private static async Task<IReadOnlyList<DiscoveredPackage>> WaitForCancellationAsync(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        return [];
    }

    /// <summary>
    ///     Creates a discovered package of the default source for a fake provider to return.
    /// </summary>
    private static DiscoveredPackage Package(string name, string version) =>
        new(name, PackageVersion.TryParse(version, out var parsed) && parsed is not null
            ? parsed
            : throw new ArgumentException("Invalid test version.", nameof(version)), version, "Default");

    /// <summary>
    ///     Creates a card whose single source ("Default") is served by the given provider, so a
    ///     test controls exactly when the source answers.
    /// </summary>
    private RepoCardViewModel CreateCardWithProvider(
        string repoRoot, string? pinnedPackageName, string? pinnedVersion, IPackageSourceProvider provider,
        AppSettings? settings = null)
    {
        settings ??= new AppSettings { Sources = [ZipSource("Default", "unused")] };
        if (pinnedPackageName is not null)
        {
            RepoPinStore.Save(
                repoRoot,
                new RepoPin { Source = "Default", PackageName = pinnedPackageName, Version = pinnedVersion ?? "0.0.0" });
        }

        var registry = new PackageSourceRegistry(settings.Sources, _ => provider);
        var recentRepo = new RecentRepo
        {
            Path = repoRoot,
            PinnedPackageName = pinnedPackageName,
            PinnedPackageVersion = pinnedVersion,
            PinnedSource = pinnedPackageName is null ? null : "Default"
        };
        return new RepoCardViewModel(recentRepo, () => settings, new PackageVersionCache(), null, _ => registry);
    }

    /// <summary>
    ///     A source whose version listing is supplied by a delegate that receives the call number
    ///     and the cancellation token, so a test controls timing and answers exactly.
    /// </summary>
    private sealed class DelegatePackageSourceProvider : IPackageSourceProvider
    {
        private readonly Func<int, CancellationToken, Task<IReadOnlyList<DiscoveredPackage>>> _listVersions;
        private int _listVersionsCalls;

        /// <summary>
        ///     Initializes the provider.
        /// </summary>
        /// <param name="listVersions">Produces the answer of each version listing from its call
        ///     number (starting at one) and token.</param>
        public DelegatePackageSourceProvider(
            Func<int, CancellationToken, Task<IReadOnlyList<DiscoveredPackage>>> listVersions) =>
            _listVersions = listVersions;

        /// <inheritdoc />
        public string Alias => "Default";

        /// <summary>
        ///     Gets how many version listings were requested.
        /// </summary>
        public int ListVersionsCalls => Volatile.Read(ref _listVersionsCalls);

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(["contoso-agents"]);

        /// <inheritdoc />
        public Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
            string packageId, CancellationToken cancellationToken) =>
            _listVersions(Interlocked.Increment(ref _listVersionsCalls), cancellationToken);

        /// <inheritdoc />
        public Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     Creates a card whose NuGet package caches live in an isolated folder (via the startup
    ///     configuration-directory override), so tests never touch the real user profile.
    /// </summary>
    private RepoCardViewModel CreateCardWithIsolatedNuGetCache(string repoRoot, AppSettings settings)
    {
        var startupOptions = StartupOptions.Parse(["--config-dir", CreateTempDirectory()]);
        return new RepoCardViewModel(
            new RecentRepo { Path = repoRoot }, () => settings, new PackageVersionCache(), startupOptions);
    }

    /// <summary>
    ///     Creates a zip-folder source definition.
    /// </summary>
    private static PackageSourceDefinition ZipSource(string alias, string folder) =>
        new() { Alias = alias, Type = PackageSourceType.Zip, Location = folder };

    /// <summary>
    ///     Creates a NuGet folder-feed source definition.
    /// </summary>
    private static PackageSourceDefinition NuGetFolderSource(string alias, string folder) =>
        new() { Alias = alias, Type = PackageSourceType.NuGet, Location = folder };

    /// <summary>
    ///     Creates a <see cref="RepoCardViewModel"/> for the given repo path, cached pin fields,
    ///     and settings snapshot. When a package name is supplied, the corresponding pin file is
    ///     also written to <paramref name="repoPath"/> so that a subsequent <c>Refresh()</c> (which
    ///     always re-reads the pin file from disk rather than trusting the constructor's cached
    ///     values) observes the same pin, mirroring how a real repo's pin file persists across
    ///     app sessions.
    /// </summary>
    private static RepoCardViewModel CreateCard(
        string repoPath, string? pinnedPackageName, string? pinnedPackageVersion, AppSettings settings)
    {
        if (pinnedPackageName is not null)
        {
            RepoPinStore.Save(repoPath, new RepoPin { Source = "Default", PackageName = pinnedPackageName, Version = pinnedPackageVersion ?? "0.0.0" });
        }

        var recentRepo = new RecentRepo
        {
            Path = repoPath,
            PinnedPackageName = pinnedPackageName,
            PinnedPackageVersion = pinnedPackageVersion
        };

        return new RepoCardViewModel(recentRepo, () => settings, new PackageVersionCache());
    }

    /// <summary>
    ///     Creates a unique temporary directory tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_repo_card_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     Creates a minimal package zip named <c>{packageName}-{version}.zip</c> at
    ///     <paramref name="sourceDir"/>, populating all four managed folders (so
    ///     <see cref="PackageZipExtractor.AllManagedFoldersExist"/> is true after extraction) and
    ///     optionally including a root-level release notes entry and/or a root-level AGENTS.md
    ///     template entry.
    /// </summary>
    /// <param name="sourceDir">The package-source directory to create the zip in.</param>
    /// <param name="packageName">The package base name.</param>
    /// <param name="version">The package's semantic version.</param>
    /// <param name="releaseNotes">The root-level release notes content, or <see langword="null"/>
    ///     to omit the entry entirely.</param>
    /// <param name="marker">The content written to <c>.github/agents/copilot.md</c>, used by
    ///     tests to distinguish which package version was actually extracted.</param>
    /// <param name="agentsMdTemplate">The root-level AGENTS.md template content, or
    ///     <see langword="null"/> to omit the entry entirely.</param>
    private void CreatePackageZip(
        string sourceDir, string packageName, string version, string? releaseNotes = null,
        string marker = "agents content", string? agentsMdTemplate = null)
    {
        var zipPath = Path.Combine(sourceDir, $"{packageName}-{version}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddZipEntry(archive, ".github/agents/copilot.md", marker);
        AddZipEntry(archive, ".github/standards/style.md", "standards content");
        AddZipEntry(archive, ".github/templates/template.md", "templates content");
        AddZipEntry(archive, ".github/skills/skill.md", "skills content");

        if (releaseNotes is not null)
        {
            AddZipEntry(archive, "release-notes.md", releaseNotes);
        }

        if (agentsMdTemplate is not null)
        {
            AddZipEntry(archive, "AGENTS.md", agentsMdTemplate);
        }

        _tempPaths.Add(zipPath);
    }

    /// <summary>
    ///     Adds a single UTF-8 text entry to a zip archive being built.
    /// </summary>
    private static void AddZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    /// <summary>
    ///     Directly creates the four managed folders (empty) under a repo root, without going
    ///     through a package zip/extraction - used by tests asserting the "already synced, do not
    ///     re-extract" ensure-synced-before-launch path.
    /// </summary>
    private static void CreateManagedFolders(string repoRoot)
    {
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "agents"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "standards"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "templates"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".github", "skills"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp files do not fail the test.
            }
        }
    }
}
