### SettingsWriter

#### Verification Approach

`SettingsWriter` is verified with unit tests defined in `SettingsWriterTests.cs`. The debounce runs on a
`FakeTimeProvider` that moves only when a test advances it, and the save delegate is a probe that records the
snapshots it started and wrote, counts how many writes run at once, can hold every write at a gate and can fail like a
full disk, so every test controls exactly when each write happens and none depends on timing. Each snapshot is told
apart by its git executable path. Every writer is created with no synchronization context current, so a failure is
reported on the writing thread before the write's turn ends and a flush that follows has seen it. One test writes
through the real `SettingsStore.Save` into a temporary directory, on a thread that refuses blocking calls
(`BlockingCallGuard`), to show that neither scheduling nor starting a flush makes a blocking call on the caller's
thread.

The window's close relies on two of these guarantees - a confirmed save asked for after the writes it waits for have
ended is written after them and after a scheduled write already waiting for its turn
(`SettingsWriter_Writes_NeverOverlapAndScheduleNeverWaits`), and a confirmed save that succeeds drops an older waiting
snapshot (`SettingsWriter_SaveNowAsync_Success_DropsOlderWaitingSnapshot`) - and is verified with the writer it owns
by the `MainWindowViewModel` tests `MainWindowViewModel_Dispose_AddWriteRunningPastTheWait_ChangeWrittenAfterTheAdd`,
`MainWindowViewModel_Dispose_AddWriteFailsDuringTheClose_ChangeWrittenWithoutTheAdd` and
`MainWindowViewModel_Dispose_AddWriteHeld_CloseWriteIsTheLastWrite` (see the `MainWindowViewModel` verification).

#### Test Environment

N/A - standard test environment. The one test that writes the real settings file uses its own temporary directory and
removes it.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Several snapshots scheduled less than the delay apart are written once, with the newest.
- Scheduling returns while a write is held; writes never run at the same time and run in the order they were
  accepted; the newest scheduled snapshot is written last.
- A confirmed save completes once written, faults with the reason when it is not written (without a background
  report), drops an older waiting snapshot when it succeeds, keeps a newer one, and keeps the waiting one when it fails.
- Background writes that fail in a row are reported once, with the reason; a write that succeeds ends the streak, and
  the next failure is reported again; the failed snapshot is kept until a write succeeds and is written by the next
  flush.
- A flush writes the waiting snapshot at once and it is not written again later.
- Scheduling and starting a flush on a guarded thread record no blocking call, and the real settings file is written.
- A missing save delegate and a negative delay are refused.

#### Test Scenarios

**SettingsWriter_Schedule_SeveralWithinDelay_WritesOnceWithLatest**: Three snapshots are scheduled, each less than the
delay after the previous one, then the delay passes: one write, with the third snapshot, and nothing is left unsaved,
covering `AgentControl-SettingsWriter-Debounce`.

**SettingsWriter_Writes_NeverOverlapAndScheduleNeverWaits**: A write is held at the gate; meanwhile a confirmed save
and two scheduled snapshots are accepted at once and nothing else starts. Once the gate opens the writes run one at a
time (never two at once), in order - the held one, the confirmed one, the newest scheduled one - covering
`AgentControl-SettingsWriter-Debounce` and `AgentControl-SettingsWriter-Serialize`.

**SettingsWriter_SaveNowAsync_Success_CompletesWhenWritten**: A confirmed save completes with its snapshot written and
nothing unsaved, covering `AgentControl-SettingsWriter-ConfirmedSave`.

**SettingsWriter_SaveNowAsync_Failure_FaultsWithReason**: A confirmed save whose write fails faults with
`InvalidOperationException` carrying the reason, nothing is written and no background failure is reported, covering
`AgentControl-SettingsWriter-ConfirmedSave`.

**SettingsWriter_SaveNowAsync_Success_DropsOlderWaitingSnapshot**: A snapshot waits for its delay, then a confirmed save
succeeds and the delay passes: only the confirmed snapshot is written, covering
`AgentControl-SettingsWriter-ConfirmedSave`.

**SettingsWriter_SaveNowAsync_ScheduledWhileSaving_WrittenAfter**: A snapshot scheduled while a confirmed save is held
is written after it, covering `AgentControl-SettingsWriter-ConfirmedSave`.

**SettingsWriter_SaveNowAsync_Failure_KeepsWaitingSnapshotScheduled**: A snapshot waits for its delay, a confirmed save
fails, and the waiting snapshot is still written when its delay passes, covering
`AgentControl-SettingsWriter-ConfirmedSave`.

**SettingsWriter_ScheduledWriteFails_RaisesWriteFailedOncePerStreak**: Two scheduled writes (and the flushes after them)
fail in a row: one report, with the reason, and the change is still unsaved. A write that then succeeds is written,
and a later failure is reported again, covering `AgentControl-SettingsWriter-FailureReportedOnce`.

**SettingsWriter_FailedWrite_KeptAndWrittenByNextFlush**: A scheduled write fails and its flush returns false; once
writing works again the next flush returns true and writes the kept snapshot, covering
`AgentControl-SettingsWriter-FailureReportedOnce` and `AgentControl-SettingsWriter-Flush`.

**SettingsWriter_FlushAsync_WritesWaitingSnapshotNow**: A flush writes a waiting snapshot before its delay, and the
delay passing afterwards writes nothing more, covering `AgentControl-SettingsWriter-Flush`.

**SettingsWriter_ScheduleAndFlushOnGuardedThread_WritesWithoutBlockingCall**: On a thread guarded against blocking
calls, a snapshot is scheduled and a flush started over the real `SettingsStore.Save`: no blocking call is recorded on
that thread, the flush returns true and the settings file holds the snapshot, covering
`AgentControl-SettingsWriter-Serialize` and `AgentControl-SettingsWriter-Flush`.

**SettingsWriter_Constructor_InvalidArguments_Throw**: A null save delegate and a negative delay are refused (a
defensive test, not linked to a requirement).
