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

using System.Text.Json;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Writes an AgentControl <c>settings.json</c> file into an isolated config directory,
///     without taking a compile-time dependency on the (internal) <c>AppSettings</c>/
///     <c>SettingsStore</c> types.
/// </summary>
/// <remarks>
///     Mirrors the exact JSON shape <c>SettingsStore</c>/<c>AppSettings</c> produce (property
///     names and the plain-integer <c>AgentToolKind</c> enum encoding used by
///     <c>System.Text.Json</c>'s default converter - no <c>JsonStringEnumConverter</c> is
///     registered in <c>SettingsStore</c>), so these black-box tests can pre-seed settings the
///     same way a real prior run would have written them. <c>AgentToolKind.Custom</c> is value
///     <c>3</c> (CopilotCli=0, Cursor=1, ClaudeCode=2, Custom=3).
/// </remarks>
internal static class TestSettingsWriter
{
    /// <summary>
    ///     Writes a <c>settings.json</c> file under <paramref name="configDirectory"/> pointing
    ///     the git executable and the (custom) agent-tool command at the arg-logger stub, and
    ///     pre-populating the recent-repos list with a single repo.
    /// </summary>
    /// <param name="configDirectory">The isolated configuration directory to write into; created
    ///     if it does not already exist.</param>
    /// <param name="argLoggerStubExePath">Path to the arg-logger stub executable, used as both
    ///     the git executable override and the custom agent-tool command.</param>
    /// <param name="packageSourcePath">The package source directory to configure, or
    ///     <see langword="null"/> to leave it unconfigured.</param>
    /// <param name="repoPath">Absolute path of the repo to seed into the recent-repos list.</param>
    /// <param name="pinnedPackageName">The pinned package name for <paramref name="repoPath"/>,
    ///     or <see langword="null"/> if the repo has never been synced.</param>
    /// <param name="pinnedPackageVersion">The pinned package version for
    ///     <paramref name="repoPath"/>, or <see langword="null"/> if the repo has never been
    ///     synced.</param>
    public static void Write(
        string configDirectory,
        string argLoggerStubExePath,
        string? packageSourcePath,
        string repoPath,
        string? pinnedPackageName = null,
        string? pinnedPackageVersion = null)
    {
        Directory.CreateDirectory(configDirectory);

        var settings = new
        {
            PackageSourcePath = packageSourcePath,
            GitExecutablePath = argLoggerStubExePath,
            AgentTool = 3, // AgentToolKind.Custom
            CustomAgentCommand = $"\"{argLoggerStubExePath}\"",
            ShellPreference = (string?)null,
            RecentRepos = new[]
            {
                new
                {
                    Path = repoPath,
                    PinnedPackageName = pinnedPackageName,
                    PinnedPackageVersion = pinnedPackageVersion
                }
            }
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(configDirectory, "settings.json"), json);
    }
}
