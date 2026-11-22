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

using System.Runtime.ExceptionServices;

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Writes settings snapshots to disk in the background, one at a time, so that a change the user
///     makes in the window never waits for the settings file.
/// </summary>
/// <remarks>
///     <para>
///     Saving the settings file can take a while: <see cref="SettingsStore.Save"/> retries for up to
///     about a second when another process (a virus scanner, an indexer, a cloud-sync client) holds
///     the file. A window that saved on the UI thread froze for that long on every click. This writer
///     takes the saves off the caller: the caller applies a change in memory at once and hands over a
///     snapshot of the settings (see <see cref="AppSettings.CreateSnapshot"/>), never the live object,
///     so it can keep changing its settings while a snapshot is written.
///     </para>
///     <para>
///     Rules:
///     </para>
///     <list type="bullet">
///         <item><b>Debounced, latest wins.</b> <see cref="Schedule"/> replaces the snapshot that is
///         waiting and writes it once no other snapshot was scheduled for the debounce delay; a burst
///         of changes is written once, with the newest state.</item>
///         <item><b>Serialized.</b> Writes never overlap: a write starts only after the one before has
///         ended, whether scheduled, confirmed (<see cref="SaveNowAsync"/>) or flushed.</item>
///         <item><b>Off the caller's thread.</b> Every write runs on a thread-pool thread, including
///         the ones a flush makes, so the save never runs on the UI thread.</item>
///         <item><b>Failures.</b> A scheduled write that fails is reported through
///         <see cref="WriteFailed"/> once per failure streak (the first failure after a write that
///         succeeded); its snapshot stays waiting, without a timer, and is written with the next
///         change, the next flush or a confirmed save.</item>
///         <item><b>Confirmed saves.</b> <see cref="SaveNowAsync"/> writes a snapshot after the write
///         in progress and tells the caller whether it was written. When it was, a snapshot
///         scheduled before the call is dropped (the confirmed one was built later, from settings
///         that already held it); when it failed, the waiting snapshot stays scheduled, so a change
///         made meanwhile is not lost.</item>
///     </list>
///     <para>
///     Thread-safe: every member may be called from any thread. <see cref="WriteFailed"/> is raised
///     on the <see cref="SynchronizationContext"/> that was current when the writer was created (the
///     UI thread in the application), or on the writing thread when there was none.
///     </para>
/// </remarks>
internal sealed class SettingsWriter : IDisposable
{
    /// <summary>
    ///     How long a scheduled snapshot waits for a newer one before it is written, when the caller
    ///     gives no delay.
    /// </summary>
    /// <remarks>
    ///     Long enough to fold a burst of clicks or a drag (several changes in quick succession) into
    ///     one write, short enough that a change is on disk well before the user could close the
    ///     window and expect it kept; the close writes what is left anyway.
    /// </remarks>
    internal static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    ///     Writes one snapshot to disk; throws when it could not be written.
    /// </summary>
    private readonly Action<AppSettings> _save;

    /// <summary>
    ///     How long a scheduled snapshot waits before it is written.
    /// </summary>
    private readonly TimeSpan _delay;

    /// <summary>
    ///     The context <see cref="WriteFailed"/> is posted to, or <see langword="null"/> to raise it on
    ///     the writing thread.
    /// </summary>
    private readonly SynchronizationContext? _context;

    /// <summary>
    ///     The debounce timer: started (or restarted) by <see cref="Schedule"/>, it writes the waiting
    ///     snapshot when it fires.
    /// </summary>
    private readonly ITimer _timer;

    /// <summary>
    ///     The turn to write: one write at a time. Never disposed, because a write that is still
    ///     running when the writer is disposed releases it.
    /// </summary>
    private readonly SemaphoreSlim _writeTurn = new(1, 1);

    /// <summary>
    ///     Guards every field below.
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    ///     The snapshot waiting to be written, or <see langword="null"/> when none is.
    /// </summary>
    private AppSettings? _waiting;

    /// <summary>
    ///     The number <see cref="Schedule"/> gave <see cref="_waiting"/>, so a confirmed save can tell
    ///     whether the waiting snapshot is older than its own.
    /// </summary>
    private long _waitingVersion;

    /// <summary>
    ///     The number of the most recent <see cref="Schedule"/> call; zero before the first.
    /// </summary>
    private long _version;

    /// <summary>
    ///     Whether a write is running.
    /// </summary>
    private bool _writing;

    /// <summary>
    ///     Whether the current failure streak was reported already; cleared by any write that
    ///     succeeds.
    /// </summary>
    private bool _failureReported;

    /// <summary>
    ///     Whether <see cref="Dispose"/> was called, after which nothing more is scheduled.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Initializes a writer.
    /// </summary>
    /// <remarks>
    ///     Captures the current <see cref="SynchronizationContext"/> for <see cref="WriteFailed"/>, so
    ///     create the writer on the thread that shows the failure (the UI thread in the
    ///     application).
    /// </remarks>
    /// <param name="save">Writes one snapshot to disk; it must throw (normally an
    ///     <see cref="InvalidOperationException"/>) when the snapshot could not be written. It is
    ///     called on a thread-pool thread, never two at a time. Must not be
    ///     <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock behind the debounce delay, or <see langword="null"/> for
    ///     the system clock. Supplied so tests can move time without waiting.</param>
    /// <param name="delay">How long a scheduled snapshot waits for a newer one, or
    ///     <see langword="null"/> for <see cref="DefaultDelay"/>; zero writes at once. Must not be
    ///     negative.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="save"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="delay"/> is
    ///     negative.</exception>
    public SettingsWriter(Action<AppSettings> save, TimeProvider? timeProvider = null, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay ?? DefaultDelay, TimeSpan.Zero);

        _save = save;
        _delay = delay ?? DefaultDelay;
        _context = SynchronizationContext.Current;
        _timer = (timeProvider ?? TimeProvider.System).CreateTimer(
            OnTimerFired, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    ///     Raised when a scheduled (or flushed) write fails after the write before it succeeded: once
    ///     per failure streak, not once per failed write, so a disk that keeps failing does not
    ///     interrupt the user on every change. The argument is the failure; its message is not
    ///     redacted.
    /// </summary>
    /// <remarks>
    ///     Not raised for a confirmed save (<see cref="SaveNowAsync"/>), whose caller is told through
    ///     the returned task. The snapshot that failed stays waiting and is written with the next
    ///     change, flush or confirmed save.
    /// </remarks>
    public event EventHandler<Exception>? WriteFailed;

    /// <summary>
    ///     Gets a value indicating whether something accepted is not on disk yet: a snapshot is
    ///     waiting (scheduled, or kept after a failed write) or a write is running.
    /// </summary>
    public bool HasUnsavedChanges
    {
        get
        {
            lock (_lock)
            {
                return _waiting is not null || _writing;
            }
        }
    }

    /// <summary>
    ///     Schedules a snapshot to be written in the background; returns at once.
    /// </summary>
    /// <remarks>
    ///     The snapshot replaces the one that is waiting (latest wins) and is written once no newer
    ///     one was scheduled for the debounce delay, after any write in progress. Ignored after
    ///     <see cref="Dispose"/>.
    /// </remarks>
    /// <param name="snapshot">The settings to write: a copy that nobody changes any more (see
    ///     <see cref="AppSettings.CreateSnapshot"/>). Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is
    ///     <see langword="null"/>.</exception>
    public void Schedule(AppSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _waiting = snapshot;
            _waitingVersion = ++_version;

            // Trailing debounce: every new snapshot restarts the wait
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    ///     Writes a snapshot after the write in progress and tells the caller whether it was written;
    ///     for a change the caller must not apply unless it is on disk (an add, the Settings window's
    ///     Save).
    /// </summary>
    /// <remarks>
    ///     The write runs on a thread-pool thread; the caller's thread does not wait. When it
    ///     succeeds, a snapshot that was scheduled before this call and is still waiting is dropped,
    ///     because the confirmed snapshot was built from settings that already held it; one
    ///     scheduled after this call stays. When it fails, the waiting snapshot stays scheduled
    ///     (a change made meanwhile is still written), and <see cref="WriteFailed"/> is not raised.
    /// </remarks>
    /// <param name="snapshot">The settings to write: a copy that nobody changes any more. Must not
    ///     be <see langword="null"/>.</param>
    /// <returns>A task that completes when the snapshot was written, or faults with the exception
    ///     the save threw (normally an <see cref="InvalidOperationException"/>) when it was not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the writer was disposed.</exception>
    public async Task SaveNowAsync(AppSettings snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // The newest schedule at the time of the call: anything up to it is older than the snapshot
        long version;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            version = _version;
        }

        await _writeTurn.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_lock)
            {
                _writing = true;
            }

            var failure = await WriteAsync(snapshot).ConfigureAwait(false);
            if (failure is not null)
            {
                lock (_lock)
                {
                    _writing = false;
                }

                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            // Written: an older waiting snapshot holds nothing the file does not have now
            lock (_lock)
            {
                _writing = false;
                if (_waiting is not null && _waitingVersion <= version)
                {
                    _waiting = null;
                    _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
            }
        }
        finally
        {
            _writeTurn.Release();
        }
    }

    /// <summary>
    ///     Writes the waiting snapshot now, without waiting for the debounce delay, after the write in
    ///     progress.
    /// </summary>
    /// <remarks>
    ///     Writes a snapshot kept after a failed write as well (a failure is reported only when it
    ///     starts a new streak). The write runs on a thread-pool thread.
    /// </remarks>
    /// <returns>A task that completes, never faulting, when every write accepted before the call has
    ///     ended: <see langword="true"/> when the waiting snapshot (if any) was written,
    ///     <see langword="false"/> when its write failed.</returns>
    public async Task<bool> FlushAsync()
    {
        lock (_lock)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        return await WriteWaitingAsync().ConfigureAwait(false);
    }


    /// <summary>
    ///     Stops the debounce timer and ignores later <see cref="Schedule"/> calls; a write in
    ///     progress completes. Write what is waiting first (a flush or a confirmed save).
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
    }

    /// <summary>
    ///     Starts the write of the waiting snapshot when the debounce delay has passed; the write
    ///     never faults, so it is not awaited.
    /// </summary>
    /// <param name="state">Unused timer state.</param>
    private void OnTimerFired(object? state)
    {
        _ = WriteWaitingAsync();
    }

    /// <summary>
    ///     Writes the waiting snapshot, if any, in its turn, reporting a failure that starts a streak;
    ///     the body of every scheduled and flushed write.
    /// </summary>
    /// <returns><see langword="true"/> when nothing was waiting or it was written;
    ///     <see langword="false"/> when the write failed. Never faults.</returns>
    private async Task<bool> WriteWaitingAsync()
    {
        await _writeTurn.WaitAsync().ConfigureAwait(false);
        try
        {
            // Take the newest snapshot; one scheduled from now on waits for its own write
            AppSettings? snapshot;
            long version;
            lock (_lock)
            {
                snapshot = _waiting;
                version = _waitingVersion;
                _waiting = null;
                _writing = snapshot is not null;
            }

            if (snapshot is null)
            {
                return true;
            }

            var failure = await WriteAsync(snapshot).ConfigureAwait(false);
            if (failure is null)
            {
                lock (_lock)
                {
                    _writing = false;
                }

                return true;
            }

            // Failed: keep the snapshot for the next change, flush or confirmed save (unless a newer
            // one was scheduled meanwhile), and report it unless this streak was reported already
            bool report;
            lock (_lock)
            {
                _writing = false;
                if (_waiting is null)
                {
                    _waiting = snapshot;
                    _waitingVersion = version;
                }

                report = !_failureReported;
                _failureReported = true;
            }

            if (report)
            {
                ReportFailure(failure);
            }

            return false;
        }
        finally
        {
            _writeTurn.Release();
        }
    }

    /// <summary>
    ///     Runs the save of one snapshot on a thread-pool thread; called only while holding the turn,
    ///     with <see cref="_writing"/> set by the caller (in the same lock that took the snapshot, so
    ///     <see cref="HasUnsavedChanges"/> never misses it), which also clears it.
    /// </summary>
    /// <param name="snapshot">The snapshot to write.</param>
    /// <returns>A task with <see langword="null"/> when the snapshot was written, otherwise the
    ///     exception the save threw. Never faults.</returns>
    private async Task<Exception?> WriteAsync(AppSettings snapshot)
    {
        try
        {
            await Task.Run(() => _save(snapshot)).ConfigureAwait(false);

            // Any write that succeeds ends a failure streak
            lock (_lock)
            {
                _failureReported = false;
            }

            return null;
        }
        catch (Exception ex)
        {
            // Whatever the save threw, the snapshot is not on disk; the callers decide what to tell
            return ex;
        }
    }

    /// <summary>
    ///     Raises <see cref="WriteFailed"/> on the captured context, or on this thread when there is
    ///     none.
    /// </summary>
    /// <param name="failure">The failure to report.</param>
    private void ReportFailure(Exception failure)
    {
        if (_context is null)
        {
            WriteFailed?.Invoke(this, failure);
            return;
        }

        _context.Post(_ => WriteFailed?.Invoke(this, failure), null);
    }
}
