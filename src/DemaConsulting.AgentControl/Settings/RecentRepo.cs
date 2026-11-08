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

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Records a single entry in the user's recent-repos list.
/// </summary>
/// <remarks>
///     Mutable to support JSON round-tripping via <see cref="System.Text.Json.JsonSerializer"/>
///     and simple in-place updates (e.g. refreshing <see cref="PinnedPackageVersion"/> after a
///     sync) without reconstructing the containing list. Not thread-safe; callers must
///     synchronize external access when mutating shared instances.
/// </remarks>
internal sealed class RecentRepo
{
    /// <summary>
    ///     Gets or sets the absolute filesystem path to the repository root.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the agent package name last seen pinned in this repo's
    ///     <c>.agentcontrol.json</c>, or <see langword="null"/> if the repo has never been synced.
    /// </summary>
    /// <remarks>
    ///     This is a cached copy for quick display in the recent-repos list (e.g. upgrade-badge
    ///     comparisons without re-reading the pin file); the authoritative value always lives in
    ///     the repo's own <c>.agentcontrol.json</c> (see the <c>RepoConfig</c> subsystem).
    /// </remarks>
    public string? PinnedPackageName { get; set; }

    /// <summary>
    ///     Gets or sets the semantic version last seen pinned in this repo's
    ///     <c>.agentcontrol.json</c>, or <see langword="null"/> if the repo has never been synced.
    /// </summary>
    /// <remarks>
    ///     Cached copy; see <see cref="PinnedPackageName"/> remarks for the authoritative source.
    /// </remarks>
    public string? PinnedPackageVersion { get; set; }

    /// <summary>
    ///     Gets or sets the alias of the package source the pin last seen in this repo's
    ///     <c>.agentcontrol.json</c> names, or <see langword="null"/> if the repo has never been
    ///     synced (or was last seen before pins named a source).
    /// </summary>
    /// <remarks>
    ///     Cached copy; see <see cref="PinnedPackageName"/> remarks for the authoritative source.
    /// </remarks>
    public string? PinnedSource { get; set; }

    /// <summary>
    ///     Gets or sets whether the user has pinned/favorited this repo, per architecture.md's
    ///     recent-repos "pinning/favorites" sort-order feature.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see langword="false"/> so existing settings JSON files (written before
    ///     this field existed) deserialize cleanly - <see cref="System.Text.Json.JsonSerializer"/>
    ///     leaves a missing property at its C# default. Stored here (per-repo, per-user) rather
    ///     than in a separate cache file, following the same precedent already established by
    ///     <see cref="PinnedPackageName"/>/<see cref="PinnedPackageVersion"/>.
    /// </remarks>
    public bool IsFavorite { get; set; }

    /// <summary>
    ///     Gets or sets the UTC timestamp this repo was last successfully launched via
    ///     AgentControl, or <see langword="null"/> if it has never been launched (or was added
    ///     before this field existed).
    /// </summary>
    /// <remarks>
    ///     Drives the recent-repos list's default most-recently-launched sort order. See
    ///     <see cref="IsFavorite"/> remarks for why this lives on <see cref="RecentRepo"/> rather
    ///     than a separate cache file.
    /// </remarks>
    public DateTimeOffset? LastLaunchedUtc { get; set; }

    /// <summary>
    ///     Gets or sets the name of the user's own group this repo is listed under in the main
    ///     window, or <see langword="null"/> for no group.
    /// </summary>
    /// <remarks>
    ///     A user-specific organizing choice: it lives only in the per-user settings file, never in
    ///     the repository or its <c>.agentcontrol.json</c>, so it is never shared through git. A
    ///     group has no entry of its own - it exists while at least one repo names it. Persisted
    ///     only when set; a stored value that is not a string reads as no group, and
    ///     <see cref="AppSettings.NormalizeNulls"/> repairs the text (see <see cref="RepoGroupName"/>),
    ///     so the value can never keep the application from starting.
    /// </remarks>
    [JsonConverter(typeof(OptionalStringJsonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupName { get; set; }

    /// <summary>
    ///     Gets or sets the name of the palette color the user chose for this repo's card tile, or
    ///     <see langword="null"/> for the automatic color (the one derived from the repo path).
    /// </summary>
    /// <remarks>
    ///     A user-specific appearance choice stored only in the per-user settings file, never in
    ///     the repository. Kept as the palette entry's name (for example <c>Teal</c>) rather than an
    ///     index so reordering the palette cannot change anyone's choice; a name the palette does
    ///     not know (a typo, or a color from a newer version) is repaired to <see langword="null"/>,
    ///     the automatic color, by <see cref="AppSettings.NormalizeNulls"/> when the settings are
    ///     loaded (see <see cref="NormalizeCardColor"/>), so it is neither kept nor written back.
    ///     Persisted only when set.
    /// </remarks>
    [JsonConverter(typeof(OptionalStringJsonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CardColor { get; set; }

    /// <summary>
    ///     The names of the colors a repo card's tile can be given, in the order of the palette (a
    ///     name's position is its palette index); the single source of truth for which
    ///     <see cref="CardColor"/> values are valid.
    /// </summary>
    /// <remarks>
    ///     Owned here, where the value is stored, so that <see cref="AppSettings.NormalizeNulls"/>
    ///     can repair an unknown name at load time; the launcher's color palette takes its entries
    ///     from this list rather than repeating it.
    /// </remarks>
    internal static readonly IReadOnlyList<string> CardColorNames =
        ["Blue", "Violet", "Magenta", "Green", "Amber", "Teal", "Orange", "Indigo", "Red", "Lime"];

    /// <summary>
    ///     Finds the canonical spelling of a stored card color name.
    /// </summary>
    /// <param name="name">The stored name; may be <see langword="null"/>, blank or unknown. Leading
    ///     and trailing white space and letter case are ignored.</param>
    /// <returns>The name as listed in <see cref="CardColorNames"/>, or <see langword="null"/> (the
    ///     automatic color) when <paramref name="name"/> is not blank but names no known color, so
    ///     an unknown value (a typo, or a color from a newer version) is never kept or written back.</returns>
    internal static string? NormalizeCardColor(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : CardColorNames.FirstOrDefault(entry => string.Equals(entry, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Creates an independent copy of this entry, so a change can be tried on the copy (and
    ///     saved) before it is applied to the live entry.
    /// </summary>
    /// <returns>A new instance with the same values; every member is a value or an immutable
    ///     string, so the copy shares no mutable state with this instance.</returns>
    internal RecentRepo Clone() =>
        // cspell:disable-next-line
        (RecentRepo)MemberwiseClone();

    /// <summary>
    ///     Reads an optional string and treats a JSON value of any other kind as
    ///     <see langword="null"/> instead of failing.
    /// </summary>
    /// <remarks>
    ///     The built-in string handling throws on a number, boolean, object or array, which
    ///     <see cref="SettingsStore.Load"/> would report as a corrupt settings file; the organizing
    ///     choices on a repo must never be able to do that. Stateless and thread-safe.
    /// </remarks>
    private sealed class OptionalStringJsonConverter : JsonConverter<string?>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <summary>
        ///     Reads a string, or <see langword="null"/> for anything that is not one.
        /// </summary>
        /// <param name="reader">The reader positioned on the value.</param>
        /// <param name="typeToConvert">The target type (unused).</param>
        /// <param name="options">The serializer options (unused).</param>
        /// <returns>The string, or <see langword="null"/> for a null or non-string value.</returns>
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    // Consume the whole nested value so the reader stays positioned correctly
                    reader.Skip();
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        ///     Writes the string, or JSON null.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="options">The serializer options (unused).</param>
        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value);
            }
        }
    }
}
