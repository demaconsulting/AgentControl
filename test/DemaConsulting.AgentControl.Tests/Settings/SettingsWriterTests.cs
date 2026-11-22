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

using DemaConsulting.AgentControl.Settings;
using DemaConsulting.AgentControl.Utilities;
using Microsoft.Extensions.Time.Testing;

namespace DemaConsulting.AgentControl.Tests.Settings;

/// <summary>
///     Unit tests for <see cref="SettingsWriter"/>.
/// </summary>
/// <remarks>
///     The debounce runs on a <see cref="FakeTimeProvider"/> that only moves when a test advances it,
///     and the save is a probe that records what it wrote, can hold every write at a gate and can fail,
///     so no test depends on timing. Each snapshot is told apart by its
///     <see cref="AppSettings.GitExecutablePath"/>. Every writer is created with no synchronization
///     context current, so a failure is reported on the writing thread before the write's turn ends.
/// </remarks>
public class SettingsWriterTests
{
    /// <summary>
    ///     The debounce delay every writer of these tests uses.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    ///     Test that several snapshots scheduled less than the delay apart are written once, with the
    ///     newest one, once the delay has passed after the last.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_Schedule_SeveralWithinDelay_WritesOnceWithLatest()
    {
        // Arrange
        var probe = new SaveProbe();
        using var writer = CreateWriter(probe, out var time);

        // Act: three snapshots, each before the previous one's delay ran out, then the full delay
        writer.Schedule(Snapshot("first"));
        time.Advance(Delay / 2);
        writer.Schedule(Snapshot("second"));
        time.Advance(Delay / 2);
        writer.Schedule(Snapshot("third"));
        time.Advance(Delay);
        await writer.FlushAsync();

        // Assert: one write, with the newest snapshot
        Assert.Equal(["third"], probe.Written);
        Assert.False(writer.HasUnsavedChanges);
    }

    /// <summary>
    ///     Test that scheduling never waits for a write: while a write is held, further snapshots are
    ///     accepted at once, a confirmed save and a scheduled write queue behind it without running at
    ///     the same time, and the newest scheduled snapshot is written last.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_Writes_NeverOverlapAndScheduleNeverWaits()
    {
        // Arrange: the first write is held at the gate
        var probe = new SaveProbe();
        using var writer = CreateWriter(probe, out var time);
        probe.Gate.Reset();
        writer.Schedule(Snapshot("held"));
        time.Advance(Delay);
        await probe.WaitForStartAsync();

        // Act: while it is held, a confirmed save and two scheduled snapshots
        var confirmed = writer.SaveNowAsync(Snapshot("confirmed"));
        writer.Schedule(Snapshot("older"));
        writer.Schedule(Snapshot("newest"));
        time.Advance(Delay);

        // Assert: still only the held write has started, and everything was accepted
        Assert.Equal(["held"], probe.Started);
        Assert.False(confirmed.IsCompleted);
        Assert.True(writer.HasUnsavedChanges);

        // Act: let the writes run
        probe.Gate.Set();
        await confirmed;
        await writer.FlushAsync();

        // Assert: one at a time, in order, the newest scheduled snapshot last
        Assert.Equal(["held", "confirmed", "newest"], probe.Written);
        Assert.Equal(1, probe.MaxRunning);
        Assert.False(writer.HasUnsavedChanges);
    }

    /// <summary>
    ///     Test that a confirmed save completes once its snapshot was written.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_SaveNowAsync_Success_CompletesWhenWritten()
    {
        // Arrange
        var probe = new SaveProbe();
        using var writer = CreateWriter(probe, out _);

        // Act
        await writer.SaveNowAsync(Snapshot("confirmed"));

        // Assert
        Assert.Equal(["confirmed"], probe.Written);
        Assert.False(writer.HasUnsavedChanges);
    }

    /// <summary>
    ///     Test that a confirmed save that cannot be written faults with the reason, and is not
    ///     reported as a background failure (its caller is told).
    /// </summary>
    [Fact]
    public async Task SettingsWriter_SaveNowAsync_Failure_FaultsWithReason()
    {
        // Arrange: every write fails
        var probe = new SaveProbe { Fail = true };
        using var writer = CreateWriter(probe, out _);
        var reported = 0;
        writer.WriteFailed += (_, _) => Interlocked.Increment(ref reported);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveNowAsync(Snapshot("confirmed")));

        // Assert
        Assert.Contains("The disk is full.", ex.Message, StringComparison.Ordinal);
        Assert.Empty(probe.Written);
        Assert.Equal(0, reported);
    }

    /// <summary>
    ///     Test that a confirmed save that succeeds drops a snapshot scheduled before it (the confirmed
    ///     one was built later), so the older snapshot is never written over it.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_SaveNowAsync_Success_DropsOlderWaitingSnapshot()
    {
        // Arrange: a snapshot waits for its delay
        var probe = new SaveProbe();
        using var writer = CreateWriter(probe, out var time);
        writer.Schedule(Snapshot("older"));

        // Act: a confirmed save, then the older snapshot's delay runs out
        await writer.SaveNowAsync(Snapshot("confirmed"));
        time.Advance(Delay);
        await writer.FlushAsync();

        // Assert: only the confirmed snapshot was written
        Assert.Equal(["confirmed"], probe.Written);
        Assert.False(writer.HasUnsavedChanges);
    }

    /// <summary>
    ///     Test that a snapshot scheduled while a confirmed save runs is newer than it and is written
    ///     after it.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_SaveNowAsync_ScheduledWhileSaving_WrittenAfter()
    {
        // Arrange: the confirmed save is held at the gate
        var probe = new SaveProbe();
        using var writer = CreateWriter(probe, out var time);
        probe.Gate.Reset();
        var confirmed = writer.SaveNowAsync(Snapshot("confirmed"));
        await probe.WaitForStartAsync();

        // Act: schedule while it is held, then let it complete
        writer.Schedule(Snapshot("newer"));
        probe.Gate.Set();
        await confirmed;
        time.Advance(Delay);
        await writer.FlushAsync();

        // Assert
        Assert.Equal(["confirmed", "newer"], probe.Written);
    }

    /// <summary>
    ///     Test that a confirmed save that fails keeps the snapshot that was waiting scheduled, so a
    ///     change made before it (for example during an add that is then discarded) is still written.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_SaveNowAsync_Failure_KeepsWaitingSnapshotScheduled()
    {
        // Arrange: a snapshot waits for its delay; only the confirmed snapshot fails
        var probe = new SaveProbe { FailFor = "confirmed" };
        using var writer = CreateWriter(probe, out var time);
        writer.Schedule(Snapshot("waiting"));

        // Act: the confirmed save fails, then the waiting snapshot's delay runs out
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveNowAsync(Snapshot("confirmed")));
        time.Advance(Delay);
        await writer.FlushAsync();

        // Assert: the waiting snapshot was written
        Assert.Equal(["waiting"], probe.Written);
        Assert.False(writer.HasUnsavedChanges);
    }

    /// <summary>
    ///     Test that background writes that fail are reported once per failure streak: the first
    ///     failure after a write that succeeded is reported, the failures that follow it are not, and
    ///     a failure after a later success is reported again. The failed snapshot stays unsaved until a
    ///     write succeeds.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_ScheduledWriteFails_RaisesWriteFailedOncePerStreak()
    {
        // Arrange: writes fail
        var probe = new SaveProbe { Fail = true };
        using var writer = CreateWriter(probe, out var time);
        var failures = new List<Exception>();
        writer.WriteFailed += (_, failure) =>
        {
            lock (failures)
            {
                failures.Add(failure);
            }
        };

        // Act: two scheduled writes fail in a row
        writer.Schedule(Snapshot("first"));
        time.Advance(Delay);
        await writer.FlushAsync();
        writer.Schedule(Snapshot("second"));
        time.Advance(Delay);
        await writer.FlushAsync();

        // Assert: reported once, with the reason; the change is still unsaved
        var failure = Assert.Single(failures);
        Assert.Contains("The disk is full.", failure.Message, StringComparison.Ordinal);
        Assert.True(writer.HasUnsavedChanges);

        // Act: writing works again, then fails again
        probe.Fail = false;
        writer.Schedule(Snapshot("third"));
        time.Advance(Delay);
        await writer.FlushAsync();
        probe.Fail = true;
        writer.Schedule(Snapshot("fourth"));
        time.Advance(Delay);
        await writer.FlushAsync();

        // Assert: the success ended the streak, so the new failure was reported
        Assert.Equal(["third"], probe.Written);
        Assert.Equal(2, failures.Count);
    }

    /// <summary>
    ///     Test that a snapshot whose write failed is kept and written by the next flush once writing
    ///     works again (as the window's close does), without a new change.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_FailedWrite_KeptAndWrittenByNextFlush()
    {
        // Arrange: the write fails
        var probe = new SaveProbe { Fail = true };
        using var writer = CreateWriter(probe, out var time);
        writer.Schedule(Snapshot("kept"));
        time.Advance(Delay);
        var firstFlush = await writer.FlushAsync();

        // Act: writing works again; flush
        probe.Fail = false;
        var secondFlush = await writer.FlushAsync();

        // Assert
        Assert.False(firstFlush);
        Assert.True(secondFlush);
        Assert.Equal(["kept"], probe.Written);
        Assert.False(writer.HasUnsavedChanges);
    }

    /// <summary>
    ///     Test that a flush writes the waiting snapshot at once, without waiting for the delay, and that
    ///     it is not written a second time when the delay runs out.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_FlushAsync_WritesWaitingSnapshotNow()
    {
        // Arrange
        var probe = new SaveProbe();
        using var writer = CreateWriter(probe, out var time);
        writer.Schedule(Snapshot("waiting"));

        // Act
        var written = await writer.FlushAsync();
        time.Advance(Delay);
        await writer.FlushAsync();

        // Assert
        Assert.True(written);
        Assert.Equal(["waiting"], probe.Written);
    }

    /// <summary>
    ///     Test that scheduling and flushing make no blocking call on a thread that refuses them (as the
    ///     UI thread does in the headless tests): the real settings store writes on a worker thread, and
    ///     the file is written.
    /// </summary>
    [Fact]
    public async Task SettingsWriter_ScheduleAndFlushOnGuardedThread_WritesWithoutBlockingCall()
    {
        // Arrange: a writer over the real settings store
        var configDir = Path.Combine(Path.GetTempPath(), "agentcontrol_writer_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var writer = CreateWriter(snapshot => SettingsStore.Save(snapshot, configDir), out _);
            Task<bool> flush;

            // Act: schedule and start the flush on a guarded thread
            using (BlockingCallGuard.GuardCurrentThread())
            {
                writer.Schedule(Snapshot("guarded"));
                flush = writer.FlushAsync();
            }

            // Assert: no blocking call was made here (checked before awaiting, on the guarded thread),
            // and once the flush has ended the file holds the snapshot
            Assert.Empty(BlockingCallGuard.TakeViolations());
            var flushed = await flush;
            Assert.True(flushed);
            Assert.Equal("guarded", SettingsStore.Load(configDir).GitExecutablePath);
        }
        finally
        {
            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Test that a writer refuses a missing save delegate or a negative delay.
    /// </summary>
    [Fact]
    public void SettingsWriter_Constructor_InvalidArguments_Throw()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsWriter(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SettingsWriter(_ => { }, delay: TimeSpan.FromSeconds(-1)));
    }

    /// <summary>
    ///     Creates a snapshot told apart by its git executable path.
    /// </summary>
    /// <param name="name">The snapshot's name.</param>
    /// <returns>The snapshot.</returns>
    private static AppSettings Snapshot(string name) => new() { GitExecutablePath = name };

    /// <summary>
    ///     Creates a writer over a probe, on a fake clock.
    /// </summary>
    /// <param name="probe">The save probe.</param>
    /// <param name="time">The fake clock behind the debounce.</param>
    /// <returns>The writer.</returns>
    private static SettingsWriter CreateWriter(SaveProbe probe, out FakeTimeProvider time) =>
        CreateWriter(probe.Save, out time);

    /// <summary>
    ///     Creates a writer over a save delegate, on a fake clock, with no synchronization context
    ///     current.
    /// </summary>
    /// <param name="save">The save delegate.</param>
    /// <param name="time">The fake clock behind the debounce.</param>
    /// <returns>The writer.</returns>
    private static SettingsWriter CreateWriter(Action<AppSettings> save, out FakeTimeProvider time)
    {
        time = new FakeTimeProvider();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            return new SettingsWriter(save, time, Delay);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    ///     The save delegate of a writer under test: records the snapshots it started and wrote, counts
    ///     how many writes run at once, can hold every write at a gate and can fail.
    /// </summary>
    private sealed class SaveProbe
    {
        /// <summary>
        ///     Guards the records.
        /// </summary>
        private readonly Lock _lock = new();

        /// <summary>
        ///     Released once at the start of every write.
        /// </summary>
        private readonly SemaphoreSlim _started = new(0);

        /// <summary>
        ///     Ends a held write when the test is canceled, so a broken test cannot hang the run.
        /// </summary>
        private readonly CancellationToken _cancellationToken = TestContext.Current.CancellationToken;

        /// <summary>
        ///     The names of the snapshots whose write started, in order.
        /// </summary>
        private readonly List<string> _startedNames = [];

        /// <summary>
        ///     The names of the snapshots written, in order.
        /// </summary>
        private readonly List<string> _written = [];

        /// <summary>
        ///     How many writes run now.
        /// </summary>
        private int _running;

        /// <summary>
        ///     Backing field for <see cref="Fail"/>.
        /// </summary>
        private volatile bool _fail;

        /// <summary>
        ///     Gets the gate every write waits at; open unless a test closes it.
        /// </summary>
        public ManualResetEventSlim Gate { get; } = new(initialState: true);

        /// <summary>
        ///     Gets or sets whether every write fails.
        /// </summary>
        public bool Fail
        {
            get => _fail;
            set => _fail = value;
        }

        /// <summary>
        ///     Gets or sets the name of the one snapshot whose write fails, or <see langword="null"/>.
        /// </summary>
        public string? FailFor { get; init; }

        /// <summary>
        ///     Gets the most writes that ran at the same time.
        /// </summary>
        public int MaxRunning { get; private set; }

        /// <summary>
        ///     Gets the names of the snapshots whose write started, in order.
        /// </summary>
        public IReadOnlyList<string> Started
        {
            get
            {
                lock (_lock)
                {
                    return [.. _startedNames];
                }
            }
        }

        /// <summary>
        ///     Gets the names of the snapshots written, in order.
        /// </summary>
        public IReadOnlyList<string> Written
        {
            get
            {
                lock (_lock)
                {
                    return [.. _written];
                }
            }
        }

        /// <summary>
        ///     Waits until one more write has started (counting from the previous wait).
        /// </summary>
        /// <returns>A task that completes when a write started.</returns>
        public Task WaitForStartAsync() => _started.WaitAsync(_cancellationToken);

        /// <summary>
        ///     Writes a snapshot: records it, waits at the gate, and fails or records it as written.
        /// </summary>
        /// <param name="snapshot">The snapshot.</param>
        /// <exception cref="InvalidOperationException">Thrown when the write fails.</exception>
        public void Save(AppSettings snapshot)
        {
            var name = snapshot.GitExecutablePath ?? string.Empty;
            lock (_lock)
            {
                _startedNames.Add(name);
                _running++;
                MaxRunning = Math.Max(MaxRunning, _running);
            }

            _started.Release();
            try
            {
                Gate.Wait(_cancellationToken);
                if (Fail || name == FailFor)
                {
                    throw new InvalidOperationException("The disk is full.");
                }

                lock (_lock)
                {
                    _written.Add(name);
                }
            }
            finally
            {
                lock (_lock)
                {
                    _running--;
                }
            }
        }
    }
}
