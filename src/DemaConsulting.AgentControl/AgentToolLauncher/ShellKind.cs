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

namespace DemaConsulting.AgentControl.AgentToolLauncher;

/// <summary>
///     Identifies the family of shell/terminal that AgentControl detected or was configured to
///     use for launching the agentic CLI tool.
/// </summary>
internal enum ShellKind
{
    /// <summary>
    ///     PowerShell 7+ (<c>pwsh</c>), cross-platform PowerShell.
    /// </summary>
    PowerShellCore,

    /// <summary>
    ///     Windows PowerShell 5.x (<c>powershell.exe</c>), the Windows-only in-box PowerShell.
    /// </summary>
    WindowsPowerShell,

    /// <summary>
    ///     The Windows command interpreter (<c>cmd.exe</c>), used only when no PowerShell is
    ///     available.
    /// </summary>
    Cmd,

    /// <summary>
    ///     The user's default POSIX shell on macOS/Linux (from the <c>$SHELL</c> environment
    ///     variable, falling back to <c>/bin/sh</c>).
    /// </summary>
    Posix
}

/// <summary>
///     A shell/terminal that has been detected or configured, along with the path used to launch
///     it.
/// </summary>
/// <param name="Kind">The family of shell.</param>
/// <param name="ExecutablePath">
///     The path (or bare command name, if resolvable via <c>PATH</c> by the OS process loader)
///     used to launch this shell.
/// </param>
internal sealed record DetectedShell(ShellKind Kind, string ExecutablePath);
