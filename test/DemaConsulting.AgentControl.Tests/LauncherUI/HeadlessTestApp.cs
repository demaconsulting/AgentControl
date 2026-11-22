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

using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Bootstraps the real AgentControl <c>App</c> (theme and styles included) on Avalonia's
///     headless platform so window code-behind can be exercised by ordinary <c>[Fact]</c> tests
///     through <see cref="Run(Action)"/> / <see cref="RunAsync(Func{Task})"/>.
/// </summary>
/// <remarks>
///     <para>
///     Avalonia allows only one application per process, so a single shared session (and UI
///     thread) is created lazily and reused by every headless test.
///     </para>
///     <para>
///     The session's UI thread is marked as guarded (see <see cref="BlockingCallGuard"/>), so a git
///     command or another guarded blocking call made on it throws, and is recorded even when a
///     catch-all swallows the throw. After each <see cref="Run(Action)"/> and
///     <see cref="RunAsync(Func{Task})"/> the record is drained on the UI thread and the test fails
///     when it holds anything: no headless test can pass while the UI thread blocks on such a call.
///     </para>
///     <para>
///     The session gives every test body an application of its own and drops what the UI thread still
///     had queued when the body ended. Work a test started on the UI thread that is still in flight then
///     (an upgrade check that runs git, for example) never finishes and never releases what it holds, so a
///     test waits for such work before it ends (see <see cref="RunJobsUntil"/>).
///     </para>
/// </remarks>
public static class HeadlessTestApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(StartGuardedSession);

    /// <summary>
    ///     Starts the shared headless session and marks its UI thread as guarded against blocking calls
    ///     for the rest of the process.
    /// </summary>
    /// <returns>The session.</returns>
    private static HeadlessUnitTestSession StartGuardedSession()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp));
        session.Dispatch(BlockingCallGuard.MarkCurrentThreadGuarded, CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    /// <summary>
    ///     Builds the headless Avalonia application; discovered by name by the test session.
    /// </summary>
    /// <returns>The configured <see cref="AppBuilder"/>.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>
    ///     Runs <paramref name="action"/> on the headless UI thread and waits for it to finish.
    /// </summary>
    /// <param name="action">The UI test body.</param>
    public static void Run(Action action) =>
        Session.Value.Dispatch(() =>
        {
            try
            {
                action();
            }
            catch
            {
                // The test's own failure is the one reported; the record is still cleared
                BlockingCallGuard.TakeViolations();
                throw;
            }

            FailOnBlockingCalls();
        }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    ///     Runs <paramref name="action"/> on the headless UI thread and waits for it to finish, so
    ///     an assertion that fails inside it fails the calling test.
    /// </summary>
    /// <param name="action">The asynchronous UI test body.</param>
    public static void RunAsync(Func<Task> action) =>
        Session.Value.Dispatch(async () =>
        {
            try
            {
                await action();
            }
            catch
            {
                // The test's own failure is the one reported; the record is still cleared
                BlockingCallGuard.TakeViolations();
                throw;
            }

            FailOnBlockingCalls();
            return 0;
        }, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    ///     Runs the UI thread's jobs until every card of <paramref name="viewModel"/> has its repo state
    ///     read (see <see cref="MainWindowViewModel.WhenRepoStateSettledAsync"/>), for a test that runs on
    ///     the UI thread and must act on settled cards.
    /// </summary>
    /// <remarks>
    ///     The cards' reads run in the background and are applied on the UI thread, so a test running
    ///     there cannot block on them: this polls, running the posted jobs in between, and fails the
    ///     test when the state has not settled after about a minute. Call it after the window was shown
    ///     (and its jobs run), so the working-tree checks of the cards it shows are waited for too.
    /// </remarks>
    /// <param name="viewModel">The main window's view model.</param>
    internal static void RunJobsUntilRepoStateSettled(MainWindowViewModel viewModel) =>
        RunJobsUntil(() => viewModel.WhenRepoStateSettledAsync().IsCompleted, "The cards' repo state did not settle.");

    /// <summary>
    ///     Writes the settings that <paramref name="viewModel"/> has waiting to be written (see
    ///     <see cref="MainWindowViewModel.FlushSettingsAsync"/>), running the UI thread's jobs until that is
    ///     done, for a test that runs on the UI thread and reads the settings file right after a change.
    /// </summary>
    /// <remarks>
    ///     The window's changes are written in the background a moment after they were made, so the file
    ///     is read only once this returns. Fails the test when the write has not ended after about a
    ///     minute.
    /// </remarks>
    /// <param name="viewModel">The main window's view model.</param>
    internal static void RunJobsUntilSettingsWritten(MainWindowViewModel viewModel)
    {
        var flush = viewModel.FlushSettingsAsync();
        RunJobsUntil(() => flush.IsCompleted, "The settings were not written.");
    }

    /// <summary>
    ///     Runs the UI thread's jobs until <paramref name="condition"/> holds, for a test that runs on the UI
    ///     thread and must wait for background work whose results are applied there.
    /// </summary>
    /// <remarks>
    ///     Polls, running the posted jobs in between, and fails the test with <paramref name="failure"/> when
    ///     the condition does not hold after about a minute.
    /// </remarks>
    /// <param name="condition">The condition to wait for.</param>
    /// <param name="failure">The failure message when it never holds.</param>
    internal static void RunJobsUntil(Func<bool> condition, string failure)
    {
        for (var attempt = 0; attempt < 2400; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                Dispatcher.UIThread.RunJobs();
                return;
            }

            Thread.Sleep(25);
        }

        Assert.Fail(failure);
    }

    /// <summary>
    ///     Runs blocking test work - an arrange step that writes a pin or hashes a package, or an assertion
    ///     that reads a pin - on a worker thread and waits for it, for a test body that runs on the guarded
    ///     UI thread.
    /// </summary>
    /// <remarks>
    ///     The guard refuses such calls on the UI thread because the application must never make them
    ///     there; a test's own preparation and checks are not the application, so they run on a worker.
    ///     Waiting for the worker cannot deadlock: the work never needs the UI thread. The work runs on a
    ///     thread of its own (a long-running task), which a wait never runs inline on the waiting thread,
    ///     as it may a thread-pool task that has not started yet.
    /// </remarks>
    /// <param name="work">The blocking work.</param>
    internal static void RunOffUiThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .GetAwaiter().GetResult();

    /// <summary>
    ///     Runs blocking test work that produces a value on a worker thread and waits for it (see
    ///     <see cref="RunOffUiThread(Action)"/>).
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="work">The blocking work.</param>
    /// <returns>The value the work produced.</returns>
    internal static T RunOffUiThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .GetAwaiter().GetResult();

    /// <summary>
    ///     Fails the running test when a guarded blocking call (a git command, for example) was made on
    ///     the UI thread since the record was last drained, clearing the record.
    /// </summary>
    /// <remarks>
    ///     Must run on the UI thread: the record is kept per thread.
    /// </remarks>
    private static void FailOnBlockingCalls()
    {
        var violations = BlockingCallGuard.TakeViolations();
        if (violations.Count > 0)
        {
            Assert.Fail($"Blocking call(s) made on the UI thread: {string.Join("; ", violations)}");
        }
    }
}
