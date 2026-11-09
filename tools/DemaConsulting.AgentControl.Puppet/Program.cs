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
///     The entry point of <c>agentcontrol-puppet</c>, a stateless driver for the AgentControl desktop application.
/// </summary>
internal static class Program
{
    /// <summary>
    ///     The command overview.
    /// </summary>
    private const string UsageText = """
        agentcontrol-puppet - stateless driver for the AgentControl desktop app (one action per process).

        WARNING: some commands drive the real desktop (cursor, keys). Do not use the keyboard or mouse during a run.

        Every command accepts --instance <alias> (default 'main').

        INSTANCES   launch [--instance a] [--exe <path>] [--config-dir <dir>] [--git-path <p>] [--agent-tool-command <cmd>] [--allow-existing]
                    instances | quit [--kill]
        UI          window | tree [--depth N] [--under <id>] | find <id> | read <id> [--raw]
                    click <id> | type <id> "<text>" | choose <comboId> "<item>" | select <listId> "<item text|index>"
                    menu <Menu>/<Item> | dialogs | shot <path.png> [--screen]
                    place (--x <px> --y <px> --width <px> --height <px> | --work-area [--left-gap <px>] [--margin <px>])
                    element commands accept --item <repo name|index> (scope to one repo card) and --window <title>
        REAL INPUT  key <chord> [--to <id>] | hover <id> [--wait <ms>] [--shot <path.png>]
                    click-at <x> <y> [--right] [--double] [--shift|--ctrl] | drag-element <id> <dx> <dy>
                    wheel <notches> [--ctrl] [--on <id> | --at x,y]    (coordinates are physical screen pixels)
        SANDBOX     fixture <dir> [--packages zip|nupkg|both|git|all] [--repo] [--pin <name>@<version>]

        Exit codes: 0 ok, 1 not found, 2 timeout, 3 not running, 4 launch failed, 5 usage, 6 disabled, 7 no focus,
                    8 no effect, 9 read-only, 10 dialog open, 11 obscured (input refused), 99 error.
        """;

    /// <summary>
    ///     The commands and the methods that run them.
    /// </summary>
    private static readonly Dictionary<string, Func<ParsedCommand, int>> Handlers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["launch"] = Commands.Launch,
        ["instances"] = _ => Commands.Instances(),
        ["quit"] = Commands.Quit,
        ["window"] = Commands.Window,
        ["tree"] = Commands.Tree,
        ["find"] = Commands.Find,
        ["click"] = Commands.Click,
        ["type"] = Commands.Type,
        ["choose"] = Commands.Choose,
        ["select"] = Commands.Select,
        ["key"] = Commands.Key,
        ["read"] = Commands.Read,
        ["menu"] = Commands.Menu,
        ["dialogs"] = Commands.Dialogs,
        ["shot"] = Commands.Shot,
        ["place"] = Commands.Place,
        ["hover"] = Commands.Hover,
        ["click-at"] = Commands.ClickAt,
        ["drag-element"] = Commands.DragElement,
        ["wheel"] = Commands.Wheel,
        ["fixture"] = Commands.Fixture,
    };

    /// <summary>
    ///     Runs one command.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code (see the README).</returns>
    public static int Main(string[] args)
    {
        DpiAwareness.Enable();
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? ExitCodes.UsageError : ExitCodes.Success;
        }

        var parsed = CommandLineParser.Parse(args);
        if (!Handlers.TryGetValue(parsed.CommandName, out var handler))
        {
            Console.Error.WriteLine($"USAGE: Unknown command '{parsed.CommandName}'.");
            PrintUsage();
            return ExitCodes.UsageError;
        }

        if (CommandOptions.FindUnknown(parsed) is { } unknown)
        {
            Console.Error.WriteLine($"USAGE: Unknown option '--{unknown}' for command '{parsed.CommandName}'.");
            return ExitCodes.UsageError;
        }

        try
        {
            return handler(parsed);
        }
        catch (PuppetException ex)
        {
            Console.Error.WriteLine($"{ex.Label}: {ex.Message}");
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"ERROR: {parsed.CommandName} failed: {ex.Message}");
            if (parsed.HasOption("trace"))
            {
                Console.Error.WriteLine(ex.ToString());
            }

            return ExitCodes.Error;
        }
    }

    /// <summary>
    ///     Prints the command overview to standard error.
    /// </summary>
    private static void PrintUsage() => Console.Error.WriteLine(UsageText);
}
