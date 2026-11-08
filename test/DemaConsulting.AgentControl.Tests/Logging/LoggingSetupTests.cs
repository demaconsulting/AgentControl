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

using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.Logging;

/// <summary>
///     Unit tests for <see cref="LoggingSetup"/>.
/// </summary>
/// <remarks>
///     <see cref="LoggingSetup.Initialize"/> replaces the process-wide static
///     <c>Serilog.Log.Logger</c>, and the assembly-wide <see cref="TestLoggingFixture"/> has
///     already called it once (before any test in this assembly runs) so that every other test
///     class gets working diagnostic logging. Calling <see cref="LoggingSetup.Initialize"/> a
///     second time here with a *valid* directory would redirect that shared static logger away
///     from the fixture's <c>test-logs</c> location for the remainder of the run, breaking
///     diagnostics for every test class that executes afterward (including ones running
///     concurrently, since xUnit v3 parallelizes across test classes by default). The invalid-path
///     test below is safe because <see cref="LoggingSetup.Initialize"/> fails - and throws -
///     before ever assigning <c>Serilog.Log.Logger</c>. The direct behavioral test reuses the
///     fixture's already-initialized pipeline instead of calling <see cref="LoggingSetup.Initialize"/>
///     again, so it exercises the real rolling log file's location and content without mutating
///     shared process-wide state.
/// </remarks>
public sealed class LoggingSetupTests
{
    /// <summary>
    ///     The assembly-wide logging fixture, injected by xUnit v3 per
    ///     <c>[assembly: Xunit.AssemblyFixture(typeof(TestLoggingFixture))]</c>.
    /// </summary>
    private readonly TestLoggingFixture _loggingFixture;

    /// <summary>
    ///     Initializes a new instance of <see cref="LoggingSetupTests"/>.
    /// </summary>
    /// <param name="loggingFixture">The shared test-assembly logging fixture.</param>
    public LoggingSetupTests(TestLoggingFixture loggingFixture)
    {
        _loggingFixture = loggingFixture;
    }

    /// <summary>
    ///     Test that an unusable log-directory path (one <see cref="Directory.CreateDirectory(string)"/>
    ///     always rejects, on any platform, with an <see cref="ArgumentException"/>) is
    ///     normalized to the documented <see cref="InvalidOperationException"/> contract rather
    ///     than leaking the raw <see cref="ArgumentException"/>.
    /// </summary>
    [Fact]
    public void LoggingSetup_Initialize_LogDirectoryPathInvalid_ThrowsInvalidOperationException()
    {
        // Arrange: a configuration directory whose path contains a NUL character - rejected by
        // Directory.CreateDirectory on every platform since a path can never legally contain one
        var invalidConfigDirectory = Path.Combine(Path.GetTempPath(), "agentcontrol_invalid_\0_dir");

        // Act / Assert: the documented InvalidOperationException contract is honored instead of
        // the raw ArgumentException escaping uncaught
        Assert.Throws<InvalidOperationException>(() => LoggingSetup.Initialize(invalidConfigDirectory));
    }

    /// <summary>
    ///     Test that the process-wide unhandled-exception handler writes the exception only after
    ///     removing secrets from it: an <c>Authorization</c> value, a URL's user information and
    ///     query, and a secret that is registered for redaction never reach the log raw - not in the
    ///     message, not in an inner exception - while the entry still says what happened.
    /// </summary>
    [Fact]
    public void LoggingSetup_ReportUnhandledException_ExceptionCarryingSecrets_LogsOnlyASanitizedDescription()
    {
        // Arrange: an exception chain whose texts carry every kind of secret
        var registered = "registered-secret-" + Guid.NewGuid().ToString("N");
        using var lease = SecretRedactor.Lease(new NuGetCredential("svc", registered));
        var inner = new InvalidOperationException($"GET https://user:inner-password-4423@feeds.example.com/v3/index.json?token=inner-token-4423 failed for {registered}");
        var exception = new InvalidOperationException(
            "request failed. Authorization: Bearer outer-bearer-token-4423", inner);
        var logger = new ListLogger();

        // Act
        LoggingSetup.ReportUnhandledException(logger, exception, isTerminating: true);

        // Assert
        var line = Assert.Single(logger.Snapshot());
        Assert.Contains("Unhandled exception reached AppDomain.UnhandledException", line, StringComparison.Ordinal);
        Assert.Contains("IsTerminating=True", line, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), line, StringComparison.Ordinal);
        Assert.DoesNotContain("outer-bearer-token-4423", line, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-password-4423", line, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-token-4423", line, StringComparison.Ordinal);
        Assert.DoesNotContain(registered, line, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the same protection applies to an unobserved task exception, and that an
    ///     unhandled object that is not an exception is logged without failing.
    /// </summary>
    [Fact]
    public void LoggingSetup_ReportUnobservedAndNonExceptionObjects_AreRedactedAndTolerated()
    {
        // Arrange
        var logger = new ListLogger();
        var basicValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user:password-4424"));
        var aggregate = new AggregateException(new InvalidOperationException($"Authorization: Basic {basicValue}"));

        // Act
        LoggingSetup.ReportUnobservedTaskException(logger, aggregate);
        LoggingSetup.ReportUnhandledException(logger, "just text", isTerminating: false);

        // Assert
        var lines = logger.Snapshot();
        Assert.Equal(2, lines.Count);
        Assert.Contains("Unobserved task exception", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain(basicValue, lines[0], StringComparison.Ordinal);
        Assert.Contains("IsTerminating=False", lines[1], StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a log entry written through the pipeline <see cref="LoggingSetup.Initialize"/>
    ///     configures is actually persisted to the documented rolling log file location, proving
    ///     the requirement's rolling-file/captured-detail behavior with direct evidence rather
    ///     than only the indirect "did not throw" evidence previously relied on.
    /// </summary>
    [Fact]
    public void LoggingSetup_Initialize_LogEntryWritten_AppearsInLogFileUnderLogsSubfolder()
    {
        // Arrange: a distinct marker so this test can identify its own entry among any other
        // concurrently-running test's log output sharing the same rolling file
        var marker = $"LoggingSetupTests-marker-{Guid.NewGuid():N}";
        var logger = _loggingFixture.CreateLogger<LoggingSetupTests>();
        var logsDirectory = Path.Combine(_loggingFixture.ConfigDirectory, "logs");

        // Act: write a log entry through the same Serilog pipeline the fixture initialized via
        // LoggingSetup.Initialize. The date is sampled before and after the write so a run
        // crossing local midnight still finds the rolling file the entry actually went to.
        var dateBefore = DateTime.Now;
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Test marker: {Marker}", marker);
        }

        var dateAfter = DateTime.Now;

        // Assert: one of the rolling log files for those date(s) under the documented "logs"
        // subfolder (earlier days' files may linger in the build output from previous runs, so
        // only these are considered) contains the entry, proving it was actually captured to
        // disk. Files are opened with FileShare.ReadWrite because Serilog's File sink keeps its
        // own write handle open for the lifetime of the process - a plain File.ReadAllText call
        // (which requests only FileShare.Read) is denied access while that handle remains open.
        var logFiles = new[] { dateBefore, dateAfter }
            .Select(date => Path.Combine(logsDirectory, $"agentcontrol-{date:yyyyMMdd}.log"))
            .Distinct()
            .Where(File.Exists)
            .ToList();
        Assert.NotEmpty(logFiles);

        var found = logFiles.Any(logFile =>
        {
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Contains(marker, StringComparison.Ordinal);
        });

        Assert.True(found);
    }
}
