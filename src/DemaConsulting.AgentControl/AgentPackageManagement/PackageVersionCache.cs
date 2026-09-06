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
///     Session-scoped cache decorating <see cref="PackageSource"/>'s version-scanning logic,
///     keyed by <c>(sourceDirectory, packageName)</c>.
/// </summary>
/// <remarks>
///     <para>
///     Per architecture.md's repo-fact caching strategy, the set of available package versions
///     at the configured source is not repo-scoped at all (every repo pinned to the same package
///     name shares the same answer) and may involve slow network/UNC I/O, so it is cached once
///     per app session rather than rescanned per repo-card. <see cref="PackageSource"/> itself is
///     deliberately left untouched (stateless/pure, per its own doc comment) - this class is a
///     decorator, not a modification, constructed once by <c>MainWindowViewModel</c> and shared
///     across every <c>RepoCardViewModel</c> instance for the lifetime of the app session.
///     </para>
///     <para>
///     Not thread-safe; callers are expected to use this only from the UI thread, consistent
///     with every other view-model-adjacent class in this codebase.
///     </para>
/// </remarks>
internal sealed class PackageVersionCache
{
    /// <summary>
    ///     The cached "latest discovered package" result, keyed by source directory and package
    ///     name. A cached <see langword="null"/> value (no package found) is a valid, distinct
    ///     cache entry from "not yet queried".
    /// </summary>
    private readonly Dictionary<(string SourceDirectory, string PackageName), DiscoveredPackage?> _cache = new();

    /// <summary>
    ///     The cached distinct package base names discoverable at a source directory, keyed by
    ///     source directory.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _packageNamesCache = new();

    /// <summary>
    ///     The cached descending-by-version package list for a given <c>(sourceDirectory,
    ///     packageName)</c> pair.
    /// </summary>
    private readonly Dictionary<(string SourceDirectory, string PackageName), IReadOnlyList<DiscoveredPackage>>
        _versionsDescendingCache = new();

    /// <summary>
    ///     Enumerates the distinct package base names discoverable at a source directory,
    ///     consulting (and populating) this session cache instead of re-scanning the filesystem
    ///     on every call.
    /// </summary>
    /// <param name="sourceDirectory">Filesystem directory to scan (local, mapped drive, or UNC).</param>
    /// <returns>The distinct package base names found, ordered by
    ///     <see cref="StringComparer.OrdinalIgnoreCase"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown on a cache miss when
    ///     <paramref name="sourceDirectory"/> does not exist or is unreachable; a cache hit
    ///     returns the previously-observed result instead, even if the source directory has since
    ///     become unreachable.</exception>
    public IReadOnlyList<string> GetPackageNames(string sourceDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);

        if (!_packageNamesCache.TryGetValue(sourceDirectory, out var names))
        {
            names = PackageSource.EnumeratePackageNames(sourceDirectory);
            _packageNamesCache[sourceDirectory] = names;
        }

        return names;
    }

    /// <summary>
    ///     Enumerates a named package's discoverable versions at a source directory, descending
    ///     by version, consulting (and populating) this session cache instead of re-scanning the
    ///     filesystem on every call.
    /// </summary>
    /// <param name="sourceDirectory">Filesystem directory to scan (local, mapped drive, or UNC).</param>
    /// <param name="packageName">The package base name to match.</param>
    /// <returns>The discovered packages matching <paramref name="packageName"/>, ordered from
    ///     highest to lowest version.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/> or
    ///     <paramref name="packageName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageName"/> is empty or
    ///     whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown on a cache miss when
    ///     <paramref name="sourceDirectory"/> does not exist or is unreachable; a cache hit
    ///     returns the previously-observed result instead, even if the source directory has since
    ///     become unreachable.</exception>
    public IReadOnlyList<DiscoveredPackage> GetVersionsDescending(string sourceDirectory, string packageName)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        var key = (sourceDirectory, packageName);
        if (!_versionsDescendingCache.TryGetValue(key, out var versions))
        {
            versions = PackageSource.EnumeratePackages(sourceDirectory, packageName)
                .OrderByDescending(p => p.Version)
                .ToList();
            _versionsDescendingCache[key] = versions;
        }

        return versions;
    }

    /// <summary>
    ///     Determines whether a newer version of a package is available than the version
    ///     currently pinned in a repo, consulting (and populating) this session cache instead of
    ///     re-scanning the filesystem on every call.
    /// </summary>
    /// <param name="sourceDirectory">Filesystem directory to scan (local, mapped drive, or UNC).</param>
    /// <param name="packageName">The package base name to match.</param>
    /// <param name="pinnedVersion">The repo's current pinned semantic version string.</param>
    /// <param name="latest">
    ///     On return, the highest-versioned discovered package (whether or not it is newer than
    ///     <paramref name="pinnedVersion"/>), or <see langword="null"/> if none was found.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> if a discoverable version compares strictly greater than
    ///     <paramref name="pinnedVersion"/>, or if <paramref name="pinnedVersion"/> fails to
    ///     parse; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/>,
    ///     <paramref name="packageName"/>, or <paramref name="pinnedVersion"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageName"/> is empty or
    ///     whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown on a cache miss when
    ///     <paramref name="sourceDirectory"/> does not exist or is unreachable; a cache hit
    ///     returns the previously-observed result instead, even if the source directory has since
    ///     become unreachable.</exception>
    public bool IsNewerVersionAvailable(
        string sourceDirectory,
        string packageName,
        string pinnedVersion,
        out DiscoveredPackage? latest)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(pinnedVersion);

        var key = (sourceDirectory, packageName);
        if (!_cache.TryGetValue(key, out latest))
        {
            latest = PackageSource.FindLatest(sourceDirectory, packageName);
            _cache[key] = latest;
        }

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

    /// <summary>
    ///     Clears every cached entry, forcing the next <see cref="IsNewerVersionAvailable"/> call
    ///     for any source/package pair to re-enumerate the filesystem.
    /// </summary>
    /// <remarks>
    ///     Called when the package-source path setting changes, since a cached result for the old
    ///     path would otherwise be silently (and incorrectly) reused for the new path.
    /// </remarks>
    public void Invalidate()
    {
        _cache.Clear();
        _packageNamesCache.Clear();
        _versionsDescendingCache.Clear();
    }
}
