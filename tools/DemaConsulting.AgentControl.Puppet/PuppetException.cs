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
///     A failure that ends a command with a specific exit code and a one-line explanation.
/// </summary>
public sealed class PuppetException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PuppetException"/> class.
    /// </summary>
    /// <param name="exitCode">The exit code the process ends with.</param>
    /// <param name="label">The upper-case label printed before the message, such as <c>NOT_FOUND</c>.</param>
    /// <param name="message">What went wrong.</param>
    internal PuppetException(int exitCode, string label, string message)
        : base(message)
    {
        ExitCode = exitCode;
        Label = label;
    }

    /// <summary>
    ///     Gets the exit code the process ends with.
    /// </summary>
    internal int ExitCode { get; }

    /// <summary>
    ///     Gets the label printed before the message.
    /// </summary>
    internal string Label { get; }

    /// <summary>
    ///     Creates the failure for a wrong command line.
    /// </summary>
    /// <param name="usage">The correct usage, or the reason.</param>
    /// <returns>The exception.</returns>
    internal static PuppetException Usage(string usage) => new(ExitCodes.UsageError, "USAGE", usage);

    /// <summary>
    ///     Creates the failure for a target that is not on the instance's window.
    /// </summary>
    /// <param name="message">Why the input was refused.</param>
    /// <returns>The exception.</returns>
    internal static PuppetException Obscured(string message) => new(ExitCodes.Obscured, "OBSCURED", message);
}
