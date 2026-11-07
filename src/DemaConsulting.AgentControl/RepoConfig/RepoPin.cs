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

namespace DemaConsulting.AgentControl.RepoConfig;

/// <summary>
///     Data model for a repository's <c>.agentcontrol.json</c> pin file.
/// </summary>
/// <remarks>
///     <para>
///     Per architecture.md's "Mandatory version pinning" decision, a repo always names an exact
///     package and version - there is no "latest"/unpinned mode. Since several package sources can
///     be configured, a pin also names the <see cref="Source"/> alias the package came from, so it
///     is only ever resolved against that source.
///     </para>
///     <para>
///     Property names are written exactly as declared (PascalCase, as pin files always have been)
///     and read case-insensitively. Mutable with public getters/setters so
///     <see cref="JsonSerializer"/> can round-trip the type without custom converters; any JSON
///     property this version does not know is retained in <see cref="ExtensionData"/> and written
///     back unchanged, so a rewrite never silently strips fields added by another version. Not
///     thread-safe.
///     </para>
/// </remarks>
internal sealed class RepoPin
{
    /// <summary>
    ///     The pin file schema version this build reads and writes.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    ///     The source alias a pin written before pins named a source is resolved against: the alias
    ///     the legacy single package-folder setting migrates to.
    /// </summary>
    public const string LegacySourceAlias = "Default";

    /// <summary>
    ///     Gets or sets the pin file schema version. Zero (or one) means the file was written
    ///     before pins named a source; <see cref="RepoPinStore"/> always writes
    ///     <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    ///     Gets or sets the alias of the package source the pinned package came from. For a pin file
    ///     written before sources existed, <see cref="RepoPinStore.Load"/> sets this to
    ///     <see cref="LegacySourceAlias"/>.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    ///     Gets or sets the pinned agent package's name or ID (e.g. the <c>{name}</c> portion of
    ///     the <c>{name}-{version}.zip</c> file this repo was last synced from).
    /// </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the pinned agent package's exact version text (e.g. <c>"1.2.3"</c>).
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets whether the user has already been offered, and declined, the package's
    ///     optional root-level <c>AGENTS.md</c> template for this repo.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see langword="false"/> ("not yet declined"), which is also what a
    ///     pre-existing pin file written before this field existed deserializes to - a missing
    ///     JSON property is left at the C# property's default by
    ///     <see cref="JsonSerializer"/>, so no migration code is needed. Once set
    ///     to <see langword="true"/>, the offer is never repeated for this repo; there is
    ///     deliberately no mechanism to reset it back to <see langword="false"/> (e.g. if the
    ///     user later deletes their <c>AGENTS.md</c> file), per the feature's explicit scope.
    /// </remarks>
    public bool AgentsMdTemplateDeclined { get; set; }

    /// <summary>
    ///     Gets or sets the JSON properties present in the file that this version does not model,
    ///     retained so they are written back unchanged.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
