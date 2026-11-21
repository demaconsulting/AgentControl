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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     What a read through the <see cref="RepoRefreshQueue"/> finds out about a repo; part of the
///     read's identity, so two requests to read the same thing about the same repo are coalesced.
/// </summary>
internal enum RepoRefreshKind
{
    /// <summary>
    ///     The card's cheap facts: whether the folder exists, its pin file, branch, <c>HEAD</c> and
    ///     committed agent folders.
    /// </summary>
    CheapFacts,

    /// <summary>
    ///     Whether the working tree has uncommitted changes (<c>git status</c>).
    /// </summary>
    WorkingTree,

    /// <summary>
    ///     What a folder that is being added is (missing, a file, a plain folder or a Git repository
    ///     root) and, for a new repo, its cheap facts.
    /// </summary>
    FolderInspection
}

/// <summary>
///     How urgently a read through the <see cref="RepoRefreshQueue"/> is wanted; a higher priority
///     starts first.
/// </summary>
internal enum RepoRefreshPriority
{
    /// <summary>
    ///     Background work nobody is looking at yet (a card that is not shown, a bulk refresh).
    /// </summary>
    Background = 0,

    /// <summary>
    ///     A card that is shown in the window, or a folder the user is adding.
    /// </summary>
    Visible = 1,

    /// <summary>
    ///     A read a user action waits for (Refresh, an action on the card).
    /// </summary>
    UserAction = 2
}

/// <summary>
///     Identifies a read through the <see cref="RepoRefreshQueue"/>: the repo and what is read about
///     it. Two requests with equal keys read the same thing, so a waiting request is joined rather than
///     queued again.
/// </summary>
/// <remarks>
///     The path is compared ignoring letter case, like every repo path in the application; callers
///     pass the path in the form the card or the add uses (normalized), so spellings of one folder
///     match.
/// </remarks>
/// <param name="RepoPath">The repo's (or folder's) path. Must not be <see langword="null"/>.</param>
/// <param name="Kind">What is read about it.</param>
internal readonly record struct RepoRefreshKey(string RepoPath, RepoRefreshKind Kind)
{
    /// <summary>
    ///     Determines whether two keys name the same read: the same kind for the same path, ignoring
    ///     letter case.
    /// </summary>
    /// <param name="other">The other key.</param>
    /// <returns><see langword="true"/> when the keys are equal.</returns>
    public bool Equals(RepoRefreshKey other) =>
        Kind == other.Kind && string.Equals(RepoPath, other.RepoPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Gets a hash code consistent with <see cref="Equals(RepoRefreshKey)"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(RepoPath ?? string.Empty));
}

/// <summary>
///     How far the current wave of counted reads (see <see cref="RepoRefreshQueue"/>) has got.
/// </summary>
/// <param name="Completed">How many of the wave's reads have finished.</param>
/// <param name="Total">How many reads the wave has accepted (requests that were canceled before
///     they started are no longer counted).</param>
/// <param name="IsActive">Whether the wave is still running: a counted read waits or runs.</param>
internal readonly record struct RepoRefreshProgress(int Completed, int Total, bool IsActive);

/// <summary>
///     The one queue every lazy repo-state read goes through - the cheap facts and working-tree checks
///     of the cards and the folder checks of an add - so that however many repos and callers there
///     are, only a few git processes and disk reads run at the same time.
/// </summary>
/// <remarks>
///     <para>
///     <b>Limit.</b> At most <see cref="DefaultMaxConcurrentReads"/> (two) reads run at a time. One read
///     is at most one git process (or one burst of file-system reads). Two rather than one, so a single
///     slow repo - on a network share, with a huge working tree, or a git that hangs until its time
///     limit - cannot stall every other read, including a user action promoted behind it. Not more,
///     because the reads compete for the same disk and are scanned at process start by antivirus: more
///     parallel <c>git status</c> runs give little and make the machine sluggish.
///     </para>
///     <para>
///     <b>Coalescing.</b> A request whose key matches a read that is waiting joins it and gets the same
///     result: the read takes the higher of the two priorities, is counted in the progress if either
///     request asks for it, and uses the newest request's read delegate (which captured the newest
///     settings). A request whose key matches a read that is already running queues a new read, because
///     the running one may predate the change the requester reacts to; that new read can itself be
///     joined. So a waiting read is never queued twice, and nothing reads stale data.
///     </para>
///     <para>
///     <b>Priority.</b> When a slot frees, the waiting read with the highest
///     <see cref="RepoRefreshPriority"/> starts; reads of equal priority start in the order they were
///     queued. <see cref="Promote"/> raises a waiting read's priority.
///     </para>
///     <para>
///     <b>Cancellation.</b> A requester whose token is signaled gets a canceled task; a waiting read
///     every requester has given up on is removed (and leaves the progress count). A running read is
///     not interrupted by a requester. <see cref="Dispose"/> cancels everything: the running reads get
///     a signaled token (a git process is killed through it), the waiting ones are canceled, and later
///     requests are refused with a canceled task.
///     </para>
///     <para>
///     <b>Progress.</b> Reads requested with <c>reportsProgress</c> form waves: a wave starts with the
///     first counted read accepted while none is pending and ends when no counted read waits or runs.
///     <see cref="ProgressChanged"/> reports each change, posted to the synchronization context that
///     was current when the queue was created (the UI thread in the application), in order; without
///     one it is raised on the thread that made the change, one raise at a time.
///     </para>
///     <para>
///     <b>Threads.</b> Every member is safe to call from any thread. The bookkeeping runs under one
///     lock and does no I/O, so requesting a read on the UI thread costs a few in-memory operations.
///     The reads run through the <see cref="IBackgroundWorkRunner"/> (the thread pool in the
///     application). A requester's task completes asynchronously, so its continuation runs on the
///     requester's own captured context (the UI thread in the application), never inside the queue's
///     lock.
///     </para>
/// </remarks>
internal sealed class RepoRefreshQueue : IDisposable
{
    /// <summary>
    ///     How many reads run at the same time unless the constructor is told otherwise; see the class
    ///     remarks for why two.
    /// </summary>
    public const int DefaultMaxConcurrentReads = 2;

    /// <summary>
    ///     Runs each read off the calling thread.
    /// </summary>
    private readonly IBackgroundWorkRunner _backgroundWork;

    /// <summary>
    ///     The most reads that run at the same time.
    /// </summary>
    private readonly int _maxConcurrentReads;

    /// <summary>
    ///     The context <see cref="ProgressChanged"/> is posted to, or <see langword="null"/> to raise it
    ///     on the thread that made the change.
    /// </summary>
    private readonly SynchronizationContext? _progressContext;

    /// <summary>
    ///     Guards every field below.
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    ///     Serializes the raises of <see cref="ProgressChanged"/> when there is no context to post to.
    /// </summary>
    private readonly Lock _eventLock = new();

    /// <summary>
    ///     Signaled by <see cref="Dispose"/>; every running read gets its token.
    /// </summary>
    /// <remarks>
    ///     Canceled before it is disposed, so a read still running afterwards sees its token signaled
    ///     (a canceled token stays canceled once its source is disposed).
    /// </remarks>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    ///     The token of <see cref="_shutdown"/>, taken once so it can still be handed out after the
    ///     source was disposed.
    /// </summary>
    private readonly CancellationToken _shutdownToken;

    /// <summary>
    ///     The reads that wait for a slot, in the order they were queued.
    /// </summary>
    private readonly List<QueuedRead> _waiting = [];

    /// <summary>
    ///     The waiting reads by key, so a request can join one.
    /// </summary>
    private readonly Dictionary<RepoRefreshKey, QueuedRead> _waitingByKey = [];

    /// <summary>
    ///     How many reads are running.
    /// </summary>
    private int _running;

    /// <summary>
    ///     The order number the next queued read gets.
    /// </summary>
    private long _nextSequence;

    /// <summary>
    ///     How many counted reads wait or run.
    /// </summary>
    private int _countedPending;

    /// <summary>
    ///     The current (or last) wave's progress.
    /// </summary>
    private RepoRefreshProgress _progress;

    /// <summary>
    ///     How many progress changes were made, so a raise without a context never repeats or goes
    ///     back to an older one.
    /// </summary>
    private long _progressVersion;

    /// <summary>
    ///     The progress version last raised without a context.
    /// </summary>
    private long _raisedProgressVersion;

    /// <summary>
    ///     Completes when the queue is next idle, or <see langword="null"/> when nobody waits for that.
    /// </summary>
    private TaskCompletionSource? _idle;

    /// <summary>
    ///     Whether <see cref="Dispose"/> was called.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Initializes a new queue.
    /// </summary>
    /// <remarks>
    ///     Captures the current <see cref="SynchronizationContext"/> for <see cref="ProgressChanged"/>,
    ///     so create the queue on the thread that shows the progress (the UI thread in the
    ///     application).
    /// </remarks>
    /// <param name="backgroundWork">Runs each read off the calling thread, or
    ///     <see langword="null"/> for the thread pool. Supplied so tests can hold the reads at a
    ///     gate.</param>
    /// <param name="maxConcurrentReads">The most reads that run at the same time; at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    ///     <paramref name="maxConcurrentReads"/> is less than one.</exception>
    public RepoRefreshQueue(IBackgroundWorkRunner? backgroundWork = null, int maxConcurrentReads = DefaultMaxConcurrentReads)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentReads, 1);
        _backgroundWork = backgroundWork ?? ThreadPoolBackgroundWorkRunner.Instance;
        _maxConcurrentReads = maxConcurrentReads;
        _progressContext = SynchronizationContext.Current;
        _shutdownToken = _shutdown.Token;
    }

    /// <summary>
    ///     Raised when the progress of the current wave of counted reads changes: a wave starts, a read
    ///     is accepted, finishes or is given up, or the wave ends.
    /// </summary>
    /// <remarks>
    ///     Posted to the synchronization context captured by the constructor, in the order of the
    ///     changes; raised on the changing thread, one raise at a time and never with an older value
    ///     than one already raised, when there was none.
    /// </remarks>
    public event EventHandler<RepoRefreshProgress>? ProgressChanged;

    /// <summary>
    ///     Gets the progress of the current wave of counted reads (or the last one, inactive, once it
    ///     ended).
    /// </summary>
    public RepoRefreshProgress Progress
    {
        get
        {
            lock (_lock)
            {
                return _progress;
            }
        }
    }

    /// <summary>
    ///     Queues a read, or joins the waiting read with the same key, and completes with its result.
    /// </summary>
    /// <remarks>
    ///     Returns at once: the read runs later, through the background runner, when a slot is free and
    ///     no waiting read has a higher priority. See the class remarks for how requests are coalesced
    ///     and how progress is counted.
    /// </remarks>
    /// <typeparam name="T">The read's result type; every request for a key must use the same
    ///     type.</typeparam>
    /// <param name="key">What is read about which repo. Its path must not be
    ///     <see langword="null"/>.</param>
    /// <param name="priority">How urgently the result is wanted.</param>
    /// <param name="read">The read; runs on a worker thread, must touch no view-model state, and gets a
    ///     token signaled when the queue is disposed (pass it to git so a running process is stopped).
    ///     Must not be <see langword="null"/>.</param>
    /// <param name="reportsProgress">Whether the read counts in the progress
    ///     (<see cref="ProgressChanged"/>).</param>
    /// <param name="cancellationToken">Gives up this request: its task is canceled, and a waiting read
    ///     every requester gave up on is removed. A running read is not interrupted.</param>
    /// <returns>A task that completes with the read's result; faults with the read's exception; is
    ///     canceled when this request was given up, the queue was disposed, or the read was stopped by
    ///     the disposal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="read"/> or the key's path is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a read with the same key but another
    ///     result type is waiting (a programming error).</exception>
    public Task<T> EnqueueAsync<T>(
        RepoRefreshKey key,
        RepoRefreshPriority priority,
        Func<CancellationToken, T> read,
        bool reportsProgress = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(key.RepoPath);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        QueuedRead<T> queued;
        lock (_lock)
        {
            if (_disposed)
            {
                return Task.FromCanceled<T>(new CancellationToken(canceled: true));
            }

            if (_waitingByKey.TryGetValue(key, out var existing))
            {
                // Join the waiting read: same result for both requesters, the newest delegate, the
                // higher priority, and counted when either request asks for it
                queued = existing as QueuedRead<T> ?? throw new InvalidOperationException(
                    $"A read of '{key.Kind}' for '{key.RepoPath}' with another result type is already waiting.");
                queued.Read = read;
                queued.Priority = (RepoRefreshPriority)Math.Max((int)queued.Priority, (int)priority);
                queued.Requesters++;
                if (reportsProgress && !queued.ReportsProgress)
                {
                    queued.ReportsProgress = true;
                    CountAcceptedRead();
                }
            }
            else
            {
                queued = new QueuedRead<T>(key, priority, _nextSequence++, read) { Requesters = 1 };
                _waiting.Add(queued);
                _waitingByKey.Add(key, queued);
                if (reportsProgress)
                {
                    queued.ReportsProgress = true;
                    CountAcceptedRead();
                }
            }
        }

        RaiseProgressChanged();
        StartReads();

        // A requester that cannot give up shares the read's own task
        return cancellationToken.CanBeCanceled
            ? AwaitCancelableRequestAsync(queued, cancellationToken)
            : queued.Completion.Task;
    }

    /// <summary>
    ///     Raises the priority of the waiting read with the given key, so it starts before reads of a
    ///     lower priority.
    /// </summary>
    /// <remarks>
    ///     A read that is already running, or none at all, is left alone; a lower priority than the
    ///     read already has changes nothing.
    /// </remarks>
    /// <param name="key">The read to promote.</param>
    /// <param name="priority">The priority it should have at least.</param>
    /// <returns><see langword="true"/> when a read with the key was waiting.</returns>
    public bool Promote(RepoRefreshKey key, RepoRefreshPriority priority)
    {
        lock (_lock)
        {
            if (!_waitingByKey.TryGetValue(key, out var queued))
            {
                return false;
            }

            if (priority > queued.Priority)
            {
                queued.Priority = priority;
            }

            return true;
        }
    }

    /// <summary>
    ///     Gets a task that completes once no read waits or runs; for tests and callers that must let
    ///     the reads settle.
    /// </summary>
    /// <returns>The task; already complete when the queue is idle.</returns>
    public Task WhenIdleAsync()
    {
        lock (_lock)
        {
            if (_waiting.Count == 0 && _running == 0)
            {
                return Task.CompletedTask;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _idle.Task;
        }
    }

    /// <summary>
    ///     Shuts the queue down: signals the running reads' token (stopping their git processes),
    ///     cancels every waiting read, ends the progress wave and refuses later requests. Called when the
    ///     main window closes; calling it again does nothing.
    /// </summary>
    public void Dispose()
    {
        List<QueuedRead> canceled;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            canceled = [.. _waiting];
            _waiting.Clear();
            _waitingByKey.Clear();
            var countedCanceled = canceled.Count(queued => queued.ReportsProgress);
            _countedPending -= countedCanceled;
            _progress = _progress with { Total = _progress.Total - countedCanceled, IsActive = false };
            _progressVersion++;
            SignalIdleIfIdle();
        }

        _shutdown.Cancel();
        foreach (var queued in canceled)
        {
            queued.Cancel(_shutdownToken);
        }

        _shutdown.Dispose();

        RaiseProgressChanged();
    }

    /// <summary>
    ///     Counts a newly accepted counted read in the wave, starting a new wave when none is pending.
    ///     Must be called under <see cref="_lock"/>.
    /// </summary>
    private void CountAcceptedRead()
    {
        if (_countedPending == 0)
        {
            _progress = new RepoRefreshProgress(0, 0, true);
        }

        _countedPending++;
        _progress = _progress with { Total = _progress.Total + 1, IsActive = true };
        _progressVersion++;
    }

    /// <summary>
    ///     Removes a counted read that ended (finished, or given up before it started) from what is
    ///     pending, and ends the wave when it was the last. Must be called under <see cref="_lock"/>.
    /// </summary>
    /// <param name="finished">Whether the read finished (counted as completed) rather than being
    ///     given up (no longer counted at all).</param>
    private void RemoveCountedRead(bool finished)
    {
        _countedPending--;
        _progress = finished
            ? _progress with { Completed = _progress.Completed + 1 }
            : _progress with { Total = _progress.Total - 1 };
        _progress = _progress with { IsActive = !_disposed && _countedPending > 0 };
        _progressVersion++;
    }

    /// <summary>
    ///     Completes the idle task when nothing waits or runs. Must be called under
    ///     <see cref="_lock"/>.
    /// </summary>
    private void SignalIdleIfIdle()
    {
        if (_waiting.Count == 0 && _running == 0 && _idle is not null)
        {
            _idle.TrySetResult();
            _idle = null;
        }
    }

    /// <summary>
    ///     Starts waiting reads, highest priority first, while slots are free.
    /// </summary>
    private void StartReads()
    {
        var starting = new List<QueuedRead>();
        lock (_lock)
        {
            while (!_disposed && _running < _maxConcurrentReads && _waiting.Count > 0)
            {
                // The highest priority wins; the earliest queued among equals
                var next = _waiting[0];
                foreach (var queued in _waiting.Where(queued =>
                             queued.Priority > next.Priority ||
                             (queued.Priority == next.Priority && queued.Sequence < next.Sequence)))
                {
                    next = queued;
                }

                _waiting.Remove(next);
                _waitingByKey.Remove(next.Key);
                _running++;
                starting.Add(next);
            }
        }

        // Started outside the lock: the runner may run a little synchronously
        foreach (var queued in starting)
        {
            _ = RunReadAsync(queued);
        }
    }

    /// <summary>
    ///     Runs one read, then updates the bookkeeping and the progress before it hands the result to
    ///     the requesters (so a requester that resumes sees the progress already updated), and starts
    ///     the next read.
    /// </summary>
    /// <param name="queued">The read.</param>
    /// <returns>A task that completes when the read was handed over; never faults.</returns>
    private async Task RunReadAsync(QueuedRead queued)
    {
        var complete = await queued.ExecuteAsync(_backgroundWork, _shutdownToken).ConfigureAwait(false);
        lock (_lock)
        {
            _running--;
            if (queued.ReportsProgress)
            {
                RemoveCountedRead(finished: true);
            }

            SignalIdleIfIdle();
        }

        RaiseProgressChanged();
        complete();
        StartReads();
    }

    /// <summary>
    ///     Waits for a read on behalf of one requester that may give up, giving up when its token is
    ///     signaled.
    /// </summary>
    /// <typeparam name="T">The read's result type.</typeparam>
    /// <param name="queued">The read.</param>
    /// <param name="cancellationToken">The requester's token.</param>
    /// <returns>The read's result.</returns>
    private async Task<T> AwaitCancelableRequestAsync<T>(QueuedRead<T> queued, CancellationToken cancellationToken)
    {
        try
        {
            return await queued.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A requester that gives up leaves the read; the last one to leave a waiting read removes
            // it. Done before this request's task completes, so its requester sees the queue updated.
            OnRequesterGaveUp(queued);
            throw;
        }
    }

    /// <summary>
    ///     Records that one requester of a read gave up, and removes the read when it was still waiting
    ///     and no requester is left.
    /// </summary>
    /// <param name="queued">The read.</param>
    private void OnRequesterGaveUp(QueuedRead queued)
    {
        lock (_lock)
        {
            queued.Requesters--;
            if (queued.Requesters > 0 || !_waitingByKey.TryGetValue(queued.Key, out var waiting) ||
                !ReferenceEquals(waiting, queued))
            {
                return;
            }

            _waiting.Remove(queued);
            _waitingByKey.Remove(queued.Key);
            if (queued.ReportsProgress)
            {
                RemoveCountedRead(finished: false);
            }

            SignalIdleIfIdle();
        }

        queued.Cancel(CancellationToken.None);
        RaiseProgressChanged();
    }

    /// <summary>
    ///     Reports the current progress through <see cref="ProgressChanged"/>: posted to the captured
    ///     context, or raised here when there is none (skipping a value already raised or older).
    /// </summary>
    private void RaiseProgressChanged()
    {
        if (_progressContext is not null)
        {
            // Posted under the lock, so the posts are in the order of the changes; each post carries
            // the value of its own change
            lock (_lock)
            {
                if (_progressVersion == _raisedProgressVersion)
                {
                    return;
                }

                _raisedProgressVersion = _progressVersion;
                var progress = _progress;
                _progressContext.Post(_ => ProgressChanged?.Invoke(this, progress), null);
            }

            return;
        }

        // No context: raised one at a time, always with the newest value, never twice for one change
        lock (_eventLock)
        {
            RepoRefreshProgress progress;
            lock (_lock)
            {
                if (_progressVersion == _raisedProgressVersion)
                {
                    return;
                }

                _raisedProgressVersion = _progressVersion;
                progress = _progress;
            }

            ProgressChanged?.Invoke(this, progress);
        }
    }

    /// <summary>
    ///     A read in the queue, independent of its result type.
    /// </summary>
    /// <param name="key">The read's key.</param>
    /// <param name="priority">Its initial priority.</param>
    /// <param name="sequence">Its order number.</param>
    private abstract class QueuedRead(RepoRefreshKey key, RepoRefreshPriority priority, long sequence)
    {
        /// <summary>
        ///     Gets the read's key.
        /// </summary>
        public RepoRefreshKey Key { get; } = key;

        /// <summary>
        ///     Gets the order the read was queued in.
        /// </summary>
        public long Sequence { get; } = sequence;

        /// <summary>
        ///     Gets or sets the read's priority; raised by joins and promotions while it waits.
        /// </summary>
        public RepoRefreshPriority Priority { get; set; } = priority;

        /// <summary>
        ///     Gets or sets a value indicating whether the read counts in the progress.
        /// </summary>
        public bool ReportsProgress { get; set; }

        /// <summary>
        ///     Gets or sets how many requesters still wait for the read.
        /// </summary>
        public int Requesters { get; set; }

        /// <summary>
        ///     Runs the read through the runner and returns what hands its outcome to the requesters.
        /// </summary>
        /// <param name="runner">Runs the read off the calling thread.</param>
        /// <param name="shutdownToken">Signaled when the queue is disposed.</param>
        /// <returns>The action that completes the requesters' task; never faults.</returns>
        public abstract Task<Action> ExecuteAsync(IBackgroundWorkRunner runner, CancellationToken shutdownToken);

        /// <summary>
        ///     Cancels the requesters' task of a read that will not run.
        /// </summary>
        /// <param name="token">The token to report.</param>
        public abstract void Cancel(CancellationToken token);
    }

    /// <summary>
    ///     A read in the queue with its result type.
    /// </summary>
    /// <typeparam name="T">The read's result type.</typeparam>
    /// <param name="key">The read's key.</param>
    /// <param name="priority">Its initial priority.</param>
    /// <param name="sequence">Its order number.</param>
    /// <param name="read">The read delegate.</param>
    private sealed class QueuedRead<T>(
        RepoRefreshKey key, RepoRefreshPriority priority, long sequence, Func<CancellationToken, T> read)
        : QueuedRead(key, priority, sequence)
    {
        /// <summary>
        ///     Gets the requesters' shared task source; continuations never run inside the queue.
        /// </summary>
        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Gets or sets the read delegate; the newest joining request's replaces the older one.
        /// </summary>
        public Func<CancellationToken, T> Read { get; set; } = read;

        /// <inheritdoc />
        public override async Task<Action> ExecuteAsync(IBackgroundWorkRunner runner, CancellationToken shutdownToken)
        {
            var current = Read;
            try
            {
                var result = await runner.RunAsync(() => current(shutdownToken), CancellationToken.None).ConfigureAwait(false);

                // A read that ended after the queue shut down is not delivered: the window is gone
                return shutdownToken.IsCancellationRequested
                    ? () => Completion.TrySetCanceled(shutdownToken)
                    : () => Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                return () => Completion.TrySetCanceled(shutdownToken);
            }
            catch (Exception ex)
            {
                // Any other failure belongs to the requesters, who decide what it means
                return () => Completion.TrySetException(ex);
            }
        }

        /// <inheritdoc />
        public override void Cancel(CancellationToken token) => Completion.TrySetCanceled(token);
    }
}
