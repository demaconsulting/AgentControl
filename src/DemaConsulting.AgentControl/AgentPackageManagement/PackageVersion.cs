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
///     Parses and compares semantic version strings (<c>MAJOR.MINOR.PATCH[-PRERELEASE]</c>) used
///     to name and pin agent package zip files.
/// </summary>
/// <remarks>
///     Implements the subset of the <see href="https://semver.org">Semantic Versioning 2.0.0</see>
///     grammar needed for AgentControl: numeric major/minor/patch plus an optional dot-separated
///     prerelease identifier list (build metadata after <c>+</c> is intentionally unsupported —
///     package file names never carry it). A prerelease version always compares as lower than the
///     same numeric version without one (e.g. <c>1.0.0-beta &lt; 1.0.0</c>), matching semver's
///     precedence rules. Immutable and thread-safe once constructed.
/// </remarks>
internal sealed class PackageVersion : IComparable<PackageVersion>, IEquatable<PackageVersion>
{
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
    ///     Initializes a new <see cref="PackageVersion"/> from its components.
    /// </summary>
    /// <param name="major">Major version component. Must be non-negative.</param>
    /// <param name="minor">Minor version component. Must be non-negative.</param>
    /// <param name="patch">Patch version component. Must be non-negative.</param>
    /// <param name="prerelease">Optional prerelease identifier; <see langword="null"/> for a
    ///     release version.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any numeric component is
    ///     negative.</exception>
    public PackageVersion(int major, int minor, int patch, string? prerelease = null)
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

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
    }

    /// <summary>
    ///     Attempts to parse a semantic version string.
    /// </summary>
    /// <param name="value">The string to parse, e.g. <c>"1.2.3"</c> or <c>"1.2.3-beta.1"</c>.</param>
    /// <param name="version">On success, the parsed version; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> was a valid semantic version;
    ///     otherwise <see langword="false"/>.</returns>
    /// <remarks>
    ///     Does not throw on malformed input — callers enumerating a directory of arbitrary file
    ///     names are expected to skip entries that fail to parse rather than treat them as errors.
    /// </remarks>
    public static bool TryParse(string? value, out PackageVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Split off the optional prerelease suffix (everything after the first '-')
        var dashIndex = value.IndexOf('-');
        var numericPart = dashIndex >= 0 ? value[..dashIndex] : value;
        var prerelease = dashIndex >= 0 ? value[(dashIndex + 1)..] : null;

        // The numeric part must be exactly three dot-separated non-negative integers
        var parts = numericPart.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        // Reject an empty prerelease identifier (e.g. a trailing "-" with nothing after it)
        if (dashIndex >= 0 && string.IsNullOrEmpty(prerelease))
        {
            return false;
        }

        version = new PackageVersion(major, minor, patch, prerelease);
        return true;
    }

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

        // Same major.minor.patch: a release (no prerelease) outranks any prerelease of the
        // same numeric version, per semver precedence rules
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

        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    /// <inheritdoc />
    public bool Equals(PackageVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as PackageVersion);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    /// <inheritdoc />
    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

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
