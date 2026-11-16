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

using System.Diagnostics;
using DemaConsulting.AgentControl.AgentToolLauncher;

namespace DemaConsulting.AgentControl.Tests.AgentToolLauncher;

/// <summary>
///     An <see cref="IAgentToolProcessStarter"/> for tests that only records the
///     <see cref="ProcessStartInfo"/> it is given and never starts a process, so the launch flow
///     can be exercised without opening a shell on the developer's desktop.
/// </summary>
/// <remarks>
///     Also provides a deterministic <see cref="ShellDetector"/> so a test's shell does not depend
///     on what is installed. Thread-safe, because the code under test launches from worker threads.
/// </remarks>
internal sealed class FakeAgentToolProcessStarter : IAgentToolProcessStarter
{
    /// <summary>
    ///     Guards <see cref="_started"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    ///     The start information received so far, as copies.
    /// </summary>
    private readonly List<ProcessStartInfo> _started = [];

    /// <summary>
    ///     Gets or sets the exception <see cref="Start"/> throws instead of recording the call
    ///     (simulating a process that cannot be started); <see langword="null"/> records it.
    /// </summary>
    public Exception? ThrowOnStart { get; set; }

    /// <summary>
    ///     Gets a snapshot of the start information received so far, oldest first.
    /// </summary>
    public IReadOnlyList<ProcessStartInfo> Started
    {
        get
        {
            lock (_gate)
            {
                return [.. _started];
            }
        }
    }

    /// <summary>
    ///     Creates a <see cref="ShellDetector"/> that behaves like Windows with every shell
    ///     present, independent of the machine the test runs on: a named preference resolves to
    ///     <c>C:\fake\{name}</c> and auto-detection finds PowerShell Core.
    /// </summary>
    /// <returns>The deterministic detector.</returns>
    public static ShellDetector CreateShellDetector() =>
        new(
            resolveOnPath: name => @"C:\fake\" + name,
            fileExists: _ => true,
            getShellEnvironmentVariable: () => "/bin/sh",
            isWindows: true);

    /// <summary>
    ///     Records a copy of <paramref name="startInfo"/>, or throws <see cref="ThrowOnStart"/>.
    /// </summary>
    /// <param name="startInfo">The process start information.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startInfo"/> is
    ///     <see langword="null"/>.</exception>
    public void Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (ThrowOnStart is not null)
        {
            throw ThrowOnStart;
        }

        var copy = new ProcessStartInfo
        {
            FileName = startInfo.FileName,
            WorkingDirectory = startInfo.WorkingDirectory,
            UseShellExecute = startInfo.UseShellExecute
        };
        foreach (var argument in startInfo.ArgumentList)
        {
            copy.ArgumentList.Add(argument);
        }

        lock (_gate)
        {
            _started.Add(copy);
        }
    }
}
