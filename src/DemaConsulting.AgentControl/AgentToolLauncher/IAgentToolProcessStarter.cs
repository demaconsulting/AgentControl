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

namespace DemaConsulting.AgentControl.AgentToolLauncher;

/// <summary>
///     Starts the operating-system process that hosts a launched agent tool, abstracting the
///     process spawn away from the launch flow so the flow can be tested without starting a real
///     shell.
/// </summary>
/// <remarks>
///     A supporting type of the AgentToolLauncher subsystem. The application uses
///     <see cref="AgentToolProcessStarter"/>; tests supply a fake that only records the
///     <see cref="ProcessStartInfo"/> it was given. Implementations must be thread-safe.
/// </remarks>
internal interface IAgentToolProcessStarter
{
    /// <summary>
    ///     Starts the process described by <paramref name="startInfo"/>.
    /// </summary>
    /// <param name="startInfo">The process start information, typically from
    ///     <see cref="AgentToolLauncher.BuildProcessStartInfo"/>.</param>
    /// <remarks>The caller does not own the started process and cannot observe it afterwards.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startInfo"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process cannot be started.</exception>
    void Start(ProcessStartInfo startInfo);
}
