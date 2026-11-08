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
///     A parsed command line: the command name, its options and its positional arguments.
/// </summary>
internal sealed class ParsedCommand
{
    /// <summary>
    ///     Gets the command name (the first argument), or an empty string when there was none.
    /// </summary>
    internal string CommandName { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the <c>--name [value]</c> options; a flag has a <see langword="null"/> value.
    /// </summary>
    internal IReadOnlyDictionary<string, string?> Options { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Gets the arguments that are not options, in order.
    /// </summary>
    internal IReadOnlyList<string> Positional { get; init; } = [];

    /// <summary>
    ///     Gets the instance alias the command addresses: the <c>--instance</c> value (empty, and so invalid, when the
    ///     option has none), or the default alias.
    /// </summary>
    internal string Instance => HasOption("instance") ? GetOption("instance") ?? string.Empty : InstanceRegistry.DefaultAlias;

    /// <summary>
    ///     Gets the value of an option.
    /// </summary>
    /// <param name="name">The option name without the leading dashes.</param>
    /// <returns>The value, or <see langword="null"/> when the option is absent or a flag.</returns>
    internal string? GetOption(string name) => Options.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    ///     Tests whether an option (or flag) was given.
    /// </summary>
    /// <param name="name">The option name without the leading dashes.</param>
    /// <returns><see langword="true"/> when it was given.</returns>
    internal bool HasOption(string name) => Options.ContainsKey(name);
}

/// <summary>
///     Splits a command line into a <see cref="ParsedCommand"/>.
/// </summary>
internal static class CommandLineParser
{
    /// <summary>
    ///     The options that never take a value, so a positional argument after them is not swallowed.
    /// </summary>
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "allow-existing", "shift", "ctrl", "kill", "trace", "double", "right", "work-area", "screen", "repo", "raw",
    };

    /// <summary>
    ///     Parses the arguments.
    /// </summary>
    /// <remarks>
    ///     The first argument is the command. After it, <c>--name value</c> is an option, <c>--flag</c> is a known
    ///     value-less flag (or an option at the end of the line) and everything else is positional. A value may start
    ///     with a single dash (<c>-40</c>, <c>-3,4</c>) so negative numbers work.
    /// </remarks>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The parsed command; an empty one for no arguments.</returns>
    internal static ParsedCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            return new ParsedCommand();
        }

        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();
        var i = 1;
        while (i < args.Length)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(args[i]);
                i++;
                continue;
            }

            var name = args[i][2..];
            var takesValue = !Flags.Contains(name) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            options[name] = takesValue ? args[i + 1] : null;
            i += takesValue ? 2 : 1;
        }

        return new ParsedCommand { CommandName = args[0], Options = options, Positional = positional };
    }
}
