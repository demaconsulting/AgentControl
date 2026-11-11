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
    /// <summary>
    ///     The temporary folders created by the tests, deleted (best effort) when the test ends.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that a token that is already canceled stops a listing before any scan starts.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_ListPackageIds_PreCanceledToken_ThrowsOperationCanceled()
    {
        // Arrange
        var folder = CreateTempDirectory();
        CreateZip(folder, "contoso-agents-1.0.0.zip");
        var provider = CreateProvider("Zips", folder);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ListPackageIdsAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ListVersionsAsync("contoso-agents", cancellation.Token));
    }

    /// <summary>
    ///     Test that a call whose token is canceled while blocking work is running returns at once
    ///     with <see cref="OperationCanceledException"/>, and that the result the abandoned work
    ///     finally produces is handed back for disposal rather than leaked.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_RunCancellable_CanceledWhileWorkRuns_ReturnsPromptlyAndDisposesLateResult()
    {
        // Arrange: work that blocks until the test lets it finish
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var disposed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = ZipFolderSourceProvider.RunCancellableAsync(
            () =>
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(60));
                return "late result";
            },
            cancellation.Token,
            late => disposed.TrySetResult(late));
        Assert.True(started.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        // Act: cancel while the work is still blocked
        await cancellation.CancelAsync();

        // Assert: the caller is released although the work has not finished, and nothing was disposed yet
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.False(release.IsSet);
        Assert.False(disposed.Task.IsCompleted);

        // Act: the abandoned work finishes
        release.Set();

        // Assert: its late result was handed back for disposal
        Assert.Equal("late result", await disposed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a zip that is acquired after its folder has disappeared is reported as a
    ///     missing folder (<see cref="DirectoryNotFoundException"/>), as listing would report it.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_FolderRemovedAfterListing_ThrowsDirectoryNotFoundException()
    {
        // Arrange: list a package, then remove the whole folder (an unplugged drive, a deleted share)
        var folder = CreateTempDirectory();
        CreateZip(folder, "contoso-agents-1.0.0.zip");
        var provider = CreateProvider("Zips", folder);
        var package = Assert.Single(await provider.ListVersionsAsync("contoso-agents", CancellationToken.None));
        Directory.Delete(folder, recursive: true);

        // Act / Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.AcquireAsync(package, CancellationToken.None));
    }

    /// <summary>
    ///     Test that a source whose configured path is not a folder at all (here a file) is
    ///     reported as a folder problem - a missing folder - for both listings.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_ListPackageIds_FolderPathIsAFile_IsReportedAsFolderProblem()
    {
        // Arrange
        var folder = CreateTempDirectory();
        var file = Path.Combine(folder, "not-a-folder.txt");
        await File.WriteAllTextAsync(file, "just a file", TestContext.Current.CancellationToken);
        var provider = CreateProvider("Zips", file);

        // Act / Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.ListPackageIdsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => provider.ListVersionsAsync("contoso-agents", CancellationToken.None));
    }

    /// <summary>
    ///     Test that a zip that cannot be opened (here, not a zip archive at all) is reported as an
    ///     <see cref="InvalidOperationException"/> naming the problem, not a raw I/O exception.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_UnreadableZip_ThrowsInvalidOperationExceptionWithSafeMessage()
    {
        // Arrange: a file with a package name that is not a zip
        var folder = CreateTempDirectory();
        var path = Path.Combine(folder, "contoso-agents-1.0.0.zip");
        await File.WriteAllTextAsync(path, "this is not a zip", TestContext.Current.CancellationToken);
        var provider = CreateProvider("Zips", folder);
        var package = new DiscoveredPackage("contoso-agents", Parse("1.0.0"), path, "Zips");

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Contains("Failed to open package zip", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a package zip in the source's folder that is a symbolic link to a zip outside
    ///     the folder is refused, although its own path is inside the folder.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_ZipIsSymlinkToFileOutsideFolder_ThrowsArgumentException()
    {
        // Arrange: a real zip elsewhere, and a link to it with a package name inside the source folder
        var folder = CreateTempDirectory();
        var outsideZip = CreateZip(CreateTempDirectory(), "contoso-agents-1.0.0.zip");
        var link = Path.Combine(folder, "contoso-agents-1.0.0.zip");
        TryCreateFileSymlinkOrSkip(link, outsideZip);
        var provider = CreateProvider("Zips", folder);
        var package = Assert.Single(await provider.ListVersionsAsync("contoso-agents", CancellationToken.None));

        // Act / Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => provider.AcquireAsync(package, CancellationToken.None));
        Assert.Contains("outside", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a package zip that is a symbolic link to another file in the same folder is
    ///     still allowed: the content comes from the source's own folder.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_ZipIsSymlinkToFileInsideFolder_IsAllowed()
    {
        // Arrange: a real zip in the folder and a second package name linking to it
        var folder = CreateTempDirectory();
        var realZip = CreateZip(folder, "contoso-agents-1.0.0.zip", ("AGENTS.md", "real"));
        var link = Path.Combine(folder, "contoso-agents-1.0.1.zip");
        TryCreateFileSymlinkOrSkip(link, realZip);
        var provider = CreateProvider("Zips", folder);
        var package = (await provider.ListVersionsAsync("contoso-agents", CancellationToken.None))
            .Single(p => p.Version.ToString() == "1.0.1");

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert
        Assert.Equal("real", reader.ReadText("AGENTS.md"));
    }

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
    ///     Test that the reader of an acquired zip reports the identity of the agent files in the zip as its
    ///     content identity, that a repacked zip with the same files reports the same one, and that a
    ///     republished zip with different content reports another.
    /// </summary>
    [Fact]
    public async Task ZipFolderSourceProvider_Acquire_DiscoveredPackage_ReportsFilesIdentityOfZipContent()
    {
        // Arrange: a zip with an agent file and release notes
        var folder = CreateTempDirectory();
        var zipPath = CreateZip(
            folder, "contoso-agents-1.0.0.zip", (".github/agents/copilot.md", "original"), ("release-notes.md", "the notes"));
        var provider = CreateProvider("Zips", folder);
        var package = Assert.Single(await provider.ListVersionsAsync("contoso-agents", CancellationToken.None));

        // Act: acquire it, then repack the same files (other notes, other order) and acquire again, then
        // republish the same version with different content and acquire again
        string? first;
        using (var reader = await provider.AcquireAsync(package, CancellationToken.None))
        {
            first = reader.ContentIdentity;
        }

        File.Delete(zipPath);
        CreateZip(
            folder, "contoso-agents-1.0.0.zip", ("release-notes.md", "other notes"), (".github/agents/copilot.md", "original"));
        string? repacked;
        using (var reader = await provider.AcquireAsync(package, CancellationToken.None))
        {
            repacked = reader.ContentIdentity;
        }

        File.Delete(zipPath);
        CreateZip(folder, "contoso-agents-1.0.0.zip", (".github/agents/copilot.md", "republished"));
        using var republished = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert
        Assert.True(PackageContentIdentity.IsFilesIdentity(first));
        Assert.Equal(first, repacked);
        Assert.True(PackageContentIdentity.IsFilesIdentity(republished.ContentIdentity));
        Assert.NotEqual(first, republished.ContentIdentity);
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
    ///     Creates a file symbolic link, or skips the test when the platform or its privileges do not
    ///     allow creating one.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="target">The file it points at.</param>
    private static void TryCreateFileSymlinkOrSkip(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
        }
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
