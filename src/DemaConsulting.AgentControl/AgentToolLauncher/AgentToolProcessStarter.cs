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
///     The production <see cref="IAgentToolProcessStarter"/>: starts the agent-tool process
///     through the operating system via <see cref="AgentToolLauncher.Launch"/>.
/// </summary>
/// <remarks>
///     The only place the launch flow reaches <see cref="Process.Start(ProcessStartInfo)"/>.
///     Stateless and thread-safe.
/// </remarks>
internal sealed class AgentToolProcessStarter : IAgentToolProcessStarter
{
    /// <summary>
    ///     Starts the process described by <paramref name="startInfo"/> and releases this
    ///     application's handle to it without stopping it.
    /// </summary>
    /// <param name="startInfo">The process start information.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="startInfo"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process cannot be started.</exception>
    public void Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        // Launch logs the attempt and turns a Win32Exception into an InvalidOperationException.
        // Disposing releases only our handle to the process; it never stops the launched shell.
        using var process = AgentToolLauncher.Launch(startInfo);
    }
}
