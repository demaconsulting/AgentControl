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
///     Unit tests for <see cref="PackageSource"/>.
/// </summary>
public class PackageSourceTests
{
    /// <summary>
    ///     Test that enumerating packages finds only zip files matching the requested package
    ///     name's naming convention.
    /// </summary>
    [Fact]
    public void PackageSource_EnumeratePackages_MixedDirectory_FindsOnlyMatchingPackage()
    {
        // Arrange: a source directory with the target package, another package, and unrelated files
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
            CreateFile(sourceDir, "contoso-agents-1.1.0.zip");
            CreateFile(sourceDir, "other-package-9.9.9.zip");
            CreateFile(sourceDir, "release-notes.md");

            // Act: enumerate packages for "contoso-agents"
            var discovered = PackageSource.EnumeratePackages(sourceDir, "contoso-agents");

            // Assert: only the two matching zips are found
            Assert.Equal(2, discovered.Count);
            Assert.All(discovered, p => Assert.Equal("contoso-agents", p.PackageName));
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindLatest returns the highest-versioned match.
    /// </summary>
    [Fact]
    public void PackageSource_FindLatest_MultipleVersions_ReturnsHighest()
    {
        // Arrange: three versions of the same package
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
            CreateFile(sourceDir, "contoso-agents-2.5.0.zip");
            CreateFile(sourceDir, "contoso-agents-2.4.9.zip");

            // Act: find the latest version
            var latest = PackageSource.FindLatest(sourceDir, "contoso-agents");

            // Assert: the highest version (2.5.0) is returned
            Assert.NotNull(latest);
            Assert.Equal("2.5.0", latest.Version.ToString());
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that FindLatest returns null when no matching package exists.
    /// </summary>
    [Fact]
    public void PackageSource_FindLatest_NoMatchingPackage_ReturnsNull()
    {
        // Arrange: an empty source directory
        var sourceDir = CreateTempDirectory();
        try
        {
            // Act: find the latest version of a package that does not exist
            var latest = PackageSource.FindLatest(sourceDir, "contoso-agents");

            // Assert: no match means null, not an exception
            Assert.Null(latest);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a newer version on the source is detected as available.
    /// </summary>
    [Fact]
    public void PackageSource_IsNewerVersionAvailable_SourceHasNewerVersion_ReturnsTrue()
    {
        // Arrange: source has a newer version than the repo's current pin
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
            CreateFile(sourceDir, "contoso-agents-1.1.0.zip");

            // Act: check for an upgrade from 1.0.0
            var isNewer = PackageSource.IsNewerVersionAvailable(sourceDir, "contoso-agents", "1.0.0", out var latest);

            // Assert: a newer version (1.1.0) is reported
            Assert.True(isNewer);
            Assert.NotNull(latest);
            Assert.Equal("1.1.0", latest.Version.ToString());
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a pin already at the highest available version reports no upgrade.
    /// </summary>
    [Fact]
    public void PackageSource_IsNewerVersionAvailable_PinIsCurrent_ReturnsFalse()
    {
        // Arrange: source's only version matches the pin exactly
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "contoso-agents-1.0.0.zip");

            // Act: check for an upgrade from the same version
            var isNewer = PackageSource.IsNewerVersionAvailable(sourceDir, "contoso-agents", "1.0.0", out var latest);

            // Assert: no upgrade is available
            Assert.False(isNewer);
            Assert.NotNull(latest);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that no discoverable package at all reports no upgrade available.
    /// </summary>
    [Fact]
    public void PackageSource_IsNewerVersionAvailable_NoPackagesFound_ReturnsFalse()
    {
        // Arrange: an empty source directory
        var sourceDir = CreateTempDirectory();
        try
        {
            // Act: check for an upgrade with nothing on the source
            var isNewer = PackageSource.IsNewerVersionAvailable(sourceDir, "contoso-agents", "1.0.0", out var latest);

            // Assert: nothing found means no upgrade, not an exception
            Assert.False(isNewer);
            Assert.Null(latest);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that an unparsable pinned version is treated as needing an upgrade when any
    ///     package is discoverable.
    /// </summary>
    [Fact]
    public void PackageSource_IsNewerVersionAvailable_UnparsablePin_ReturnsTrue()
    {
        // Arrange: source has a package, but the recorded pin is not a valid semver string
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "contoso-agents-1.0.0.zip");

            // Act: check for an upgrade against a malformed pin
            var isNewer = PackageSource.IsNewerVersionAvailable(sourceDir, "contoso-agents", "not-a-version", out var latest);

            // Assert: an unparsable pin is treated conservatively as needing attention
            Assert.True(isNewer);
            Assert.NotNull(latest);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that enumerating with a non-existent source directory throws a
    ///     DirectoryNotFoundException.
    /// </summary>
    [Fact]
    public void PackageSource_EnumeratePackages_DirectoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange: a path that does not exist
        var missingDir = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_" + Guid.NewGuid());

        // Act / Assert: a missing source directory is surfaced as an exception
        Assert.Throws<DirectoryNotFoundException>(() => PackageSource.EnumeratePackages(missingDir, "contoso-agents"));
    }

    /// <summary>
    ///     Test that EnumeratePackageNames finds distinct, sorted names across multiple packages,
    ///     collapsing multiple versions of the same name into one entry.
    /// </summary>
    [Fact]
    public void PackageSource_EnumeratePackageNames_MixedDirectory_ReturnsDistinctSortedNames()
    {
        // Arrange: two versions of one package, one version of another, plus an unrelated file
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "contoso-agents-1.0.0.zip");
            CreateFile(sourceDir, "contoso-agents-2.0.0.zip");
            CreateFile(sourceDir, "other-package-9.9.9.zip");
            CreateFile(sourceDir, "release-notes.md");

            // Act
            var names = PackageSource.EnumeratePackageNames(sourceDir);

            // Assert: distinct, sorted names
            Assert.Equal(["contoso-agents", "other-package"], names);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that the leftmost-parseable-hyphen rule correctly splits a package name that
    ///     itself contains a hyphen, walking past earlier hyphen candidates that do not parse as
    ///     a version.
    /// </summary>
    [Fact]
    public void PackageSource_EnumeratePackageNames_HyphenatedPackageName_SplitsAtLeftmostParseableHyphen()
    {
        // Arrange: a package whose own name segment contains a hyphen
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "contoso-agents-extra-1.2.0.zip");

            // Act
            var names = PackageSource.EnumeratePackageNames(sourceDir);

            // Assert: the algorithm walks past "agents-1" (not a valid version suffix) all the
            // way to "1.2.0", so the full "contoso-agents-extra" is recognized as the name
            Assert.Equal(["contoso-agents-extra"], names);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a file with no valid hyphen split point (no suffix after any hyphen parses
    ///     as a version) is skipped entirely, contributing no name.
    /// </summary>
    [Fact]
    public void PackageSource_EnumeratePackageNames_NoValidSplitPoint_SkipsFile()
    {
        // Arrange: a file with no hyphen at all, and one with a trailing empty prerelease
        var sourceDir = CreateTempDirectory();
        try
        {
            CreateFile(sourceDir, "readme.zip");
            CreateFile(sourceDir, "only-prerelease-.zip");

            // Act
            var names = PackageSource.EnumeratePackageNames(sourceDir);

            // Assert: neither file contributes a name
            Assert.Empty(names);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that an empty/non-matching directory returns an empty list rather than an error.
    /// </summary>
    [Fact]
    public void PackageSource_EnumeratePackageNames_EmptyDirectory_ReturnsEmptyList()
    {
        // Arrange
        var sourceDir = CreateTempDirectory();
        try
        {
            // Act
            var names = PackageSource.EnumeratePackageNames(sourceDir);

            // Assert
            Assert.Empty(names);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that enumerating package names against a non-existent source directory throws a
    ///     DirectoryNotFoundException, mirroring EnumeratePackages' existing behavior.
    /// </summary>
    [Fact]
    public void PackageSource_EnumeratePackageNames_DirectoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var missingDir = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_" + Guid.NewGuid());

        // Act / Assert
        Assert.Throws<DirectoryNotFoundException>(() => PackageSource.EnumeratePackageNames(missingDir));
    }

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_package_source_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     Creates an empty file with the given name inside a directory.
    /// </summary>
    /// <param name="directory">The containing directory.</param>
    /// <param name="fileName">The file name to create.</param>
    private static void CreateFile(string directory, string fileName)
    {
        File.WriteAllBytes(Path.Combine(directory, fileName), []);
    }
}
