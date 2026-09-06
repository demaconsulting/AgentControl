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
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests;

/// <summary>
///     xUnit v3 assembly-level fixture that initializes the same Serilog-backed logging pipeline
///     the shipped app uses (<c>LoggingSetup.Initialize</c>) exactly once before any test in this
///     assembly runs.
/// </summary>
/// <remarks>
///     Without this fixture, <c>AppLogging.Factory</c> stays at its default no-op
///     <c>NullLoggerFactory</c> for the entire lifetime of a bare <c>dotnet test</c> run, because
///     <c>Program.Main</c> (the only other place that calls <c>LoggingSetup.Initialize</c>) is
///     never invoked by the test host. That would defeat the entire purpose of the diagnostic
///     logging added to <c>GitClient.RunGit</c>: the intermittent flaky failure this logging
///     exists to root-cause (documented in
///     <c>.agent-logs/implementation-agentcontrol-v1-final-3e91c7.md</c>) occurs specifically
///     inside <c>GitClientTests</c>, i.e. inside this test assembly's own process, so this
///     assembly must initialize logging itself rather than relying on the production app's
///     startup path. Registered via <c>[assembly: Xunit.AssemblyFixture(typeof(TestLoggingFixture))]</c>
///     in <c>AssemblyInfo.cs</c>; xUnit v3 constructs exactly one instance per test run and
///     injects it into any test class constructor that declares a
///     <see cref="TestLoggingFixture"/> parameter. Logs are written under a <c>test-logs</c>
///     directory beside this assembly's own build output (<see cref="AppContext.BaseDirectory"/>)
///     - deliberately never the production <c>%APPDATA%\AgentControl\</c> location - so test runs
///     can never pollute or depend on real user data, and so the resulting log files are
///     immediately recognizable as test-run diagnostics rather than production app logs.
/// </remarks>
public sealed class TestLoggingFixture
{
    /// <summary>
    ///     Gets the configuration-directory equivalent this fixture passed to
    ///     <see cref="LoggingSetup.Initialize"/> - the parent of the <c>logs</c> subfolder
    ///     Serilog actually writes to. Exposed so tests/tooling can locate the resulting log
    ///     file directly (e.g. <c>Path.Combine(ConfigDirectory, "logs")</c>).
    /// </summary>
    public string ConfigDirectory { get; }

    /// <summary>
    ///     Gets the shared <see cref="ILoggerFactory"/> this fixture initialized, so test classes
    ///     can create loggers for the subsystems (<c>GitClient</c>, <c>AgentToolLauncher</c>)
    ///     under test.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    ///     Initializes the test-assembly logging pipeline. Runs exactly once per test run,
    ///     before any test in this assembly executes.
    /// </summary>
    public TestLoggingFixture()
    {
        ConfigDirectory = Path.Combine(AppContext.BaseDirectory, "test-logs");
        LoggerFactory = LoggingSetup.Initialize(ConfigDirectory);
    }

    /// <summary>
    ///     Creates a logger for the given category, backed by this fixture's
    ///     <see cref="LoggerFactory"/>.
    /// </summary>
    /// <typeparam name="T">The type whose fully-qualified name becomes the logger category.</typeparam>
    /// <returns>A logger that writes to this assembly's <c>test-logs</c> log file.</returns>
    public ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.CreateLogger<T>();
    }
}
