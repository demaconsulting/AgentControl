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

using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.GitIntegration;
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for the clone part of <see cref="MainWindowViewModel"/>: the group choices of the
///     dialog and <see cref="MainWindowViewModel.CloneRepositoryAsync"/> (progress with Cancel, the clone
///     off the UI thread, the card added through the add path, one clone per destination, failures
///     reported).
/// </summary>
/// <remarks>
///     Most tests use <see cref="GitStub"/>, whose <c>clone</c> can create the destination (with a
///     read-only pack file, like a real clone) and hang; one test clones a local bare repository with
///     the real git (skipped where git is not installed, failed when the environment variable
///     <c>CI</c> is set; never the network). No Avalonia window is constructed.
/// </remarks>
[Collection("RealProcess")]
public sealed class MainWindowViewModelCloneTests : IDisposable
{
    /// <summary>
    ///     The address every stub-based test clones; the stub answers whatever it is given.
    /// </summary>
    private const string Url = "https://example.com/team/repo.git";

    /// <summary>
    ///     Files and folders to delete when the test ends.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     The view models to dispose when the test ends.
    /// </summary>
    private readonly List<MainWindowViewModel> _viewModels = [];

    /// <summary>
    ///     Test that the group choices are Ungrouped first, then every group in the order the list shows
    ///     them with its path through the tree, so nested groups of the same name can be told apart.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GetGroupChoices_ListsGroupsInTreeOrder()
    {
        // Arrange: Work > Backend, and a second top-level group
        var settings = new AppSettings
        {
            RecentRepos =
            [
                new RecentRepo { Path = CreateTempDirectory(), GroupName = "Zeta" },
                new RecentRepo { Path = CreateTempDirectory(), GroupName = "Backend" },
                new RecentRepo { Path = CreateTempDirectory(), GroupName = "Work" }
            ],
            GroupParents = new Dictionary<string, string>(RepoGroupName.Comparer) { ["Backend"] = "Work" }
        };
        var viewModel = CreateViewModel(GitStub.Create(), settings);

        // Act
        var choices = viewModel.GetGroupChoices();

        // Assert: Ungrouped (no group) first, then the tree
        Assert.Equal(["Ungrouped", "Work", "Work > Backend", "Zeta"], choices.Select(choice => choice.DisplayName));
        Assert.Equal([null, "Work", "Backend", "Zeta"], choices.Select(choice => choice.GroupName));
    }

    /// <summary>
    ///     Test that the group choices of a list without groups hold Ungrouped only.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_GetGroupChoices_NoGroups_UngroupedOnly()
    {
        // Arrange
        var viewModel = CreateViewModel(GitStub.Create(), new AppSettings());

        // Act / Assert
        var choice = Assert.Single(viewModel.GetGroupChoices());
        Assert.Null(choice.GroupName);
    }

    /// <summary>
    ///     Test that a successful clone runs git with the address and destination, adds the new folder as a
    ///     card through the add path into the chosen group (created when it does not exist yet), saves it,
    ///     says nothing, and leaves no progress behind.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_Success_AddsCardThroughAddPathIntoGroup()
    {
        // Arrange: a git whose clone creates the destination
        var argumentLog = TempFile("args.log");
        var stub = CreateStub(argumentLog, new GitStub.Command("clone", CreatesDestination: true));
        var viewModel = CreateViewModel(stub, new AppSettings(), out var configDirectory);
        var destination = Path.Combine(CreateTempDirectory(), "repo");

        // Act
        var result = await viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, "Work"));

        // Assert: cloned and added into the group, saved, nothing to tell, no progress left
        Assert.Equal(CloneRepositoryOutcome.Added, result.Outcome);
        Assert.Null(result.Notice);
        var card = Assert.Single(viewModel.RepoCards);
        Assert.Equal(destination, card.RepoPath);
        Assert.Equal("Work", card.GroupName);
        var saved = Assert.Single(SettingsStore.Load(configDirectory).RecentRepos);
        Assert.Equal(destination, saved.Path);
        Assert.Equal("Work", saved.GroupName);
        var parent = Path.GetDirectoryName(destination)!;
        Assert.Contains(
            (await File.ReadAllLinesAsync(argumentLog, TestContext.Current.CancellationToken)).Select(line => line.Trim()),
            line => line.StartsWith($"clone -- {Url} {Path.Combine(parent, ".agentcontrol-clone-")}", StringComparison.Ordinal));
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that while git clones, the strip shows "Cloning {folder}..." with a Cancel button named for
    ///     the clone, and that the strip is idle again at the end.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_ShowsProgressWithCancelAndRemovesItAtTheEnd()
    {
        // Arrange: the clone is held at a gate
        var stub = CreateStub(null, new GitStub.Command("clone", CreatesDestination: true));
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = CreateViewModel(stub, new AppSettings(), gate);
        var destination = Path.Combine(CreateTempDirectory(), "repo");

        // Act
        var cloning = viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));
        await gate.FirstCall;
        var progress = viewModel.GlobalProgress;
        var shown = (progress.IsActive, progress.Text, progress.CanCancel, progress.IsIndeterminate, progress.CancelAccessibleName);
        gate.Release();
        var result = await cloning;

        // Assert: shown while held, gone afterwards
        Assert.Equal((true, "Cloning repo...", true, true, "Cancel cloning repository"), shown);
        Assert.Equal(CloneRepositoryOutcome.Added, result.Outcome);
        Assert.False(progress.IsActive);
        Assert.Single(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that Cancel in the strip stops git, removes the partly cloned folder, adds nothing, says
    ///     nothing and leaves the strip idle.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_Cancel_RemovesPartialFolderAndAddsNothing()
    {
        // Arrange: a clone that creates its working folder and then hangs
        var stub = CreateStub(null, new GitStub.Command("clone", CreatesDestination: true, SleepSeconds: GitStub.HangSeconds));
        var viewModel = CreateViewModel(stub, new AppSettings());
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var cloning = viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));
        await WaitUntilAsync(() => Directory.GetDirectories(parent, ".agentcontrol-clone-*").Length > 0);

        // Act: Cancel in the strip
        viewModel.GlobalProgress.CancelCommand.Execute(null);
        var result = await cloning;

        // Assert: nothing is left in the parent folder, neither the destination nor the working folder
        Assert.Equal(CloneRepositoryOutcome.Canceled, result.Outcome);
        Assert.Null(result.Notice);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetFileSystemEntries(parent));
        Assert.Empty(viewModel.RepoCards);
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that a clone git cannot make is reported with git's message with credentials removed,
    ///     nothing is added, and the strip is idle again.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_Failure_ReturnsGitMessageAndAddsNothing()
    {
        // Arrange: git says the repository was not found, naming the address with a password
        var stub = CreateStub(
            null,
            new GitStub.Command(
                "clone",
                Error: "fatal: repository 'https://bob:Hunter2PasswordValue@example.com/team/repo.git/' not found",
                ExitCode: 128));
        var viewModel = CreateViewModel(stub, new AppSettings());
        var destination = Path.Combine(CreateTempDirectory(), "repo");

        // Act
        var result = await viewModel.CloneRepositoryAsync(
            new CloneRepositoryRequest("https://bob:Hunter2PasswordValue@example.com/team/repo.git", destination, null));

        // Assert
        Assert.Equal(CloneRepositoryOutcome.Failed, result.Outcome);
        Assert.StartsWith("Cloning failed: fatal: repository", result.Notice, StringComparison.Ordinal);
        Assert.DoesNotContain("Hunter2PasswordValue", result.Notice, StringComparison.Ordinal);
        Assert.Empty(viewModel.RepoCards);
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     Test that a clone that fails with no message still says something (git's exit code).
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_FailureWithoutMessage_NamesTheExitCode()
    {
        // Arrange
        var stub = CreateStub(null, new GitStub.Command("clone", ExitCode: 3));
        var viewModel = CreateViewModel(stub, new AppSettings());

        // Act
        var result = await viewModel.CloneRepositoryAsync(
            new CloneRepositoryRequest(Url, Path.Combine(CreateTempDirectory(), "repo"), null));

        // Assert
        Assert.Equal(CloneRepositoryOutcome.Failed, result.Outcome);
        Assert.Equal("Cloning failed: git exited with code 3.", result.Notice);
    }

    /// <summary>
    ///     Test that a destination that appeared after the dialog (a folder with a file in it now exists
    ///     there) is reported as a failed clone with the reason, without starting git and without
    ///     touching the folder.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_DestinationAppeared_FailsWithReasonAndTouchesNothing()
    {
        // Arrange
        var argumentLog = TempFile("args.log");
        var stub = CreateStub(argumentLog, new GitStub.Command("clone", CreatesDestination: true));
        var viewModel = CreateViewModel(stub, new AppSettings());
        var destination = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(destination, "mine.txt"), "mine", TestContext.Current.CancellationToken);

        // Act
        var result = await viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));

        // Assert
        Assert.Equal(CloneRepositoryOutcome.Failed, result.Outcome);
        Assert.Contains("already exists", result.Notice, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(destination, "mine.txt")));
        Assert.False(File.Exists(argumentLog) && (await File.ReadAllTextAsync(argumentLog, TestContext.Current.CancellationToken)).Contains("clone", StringComparison.Ordinal));
        Assert.Empty(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that a second clone into a destination that is being cloned into is refused without
    ///     starting git, whatever its letter case, and that the first clone is unaffected.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_SameDestinationTwice_SecondRefused()
    {
        // Arrange: the first clone is held at a gate
        var stub = CreateStub(null, new GitStub.Command("clone", CreatesDestination: true));
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = CreateViewModel(stub, new AppSettings(), gate);
        var destination = Path.Combine(CreateTempDirectory(), "repo");
        var first = viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));
        await gate.FirstCall;

        // Act: a second request for the same folder (spelled in another case)
        var second = await viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination.ToUpperInvariant(), null));
        var callsBeforeRelease = gate.Calls;
        gate.Release();
        var firstResult = await first;

        // Assert
        Assert.Equal(CloneRepositoryOutcome.Refused, second.Outcome);
        Assert.Contains("already being cloned", second.Notice, StringComparison.Ordinal);
        Assert.Equal(1, callsBeforeRelease);
        Assert.Equal(CloneRepositoryOutcome.Added, firstResult.Outcome);
        Assert.Single(viewModel.RepoCards);

        // Act: once the clone ended the destination is free of the reservation (and now exists)
        var third = await viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));

        // Assert: refused by the destination rule, not the reservation
        Assert.Equal(CloneRepositoryOutcome.Failed, third.Outcome);
    }

    /// <summary>
    ///     Test that the clone runs through the background work runner: while it is held the calling
    ///     (guarded) thread is free and no blocking call is made on it.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_RunsGitOffTheUiThread()
    {
        // Arrange
        var stub = CreateStub(null, new GitStub.Command("clone", CreatesDestination: true));
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = CreateViewModel(stub, new AppSettings(), gate);
        var destination = Path.Combine(CreateTempDirectory(), "repo");
        BlockingCallGuard.TakeViolations();

        // Act: start from a guarded thread; the clone is held
        Task<CloneRepositoryResult> cloning;
        using (BlockingCallGuard.GuardCurrentThread())
        {
            cloning = viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));
        }

        var violations = BlockingCallGuard.TakeViolations();
        await gate.FirstCall;
        var cloningWhileHeld = !cloning.IsCompleted;
        gate.Release();
        var result = await cloning;

        // Assert
        Assert.Empty(violations);
        Assert.True(cloningWhileHeld);
        Assert.Equal(CloneRepositoryOutcome.Added, result.Outcome);
    }

    /// <summary>
    ///     Test that when the add after the clone cannot be saved and the user discards it, the cloned
    ///     folder stays on disk, no card is added, and the notice says so.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_SaveFails_KeepsFolderAndSaysSo()
    {
        // Arrange: every save fails and the user answers Discard
        var stub = CreateStub(null, new GitStub.Command("clone", CreatesDestination: true));
        var viewModel = CreateViewModel(
            stub, new AppSettings(), saveSettings: _ => throw new InvalidOperationException("The disk is full."));
        viewModel.AddSaveFailed += (_, request) => request.Respond(false);
        var destination = Path.Combine(CreateTempDirectory(), "repo");

        // Act
        var result = await viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));

        // Assert
        Assert.Equal(CloneRepositoryOutcome.ClonedNotAdded, result.Outcome);
        Assert.Contains("The disk is full.", result.Notice, StringComparison.Ordinal);
        Assert.Contains("still on disk", result.Notice, StringComparison.Ordinal);
        Assert.True(Directory.Exists(destination));
        Assert.Empty(viewModel.RepoCards);
    }

    /// <summary>
    ///     Test that disposing the view model (the window closed) cancels a running clone, which removes
    ///     the partial folder and adds nothing.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_Dispose_CancelsRunningClone()
    {
        // Arrange: a clone that creates its working folder and then hangs
        var stub = CreateStub(null, new GitStub.Command("clone", CreatesDestination: true, SleepSeconds: GitStub.HangSeconds));
        var viewModel = CreateViewModel(stub, new AppSettings());
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var cloning = viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));
        await WaitUntilAsync(() => Directory.GetDirectories(parent, ".agentcontrol-clone-*").Length > 0);

        // Act
        viewModel.Dispose();
        var result = await cloning;

        // Assert
        Assert.Equal(CloneRepositoryOutcome.Canceled, result.Outcome);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(viewModel.RepoCards);

        // Act: a clone after the close does nothing at all
        var after = await viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, destination, null));

        // Assert
        Assert.Equal(CloneRepositoryOutcome.Canceled, after.Outcome);
        Assert.False(Directory.Exists(destination));
    }

    /// <summary>
    ///     Test that a request with an invalid group name is refused before anything starts.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_InvalidGroup_ThrowsBeforeStarting()
    {
        // Arrange
        var viewModel = CreateViewModel(GitStub.Create(), new AppSettings());

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(Url, Path.Combine(CreateTempDirectory(), "repo"), "   ")));
        Assert.False(viewModel.GlobalProgress.IsActive);
        await Assert.ThrowsAsync<ArgumentNullException>(() => viewModel.CloneRepositoryAsync(null!));
    }

    /// <summary>
    ///     End-to-end test with the real git: cloning a local bare repository (never the network) into a
    ///     new folder adds a card for a working repository in the chosen group and saves it.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_RealLocalBareRepository_ClonesAndAddsCard()
    {
        // Arrange: a bare repository with one commit on main
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["readme.txt"] = "hello" }, "Initial");
        fixture.Git(fixture.WorkPath, "push", "-q", fixture.RemotePath, "main");
        fixture.Git(fixture.RemotePath, "symbolic-ref", "HEAD", "refs/heads/main");
        var viewModel = CreateViewModel(null, new AppSettings(), out var configDirectory);
        var parent = Path.Combine(fixture.Root, "clones");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "origin");

        // Act
        var result = await viewModel.CloneRepositoryAsync(new CloneRepositoryRequest(fixture.RemotePath, destination, "Work"));
        await viewModel.WhenRepoStateSettledAsync();

        // Assert: a real working clone, listed in the group and saved
        Assert.Equal(CloneRepositoryOutcome.Added, result.Outcome);
        Assert.Null(result.Notice);
        Assert.True(GitClient.IsRepositoryRoot(destination));
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(destination, "readme.txt"), TestContext.Current.CancellationToken));
        var card = Assert.Single(viewModel.RepoCards);
        Assert.Equal(destination, card.RepoPath);
        Assert.Equal("Work", card.GroupName);
        Assert.Equal("origin", card.RepoName);
        Assert.Equal(destination, Assert.Single(SettingsStore.Load(configDirectory).RecentRepos).Path);
        Assert.False(viewModel.GlobalProgress.IsActive);
    }

    /// <summary>
    ///     End-to-end test with the real git: cloning something that is no repository fails with git's own
    ///     message, adds nothing and removes nothing.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_CloneRepositoryAsync_RealGitNotARepository_FailsWithGitsMessage()
    {
        // Arrange
        GitRepositoryFixture.SkipWithoutGit();
        var viewModel = CreateViewModel(null, new AppSettings());
        var parent = CreateTempDirectory();

        // Act
        var result = await viewModel.CloneRepositoryAsync(
            new CloneRepositoryRequest(Path.Combine(parent, "no-such-repository"), Path.Combine(parent, "copy"), null));

        // Assert
        Assert.Equal(CloneRepositoryOutcome.Failed, result.Outcome);
        Assert.StartsWith("Cloning failed: fatal:", result.Notice, StringComparison.Ordinal);
        Assert.Empty(viewModel.RepoCards);
        Assert.False(Directory.Exists(Path.Combine(parent, "copy")));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var viewModel in _viewModels)
        {
            viewModel.Dispose();
        }

        foreach (var path in _tempPaths)
        {
            DeleteTree(path);
        }
    }

    /// <summary>
    ///     Creates a stub git that answers the given extra commands (and the usual repo queries).
    /// </summary>
    /// <param name="argumentLog">The file the stub logs every call's arguments to, or
    ///     <see langword="null"/> for no log.</param>
    /// <param name="commands">The extra commands.</param>
    /// <returns>The stub, deleted when the test ends.</returns>
    private GitStub CreateStub(string? argumentLog, params GitStub.Command[] commands)
    {
        var stub = GitStub.Create(extraCommands: commands, argumentLogPath: argumentLog);
        _tempPaths.Add(stub.Path);
        _tempPaths.Add(stub.FinishedMarkerPath);
        return stub;
    }

    /// <summary>
    ///     Creates a view model over the stub git (or the real git for <see langword="null"/>) with its
    ///     own configuration folder.
    /// </summary>
    /// <param name="stub">The stub git, or <see langword="null"/> for the real git.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="backgroundWork">The runner of the background work, or <see langword="null"/> for the
    ///     thread pool.</param>
    /// <param name="saveSettings">What saves the settings, or <see langword="null"/> for the real
    ///     store.</param>
    /// <returns>The view model, disposed when the test ends.</returns>
    private MainWindowViewModel CreateViewModel(
        GitStub? stub,
        AppSettings settings,
        IBackgroundWorkRunner? backgroundWork = null,
        Action<AppSettings>? saveSettings = null) =>
        CreateViewModel(stub, settings, out _, backgroundWork, saveSettings);

    /// <summary>
    ///     Creates a view model over the stub git (or the real git for <see langword="null"/>) with its
    ///     own configuration folder.
    /// </summary>
    /// <param name="stub">The stub git, or <see langword="null"/> for the real git.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="configDirectory">The configuration folder the settings are saved in.</param>
    /// <param name="backgroundWork">The runner of the background work, or <see langword="null"/> for the
    ///     thread pool.</param>
    /// <param name="saveSettings">What saves the settings, or <see langword="null"/> for the real
    ///     store.</param>
    /// <returns>The view model, disposed when the test ends.</returns>
    private MainWindowViewModel CreateViewModel(
        GitStub? stub,
        AppSettings settings,
        out string configDirectory,
        IBackgroundWorkRunner? backgroundWork = null,
        Action<AppSettings>? saveSettings = null)
    {
        if (stub is not null)
        {
            settings.GitExecutablePath = stub.Path;
        }

        configDirectory = CreateTempDirectory();
        var viewModel = new MainWindowViewModel(
            settings, configDirectory: configDirectory, backgroundWork: backgroundWork, saveSettings: saveSettings);
        _viewModels.Add(viewModel);
        return viewModel;
    }

    /// <summary>
    ///     Gets the path of a file in a fresh temporary folder, deleted when the test ends.
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The path; the file does not exist yet.</returns>
    private string TempFile(string name) => Path.Combine(CreateTempDirectory(), name);

    /// <summary>
    ///     Creates a unique temporary folder, deleted when the test ends.
    /// </summary>
    /// <returns>The folder's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_clone_main_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     Waits until a condition holds, failing the test when it does not within thirty seconds.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Deletes a file or folder tree, clearing the read-only attributes git sets, and ignoring what
    ///     cannot be deleted.
    /// </summary>
    /// <param name="path">The file or folder.</param>
    private static void DeleteTree(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return;
            }

            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; a leftover temporary folder does not fail the test
        }
    }
}
