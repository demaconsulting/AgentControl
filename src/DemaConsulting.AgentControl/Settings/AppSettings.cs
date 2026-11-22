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
    ///     have left <see langword="null"/> with an empty value, drops null list entries, and
    ///     repairs the organizing values (group names, unknown card colors, and the group colors and
    ///     group order - see <see cref="RepairGroupSettings"/>).
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

            // A card color this version does not know reads as the automatic color and is not
            // kept, so it is never written back either
            repo.CardColor = RecentRepo.NormalizeCardColor(repo.CardColor);
        }

        NormalizeGroups();
    }

    /// <summary>
    ///     Repairs the organizing values the user may have edited by hand or an older/newer version
    ///     may have written: group names are cleaned up and unified, the collapsed list is made
    ///     a list of distinct clean names, and the group colors and group order are repaired.
    /// </summary>
    /// <remarks>
    ///     Each repo's group name is sanitized (see <see cref="RepoGroupName.Sanitize"/>), and
    ///     repos whose names differ only in letter case are given the spelling of the first of them
    ///     in the list, so a group has exactly one display name. The group colors and order are
    ///     repaired last (see <see cref="RepairGroupSettings"/>), against the repaired group names.
    ///     Idempotent.
    /// </remarks>
    private void NormalizeGroups()
    {
        // Spell each group the way its first member does
        var spellings = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var repo in RecentRepos)
        {
            var name = RepoGroupName.Sanitize(repo.GroupName);
            if (name is not null && !spellings.TryAdd(name, name))
            {
                name = spellings[name];
            }

            repo.GroupName = name;
        }

        // The collapsed list: clean, distinct, no blanks
        CollapsedGroups = CollapsedGroups is null
            ? []
            : [.. CollapsedGroups
                .Select(RepoGroupName.Normalize)
                .OfType<string>()
                .Distinct(RepoGroupName.Comparer)];

        // The group colors and order only for groups that exist, spelled like them
        RepairGroupSettings();
    }

    /// <summary>
    ///     Brings <see cref="GroupColors"/> and <see cref="GroupOrder"/> in line with the groups that
    ///     exist: only entries for groups some repo is in, with known colors, one per group, spelled the
    ///     way the group is spelled on its repos.
    /// </summary>
    /// <remarks>
    ///     A group exists only while a repo names it, so its color and position are forgotten once
    ///     its last repo leaves it (or it is removed); a later group of the same name starts afresh
    ///     instead of inheriting a stale choice. Called when the settings are loaded (so hand-edited
    ///     or damaged entries never reach the window) and by the main window's view model after every
    ///     change it saves (so the stored values follow renames, removals and repos leaving groups).
    ///     The rules: each key or name is <see cref="RepoGroupName.Sanitize"/>d and dropped when
    ///     nothing usable (or a built-in section's name) is left; a color is read through
    ///     <see cref="RecentRepo.NormalizeCardColor"/> and dropped when the palette does not know it;
    ///     entries naming a group no repo is in (favorites count as members) are dropped; entries
    ///     that differ only in letter case are merged, the first one winning. Deterministic and
    ///     idempotent, as <c>MainWindowViewModel.CommitSettingsChange</c> requires; replaces both
    ///     collections with new instances. Not thread-safe, like the rest of this type.
    /// </remarks>
    internal void RepairGroupSettings()
    {
        // The groups that exist, each spelled the way its first repo spells it
        var spellings = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var repo in RecentRepos ?? [])
        {
            if (RepoGroupName.Sanitize(repo?.GroupName) is { } name)
            {
                spellings.TryAdd(name, name);
            }
        }

        // Colors: a known color for an existing group, the first entry of a group winning
        var colors = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var (key, value) in GroupColors ?? new Dictionary<string, string>())
        {
            var name = RepoGroupName.Sanitize(key);
            var color = RecentRepo.NormalizeCardColor(value);
            if (name is not null && color is not null && spellings.TryGetValue(name, out var spelled))
            {
                colors.TryAdd(spelled, color);
            }
        }

        GroupColors = colors;

        // Order: existing groups only, each once, in the stored order
        GroupOrder =
        [
            .. (GroupOrder ?? [])
                .Select(RepoGroupName.Sanitize)
                .OfType<string>()
                .Where(spellings.ContainsKey)
                .Select(name => spellings[name])
                .Distinct(RepoGroupName.Comparer)
        ];
    }

    /// <summary>
    ///     Finds the color the user chose for one of their groups.
    /// </summary>
    /// <remarks>
    ///     Looks the group up ignoring letter case even when <see cref="GroupColors"/> was built in
    ///     memory with a case-sensitive comparer (as a test may do), and reads the stored value
    ///     through the palette so an unknown name means no color. Pure; reads only.
    /// </remarks>
    /// <param name="groupName">The group's name; <see langword="null"/>, blank or a built-in
    ///     section's name has no color.</param>
    /// <returns>The palette color's canonical name, or <see langword="null"/> when the group has
    ///     none.</returns>
    internal string? FindGroupColor(string? groupName)
    {
        var name = RepoGroupName.Sanitize(groupName);
        if (name is null || GroupColors is null)
        {
            return null;
        }

        if (GroupColors.TryGetValue(name, out var direct))
        {
            return RecentRepo.NormalizeCardColor(direct);
        }

        var match = GroupColors.FirstOrDefault(pair => RepoGroupName.Comparer.Equals(pair.Key, name));
        return RecentRepo.NormalizeCardColor(match.Value);
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
    ///     Creates a full, independent copy of these settings to hand to the background settings
    ///     writer (see <see cref="SettingsWriter"/>).
    /// </summary>
    /// <remarks>
    ///     The writer serializes the copy on another thread while the window keeps changing the live
    ///     settings, so nothing it reads may be shared with them: the repos, the sources, the collapsed
    ///     sections, the group colors and the group order are copied (the repos and sources entry by
    ///     entry); the remaining values are immutable. Like <see cref="CreatePersistedCopy"/>, the copy
    ///     holds the effective sources and no legacy <see cref="PackageSourcePath"/>. Reads only; call it
    ///     on the thread that changes these settings.
    /// </remarks>
    /// <returns>The copy; this instance is left unchanged.</returns>
    internal AppSettings CreateSnapshot()
    {
        var snapshot = CreatePersistedCopy();
        snapshot.Sources = [.. snapshot.Sources.Select(source => source.Clone())];
        snapshot.RecentRepos = [.. (RecentRepos ?? []).Select(repo => repo.Clone())];
        snapshot.CollapsedGroups = [.. CollapsedGroups ?? []];
        snapshot.GroupColors = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var (group, color) in GroupColors ?? new Dictionary<string, string>())
        {
            snapshot.GroupColors.TryAdd(group, color);
        }

        snapshot.GroupOrder = [.. GroupOrder ?? []];
        return snapshot;
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
        public override ThemePreference Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
    ///     Gets or sets whether the shell window that hosts the launched agent tool stays open
    ///     after the tool exits; <see langword="true"/> by default.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true"/> the shell is started so that it keeps running after the
    ///     tool ends (error messages stay visible and the user stays at a prompt in the repo);
    ///     when <see langword="false"/> the shell ends together with the tool. A settings file
    ///     without the value (written by an older version), or with one that is not a boolean,
    ///     reads as <see langword="true"/> instead of failing the load, so the choice can never
    ///     keep the application from starting.
    /// </remarks>
    [JsonConverter(typeof(KeepShellOpenJsonConverter))]
    public bool KeepShellOpen { get; set; } = true;

    /// <summary>
    ///     Reads and writes the keep-shell-open choice as a JSON boolean and reads anything that is
    ///     not a boolean as <see langword="true"/>.
    /// </summary>
    /// <remarks>
    ///     A custom converter is needed because the built-in boolean handling throws on null, a
    ///     string, a number, an object or an array, which <see cref="SettingsStore.Load"/> would
    ///     report as a corrupt settings file. Stateless and thread-safe.
    /// </remarks>
    private sealed class KeepShellOpenJsonConverter : JsonConverter<bool>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <summary>
        ///     Reads the choice from a JSON boolean.
        /// </summary>
        /// <param name="reader">The reader positioned on the value.</param>
        /// <param name="typeToConvert">The target type (unused).</param>
        /// <param name="options">The serializer options (unused).</param>
        /// <returns>The boolean the JSON holds, or <see langword="true"/> when the value is not a
        ///     boolean (including null, strings, numbers, objects and arrays).</returns>
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;

                case JsonTokenType.False:
                    return false;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    // Consume the whole nested value so the reader stays positioned correctly
                    reader.Skip();
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>
        ///     Writes the choice as a JSON boolean.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The choice to write.</param>
        /// <param name="options">The serializer options (unused).</param>
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
            writer.WriteBooleanValue(value);
    }

    /// <summary>
    ///     Gets or sets the repo list in the user's own display order.
    /// </summary>
    /// <remarks>
    ///     A repo is added at the end of the list; the Move menu items and drag and drop reorder it.
    ///     Nothing else (launching, refreshing, syncing) changes the order. The main window shows
    ///     the repos of each section in this relative order. Because the order is the order of the
    ///     list itself, a repo that is removed is removed from the order too, and no repo can be
    ///     missing from it. <see cref="HasUserRepoOrder"/> tells whether the sequence is already
    ///     the user's order or is still an older file's list that <see cref="EnsureRepoOrder"/>
    ///     has to arrange first.
    /// </remarks>
    public List<RecentRepo> RecentRepos { get; set; } = [];

    /// <summary>
    ///     Gets or sets whether <see cref="RecentRepos"/> is already the user's own display order;
    ///     <see langword="false"/> until <see cref="EnsureRepoOrder"/> has seeded it.
    /// </summary>
    /// <remarks>
    ///     A settings file written by an older version has no such marker, and its list was kept in
    ///     the order the repos were added, which is not the order the user saw (favorites first,
    ///     then the most recently launched). The marker is what tells the two apart, so the legacy
    ///     arrangement is applied exactly once. A value that is not a boolean reads as
    ///     <see langword="false"/> instead of failing the load, so the marker can never keep the
    ///     application from starting.
    /// </remarks>
    [JsonConverter(typeof(FalseWhenInvalidBoolJsonConverter))]
    public bool HasUserRepoOrder { get; set; }

    /// <summary>
    ///     Reads and writes the repo-order marker as a JSON boolean and reads anything that is not
    ///     a boolean as <see langword="false"/>.
    /// </summary>
    /// <remarks>
    ///     A custom converter is needed because the built-in boolean handling throws on null, a
    ///     string, a number, an object or an array, which <see cref="SettingsStore.Load"/> would
    ///     report as a corrupt settings file. Stateless and thread-safe.
    /// </remarks>
    private sealed class FalseWhenInvalidBoolJsonConverter : JsonConverter<bool>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <summary>
        ///     Reads the marker from a JSON boolean.
        /// </summary>
        /// <param name="reader">The reader positioned on the value.</param>
        /// <param name="typeToConvert">The target type (unused).</param>
        /// <param name="options">The serializer options (unused).</param>
        /// <returns>The boolean the JSON holds, or <see langword="false"/> when the value is not
        ///     a boolean (including null, strings, numbers, objects and arrays).</returns>
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;

                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    // Consume the whole nested value so the reader stays positioned correctly
                    reader.Skip();
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        ///     Writes the marker as a JSON boolean.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The marker to write.</param>
        /// <param name="options">The serializer options (unused).</param>
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
            writer.WriteBooleanValue(value);
    }

    /// <summary>
    ///     Seeds the user's repo order once, from the arrangement older versions showed: favorites
    ///     first, then the most recently launched, repos never launched last.
    /// </summary>
    /// <remarks>
    ///     Does nothing when <see cref="HasUserRepoOrder"/> is already <see langword="true"/>, so a
    ///     user's order is never re-sorted. Otherwise the list is stably re-sorted by that rule
    ///     (which gives the same relative order inside every section the main window shows) and the
    ///     marker is set. Called by <see cref="SettingsStore.Load"/> and by the main window's view
    ///     model, so settings built in memory (a first run) are seeded too. Idempotent.
    /// </remarks>
    internal void EnsureRepoOrder()
    {
        if (HasUserRepoOrder)
        {
            return;
        }

        // OrderBy is stable: repos with equal keys keep their list order. The list instance is
        // kept, because the main window shares it by reference.
        RecentRepos ??= [];
        var seeded = RecentRepos
            .OrderByDescending(repo => repo.IsFavorite)
            .ThenByDescending(repo => repo.LastLaunchedUtc ?? DateTimeOffset.MinValue)
            .ToList();
        RecentRepos.Clear();
        RecentRepos.AddRange(seeded);
        HasUserRepoOrder = true;
    }

    /// <summary>
    ///     Gets or sets the names of the main-window sections (the user's own groups and the
    ///     built-in <see cref="RepoGroupName.FavoritesName"/> and <see cref="RepoGroupName.UngroupedName"/>
    ///     sections) that the user has collapsed.
    /// </summary>
    /// <remarks>
    ///     Per-user view state, remembered between sessions; compared ignoring letter case. A
    ///     name here that no longer matches any section is harmless (and forgotten when the group is
    ///     renamed or removed). Anything in the file that is not a list of strings reads as an
    ///     empty list or loses the offending entries, and <see cref="NormalizeNulls"/> repairs the
    ///     rest, so the value can never keep the application from starting.
    /// </remarks>
    [JsonConverter(typeof(StringListJsonConverter))]
    public List<string> CollapsedGroups { get; set; } = [];

    /// <summary>
    ///     Gets or sets the color the user chose for each of their own groups: the group's name
    ///     mapped to the name of a palette color (one of <see cref="RecentRepo.CardColorNames"/>).
    /// </summary>
    /// <remarks>
    ///     A per-user appearance choice stored only in the settings file, never in a repository.
    ///     Keyed by group name ignoring letter case (see <see cref="RepoGroupName.Comparer"/>); a group
    ///     without an entry has no color. The main window shows the color on the group's header and
    ///     uses it for the tiles of the group's repos that have no card color of their own. Anything in
    ///     the file that is not an object of strings reads as empty or loses the offending entries,
    ///     and <see cref="RepairGroupSettings"/> drops unknown colors and entries of groups no repo is
    ///     in, so the value can never keep the application from starting. Always written.
    /// </remarks>
    [JsonConverter(typeof(StringMapJsonConverter))]
    public Dictionary<string, string> GroupColors { get; set; } = new(RepoGroupName.Comparer);

    /// <summary>
    ///     Gets or sets the order the user gave their own groups: group names, first to last.
    /// </summary>
    /// <remarks>
    ///     A per-user view choice stored only in the settings file. The main window lists Favorites
    ///     first, then the groups named here in this order, then the groups that have no position
    ///     yet alphabetically, then Ungrouped. Compared ignoring letter case. Anything in the file that
    ///     is not a list of strings reads as empty or loses the offending entries, and
    ///     <see cref="RepairGroupSettings"/> drops duplicates and names of groups no repo is in. Always
    ///     written.
    /// </remarks>
    [JsonConverter(typeof(StringListJsonConverter))]
    public List<string> GroupOrder { get; set; } = [];

    /// <summary>
    ///     Reads a JSON object of strings into a dictionary keyed ignoring letter case, skipping the
    ///     members whose values are not strings and treating a value that is not an object as empty,
    ///     instead of failing.
    /// </summary>
    /// <remarks>
    ///     A custom converter is needed because the built-in dictionary handling throws when a value
    ///     has the wrong type, which <see cref="SettingsStore.Load"/> would report as a corrupt settings
    ///     file. A member whose name repeats another ignoring letter case is skipped (the first wins).
    ///     Stateless and thread-safe.
    /// </remarks>
    private sealed class StringMapJsonConverter : JsonConverter<Dictionary<string, string>>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <summary>
        ///     Reads the dictionary.
        /// </summary>
        /// <param name="reader">The reader positioned on the value.</param>
        /// <param name="typeToConvert">The target type (unused).</param>
        /// <param name="options">The serializer options (unused).</param>
        /// <returns>The string members of a JSON object, keyed ignoring letter case; an empty
        ///     dictionary for any other value.</returns>
        public override Dictionary<string, string> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<string, string>(RepoGroupName.Comparer);

            // A scalar is a damaged value: ignore it. An array is skipped as a whole.
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    reader.Skip();
                }

                return result;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var key = reader.GetString() ?? string.Empty;
                reader.Read();
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        result.TryAdd(key, reader.GetString() ?? string.Empty);
                        break;

                    case JsonTokenType.StartObject or JsonTokenType.StartArray:
                        // Consume the whole nested value so the reader stays positioned correctly
                        reader.Skip();
                        break;
                }
            }

            return result;
        }

        /// <summary>
        ///     Writes the dictionary as a JSON object of strings.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The dictionary to write.</param>
        /// <param name="options">The serializer options (unused).</param>
        public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, entry) in value ?? new Dictionary<string, string>())
            {
                writer.WriteString(key, entry);
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>
    ///     Reads a list of strings and skips the entries that are not strings, treating a value that
    ///     is not a list as empty, instead of failing.
    /// </summary>
    /// <remarks>
    ///     A custom converter is needed because the built-in list handling throws when an entry has
    ///     the wrong type, which <see cref="SettingsStore.Load"/> would report as a corrupt settings
    ///     file. Stateless and thread-safe.
    /// </remarks>
    private sealed class StringListJsonConverter : JsonConverter<List<string>>
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <summary>
        ///     Reads the list.
        /// </summary>
        /// <param name="reader">The reader positioned on the value.</param>
        /// <param name="typeToConvert">The target type (unused).</param>
        /// <param name="options">The serializer options (unused).</param>
        /// <returns>The strings of a JSON array in order; an empty list for any other value.</returns>
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new List<string>();

            // A scalar is a damaged value: ignore it. An object is skipped as a whole.
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    reader.Skip();
                }

                return result;
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        result.Add(reader.GetString() ?? string.Empty);
                        break;

                    case JsonTokenType.StartObject or JsonTokenType.StartArray:
                        reader.Skip();
                        break;
                }
            }

            return result;
        }

        /// <summary>
        ///     Writes the list as a JSON array of strings.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The list to write.</param>
        /// <param name="options">The serializer options (unused).</param>
        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var name in value ?? [])
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
        }
    }
}
