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
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoConfig;
using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Startup;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;
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
///     <c>RealProcess</c> collection because each card's background repo-state read may start git.
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
        await viewModel.WhenRepoStateSettledAsync();
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

        // The refresh reads the card's repo state in the background first, then its source
        await feed.WaitForListVersionsCallsAsync(1).WaitAsync(TestContext.Current.CancellationToken);
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
    ///     Test that closing Settings without saving checks a card that needed a sign-in again, so once the
    ///     source answers the card no longer says "Sign-in needed" and offers the upgrade.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_SourceNoLongerNeedsSignIn_ClearsTheProblem()
    {
        // Arrange: the launch check found that the source needs a sign-in
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new PackageSourceAccessException(
                PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.")
        };
        var viewModel = CreateViewModel(clock, feed, out var card);
        await viewModel.BeginUpgradeRefresh();
        Assert.True(card.SourceNeedsSignIn);

        // Act: the source now answers (a sign-in was made during the connection test) and Settings closes
        feed.ListVersionsException = null;
        feed.Publish("contoso-agents", "1.1.0");
        await viewModel.RecheckAfterSettingsClosed(settingsSaved: false);

        // Assert: the problem is gone and the check's result is shown
        Assert.False(card.SourceNeedsSignIn);
        Assert.False(card.HasSourceProblem);
        Assert.False(card.IsUpgradeStatusUnknown);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that closing Settings without saving checks again a card whose last check failed because
    ///     the source could not be reached, so the card shows the result once the connection is back.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_SourceUnreachableBefore_ChecksAgain()
    {
        // Arrange: the launch check could not reach the source
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"))
        {
            ListVersionsException = new IOException("Feed is unreachable.")
        };
        var viewModel = CreateViewModel(clock, feed, out var card);
        await viewModel.BeginUpgradeRefresh();
        Assert.True(card.IsUpgradeStatusUnknown);

        // Act: the connection is back and Settings closes without saving
        feed.ListVersionsException = null;
        await viewModel.RecheckAfterSettingsClosed(settingsSaved: false);

        // Assert
        Assert.False(card.IsUpgradeStatusUnknown);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that closing Settings without saving reads no source when no card shows a problem or a
    ///     failed check.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_NoCardHasAProblem_ReadsNoSource()
    {
        // Arrange: every card's launch check succeeded
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, repoCount: 3);
        await viewModel.BeginUpgradeRefresh();
        var callsBefore = feed.ListVersionsCalls;

        // Act
        await viewModel.RecheckAfterSettingsClosed(settingsSaved: false);

        // Assert: nothing was read again
        Assert.Equal(callsBefore, feed.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that after Settings was saved (which already checks every card) closing it starts no
    ///     second check.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_SettingsSaved_StartsNoSecondCheck()
    {
        // Arrange: a card that needs a sign-in, so any check reads the source again (failures are not cached)
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new PackageSourceAccessException(
                PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.")
        };
        var viewModel = CreateViewModel(clock, feed, out var card);
        await viewModel.BeginUpgradeRefresh();
        await viewModel.ApplySettingsAsync(viewModel.Settings);
        await viewModel.PendingRefresh;
        var callsAfterSave = feed.ListVersionsCalls;

        // Act: Settings closes after the save
        await viewModel.RecheckAfterSettingsClosed(settingsSaved: true);
        await viewModel.PendingRefresh;

        // Assert: the save's own check was the only one
        Assert.Equal(callsAfterSave, feed.ListVersionsCalls);
        Assert.True(card.SourceNeedsSignIn);
    }

    /// <summary>
    ///     Test that a card busy with an action of its own is not checked when Settings closes, while another
    ///     card whose last check failed is.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_CardIsBusy_IsSkipped()
    {
        // Arrange: two cards whose last check failed; the first is now busy refreshing itself, held at its
        // source read
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"))
        {
            ListVersionsException = new IOException("Feed is unreachable.")
        };
        var viewModel = CreateViewModel(clock, feed, repoCount: 2);
        var busy = viewModel.RepoCards[0];
        var idle = viewModel.RepoCards[1];
        await viewModel.BeginUpgradeRefresh();
        Assert.True(idle.IsUpgradeStatusUnknown);
        var gate = new TaskCompletionSource();
        feed.ListVersionsGate = gate.Task;
        var readsBeforeRefresh = feed.ListVersionsCalls;
        var refresh = busy.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(busy.IsBusy);

        // The refresh reads the card's repo state in the background first, then its source (held)
        await feed.WaitForListVersionsCallsAsync(readsBeforeRefresh + 1).WaitAsync(TestContext.Current.CancellationToken);
        var callsWhileBusy = feed.ListVersionsCalls;

        // Act: Settings closes while the first card is busy; then the source answers
        var check = viewModel.RecheckAfterSettingsClosed(settingsSaved: false);
        var callsOfTheCheck = feed.ListVersionsCalls - callsWhileBusy;
        feed.ListVersionsException = null;
        gate.SetResult();
        await check;
        await refresh;

        // Assert: the source was read for the idle card only, which now shows the result
        Assert.Equal(1, callsOfTheCheck);
        Assert.False(idle.IsUpgradeStatusUnknown);
        Assert.True(idle.IsUpgradeAvailable);
    }

    /// <summary>
    ///     Test that a card waiting for its turn in the re-check after Settings closed (more cards than are
    ///     checked at once) and that the user then starts refreshing is not checked by the re-check when its
    ///     turn comes, so the re-check's result never replaces the result of the user's refresh.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_WaitingCardBecomesBusy_IsNotChecked()
    {
        // Arrange: five cards whose launch check failed (more than are checked at once)
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"))
        {
            ListVersionsException = new IOException("Feed is unreachable.")
        };
        var viewModel = CreateViewModel(clock, feed, repoCount: 5);
        await viewModel.BeginUpgradeRefresh();
        Assert.All(viewModel.RepoCards, card => Assert.True(card.IsUpgradeStatusUnknown));
        feed.ListVersionsException = null;

        // Act: Settings closes; the re-check's first cards are held at their reads while the last waits its
        // turn and the user starts refreshing it; the re-check's reads then fail and it finishes, and only
        // after that does the user's refresh read the source
        var (check, waiting, refresh, refreshGate) = await StartCheckAndRefreshTheWaitingCardAsync(
            viewModel, feed, () => viewModel.RecheckAfterSettingsClosed(settingsSaved: false));
        await check;
        var busyWhenCheckEnded = waiting.IsBusy;
        feed.ListVersionsException = null;
        refreshGate.SetResult();
        await refresh;

        // Assert: the re-check did not check the busy card, which shows what the user's refresh read
        Assert.True(busyWhenCheckEnded);
        Assert.False(waiting.IsUpgradeStatusUnknown);
        Assert.True(waiting.IsUpgradeAvailable);
        Assert.Equal("1.1.0", waiting.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that a card waiting for its turn in the launch-time check (more cards than are checked at
    ///     once) and that the user then starts refreshing is not checked by the launch-time check when its
    ///     turn comes, so that check's result never replaces the result of the user's refresh.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_LaunchCheck_WaitingCardBecomesBusy_IsNotChecked()
    {
        // Arrange: five cards (more than are checked at once)
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var viewModel = CreateViewModel(clock, feed, repoCount: 5);

        // Act: the launch check starts; its first cards are held at their reads while the last waits its
        // turn and the user starts refreshing it; the check's reads then fail and it finishes, and only
        // after that does the user's refresh read the source
        var (check, waiting, refresh, refreshGate) = await StartCheckAndRefreshTheWaitingCardAsync(
            viewModel, feed, viewModel.BeginUpgradeRefresh);
        await check;
        var busyWhenCheckEnded = waiting.IsBusy;
        feed.ListVersionsException = null;
        refreshGate.SetResult();
        await refresh;

        // Assert: the launch check did not check the busy card, which shows what the user's refresh read
        Assert.True(busyWhenCheckEnded);
        Assert.False(waiting.IsUpgradeStatusUnknown);
        Assert.True(waiting.IsUpgradeAvailable);
        Assert.Equal("1.1.0", waiting.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test that the window's upgrade check started while a card's startup read is held waits for that
    ///     read - reading no source meanwhile - and then checks the card, so the read landing does not
    ///     throw the check's result away.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Startup_UpgradeCheckWaitsForCardState()
    {
        // Arrange: one pinned repo whose startup read is held, and a source offering a newer version
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = CreateViewModel(clock, [feed], 1, backgroundWork: gate);
        var card = viewModel.RepoCards[0];

        // Act: the window starts the upgrade check while the read is held
        var check = viewModel.BeginUpgradeRefresh();
        await gate.FirstCall;

        // Assert: the check waits for the card's state; no source read yet
        Assert.False(check.IsCompleted);
        Assert.False(card.IsCheckingUpgrade);
        Assert.Equal(0, feed.ListVersionsCalls);

        // Act
        gate.Release();
        await check;

        // Assert: the state was read, then the check ran and its result stands
        Assert.True(card.HasRepoState);
        Assert.Equal("1.0.0", card.PinnedPackageVersion);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
        Assert.Equal(RepoCardStatus.UpgradeAvailable, card.Status);
    }

    /// <summary>
    ///     Test that a card the user starts refreshing while the window's upgrade check waits for the card's
    ///     startup read is skipped by that check when its turn comes (it is busy), so only the user's
    ///     refresh reads the source for it.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_Startup_BusyCardSkippedByUpgradeCheck()
    {
        // Arrange: one pinned repo whose startup read is held, and a source whose listing is held for the
        // user's refresh
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var gate = new GatedBackgroundWorkRunner();
        var viewModel = CreateViewModel(clock, [feed], 1, backgroundWork: gate);
        var card = viewModel.RepoCards[0];
        var check = viewModel.BeginUpgradeRefresh();
        await gate.FirstCall;
        var refreshGate = new TaskCompletionSource();
        feed.ListVersionsGate = refreshGate.Task;

        // Act: the user refreshes the card; the reads run; the window's check ends
        var refresh = card.RefreshAsync(TestContext.Current.CancellationToken);
        gate.Release();
        await check;
        var busyWhenCheckEnded = card.IsBusy;
        var readsWhenCheckEnded = feed.ListVersionsCalls;
        refreshGate.SetResult();
        await refresh;

        // Assert: the check ended while the card was busy, without reading the source for it; the user's
        // refresh read it once and its result is shown
        Assert.True(busyWhenCheckEnded);
        Assert.True(readsWhenCheckEnded <= 1);
        Assert.Equal(1, feed.ListVersionsCalls);
        Assert.True(card.IsUpgradeAvailable);
        Assert.False(card.IsBusy);
    }

    /// <summary>
    ///     Test that overlapping background upgrade checks share one limit: while the window's check holds
    ///     four cards at their source reads, the check of two repos added meanwhile starts no read until a
    ///     slot frees, and every card gets its answer once the source answers.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_UpgradeChecks_OverlappingChecks_ShareOneLimit()
    {
        // Arrange: four pinned cards whose source listing is held, and two pinned repos to add
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var viewModel = CreateViewModel(clock, feed, repoCount: 4);
        var listingGate = new TaskCompletionSource();
        feed.ListVersionsGate = listingGate.Task;
        var added = Enumerable.Range(0, 2).Select(_ =>
        {
            var repo = CreateTempDirectory();
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            File.WriteAllText(Path.Combine(repo, ".git", "HEAD"), "ref: refs/heads/main\n");
            RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
            return repo;
        }).ToList();

        // Act: the window's check starts (four cards read at once), then two repos are added
        var first = viewModel.BeginUpgradeRefresh();
        var checkingBeforeAdd = viewModel.RepoCards.Count(card => card.IsCheckingUpgrade);
        await viewModel.EnqueueAddReposAsync(added, null);
        var addedCards = viewModel.RepoCards.Where(card => added.Contains(card.RepoPath)).ToList();

        // Assert: the added repos' checks wait for one of the four shared slots instead of reading
        Assert.Equal(4, checkingBeforeAdd);
        Assert.Equal(2, addedCards.Count);
        Assert.All(addedCards, card => Assert.False(card.IsCheckingUpgrade));

        // Act
        feed.ListVersionsGate = null;
        listingGate.SetResult();
        await first;
        await viewModel.PendingRefresh;

        // Assert: every card got its answer
        Assert.All(viewModel.RepoCards, card => Assert.True(card.IsUpgradeAvailable));
    }
    /// <summary>
    ///     Test that closing Settings after the main window's view model was disposed checks nothing.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_Disposed_ChecksNothing()
    {
        // Arrange: a card whose last check failed, and the window closed
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new IOException("Feed is unreachable.")
        };
        var viewModel = CreateViewModel(clock, feed, out var card);
        await viewModel.BeginUpgradeRefresh();
        var callsBefore = feed.ListVersionsCalls;
        viewModel.Dispose();

        // Act
        var check = viewModel.RecheckAfterSettingsClosed(settingsSaved: false);

        // Assert: nothing was started
        Assert.True(check.IsCompleted);
        await check;
        Assert.Equal(callsBefore, feed.ListVersionsCalls);
        Assert.True(card.IsUpgradeStatusUnknown);
    }

    /// <summary>
    ///     Test that closing Settings without saving makes a periodic round that is still running forget
    ///     the sign-in failure it recorded before the sign-in: the round reads the source again for the
    ///     cards it checks afterwards, instead of putting "Sign-in needed" back on a card the re-check
    ///     clears.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_RoundRecordedFailureBeforeSignIn_RoundReadsTheSourceAgain()
    {
        // Arrange: two cards whose package only a server that needs a sign-in offers (a second source
        // offers nothing); the launch check reports "Sign-in needed" on both
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new PackageSourceAccessException(
                PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.")
        };
        var other = new FakePackageSourceProvider("Other");
        var viewModel = CreateViewModel(clock, [feed, other], repoCount: 2);
        await viewModel.BeginUpgradeRefresh();
        Assert.All(viewModel.RepoCards, card => Assert.True(card.SourceNeedsSignIn));

        // Arrange: a periodic round records the server's failure for the first card and is then held
        // at the second source, before it reaches the second card
        var otherGate = new TaskCompletionSource();
        other.ListVersionsGate = otherGate.Task;
        viewModel.StartPeriodicUpgradeRecheck();
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        var round = viewModel.PendingPeriodicRecheck;
        Assert.False(round.IsCompleted);

        // Act: the user signs in (the server now answers, after a moment) and closes Settings without
        // saving; then the round goes on to the second card
        var feedGate = new TaskCompletionSource();
        feed.ListVersionsGate = feedGate.Task;
        feed.ListVersionsException = null;
        feed.Publish("contoso-agents", "1.1.0");
        var recheck = viewModel.RecheckAfterSettingsClosed(settingsSaved: false);
        var readsBeforeRoundResumes = feed.ListVersionsCalls;
        otherGate.SetResult();
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!round.IsCompleted && feed.ListVersionsCalls == readsBeforeRoundResumes && waited.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        var roundReadTheServer = feed.ListVersionsCalls > readsBeforeRoundResumes;
        feedGate.SetResult();
        await recheck;
        await round;

        // Assert: the round asked the server again for the second card rather than replaying the old
        // failure, so no card is left saying "Sign-in needed" and both offer the upgrade
        Assert.True(roundReadTheServer);
        Assert.All(viewModel.RepoCards, card =>
        {
            Assert.False(card.SourceNeedsSignIn);
            Assert.False(card.IsUpgradeStatusUnknown);
            Assert.True(card.IsUpgradeAvailable);
        });
    }

    /// <summary>
    ///     Test that closing Settings without saving checks again a card whose background check is reading
    ///     the source at that moment: the read began before the sign-in, so when it then fails the card does
    ///     not show "Sign-in needed" but the result of the newer check.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_CardCheckInFlight_OldFailureIsNotShown()
    {
        // Arrange: a card that is up to date, and a periodic round whose read of the source (still with
        // the state from before the sign-in) is held open
        var clock = new FakeTimeProvider();
        var feed = new FakePackageSourceProvider("Feed", ("contoso-agents", "1.0.0"));
        var viewModel = CreateViewModel(clock, feed, out var card);
        await viewModel.BeginUpgradeRefresh();
        Assert.False(card.HasSourceProblem);
        var gate = new TaskCompletionSource();
        feed.ListVersionsGate = gate.Task;
        viewModel.StartPeriodicUpgradeRecheck();
        clock.Advance(MainWindowViewModel.DefaultUpgradeRecheckInterval);
        var round = viewModel.PendingPeriodicRecheck;
        Assert.True(card.IsCheckingUpgrade);

        // Act: the user signs in and closes Settings without saving (the source now answers); then the
        // round's old read fails as needing a sign-in
        feed.ListVersionsGate = null;
        feed.Publish("contoso-agents", "1.1.0");
        var recheck = viewModel.RecheckAfterSettingsClosed(settingsSaved: false);
        await recheck;
        feed.ListVersionsException = new PackageSourceAccessException(
            PackageSourceFailureKind.AuthenticationFailed, "Source 'Feed' needs a sign-in.");
        gate.SetResult();
        await round;

        // Assert: the card shows the newer check's result, not the old failure
        Assert.False(card.SourceNeedsSignIn);
        Assert.False(card.HasSourceProblem);
        Assert.False(card.IsUpgradeStatusUnknown);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
    }

    /// <summary>
    ///     Test the user's scenario end to end against a NuGet server that needs a sign-in: the background
    ///     check says "Sign-in needed" without asking, the user signs in through Settings' Test connection,
    ///     and closing Settings without saving clears the chip using that sign-in, without asking again.
    /// </summary>
    [Fact]
    public async Task MainWindowViewModel_RecheckAfterSettingsClosed_SignedInDuringConnectionTest_UsesTheSignInWithoutAsking()
    {
        // Arrange: a server that needs a sign-in, a card pinned to an older version, and a sign-in prompt
        await using var feed = await MockNuGetFeed.StartAsync();
        feed.AddPackage("contoso-agents", "1.0.0");
        feed.AddPackage("contoso-agents", "1.1.0");
        feed.RequiredUser = "svc-agents";
        feed.RequiredPassword = "recheck-test-pw-7741";
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc-agents", "recheck-test-pw-7741"));
        var repo = CreateTempDirectory();
        RepoPinStore.Save(repo, new RepoPin { PackageName = "contoso-agents", Version = "1.0.0" });
        var source = new PackageSourceDefinition
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = feed.IndexUrl,
            PackageIds = ["contoso-agents"],
            AllowInsecureHttp = true
        };
        var settings = new AppSettings { Sources = [source], RecentRepos = [new RecentRepo { Path = repo }] };
        var viewModel = new MainWindowViewModel(
            settings,
            startupOptions: StartupOptions.Parse(["--config-dir", CreateTempDirectory()]),
            configDirectory: CreateTempDirectory(),
            credentials: new PackageCredentialContext(new SessionCredentialStore(), prompt));
        var card = viewModel.RepoCards[0];
        await viewModel.BeginUpgradeRefresh();
        Assert.True(card.SourceNeedsSignIn);
        Assert.Equal(0, prompt.AskCount);

        // Act: Test connection signs in (the user answers the prompt), then Settings closes without saving
        var testProvider = viewModel.CreateConnectionTestProvider(source);
        await testProvider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);
        Assert.Equal(1, prompt.AskCount);
        await viewModel.RecheckAfterSettingsClosed(settingsSaved: false);

        // Assert: the card used the session's sign-in, without asking again
        Assert.Equal(1, prompt.AskCount);
        Assert.False(card.SourceNeedsSignIn);
        Assert.False(card.HasSourceProblem);
        Assert.True(card.IsUpgradeAvailable);
        Assert.Equal("1.1.0", card.LatestAvailableVersion);
        viewModel.Dispose();
    }

    /// <summary>
    ///     Starts a background check of five cards with the source's reads held open, so four cards are
    ///     reading and the fifth waits for its turn; starts the user's refresh of the waiting card, whose read
    ///     is held open by a gate of its own; then makes every later read fail at once and lets the
    ///     background check's held reads go on (they fail too). Returns once the refresh is held at its
    ///     source read.
    /// </summary>
    /// <remarks>
    ///     A background check started on the busy card when its turn comes would read the source at once,
    ///     fail, and - being the card's newest check - make the card show that failure in place of what the
    ///     user's refresh reads afterwards.
    /// </remarks>
    /// <param name="viewModel">The view model, over five cards.</param>
    /// <param name="feed">The cards' only source.</param>
    /// <param name="startCheck">Starts the background check.</param>
    /// <returns>The background check, the waiting card, the user's refresh of it and the gate that holds
    ///     that refresh's read.</returns>
    private static async Task<(Task Check, RepoCardViewModel Waiting, Task Refresh, TaskCompletionSource RefreshGate)>
        StartCheckAndRefreshTheWaitingCardAsync(
            MainWindowViewModel viewModel, FakePackageSourceProvider feed, Func<Task> startCheck)
    {
        var checkGate = new TaskCompletionSource();
        feed.ListVersionsGate = checkGate.Task;
        var check = startCheck();
        var waiting = Assert.Single(viewModel.RepoCards, card => !card.IsCheckingUpgrade);

        var refreshGate = new TaskCompletionSource();
        feed.ListVersionsGate = refreshGate.Task;
        var readsBeforeRefresh = feed.ListVersionsCalls;
        var refresh = waiting.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(waiting.IsBusy);

        // The refresh reads the card's repo state in the background first, then the source (held)
        await feed.WaitForListVersionsCallsAsync(readsBeforeRefresh + 1).WaitAsync(TestContext.Current.CancellationToken);

        feed.ListVersionsGate = null;
        feed.ListVersionsException = new IOException("Feed is unreachable.");
        checkGate.SetResult();
        return (check, waiting, refresh, refreshGate);
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
    /// <param name="backgroundWork">Runs the cards' repo-state reads, or <see langword="null"/> for the
    ///     thread pool; with <see langword="null"/> the view model is returned once those reads settled.</param>
    /// <returns>The view model.</returns>
    private MainWindowViewModel CreateViewModel(
        FakeTimeProvider clock,
        FakePackageSourceProvider[] feeds,
        int repoCount,
        TimeSpan? interval = null,
        IBackgroundWorkRunner? backgroundWork = null)
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
        var viewModel = new MainWindowViewModel(
            settings,
            configDirectory: CreateTempDirectory(),
            timeProvider: clock,
            upgradeRecheckInterval: interval,
            registryFactory: _ => new PackageSourceRegistry(
                settings.Sources, definition => feeds.Single(feed => feed.Alias == definition.Alias)),
            backgroundWork: backgroundWork);

        // The cards' repo state is read in the background at construction; these tests are about the
        // upgrade checks, so they start once it has settled (a test that holds the reads does not wait).
        // Waited for on a pool thread, so no synchronization context is needed.
        if (backgroundWork is null)
        {
            Task.Run(viewModel.WhenRepoStateSettledAsync).GetAwaiter().GetResult();
        }

        return viewModel;
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
