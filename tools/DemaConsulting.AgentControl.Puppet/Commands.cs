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
using System.Globalization;
using DemaConsulting.AgentControl.Puppet.Automation;
using FlaUI.Core.AutomationElements;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     The commands of <c>agentcontrol-puppet</c>; this part holds the helpers they share.
/// </summary>
/// <remarks>
///     Each command is one static method that does one thing and returns its exit code. Failures that should end the
///     command with a specific code are thrown as <see cref="PuppetException"/> and turned into a message and an exit
///     code by <c>Program</c>.
/// </remarks>
internal static partial class Commands
{
    /// <summary>
    ///     The error label reported when an element cannot be found.
    /// </summary>
    private const string NotFoundCode = "NOT_FOUND";

    /// <summary>
    ///     The error label reported when an instance is not running.
    /// </summary>
    private const string NotRunningCode = "NOT_RUNNING";

    /// <summary>
    ///     Attaches to the instance a command addresses.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The attachment; the caller disposes it.</returns>
    /// <exception cref="PuppetException">Thrown when the instance is not running or has no window.</exception>
    private static AttachContext Attach(ParsedCommand parsed)
    {
        var alias = ValidAlias(parsed);
        var registry = InstanceRegistry.CreateDefault();
        var record = registry.TryGetLive(alias, out var state);
        if (record is null)
        {
            throw new PuppetException(ExitCodes.NotRunning, NotRunningCode, NotRunningMessage(alias, state));
        }

        Process process;
        try
        {
            process = Process.GetProcessById(record.Pid);
        }
        catch (ArgumentException)
        {
            throw new PuppetException(ExitCodes.NotRunning, NotRunningCode, NotRunningMessage(alias, InstanceState.NotRunning));
        }

        return AttachContext.TryAttach(alias, process, out var error)
            ?? throw new PuppetException(ExitCodes.NotRunning, "ATTACH_FAILED", error);
    }

    /// <summary>
    ///     Explains why an alias has no live instance.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="state">The verdict on its record.</param>
    /// <returns>The message.</returns>
    private static string NotRunningMessage(string alias, InstanceState state) => state switch
    {
        InstanceState.PidReused => $"Instance '{alias}' is not running: its recorded process id now belongs to another program (the record is stale; use launch).",
        InstanceState.ConfigMismatch => $"Instance '{alias}' is not running: the process with its recorded id was not started with the recorded --config-dir.",
        _ => $"Instance '{alias}' is not running (no live instance was launched under that alias; use launch, or instances to list the live ones).",
    };

    /// <summary>
    ///     Gets the instance alias of a command, checked.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The alias.</returns>
    /// <exception cref="PuppetException">Thrown when the alias is not valid.</exception>
    private static string ValidAlias(ParsedCommand parsed) =>
        InstanceRegistry.IsValidAlias(parsed.Instance)
            ? parsed.Instance
            : throw PuppetException.Usage($"'{parsed.Instance}' is not a valid instance alias (letters, digits, '-' and '_').");

    /// <summary>
    ///     Requires a number of positional arguments.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <param name="count">The minimum number.</param>
    /// <param name="usage">The usage text printed when too few were given.</param>
    /// <exception cref="PuppetException">Thrown when there are too few arguments.</exception>
    private static void RequireArgs(ParsedCommand parsed, int count, string usage)
    {
        if (parsed.Positional.Count < count)
        {
            throw PuppetException.Usage(usage);
        }
    }

    /// <summary>
    ///     Parses a number argument.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="what">What the number is, for the message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="PuppetException">Thrown when the text is not a number.</exception>
    private static double Number(string text, string what) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw PuppetException.Usage($"{what} must be a number, not '{text}'.");

    /// <summary>
    ///     Reads an integer option.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <param name="name">The option name.</param>
    /// <param name="fallback">The value when the option is absent.</param>
    /// <returns>The value.</returns>
    /// <exception cref="PuppetException">Thrown when the option is not an integer.</exception>
    private static int IntOption(ParsedCommand parsed, string name, int fallback)
    {
        if (parsed.GetOption(name) is not { } text)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw PuppetException.Usage($"--{name} must be an integer, not '{text}'.");
    }

    /// <summary>
    ///     Finds the element with an AutomationId, honoring <c>--item</c> and <c>--window</c>.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <param name="parsed">The parsed command (for the scope options).</param>
    /// <param name="automationId">The AutomationId.</param>
    /// <returns>The element.</returns>
    /// <exception cref="PuppetException">Thrown when there is no such element.</exception>
    private static AutomationElement Locate(AttachContext context, ParsedCommand parsed, string automationId)
    {
        var scope = ElementScope.FromCommand(parsed);
        return ElementFinder.FindById(context, automationId, scope)
            ?? throw new PuppetException(ExitCodes.NotFound, NotFoundCode, ElementFinder.BuildNotFoundMessage(context, automationId, scope));
    }

    /// <summary>
    ///     Runs an action on the element with an AutomationId.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <param name="automationId">The AutomationId.</param>
    /// <param name="action">The action; it returns the exit code.</param>
    /// <returns>The exit code of the action.</returns>
    /// <exception cref="PuppetException">Thrown when there is no instance or no such element.</exception>
    private static int WithElement(ParsedCommand parsed, string automationId, Func<AttachContext, AutomationElement, int> action)
    {
        using var context = Attach(parsed);
        return action(context, Locate(context, parsed, automationId));
    }

    /// <summary>
    ///     Describes the scope of a command for a result line.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>Text such as <c> item="my-repo"</c>, or an empty string when the command is not scoped.</returns>
    private static string ScopeSuffix(ParsedCommand parsed) =>
        (parsed.GetOption("item") is { } item ? $" item=\"{item}\"" : string.Empty)
        + (parsed.GetOption("window") is { } window ? $" window=\"{window}\"" : string.Empty);

    /// <summary>
    ///     Prints a line to standard output and returns success.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns><see cref="ExitCodes.Success"/>.</returns>
    private static int Ok(string line)
    {
        Console.WriteLine(line);
        return ExitCodes.Success;
    }
}
