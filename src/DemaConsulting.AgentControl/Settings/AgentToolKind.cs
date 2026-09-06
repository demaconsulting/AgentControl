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

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Identifies which agentic CLI tool AgentControl should launch after a repo is synced.
/// </summary>
/// <remarks>
///     Per architecture.md's "Configurable agent tool and shell" decision, the tool is never
///     hardcoded to Copilot CLI: users pick one of the well-known tools, or <see cref="Custom"/>
///     to supply an arbitrary command string (see <c>AppSettings.CustomAgentCommand</c>).
/// </remarks>
internal enum AgentToolKind
{
    /// <summary>
    ///     GitHub Copilot CLI (<c>copilot</c>).
    /// </summary>
    CopilotCli,

    /// <summary>
    ///     Cursor's CLI/agent tool.
    /// </summary>
    Cursor,

    /// <summary>
    ///     Anthropic's Claude Code CLI.
    /// </summary>
    ClaudeCode,

    /// <summary>
    ///     A user-supplied custom command line, stored in <c>AppSettings.CustomAgentCommand</c>.
    /// </summary>
    Custom
}
