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
public sealed class PackageVersionCacheTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a cache hit returns the previously-observed result even after the source
    ///     directory has since been deleted, rather than re-enumerating the filesystem.
    /// </summary>
    [Fact]
    public void PackageVersionCache_RepeatedCallSameSource_ReturnsCachedResultWithoutRescanning()
    {
        // Arrange: a source directory with a 2.0.0 package
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-2.0.0.zip");
        var cache = new PackageVersionCache();

        // Act: first call populates the cache, then the directory is deleted, then a second call
        var firstResult = cache.IsNewerVersionAvailable(sourceDir, "contoso-agents", "1.0.0", out var firstLatest);
        Directory.Delete(sourceDir, recursive: true);
        var secondResult = cache.IsNewerVersionAvailable(sourceDir, "contoso-agents", "1.0.0", out var secondLatest);

        // Assert: both calls return the same cached result, and the deleted directory did not
        // cause the second (cached) call to throw
        Assert.True(firstResult);
        Assert.True(secondResult);
        Assert.Equal("2.0.0", firstLatest?.Version.ToString());
        Assert.Equal("2.0.0", secondLatest?.Version.ToString());
    }

    /// <summary>
    ///     Test that a genuinely fresh (never-cached) call against a non-existent source
    ///     directory still throws <see cref="DirectoryNotFoundException"/>, matching
    ///     <see cref="PackageSource"/>'s existing behavior.
    /// </summary>
    [Fact]
    public void PackageVersionCache_UncachedCallMissingDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var missingDir = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_source_" + Guid.NewGuid());
        var cache = new PackageVersionCache();

        // Act / Assert
        Assert.Throws<DirectoryNotFoundException>(
            () => cache.IsNewerVersionAvailable(missingDir, "contoso-agents", "1.0.0", out _));
    }

    /// <summary>
    ///     Test that Invalidate forces the next call to re-enumerate the filesystem, observing a
    ///     package added after the first (now-invalidated) call.
    /// </summary>
    [Fact]
    public void PackageVersionCache_Invalidate_ForcesReEnumeration()
    {
        // Arrange: a source directory with only a 1.0.0 package initially
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var cache = new PackageVersionCache();
        cache.IsNewerVersionAvailable(sourceDir, "contoso-agents", "1.0.0", out var initialLatest);
        Assert.Equal("1.0.0", initialLatest?.Version.ToString());

        // Act: add a newer package, invalidate, then query again
        CreateFile(sourceDir, "contoso-agents-2.0.0.zip");
        cache.Invalidate();
        var result = cache.IsNewerVersionAvailable(sourceDir, "contoso-agents", "1.0.0", out var latest);

        // Assert: the newly-added 2.0.0 package is now observed
        Assert.True(result);
        Assert.Equal("2.0.0", latest?.Version.ToString());
    }

    /// <summary>
    ///     Test that an unparsable pinned version is treated as needing an upgrade, matching
    ///     <see cref="PackageSource.IsNewerVersionAvailable"/>'s documented behavior.
    /// </summary>
    [Fact]
    public void PackageVersionCache_UnparsablePinnedVersion_ReturnsTrue()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var cache = new PackageVersionCache();

        // Act
        var result = cache.IsNewerVersionAvailable(sourceDir, "contoso-agents", "not-a-version", out var latest);

        // Assert
        Assert.True(result);
        Assert.NotNull(latest);
    }

    /// <summary>
    ///     Test that GetPackageNames returns a cached result on a second call even if the source
    ///     directory is deleted in between.
    /// </summary>
    [Fact]
    public void PackageVersionCache_GetPackageNames_RepeatedCall_ReturnsCachedResultWithoutRescanning()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var cache = new PackageVersionCache();

        // Act: first call populates the cache, then the directory is deleted, then a second call
        var firstNames = cache.GetPackageNames(sourceDir);
        Directory.Delete(sourceDir, recursive: true);
        var secondNames = cache.GetPackageNames(sourceDir);

        // Assert: both calls return the same cached result
        Assert.Equal(["contoso-agents"], firstNames);
        Assert.Equal(["contoso-agents"], secondNames);
    }

    /// <summary>
    ///     Test that GetVersionsDescending returns versions in descending order and is cached per
    ///     (sourceDirectory, packageName).
    /// </summary>
    [Fact]
    public void PackageVersionCache_GetVersionsDescending_MultipleVersions_ReturnsDescendingAndCached()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.5.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.4.9.zip");
        var cache = new PackageVersionCache();

        // Act: first call populates the cache, then the directory is deleted, then a second call
        var firstVersions = cache.GetVersionsDescending(sourceDir, "contoso-agents");
        Directory.Delete(sourceDir, recursive: true);
        var secondVersions = cache.GetVersionsDescending(sourceDir, "contoso-agents");

        // Assert: descending order, and the second (cached) call did not need the directory
        Assert.Equal(["2.5.0", "2.4.9", "1.0.0"], firstVersions.Select(p => p.Version.ToString()));
        Assert.Equal(["2.5.0", "2.4.9", "1.0.0"], secondVersions.Select(p => p.Version.ToString()));
    }

    /// <summary>
    ///     Test that Invalidate clears both new caches (GetPackageNames/GetVersionsDescending),
    ///     not just the pre-existing IsNewerVersionAvailable cache.
    /// </summary>
    [Fact]
    public void PackageVersionCache_Invalidate_ClearsPackageNamesAndVersionsDescendingCaches()
    {
        // Arrange: populate all three caches, then add a new package
        var sourceDir = CreateTempDirectory();
        CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
        var cache = new PackageVersionCache();
        cache.GetPackageNames(sourceDir);
        cache.GetVersionsDescending(sourceDir, "contoso-agents");

        CreateFile(sourceDir, "another-package-1.0.0.zip");
        CreateFile(sourceDir, "contoso-agents-2.0.0.zip");

        // Act
        cache.Invalidate();
        var names = cache.GetPackageNames(sourceDir);
        var versions = cache.GetVersionsDescending(sourceDir, "contoso-agents");

        // Assert: the newly-added package/version are now observed
        Assert.Equal(["another-package", "contoso-agents"], names);
        Assert.Equal(["2.0.0", "1.0.0"], versions.Select(p => p.Version.ToString()));
    }

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
