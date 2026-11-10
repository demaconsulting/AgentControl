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

using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="PackageVersionCache"/>.
/// </summary>
/// <remarks>
///     Most tests use real zip-folder sources over real temporary directories (so staleness after
///     deleting the directory proves a cache hit); the multi-source tests use
///     <see cref="FakePackageSourceProvider"/> so provider call counts can be asserted.
/// </remarks>
public sealed class PackageVersionCacheTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that the upgrade check carries the discovered package of the pinned version from the same
    ///     listing - with its revision - so a caller can see that a tag was moved without another call.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_IsNewerVersionAvailable_PinnedVersionListed_ReturnsPinnedPackageWithRevision()
    {
        // Arrange: 1.0.0 (pinned) and 1.1.0, each with a revision
        var provider = new FakePackageSourceProvider("Repo", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        provider.Revisions["1.0.0"] = new string('a', 40);
        provider.Revisions["1.1.0"] = new string('b', 40);
        var registry = CreateRegistry(provider);
        var cache = new PackageVersionCache();

        // Act
        var check = await cache.IsNewerVersionAvailableAsync(
            registry, "Repo", "contoso-agents", "1.0.0", CancellationToken.None);

        // Assert: the latest and the pinned entries each carry their revision, from one listing call
        Assert.True(check.IsNewerAvailable);
        Assert.Equal("1.1.0", check.Latest?.Version.ToString());
        Assert.Equal("1.0.0", check.Pinned?.Version.ToString());
        Assert.Equal(new string('a', 40), check.Pinned?.Revision);
        Assert.Equal(new string('b', 40), check.Latest?.Revision);
        Assert.Equal(1, provider.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that a pinned version the source does not list gives no pinned entry, while the latest
    ///     version is still reported.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_IsNewerVersionAvailable_PinnedVersionNotListed_ReturnsNoPinnedPackage()
    {
        // Arrange: a source that lists 1.0.0 and 1.1.0, and a pin to 0.9.0
        var provider = new FakePackageSourceProvider("Repo", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var registry = CreateRegistry(provider);
        var cache = new PackageVersionCache();

        // Act
        var check = await cache.IsNewerVersionAvailableAsync(
            registry, "Repo", "contoso-agents", "0.9.0", CancellationToken.None);

        // Assert
        Assert.Null(check.Pinned);
        Assert.True(check.IsNewerAvailable);
        Assert.Equal("1.1.0", check.Latest?.Version.ToString());
    }

    /// <summary>
    ///     Test that a pin that does not parse as a version gives no pinned entry.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_IsNewerVersionAvailable_PinDoesNotParse_ReturnsNoPinnedPackage()
    {
        // Arrange: a source that lists two versions, and a pin that is not a version
        var provider = new FakePackageSourceProvider("Repo", ("contoso-agents", "1.0.0"), ("contoso-agents", "1.1.0"));
        var registry = CreateRegistry(provider);
        var cache = new PackageVersionCache();

        // Act
        var check = await cache.IsNewerVersionAvailableAsync(
            registry, "Repo", "contoso-agents", "not-a-version", CancellationToken.None);

        // Assert
        Assert.Null(check.Pinned);
    }

    /// <summary>
    ///     Test that a cache hit returns the previously-observed result even after the source
    ///     directory has since been deleted, rather than re-enumerating the filesystem.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_RepeatedCallSameSource_ReturnsCachedResultWithoutRescanning()
    {
        // Arrange: a source directory with a 2.0.0 package
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-2.0.0.zip");
        var registry = CreateRegistry(sourceDir);
        var cache = new PackageVersionCache();

        // Act: first call populates the cache, then the directory is deleted, then a second call
        var first = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.0.0", CancellationToken.None);
        Directory.Delete(sourceDir, recursive: true);
        var second = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.0.0", CancellationToken.None);

        // Assert: both calls return the same cached result, and the deleted directory did not
        // cause the second (cached) call to throw
        Assert.True(first.IsNewerAvailable);
        Assert.True(second.IsNewerAvailable);
        Assert.Equal("2.0.0", first.Latest?.Version.ToString());
        Assert.Equal("2.0.0", second.Latest?.Version.ToString());
    }

    /// <summary>
    ///     Test that a genuinely fresh (never-cached) call against a non-existent source
    ///     directory still throws <see cref="DirectoryNotFoundException"/>, matching
    ///     <see cref="PackageSource"/>'s existing behavior.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_UncachedCallMissingDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var missingDir = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_source_" + Guid.NewGuid());
        var registry = CreateRegistry(missingDir);
        var cache = new PackageVersionCache();

        // Act / Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.0.0", CancellationToken.None));
    }

    /// <summary>
    ///     Test that Invalidate forces the next call to re-enumerate the filesystem, observing a
    ///     package added after the first (now-invalidated) call.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_Invalidate_ForcesReEnumeration()
    {
        // Arrange: a source directory with only a 1.0.0 package initially
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var registry = CreateRegistry(sourceDir);
        var cache = new PackageVersionCache();
        var initial = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.0.0", CancellationToken.None);
        Assert.Equal("1.0.0", initial.Latest?.Version.ToString());

        // Act: add a newer package, invalidate, then query again
        CreateFile(sourceDir, "contoso-agents-2.0.0.zip");
        cache.Invalidate();
        var result = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.0.0", CancellationToken.None);

        // Assert: the newly-added 2.0.0 package is now observed
        Assert.True(result.IsNewerAvailable);
        Assert.Equal("2.0.0", result.Latest?.Version.ToString());
    }

    /// <summary>
    ///     Test that an unparsable pinned version is treated as needing an upgrade, matching
    ///     <see cref="PackageSource.IsNewerVersionAvailable"/>'s documented behavior.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_UnparsablePinnedVersion_ReturnsTrue()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var registry = CreateRegistry(sourceDir);
        var cache = new PackageVersionCache();

        // Act
        var result = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "not-a-version", CancellationToken.None);

        // Assert
        Assert.True(result.IsNewerAvailable);
        Assert.NotNull(result.Latest);
    }

    /// <summary>
    ///     Test that a pin that is a four-part version or carries build metadata is compared by
    ///     version (metadata ignored) rather than being treated as unparsable.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_PinWithRevisionOrMetadata_ComparedByVersionNotTreatedAsUnparsable()
    {
        // Arrange: a source offering 1.2.3
        var registry = CreateRegistry(
            new FakePackageSourceProvider("Default", ("contoso-agents", "1.2.3")));
        var cache = new PackageVersionCache();

        // Act
        var revisionAhead = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.2.3.1", CancellationToken.None);
        var metadataEqual = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.2.3+build7", CancellationToken.None);

        // Assert: 1.2.3.1 is ahead of 1.2.3, and 1.2.3+build7 is the same version as 1.2.3
        Assert.False(revisionAhead.IsNewerAvailable);
        Assert.False(metadataEqual.IsNewerAvailable);
    }

    /// <summary>
    ///     Test that no packages found at the source is a definite "no upgrade" answer with no
    ///     latest package.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_NoPackagesFound_ReturnsNoUpgradeAndNoLatest()
    {
        // Arrange: an empty source
        var registry = CreateRegistry(CreateTempDirectory());
        var cache = new PackageVersionCache();

        // Act
        var result = await cache.IsNewerVersionAvailableAsync(
            registry, "Default", "contoso-agents", "1.0.0", CancellationToken.None);

        // Assert
        Assert.False(result.IsNewerAvailable);
        Assert.Null(result.Latest);
    }

    /// <summary>
    ///     Test that GetPackageIdsAsync returns a cached result on a second call even if the
    ///     source directory is deleted in between.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_GetPackageIds_RepeatedCall_ReturnsCachedResultWithoutRescanning()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var registry = CreateRegistry(sourceDir);
        var cache = new PackageVersionCache();

        // Act: first call populates the cache, then the directory is deleted, then a second call
        var firstNames = await cache.GetPackageIdsAsync(registry, "Default", CancellationToken.None);
        Directory.Delete(sourceDir, recursive: true);
        var secondNames = await cache.GetPackageIdsAsync(registry, "Default", CancellationToken.None);

        // Assert: both calls return the same cached result
        Assert.Equal(["contoso-agents"], firstNames);
        Assert.Equal(["contoso-agents"], secondNames);
    }

    /// <summary>
    ///     Test that GetVersionsDescendingAsync returns versions in descending order and is cached
    ///     per (source alias, package ID).
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_GetVersionsDescending_MultipleVersions_ReturnsDescendingAndCached()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.5.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.4.9.zip");
        var registry = CreateRegistry(sourceDir);
        var cache = new PackageVersionCache();

        // Act: first call populates the cache, then the directory is deleted, then a second call
        var firstVersions = await cache.GetVersionsDescendingAsync(
            registry, "Default", "contoso-agents", CancellationToken.None);
        Directory.Delete(sourceDir, recursive: true);
        var secondVersions = await cache.GetVersionsDescendingAsync(
            registry, "Default", "contoso-agents", CancellationToken.None);

        // Assert: descending order, and the second (cached) call did not need the directory
        Assert.Equal(["2.5.0", "2.4.9", "1.0.0"], firstVersions.Select(p => p.Version.ToString()));
        Assert.Equal(["2.5.0", "2.4.9", "1.0.0"], secondVersions.Select(p => p.Version.ToString()));
    }

    /// <summary>
    ///     Test that Invalidate clears both caches (package IDs and versions), not just the
    ///     upgrade-check result.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_Invalidate_ClearsPackageNamesAndVersionsDescendingCaches()
    {
        // Arrange: populate both caches, then add a new package
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var registry = CreateRegistry(sourceDir);
        var cache = new PackageVersionCache();
        await cache.GetPackageIdsAsync(registry, "Default", CancellationToken.None);
        await cache.GetVersionsDescendingAsync(registry, "Default", "contoso-agents", CancellationToken.None);

        CreateFile(sourceDir, "another-package-1.0.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.0.0.zip");

        // Act
        cache.Invalidate();
        var names = await cache.GetPackageIdsAsync(registry, "Default", CancellationToken.None);
        var versions = await cache.GetVersionsDescendingAsync(
            registry, "Default", "contoso-agents", CancellationToken.None);

        // Assert: the newly-added package/version are now observed
        Assert.Equal(["another-package", "contoso-agents"], names);
        Assert.Equal(["2.0.0", "1.0.0"], versions.Select(p => p.Version.ToString()));
    }

    /// <summary>
    ///     Test that the same package ID at two sources is cached separately per source alias, so
    ///     a result fetched from one source is never returned for the other.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_SamePackageIdInTwoSources_CachedPerSourceAlias()
    {
        // Arrange: two sources offering the same package ID with different versions
        var sourceA = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var sourceB = new FakePackageSourceProvider("B", ("contoso-agents", "9.0.0"));
        var registry = CreateRegistry(sourceA, sourceB);
        var cache = new PackageVersionCache();

        // Act: query both sources twice each
        var a1 = await cache.GetVersionsDescendingAsync(registry, "A", "contoso-agents", CancellationToken.None);
        var b1 = await cache.GetVersionsDescendingAsync(registry, "B", "contoso-agents", CancellationToken.None);
        var a2 = await cache.GetVersionsDescendingAsync(registry, "A", "contoso-agents", CancellationToken.None);
        var b2 = await cache.GetVersionsDescendingAsync(registry, "B", "contoso-agents", CancellationToken.None);

        // Assert: each source returns its own version, and each was queried exactly once
        Assert.Equal("1.0.0", a1.Single().Version.ToString());
        Assert.Equal("9.0.0", b1.Single().Version.ToString());
        Assert.Equal("1.0.0", a2.Single().Version.ToString());
        Assert.Equal("9.0.0", b2.Single().Version.ToString());
        Assert.Equal(1, sourceA.ListVersionsCalls);
        Assert.Equal(1, sourceB.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that invalidating one source leaves the other source's cached results in place and
    ///     forces only the invalidated source to be queried again.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_InvalidateSingleSource_LeavesOtherSourcesCached()
    {
        // Arrange: two sources, both queried once
        var sourceA = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var sourceB = new FakePackageSourceProvider("B", ("contoso-agents", "9.0.0"));
        var registry = CreateRegistry(sourceA, sourceB);
        var cache = new PackageVersionCache();
        await cache.GetVersionsDescendingAsync(registry, "A", "contoso-agents", CancellationToken.None);
        await cache.GetVersionsDescendingAsync(registry, "B", "contoso-agents", CancellationToken.None);

        // Act: invalidate source A (alias compared case-insensitively), then query both again
        cache.Invalidate("a");
        await cache.GetVersionsDescendingAsync(registry, "A", "contoso-agents", CancellationToken.None);
        await cache.GetVersionsDescendingAsync(registry, "B", "contoso-agents", CancellationToken.None);

        // Assert: only A was queried again
        Assert.Equal(2, sourceA.ListVersionsCalls);
        Assert.Equal(1, sourceB.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that a fetch already in flight when its source is invalidated does not repopulate
    ///     the cache with its (stale) result.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_InvalidateDuringInFlightFetch_DoesNotCacheStaleResult()
    {
        // Arrange: a source whose version listing is held open
        var gate = new TaskCompletionSource();
        var source = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0")) { ListVersionsGate = gate.Task };
        var registry = CreateRegistry(source);
        var cache = new PackageVersionCache();
        var inFlight = cache.GetVersionsDescendingAsync(registry, "A", "contoso-agents", CancellationToken.None);

        // Act: invalidate while the fetch is in flight, release it, then query again
        cache.Invalidate("A");
        gate.SetResult();
        await inFlight;
        await cache.GetVersionsDescendingAsync(registry, "A", "contoso-agents", CancellationToken.None);

        // Assert: the in-flight result was discarded, so the second query had to fetch again
        Assert.Equal(2, source.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that asking the cache about an alias that is not configured fails with
    ///     <see cref="UnknownSourceException"/> and never consults any other source.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_UnknownAlias_ThrowsUnknownSourceExceptionWithoutContactingOtherSources()
    {
        // Arrange: only source A is configured
        var sourceA = new FakePackageSourceProvider("A", ("contoso-agents", "1.0.0"));
        var registry = CreateRegistry(sourceA);
        var cache = new PackageVersionCache();

        // Act / Assert
        await Assert.ThrowsAsync<UnknownSourceException>(() => cache.GetVersionsDescendingAsync(
            registry, "B", "contoso-agents", CancellationToken.None));
        Assert.Equal(0, sourceA.TotalCalls);
    }

    /// <summary>
    ///     Test that the cross-source listing returns every source's offers in the order the sources are
    ///     configured (each source's versions highest first), leaves out the excluded alias without
    ///     contacting it, and reads each source once however often it is asked.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_GetVersionsAcrossSources_ReturnsOffersInConfigurationOrderAndExcludesAlias()
    {
        // Arrange: three sources, the first of which is the one to leave out
        var pinned = new FakePackageSourceProvider("Pinned", ("contoso-agents", "1.0.0"));
        var first = new FakePackageSourceProvider("First", ("contoso-agents", "1.0.0"), ("contoso-agents", "2.0.0"));
        var second = new FakePackageSourceProvider("Second", ("contoso-agents", "1.5.0"), ("other-agents", "9.0.0"));
        var registry = CreateRegistry(pinned, first, second);
        var cache = new PackageVersionCache();

        // Act: asked twice
        var offers = await cache.GetVersionsAcrossSourcesAsync(registry, "contoso-agents", "pinned", CancellationToken.None);
        await cache.GetVersionsAcrossSourcesAsync(registry, "contoso-agents", "pinned", CancellationToken.None);

        // Assert: configuration order, highest first within a source, no contact with the excluded source
        Assert.Equal(
            ["First 2.0.0", "First 1.0.0", "Second 1.5.0"],
            offers.Select(offer => $"{offer.SourceAlias} {offer.Version}"));
        Assert.Equal(0, pinned.TotalCalls);
        Assert.Equal(1, first.ListVersionsCalls);
        Assert.Equal(1, second.ListVersionsCalls);
    }

    /// <summary>
    ///     Test that a source that cannot be read is skipped, so one unreachable source never hides what the
    ///     others offer.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_GetVersionsAcrossSources_OneSourceFails_ReturnsTheOthersOffers()
    {
        // Arrange: the first source is unreachable
        var broken = new FakePackageSourceProvider("Broken", ("contoso-agents", "1.0.0"))
        {
            ListVersionsException = new IOException("unreachable")
        };
        var working = new FakePackageSourceProvider("Working", ("contoso-agents", "1.0.0"));
        var registry = CreateRegistry(broken, working);
        var cache = new PackageVersionCache();

        // Act
        var offers = await cache.GetVersionsAcrossSourcesAsync(registry, "contoso-agents", null, CancellationToken.None);

        // Assert
        Assert.Equal("Working", Assert.Single(offers).SourceAlias);
    }

    /// <summary>
    ///     Test that when every consulted source fails the failure is reported instead of an empty answer,
    ///     so a check that learned nothing is not mistaken for "nobody offers the package".
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_GetVersionsAcrossSources_EverySourceFails_ThrowsTheFirstFailure()
    {
        // Arrange: both sources fail
        var one = new FakePackageSourceProvider("One") { ListVersionsException = new IOException("first failure") };
        var two = new FakePackageSourceProvider("Two") { ListVersionsException = new InvalidOperationException("second failure") };
        var registry = CreateRegistry(one, two);
        var cache = new PackageVersionCache();

        // Act / Assert
        var failure = await Assert.ThrowsAsync<IOException>(
            () => cache.GetVersionsAcrossSourcesAsync(registry, "contoso-agents", null, CancellationToken.None));
        Assert.Equal("first failure", failure.Message);
    }

    /// <summary>
    ///     Test that the cross-source upgrade check reports the newest version across the sources, that a
    ///     version offered by several sources is attributed to the first of them in configuration order, and
    ///     that it carries the entry of the pinned version.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_IsNewerVersionAvailableAcrossSources_NewestWinsAndTieKeepsConfigurationOrder()
    {
        // Arrange: 3.0.0 is offered by both the second and third source, 2.0.0 by the first
        var first = new FakePackageSourceProvider("First", ("contoso-agents", "1.0.0"), ("contoso-agents", "2.0.0"));
        var second = new FakePackageSourceProvider("Second", ("contoso-agents", "3.0.0"));
        var third = new FakePackageSourceProvider("Third", ("contoso-agents", "3.0.0"), ("contoso-agents", "1.0.0"));
        var registry = CreateRegistry(first, second, third);
        var cache = new PackageVersionCache();

        // Act
        var check = await cache.IsNewerVersionAvailableAcrossSourcesAsync(
            registry, "contoso-agents", "1.0.0", CancellationToken.None);

        // Assert
        Assert.True(check.IsNewerAvailable);
        Assert.Equal("3.0.0", check.Latest?.Version.ToString());
        Assert.Equal("Second", check.Latest?.SourceAlias);
        Assert.Equal("First", check.Pinned?.SourceAlias);
    }

    /// <summary>
    ///     Test that the cross-source upgrade check reports no upgrade, and no latest version, when no
    ///     source offers the package.
    /// </summary>
    [Fact]
    public async Task PackageVersionCache_IsNewerVersionAvailableAcrossSources_NobodyOffersPackage_ReportsNoUpgrade()
    {
        // Arrange
        var registry = CreateRegistry(new FakePackageSourceProvider("Only", ("other-agents", "1.0.0")));
        var cache = new PackageVersionCache();

        // Act
        var check = await cache.IsNewerVersionAvailableAcrossSourcesAsync(
            registry, "contoso-agents", "1.0.0", CancellationToken.None);

        // Assert
        Assert.False(check.IsNewerAvailable);
        Assert.Null(check.Latest);
    }

    /// <summary>
    ///     Creates a registry with a single zip-folder source named <c>Default</c> at a directory.
    /// </summary>
    private static PackageSourceRegistry CreateRegistry(string sourceDir) =>
        PackageSourceRegistry.Create([PackageSourceDefinition.CreateDefaultZip(sourceDir)]);

    /// <summary>
    ///     Creates a registry that serves the given fake providers by their aliases.
    /// </summary>
    private static PackageSourceRegistry CreateRegistry(params FakePackageSourceProvider[] providers) =>
        new(
            providers.Select(p => new PackageSourceDefinition { Alias = p.Alias, Type = PackageSourceType.Zip, Location = "unused" }),
            definition => providers.Single(p => p.Alias == definition.Alias));

    /// <summary>
    ///     Creates a unique temporary directory tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_pkg_version_cache_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    ///     Creates an empty file with the given name inside a directory.
    /// </summary>
    private static void CreateFile(string directory, string fileName)
    {
        File.WriteAllText(Path.Combine(directory, fileName), string.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var path in _tempPaths.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; leftover temp directories do not fail the test.
            }
        }
    }
}
