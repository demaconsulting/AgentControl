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

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     The process exit codes of <c>agentcontrol-puppet</c>; every command ends with exactly one of them.
/// </summary>
internal static class ExitCodes
{
    /// <summary>
    ///     The command did what was asked.
    /// </summary>
    internal const int Success = 0;

    /// <summary>
    ///     An element, list item, menu item or other target was not found.
    /// </summary>
    internal const int NotFound = 1;

    /// <summary>
    ///     A wait ran out of time.
    /// </summary>
    internal const int Timeout = 2;

    /// <summary>
    ///     The addressed AgentControl instance is not running (or has no window).
    /// </summary>
    internal const int NotRunning = 3;

    /// <summary>
    ///     Launching an instance failed or was refused.
    /// </summary>
    internal const int LaunchFailed = 4;

    /// <summary>
    ///     The command line was wrong.
    /// </summary>
    internal const int UsageError = 5;

    /// <summary>
    ///     The addressed element is disabled.
    /// </summary>
    internal const int Disabled = 6;

    /// <summary>
    ///     No usable keyboard focus.
    /// </summary>
    internal const int NoFocus = 7;

    /// <summary>
    ///     The input had no effect.
    /// </summary>
    internal const int NoEffect = 8;

    /// <summary>
    ///     The element is read-only.
    /// </summary>
    internal const int ReadOnly = 9;

    /// <summary>
    ///     A dialog is open (reported by <c>dialogs</c>).
    /// </summary>
    internal const int BlockedOnDialog = 10;

    /// <summary>
    ///     Real mouse input was refused because the target point is not on the instance's window (covered, off screen or
    ///     the window cannot be brought to the foreground, for example on a locked desktop).
    /// </summary>
    internal const int Obscured = 11;

    /// <summary>
    ///     An unexpected error.
    /// </summary>
    internal const int Error = 99;
}
