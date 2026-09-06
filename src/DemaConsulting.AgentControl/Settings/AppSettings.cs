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

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Per-user AgentControl settings: package source, tool overrides, agent/shell preferences,
///     and the recent-repos list.
/// </summary>
/// <remarks>
///     This is a plain data-transfer object persisted as JSON by <see cref="SettingsStore"/> under
///     <c>%APPDATA%\AgentControl\</c> (or a config-dir override). All members are mutable with
///     public getters/setters so <see cref="System.Text.Json.JsonSerializer"/> can round-trip the
///     type without custom converters. Not thread-safe; the Phase 2 UI is expected to own a single
///     in-memory instance per application session.
/// </remarks>
internal sealed class AppSettings
{
    /// <summary>
    ///     Gets or sets the filesystem path (local drive, mapped drive, or UNC path) from which
    ///     agent package zip files are enumerated, or <see langword="null"/> if not yet configured.
    /// </summary>
    public string? PackageSourcePath { get; set; }

    /// <summary>
    ///     Gets or sets the git executable path override, or <see langword="null"/> to resolve
    ///     <c>git</c> from the process <c>PATH</c> (the default).
    /// </summary>
    public string? GitExecutablePath { get; set; }

    /// <summary>
    ///     Gets or sets which agentic CLI tool AgentControl launches after a repo is synced.
    /// </summary>
    public AgentToolKind AgentTool { get; set; } = AgentToolKind.CopilotCli;

    /// <summary>
    ///     Gets or sets the custom command line used when <see cref="AgentTool"/> is
    ///     <see cref="AgentToolKind.Custom"/>; ignored for the other <see cref="AgentToolKind"/>
    ///     values.
    /// </summary>
    public string? CustomAgentCommand { get; set; }

    /// <summary>
    ///     Gets or sets the user's shell/terminal preference, or <see langword="null"/> to let
    ///     <c>AgentToolLauncher</c>'s <c>ShellDetector</c> auto-detect the best available shell.
    /// </summary>
    /// <remarks>
    ///     Stored as free text (e.g. <c>"pwsh"</c>, <c>"cmd"</c>) rather than an enum so that any
    ///     shell executable resolvable on <c>PATH</c> or by absolute path can be selected, without
    ///     this settings model needing to enumerate every possible shell.
    /// </remarks>
    public string? ShellPreference { get; set; }

    /// <summary>
    ///     Gets or sets the recent-repos list, most-recently-used first.
    /// </summary>
    public List<RecentRepo> RecentRepos { get; set; } = [];
}
