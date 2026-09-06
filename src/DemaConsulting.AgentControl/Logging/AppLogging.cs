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
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.AgentControl.Logging;

/// <summary>
///     Process-wide access point for the <see cref="ILoggerFactory"/> backing every
///     <see cref="ILogger{TCategoryName}"/> instance used by the application's subsystems.
/// </summary>
/// <remarks>
///     Subsystem classes such as <c>GitClient</c> and <c>AgentToolLauncher</c> accept an optional
///     <see cref="ILogger{TCategoryName}"/> constructor parameter for testability (per
///     coding-principles.md's "Dependency Injection" guidance), but most production call sites
///     (e.g. view models constructed deep inside the Avalonia UI tree) have no convenient path to
///     thread a logger through from <c>Program.Main</c>. This static holder lets those
///     constructors fall back to a shared, already-configured factory without hard-coding
///     Serilog anywhere outside <c>Logging/LoggingSetup.cs</c>, keeping subsystems coupled only to
///     the portable <c>Microsoft.Extensions.Logging</c> abstraction. Defaults to
///     <see cref="NullLoggerFactory"/> (a safe no-op) so unit tests and any code path that runs
///     before <c>LoggingSetup.Initialize</c> never null-reference; <c>LoggingSetup.Initialize</c>
///     overwrites it with the real Serilog-backed factory during application startup. Thread-safe
///     via the property's own atomic reference assignment; not safe to mutate concurrently with
///     reads that expect a stable factory instance for a long-lived logger, but in practice this
///     is set exactly once, early in <c>Program.Main</c>, before any subsystem is constructed.
/// </remarks>
internal static class AppLogging
{
    /// <summary>
    ///     Gets or sets the shared <see cref="ILoggerFactory"/> used to create loggers for
    ///     subsystems that were not given an explicit logger.
    /// </summary>
    public static ILoggerFactory Factory { get; set; } = NullLoggerFactory.Instance;
}
