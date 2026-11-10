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
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.RepoConfig;

/// <summary>
///     Raised when a pin file declares a schema version newer than this build understands, so the
///     file is neither interpreted nor overwritten.
/// </summary>
internal sealed class UnsupportedPinSchemaException : InvalidOperationException
{
    /// <summary>
    ///     Initializes the exception with an actionable message.
    /// </summary>
    /// <param name="filePath">Path of the pin file.</param>
    /// <param name="schemaVersion">The schema version the file declares.</param>
    public UnsupportedPinSchemaException(string filePath, int schemaVersion)
        : base($"Pin file '{filePath}' uses schema version {schemaVersion}, which is newer than this version of " +
               $"AgentControl supports (up to {RepoPin.CurrentSchemaVersion}). Update AgentControl to work with " +
               "this repo; the pin file was not changed.")
    {
        SchemaVersion = schemaVersion;
    }

    /// <summary>
    ///     Gets the schema version the pin file declares.
    /// </summary>
    public int SchemaVersion { get; }
}

/// <summary>
///     Reads and writes the per-repo <c>.agentcontrol.json</c> pin file at a repo's root.
/// </summary>
/// <remarks>
///     <para>
///     The store understands four schema versions. Version 1 (no <c>SchemaVersion</c> or
///     <c>Source</c> property) predates configurable sources and is read as a pin to the source
///     named <see cref="RepoPin.LegacySourceAlias"/>; version 2 adds <c>SchemaVersion</c> and
///     <c>Source</c>; version 3 added a <c>ContentIdentity</c> of the package container; version 4
///     makes <c>ContentIdentity</c> an identity of the delivered files, adds <c>SourceRevision</c>
///     and is what <see cref="Save"/> always writes. Reading a pin older than version 4 drops its
///     container identity (it cannot be compared with a files identity, so it means "no recorded
///     identity", never "changed") and carries a recorded Git commit over as the source revision;
///     the files identity is recorded the next time a package is selected, changed or upgraded.
///     A file declaring a newer schema
///     than <see cref="RepoPin.CurrentSchemaVersion"/> is rejected with
///     <see cref="UnsupportedPinSchemaException"/> by both <see cref="Load"/> and <see cref="Save"/>,
///     so an older AgentControl never misreads - or clobbers - a file written by a newer one (which
///     is why the identity is a new schema version: an older client re-pinning a repo to another
///     version would otherwise carry the old identity forward as an unknown property). The declared
///     schema is read first from the raw JSON (<see cref="ProbeSchemaVersion"/>), before any typed
///     reading, so a newer file whose known property has another type (an identity that is an
///     object, say) is still recognized as newer and never treated as corrupt or overwritten. JSON
///     properties this version does not know are preserved on rewrite, whether or not the caller
///     loaded the pin first.
///     </para>
///     <para>
///     Stateless and thread-safe: every member is a pure function of its parameters plus the
///     filesystem. Uses <see cref="PathHelpers.SafePathCombine"/> to construct the pin file path
///     so the repo root cannot be escaped via a malformed root path.
///     </para>
/// </remarks>
internal static class RepoPinStore
{
    /// <summary>
    ///     File name of the per-repo pin file, always located directly at the repo root.
    /// </summary>
    private const string PinFileName = ".agentcontrol.json";

    /// <summary>
    ///     The first schema version whose pins name the package source they came from; an older pin
    ///     is read as a pin to <see cref="RepoPin.LegacySourceAlias"/>.
    /// </summary>
    private const int FirstSchemaWithSource = 2;

    /// <summary>
    ///     The first schema version whose <c>ContentIdentity</c> is computed from the delivered
    ///     files; an older pin's identity (of a container, or of a Git commit) is not carried over as
    ///     one.
    /// </summary>
    private const int FirstSchemaWithFilesIdentity = 4;

    /// <summary>
    ///     The prefix of the Git commit text a schema 3 pin recorded as its identity for a Git
    ///     source; such a value becomes the source revision of the migrated pin.
    /// </summary>
    private const string LegacyGitCommitPrefix = "git-commit:";

    /// <summary>
    ///     JSON serialization options shared by <see cref="Load"/> and <see cref="Save"/>: indented
    ///     output, and case-insensitive reading so hand-edited camelCase names are accepted.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Loads the <see cref="RepoPin"/> for the repo at <paramref name="repoRoot"/>, or
    ///     <see langword="null"/> if the repo has no pin file yet (never synced).
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <returns>The parsed pin, or <see langword="null"/> when no pin file exists. A version 1 pin
    ///     (schema older than 2) is returned with <see cref="RepoPin.Source"/> set to
    ///     <see cref="RepoPin.LegacySourceAlias"/>; a pin older than version 4 is returned without
    ///     a <see cref="RepoPin.ContentIdentity"/> (and, for a Git commit it recorded, with that
    ///     commit as <see cref="RepoPin.SourceRevision"/>).</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedPinSchemaException">Thrown when the pin file declares a schema
    ///     version newer than <see cref="RepoPin.CurrentSchemaVersion"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the pin file exists but cannot be
    ///     read, contains invalid JSON, or is a current-schema pin that names no source (only a
    ///     version 1 pin is mapped to <see cref="RepoPin.LegacySourceAlias"/>).</exception>
    public static RepoPin? Load(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);

        var filePath = PathHelpers.SafePathCombine(repoRoot, PinFileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        RepoPin? pin;
        try
        {
            var json = File.ReadAllText(filePath);

            // The declared schema is read first, from the raw JSON: a file from a newer schema may
            // give a property this version knows another type, and typed reading would then fail
            // before the schema check, making a valid newer file look corrupt
            var declaredSchema = ProbeSchemaVersion(json);
            if (declaredSchema > RepoPin.CurrentSchemaVersion)
            {
                throw new UnsupportedPinSchemaException(filePath, declaredSchema.Value);
            }

            pin = JsonSerializer.Deserialize<RepoPin>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Failed to load pin file '{filePath}': {ex.Message}", ex);
        }

        if (pin is null)
        {
            return null;
        }

        // Never interpret a file written by a newer schema - its fields may mean something else
        if (pin.SchemaVersion > RepoPin.CurrentSchemaVersion)
        {
            throw new UnsupportedPinSchemaException(filePath, pin.SchemaVersion);
        }

        // A pin written before schema 4 recorded an identity of the package container (a zip or
        // .nupkg hash) or of a Git commit: neither can be compared with an identity computed from
        // the delivered files, so the pin has no files identity until it is next saved with one
        // (never a reason to refuse anything). A Git commit it recorded is still the commit its
        // files came from, so it carries over as the source revision, which keeps a moved tag
        // detectable for such a pin.
        if (pin.SchemaVersion < FirstSchemaWithFilesIdentity)
        {
            var legacyIdentity = pin.ContentIdentity?.Trim();
            pin.ContentIdentity = null;
            if (string.IsNullOrWhiteSpace(pin.SourceRevision) && legacyIdentity is not null &&
                legacyIdentity.StartsWith(LegacyGitCommitPrefix, StringComparison.OrdinalIgnoreCase))
            {
                pin.SourceRevision = legacyIdentity;
            }
        }

        // A pin written before sources existed (schema 1) names none: it belongs to the legacy
        // "Default" source (and only that source - there is no guessing among several). From
        // schema 2 on a pin must name its source, whatever the current schema is
        if (pin.SchemaVersion < FirstSchemaWithSource && string.IsNullOrWhiteSpace(pin.Source))
        {
            pin.Source = RepoPin.LegacySourceAlias;
        }

        // A current-schema pin must name its source: falling back to the legacy alias here would
        // resolve the pinned package from a source the pin never named
        if (string.IsNullOrWhiteSpace(pin.Source))
        {
            throw new InvalidOperationException(
                $"Pin file '{filePath}' uses schema version {pin.SchemaVersion} but does not name the package " +
                "source it was pinned from, so it is not trusted to resolve against any source. " +
                "Select the package again to repair it.");
        }

        return pin;
    }

    /// <summary>
    ///     Writes the <see cref="RepoPin"/> for the repo at <paramref name="repoRoot"/>, creating
    ///     or overwriting the <c>.agentcontrol.json</c> file as schema version
    ///     <see cref="RepoPin.CurrentSchemaVersion"/>.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root. Must already exist.</param>
    /// <param name="pin">The pin to persist. Its <see cref="RepoPin.SchemaVersion"/> is set to the
    ///     current version, and JSON properties found in an existing file that this version does
    ///     not model are merged into <see cref="RepoPin.ExtensionData"/> (the pin's own values
    ///     win), so unknown fields survive the rewrite.</param>
    /// <remarks>
    ///     The file is replaced atomically (written to a temporary file, then moved over the pin
    ///     file) so a failure never leaves a half-written pin.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> or
    ///     <paramref name="pin"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnsupportedPinSchemaException">Thrown when the existing file declares a
    ///     newer schema; the file is left untouched.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the pin file cannot be written.</exception>
    public static void Save(string repoRoot, RepoPin pin)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(pin);

        var filePath = PathHelpers.SafePathCombine(repoRoot, PinFileName);
        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            // Look at what is already there first: refuse to clobber a newer schema, and carry
            // over any unknown properties so another version's fields are not stripped
            MergeExistingFile(filePath, pin);
            pin.SchemaVersion = RepoPin.CurrentSchemaVersion;

            var json = JsonSerializer.Serialize(pin, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to save pin file '{filePath}': {ex.Message}", ex);
        }
        finally
        {
            // A successful move leaves nothing behind; remove the temporary file after a failure
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    ///     Inspects an existing pin file before it is overwritten: rejects a newer schema and merges
    ///     its unknown properties into the pin about to be written.
    /// </summary>
    /// <param name="filePath">Path of the pin file.</param>
    /// <param name="pin">The pin about to be written; receives the unknown properties.</param>
    /// <exception cref="UnsupportedPinSchemaException">Thrown when the existing file declares a
    ///     newer schema version.</exception>
    private static void MergeExistingFile(string filePath, RepoPin pin)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        // An existing file that cannot be parsed is corrupt: overwriting it is the repair, so it
        // contributes nothing and blocks nothing. The declared schema is read first from the raw
        // JSON, before any typed reading: a file from a newer schema is never overwritten, even
        // when it gives a property this version knows another type
        RepoPin? existing;
        try
        {
            var json = File.ReadAllText(filePath);
            var declaredSchema = ProbeSchemaVersion(json);
            if (declaredSchema > RepoPin.CurrentSchemaVersion)
            {
                throw new UnsupportedPinSchemaException(filePath, declaredSchema.Value);
            }

            existing = JsonSerializer.Deserialize<RepoPin>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (existing is null)
        {
            return;
        }

        if (existing.SchemaVersion > RepoPin.CurrentSchemaVersion)
        {
            throw new UnsupportedPinSchemaException(filePath, existing.SchemaVersion);
        }

        if (existing.ExtensionData is null)
        {
            return;
        }

        pin.ExtensionData ??= [];
        foreach (var (name, value) in existing.ExtensionData)
        {
            pin.ExtensionData.TryAdd(name, value);
        }
    }

    /// <summary>
    ///     Reads the schema version a pin file declares from its raw JSON, without interpreting any
    ///     other property, so the version can be checked even when the rest of the file has types
    ///     this build does not understand.
    /// </summary>
    /// <param name="json">The text of the pin file.</param>
    /// <returns>The declared version (property name matched without regard to letter case; a number,
    ///     or text holding a whole number); <see cref="int.MaxValue"/> for a positive number too
    ///     large for an <see cref="int"/> (however many digits it has), which is certainly newer;
    ///     <see langword="null"/> when the
    ///     document is not an object or declares no usable version (a schema 1 file, or one whose
    ///     <c>SchemaVersion</c> is another kind of value, which typed reading then judges).</returns>
    /// <exception cref="JsonException">Thrown when the text is not valid JSON.</exception>
    internal static int? ProbeSchemaVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.Equals(nameof(RepoPin.SchemaVersion), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property.Value;
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out var version))
                {
                    return version;
                }

                // A positive number beyond int (a whole number of any length, or one written with
                // an exponent) is newer than any version this build knows; the text is judged, so
                // no numeric range can make it look unusable and let the file be overwritten
                return ReadBeyondInt(value.GetRawText());
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = value.GetString();
            return int.TryParse(text, out var parsed) ? parsed : ReadBeyondInt(text);
        }

        return null;
    }

    /// <summary>
    ///     Judges a number written beyond the range of an <see cref="int"/>.
    /// </summary>
    /// <param name="text">The number as written.</param>
    /// <returns><see cref="int.MaxValue"/> when it is a number above the current schema version
    ///     (however large), otherwise <see langword="null"/>.</returns>
    private static int? ReadBeyondInt(string? text) =>
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var wide) && wide > RepoPin.CurrentSchemaVersion
            ? int.MaxValue
            : null;
}
