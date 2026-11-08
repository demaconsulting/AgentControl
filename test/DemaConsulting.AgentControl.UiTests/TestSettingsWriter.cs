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
    ///     Shared options instance for the JSON serialization performed by this writer, avoiding
    ///     a fresh <see cref="JsonSerializerOptions"/> allocation on every call.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    ///     The single argument appended to the quoted arg-logger stub path in the custom
    ///     agent-tool command. Git invocations of the stub never carry it, so a recorded
    ///     invocation whose arguments are exactly this value can only have come from the
    ///     agent-tool launch (the quoted path therefore also had to be executed by the shell,
    ///     not merely echoed).
    /// </summary>
    public const string AgentToolMarkerArgument = "--agent-tool-launch";

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
    /// <param name="nuGetFeedPath">A NuGet package folder to configure as an additional source
    ///     with alias <c>Feed</c> (switching the file to the multi-source format, in which
    ///     <paramref name="packageSourcePath"/> becomes the <c>Default</c> zip source), or
    ///     <see langword="null"/> for the older single-folder format.</param>
    /// <param name="nuGetPackageIds">The package IDs offered by the <c>Feed</c> source.</param>
    /// <param name="nuGetRequireSigned">Whether the <c>Feed</c> source requires signed
    ///     packages.</param>
    /// <param name="nuGetServerIndexUrl">The service-index address of a NuGet package server (plain
    ///     http allowed) to configure as an additional source with alias <c>Server</c>, or
    ///     <see langword="null"/> for none; its package IDs are <paramref name="nuGetPackageIds"/>.</param>
    /// <param name="customize">Edits the settings dictionary just before it is written (for example
    ///     to add more repos, a repo's group or the collapsed groups), or <see langword="null"/>
    ///     for none.</param>
    public static void Write(
        string configDirectory,
        string argLoggerStubExePath,
        string? packageSourcePath,
        string repoPath,
        string? pinnedPackageName = null,
        string? pinnedPackageVersion = null,
        string? nuGetFeedPath = null,
        IReadOnlyList<string>? nuGetPackageIds = null,
        bool nuGetRequireSigned = false,
        string? nuGetServerIndexUrl = null,
        Action<Dictionary<string, object?>>? customize = null)
    {
        Directory.CreateDirectory(configDirectory);

        // Never set by this test helper; a typed local (rather than a cast null literal) gives
        // the anonymous type property below a type without an unnecessary upcast.
        string? shellPreference = null;

        var settings = new Dictionary<string, object?>
        {
            ["GitExecutablePath"] = argLoggerStubExePath,
            ["AgentTool"] = 3, // AgentToolKind.Custom
            ["CustomAgentCommand"] = $"\"{argLoggerStubExePath}\" {AgentToolMarkerArgument}",
            ["ShellPreference"] = shellPreference,
            ["RecentRepos"] = new[]
            {
                new
                {
                    Path = repoPath,
                    PinnedPackageName = pinnedPackageName,
                    PinnedPackageVersion = pinnedPackageVersion
                }
            }
        };

        if (nuGetFeedPath is null && nuGetServerIndexUrl is null)
        {
            // Older single-folder format, which the application migrates to a "Default" source
            settings["PackageSourcePath"] = packageSourcePath;
        }
        else
        {
            var sources = new List<object>();
            if (packageSourcePath is not null)
            {
                sources.Add(new { Alias = "Default", Type = "Zip", Location = packageSourcePath });
            }

            if (nuGetFeedPath is not null)
            {
                sources.Add(new
                {
                    Alias = "Feed",
                    Type = "NuGet",
                    Location = nuGetFeedPath,
                    PackageIds = nuGetPackageIds ?? [],
                    RequireSignedPackages = nuGetRequireSigned
                });
            }

            if (nuGetServerIndexUrl is not null)
            {
                sources.Add(new
                {
                    Alias = "Server",
                    Type = "NuGet",
                    Location = nuGetServerIndexUrl,
                    PackageIds = nuGetPackageIds ?? [],
                    AllowInsecureHttp = true
                });
            }

            settings["Sources"] = sources;
        }

        customize?.Invoke(settings);

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(Path.Combine(configDirectory, "settings.json"), json);
    }
}
