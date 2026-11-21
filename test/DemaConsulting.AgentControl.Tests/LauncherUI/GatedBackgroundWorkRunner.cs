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
    ///     Opens when <see cref="Release"/> is called.
    /// </summary>
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Completes the first time a piece of work reaches the gate.
    /// </summary>
    private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    ///     Gets a task that completes once the first piece of work has reached the gate.
    /// </summary>
    public Task FirstCall => _firstCall.Task;

    /// <summary>
    ///     Opens the gate: held work, and every later piece of work, runs.
    /// </summary>
    public void Release() => _gate.TrySetResult();

    /// <inheritdoc />
    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Counted and announced at once, then held until the test opens the gate
        Interlocked.Increment(ref _calls);
        var inFlight = Interlocked.Increment(ref _inFlight);
        UpdateMaxInFlight(inFlight);
        _firstCall.TrySetResult();
        try
        {
            await _gate.Task.ConfigureAwait(false);
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
