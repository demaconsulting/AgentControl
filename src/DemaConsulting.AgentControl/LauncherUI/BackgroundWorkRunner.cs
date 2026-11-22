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
///     Runs blocking work (file-system reads, git processes) away from the UI thread and hands the
///     result back to the awaiting caller.
/// </summary>
/// <remarks>
///     The view models await <see cref="RunAsync{T}"/> from the UI thread, so the continuation that
///     applies the result resumes on the UI thread's synchronization context (Avalonia's dispatcher
///     in the application); the work itself only reads and returns an immutable result and never
///     touches view-model state. The seam exists so tests can hold the work at a gate and prove the
///     caller (and the UI thread) stays free while it runs, without any timing assumption.
///     Implementations must be safe for concurrent use.
/// </remarks>
internal interface IBackgroundWorkRunner
{
    /// <summary>
    ///     Runs <paramref name="work"/> off the calling thread and completes with its result.
    /// </summary>
    /// <typeparam name="T">The type of the work's result.</typeparam>
    /// <param name="work">The work; must not touch UI or view-model state. Must not be
    ///     <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the work before it starts; work that has started
    ///     runs to completion.</param>
    /// <returns>A task that completes with the work's result, faults with the work's exception, or
    ///     is canceled when <paramref name="cancellationToken"/> was signaled before the work
    ///     started.</returns>
    Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken);
}

/// <summary>
///     The application's <see cref="IBackgroundWorkRunner"/>: runs the work on the thread pool.
/// </summary>
/// <remarks>
///     Stateless and safe for concurrent use; a single shared <see cref="Instance"/> serves every
///     view model.
/// </remarks>
internal sealed class ThreadPoolBackgroundWorkRunner : IBackgroundWorkRunner
{
    /// <summary>
    ///     Gets the shared instance.
    /// </summary>
    public static ThreadPoolBackgroundWorkRunner Instance { get; } = new();

    /// <summary>
    ///     Prevents creating further instances; use <see cref="Instance"/>.
    /// </summary>
    private ThreadPoolBackgroundWorkRunner()
    {
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="work"/> is
    ///     <see langword="null"/>.</exception>
    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Task.Run(work, cancellationToken);
    }
}
