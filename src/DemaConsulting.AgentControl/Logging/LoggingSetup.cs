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
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace DemaConsulting.AgentControl.Logging;

/// <summary>
///     Configures the application's Serilog-backed logging pipeline as the very first step of
///     startup, before the Avalonia app builder runs, so that even startup-time failures are
///     captured on disk.
/// </summary>
/// <remarks>
///     The investigation into the intermittent <c>GitClientTests</c> flakiness documented in
///     <c>.agent-logs/implementation-agentcontrol-v1-final-3e91c7.md</c> stalled specifically for
///     lack of diagnostic data (no Win32 error code, no stderr capture, no timing) from an actual
///     failure occurrence; this type exists purely to make sure the next occurrence is captured.
///     Logging writes to a file under a <c>logs\</c> subfolder of the same configuration
///     directory <see cref="SettingsStore"/> already uses for <c>settings.json</c> (reusing
///     <see cref="SettingsStore.GetDefaultConfigDirectory"/> and the <c>--config-dir</c> override
///     rather than duplicating path-resolution logic), so production and test runs that already
///     redirect settings via <c>StartupOptions.ConfigDirectory</c> also get isolated log files.
///     Daily rolling with a 14-file retention limit (via Serilog's own
///     <c>Serilog.Sinks.File</c> options) means log files never need hand-rolled cleanup code.
///     Not thread-safe to call more than once concurrently; in practice it is called exactly once,
///     synchronously, from <c>Program.Main</c> before any other thread starts.
/// </remarks>
internal static class LoggingSetup
{
    /// <summary>
    ///     File name pattern passed to the Serilog file sink; the trailing hyphen is where
    ///     Serilog inserts the rolling date suffix (e.g. <c>agentcontrol-20260101.log</c>).
    /// </summary>
    private const string LogFileNamePattern = "agentcontrol-.log";

    /// <summary>
    ///     Number of most-recent daily log files retained before Serilog deletes older ones.
    /// </summary>
    private const int RetainedFileCountLimit = 14;

    /// <summary>
    ///     Initializes Serilog, wires it into <see cref="AppLogging.Factory"/> as an
    ///     <see cref="ILoggerFactory"/>, and installs process-wide safety-net handlers for
    ///     otherwise-unobserved exceptions.
    /// </summary>
    /// <param name="configDirectory">
    ///     The same configuration-directory override consumed by <see cref="SettingsStore"/> (the
    ///     parsed <c>--config-dir</c> startup argument), or <see langword="null"/> to use
    ///     <see cref="SettingsStore.GetDefaultConfigDirectory"/>. Passing the same value used for
    ///     settings ensures test runs that redirect settings to a temporary directory also get an
    ///     isolated log directory rather than writing into the shared production log files.
    /// </param>
    /// <returns>The configured <see cref="ILoggerFactory"/> (also stored in
    ///     <see cref="AppLogging.Factory"/>).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the <c>logs</c> directory cannot
    ///     be created (e.g. permissions, invalid path).</exception>
    public static ILoggerFactory Initialize(string? configDirectory)
    {
        // Reuse the exact same configuration-directory resolution as SettingsStore so a
        // --config-dir override redirects both settings.json and the log files together,
        // keeping test runs isolated from production logs.
        var baseDirectory = configDirectory ?? SettingsStore.GetDefaultConfigDirectory();
        var logDirectory = Path.Combine(baseDirectory, "logs");

        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                   or PathTooLongException or NotSupportedException or DirectoryNotFoundException)
        {
            // Directory.CreateDirectory surfaces path-validation failures (invalid characters, an
            // empty/whitespace path, an unreachable drive, etc.) as several distinct exception
            // types - normalize every one of them to InvalidOperationException, matching the
            // documented contract and the same pattern used by SettingsStore.Save.
            throw new InvalidOperationException(
                $"Failed to create log directory '{logDirectory}': {ex.Message}", ex);
        }

        var logFilePath = Path.Combine(logDirectory, LogFileNamePattern);

        // Daily rolling plus a retained-file-count limit means Serilog itself deletes log files
        // older than the 14 most recent days - no hand-rolled expiration logic is needed here.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCountLimit,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Bridge Serilog to the portable Microsoft.Extensions.Logging.ILogger abstraction so
        // subsystems (GitClient, AgentToolLauncher, ...) depend only on ILogger<T>, never on
        // Serilog directly, keeping them decoupled and testable per coding-principles.md.
        var factory = new SerilogLoggerFactory(Log.Logger, dispose: true);
        AppLogging.Factory = factory;

        InstallUnhandledExceptionSafetyNet(factory);

        return factory;
    }

    /// <summary>
    ///     Hooks <see cref="AppDomain.UnhandledException"/> and
    ///     <see cref="TaskScheduler.UnobservedTaskException"/> so that exceptions which would
    ///     otherwise crash the process (or silently vanish, for unobserved task faults) are always
    ///     recorded to the log file before the process potentially terminates.
    /// </summary>
    /// <param name="factory">The logger factory to log through.</param>
    /// <remarks>
    ///     This is a last-resort safety net, not a replacement for the try/catch around
    ///     <c>Program.Main</c>'s own startup sequence: by the time
    ///     <see cref="AppDomain.UnhandledException"/> fires the process is already unwinding and
    ///     cannot be recovered, so the handler only logs - it never attempts to suppress
    ///     termination.
    /// </remarks>
    private static void InstallUnhandledExceptionSafetyNet(ILoggerFactory factory)
    {
        var logger = factory.CreateLogger("DemaConsulting.AgentControl.UnhandledException");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.LogCritical(
                e.ExceptionObject as Exception,
                "Unhandled exception reached AppDomain.UnhandledException (IsTerminating={IsTerminating})",
                e.IsTerminating);

            // The process is terminating regardless; flush now so the entry is not lost.
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");

            // Mark observed so the finalizer thread does not escalate this into a process crash;
            // the log entry above is the record of the failure.
            e.SetObserved();
        };
    }
}
