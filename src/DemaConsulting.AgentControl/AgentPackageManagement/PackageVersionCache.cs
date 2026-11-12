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

using System.Runtime.ExceptionServices;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     The answer to "is a newer package version available than the pinned one?".
/// </summary>
/// <param name="IsNewerAvailable"><see langword="true"/> if the highest discoverable version
///     compares strictly greater than the pinned version, or if the pinned version does not parse
///     (an unparsable pin is treated as needing attention); otherwise <see langword="false"/>,
///     including when no version was found.</param>
/// <param name="Latest">The highest-versioned discovered package whether or not it is newer than
///     the pin, or <see langword="null"/> if none was found.</param>
internal sealed record UpgradeCheck(bool IsNewerAvailable, DiscoveredPackage? Latest);

/// <summary>
///     Session-scoped cache of what each configured package source offers, keyed by
///     <c>(source alias, package ID)</c>.
/// </summary>
/// <remarks>
///     <para>
///     Per architecture.md's repo-fact caching strategy, the set of available package versions at
///     a source is not repo-scoped at all (every repo pinned to the same package at the same source
///     shares the same answer) and may involve slow network I/O, so it is fetched once per app
///     session rather than per repo-card. Entries are keyed by alias, never by location, so two
///     sources that offer the same package ID are cached - and invalidated - independently, and a
///     result fetched from one source is never returned for another. Providers are reached through
///     a <see cref="PackageSourceRegistry"/>, so an unconfigured alias fails with
///     <see cref="UnknownSourceException"/> instead of reading any cached data.
///     </para>
///     <para>
///     A cached empty result (no versions found) is a valid, distinct entry from "not yet queried",
///     and a failed fetch is never cached. <see cref="Invalidate()"/> clears everything, while
///     <see cref="Invalidate(string)"/> clears one source so editing or removing a source does not
///     discard the other sources' results; a fetch that was already in flight when its source was
///     invalidated does not repopulate the cache. Safe for concurrent use; two simultaneous misses
///     for the same key may both fetch, and the later result wins.
///     </para>
/// </remarks>
internal sealed class PackageVersionCache
{
    /// <summary>
    ///     Guards <see cref="_sources"/> and the per-source entries.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The cached data per source alias (case-insensitive).
    /// </summary>
    private readonly Dictionary<string, SourceEntry> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Gets the package names or IDs a source offers, consulting (and populating) the cache.
    /// </summary>
    /// <param name="registry">The registry that resolves <paramref name="sourceAlias"/> to a
    ///     provider on a cache miss.</param>
    /// <param name="sourceAlias">The source alias.</param>
    /// <param name="cancellationToken">Token to abandon a cache-miss fetch.</param>
    /// <returns>The distinct package names, ordered case-insensitively.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceAlias"/> is null,
    ///     empty or whitespace.</exception>
    /// <exception cref="UnknownSourceException">Thrown when the alias is not configured.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown on a cache miss when a folder source
    ///     does not exist or is unreachable; a cache hit returns the previously observed result
    ///     even if the source has since become unreachable.</exception>
    public async Task<IReadOnlyList<string>> GetPackageIdsAsync(
        PackageSourceRegistry registry, string sourceAlias, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);

        var provider = registry.Resolve(sourceAlias);
        var entry = GetOrCreateEntry(sourceAlias);
        lock (_lock)
        {
            if (entry.PackageIds is not null)
            {
                return entry.PackageIds;
            }
        }

        var ids = await provider.ListPackageIdsAsync(cancellationToken);
        lock (_lock)
        {
            entry.PackageIds = ids;
        }

        return ids;
    }

    /// <summary>
    ///     Gets a package's versions at a source, highest first, consulting (and populating) the
    ///     cache.
    /// </summary>
    /// <param name="registry">The registry that resolves <paramref name="sourceAlias"/> to a
    ///     provider on a cache miss.</param>
    /// <param name="sourceAlias">The source alias.</param>
    /// <param name="packageId">The package name or ID.</param>
    /// <param name="cancellationToken">Token to abandon a cache-miss fetch.</param>
    /// <returns>The discovered packages ordered from highest to lowest version.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceAlias"/> or
    ///     <paramref name="packageId"/> is null, empty or whitespace.</exception>
    /// <exception cref="UnknownSourceException">Thrown when the alias is not configured.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown on a cache miss when a folder source
    ///     does not exist or is unreachable.</exception>
    public async Task<IReadOnlyList<DiscoveredPackage>> GetVersionsDescendingAsync(
        PackageSourceRegistry registry, string sourceAlias, string packageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var provider = registry.Resolve(sourceAlias);
        var entry = GetOrCreateEntry(sourceAlias);
        lock (_lock)
        {
            if (entry.Versions.TryGetValue(packageId, out var cached))
            {
                return cached;
            }
        }

        var versions = (await provider.ListVersionsAsync(packageId, cancellationToken))
            .OrderByDescending(package => package.Version)
            .ToList();
        lock (_lock)
        {
            entry.Versions[packageId] = versions;
        }

        return versions;
    }

    /// <summary>
    ///     Determines whether a newer version of a package is available at a source than the version
    ///     currently pinned in a repo, consulting (and populating) the cache.
    /// </summary>
    /// <param name="registry">The registry that resolves <paramref name="sourceAlias"/> to a
    ///     provider on a cache miss.</param>
    /// <param name="sourceAlias">The source alias.</param>
    /// <param name="packageId">The package name or ID.</param>
    /// <param name="pinnedVersion">The repo's current pinned version text.</param>
    /// <param name="cancellationToken">Token to abandon a cache-miss fetch.</param>
    /// <returns>The upgrade check result; see <see cref="UpgradeCheck"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> or
    ///     <paramref name="pinnedVersion"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceAlias"/> or
    ///     <paramref name="packageId"/> is null, empty or whitespace.</exception>
    /// <exception cref="UnknownSourceException">Thrown when the alias is not configured.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown on a cache miss when a folder source
    ///     does not exist or is unreachable.</exception>
    public async Task<UpgradeCheck> IsNewerVersionAvailableAsync(
        PackageSourceRegistry registry,
        string sourceAlias,
        string packageId,
        string pinnedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pinnedVersion);

        var versions = await GetVersionsDescendingAsync(registry, sourceAlias, packageId, cancellationToken);
        return BuildUpgradeCheck(versions, pinnedVersion);
    }

    /// <summary>
    ///     Gets what every configured source offers of a package, in the order the sources are
    ///     configured, consulting (and populating) the cache of each source.
    /// </summary>
    /// <param name="registry">The registry that lists the configured sources and resolves each to a
    ///     provider on a cache miss.</param>
    /// <param name="packageId">The package name or ID.</param>
    /// <param name="excludeAlias">The alias of a source to leave out (the pinned source, already
    ///     consulted on its own), or <see langword="null"/> to include every source.</param>
    /// <param name="cancellationToken">Token to abandon a cache-miss fetch.</param>
    /// <param name="onSourceFailure">Called with the failure of each source that could not be read
    ///     (and was skipped), or <see langword="null"/>. Lets a caller tell "no source offers the
    ///     package" from "a source that might have could not be read".</param>
    /// <returns>The discovered packages source by source in configuration order (the order of
    ///     Settings), each source's versions from highest to lowest; empty when no source offers
    ///     the package. A source that cannot be read is skipped, so one unreachable source never
    ///     hides what the others offer.</returns>
    /// <remarks>
    ///     Asks every configured source what it offers (aliases are user-specific configuration, so
    ///     a pin names none): through the per-source cache, so no source is read twice in a
    ///     session. Whether the content a
    ///     source offers is the content the pin recorded is for the caller to verify - a listing
    ///     says nothing about it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageId"/> is null, empty
    ///     or whitespace.</exception>
    /// <exception cref="IOException">Thrown (as the first failure that occurred, or the first refusal of
    ///     access - HTTP 403, a <see cref="PackageSourceAccessException"/> of kind
    ///     <see cref="PackageSourceFailureKind.AccessDenied"/> - when there was one) only when every
    ///     source that was consulted failed, so a check that learned nothing is not mistaken for
    ///     "no source offers the package"; the same holds for
    ///     <see cref="UnauthorizedAccessException"/> and <see cref="InvalidOperationException"/>,
    ///     which include <see cref="PackageSourceAccessException"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is canceled.</exception>
    public async Task<IReadOnlyList<DiscoveredPackage>> GetVersionsAcrossSourcesAsync(
        PackageSourceRegistry registry,
        string packageId,
        string? excludeAlias,
        CancellationToken cancellationToken,
        Action<Exception>? onSourceFailure = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var offers = new List<DiscoveredPackage>();
        Exception? firstFailure = null;
        Exception? firstAccessDenied = null;
        var consulted = 0;
        var succeeded = 0;
        foreach (var alias in registry.Definitions
                     .Select(definition => definition.Alias)
                     .Where(alias => !string.Equals(alias, excludeAlias, StringComparison.OrdinalIgnoreCase)))
        {
            consulted++;
            try
            {
                offers.AddRange(await GetVersionsDescendingAsync(registry, alias, packageId, cancellationToken));
                succeeded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                firstFailure ??= ex;
                if (ex is PackageSourceAccessException { Kind: PackageSourceFailureKind.AccessDenied })
                {
                    firstAccessDenied ??= ex;
                }

                onSourceFailure?.Invoke(ex);
            }
        }

        // Every consulted source failing means nothing is known: say so instead of reporting an
        // empty offer. A refusal of access (HTTP 403) is the failure to report when there is one: it is
        // the one a person can act on (and carries the server's reason), whatever order the sources failed in
        if (consulted > 0 && succeeded == 0 && firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstAccessDenied ?? firstFailure).Throw();
        }

        return offers;
    }

    /// <summary>
    ///     Determines whether a newer version of a package is available at the configured sources
    ///     than the version currently pinned in a repo, for a pin whose own source is not available.
    /// </summary>
    /// <param name="registry">The registry that lists the configured sources.</param>
    /// <param name="packageId">The package name or ID.</param>
    /// <param name="pinnedVersion">The repo's current pinned version text.</param>
    /// <param name="cancellationToken">Token to abandon a cache-miss fetch.</param>
    /// <returns>The upgrade check result; see <see cref="UpgradeCheck"/>. The newest version
    ///     across the sources wins, and a version offered by several sources is attributed to the
    ///     first of them in configuration order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> or
    ///     <paramref name="pinnedVersion"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="packageId"/> is null, empty
    ///     or whitespace.</exception>
    /// <exception cref="IOException">Thrown when every source that was consulted failed; see
    ///     <see cref="GetVersionsAcrossSourcesAsync"/>.</exception>
    public async Task<UpgradeCheck> IsNewerVersionAvailableAcrossSourcesAsync(
        PackageSourceRegistry registry, string packageId, string pinnedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pinnedVersion);

        var offers = await GetVersionsAcrossSourcesAsync(registry, packageId, excludeAlias: null, cancellationToken);
        return BuildUpgradeCheck(offers, pinnedVersion);
    }

    /// <summary>
    ///     Builds the answer to "is a newer version available?" from a list of offers.
    /// </summary>
    /// <param name="offers">The offered packages in order of preference: when two offers have the
    ///     same version the earlier one is the one reported.</param>
    /// <param name="pinnedVersion">The pinned version text.</param>
    /// <returns>The upgrade check result.</returns>
    private static UpgradeCheck BuildUpgradeCheck(IReadOnlyList<DiscoveredPackage> offers, string pinnedVersion)
    {
        // The newest offer; a strictly greater version is required to replace the current choice,
        // so a tie keeps the earlier (preferred) offer
        var latest = offers.Aggregate(
            (DiscoveredPackage?)null,
            (best, offer) => best is null || offer.Version.CompareTo(best.Version) > 0 ? offer : best);

        if (latest is null)
        {
            return new UpgradeCheck(false, null);
        }

        // An unparsable pin is treated as needing attention rather than silently ignored
        if (!PackageVersion.TryParse(pinnedVersion, out var pinned) || pinned is null)
        {
            return new UpgradeCheck(true, latest);
        }

        return new UpgradeCheck(latest.Version.CompareTo(pinned) > 0, latest);
    }

    /// <summary>
    ///     Clears every cached entry for every source, forcing the next call for any source to
    ///     fetch again.
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _sources.Clear();
        }
    }

    /// <summary>
    ///     Clears the cached entries of one source only, leaving other sources' results in place.
    /// </summary>
    /// <param name="sourceAlias">The alias of the source whose cached results are stale (changed
    ///     or removed); an alias with no cached data is ignored.</param>
    /// <remarks>
    ///     Called when a source is edited or removed, since a cached result for the old definition
    ///     would otherwise be silently (and incorrectly) reused.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceAlias"/> is null,
    ///     empty or whitespace.</exception>
    public void Invalidate(string sourceAlias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);

        lock (_lock)
        {
            _sources.Remove(sourceAlias);
        }
    }

    /// <summary>
    ///     Gets the cache entry of a source, creating it if needed.
    /// </summary>
    /// <param name="sourceAlias">The source alias.</param>
    /// <returns>The entry. A fetch writes its result into the entry it captured here, so a result
    ///     fetched before an invalidation lands in a detached entry and is discarded.</returns>
    private SourceEntry GetOrCreateEntry(string sourceAlias)
    {
        lock (_lock)
        {
            if (!_sources.TryGetValue(sourceAlias, out var entry))
            {
                entry = new SourceEntry();
                _sources[sourceAlias] = entry;
            }

            return entry;
        }
    }

    /// <summary>
    ///     The cached data of one source.
    /// </summary>
    private sealed class SourceEntry
    {
        /// <summary>
        ///     Gets or sets the cached package names, or <see langword="null"/> when not yet
        ///     queried.
        /// </summary>
        public IReadOnlyList<string>? PackageIds { get; set; }

        /// <summary>
        ///     Gets the cached descending version lists by package ID. A present key with an empty
        ///     list means "queried, none found".
        /// </summary>
        public Dictionary<string, IReadOnlyList<DiscoveredPackage>> Versions { get; } = new(StringComparer.Ordinal);
    }
}
