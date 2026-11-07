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

using System.Globalization;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     Parses and compares version strings (<c>MAJOR.MINOR.PATCH[.REVISION][-PRERELEASE][+METADATA]</c>)
///     used to name and pin agent packages from zip files and NuGet sources.
/// </summary>
/// <remarks>
///     <para>
///     Implements the <see href="https://semver.org">Semantic Versioning 2.0.0</see> grammar plus
///     the two extensions NuGet package versions need: an optional fourth numeric segment (the
///     revision, compared after the patch and treated as zero when absent, so <c>1.0.0.0</c>
///     equals <c>1.0.0</c>) and optional <c>+metadata</c> build metadata. Metadata is preserved in
///     <see cref="Metadata"/> and <see cref="Original"/> but is ignored by comparison, equality and
///     hashing, per semver precedence rules. A prerelease version always compares as lower than the
///     same numeric version without one (e.g. <c>1.0.0-beta &lt; 1.0.0</c>), and prerelease
///     identifiers follow semver precedence (numeric identifiers compare numerically, e.g.
///     <c>beta.2 &lt; beta.10</c>; numeric ranks below alphanumeric; more identifiers rank higher).
///     </para>
///     <para>
///     Zip package file names carry only the strict three-part form, so
///     <see cref="TryParseStrict"/> keeps that narrower grammar for file-name splitting while
///     <see cref="TryParse"/> accepts the extended NuGet-compatible grammar. Prerelease labels
///     compare without regard to letter case, as NuGet does (so <c>1.0.0-beta</c> equals
///     <c>1.0.0-BETA</c>), and equality and hashing agree with that comparison.
///     Immutable and thread-safe once constructed.
///     </para>
/// </remarks>
internal sealed class PackageVersion : IComparable<PackageVersion>, IEquatable<PackageVersion>
{
    /// <summary>
    ///     The exact text this version was parsed from, or its canonical rendering when it was
    ///     built from components; returned by <see cref="ToString"/> and <see cref="Original"/>.
    /// </summary>
    private readonly string _original;

    /// <summary>
    ///     Gets the major version component.
    /// </summary>
    public int Major { get; }

    /// <summary>
    ///     Gets the minor version component.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    ///     Gets the patch version component.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    ///     Gets the prerelease identifier (the text after the first <c>-</c>), or
    ///     <see langword="null"/> if this is a release version.
    /// </summary>
    public string? Prerelease { get; }

    /// <summary>
    ///     Gets the optional fourth numeric segment (NuGet's legacy revision), or zero when the
    ///     version has only three numeric segments.
    /// </summary>
    public int Revision { get; }

    /// <summary>
    ///     Gets the build metadata (the text after <c>+</c>), or <see langword="null"/> when none
    ///     was supplied. Never taken into account by comparison or equality.
    /// </summary>
    public string? Metadata { get; }

    /// <summary>
    ///     Gets the exact text this version was parsed from (including any metadata), or the
    ///     canonical rendering when it was constructed from components. This is the text to
    ///     persist in a pin so the original spelling survives a round trip.
    /// </summary>
    public string Original => _original;

    /// <summary>
    ///     Initializes a new <see cref="PackageVersion"/> from its components.
    /// </summary>
    /// <param name="major">Major version component. Must be non-negative.</param>
    /// <param name="minor">Minor version component. Must be non-negative.</param>
    /// <param name="patch">Patch version component. Must be non-negative.</param>
    /// <param name="prerelease">Optional prerelease identifier; <see langword="null"/> for a
    ///     release version.</param>
    /// <param name="revision">Optional fourth numeric segment. Must be non-negative; zero (the
    ///     default) means "absent".</param>
    /// <param name="metadata">Optional build metadata; <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric component is
    ///     negative.</exception>
    public PackageVersion(int major, int minor, int patch, string? prerelease = null, int revision = 0, string? metadata = null)
        : this(major, minor, patch, prerelease, revision, metadata, original: null)
    {
    }

    /// <summary>
    ///     Initializes a new <see cref="PackageVersion"/> from its components and, optionally, the
    ///     exact text it was parsed from.
    /// </summary>
    /// <param name="major">Major version component. Must be non-negative.</param>
    /// <param name="minor">Minor version component. Must be non-negative.</param>
    /// <param name="patch">Patch version component. Must be non-negative.</param>
    /// <param name="prerelease">Optional prerelease identifier; <see langword="null"/> for a
    ///     release version.</param>
    /// <param name="revision">Fourth numeric segment; zero means "absent". Must be non-negative.</param>
    /// <param name="metadata">Build metadata, or <see langword="null"/>.</param>
    /// <param name="original">The text the version was parsed from, or <see langword="null"/> to
    ///     derive the canonical rendering from the components.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric component is
    ///     negative.</exception>
    private PackageVersion(
        int major, int minor, int patch, string? prerelease, int revision, string? metadata, string? original)
    {
        if (major < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major), major, "Major version must be non-negative.");
        }

        if (minor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minor), minor, "Minor version must be non-negative.");
        }

        if (patch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(patch), patch, "Patch version must be non-negative.");
        }

        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "Revision must be non-negative.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
        Revision = revision;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
        Metadata = string.IsNullOrEmpty(metadata) ? null : metadata;
        _original = original ?? BuildCanonicalText();
    }

    /// <summary>
    ///     Renders the canonical text of this version from its components, used when no original
    ///     text is available (a version built directly from components).
    /// </summary>
    /// <returns>The canonical rendering, e.g. <c>1.2.3</c>, <c>1.2.3.4-beta.1+build5</c>. The
    ///     fourth segment is only rendered when non-zero.</returns>
    private string BuildCanonicalText()
    {
        var text = Revision == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}.{Revision}";
        if (Prerelease is not null)
        {
            text += "-" + Prerelease;
        }

        return Metadata is null ? text : text + "+" + Metadata;
    }

    /// <summary>
    ///     Attempts to parse a version string using the extended, NuGet-compatible grammar:
    ///     three or four numeric segments, an optional prerelease, and optional <c>+metadata</c>.
    /// </summary>
    /// <param name="value">The string to parse, e.g. <c>"1.2.3"</c>, <c>"1.2.3.4"</c>,
    ///     <c>"1.2.3-beta.1"</c> or <c>"1.2.3+build5"</c>.</param>
    /// <param name="version">On success, the parsed version; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> was a valid version;
    ///     otherwise <see langword="false"/>.</returns>
    /// <remarks>
    ///     Used for pins and NuGet-reported versions. Does not throw on malformed input. Zip file
    ///     names must instead use <see cref="TryParseStrict"/> so their name/version splitting
    ///     rules stay exactly as they always were.
    /// </remarks>
    public static bool TryParse(string? value, out PackageVersion? version) =>
        TryParseCore(value, allowExtended: true, out version);

    /// <summary>
    ///     Attempts to parse a strict semantic version string
    ///     (<c>MAJOR.MINOR.PATCH[-PRERELEASE]</c>): exactly three numeric segments and no
    ///     <c>+metadata</c>.
    /// </summary>
    /// <param name="value">The string to parse, e.g. <c>"1.2.3"</c> or <c>"1.2.3-beta.1"</c>.</param>
    /// <param name="version">On success, the parsed version; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> was a valid strict semantic
    ///     version; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    ///     Used for zip package file names, where the package name and version are split at the
    ///     leftmost hyphen whose suffix parses; accepting the extended grammar there would change
    ///     which file names are recognized as packages. Does not throw on malformed input —
    ///     callers enumerating a directory of arbitrary file names are expected to skip entries
    ///     that fail to parse rather than treat them as errors.
    /// </remarks>
    public static bool TryParseStrict(string? value, out PackageVersion? version) =>
        TryParseCore(value, allowExtended: false, out version);

    /// <summary>
    ///     Shared parser behind <see cref="TryParse"/> and <see cref="TryParseStrict"/>.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="allowExtended"><see langword="true"/> to accept a fourth numeric segment and
    ///     <c>+metadata</c>; <see langword="false"/> for the strict three-part grammar.</param>
    /// <param name="version">On success, the parsed version; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> on success.</returns>
    private static bool TryParseCore(string? value, bool allowExtended, out PackageVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Split off the optional build metadata (everything after the first '+'); only the
        // extended grammar permits it, and it must be dot-separated non-empty identifiers
        var remainder = value;
        string? metadata = null;
        var plusIndex = remainder.IndexOf('+');
        if (plusIndex >= 0)
        {
            if (!allowExtended)
            {
                return false;
            }

            metadata = remainder[(plusIndex + 1)..];
            remainder = remainder[..plusIndex];
            if (!IsValidMetadata(metadata))
            {
                return false;
            }
        }

        // Split off the optional prerelease suffix (everything after the first '-')
        var dashIndex = remainder.IndexOf('-');
        var numericPart = dashIndex >= 0 ? remainder[..dashIndex] : remainder;
        var prerelease = dashIndex >= 0 ? remainder[(dashIndex + 1)..] : null;

        // The numeric part must be three (strict) or three-to-four (extended) dot-separated
        // non-negative integers
        var parts = numericPart.Split('.');
        var maxParts = allowExtended ? 4 : 3;
        if (parts.Length < 3 || parts.Length > maxParts)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        var revision = 0;
        if (parts.Length == 4 &&
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out revision))
        {
            return false;
        }

        // Reject an empty prerelease identifier (e.g. a trailing "-" with nothing after it) or
        // one that is not valid semver (empty/invalid identifiers, numeric leading zeros)
        if (dashIndex >= 0 && (string.IsNullOrEmpty(prerelease) || !IsValidPrerelease(prerelease)))
        {
            return false;
        }

        version = new PackageVersion(major, minor, patch, prerelease, revision, metadata, value);
        return true;
    }

    /// <summary>
    ///     Determines whether a build-metadata string is valid semver: dot-separated, non-empty
    ///     identifiers of ASCII letters, digits, and hyphens (leading zeros are permitted).
    /// </summary>
    /// <param name="metadata">The metadata text after the <c>+</c>.</param>
    /// <returns><see langword="true"/> if valid.</returns>
    private static bool IsValidMetadata(string metadata) =>
        metadata.Split('.').All(id => id.Length > 0 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));

    /// <summary>
    ///     Compares this version to another, following semver precedence rules.
    /// </summary>
    /// <param name="other">The version to compare against.</param>
    /// <returns>
    ///     A negative value if this version precedes <paramref name="other"/>, zero if they are
    ///     equal, or a positive value if this version follows <paramref name="other"/>.
    /// </returns>
    public int CompareTo(PackageVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        // Compare numeric components first, most significant first
        var majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0)
        {
            return majorCompare;
        }

        var minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0)
        {
            return minorCompare;
        }

        var patchCompare = Patch.CompareTo(other.Patch);
        if (patchCompare != 0)
        {
            return patchCompare;
        }

        // The optional fourth segment ranks after the patch (zero when absent)
        var revisionCompare = Revision.CompareTo(other.Revision);
        if (revisionCompare != 0)
        {
            return revisionCompare;
        }

        // Same numeric version: a release (no prerelease) outranks any prerelease of the
        // same numeric version, per semver precedence rules. Build metadata never takes part.
        if (Prerelease is null && other.Prerelease is null)
        {
            return 0;
        }

        if (Prerelease is null)
        {
            return 1;
        }

        if (other.Prerelease is null)
        {
            return -1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>
    ///     Compares two prerelease strings using semver precedence: identifiers are compared left
    ///     to right; numeric identifiers compare numerically and rank below alphanumeric ones;
    ///     alphanumeric identifiers compare in ASCII order ignoring letter case (as NuGet does,
    ///     so <c>1.0.0-beta</c> equals <c>1.0.0-BETA</c>); and when all shared identifiers are
    ///     equal, the longer list ranks higher.
    /// </summary>
    /// <param name="left">The left prerelease string (dot-separated identifiers).</param>
    /// <param name="right">The right prerelease string (dot-separated identifiers).</param>
    /// <returns>A negative, zero, or positive value as <paramref name="left"/> precedes, equals,
    ///     or follows <paramref name="right"/>.</returns>
    private static int ComparePrerelease(string left, string right)
    {
        var leftIds = left.Split('.');
        var rightIds = right.Split('.');

        for (var i = 0; i < Math.Min(leftIds.Length, rightIds.Length); i++)
        {
            var result = CompareIdentifier(leftIds[i], rightIds[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return leftIds.Length.CompareTo(rightIds.Length);
    }

    /// <summary>
    ///     Compares a single prerelease identifier pair.
    /// </summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns>A negative, zero, or positive comparison result.</returns>
    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = IsNumeric(left);
        var rightNumeric = IsNumeric(right);

        if (leftNumeric && rightNumeric)
        {
            // Compare as arbitrarily large integers without parsing: after trimming leading
            // zeros, a longer digit string is larger, and equal lengths compare by character
            var l = left.TrimStart('0');
            var r = right.TrimStart('0');
            return l.Length != r.Length ? l.Length.CompareTo(r.Length) : string.CompareOrdinal(l, r);
        }

        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        // Prerelease labels compare without regard to letter case, as NuGet does, so the
        // "latest" chosen here for a NuGet source is the one NuGet itself would choose
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Determines whether an identifier consists only of ASCII digits.
    /// </summary>
    /// <param name="identifier">The identifier to test.</param>
    /// <returns><see langword="true"/> if the identifier is non-empty and all digits.</returns>
    private static bool IsNumeric(string identifier) =>
        identifier.Length > 0 && identifier.All(char.IsAsciiDigit);

    /// <summary>
    ///     Determines whether a prerelease string is a valid semver prerelease: dot-separated,
    ///     non-empty identifiers of ASCII letters, digits, and hyphens, where numeric identifiers
    ///     have no leading zeros.
    /// </summary>
    /// <param name="prerelease">The prerelease string to validate.</param>
    /// <returns><see langword="true"/> if valid.</returns>
    private static bool IsValidPrerelease(string prerelease) =>
        prerelease.Split('.').All(id =>
            id.Length > 0 &&
            id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
            !(IsNumeric(id) && id.Length > 1 && id[0] == '0'));

    /// <inheritdoc />
    public bool Equals(PackageVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PackageVersion);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(
        Major, Minor, Patch, Revision, StringComparer.OrdinalIgnoreCase.GetHashCode(CanonicalPrerelease(Prerelease) ?? string.Empty));

    /// <summary>
    ///     Produces the representation equality is based on: numeric identifiers with leading
    ///     zeros removed (the hash is then taken ignoring letter case), so versions that compare
    ///     equal always hash equally.
    /// </summary>
    /// <param name="prerelease">The prerelease string, or <see langword="null"/>.</param>
    /// <returns>The canonical prerelease string, or <see langword="null"/>.</returns>
    private static string? CanonicalPrerelease(string? prerelease) =>
        prerelease is null
            ? null
            : string.Join('.', prerelease.Split('.').Select(id => IsNumeric(id) ? id.TrimStart('0') : id));

    /// <inheritdoc />
    /// <remarks>
    ///     Returns <see cref="Original"/>, so a version parsed from text renders exactly as
    ///     written (including any metadata) and round-trips through <see cref="TryParse"/>.
    /// </remarks>
    public override string ToString() => _original;

    /// <summary>
    ///     Determines whether two versions are equal.
    /// </summary>
    /// <param name="left">The first version, or <see langword="null"/>.</param>
    /// <param name="right">The second version, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if both are <see langword="null"/> or compare equal.</returns>
    public static bool operator ==(PackageVersion? left, PackageVersion? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>
    ///     Determines whether two versions are not equal.
    /// </summary>
    /// <param name="left">The first version, or <see langword="null"/>.</param>
    /// <param name="right">The second version, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the versions do not compare equal.</returns>
    public static bool operator !=(PackageVersion? left, PackageVersion? right) => !(left == right);

    /// <summary>
    ///     Determines whether <paramref name="left"/> precedes <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The first version. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is lower precedence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="left"/> is <see langword="null"/>.</exception>
    public static bool operator <(PackageVersion left, PackageVersion? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    ///     Determines whether <paramref name="left"/> is the same as or precedes <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The first version. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is the same as or lower precedence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="left"/> is <see langword="null"/>.</exception>
    public static bool operator <=(PackageVersion left, PackageVersion? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    ///     Determines whether <paramref name="left"/> follows <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The first version. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is higher precedence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="left"/> is <see langword="null"/>.</exception>
    public static bool operator >(PackageVersion left, PackageVersion? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    ///     Determines whether <paramref name="left"/> is the same as or follows <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The first version. Must not be <see langword="null"/>.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is the same as or higher precedence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="left"/> is <see langword="null"/>.</exception>
    public static bool operator >=(PackageVersion left, PackageVersion? right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right) >= 0;
    }
}
