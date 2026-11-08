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

/// <content>
///     The <c>fixture</c> command.
/// </content>
internal static partial class Commands
{
    /// <summary>
    ///     Creates a ready-to-use sandbox: sample packages, optionally a git repository, and a <c>settings.json</c>.
    /// </summary>
    /// <param name="parsed">The parsed command: the folder, <c>--packages</c>, <c>--repo</c>, <c>--pin</c>.</param>
    /// <returns>The exit code.</returns>
    internal static int Fixture(ParsedCommand parsed)
    {
        var options = FixtureOptions.Parse(parsed);
        var result = FixtureBuilder.Build(options, new ProcessGitRunner());
        Console.WriteLine($"FIXTURE root=\"{result.Root}\"");
        Console.WriteLine($"PACKAGES dir=\"{result.PackagesDirectory}\" count={result.Packages.Count}");
        foreach (var package in result.Packages)
        {
            Console.WriteLine($"  package \"{package}\"");
        }

        if (result.RepoDirectory is { } repo)
        {
            Console.WriteLine($"REPO path=\"{repo}\"");
        }

        Console.WriteLine($"SETTINGS path=\"{result.SettingsFile}\"");
        return Ok($"OK fixture config=\"{result.ConfigDirectory}\" (launch it with: launch --config-dir \"{result.ConfigDirectory}\")");
    }
}
