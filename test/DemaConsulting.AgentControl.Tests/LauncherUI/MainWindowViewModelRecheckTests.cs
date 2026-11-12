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
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using Microsoft.Extensions.Time.Testing;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of the periodic background upgrade re-check of <see cref="MainWindowViewModel"/>: every
///     five minutes the cards' upgrade availability is checked again, so a package published while the
///     application stays open shows up.
/// </summary>
/// <remarks>
///     The view model runs on a <see cref="FakeTimeProvider"/>, so a test moves the clock by hand
///     instead of waiting, and on an in-memory source whose reads are counted. Runs in the
///     <c>RealProcess</c> collection because each card's eager <c>RefreshCheap()</c> may start git.
/// </remarks>
[Collection("RealProcess")]
public sealed class MainWindowViewModelRecheckTests : IDisposable
{
    /// <summary>
    ///     The folders to delete after a test.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that the default interval of the periodic re-check is the five minutes a cached listing
    ///     lives, and that an interval that is not positive is refused.
    /// </summary>
    [Fact]
    public void MainWindowViewModel_Constructor_RecheckInterval_DefaultsToFiveMinutesAndMustBePositive()
    {
        // Act / Assert
        Assert.Equal(TimeSpan.FromMinutes(5), MainWindowViewModel.DefaultUpgradeRecheckInterval);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MainWindowViewModel(new AppSettings(), upgradeRecheckInterval: TimeSpan.Zero));
    }

    /// <summary>
    ///     Test that a periodic round shows an upgrade for a version published after the launch-time check,
    ///     without anyone asking, and that nothing is checked before the interval has passed.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_VersionPublishedAfterLaunchCheck_ShowsUpgradeAfterInterval()
    {
        // Arrange: a card pinned to 1.0.0 whose launch-time check found nothing newer
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card);
        await viewModel.BeginUpgradeRefresh();
        Assert.False(card.IsUpgradeAvailable);
        viewModel.StartPeriodicUpgradeRecheck();

        // Act: a version is published; just under the interval passes, then the interval is reached
        feed.Publish("contoso-agents", "1.1.0");
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval - TimeSpan.FromSeconds(1));
        await viewModel.PendingPeriodicRecheck;
        var upgradeBeforeInterval = card.IsUpgradeAvailable;
        clock.Advance(TimeSpan.FromSeconds(1));
        await viewModel.PendingPeriodicRecheck;

        // Assert: nothing happened early, and the round then showed the upgrade
        Assert.False(upgradeBeforeInterval);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
        Assert.False(card.HasSourceProblem);
    }

    /// <summary>
    ///     Test that the interval can be chosen (here one minute) and that the round repeats.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_CustomInterval_RepeatsEveryInterval()
    {
        // Arrange: a one-minute interval
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card, TimeSpan.FromMinutes(1));
        viewModel.StartPeriodicUpgradeRecheck();

        // Act: two rounds, a version published before each
        feed.Publish("contoso-agents", "1.1.0");
        clock.Advance(TimeSpan.FromMinutes(1));
        await viewModel.PendingPeriodicRecheck;
        var first = card.LatestAvailableVersion;
        feed.Publish("contoso-agents", "1.2.0");
        clock.Advance(TimeSpan.FromMinutes(1));
        await viewModel.PendingPeriodicRecheck;

        // Assert
        Assert.Equal("1.1.0", first);
        Assert.Equal("1.2.0", card.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that a round does not start while the previous one is still running: a source that is
    ///     slow to answer is read once, not once per tick.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_PreviousRoundStillRunning_SkipsTheTick()
    {
        // Arrange: a source whose read is held open
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card);
        viewModel.StartPeriodicUpgradeRecheck();
        var gate = new TaskCompletionSource();
        feed.ListVersionsGate = gate.Task;
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        var running = viewModel.PendingPeriodicRecheck;
        Assert.False(running.IsCompleted);

        // Act: the next tick comes while that round is still waiting for the source
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);

        // Assert: no second round started, and the source was read once
        Assert.Same(running, viewModel.PendingPeriodicRecheck);
        Assert.Equal(1, feed.ListVersionsCalls);

        // Act: the source answers
        feed.Publish("contoso-agents", "1.1.0");
        gate.SetResult();
        await running;

        // Assert: the round finished normally, and the next tick starts a new one
        Assert.False(card.IsCheckingUpgrade);
        feed.ListVersionsGate = null;
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        Assert.NotSame(running, viewModel.PendingPeriodicRecheck);
        await viewModel.PendingPeriodicRecheck;
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that a round reads each source once however many cards pin the same package: the cards
    ///     share what the first of them read.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_SeveralCardsSamePackage_ReadsTheSourceOnce()
    {
        // Arrange: five repos pinned to the same package, all already checked once
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, repoCount: 5);
        await viewModel.BeginUpgradeRefresh();
        viewModel.StartPeriodicUpgradeRecheck();
        feed.Publish("contoso-agents", "1.1.0");
        var readsBefore = feed.ListVersionsCalls;

        // Act: one round
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: the source was read once for the whole round, and every card shows the upgrade
        Assert.Equal(1, feed.ListVersionsCalls - readsBefore);
        Assert.All(viewModel.RepoCards, card => Assert.True(card.IsUpgradeAvailable));
    }

    /// <summary>
    ///     Test that a round reads an unreachable source once however many cards pin the package (a failed
    ///     read is not cached, but the round remembers it), that every card shows the failure the way the
    ///     launch-time check shows it, and that the next round reads the source again.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_SourceUnreachable_ReadsOncePerRoundAndEveryCardShowsFailure()
    {
        // Arrange: five repos pinned to the same package at a source that cannot be reached
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new IOException("Feed is unreachable.")
        };
        var viewModel = CreateViewModel(clock, feed, repoCount: 5);
        viewModel.StartPeriodicUpgradeRecheck();

        // Act: one round
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: the source was read once, and every card shows the failed-check state
        Assert.Equal(1, feed.ListVersionsCalls);
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.True(card.IsUpgradeStatusUnknown);
            Assert.False(card.IsUpgradeAvailable);
            Assert.False(card.IsCheckingUpgrade);
        });

        // Act: the next round, with the source back
        feed.ListVersionsException = null;
        feed.Publish("contoso-agents", "1.1.0");
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: the failure was not persisted - the source was read again (once) and the cards recovered
        Assert.Equal(2, feed.ListVersionsCalls);
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.False(card.IsUpgradeStatusUnknown);
            Assert.True(card.IsUpgradeAvailable);
        });
    }

    /// <summary>
    ///     Test that a source that is still unreachable in the next round is read again (once), so the
    ///     round's memory of a failure never lasts longer than the round.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_SourceStaysUnreachable_ReadsOnceInEachRound()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new IOException("Feed is unreachable.")
        };
        var viewModel = CreateViewModel(clock, feed, repoCount: 4);
        viewModel.StartPeriodicUpgradeRecheck();

        // Act: three rounds
        for (var round = 1; round <= 3; round++)
        {
            clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
            await viewModel.PendingPeriodicRecheck;

            // Assert: one read per round
            Assert.Equal(round, feed.ListVersionsCalls);
        }
    }

    /// <summary>
    ///     Test that a source that needs a sign-in is read once per round and every card reports the
    ///     sign-in on itself, like the first card does.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_SourceNeedsSignIn_ReadsOncePerRoundAndEveryCardReportsIt()
    {
        // Arrange: five cards, a source that asks for a sign-in
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new PackageSourceAccessException(
                PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.")
        };
        var viewModel = CreateViewModel(clock, feed, repoCount: 5);
        viewModel.StartPeriodicUpgradeRecheck();

        // Act
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert
        Assert.Equal(1, feed.ListVersionsCalls);
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.True(card.SourceNeedsSignIn);
            Assert.True(card.IsUpgradeStatusUnknown);
            Assert.Contains("needs a sign-in", card.SourceProblem, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Test that when one of two sources fails and the other answers, the failing source is read once
    ///     per round (not once per card, nor twice per card), every card shows the upgrade the working
    ///     source offers, and the failing source is read again in the next round.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_OneSourceFails_FailingSourceReadOncePerRound()
    {
        // Arrange: five cards; the first source is down, the second offers a newer version
        var clock = new FakeTimeProvider();
        var down = new FakePackageSourceProvider("Down", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new IOException("Down is unreachable.")
        };
        var up = new FakePackageSourceProvider("Up", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var viewModel = CreateViewModel(clock, [down, up], repoCount: 5);
        viewModel.StartPeriodicUpgradeRecheck();

        // Act: one round
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: each source was read once, and every card shows the upgrade from the source that answered
        Assert.Equal(1, down.ListVersionsCalls);
        Assert.Equal(1, up.ListVersionsCalls);
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.True(card.IsUpgradeAvailable);
            Assert.Equal("1.1.0", card.LatestAvailableVersion);
            Assert.False(card.IsUpgradeStatusUnknown);
        });

        // Act: the next round
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: both sources were read again, once each
        Assert.Equal(2, down.ListVersionsCalls);
        Assert.Equal(2, up.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that the launch-time check shares what it learned of an unreachable source with the cards
    ///     that follow, instead of reading it once per card, and that every card shows the failure.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_LaunchCheck_SourceUnreachable_CardsShareTheFailure()
    {
        // Arrange: five repos pinned to the same package at a source that cannot be reached
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new IOException("Feed is unreachable.")
        };
        var viewModel = CreateViewModel(clock, feed, repoCount: 5);

        // Act
        await viewModel.BeginUpgradeRefresh();

        // Assert
        Assert.Equal(1, feed.ListVersionsCalls);
        Assert.All(viewModel.RepoCards, card => Assert.True(card.IsUpgradeStatusUnknown));
    }

    /// <summary>
    ///     Test that a round skips a card with no pin and a card whose folder is missing, and reads no
    ///     source when there is nothing to check.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_CardsWithNoPinOrMissingFolder_AreSkipped()
    {
        // Arrange: one repo with no pin, and one pinned repo whose folder was removed
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var unpinned = CreateTempDirectory();
        var missing = CreateTempDirectory();
        RepoPinStore.Save(missing, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var settings = CreateSettings(unpinned, missing);
        var viewModel = new MainWindowViewModel(
            settings, configDirectory: CreateTempDirectory(), timeProvider: clock,
            registryFactory: _ => new PackageSourceRegistry(settings.Sources, _ => feed));
        Directory.Delete(missing, recursive: true);
        viewModel.RepoCards[1].RefreshCheap();
        Assert.True(viewModel.RepoCards[1].IsMissing);
        viewModel.StartPeriodicUpgradeRecheck();

        // Act
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: neither card was checked, so no source was read
        Assert.Equal(0, feed.TotalCalls);
    }

    /// <summary>
    ///     Test that a card busy with an action of its own is not re-checked by a round.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_CardIsBusy_IsSkipped()
    {
        // Arrange: a card that is busy refreshing itself, held at its source read
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card);
        viewModel.StartPeriodicUpgradeRecheck();
        var gate = new TaskCompletionSource();
        feed.ListVersionsGate = gate.Task;
        var refresh = card.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(card.IsBusy);
        Assert.Equal(1, feed.ListVersionsCalls);

        // Act: a tick while the card is busy
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: the round did not read the source for it
        Assert.Equal(1, feed.ListVersionsCalls);
        gate.SetResult();
        await refresh;
    }

    /// <summary>
    ///     Test that a source that needs a sign-in is reported on the card by a round (the same way the
    ///     launch-time check reports it), and that the round neither throws nor shows a dialog.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_SourceNeedsSignIn_IsReportedOnTheCard()
    {
        // Arrange: a source that now asks for a sign-in
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card);
        await viewModel.BeginUpgradeRefresh();
        viewModel.StartPeriodicUpgradeRecheck();
        feed.ListVersionsException = new PackageSourceAccessException(
            PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.");

        // Act
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: the card says so, without a prompt (the check is the one that never asks)
        Assert.True(card.SourceNeedsSignIn);
        Assert.True(card.IsUpgradeStatusUnknown);
        Assert.False(card.IsUpgradeAvailable);
        Assert.Contains("needs a sign-in", card.SourceProblem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that each tick is handed to the UI-thread runner the caller supplied, and that the round
    ///     runs only when the runner runs it.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_UiThreadRunnerSupplied_TickRunsThroughIt()
    {
        // Arrange: a runner that queues the work instead of running it
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card);
        var queued = new List<Action>();
        viewModel.StartPeriodicUpgradeRecheck(queued.Add);
        feed.Publish("contoso-agents", "1.1.0");

        // Act: the timer fires
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);

        // Assert: nothing ran yet; running the queued tick performs the round
        Assert.Single(queued);
        Assert.Equal(0, feed.ListVersionsCalls);
        queued[0]();
        await viewModel.PendingPeriodicRecheck;
        Assert.True(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that disposing the view model stops the timer: no round starts afterwards, and starting
    ///     the re-check again is refused.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Dispose_StopsThePeriodicRecheck()
    {
        // Arrange: a running re-check
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card);
        viewModel.StartPeriodicUpgradeRecheck();
        feed.Publish("contoso-agents", "1.1.0");

        // Act: the view model is disposed (twice, which is harmless), then the interval passes
        viewModel.Dispose();
        viewModel.Dispose();
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval * 2);
        await viewModel.PendingPeriodicRecheck;

        // Assert: nothing was read, and the re-check cannot be started again
        Assert.Equal(0, feed.ListVersionsCalls);
        Assert.False(card.IsUpgradeAvailable);
        Assert.Throws<ObjectDisposedException>(() => viewModel.StartPeriodicUpgradeRecheck());
    }

    /// <summary>
    ///     Test that disposing the view model while a round waits for a source cancels the round and
    ///     leaves the card as it was.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Dispose_RoundRunning_CancelsIt()
    {
        // Arrange: a round held at the source read
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsGate = new TaskCompletionSource().Task
        };
        var viewModel = CreateViewModel(clock, feed, out var card);
        viewModel.StartPeriodicUpgradeRecheck();
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        var running = viewModel.PendingPeriodicRecheck;
        Assert.False(running.IsCompleted);

        // Act
        viewModel.Dispose();
        await running;

        // Assert: the round ended without an error, and the card did not change
        Assert.False(card.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that a round superseded by a launch-style check stays registered until it has finished
    ///     unwinding, so a tick that comes after the new check completed (while the canceled round's
    ///     provider call is still in flight) starts no second round on top of it.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_RoundSuperseded_NoNewRoundUntilItHasUnwound()
    {
        // Arrange: a round held at a provider call that does not notice cancellation
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out _);
        viewModel.StartPeriodicUpgradeRecheck();
        var gate = new TaskCompletionSource();
        feed.ListVersionsGate = gate.Task;
        feed.ListVersionsGateIgnoresCancellation = true;
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        var oldRound = viewModel.PendingPeriodicRecheck;
        Assert.False(oldRound.IsCompleted);
        Assert.Equal(1, feed.ListVersionsCalls);

        // Act: a launch-style check (settings changed) supersedes the round and completes, while the
        // canceled round's provider call is still in flight; then a tick comes
        feed.ListVersionsGate = null;
        await viewModel.BeginUpgradeRefresh();
        Assert.True(viewModel.PendingRefresh.IsCompleted);
        Assert.False(oldRound.IsCompleted);
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);

        // Assert: no second round started, and the source was not read for one
        Assert.Same(oldRound, viewModel.PendingPeriodicRecheck);
        Assert.Equal(2, feed.ListVersionsCalls);

        // Act: the canceled round finishes unwinding; the next tick may start a round
        gate.SetResult();
        await oldRound;
        var readsAfterUnwind = feed.ListVersionsCalls;
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);

        // Assert: the new round read the source once
        Assert.NotSame(oldRound, viewModel.PendingPeriodicRecheck);
        await viewModel.PendingPeriodicRecheck;
        Assert.Equal(1, feed.ListVersionsCalls - readsAfterUnwind);
    }

    /// <summary>
    ///     Test that a launch-style check that was superseded, but is still inside a provider call
    ///     that ignores cancellation, holds off the periodic round until it has unwound, even though
    ///     the newer check completed and <see cref="MainWindowViewModel.PendingRefresh"/> is done.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_PeriodicRecheck_SupersededLaunchCheckStillRunning_NoRoundUntilItHasUnwound()
    {
        // Arrange: a launch check held at a provider call that does not notice cancellation
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var gate = new TaskCompletionSource();
        feed.ListVersionsGate = gate.Task;
        feed.ListVersionsGateIgnoresCancellation = true;
        var viewModel = CreateViewModel(clock, feed, out _);
        var superseded = viewModel.BeginUpgradeRefresh();
        Assert.False(superseded.IsCompleted);
        viewModel.StartPeriodicUpgradeRecheck();

        // Act: a newer launch check supersedes it and completes; then a tick comes
        feed.ListVersionsGate = null;
        await viewModel.BeginUpgradeRefresh();
        Assert.True(viewModel.PendingRefresh.IsCompleted);
        Assert.False(superseded.IsCompleted);
        var readsBefore = feed.ListVersionsCalls;
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);

        // Assert: no round started, so the source was not read for one
        Assert.Equal(readsBefore, feed.ListVersionsCalls);

        // Act: the superseded check unwinds; the next tick may start a round
        gate.SetResult();
        await superseded;
        var readsAfterUnwind = feed.ListVersionsCalls;
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);

        // Assert: a round started and read the source once (it completes synchronously here, so its
        // task cannot be told apart from the initial completed one)
        await viewModel.PendingPeriodicRecheck;
        Assert.Equal(1, feed.ListVersionsCalls - readsAfterUnwind);
    }

    /// <summary>
    ///     Test that disposing the view model while the launch-time check waits for a source cancels that
    ///     check too (the provider's token is canceled) and that nothing reaches the card afterwards.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Dispose_LaunchCheckRunning_CancelsIt()
    {
        // Arrange: a launch-time check held at the source read, with a newer version waiting
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var gate = new TaskCompletionSource();
        feed.ListVersionsGate = gate.Task;
        var viewModel = CreateViewModel(clock, feed, out var card);
        var launch = viewModel.BeginUpgradeRefresh();
        Assert.False(launch.IsCompleted);
        Assert.NotNull(feed.LastListVersionsToken);
        Assert.False(feed.LastListVersionsToken.Value.IsCancellationRequested);

        // Act: the window closes
        viewModel.Dispose();

        // Assert: the provider was told to stop, and the check ended without the gate being released
        Assert.True(feed.LastListVersionsToken.Value.IsCancellationRequested);
        await launch.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The source answers late: the card was not updated
        gate.SetResult();
        Assert.False(card.IsUpgradeAvailable);
        Assert.Null(card.LatestAvailableVersion);
        Assert.False(card.IsCheckingUpgrade);
    }

    /// <summary>
    ///     Test that starting the periodic re-check twice does not start a second timer.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_StartPeriodicUpgradeRecheck_CalledTwice_RunsOneTimer()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out _);
        viewModel.StartPeriodicUpgradeRecheck();
        viewModel.StartPeriodicUpgradeRecheck();

        // Act
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        await viewModel.PendingPeriodicRecheck;

        // Assert: one round, one read
        Assert.Equal(1, feed.ListVersionsCalls);
    }

    /// <summary>
    ///     Creates a view model over one pinned repo whose only source is an in-memory feed.
    /// </summary>
    /// <param name="clock">The fake clock.</param>
    /// <param name="feed">The feed that serves the source.</param>
    /// <param name="card">Receives the repo's card.</param>
    /// <param name="interval">The re-check interval, or <see langword="null"/> for the default.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(
        FakeTimeProvider clock, FakePackageSourceProvider feed, out RepoCardViewModel card, TimeSpan? interval = null)
    {
        var viewModel = CreateViewModel(clock, feed, 1, interval);
        card = viewModel.RepoCards[0];
        return viewModel;
    }

    /// <summary>
    ///     Creates a view model over repos pinned to <c>contoso-agents 1.0.0</c> whose only source is an
    ///     in-memory feed.
    /// </summary>
    /// <param name="clock">The fake clock.</param>
    /// <param name="feed">The feed that serves the source.</param>
    /// <param name="repoCount">How many repos are pinned.</param>
    /// <param name="interval">The re-check interval, or <see langword="null"/> for the default.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(
        FakeTimeProvider clock, FakePackageSourceProvider feed, int repoCount, TimeSpan? interval = null) =>
        CreateViewModel(clock, [feed], repoCount, interval);

    /// <summary>
    ///     Creates a view model over repos pinned to <c>contoso-agents 1.0.0</c> whose sources are the given
    ///     in-memory feeds, in Settings order.
    /// </summary>
    /// <param name="clock">The fake clock.</param>
    /// <param name="feeds">The feeds that serve the sources, one source per feed (named by its alias).</param>
    /// <param name="repoCount">How many repos are pinned.</param>
    /// <param name="interval">The re-check interval, or <see langword="null"/> for the default.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(
        FakeTimeProvider clock, FakePackageSourceProvider[] feeds, int repoCount, TimeSpan? interval = null)
    {
        var repos = new string[repoCount];
        for (var index = 0; index < repoCount; index++)
        {
            repos[index] = CreateTempDirectory();
            RepoPinStore.Save(repos[index], new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        }

        var settings = CreateSettings(repos);
        settings.Sources =
        [
            .. feeds.Select(feed => new PackageSourceDefinition
            {
                Alias = feed.Alias, Type = PackageSourceType.Zip, Location = "unused"
            })
        ];
        return new MainWindowViewModel(
            settings,
            configDirectory: CreateTempDirectory(),
            timeProvider: clock,
            upgradeRecheckInterval: interval,
            registryFactory: _ => new PackageSourceRegistry(
                settings.Sources, definition => feeds.Single(feed => feed.Alias == definition.Alias)));
    }

    /// <summary>
    ///     Creates settings with one source, <c>Feed</c>, and the given repos.
    /// </summary>
    /// <param name="repos">The repo folders.</param>
    /// <returns>The settings.</returns>
    private static AppSettings CreateSettings(params string[] repos) => new()
    {
        Sources = [new PackageSourceDefinition { Alias = "Feed", Type = PackageSourceType.Zip, Location = "unused" }],
        RecentRepos = [.. repos.Select(path => new RecentRepo { Path = path })]
    };

    /// <summary>
    ///     Creates a unique temporary directory removed by <see cref="Dispose"/>.
    /// </summary>
    /// <returns>The folder.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_recheck_test_" + Guid.NewGuid());
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
