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

namespace DemaConsulting.AgentControl.Utilities;

/// <summary>
///     Refuses a blocking call (a git process, a disk read or write) made on a thread that must never
///     wait for one, such as the window's UI thread, so the tests catch such a call the moment it is
///     added.
/// </summary>
/// <remarks>
///     <para>
///     The window must never wait for the disk or a process: every such call belongs on a worker
///     thread. A call made on the UI thread by mistake still works - it only freezes the window for a
///     while - so nothing would notice it. This guard makes it visible: the code that makes a blocking
///     call asks <see cref="ThrowIfGuarded"/> first, and a thread a test has marked as guarded gets a
///     <see cref="BlockingCallOnGuardedThreadException"/> instead of the call.
///     </para>
///     <para>
///     Nothing is guarded unless a test asks for it, so the shipped application is unaffected: an
///     unguarded thread passes <see cref="ThrowIfGuarded"/> at the cost of reading two thread-local
///     fields. A test guards the synchronous part of a gesture with <see cref="GuardCurrentThread"/>,
///     or a whole thread it owns (the shared headless UI thread) with
///     <see cref="MarkCurrentThreadGuarded"/>. The guard and the record of refused calls are per
///     thread (<see cref="ThreadStaticAttribute"/>), so tests running in parallel on other threads
///     never see each other's guard or refusals. Every refusal is also recorded, because a broad
///     <c>catch</c> in the code under test could hide the exception; <see cref="TakeViolations"/>
///     drains the record. Every member acts on the calling thread only and is safe to call from any
///     thread.
///     </para>
/// </remarks>
internal static class BlockingCallGuard
{
    /// <summary>
    ///     How many <see cref="GuardCurrentThread"/> scopes are open on this thread.
    /// </summary>
    [ThreadStatic]
    private static int t_guardDepth;

    /// <summary>
    ///     Whether <see cref="MarkCurrentThreadGuarded"/> guarded this thread for its lifetime.
    /// </summary>
    [ThreadStatic]
    private static bool t_guardedForLifetime;

    /// <summary>
    ///     The blocking calls refused on this thread and not yet taken by <see cref="TakeViolations"/>,
    ///     or <see langword="null"/> when there are none.
    /// </summary>
    [ThreadStatic]
    private static List<string>? t_violations;

    /// <summary>
    ///     Gets a value indicating whether the calling thread is guarded: a blocking call made on it is
    ///     refused.
    /// </summary>
    internal static bool IsCurrentThreadGuarded => t_guardedForLifetime || t_guardDepth > 0;

    /// <summary>
    ///     Guards the calling thread until the returned scope is disposed.
    /// </summary>
    /// <remarks>
    ///     Scopes nest: the thread stays guarded until the outermost one is disposed. The scope must be
    ///     disposed on the thread that opened it (a <c>using</c> around synchronous code); disposing it
    ///     on another thread, or a second time, does nothing.
    /// </remarks>
    /// <returns>The scope; dispose it to end the guard.</returns>
    internal static IDisposable GuardCurrentThread()
    {
        t_guardDepth++;
        return new GuardScope(Environment.CurrentManagedThreadId);
    }

    /// <summary>
    ///     Guards the calling thread for the rest of its lifetime.
    /// </summary>
    /// <remarks>
    ///     For a thread a test harness owns and keeps, such as the single UI thread of the headless
    ///     test session: every test that runs on it is then checked. Never call it on a thread-pool
    ///     thread, which other code reuses.
    /// </remarks>
    internal static void MarkCurrentThreadGuarded() => t_guardedForLifetime = true;

    /// <summary>
    ///     Refuses a blocking call on a guarded thread; does nothing on any other thread.
    /// </summary>
    /// <remarks>
    ///     Called by the code that is about to make the blocking call (start a git process, read or
    ///     write a file), before it does anything. The refusal is recorded (see
    ///     <see cref="TakeViolations"/>) before it is thrown.
    /// </remarks>
    /// <param name="operation">A short description of the call, for the message and the record (for
    ///     example <c>git status</c>). Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when the calling thread is
    ///     guarded; the call must then not be made.</exception>
    internal static void ThrowIfGuarded(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!IsCurrentThreadGuarded)
        {
            return;
        }

        // Recorded first, so a catch-all in the caller cannot hide the refusal from the test
        (t_violations ??= []).Add(operation);
        throw new BlockingCallOnGuardedThreadException(
            $"The blocking call '{operation}' was made on a thread that must not wait for the disk or a process.");
    }

    /// <summary>
    ///     Returns the blocking calls refused on the calling thread since the last call, and clears
    ///     that record.
    /// </summary>
    /// <returns>The refused calls' descriptions in the order they were refused; empty when there were
    ///     none.</returns>
    internal static IReadOnlyList<string> TakeViolations()
    {
        var violations = t_violations;
        t_violations = null;
        return violations ?? [];
    }


    /// <summary>
    ///     The scope <see cref="GuardCurrentThread"/> returns: ends one level of guarding when disposed
    ///     on the thread that opened it.
    /// </summary>
    /// <param name="threadId">The managed id of the thread that opened the scope.</param>
    private sealed class GuardScope(int threadId) : IDisposable
    {
        /// <summary>
        ///     Whether the scope was disposed already.
        /// </summary>
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            // Only the opening thread's guard can be ended, and only once
            if (_disposed || Environment.CurrentManagedThreadId != threadId)
            {
                return;
            }

            _disposed = true;
            EndGuard();
        }

        /// <summary>
        ///     Ends one level of guarding of the calling thread; the counterpart of
        ///     <see cref="GuardCurrentThread"/>.
        /// </summary>
        private static void EndGuard() => t_guardDepth--;
    }
}

/// <summary>
///     Thrown by <see cref="BlockingCallGuard.ThrowIfGuarded"/> when a blocking call is made on a
///     thread that must never wait for the disk or a process.
/// </summary>
/// <remarks>
///     Deliberately not an <see cref="InvalidOperationException"/>: the code that makes git and file
///     calls catches that type to report ordinary failures (git missing, a file that cannot be read),
///     and must not treat a programming error as one of them.
/// </remarks>
public sealed class BlockingCallOnGuardedThreadException : Exception
{
    /// <summary>
    ///     Initializes the exception with a default message.
    /// </summary>
    public BlockingCallOnGuardedThreadException()
        : base("A blocking call was made on a thread that must not wait for the disk or a process.")
    {
    }

    /// <summary>
    ///     Initializes the exception with a message.
    /// </summary>
    /// <param name="message">What was called, and why it was refused.</param>
    public BlockingCallOnGuardedThreadException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes the exception with a message and the exception that caused it.
    /// </summary>
    /// <param name="message">What was called, and why it was refused.</param>
    /// <param name="innerException">The cause.</param>
    public BlockingCallOnGuardedThreadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
