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

using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     A test <see cref="IBackgroundWorkRunner"/> that holds every piece of background work at a gate
///     until the test releases it, so a test can prove the caller (and the UI thread) stays free while
///     the work is outstanding, without any timing assumption.
/// </summary>
/// <remarks>
///     While the gate is closed, <see cref="RunAsync{T}"/> returns a task that has not completed; once
///     <see cref="Release"/> is called the held work, and any work started afterwards, runs on the
///     thread pool. The work runs even when its token is canceled after it was handed over (as work
///     that already started does), which lets tests cancel while work is held. Safe for concurrent use.
/// </remarks>
internal sealed class GatedBackgroundWorkRunner : IBackgroundWorkRunner
{
    /// <summary>
    ///     Opens when <see cref="Release"/> is called; replaced by a closed one by <see cref="Close"/>.
    /// </summary>
    private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Completes the first time a piece of work reaches the gate (since the last
    ///     <see cref="Close"/>).
    /// </summary>
    private TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     How many pieces of work were handed over.
    /// </summary>
    private int _calls;

    /// <summary>
    ///     How many pieces of work were handed over and have not finished (held or running).
    /// </summary>
    private int _inFlight;

    /// <summary>
    ///     The most pieces of work that were handed over and unfinished at the same time.
    /// </summary>
    private int _maxInFlight;

    /// <summary>
    ///     Gets how many pieces of work were handed over so far.
    /// </summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>
    ///     Gets the most pieces of work that were handed over and unfinished (held at the gate or
    ///     running) at the same time - how many reads the caller ran at once.
    /// </summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    /// <summary>
    ///     Gets a task that completes once the first piece of work has reached the gate (since the last
    ///     <see cref="Close"/>).
    /// </summary>
    public Task FirstCall => Volatile.Read(ref _firstCall).Task;

    /// <summary>
    ///     Opens the gate: held work, and every later piece of work, runs.
    /// </summary>
    public void Release() => Volatile.Read(ref _gate).TrySetResult();

    /// <summary>
    ///     Closes the gate again, so work handed over from now on is held until the next
    ///     <see cref="Release"/> (work released earlier is not affected), and <see cref="FirstCall"/>
    ///     waits for the next piece. Lets a test let one phase run (for example the window's startup
    ///     reads) and hold the next one. <see cref="Calls"/> and <see cref="MaxInFlight"/> keep counting.
    /// </summary>
    public void Close()
    {
        Volatile.Write(ref _firstCall, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        Volatile.Write(ref _gate, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    /// <inheritdoc />
    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Counted and announced at once, then held until the test opens the gate
        Interlocked.Increment(ref _calls);
        var inFlight = Interlocked.Increment(ref _inFlight);
        UpdateMaxInFlight(inFlight);
        var gate = Volatile.Read(ref _gate);
        Volatile.Read(ref _firstCall).TrySetResult();
        try
        {
            await gate.Task.ConfigureAwait(false);
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    /// <summary>
    ///     Raises the recorded maximum to <paramref name="inFlight"/> when it is higher.
    /// </summary>
    /// <param name="inFlight">How many pieces of work are unfinished now.</param>
    private void UpdateMaxInFlight(int inFlight)
    {
        var max = Volatile.Read(ref _maxInFlight);
        while (inFlight > max)
        {
            var seen = Interlocked.CompareExchange(ref _maxInFlight, inFlight, max);
            if (seen == max)
            {
                return;
            }

            max = seen;
        }
    }
}

/// <summary>
///     A test <see cref="IBackgroundWorkRunner"/> that holds every piece of background work until the
///     test releases it, one piece at a time in the order the pieces arrived, so a test can tell which
///     read the shared refresh queue started next (a queue with one slot hands over one read at a
///     time).
/// </summary>
/// <remarks>
///     <see cref="WaitForCallsAsync"/> waits until a given number of pieces were handed over;
///     <see cref="ReleaseNext"/> lets the oldest held piece run; <see cref="ReleaseAll"/> lets every held
///     piece, and every later one, run. Released work runs on the thread pool. Safe for concurrent use.
/// </remarks>
internal sealed class SteppedBackgroundWorkRunner : IBackgroundWorkRunner
{
    /// <summary>
    ///     Guards every field below.
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    ///     The gates of the pieces that are held, oldest first.
    /// </summary>
    private readonly Queue<TaskCompletionSource> _held = new();

    /// <summary>
    ///     The tests waiting for a number of pieces to have been handed over.
    /// </summary>
    private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];

    /// <summary>
    ///     How many pieces were handed over.
    /// </summary>
    private int _calls;

    /// <summary>
    ///     Whether <see cref="ReleaseAll"/> was called, after which nothing is held.
    /// </summary>
    private bool _allReleased;

    /// <summary>
    ///     Gets how many pieces of work were handed over so far.
    /// </summary>
    public int Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls;
            }
        }
    }

    /// <summary>
    ///     Waits until at least <paramref name="count"/> pieces of work were handed over.
    /// </summary>
    /// <param name="count">The number of pieces.</param>
    /// <returns>A task that completes once that many pieces arrived.</returns>
    public Task WaitForCallsAsync(int count)
    {
        lock (_lock)
        {
            if (_calls >= count)
            {
                return Task.CompletedTask;
            }

            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, reached));
            return reached.Task;
        }
    }

    /// <summary>
    ///     Lets the oldest held piece of work run.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no piece is held.</exception>
    public void ReleaseNext()
    {
        TaskCompletionSource gate;
        lock (_lock)
        {
            gate = _held.Count > 0
                ? _held.Dequeue()
                : throw new InvalidOperationException("No background work is held.");
        }

        gate.TrySetResult();
    }

    /// <summary>
    ///     Lets every held piece of work, and every later one, run.
    /// </summary>
    public void ReleaseAll()
    {
        List<TaskCompletionSource> gates;
        lock (_lock)
        {
            _allReleased = true;
            gates = [.. _held];
            _held.Clear();
        }

        foreach (var gate in gates)
        {
            gate.TrySetResult();
        }
    }

    /// <inheritdoc />
    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Counted and held (unless everything was released), announcing the count to waiting tests
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        List<TaskCompletionSource> reached;
        lock (_lock)
        {
            _calls++;
            if (_allReleased)
            {
                gate.TrySetResult();
            }
            else
            {
                _held.Enqueue(gate);
            }

            reached = [.. _waiters.Where(waiter => waiter.Count <= _calls).Select(waiter => waiter.Reached)];
            _waiters.RemoveAll(waiter => waiter.Count <= _calls);
        }

        foreach (var waiter in reached)
        {
            waiter.TrySetResult();
        }

        await gate.Task.ConfigureAwait(false);
        return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
    }
}
