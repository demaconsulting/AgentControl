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
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="SwitchBranchViewModel"/> (and its <see cref="BranchItemViewModel"/> rows).
/// </summary>
/// <remarks>
///     The branches come from a function that returns canned <see cref="GitBranchList"/> values, so no
///     git is involved; the background work runner is the real thread-pool one or a gate that holds the
///     read, to prove the UI thread is not used. No Avalonia window is constructed.
/// </remarks>
public sealed class SwitchBranchViewModelTests
{
    /// <summary>
    ///     A repo on <c>main</c> with another local branch and two remote branches.
    /// </summary>
    private static readonly GitBranchList Branches = new(
        "main",
        null,
        ["feature/login", "main"],
        [new GitRemoteBranch("origin", "feature/billing", "origin/feature/billing"), new GitRemoteBranch("origin", "release", "origin/release")]);

    /// <summary>
    ///     Test that the list holds the local branches first, then the remote ones, marks the
    ///     checked-out branch and says where the repo is.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Load_ListsLocalThenRemoteAndMarksCurrent()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);

        // Act
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["feature/login", "main", "feature/billing", "release"], viewModel.Items.Select(item => item.Name));
        Assert.Equal(["feature/login", "main", "origin/feature/billing", "origin/release"], viewModel.Items.Select(item => item.FullName));
        Assert.Equal([false, true, false, false], viewModel.Items.Select(item => item.IsCurrent));
        Assert.Equal(["current"], viewModel.Items.Where(item => item.IsCurrent).Select(item => item.Caption));
        Assert.Equal("remote origin", viewModel.Items[2].Caption);
        Assert.Equal(string.Empty, viewModel.Items[0].Caption);
        Assert.Equal("Currently on main", viewModel.CurrentText);
        Assert.Equal("Switch branch - Agents", viewModel.Title);
        Assert.Contains("Git Pull", viewModel.HintText, StringComparison.Ordinal);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasError);
    }

    /// <summary>
    ///     Test that the read runs through the background work runner, so the calling (UI) thread is free
    ///     while git works and no blocking call is made on it.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Load_RunsOffTheUiThread()
    {
        // Arrange: a read that is held at a gate
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = new SwitchBranchViewModel("Agents", _ => RunsOnlyOffGuardedThreads(), gate);
        BlockingCallGuard.TakeViolations();

        // Act: load from a guarded thread; the read is held
        Task loading;
        using (BlockingCallGuard.GuardCurrentThread())
        {
            loading = viewModel.LoadAsync(TestContext.Current.CancellationToken);
        }

        var violations = BlockingCallGuard.TakeViolations();
        await gate.FirstCall;
        var loadingWhileHeld = viewModel.IsLoading;
        var itemsWhileHeld = viewModel.Items.Count;
        gate.Release();
        await loading;

        // Assert: the caller was free and the list was empty meanwhile; the branches arrived afterwards
        Assert.Empty(violations);
        Assert.True(loadingWhileHeld);
        Assert.Equal(0, itemsWhileHeld);
        Assert.False(viewModel.IsLoading);
        Assert.Equal(4, viewModel.Items.Count);
    }

    /// <summary>
    ///     Test that a read that fails is shown as text, with credentials removed, and nothing can be
    ///     switched to.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Load_Fails_ShowsErrorAndCannotSwitch()
    {
        // Arrange: a read that fails and mentions an address with a password
        var viewModel = new SwitchBranchViewModel(
            "Agents",
            _ => throw new InvalidOperationException("'git for-each-ref' failed: https://bob:Hunter2PasswordValue@example.com/r.git"));

        // Act
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(viewModel.HasError);
        Assert.StartsWith("The branches could not be read:", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("Hunter2PasswordValue", viewModel.ErrorText, StringComparison.Ordinal);
        Assert.Empty(viewModel.Items);
        Assert.Null(viewModel.EmptyText);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.CanSwitch);
        Assert.False(viewModel.SwitchCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that the filter lists the branches whose name contains the text, ignoring letter case
    ///     and counting a remote branch's remote name, and that an empty result says so.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Filter_MatchesCaseInsensitiveSubstring()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        // Act / Assert: a substring of two branches, in another letter case
        viewModel.FilterText = "FEATURE";
        Assert.Equal(["feature/login", "feature/billing"], viewModel.Items.Select(item => item.Name));

        // Act / Assert: the remote's name counts
        viewModel.FilterText = "origin/";
        Assert.Equal(["feature/billing", "release"], viewModel.Items.Select(item => item.Name));

        // Act / Assert: nothing matches
        viewModel.FilterText = "zzz";
        Assert.Empty(viewModel.Items);
        Assert.Equal("No branch matches the filter.", viewModel.EmptyText);

        // Act / Assert: cleared, everything is back
        viewModel.FilterText = string.Empty;
        Assert.Equal(4, viewModel.Items.Count);
        Assert.Null(viewModel.EmptyText);
    }

    /// <summary>
    ///     Test that a filter that leaves exactly one branch to switch to selects it, and that a selection
    ///     that is filtered out is dropped.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Filter_OneBranchLeft_SelectsItAndDropsFilteredOutSelection()
    {
        // Arrange: a selection that the filter will remove
        var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        viewModel.SelectedItem = viewModel.Items[0];

        // Act: a filter that keeps only the remote release branch
        viewModel.FilterText = "release";

        // Assert: the old selection is gone and the only candidate is selected
        Assert.Equal("release", viewModel.SelectedItem?.Name);
        Assert.True(viewModel.CanSwitch);

        // Act: a filter that leaves only the current branch
        viewModel.FilterText = "main";

        // Assert: it cannot be chosen, so nothing is selected
        Assert.Null(viewModel.SelectedItem);
        Assert.False(viewModel.CanSwitch);
    }

    /// <summary>
    ///     Test that the checked-out branch cannot be switched to, while any other branch can.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_CurrentBranchSelected_CannotSwitch()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(viewModel.SwitchCommand.CanExecute(null));

        // Act / Assert: the current branch
        viewModel.SelectedItem = viewModel.Items.Single(item => item.IsCurrent);
        Assert.False(viewModel.CanSwitch);
        Assert.False(viewModel.SwitchCommand.CanExecute(null));

        // Act / Assert: another branch
        viewModel.SelectedItem = viewModel.Items[0];
        Assert.True(viewModel.CanSwitch);
        Assert.True(viewModel.SwitchCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that a detached HEAD is described with its commit, and that any branch can be chosen.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_DetachedHead_ShowsDetachedTextAndAllowsSwitch()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel(
            "Agents", _ => new GitBranchList(null, "1a2b3c4", ["main"], []));
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        // Act
        viewModel.SelectedItem = viewModel.Items[0];

        // Assert
        Assert.Equal("Detached HEAD at 1a2b3c4", viewModel.CurrentText);
        Assert.False(viewModel.Items[0].IsCurrent);
        Assert.True(viewModel.CanSwitch);
    }

    /// <summary>
    ///     Test that a repo without branches says so instead of showing an empty list.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_NoBranches_SaysSo()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", _ => new GitBranchList(null, null, [], []));

        // Act
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("This repository has no branches yet.", viewModel.EmptyText);
        Assert.Equal("Detached HEAD", viewModel.CurrentText);
    }

    /// <summary>
    ///     Test that the switch command completes the dialog once with the chosen branch, a remote one
    ///     carrying its remote-qualified name.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Switch_CompletesOnceWithChoice()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        var completions = new List<BranchChoice?>();
        viewModel.Completed += (_, choice) => completions.Add(choice);
        viewModel.SelectedItem = viewModel.Items.Single(item => item.FullName == "origin/feature/billing");

        // Act: switch, then cancel and switch again, which must not complete twice
        viewModel.SwitchCommand.Execute(null);
        viewModel.Cancel();
        viewModel.SwitchCommand.Execute(null);

        // Assert
        var choice = Assert.Single(completions);
        Assert.Equal(new BranchChoice("feature/billing", "origin/feature/billing", true), choice);
    }

    /// <summary>
    ///     Test that confirming a row (a double-click) selects it and completes with it, and that
    ///     confirming the current branch completes nothing.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Confirm_SelectsRowAndCompletes()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        var completions = new List<BranchChoice?>();
        viewModel.Completed += (_, choice) => completions.Add(choice);

        // Act / Assert: the current branch is ignored
        viewModel.Confirm(viewModel.Items.Single(item => item.IsCurrent));
        Assert.Empty(completions);

        // Act: a local branch
        viewModel.Confirm(viewModel.Items[0]);

        // Assert
        Assert.Equal(new BranchChoice("feature/login", "feature/login", false), Assert.Single(completions));
    }

    /// <summary>
    ///     Test that canceling completes the dialog with no branch, once.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Cancel_CompletesWithNone()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", _ => Branches);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        var completions = new List<BranchChoice?>();
        viewModel.Completed += (_, choice) => completions.Add(choice);

        // Act
        viewModel.CancelCommand.Execute(null);
        viewModel.Cancel();

        // Assert
        Assert.Null(Assert.Single(completions));
    }

    /// <summary>
    ///     Test that a load that is canceled (the dialog closed) is not reported as a failure.
    /// </summary>
    [Fact]
    public async Task SwitchBranchViewModel_Load_Canceled_ThrowsCanceledWithoutShowingAnError()
    {
        // Arrange
        var viewModel = new SwitchBranchViewModel("Agents", token => { token.ThrowIfCancellationRequested(); return Branches; });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => viewModel.LoadAsync(cancellation.Token));
        Assert.False(viewModel.HasError);
    }

    /// <summary>
    ///     Returns the canned branches, failing the test when run on a thread the tests guard against
    ///     blocking calls (the UI thread).
    /// </summary>
    /// <returns>The branches.</returns>
    private static GitBranchList RunsOnlyOffGuardedThreads()
    {
        Assert.False(BlockingCallGuard.IsCurrentThreadGuarded);
        return Branches;
    }
}
