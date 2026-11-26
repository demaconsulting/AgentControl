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
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.AgentToolLauncher;
using DemaConsulting.AgentControl.Tests.GitIntegration;
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for the "Switch branch" part of <see cref="RepoCardViewModel"/>: the command, the
///     request that opens the dialog, and the switch itself (<see cref="RepoCardViewModel.SwitchBranchAsync"/>).
/// </summary>
/// <remarks>
///     Tests that need git's real behavior (the pin file following the branch, a switch git refuses)
///     use a real working repository, a local bare repository and a clone of it (built by
///     <see cref="GitRepositoryFixture"/>, skipped where git is not installed and failed when the
///     environment variable <c>CI</c> is set); the others use <see cref="GitStub"/>. No Avalonia window is
///     constructed.
/// </remarks>
[Collection("RealProcess")]
public sealed class RepoCardSwitchBranchTests : IDisposable
{
    /// <summary>
    ///     Files and folders to delete when the test ends.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that the command is not available for a card whose repo state has not been read, whose
    ///     folder is missing, or while the card runs another operation, and is available otherwise.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchCommand_RepoMissingOrBusy_CannotExecute()
    {
        // Arrange: a stub git whose switch takes a moment, a card for an existing folder and one for a missing folder
        var stub = GitStub.Create(extraCommands: [new GitStub.Command("switch", SleepSeconds: 2)]);
        _tempPaths.Add(stub.Path);
        var settings = new AppSettings { GitExecutablePath = stub.Path };
        var card = CreateCard(CreateTempDirectory(), settings);
        var missing = CreateCard(Path.Combine(Path.GetTempPath(), "agentcontrol_missing_" + Guid.NewGuid()), settings);

        // Assert: the state was not read yet
        Assert.False(card.SwitchBranchCommand.CanExecute(null));

        // Act: read the state
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        await missing.RefreshAsync(TestContext.Current.CancellationToken);

        // Assert: available for the existing folder only
        Assert.True(card.SwitchBranchCommand.CanExecute(null));
        Assert.False(missing.SwitchBranchCommand.CanExecute(null));

        // Act: a switch is running
        var switching = card.SwitchBranchAsync(new BranchChoice("other", "other", false), TestContext.Current.CancellationToken);
        var availableWhileBusy = card.SwitchBranchCommand.CanExecute(null);
        var busy = card.IsBusy;
        await switching;

        // Assert: not available while busy, available again afterwards
        Assert.True(busy);
        Assert.False(availableWhileBusy);
        Assert.True(card.SwitchBranchCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that executing the command raises the request the window answers by showing the dialog
    ///     and does nothing else.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchCommand_Executes_RaisesRequestForTheDialog()
    {
        // Arrange: a card whose state was read
        var stub = GitStub.Create();
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        var requests = 0;
        card.SwitchBranchRequested += (_, _) => requests++;

        // Act
        card.SwitchBranchCommand.Execute(null);

        // Assert
        Assert.Equal(1, requests);
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that a switch asks git for the chosen branch, tells the user it was made and leaves the
    ///     card ready again.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_Success_RunsSwitchAndSetsStatusMessage()
    {
        // Arrange: a stub git that accepts the switch
        var argumentLog = Path.Combine(Path.GetTempPath(), "agentcontrol_switch_args_" + Guid.NewGuid() + ".log");
        _tempPaths.Add(argumentLog);
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("switch", Error: "Switched to a new branch 'feature/x'")]);
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });
        await card.RefreshAsync(TestContext.Current.CancellationToken);

        // Act: switch to a remote branch
        await card.SwitchBranchAsync(new BranchChoice("feature/x", "origin/feature/x", true), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Switched to branch 'feature/x'.", card.StatusMessage);
        Assert.False(card.LastActionFailed);
        Assert.False(card.IsBusy);
        Assert.Contains("switch --track -- origin/feature/x", (await File.ReadAllLinesAsync(argumentLog, TestContext.Current.CancellationToken)).Select(line => line.Trim()));
    }

    /// <summary>
    ///     Test that a switch git refuses is reported in git's words, with the same text in the card's
    ///     status and in the error the window shows, and that the card is marked as failed.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_Refused_ShowsGitsReasonAndMarksTheCardFailed()
    {
        // Arrange: a stub git that refuses the switch
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command(
                "switch",
                Error: "error: Your local changes to the following files would be overwritten by checkout:\n\tfile.txt\nAborting\n",
                ExitCode: 1)
        ]);
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.SwitchBranchAsync(new BranchChoice("other", "other", false), TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("Could not switch to 'other': error: Your local changes", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("file.txt", card.StatusMessage, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was changed.", card.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(card.StatusMessage, Assert.Single(errors));
        Assert.True(card.LastActionFailed);
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that git failing to start is reported as an error of the card, not thrown.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_GitCannotBeStarted_ReportsErrorWithoutThrowing()
    {
        // Arrange: a git executable that does not exist
        var card = CreateCard(
            CreateTempDirectory(),
            new AppSettings { GitExecutablePath = Path.Combine(Path.GetTempPath(), "no-such-git-" + Guid.NewGuid() + ".exe") });
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.SwitchBranchAsync(new BranchChoice("other", "other", false), TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("Failed to switch branch:", Assert.Single(errors), StringComparison.Ordinal);
        Assert.True(card.LastActionFailed);
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that a switch to a branch that pins another version makes the card show the new branch
    ///     and the new pin, because the card is refreshed in full after the switch.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_RealRepoWithOtherPin_PinChipFollowsTheNewBranch()
    {
        // Arrange: a clone whose main branch pins 1.0.0 and whose remote feature branch pins 2.0.0
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clonePath = CloneWithFeatureBranch(fixture);
        var card = CreateCard(clonePath, new AppSettings());
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("main", card.CurrentBranch);
        Assert.Equal("1.0.0", card.PinnedPackageVersion);

        // Act: switch to the remote branch
        await card.SwitchBranchAsync(new BranchChoice("feature", "origin/feature", true), TestContext.Current.CancellationToken);

        // Assert: the branch chip and the pin follow, and the outcome of the switch is the status
        Assert.Equal("feature", card.CurrentBranch);
        Assert.Equal("contoso-agents", card.PinnedPackageName);
        Assert.Equal("2.0.0", card.PinnedPackageVersion);
        Assert.Equal("Switched to branch 'feature'.", card.StatusMessage);
        Assert.False(card.LastActionFailed);
        Assert.False(card.IsBusy);
        Assert.True(card.CanPull);

        // Act: and back to the local branch
        await card.SwitchBranchAsync(new BranchChoice("main", "main", false), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("main", card.CurrentBranch);
        Assert.Equal("1.0.0", card.PinnedPackageVersion);
    }

    /// <summary>
    ///     Test that a switch that would overwrite an uncommitted change is refused with git's reason
    ///     and changes nothing: the card stays on its branch and the change is intact.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_Blocked_ShowsGitReasonAndKeepsBranch()
    {
        // Arrange: a clone with an edit of the pin file that the feature branch also changes
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clonePath = CloneWithFeatureBranch(fixture);
        fixture.Git(clonePath, "branch", "feature", "origin/feature");
        var pinFile = Path.Combine(clonePath, ".agentcontrol.json");
        await File.WriteAllTextAsync(pinFile, "{ \"edited\": true }", TestContext.Current.CancellationToken);
        var card = CreateCard(clonePath, new AppSettings());
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.SwitchBranchAsync(new BranchChoice("feature", "feature", false), TestContext.Current.CancellationToken);

        // Assert: git's reason names the file; the branch and the edit are as they were
        Assert.StartsWith("Could not switch to 'feature':", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(".agentcontrol.json", card.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(card.StatusMessage, Assert.Single(errors));
        Assert.True(card.LastActionFailed);
        Assert.Equal("main", card.CurrentBranch);
        Assert.Equal("{ \"edited\": true }", await File.ReadAllTextAsync(pinFile, TestContext.Current.CancellationToken));
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that a switch from a branch with an uncommitted change that does not conflict with the
    ///     target branch succeeds, the change travels with it, and the card still shows the working tree
    ///     as having uncommitted changes (so a pull stays unavailable).
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_RealRepo_UnrelatedDirtyFile_StaysDirtyAfterSwitch()
    {
        // Arrange: notes.md is the same on both branches; only the pin differs
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string> { [".agentcontrol.json"] = PinFileText("1.0.0"), ["notes.md"] = "notes" },
            "Pin 1.0.0");
        fixture.Git(fixture.WorkPath, "switch", "-q", "-c", "feature");
        fixture.Commit(new Dictionary<string, string> { [".agentcontrol.json"] = PinFileText("2.0.0") }, "Pin 2.0.0");
        fixture.Git(fixture.WorkPath, "switch", "-q", "main");
        fixture.Git(fixture.WorkPath, "push", "-q", fixture.RemotePath, "main", "feature");
        fixture.Git(fixture.RemotePath, "symbolic-ref", "HEAD", "refs/heads/main");
        var clonePath = Path.Combine(fixture.Root, "clone");
        fixture.Git(fixture.Root, "clone", "-q", "-b", "main", fixture.RemotePath, clonePath);
        var notes = Path.Combine(clonePath, "notes.md");
        await File.WriteAllTextAsync(notes, "my edit", TestContext.Current.CancellationToken);
        var card = CreateCard(clonePath, new AppSettings());
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(card.IsWorkingTreeDirty);

        // Act
        await card.SwitchBranchAsync(new BranchChoice("feature", "origin/feature", true), TestContext.Current.CancellationToken);

        // Assert: the switch worked, the pin follows, the edit is intact and the card still shows it
        Assert.False(card.LastActionFailed);
        Assert.Equal("feature", card.CurrentBranch);
        Assert.Equal("2.0.0", card.PinnedPackageVersion);
        Assert.Equal("my edit", await File.ReadAllTextAsync(notes, TestContext.Current.CancellationToken));
        Assert.True(card.IsWorkingTreeDirty);
        Assert.False(card.CanPull);
    }

    /// <summary>
    ///     Test that a switch from a detached HEAD to a branch works and the card then shows the branch.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_DetachedHead_ShowsDetachedAndSwitchesToBranch()
    {
        // Arrange: a clone whose HEAD is detached
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clonePath = CloneWithFeatureBranch(fixture);
        fixture.Git(clonePath, "checkout", "-q", "--detach");
        var card = CreateCard(clonePath, new AppSettings());
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("(detached)", card.CurrentBranch);

        // Act
        await card.SwitchBranchAsync(new BranchChoice("main", "main", false), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("main", card.CurrentBranch);
        Assert.Equal("Switched to branch 'main'.", card.StatusMessage);
    }

    /// <summary>
    ///     Test that the switch runs git off the calling (UI) thread: started on a guarded thread it
    ///     records no blocking call, and the git is only run once the work proceeds.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_RunsGitOffTheUiThread()
    {
        // Arrange: a card whose state was read, with a stub git that accepts the switch
        var stub = GitStub.Create(extraCommands: [new GitStub.Command("switch", SleepSeconds: 1)]);
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        BlockingCallGuard.TakeViolations();

        // Act: start the switch on a guarded thread; the record of refused calls is read before any await
        Task switching;
        using (BlockingCallGuard.GuardCurrentThread())
        {
            switching = card.SwitchBranchAsync(new BranchChoice("other", "other", false), TestContext.Current.CancellationToken);
        }

        var violations = BlockingCallGuard.TakeViolations();
        var busyWhileRunning = card.IsBusy;
        await switching;

        // Assert: nothing blocked on the guarded thread; the card was busy meanwhile and is ready again
        Assert.Empty(violations);
        Assert.True(busyWhileRunning);
        Assert.False(card.IsBusy);
        Assert.Equal("Switched to branch 'other'.", card.StatusMessage);
    }

    /// <summary>
    ///     Test that the dialog's view model reads the branches through the card's git, off the UI thread.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_CreateSwitchBranchViewModel_ListsTheBranchesGitReports()
    {
        // Arrange: a stub git that reports two branches
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("for-each-ref", Output: "*\trefs/heads/main\n \trefs/heads/other\n"),
            new GitStub.Command("remote")
        ]);
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });

        // Act
        var viewModel = card.CreateSwitchBranchViewModel();
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["main", "other"], viewModel.Items.Select(item => item.Name));
        Assert.Equal("Currently on main", viewModel.CurrentText);
        Assert.Contains(card.RepoName, viewModel.Title, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a switch git does not finish within the switch limit is stopped, the card then reads
    ///     its branch and working tree from disk again, and the card, and the error the window shows, say
    ///     that the result is unknown; the card stays marked as failed and ready.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_TimesOut_RefreshesAndSaysResultIsUnknown()
    {
        // Arrange: a git whose switch hangs, a one-second switch limit and a card whose state was read
        var argumentLog = TempFile("switch_timeout_args.log");
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("switch", SleepSeconds: GitStub.HangSeconds)]);
        _tempPaths.Add(stub.Path);
        _tempPaths.Add(stub.FinishedMarkerPath);
        var card = CreateCard(
            CreateTempDirectory(),
            new AppSettings { GitExecutablePath = stub.Path },
            gitTimeouts: new GitClientTimeouts(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10))
            {
                SwitchBranch = TimeSpan.FromSeconds(1)
            });
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.SwitchBranchAsync(new BranchChoice("other", "other", false), TestContext.Current.CancellationToken);

        // Assert: said as unknown, in the status and in the error; marked failed; ready again
        var expected =
            "Switching to 'other' did not finish within 1 second and was stopped; the result is unknown. " +
            "The card shows what is on disk now.";
        Assert.Equal(expected, card.StatusMessage);
        Assert.Equal(expected, Assert.Single(errors));
        Assert.True(card.LastActionFailed);
        Assert.False(card.IsBusy);

        // Assert: after the stopped switch git was asked for the branch and the working tree again
        var calls = ReadLog(argumentLog);
        var switchIndex = Array.FindIndex(calls, call => call.StartsWith("switch", StringComparison.Ordinal));
        Assert.True(switchIndex >= 0);
        var after = calls.Skip(switchIndex + 1).ToArray();
        Assert.Contains(after, call => call.StartsWith("rev-parse --abbrev-ref", StringComparison.Ordinal));
        Assert.Contains(after, call => call.StartsWith("status", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a card that was removed from the list while its switch ran reads nothing afterwards
    ///     (no git call follows the switch), and still reports the outcome of the switch.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_CardRetiredDuringSwitch_ReadsNothingAndReportsOutcome()
    {
        // Arrange: a switch that takes a moment
        var argumentLog = TempFile("switch_retired_args.log");
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("switch", SleepSeconds: 2)]);
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        var switching = card.SwitchBranchAsync(new BranchChoice("other", "other", false), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => ReadLog(argumentLog).Any(call => call.StartsWith("switch", StringComparison.Ordinal)));

        // Act: the repo is removed from the list while git switches
        card.Retire();
        await switching;

        // Assert: the switch's outcome is reported, and git was not asked anything after the switch
        Assert.Equal("Switched to branch 'other'.", card.StatusMessage);
        Assert.False(card.LastActionFailed);
        Assert.False(card.IsBusy);
        var calls = ReadLog(argumentLog);
        var switchIndex = Array.FindIndex(calls, call => call.StartsWith("switch", StringComparison.Ordinal));
        Assert.Empty(calls.Skip(switchIndex + 1));
    }

    /// <summary>
    ///     Test that a caller who gives up while the switch runs does not turn a switch that was made into
    ///     "The operation was canceled.": the card keeps saying it switched, and still reads the branch
    ///     and the working tree after it.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_RefreshCanceledAfterSwitch_KeepsSwitchedMessageAndReadsBranch()
    {
        // Arrange: a switch that takes a moment and a caller that gives up while it runs
        var argumentLog = TempFile("switch_canceled_args.log");
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("switch", SleepSeconds: 2)]);
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var switching = card.SwitchBranchAsync(new BranchChoice("other", "other", false), giveUp.Token);
        await WaitUntilAsync(() => ReadLog(argumentLog).Any(call => call.StartsWith("switch", StringComparison.Ordinal)));

        // Act: the switch is not interrupted; the refresh that follows starts canceled
        await giveUp.CancelAsync();
        await switching;

        // Assert: the switch's outcome stands, and the branch and working tree were read after it
        Assert.Equal("Switched to branch 'other'.", card.StatusMessage);
        Assert.False(card.LastActionFailed);
        Assert.False(card.IsBusy);
        var calls = ReadLog(argumentLog);
        var switchIndex = Array.FindIndex(calls, call => call.StartsWith("switch", StringComparison.Ordinal));
        var after = calls.Skip(switchIndex + 1).ToArray();
        Assert.Contains(after, call => call.StartsWith("rev-parse --abbrev-ref", StringComparison.Ordinal));
        Assert.Contains(after, call => call.StartsWith("status", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a switch asked for while the card runs another operation is not started and says so.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_CardBusy_DropsTheSwitchWithAMessage()
    {
        // Arrange: a switch is running
        var argumentLog = TempFile("switch_busy_args.log");
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("switch", SleepSeconds: 2)]);
        _tempPaths.Add(stub.Path);
        var card = CreateCard(CreateTempDirectory(), new AppSettings { GitExecutablePath = stub.Path });
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        var first = card.SwitchBranchAsync(new BranchChoice("other", "other", false), TestContext.Current.CancellationToken);
        Assert.True(card.IsBusy);

        // Act: a second switch is asked for
        await card.SwitchBranchAsync(new BranchChoice("third", "third", false), TestContext.Current.CancellationToken);
        var messageWhileBusy = card.StatusMessage;
        await first;

        // Assert: the second one was dropped with a message, the first one ended normally
        Assert.Equal("Another operation is running on this card; switching to 'third' was not started.", messageWhileBusy);
        Assert.Equal("Switched to branch 'other'.", card.StatusMessage);
        Assert.Single(ReadLog(argumentLog), call => call.StartsWith("switch", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a switch to a remote branch (a new local branch tracking it) that would overwrite an
    ///     uncommitted change is refused with git's reason and changes nothing: no local branch is made
    ///     and the card stays on its branch.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_RemoteBranchBlocked_ShowsGitsReasonAndNothingChanges()
    {
        // Arrange: a clone with an edit of the pin file that the remote feature branch also changes
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clonePath = CloneWithFeatureBranch(fixture);
        var pinFile = Path.Combine(clonePath, ".agentcontrol.json");
        await File.WriteAllTextAsync(pinFile, "{ \"edited\": true }", TestContext.Current.CancellationToken);
        var card = CreateCard(clonePath, new AppSettings());
        await card.RefreshAsync(TestContext.Current.CancellationToken);
        var errors = new List<string>();
        card.ErrorOccurred += (_, message) => errors.Add(message);

        // Act
        await card.SwitchBranchAsync(new BranchChoice("feature", "origin/feature", true), TestContext.Current.CancellationToken);

        // Assert: git's reason names the file, and neither the branches nor the edit changed
        Assert.StartsWith("Could not switch to 'feature':", card.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(".agentcontrol.json", card.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(card.StatusMessage, Assert.Single(errors));
        Assert.True(card.LastActionFailed);
        Assert.Equal("main", card.CurrentBranch);
        Assert.Equal("main", fixture.Git(clonePath, "rev-parse", "--abbrev-ref", "HEAD").Trim());
        Assert.Equal(string.Empty, fixture.Git(clonePath, "branch", "--list", "feature").Trim());
        Assert.Equal("{ \"edited\": true }", await File.ReadAllTextAsync(pinFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that queued reads of the card that are still held when the switch has been made end with
    ///     the card showing the new branch: a read that was only queued behind the switch never puts the
    ///     old branch back.
    /// </summary>
    [Fact]
    public async Task RepoCardViewModel_SwitchBranchAsync_OverlappingQueuedReads_EndsWithTheNewBranch()
    {
        // Arrange: a clone whose state was read; from now on the card's reads are held at a gate
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clonePath = CloneWithFeatureBranch(fixture);
        var gate = new GatedBackgroundWorkRunner();
        var card = CreateCard(clonePath, new AppSettings(), backgroundWork: gate);
        var firstRead = card.RefreshAsync(TestContext.Current.CancellationToken);
        await gate.FirstCall;
        gate.Release();
        await firstRead;
        Assert.Equal("main", card.CurrentBranch);
        gate.Close();

        // Act: the switch is made (git runs outside the gate) and its refresh is held; a background read is
        // queued beside it
        var switching = card.SwitchBranchAsync(new BranchChoice("feature", "origin/feature", true), TestContext.Current.CancellationToken);
        await gate.FirstCall;
        var background = card.RefreshRepoStateAsync(
            RepoRefreshPriority.Background, cheapFacts: true, workingTree: true, cancellationToken: TestContext.Current.CancellationToken);
        gate.Release();
        await switching;
        await background;

        // Assert
        Assert.Equal("feature", card.CurrentBranch);
        Assert.Equal("2.0.0", card.PinnedPackageVersion);
        Assert.Equal("Switched to branch 'feature'.", card.StatusMessage);
        Assert.False(card.LastActionFailed);
        Assert.False(card.IsBusy);
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
                    GitSourceProvider.DeleteDirectory(path);
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

    /// <summary>
    ///     Creates a card for a repo folder that never starts a real agent tool.
    /// </summary>
    /// <param name="repoPath">The repo folder.</param>
    /// <param name="settings">The settings the card reads.</param>
    /// <param name="backgroundWork">The runner of the card's background work, or <see langword="null"/> for the
    ///     thread pool.</param>
    /// <param name="gitTimeouts">The git time limits, or <see langword="null"/> for the defaults.</param>
    /// <returns>The card.</returns>
    private static RepoCardViewModel CreateCard(
        string repoPath,
        AppSettings settings,
        IBackgroundWorkRunner? backgroundWork = null,
        GitClientTimeouts? gitTimeouts = null) =>
        new(
            new RecentRepo { Path = repoPath },
            () => settings,
            new PackageVersionCache(),
            processStarter: new FakeAgentToolProcessStarter(),
            shellDetector: FakeAgentToolProcessStarter.CreateShellDetector(),
            backgroundWork: backgroundWork,
            gitTimeouts: gitTimeouts);

    /// <summary>
    ///     Gets the path of a file in a fresh temporary folder, deleted when the test ends.
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <returns>The path; the file does not exist yet.</returns>
    private string TempFile(string name) => Path.Combine(CreateTempDirectory(), name);

    /// <summary>
    ///     Reads the arguments of the git calls a stub logged so far.
    /// </summary>
    /// <param name="argumentLog">The stub's log file.</param>
    /// <returns>One trimmed line per call, oldest first; empty when nothing was logged or the file is
    ///     being written right now.</returns>
    private static string[] ReadLog(string argumentLog)
    {
        try
        {
            return File.Exists(argumentLog) ? File.ReadAllLines(argumentLog).Select(line => line.Trim()).ToArray() : [];
        }
        catch (IOException)
        {
            return [];
        }
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
    ///     Creates a unique temporary directory deleted when the test ends.
    /// </summary>
    /// <returns>The directory's path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_switch_card_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     Makes the fixture's remote hold <c>main</c>, whose pin file names version 1.0.0, and
    ///     <c>feature</c>, whose pin file names 2.0.0, and clones it (on <c>main</c>).
    /// </summary>
    /// <param name="fixture">The git fixture whose working repository plays the origin.</param>
    /// <returns>The path of the clone.</returns>
    private string CloneWithFeatureBranch(GitRepositoryFixture fixture)
    {
        fixture.Commit(new Dictionary<string, string> { [".agentcontrol.json"] = PinFileText("1.0.0") }, "Pin 1.0.0");
        fixture.Git(fixture.WorkPath, "switch", "-q", "-c", "feature");
        fixture.Commit(new Dictionary<string, string> { [".agentcontrol.json"] = PinFileText("2.0.0") }, "Pin 2.0.0");
        fixture.Git(fixture.WorkPath, "switch", "-q", "main");
        fixture.Git(fixture.WorkPath, "push", "-q", fixture.RemotePath, "main", "feature");
        fixture.Git(fixture.RemotePath, "symbolic-ref", "HEAD", "refs/heads/main");

        var clonePath = Path.Combine(fixture.Root, "clone");
        fixture.Git(fixture.Root, "clone", "-q", "-b", "main", fixture.RemotePath, clonePath);
        return clonePath;
    }

    /// <summary>
    ///     Gets the text of a <c>.agentcontrol.json</c> pin file that pins <c>contoso-agents</c> to a version.
    /// </summary>
    /// <param name="version">The pinned version.</param>
    /// <returns>The file's text.</returns>
    private string PinFileText(string version)
    {
        var scratch = CreateTempDirectory();
        RepoPinStore.Save(scratch, new RepoPin { PackageName = "contoso-agents", Version = version });
        return File.ReadAllText(Path.Combine(scratch, ".agentcontrol.json"));
    }
}
