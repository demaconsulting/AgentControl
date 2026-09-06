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

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     Describes an agent package zip file discovered on the configured filesystem package
///     source.
/// </summary>
/// <param name="PackageName">The package base name (the <c>{name}</c> portion of
///     <c>{name}-{version}.zip</c>).</param>
/// <param name="Version">The package's parsed semantic version.</param>
/// <param name="FilePath">Absolute path to the zip file.</param>
internal sealed record DiscoveredPackage(string PackageName, PackageVersion Version, string FilePath);

/// <summary>
///     Enumerates agent package zip files at a configured filesystem source path and resolves
///     whether a newer version than a repo's current pin is available.
/// </summary>
/// <remarks>
///     Per architecture.md, the package source is "a plain filesystem path, not specifically
///     UNC" — it may be a local drive, mapped drive, or UNC path; no authentication, checksum,
///     or signature verification is performed here (that trust boundary belongs to whoever
///     maintains the shared path). Stateless and thread-safe: every member is a pure function of
///     its parameters plus the filesystem at the moment of the call — results can become stale if
///     the source directory changes concurrently.
/// </remarks>
internal static class PackageSource
{
    /// <summary>
    ///     Naming convention for agent package zip files: <c>{packageName}-{semver}.zip</c>.
    /// </summary>
    private const string ZipExtension = ".zip";

    /// <summary>
    ///     Enumerates all discoverable versions of a named package at the given source directory.
    /// </summary>
    /// <param name="sourceDirectory">Filesystem directory to scan (local, mapped drive, or UNC).</param>
    /// <param name="packageName">The package base name to match, e.g. <c>"contoso-agents"</c>.</param>
    /// <returns>
    ///     The discovered packages matching <paramref name="packageName"/>, in no particular
    ///     order. Files that do not match the <c>{packageName}-{semver}.zip</c> naming
    ///     convention (including zips for other package names) are silently skipped, not treated
    ///     as errors — a package source directory is expected to hold arbitrary/unrelated files.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/> or
    ///     <paramref name="packageName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageName"/> is empty or
    ///     whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="sourceDirectory"/>
    ///     does not exist or is unreachable (e.g. an offline UNC path).</exception>
    public static IReadOnlyList<DiscoveredPackage> EnumeratePackages(string sourceDirectory, string packageName)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        var prefix = packageName + "-";
        var results = new List<DiscoveredPackage>();

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*" + ZipExtension))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Only consider files matching this exact package name's prefix; this also
            // naturally rejects zips belonging to other packages that happen to share a
            // common prefix stem (e.g. "contoso-agents-extra-1.0.0.zip" would still match
            // "contoso-agents-extra" as its own package name, not "contoso-agents").
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var versionText = fileName[prefix.Length..];
            if (PackageVersion.TryParse(versionText, out var version) && version is not null)
            {
                results.Add(new DiscoveredPackage(packageName, version, filePath));
            }
        }

        return results;
    }

    /// <summary>
    ///     Finds the highest-versioned discoverable package matching <paramref name="packageName"/>
    ///     at the given source directory.
    /// </summary>
    /// <param name="sourceDirectory">Filesystem directory to scan.</param>
    /// <param name="packageName">The package base name to match.</param>
    /// <returns>The highest-versioned match, or <see langword="null"/> if none was found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/> or
    ///     <paramref name="packageName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageName"/> is empty or
    ///     whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="sourceDirectory"/>
    ///     does not exist or is unreachable.</exception>
    public static DiscoveredPackage? FindLatest(string sourceDirectory, string packageName)
    {
        return EnumeratePackages(sourceDirectory, packageName)
            .OrderByDescending(p => p.Version)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Enumerates the distinct package base names discoverable at a source directory, without
    ///     requiring the caller to already know a package name (unlike <see cref="EnumeratePackages"/>).
    /// </summary>
    /// <param name="sourceDirectory">Filesystem directory to scan (local, mapped drive, or UNC).</param>
    /// <returns>
    ///     The distinct package base names found, ordered by <see cref="StringComparer.OrdinalIgnoreCase"/>
    ///     for deterministic UI display. A source directory with no recognizable package zips
    ///     returns an empty list, not an error.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///     For each <c>*.zip</c> file's base name (<see cref="Path.GetFileNameWithoutExtension(string)"/>),
    ///     this scans the file name's hyphen positions left-to-right; for each hyphen position, it
    ///     tests whether the substring <em>after</em> that hyphen parses via
    ///     <see cref="PackageVersion.TryParse"/>. The first (leftmost) hyphen whose suffix parses
    ///     successfully is the split point: everything before that hyphen is the package name,
    ///     everything after is the version. Scanning a given file stops as soon as one hyphen
    ///     succeeds - ties are therefore impossible by construction. A file with zero valid split
    ///     points (no hyphen's suffix parses as a version) is skipped entirely, mirroring
    ///     <see cref="EnumeratePackages"/>'s existing "unrelated files are silently skipped"
    ///     contract, not treated as an error.
    ///     </para>
    ///     <para>
    ///     This left-to-right rule correctly handles package names that themselves contain
    ///     hyphens (e.g. <c>contoso-agents-extra-1.2.0.zip</c> resolves to name
    ///     <c>contoso-agents-extra</c>, version <c>1.2.0</c>, because the earlier hyphen
    ///     candidates - <c>agents-extra-1.2.0</c>, <c>extra-1.2.0</c> - do not parse as a version
    ///     until the last hyphen is reached). It is a known, accepted "simplicity over precision"
    ///     limitation (per architecture.md) that a package name containing a numeric-looking
    ///     segment that itself happens to parse as a version could still be misclassified - no
    ///     code-level fix is in scope for that edge case.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="sourceDirectory"/>
    ///     does not exist or is unreachable (e.g. an offline UNC path).</exception>
    public static IReadOnlyList<string> EnumeratePackageNames(string sourceDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*" + ZipExtension))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var name = TrySplitNameAndVersion(fileName);
            if (name is not null)
            {
                names.Add(name);
            }
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    ///     Scans a package zip's base file name left-to-right for the leftmost hyphen whose
    ///     suffix parses as a <see cref="PackageVersion"/>, per <see cref="EnumeratePackageNames"/>'s
    ///     documented splitting algorithm.
    /// </summary>
    /// <param name="fileName">The zip file's base name (without extension).</param>
    /// <returns>The package name (the prefix before the winning hyphen), or <see langword="null"/>
    ///     if no hyphen's suffix parses as a valid version.</returns>
    private static string? TrySplitNameAndVersion(string fileName)
    {
        var searchFrom = 0;
        while (true)
        {
            var hyphenIndex = fileName.IndexOf('-', searchFrom);
            if (hyphenIndex < 0)
            {
                return null;
            }

            var suffix = fileName[(hyphenIndex + 1)..];
            if (PackageVersion.TryParse(suffix, out _))
            {
                return fileName[..hyphenIndex];
            }

            searchFrom = hyphenIndex + 1;
        }
    }

    /// <summary>
    ///     Determines whether a newer version of a package is available than the version
    ///     currently pinned in a repo.
    /// </summary>
    /// <param name="sourceDirectory">Filesystem directory to scan.</param>
    /// <param name="packageName">The package base name to match.</param>
    /// <param name="pinnedVersion">The repo's current pinned semantic version string.</param>
    /// <param name="latest">
    ///     On return, the highest-versioned discovered package (whether or not it is newer than
    ///     <paramref name="pinnedVersion"/>), or <see langword="null"/> if none was found.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> if a discoverable version compares strictly greater than
    ///     <paramref name="pinnedVersion"/> (per <see cref="PackageVersion.CompareTo"/>), or if
    ///     <paramref name="pinnedVersion"/> fails to parse (an unparsable pin is treated as
    ///     needing attention rather than silently ignored); otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/>,
    ///     <paramref name="packageName"/>, or <paramref name="pinnedVersion"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageName"/> is empty or
    ///     whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="sourceDirectory"/>
    ///     does not exist or is unreachable.</exception>
    public static bool IsNewerVersionAvailable(
        string sourceDirectory,
        string packageName,
        string pinnedVersion,
        out DiscoveredPackage? latest)
    {
        ArgumentNullException.ThrowIfNull(pinnedVersion);

        latest = FindLatest(sourceDirectory, packageName);
        if (latest is null)
        {
            return false;
        }

        if (!PackageVersion.TryParse(pinnedVersion, out var pinned) || pinned is null)
        {
            return true;
        }

        return latest.Version.CompareTo(pinned) > 0;
    }
}
