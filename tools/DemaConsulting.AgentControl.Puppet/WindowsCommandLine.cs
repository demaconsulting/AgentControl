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

using System.Text;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     Splits a Windows command line into arguments with the rules of <c>CommandLineToArgvW</c> (the C runtime rules
///     that .NET applications get their <c>args</c> from).
/// </summary>
internal static class WindowsCommandLine
{
    /// <summary>
    ///     Splits a command line into its arguments.
    /// </summary>
    /// <remarks>
    ///     The first argument (the program) is read with the special rule that it ends at the next quote (or white space)
    ///     and has no escapes. In the others, <c>2n</c> backslashes before a quote are <c>n</c> backslashes and the quote
    ///     toggles quoting, <c>2n+1</c> backslashes before a quote are <c>n</c> backslashes and a literal quote, backslashes
    ///     elsewhere are literal, and a doubled quote inside a quoted part is one literal quote.
    /// </remarks>
    /// <param name="commandLine">The command line.</param>
    /// <returns>The arguments, program first; empty for a blank command line.</returns>
    internal static List<string> Split(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        var arguments = new List<string>();
        var index = SkipWhiteSpace(commandLine, 0);
        if (index >= commandLine.Length)
        {
            return arguments;
        }

        arguments.Add(ReadProgram(commandLine, ref index));
        while ((index = SkipWhiteSpace(commandLine, index)) < commandLine.Length)
        {
            arguments.Add(ReadArgument(commandLine, ref index));
        }

        return arguments;
    }

    /// <summary>
    ///     Skips spaces and tabs.
    /// </summary>
    /// <param name="text">The command line.</param>
    /// <param name="index">Where to start.</param>
    /// <returns>The index of the next character that is not a space or tab.</returns>
    private static int SkipWhiteSpace(string text, int index)
    {
        while (index < text.Length && text[index] is ' ' or '\t')
        {
            index++;
        }

        return index;
    }

    /// <summary>
    ///     Reads the program name, which has no escape rules.
    /// </summary>
    /// <param name="text">The command line.</param>
    /// <param name="index">Where the program starts; moved past it.</param>
    /// <returns>The program name.</returns>
    private static string ReadProgram(string text, ref int index)
    {
        var quoted = text[index] == '"';
        var start = quoted ? index + 1 : index;
        var end = start;
        while (end < text.Length && (quoted ? text[end] != '"' : text[end] is not (' ' or '\t')))
        {
            end++;
        }

        index = quoted && end < text.Length ? end + 1 : end;
        return text[start..end];
    }

    /// <summary>
    ///     Reads one ordinary argument.
    /// </summary>
    /// <param name="text">The command line.</param>
    /// <param name="index">Where the argument starts; moved past it.</param>
    /// <returns>The argument.</returns>
    private static string ReadArgument(string text, ref int index)
    {
        var builder = new StringBuilder();
        var inQuotes = false;
        while (index < text.Length && (inQuotes || text[index] is not (' ' or '\t')))
        {
            if (text[index] == '\\')
            {
                ReadBackslashes(text, ref index, builder, ref inQuotes);
            }
            else if (text[index] == '"')
            {
                // A doubled quote inside quotes is one literal quote; otherwise a quote toggles quoting
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    builder.Append('"');
                    index += 2;
                }
                else
                {
                    inQuotes = !inQuotes;
                    index++;
                }
            }
            else
            {
                builder.Append(text[index]);
                index++;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Reads a run of backslashes and the quote that may follow it.
    /// </summary>
    /// <param name="text">The command line.</param>
    /// <param name="index">The index of the first backslash; moved past what was consumed.</param>
    /// <param name="builder">Receives the argument's characters.</param>
    /// <param name="inQuotes">The quoting state, toggled when an unescaped quote follows an even run.</param>
    private static void ReadBackslashes(string text, ref int index, StringBuilder builder, ref bool inQuotes)
    {
        var count = 0;
        while (index < text.Length && text[index] == '\\')
        {
            count++;
            index++;
        }

        if (index < text.Length && text[index] == '"')
        {
            builder.Append('\\', count / 2);
            if (count % 2 == 1)
            {
                // An odd run escapes the quote
                builder.Append('"');
                index++;
            }
            else
            {
                // An even run leaves the quote to toggle quoting
                inQuotes = !inQuotes;
                index++;
            }
        }
        else
        {
            builder.Append('\\', count);
        }
    }
}
