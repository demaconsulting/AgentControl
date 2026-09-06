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

namespace DemaConsulting.AgentControl.Startup;

/// <summary>
///     Immutable representation of the fixed set of AgentControl command-line startup arguments.
/// </summary>
/// <remarks>
///     AgentControl is a GUI application, so this parser intentionally supports only the small
///     set of flags needed for test isolation and troubleshooting (per the architecture's
///     "Testability via config-dir override and stub executables" decision): a configuration
///     directory override so FlaUI-driven integration tests can point the app at an isolated
///     settings folder instead of the real <c>%APPDATA%\AgentControl\</c>, and git/agent-tool
///     command overrides so those same tests can substitute an arg-logging stub executable for
///     git and the agentic CLI tool. Instances are created exclusively via <see cref="Parse"/>;
///     the type performs no I/O itself. Immutable and thread-safe once constructed.
/// </remarks>
internal sealed class StartupOptions
{
    /// <summary>
    ///     Gets the configuration directory override, or <see langword="null"/> if the default
    ///     <c>%APPDATA%\AgentControl\</c> (via <see cref="Environment.SpecialFolder.ApplicationData"/>)
    ///     should be used.
    /// </summary>
    public string? ConfigDirectory { get; private init; }

    /// <summary>
    ///     Gets the git executable path override supplied for test isolation, or
    ///     <see langword="null"/> if no override was supplied on the command line.
    /// </summary>
    /// <remarks>
    ///     This overrides only the startup-time default; the per-user <c>Settings</c> subsystem's
    ///     stored git executable path (if any) takes precedence once settings are loaded.
    /// </remarks>
    public string? GitExecutableOverride { get; private init; }

    /// <summary>
    ///     Gets the agent tool command override supplied for test isolation, or
    ///     <see langword="null"/> if no override was supplied on the command line.
    /// </summary>
    /// <remarks>
    ///     This overrides only the startup-time default; the per-user <c>Settings</c> subsystem's
    ///     stored agent tool command (if any) takes precedence once settings are loaded.
    /// </remarks>
    public string? AgentToolCommandOverride { get; private init; }

    /// <summary>
    ///     Private constructor - use <see cref="Parse"/> instead.
    /// </summary>
    private StartupOptions()
    {
    }

    /// <summary>
    ///     Parses AgentControl's startup command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments as supplied to <c>Main</c>.</param>
    /// <returns>A new <see cref="StartupOptions"/> instance reflecting the parsed arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="args"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when an unsupported argument is encountered, or
    ///     when an option that requires a value is the last argument (missing its value).</exception>
    public static StartupOptions Parse(string[] args)
    {
        // Validate input
        ArgumentNullException.ThrowIfNull(args);

        string? configDirectory = null;
        string? gitExecutableOverride = null;
        string? agentToolCommandOverride = null;

        // Walk the argument list, consuming the value that follows each recognized option
        var index = 0;
        while (index < args.Length)
        {
            var arg = args[index++];
            switch (arg)
            {
                case "--config-dir":
                    configDirectory = GetRequiredValue(arg, args, ref index);
                    break;

                case "--git-path":
                    gitExecutableOverride = GetRequiredValue(arg, args, ref index);
                    break;

                case "--agent-tool-command":
                    agentToolCommandOverride = GetRequiredValue(arg, args, ref index);
                    break;

                default:
                    throw new ArgumentException($"Unsupported argument '{arg}'", nameof(args));
            }
        }

        return new StartupOptions
        {
            ConfigDirectory = configDirectory,
            GitExecutableOverride = gitExecutableOverride,
            AgentToolCommandOverride = agentToolCommandOverride
        };
    }

    /// <summary>
    ///     Retrieves the value following an option flag, advancing the parse index past it.
    /// </summary>
    /// <param name="option">The option flag whose value is being retrieved (used in error messages).</param>
    /// <param name="args">The full argument list.</param>
    /// <param name="index">Current parse index; advanced past the consumed value on success.</param>
    /// <returns>The value that followed <paramref name="option"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="option"/> is the last
    ///     argument, meaning no value follows it.</exception>
    private static string GetRequiredValue(string option, string[] args, ref int index)
    {
        if (index >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value", nameof(args));
        }

        return args[index++];
    }
}
