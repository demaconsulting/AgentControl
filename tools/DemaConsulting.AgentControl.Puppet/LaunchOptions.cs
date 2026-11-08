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
///     What <c>launch</c> is going to start.
/// </summary>
/// <param name="Alias">The instance alias.</param>
/// <param name="ConfigDir">The absolute <c>--config-dir</c> the instance gets.</param>
/// <param name="ConfigDirGiven">Whether the caller named the config folder (otherwise a fresh temporary one is used).</param>
/// <param name="Arguments">The application's arguments.</param>
internal sealed record LaunchPlan(string Alias, string ConfigDir, bool ConfigDirGiven, IReadOnlyList<string> Arguments);

/// <summary>
///     Builds the command line of AgentControl for the Puppet's <c>launch</c> command.
/// </summary>
internal static class LaunchOptions
{
    /// <summary>
    ///     Translates the options of <c>launch</c> into a plan.
    /// </summary>
    /// <param name="parsed">The parsed <c>launch</c> command.</param>
    /// <param name="tempRoot">The folder a fresh config folder is created under when <c>--config-dir</c> is not given.</param>
    /// <param name="plan">The plan when the result is <see langword="true"/>.</param>
    /// <param name="error">Why the options were rejected when the result is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the options are valid.</returns>
    internal static bool TryBuild(ParsedCommand parsed, string tempRoot, out LaunchPlan plan, out string error)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        plan = new LaunchPlan(parsed.Instance, string.Empty, false, []);
        error = string.Empty;

        if (!InstanceRegistry.IsValidAlias(parsed.Instance))
        {
            error = $"'{parsed.Instance}' is not a valid instance alias (letters, digits, '-' and '_').";
            return false;
        }

        // Every value option needs a value; a flag-like use (nothing after it) is a mistake
        var missing = new[] { "exe", "config-dir", "git-path", "agent-tool-command" }
            .FirstOrDefault(n => parsed.HasOption(n) && string.IsNullOrWhiteSpace(parsed.GetOption(n)));
        if (missing is not null)
        {
            error = $"--{missing} needs a value.";
            return false;
        }

        var given = parsed.GetOption("config-dir");
        var configDir = given is null
            ? Path.Combine(tempRoot, $"config-{parsed.Instance}-{Guid.NewGuid().ToString("N")[..8]}")
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(given));

        var arguments = new List<string> { "--config-dir", configDir };
        if (parsed.GetOption("git-path") is { } git)
        {
            arguments.AddRange(["--git-path", git]);
        }

        if (parsed.GetOption("agent-tool-command") is { } command)
        {
            arguments.AddRange(["--agent-tool-command", command]);
        }

        plan = new LaunchPlan(parsed.Instance, configDir, given is not null, arguments);
        return true;
    }

    /// <summary>
    ///     Joins arguments into one Windows command-line string, quoting those that need it.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The command-line text.</returns>
    internal static string JoinArguments(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(Quote));

    /// <summary>
    ///     Quotes one argument for a Windows command line.
    /// </summary>
    /// <param name="argument">The argument.</param>
    /// <returns>The argument, wrapped in quotes (with embedded quotes escaped) when it is empty or holds white space or quotes.</returns>
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return argument;
        }

        // Backslashes only need doubling in front of a quote (or the closing quote), as the C runtime parses them
        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            builder.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return builder.Append('\\', backslashes * 2).Append('"').ToString();
    }
}

/// <summary>
///     Finds AgentControl builds.
/// </summary>
internal static class AppLocator
{
    /// <summary>
    ///     The file name of the AgentControl executable.
    /// </summary>
    internal const string ExeFileName = "DemaConsulting.AgentControl.exe";

    /// <summary>
    ///     The solution file that marks the repository root.
    /// </summary>
    private const string SolutionFileName = "DemaConsulting.AgentControl.slnx";

    /// <summary>
    ///     The build configurations that are searched.
    /// </summary>
    private static readonly string[] Configurations = ["Release", "Debug"];

    /// <summary>
    ///     Finds the AgentControl executable.
    /// </summary>
    /// <param name="explicitPath">The <c>--exe</c> value, or <see langword="null"/> to search the repository's build output.</param>
    /// <param name="startDirectory">Where to start looking for the repository root (the Puppet's own location).</param>
    /// <param name="path">The full path when the result is <see langword="true"/>.</param>
    /// <param name="error">Why it was not found when the result is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when an executable was found.</returns>
    internal static bool TryResolveExePath(string? explicitPath, string startDirectory, out string path, out string error)
    {
        path = string.Empty;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            path = Path.GetFullPath(explicitPath);
            error = File.Exists(path) ? string.Empty : $"Executable not found: '{path}'.";
            return error.Length == 0;
        }

        // The newest build wins: a stale build in another configuration must not be launched by mistake
        var candidate = FindNewest(startDirectory);
        if (candidate is null)
        {
            error = $"Could not locate {ExeFileName}. Build it first (dotnet build -c Release) or pass --exe <path>.";
            return false;
        }

        path = candidate;
        error = string.Empty;
        return true;
    }

    /// <summary>
    ///     Finds the most recently built executable of the repository that contains a folder.
    /// </summary>
    /// <param name="startDirectory">The folder to start climbing from.</param>
    /// <returns>The full path, or <see langword="null"/> when there is no repository root above it or no build.</returns>
    internal static string? FindNewest(string startDirectory) =>
        CandidateExePaths(startDirectory).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

    /// <summary>
    ///     Enumerates the places a build can be: the repository's <c>bin</c> folders, then beside the Puppet.
    /// </summary>
    /// <param name="startDirectory">The folder to start climbing from.</param>
    /// <returns>The candidate paths (they may not exist).</returns>
    internal static IEnumerable<string> CandidateExePaths(string startDirectory)
    {
        if (FindRepositoryRoot(startDirectory) is { } root)
        {
            var binRoot = Path.Combine(root, "src", "DemaConsulting.AgentControl", "bin");
            foreach (var configuration in Configurations)
            {
                var folder = Path.Combine(binRoot, configuration);
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var framework in Directory.EnumerateDirectories(folder, "net10.0*"))
                {
                    yield return Path.Combine(framework, ExeFileName);
                }
            }
        }

        // Built together into one output folder, the application sits beside the Puppet
        yield return Path.Combine(startDirectory, ExeFileName);
    }

    /// <summary>
    ///     Climbs from a folder to the repository root (the folder with the solution file).
    /// </summary>
    /// <param name="start">The folder to start from.</param>
    /// <returns>The root, or <see langword="null"/> when there is none.</returns>
    internal static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
