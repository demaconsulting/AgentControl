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

using System.IO.Compression;
using DemaConsulting.AgentControl.AgentPackageManagement;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="ZipFolderSourceProvider"/>.
/// </summary>
/// <remarks>
///     Uses real temporary folders of real zip files; the scanning rules themselves are covered
///     by <c>PackageSourceTests</c>, so these tests focus on what the provider adds: the alias,
///     the provider contract, and the checks on what may be acquired.
/// </remarks>
public sealed class ZipFolderSourceProviderTests : IDisposable
{
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that listing versions returns only the matching package's zips, each stamped with
    ///     the source alias and the zip path.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_ListVersions_MixedFolder_ReturnsMatchingPackagesStampedWithAlias()
    {
        // Arrange: two versions of one package, another package, and an unrelated file
        var folder = CreateTempDirectory();
        CreateZip(folder, "contoso-agents-1.0.0.zip");
        CreateZip(folder, "contoso-agents-2.0.0-beta.1.zip");
        CreateZip(folder, "other-package-3.0.0.zip");
        await File.WriteAllTextAsync(
            Path.Combine(folder, "readme.txt"), "not a package", TestContext.Current.CancellationToken);
        var provider = CreateProvider("Zips", folder);

        // Act
        var versions = await provider.ListVersionsAsync("contoso-agents", CancellationToken.None);

        // Assert
        Assert.Equal(
            ["1.0.0", "2.0.0-beta.1"],
            versions.OrderBy(p => p.Version).Select(p => p.Version.ToString()));
        Assert.All(versions, package =>
        {
            Assert.Equal("Zips", package.SourceAlias);
            Assert.Equal("contoso-agents", package.PackageName);
            Assert.True(File.Exists(package.Location));
        });
    }

    /// <summary>
    ///     Test that zip names with a fourth version segment or build metadata are still not
    ///     recognized as packages (the zip file-name rules are unchanged).
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_ListVersions_ExtendedVersionFileNames_AreSkipped()
    {
        // Arrange
        var folder = CreateTempDirectory();
        CreateZip(folder, "contoso-agents-1.2.3.4.zip");
        CreateZip(folder, "contoso-agents-1.2.3+build.zip");
        CreateZip(folder, "contoso-agents-1.2.3.zip");
        var provider = CreateProvider("Zips", folder);

        // Act
        var versions = await provider.ListVersionsAsync("contoso-agents", CancellationToken.None);

        // Assert: only the strict three-part name is a package
        Assert.Equal("1.2.3", Assert.Single(versions).Version.ToString());
    }

    /// <summary>
    ///     Test that listing package IDs returns the distinct, sorted package names found.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_ListPackageIds_MixedFolder_ReturnsDistinctSortedNames()
    {
        // Arrange
        var folder = CreateTempDirectory();
        CreateZip(folder, "zeta-1.0.0.zip");
        CreateZip(folder, "alpha-agents-1.0.0.zip");
        CreateZip(folder, "alpha-agents-2.0.0.zip");
        var provider = CreateProvider("Zips", folder);

        // Act
        var ids = await provider.ListPackageIdsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["alpha-agents", "zeta"], ids);
    }

    /// <summary>
    ///     Test that a source folder that does not exist is reported as a missing folder by both
    ///     listing operations.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_List_FolderDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_zip_source_" + Guid.NewGuid());
        var provider = CreateProvider("Zips", missing);

        // Act / Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.ListPackageIdsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => provider.ListVersionsAsync("contoso-agents", CancellationToken.None));
    }

    /// <summary>
    ///     Test that acquiring a discovered package returns a reader over the zip's root, exposing
    ///     its managed files and root-level release notes.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_DiscoveredPackage_ReturnsReaderOverZipRoot()
    {
        // Arrange: a zip with a managed file and release notes
        var folder = CreateTempDirectory();
        CreateZip(
            folder,
            "contoso-agents-1.0.0.zip",
            (".github/agents/copilot.md", "agents content"),
            ("release-notes.md", "the notes"));
        var provider = CreateProvider("Zips", folder);
        var package = Assert.Single(await provider.ListVersionsAsync("contoso-agents", CancellationToken.None));

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert
        Assert.Contains(".github/agents/copilot.md", reader.ListFiles());
        Assert.Equal("the notes", reader.ReadText("release-notes.md"));
        Assert.Null(reader.ReadText("AGENTS.md"));
    }

    /// <summary>
    ///     Test that a package discovered at another source is refused.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_PackageFromOtherSource_ThrowsArgumentException()
    {
        // Arrange: a real zip, but reported by a different source alias
        var folder = CreateTempDirectory();
        var zipPath = CreateZip(folder, "contoso-agents-1.0.0.zip");
        var provider = CreateProvider("Zips", folder);
        var foreign = new DiscoveredPackage("contoso-agents", Parse("1.0.0"), zipPath, "Elsewhere");

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => provider.AcquireAsync(foreign, CancellationToken.None));
    }

    /// <summary>
    ///     Test that a zip that is not in this source's own folder is refused, even if the alias
    ///     matches.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_ZipOutsideSourceFolder_ThrowsArgumentException()
    {
        // Arrange: the source folder, and a zip in a different folder
        var folder = CreateTempDirectory();
        var otherFolder = CreateTempDirectory();
        var zipPath = CreateZip(otherFolder, "contoso-agents-1.0.0.zip");
        var provider = CreateProvider("Zips", folder);
        var outside = new DiscoveredPackage("contoso-agents", Parse("1.0.0"), zipPath, "Zips");

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => provider.AcquireAsync(outside, CancellationToken.None));
    }

    /// <summary>
    ///     Test that the constructor rejects a NuGet-type definition, a blank location, and null.
    /// </summary>
    [Fact]
    public void ZipFolderSourceProvider_Constructor_InvalidDefinition_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => new ZipFolderSourceProvider(
            new PackageSourceDefinition { Alias = "N", Type = PackageSourceType.NuGet, Location = Path.GetTempPath() }));
        Assert.Throws<ArgumentException>(() => new ZipFolderSourceProvider(
            new PackageSourceDefinition { Alias = "Z", Type = PackageSourceType.Zip, Location = " " }));
        Assert.Throws<ArgumentNullException>(() => new ZipFolderSourceProvider(null!));
    }

    /// <summary>
    ///     Creates a provider for a zip source at a folder.
    /// </summary>
    private static ZipFolderSourceProvider CreateProvider(string alias, string folder) =>
        new(new PackageSourceDefinition { Alias = alias, Type = PackageSourceType.Zip, Location = folder });

    /// <summary>
    ///     Parses a version for test setup, failing loudly if invalid.
    /// </summary>
    private static PackageVersion Parse(string version) =>
        PackageVersion.TryParse(version, out var parsed) && parsed is not null
            ? parsed
            : throw new ArgumentException($"Invalid test version '{version}'.", nameof(version));

    /// <summary>
    ///     Creates a zip file with the given text entries and returns its path.
    /// </summary>
    private static string CreateZip(string folder, string fileName, params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(folder, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        return path;
    }

    /// <summary>
    ///     Creates a unique temporary directory tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_zip_provider_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
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
