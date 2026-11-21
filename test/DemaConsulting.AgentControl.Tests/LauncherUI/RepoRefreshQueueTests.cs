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

using System.Collections.Concurrent;
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="RepoRefreshQueue"/>.
/// </summary>
/// <remarks>
///     The reads are fakes that block on gates the test opens, and count how many run at once, so
///     every test controls exactly when each read finishes and needs no timing assumption. Each
///     queue is created with no synchronization context current (unless the test is about the
///     context), so its progress is reported on the changing thread.
/// </remarks>
public class RepoRefreshQueueTests
{
    /// <summary>
    ///     Test that however many reads are requested at once, never more than two run at the same
    ///     time, and every read still runs and delivers its result.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_ManyReads_NeverMoreThanLimitAtOnce()
    {
        // Arrange: ten reads that all block on one gate
        using var queue = CreateQueue();
        var probe = new ReadProbe();

        // Act: request them all, then wait until the queue has started as many as it will
        var reads = Enumerable.Range(0, 10)
            .Select(index => Enqueue(queue, Key($"repo{index}"), RepoRefreshPriority.Background, probe.Read($"repo{index}", index)))
            .ToList();
        await probe.WaitForStartsAsync(2);
        var runningWhileHeld = probe.Running;

        // Assert: two run while the gate is closed
        Assert.Equal(RepoRefreshQueue.DefaultMaxConcurrentReads, runningWhileHeld);

        // Act: open every gate and let them all finish
        probe.ReleaseAll();
        var results = await Task.WhenAll(reads);

        // Assert: every read ran once, never more than two at a time
        Assert.Equal(Enumerable.Range(0, 10), results);
        Assert.Equal(10, probe.Starts.Count);
        Assert.Equal(2, probe.MaxRunning);
    }

    /// <summary>
    ///     Test that a second request for a read that is still waiting joins it: the read runs once,
    ///     with the newest request's delegate, and both requesters get its result.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_SameKeyWhileWaiting_JoinsAndReadsOnce()
    {
        // Arrange: one slot, held by a blocker, so the next read waits
        using var queue = CreateQueue(maxConcurrentReads: 1);
        var probe = new ReadProbe();
        var blocker = Enqueue(queue, Key("blocker"), RepoRefreshPriority.Background, probe.Read("blocker", 0));
        await probe.WaitForStartsAsync(1);

        // Act: two requests for the same read while it waits, the second with a newer delegate
        var first = Enqueue(queue, Key("repo"), RepoRefreshPriority.Background, probe.Read("old", 1));
        var second = Enqueue(queue, Key("REPO"), RepoRefreshPriority.Background, probe.Read("new", 2));
        probe.ReleaseAll();
        await blocker;
        var results = await Task.WhenAll(first, second);

        // Assert: one read, the newest delegate's, shared by both requesters
        Assert.Equal([2, 2], results);
        Assert.Equal(["blocker", "new"], probe.Starts);
    }

    /// <summary>
    ///     Test that a request for a read that is already running is not joined to it (the running
    ///     read may predate the change the requester reacts to): one new read is queued, and a further
    ///     request joins that one.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_SameKeyWhileRunning_QueuesOneNewRead()
    {
        // Arrange: one slot, in which the repo's read runs
        using var queue = CreateQueue(maxConcurrentReads: 1);
        var probe = new ReadProbe();
        var running = Enqueue(queue, Key("repo"), RepoRefreshPriority.Background, probe.Read("first", 1));
        await probe.WaitForStartsAsync(1);

        // Act: two more requests for the same repo while it runs
        var second = Enqueue(queue, Key("repo"), RepoRefreshPriority.Background, probe.Read("second", 2));
        var third = Enqueue(queue, Key("repo"), RepoRefreshPriority.Background, probe.Read("third", 3));
        probe.ReleaseAll();
        var results = await Task.WhenAll(running, second, third);

        // Assert: the running read, then one new read shared by the later two requests
        Assert.Equal([1, 3, 3], results);
        Assert.Equal(["first", "third"], probe.Starts);
    }

    /// <summary>
    ///     Test that when a slot frees, waiting reads start by priority (user action, then visible,
    ///     then background) and in request order within a priority.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_HigherPriority_StartsBeforeEarlierBackground()
    {
        // Arrange: one slot, held by a blocker
        using var queue = CreateQueue(maxConcurrentReads: 1);
        var probe = new ReadProbe();
        var blocker = Enqueue(queue, Key("blocker"), RepoRefreshPriority.Background, probe.Read("blocker", 0));
        await probe.WaitForStartsAsync(1);

        // Act: background reads first, then a user action and a visible card
        var reads = new List<Task<int>>
        {
            blocker,
            Enqueue(queue, Key("b1"), RepoRefreshPriority.Background, probe.Read("b1", 1)),
            Enqueue(queue, Key("b2"), RepoRefreshPriority.Background, probe.Read("b2", 2)),
            Enqueue(queue, Key("user"), RepoRefreshPriority.UserAction, probe.Read("user", 3)),
            Enqueue(queue, Key("visible"), RepoRefreshPriority.Visible, probe.Read("visible", 4))
        };
        probe.ReleaseAll();
        await Task.WhenAll(reads);

        // Assert
        Assert.Equal(["blocker", "user", "visible", "b1", "b2"], probe.Starts);
    }

    /// <summary>
    ///     Test that promoting a waiting read makes it start before the reads of its former priority,
    ///     and that a running read cannot be promoted.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_Promote_MovesWaitingItemAhead()
    {
        // Arrange: one slot, held by a blocker, and three waiting background reads
        using var queue = CreateQueue(maxConcurrentReads: 1);
        var probe = new ReadProbe();
        var reads = new List<Task<int>>
        {
            Enqueue(queue, Key("blocker"), RepoRefreshPriority.Background, probe.Read("blocker", 0))
        };
        await probe.WaitForStartsAsync(1);
        reads.Add(Enqueue(queue, Key("b1"), RepoRefreshPriority.Background, probe.Read("b1", 1)));
        reads.Add(Enqueue(queue, Key("b2"), RepoRefreshPriority.Background, probe.Read("b2", 2)));
        reads.Add(Enqueue(queue, Key("b3"), RepoRefreshPriority.Background, probe.Read("b3", 3)));

        // Act: the last one is shown on screen; the running one is promoted too
        var promotedWaiting = queue.Promote(Key("b3"), RepoRefreshPriority.Visible);
        var promotedRunning = queue.Promote(Key("blocker"), RepoRefreshPriority.UserAction);
        probe.ReleaseAll();
        await Task.WhenAll(reads);

        // Assert: the promoted read ran next; the running one was not waiting
        Assert.True(promotedWaiting);
        Assert.False(promotedRunning);
        Assert.Equal(["blocker", "b3", "b1", "b2"], probe.Starts);
    }

    /// <summary>
    ///     Test that the progress counts the current wave of counted reads: the total grows as reads
    ///     are accepted (a joined request adds nothing), the count of completed ones grows as they
    ///     finish, the wave ends when the last one finishes, and a later counted read starts a new
    ///     wave.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_Progress_CountsCurrentWave()
    {
        // Arrange: one slot, and a recorder of every progress report
        using var queue = CreateQueue(maxConcurrentReads: 1);
        var probe = new ReadProbe();
        var reports = new ConcurrentQueue<RepoRefreshProgress>();
        queue.ProgressChanged += (_, progress) => reports.Enqueue(progress);

        // Act: three counted reads
        var a = Enqueue(queue, Key("a"), RepoRefreshPriority.Background, probe.Read("a", 1), reportsProgress: true);
        var b = Enqueue(queue, Key("b"), RepoRefreshPriority.Background, probe.Read("b", 2), reportsProgress: true);
        var c = Enqueue(queue, Key("c"), RepoRefreshPriority.Background, probe.Read("c", 3), reportsProgress: true);
        var accepted = queue.Progress;

        // Act: the first finishes
        probe.Release("a");
        await a;
        var afterFirst = queue.Progress;

        // Act: a request joins a waiting read, then two more counted reads arrive
        var cAgain = Enqueue(queue, Key("c"), RepoRefreshPriority.Background, probe.Read("c", 3), reportsProgress: true);
        var afterJoin = queue.Progress;
        var d = Enqueue(queue, Key("d"), RepoRefreshPriority.Background, probe.Read("d", 4), reportsProgress: true);
        var e = Enqueue(queue, Key("e"), RepoRefreshPriority.Background, probe.Read("e", 5), reportsProgress: true);
        var grown = queue.Progress;

        // Act: everything finishes
        probe.ReleaseAll();
        await Task.WhenAll(b, c, cAgain, d, e);
        var ended = queue.Progress;

        // Act: a new counted read
        var f = Enqueue(queue, Key("f"), RepoRefreshPriority.Background, probe.Read("f", 6), reportsProgress: true);
        var newWave = queue.Progress;
        await f;

        // Assert
        Assert.Equal(new RepoRefreshProgress(0, 3, true), accepted);
        Assert.Equal(new RepoRefreshProgress(1, 3, true), afterFirst);
        Assert.Equal(new RepoRefreshProgress(1, 3, true), afterJoin);
        Assert.Equal(new RepoRefreshProgress(1, 5, true), grown);
        Assert.Equal(new RepoRefreshProgress(5, 5, false), ended);
        Assert.Equal(new RepoRefreshProgress(0, 1, true), newWave);
        Assert.Equal(new RepoRefreshProgress(1, 1, false), queue.Progress);
        Assert.Equal(new RepoRefreshProgress(0, 1, true), reports.First());
        Assert.Contains(new RepoRefreshProgress(5, 5, false), reports);
        Assert.Equal(new RepoRefreshProgress(1, 1, false), reports.Last());
    }

    /// <summary>
    ///     Test that a waiting read whose only requester gives up is removed: the requester's task is
    ///     canceled, the read never runs, and it leaves the progress count.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_RequesterCanceledWhileWaiting_RemovedFromQueueAndWave()
    {
        // Arrange: one slot, held by a blocker, and a counted read waiting behind it
        using var queue = CreateQueue(maxConcurrentReads: 1);
        var probe = new ReadProbe();
        var blocker = Enqueue(queue, Key("blocker"), RepoRefreshPriority.Background, probe.Read("blocker", 0));
        await probe.WaitForStartsAsync(1);
        using var cancellation = new CancellationTokenSource();
        var waiting = queue.EnqueueAsync(
            Key("repo"), RepoRefreshPriority.Background, probe.Read("repo", 1), reportsProgress: true, cancellation.Token);
        var before = queue.Progress;

        // Act: the requester gives up, then the blocker finishes
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var afterCancel = queue.Progress;
        probe.ReleaseAll();
        await blocker;
        await queue.WhenIdleAsync();

        // Assert: counted while it waited, gone afterwards, and never read
        Assert.Equal(new RepoRefreshProgress(0, 1, true), before);
        Assert.Equal(new RepoRefreshProgress(0, 0, false), afterCancel);
        Assert.Equal(["blocker"], probe.Starts);
    }

    /// <summary>
    ///     Test that disposing the queue (the window closed) signals the running read's token, cancels
    ///     the waiting read without running it, ends the progress, and refuses later requests.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_Dispose_CancelsWaitingAndSignalsRunningAndRefusesNew()
    {
        // Arrange: one slot whose read waits for its token, and a counted read waiting behind it
        var queue = CreateQueue(maxConcurrentReads: 1);
        var probe = new ReadProbe();
        var runningStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Enqueue(queue, Key("running"), RepoRefreshPriority.Background, token =>
        {
            // Stands in for git: stops when the token is signaled
            var stopped = new TaskCompletionSource();
            using var registration = token.Register(() => stopped.TrySetResult());
            runningStarted.TrySetResult();
            stopped.Task.Wait(TestContext.Current.CancellationToken);
            token.ThrowIfCancellationRequested();
            return 0;
        });
        await runningStarted.Task;
        var waiting = Enqueue(
            queue,
            Key("waiting"), RepoRefreshPriority.Background, probe.Read("waiting", 1), reportsProgress: true);

        // Act
        queue.Dispose();
        var later = Enqueue(queue, Key("later"), RepoRefreshPriority.UserAction, probe.Read("later", 2));

        // Assert: everything canceled, nothing else read, no progress shown
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => later);
        Assert.Empty(probe.Starts);
        Assert.False(queue.Progress.IsActive);
    }

    /// <summary>
    ///     Test that a read that throws faults only its own requesters' task; other reads still deliver
    ///     their results.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_ReadThrows_FaultsOnlyThatTask()
    {
        // Arrange
        using var queue = CreateQueue();

        // Act
        var failing = Enqueue<int>(queue,
            Key("failing"), RepoRefreshPriority.Background, _ => throw new InvalidOperationException("git is broken"));
        var working = Enqueue(queue, Key("working"), RepoRefreshPriority.Background, _ => 42);

        // Assert
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
        Assert.Equal("git is broken", failure.Message);
        Assert.Equal(42, await working);
    }

    /// <summary>
    ///     Test that the progress is posted to the synchronization context that was current when the
    ///     queue was created (the UI thread in the application), not raised on the requesting thread.
    /// </summary>
    [Fact]
    public async Task RepoRefreshQueue_ProgressChanged_PostedToCapturedContext()
    {
        // Arrange: a queue created under a context that only collects what is posted to it
        var context = new CollectingSynchronizationContext();
        RepoRefreshQueue queue;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            queue = new RepoRefreshQueue();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (queue)
        {
            var reports = new List<RepoRefreshProgress>();
            queue.ProgressChanged += (_, progress) => reports.Add(progress);
            var probe = new ReadProbe();

            // Act: a counted read is accepted
            var read = Enqueue(queue, Key("repo"), RepoRefreshPriority.Background, probe.Read("repo", 1), reportsProgress: true);
            var raisedInline = reports.Count;
            context.RunPosted();

            // Assert: not raised inline; raised when the context ran what was posted
            Assert.Equal(0, raisedInline);
            Assert.Equal([new RepoRefreshProgress(0, 1, true)], reports);

            // Act: the read finishes
            probe.ReleaseAll();
            await read;
            context.RunPosted();

            // Assert: the end of the wave arrived through the context too
            Assert.Equal(new RepoRefreshProgress(1, 1, false), reports[^1]);
        }
    }

    /// <summary>
    ///     Creates a queue with no synchronization context current, so its progress is raised on the
    ///     changing thread.
    /// </summary>
    /// <param name="maxConcurrentReads">The queue's limit.</param>
    /// <returns>The queue.</returns>
    private static RepoRefreshQueue CreateQueue(int maxConcurrentReads = RepoRefreshQueue.DefaultMaxConcurrentReads)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return new RepoRefreshQueue(maxConcurrentReads: maxConcurrentReads);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    ///     Requests a read on behalf of the test, with the test's own cancellation.
    /// </summary>
    /// <typeparam name="T">The read's result type.</typeparam>
    /// <param name="queue">The queue.</param>
    /// <param name="key">The read's key.</param>
    /// <param name="priority">Its priority.</param>
    /// <param name="read">The read.</param>
    /// <param name="reportsProgress">Whether it counts in the progress.</param>
    /// <returns>The request's task.</returns>
    private static Task<T> Enqueue<T>(
        RepoRefreshQueue queue,
        RepoRefreshKey key,
        RepoRefreshPriority priority,
        Func<CancellationToken, T> read,
        bool reportsProgress = false) =>
        queue.EnqueueAsync(key, priority, read, reportsProgress, TestContext.Current.CancellationToken);

    /// <summary>
    ///     Builds the key of a cheap-facts read of a repo.
    /// </summary>
    /// <param name="repo">The repo's name, used as its path.</param>
    /// <returns>The key.</returns>
    private static RepoRefreshKey Key(string repo) => new(Path.Combine(Path.GetTempPath(), repo), RepoRefreshKind.CheapFacts);

    /// <summary>
    ///     Fake reads that record when they start, count how many run at once, and block until the
    ///     test releases them; safe for concurrent use.
    /// </summary>
    private sealed class ReadProbe
    {
        /// <summary>
        ///     Guards the fields below.
        /// </summary>
        private readonly Lock _lock = new();

        /// <summary>
        ///     The gate of each read by name; created when first needed.
        /// </summary>
        private readonly Dictionary<string, TaskCompletionSource> _gates = [];

        /// <summary>
        ///     The names of the reads that started, in order.
        /// </summary>
        private readonly List<string> _starts = [];

        /// <summary>
        ///     The waiters for a number of starts.
        /// </summary>
        private readonly List<(int Count, TaskCompletionSource Reached)> _startWaiters = [];

        /// <summary>
        ///     Whether every gate, including later ones, is open.
        /// </summary>
        private bool _allReleased;

        /// <summary>
        ///     How many reads run now.
        /// </summary>
        private int _running;

        /// <summary>
        ///     Gets the names of the reads that started, in order.
        /// </summary>
        public IReadOnlyList<string> Starts
        {
            get
            {
                lock (_lock)
                {
                    return [.. _starts];
                }
            }
        }

        /// <summary>
        ///     Gets how many reads run now.
        /// </summary>
        public int Running
        {
            get
            {
                lock (_lock)
                {
                    return _running;
                }
            }
        }

        /// <summary>
        ///     Gets the most reads that ran at the same time.
        /// </summary>
        public int MaxRunning { get; private set; }

        /// <summary>
        ///     Builds a read that records its start, blocks on its gate and returns a value.
        /// </summary>
        /// <param name="name">The read's name (its gate).</param>
        /// <param name="value">What it returns.</param>
        /// <returns>The read.</returns>
        public Func<CancellationToken, int> Read(string name, int value) => _ =>
        {
            Task gate;
            lock (_lock)
            {
                _starts.Add(name);
                _running++;
                MaxRunning = Math.Max(MaxRunning, _running);
                foreach (var waiter in _startWaiters.Where(waiter => _starts.Count >= waiter.Count))
                {
                    waiter.Reached.TrySetResult();
                }

                gate = GateOf(name).Task;
            }

            gate.Wait(TestContext.Current.CancellationToken);
            lock (_lock)
            {
                _running--;
            }

            return value;
        };

        /// <summary>
        ///     Waits until at least the given number of reads have started.
        /// </summary>
        /// <param name="count">The number of starts.</param>
        /// <returns>A task that completes when they have.</returns>
        public Task WaitForStartsAsync(int count)
        {
            lock (_lock)
            {
                if (_starts.Count >= count)
                {
                    return Task.CompletedTask;
                }

                var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _startWaiters.Add((count, reached));
                return reached.Task.WaitAsync(TestContext.Current.CancellationToken);
            }
        }

        /// <summary>
        ///     Opens one read's gate.
        /// </summary>
        /// <param name="name">The read's name.</param>
        public void Release(string name)
        {
            lock (_lock)
            {
                GateOf(name).TrySetResult();
            }
        }

        /// <summary>
        ///     Opens every gate, including those of reads that have not started yet.
        /// </summary>
        public void ReleaseAll()
        {
            lock (_lock)
            {
                _allReleased = true;
                foreach (var gate in _gates.Values)
                {
                    gate.TrySetResult();
                }
            }
        }

        /// <summary>
        ///     Gets (creating it when needed) a read's gate; opened at once after <see cref="ReleaseAll"/>.
        ///     Must be called under the lock.
        /// </summary>
        /// <param name="name">The read's name.</param>
        /// <returns>The gate.</returns>
        private TaskCompletionSource GateOf(string name)
        {
            if (!_gates.TryGetValue(name, out var gate))
            {
                gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _gates[name] = gate;
            }

            if (_allReleased)
            {
                gate.TrySetResult();
            }

            return gate;
        }
    }

    /// <summary>
    ///     A synchronization context that only collects what is posted to it, until the test runs it.
    /// </summary>
    private sealed class CollectingSynchronizationContext : SynchronizationContext
    {
        /// <summary>
        ///     What was posted and not run yet.
        /// </summary>
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

        /// <inheritdoc />
        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        /// <summary>
        ///     Runs everything posted so far, in order, on the calling thread.
        /// </summary>
        public void RunPosted()
        {
            while (_posted.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }
}
