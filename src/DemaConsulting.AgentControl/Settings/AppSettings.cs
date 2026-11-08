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
using System.Text.Json.Serialization;
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Per-user AgentControl settings: package sources, tool overrides, agent/shell preferences,
///     and the recent-repos list.
/// </summary>
/// <remarks>
///     This is a plain data-transfer object persisted as JSON by <see cref="SettingsStore"/> under
///     <c>%APPDATA%\AgentControl\</c> (or a config-dir override). All members are mutable with
///     public getters/setters so <see cref="System.Text.Json.JsonSerializer"/> can round-trip the
///     type without custom converters. Not thread-safe; the Phase 2 UI is expected to own a single
///     in-memory instance per application session. By design nothing here - and nothing in
///     <see cref="PackageSourceDefinition"/> - can hold a password, token or other secret.
/// </remarks>
internal sealed class AppSettings
{
    /// <summary>
    ///     Gets or sets the configured agent package sources, in the order the user arranged them.
    /// </summary>
    /// <remarks>
    ///     When empty, <see cref="GetEffectiveSources"/> falls back to the legacy
    ///     <see cref="PackageSourcePath"/> if one is set, and <see cref="SettingsStore.Load"/>
    ///     migrates that value into this list.
    /// </remarks>
    public List<PackageSourceDefinition> Sources { get; set; } = [];

    /// <summary>
    ///     Gets or sets the legacy single filesystem path (local drive, mapped drive, or UNC path)
    ///     from which agent package zip files were enumerated before multiple sources existed, or
    ///     <see langword="null"/> if not configured.
    /// </summary>
    /// <remarks>
    ///     Still read from older settings files (and by tests that seed settings directly) so an
    ///     existing configuration keeps working, but never written back: <see cref="SettingsStore"/>
    ///     persists the equivalent <c>Default</c> entry in <see cref="Sources"/> instead.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PackageSourcePath { get; set; }

    /// <summary>
    ///     Gets the package sources in effect: <see cref="Sources"/> when it has any entries,
    ///     otherwise a single <c>Default</c> zip source for the legacy
    ///     <see cref="PackageSourcePath"/> when that is set, otherwise none.
    /// </summary>
    /// <returns>The effective sources. When non-empty <see cref="Sources"/> exists the legacy path
    ///     is ignored entirely. The returned list is a snapshot; the definitions in it are the live
    ///     objects when they come from <see cref="Sources"/>.</returns>
    public IReadOnlyList<PackageSourceDefinition> GetEffectiveSources()
    {
        if (Sources.Count > 0)
        {
            return Sources;
        }

        return string.IsNullOrWhiteSpace(PackageSourcePath)
            ? []
            : [PackageSourceDefinition.CreateDefaultZip(PackageSourcePath)];
    }

    /// <summary>
    ///     Replaces every collection (and source text field) that an explicit JSON <c>null</c> may
    ///     have left <see langword="null"/> with an empty value, and drops null list entries.
    /// </summary>
    /// <remarks>
    ///     <see cref="System.Text.Json.JsonSerializer"/> bypasses a property's initializer when the
    ///     file says <c>"Sources": null</c> (or <c>"RecentRepos"</c>/<c>"PackageIds"</c>), and the
    ///     property is then really <see langword="null"/> despite its non-nullable type, which
    ///     would crash migration, startup and the source providers. Called by
    ///     <see cref="SettingsStore.Load"/> straight after deserialization so the rest of the
    ///     application can rely on the declared non-null types. Idempotent.
    /// </remarks>
    public void NormalizeNulls()
    {
        // The declared types are non-nullable, but JSON null can still put null in them
        Sources = Sources is null ? [] : [.. Sources.Where(source => source is not null)];
        RecentRepos = RecentRepos is null ? [] : [.. RecentRepos.Where(repo => repo is not null)];

        foreach (var source in Sources)
        {
            source.NormalizeNulls();
        }

        foreach (var repo in RecentRepos)
        {
            repo.Path ??= string.Empty;
        }
    }

    /// <summary>
    ///     Moves a legacy <see cref="PackageSourcePath"/> into <see cref="Sources"/> as the
    ///     <c>Default</c> zip source, then clears the legacy value.
    /// </summary>
    /// <remarks>
    ///     Does nothing when <see cref="Sources"/> already has entries (an existing source list
    ///     always wins) or no legacy path is set; in the first case the stale legacy value is still
    ///     cleared so it can never be written again. Idempotent.
    /// </remarks>
    public void MigrateLegacySource()
    {
        if (Sources.Count == 0 && !string.IsNullOrWhiteSpace(PackageSourcePath))
        {
            Sources.Add(PackageSourceDefinition.CreateDefaultZip(PackageSourcePath));
        }

        PackageSourcePath = null;
    }

    /// <summary>
    ///     Creates the copy of these settings that is written to disk: the same values, but with the
    ///     effective sources in <see cref="Sources"/> and no legacy <see cref="PackageSourcePath"/>.
    /// </summary>
    /// <returns>A shallow copy that shares the recent-repos list and source definition objects
    ///     with this instance and is intended only for serialization. This instance is left
    ///     unchanged, so callers holding it still see the values they set.</returns>
    internal AppSettings CreatePersistedCopy()
    {
        // cspell:disable-next-line
        var copy = (AppSettings)MemberwiseClone();
        copy.Sources = [.. GetEffectiveSources()];
        copy.PackageSourcePath = null;
        return copy;
    }

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
    ///     Gets or sets the color theme the user chose; <see cref="ThemePreference.System"/> (follow
    ///     the operating system) by default.
    /// </summary>
    /// <remarks>
    ///     Persisted as the member's name. A settings file without the value (written by an older
    ///     version), or with one this version does not know (a typo, or a value from a newer
    ///     version), reads as <see cref="ThemePreference.System"/> instead of failing the load, so
    ///     the appearance can never keep the application from starting.
    /// </remarks>
    [JsonConverter(typeof(ThemePreferenceJsonConverter))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    ///     Reads and writes <see cref="ThemePreference"/> by name and falls back to
    ///     <see cref="ThemePreference.System"/> for anything it does not recognize.
    /// </summary>
    /// <remarks>
    ///     A custom converter is needed because the built-in enum handling throws on an unknown
    ///     name, which <see cref="SettingsStore.Load"/> would report as a corrupt settings file.
    ///     Stateless and thread-safe.
    /// </remarks>
    private sealed class ThemePreferenceJsonConverter : JsonConverter<ThemePreference>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <summary>
        ///     Reads a theme preference from a JSON string (a member name, case-insensitive) or
        ///     number (a member's value).
        /// </summary>
        /// <param name="reader">The reader positioned on the value.</param>
        /// <param name="typeToConvert">The target type (unused).</param>
        /// <param name="options">The serializer options (unused).</param>
        /// <returns>The preference the JSON names, or <see cref="ThemePreference.System"/> when
        ///     the value is not a known member (including null, booleans, objects and arrays).</returns>
        public override ThemePreference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String
                    when Enum.TryParse<ThemePreference>(reader.GetString(), ignoreCase: true, out var named)
                         && Enum.IsDefined(named):
                    return named;

                case JsonTokenType.Number
                    when reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(ThemePreference), number):
                    return (ThemePreference)number;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    // Consume the whole nested value so the reader stays positioned correctly
                    reader.Skip();
                    return ThemePreference.System;

                default:
                    return ThemePreference.System;
            }
        }

        /// <summary>
        ///     Writes a theme preference as its member name.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The preference to write.</param>
        /// <param name="options">The serializer options (unused).</param>
        public override void Write(Utf8JsonWriter writer, ThemePreference value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>
    ///     Gets or sets the recent-repos list, most-recently-used first.
    /// </summary>
    public List<RecentRepo> RecentRepos { get; set; } = [];
}
