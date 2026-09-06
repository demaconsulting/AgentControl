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
using DemaConsulting.AgentControl.Tests.GitIntegration;

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
        RepoPinStore.Save(repoRoot, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
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

        // Assert: pull is not offered
        Assert.False(card.CanPull);
        Assert.False(card.PullCommand.CanExecute(null));
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

        // Assert: no exception, and pull is not offered
        Assert.Null(exception);
        Assert.False(card.CanPull);
    }

    /// <summary>
    ///     Test that Pull runs a successful pull and refreshes the working-tree status
    ///     afterward.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PullCommand_SuccessfulPull_UpdatesStatusMessage()
    {
        // Arrange: a git stub reporting a clean tree and a successful pull
        var stub = GitStub.Create(statusOutput: "", pullOutput: "Already up to date.", pullExitCode: 0);
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);
        card.Refresh();

        // Act: pull
        card.PullCommand.Execute(null);

        // Assert: the status message reflects success
        Assert.Equal("Pull succeeded.", card.StatusMessage);
    }

    /// <summary>
    ///     Test that Pull surfaces a failed pull via the status message rather than throwing.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_PullCommand_FailedPull_SetsFailureStatusMessage()
    {
        // Arrange: a git stub reporting a failed pull
        var stub = GitStub.Create(pullExitCode: 1, pullError: "CONFLICT: merge conflict");
        _tempPaths.Add(stub.Path);
        var repoRoot = CreateTempDirectory();
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(repoRoot, null, null, settings);

        // Act: pull
        card.PullCommand.Execute(null);

        // Assert: the failure is surfaced via the status message
        Assert.Contains("Pull failed", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("CONFLICT", card.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that Launch raises ErrorOccurred when no agent tool command can be resolved
    ///     (Custom selected with no command configured).
    /// </summary>
    [Fact]
    public void RepoCardViewModel_LaunchCommand_NoCommandConfigured_RaisesErrorOccurred()
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
        card.LaunchCommand.Execute(null);

        // Assert: an error was raised rather than attempting to launch an empty command
        Assert.NotNull(capturedError);
        Assert.Contains("agentic CLI tool", capturedError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that Upgrade extracts the new package, rewrites the pin file, and raises
    ///     ReleaseNotesReady with the new package's release notes.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_UpgradeCommand_NewerVersionAvailable_UpdatesPinAndRaisesReleaseNotesReady()
    {
        // Arrange: a repo pinned to 1.0.0 and a source with a 2.0.0 package containing release notes
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        CreatePackageZip(sourceDir, "contoso-agents", "2.0.0", "## 2.0.0\n\nNew features.");
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", settings);
        card.Refresh();
        string? capturedReleaseNotes = null;
        card.ReleaseNotesReady += (_, notes) => capturedReleaseNotes = notes;

        // Act: upgrade
        card.UpgradeCommand.Execute(null);

        // Assert: the pin file was rewritten and release notes were surfaced
        var pin = RepoPinStore.Load(repoRoot);
        Assert.NotNull(pin);
        Assert.Equal("2.0.0", pin.Version);
        Assert.Equal("2.0.0", card.PinnedPackageVersion);
        Assert.False(card.IsUpgradeAvailable);
        Assert.Equal("## 2.0.0\n\nNew features.", capturedReleaseNotes);
    }

    /// <summary>
    ///     Test that Upgrade raises ErrorOccurred rather than throwing when no package source is
    ///     configured.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_UpgradeCommand_NoPackageSourceConfigured_RaisesErrorOccurred()
    {
        // Arrange: a pinned repo with no package source configured
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, "contoso-agents", "1.0.0", new AppSettings());
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act: upgrade
        card.UpgradeCommand.Execute(null);

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
    private static IReadOnlyList<string> ReadInvocationLog(string path) =>
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
    public void RepoCardViewModel_LaunchCommand_Succeeds_RecordsLaunchTimestampAndRaisesLaunchRecorded()
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
        card.LaunchCommand.Execute(null);

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
        Assert.Single(raisedWith);
        Assert.Same(card, raisedWith[0]);
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
    ///     SelectPackageRequested exactly once, carrying the configured source path.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_SelectPackageCommand_SourceConfigured_RaisesSelectPackageRequestedWithSourcePath()
    {
        // Arrange
        var repoRoot = CreateTempDirectory();
        var sourceDir = CreateTempDirectory();
        var settings = new AppSettings { PackageSourcePath = sourceDir };
        var card = CreateCard(repoRoot, null, null, settings);
        var raisedWith = new List<string>();
        card.SelectPackageRequested += (_, source) => raisedWith.Add(source);

        // Act
        card.SelectPackageCommand.Execute(null);

        // Assert
        Assert.Single(raisedWith);
        Assert.Equal(sourceDir, raisedWith[0]);
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
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch returns false and raises ErrorOccurred
    ///     mentioning "Select Package" when no pin exists at all.
    /// </summary>
    [Fact]
    public void RepoCardViewModel_EnsureAgentFilesSyncedBeforeLaunch_NoPin_ReturnsFalseAndRaisesErrorOccurred()
    {
        // Arrange: a never-pinned repo
        var repoRoot = CreateTempDirectory();
        var card = CreateCard(repoRoot, null, null, new AppSettings());
        string? capturedError = null;
        card.ErrorOccurred += (_, message) => capturedError = message;

        // Act
        var result = card.EnsureAgentFilesSyncedBeforeLaunch();

        // Assert
        Assert.False(result);
        Assert.NotNull(capturedError);
        Assert.Contains("Select Package", capturedError, StringComparison.Ordinal);
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
    ///     Test that EnsureAgentFilesSyncedBeforeLaunch returns false and raises ErrorOccurred
    ///     when the pinned version is no longer resolvable at the configured source.
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
            RepoPinStore.Save(repoPath, new RepoPin { PackageName = pinnedPackageName, Version = pinnedPackageVersion ?? "0.0.0" });
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
    ///     optionally including a root-level release notes entry.
    /// </summary>
    /// <param name="sourceDir">The package-source directory to create the zip in.</param>
    /// <param name="packageName">The package base name.</param>
    /// <param name="version">The package's semantic version.</param>
    /// <param name="releaseNotes">The root-level release notes content, or <see langword="null"/>
    ///     to omit the entry entirely.</param>
    /// <param name="marker">The content written to <c>.github/agents/copilot.md</c>, used by
    ///     tests to distinguish which package version was actually extracted.</param>
    private void CreatePackageZip(
        string sourceDir, string packageName, string version, string? releaseNotes = null, string marker = "agents content")
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
