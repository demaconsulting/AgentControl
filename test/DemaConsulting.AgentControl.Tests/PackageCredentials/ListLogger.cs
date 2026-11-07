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

using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     A logger that records the formatted text of every message it receives, so tests can assert
///     what would reach the application log.
/// </summary>
/// <remarks>
///     Thread-safe, because the NuGet library logs from worker threads.
/// </remarks>
internal sealed class ListLogger : ILogger
{
    /// <summary>
    ///     Gets the formatted messages received, with each exception's text appended.
    /// </summary>
    public List<string> Lines { get; } = [];

    /// <summary>
    ///     Gets or sets the lowest level that is enabled.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>
    ///     Gets a copy of the messages received so far.
    /// </summary>
    /// <returns>The messages.</returns>
    public IReadOnlyList<string> Snapshot()
    {
        lock (Lines)
        {
            return [.. Lines];
        }
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines)
        {
            Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }
}
