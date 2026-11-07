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

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoSync;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Unit tests for <see cref="NuGetSourceProvider"/>.
/// </summary>
/// <remarks>
///     Every test runs against a real NuGet folder feed created on the fly in a temporary folder
///     (see <see cref="NuGetFeedBuilder"/>), so no network is involved. Each test also supplies its
///     own per-source cache base folder and uses package IDs unique to the test, so tests are safe
///     to run in parallel and never read or write the developer's real NuGet cache.
/// </remarks>
public sealed class NuGetSourceProviderTests : IDisposable
{
    /// <summary>
    ///     The temporary folders (feeds and per-source cache bases) created by the tests, deleted
    ///     (best effort) when the test ends.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     Test that listing versions of a package in a folder feed returns every version, each
    ///     stamped with the source alias, including prereleases.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_FolderFeedWithVersions_ReturnsAllVersionsStampedWithAlias()
    {
        // Arrange: a feed with three versions of one package and one of another
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "2.0.0-beta.1");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.2.3.4");
        NuGetFeedBuilder.CreateAgentPackage(feed, UniqueId(), "9.9.9");
        var provider = CreateProvider("Feed", feed);

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert: all three versions of the requested package, and only those, with the alias
        Assert.Equal(
            ["1.0.0", "1.2.3.4", "2.0.0-beta.1"],
            versions.OrderBy(p => p.Version).Select(p => p.Version.ToString()));
        Assert.All(versions, package =>
        {
            Assert.Equal("Feed", package.SourceAlias);
            Assert.Equal(id, package.PackageName);
        });
    }

    /// <summary>
    ///     Test that listing versions of a package the feed does not carry returns an empty list
    ///     rather than failing.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_PackageNotInFeed_ReturnsEmptyList()
    {
        // Arrange: a feed with an unrelated package
        var feed = CreateTempDirectory();
        NuGetFeedBuilder.CreateAgentPackage(feed, UniqueId(), "1.0.0");
        var provider = CreateProvider("Feed", feed);

        // Act
        var versions = await provider.ListVersionsAsync(UniqueId(), CancellationToken.None);

        // Assert
        Assert.Empty(versions);
    }

    /// <summary>
    ///     Test that a feed folder that does not exist is reported as a missing folder, the same
    ///     way a missing zip source folder is.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_FolderDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_feed_" + Guid.NewGuid());
        var provider = CreateProvider("Feed", missing);

        // Act / Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => provider.ListVersionsAsync(UniqueId(), CancellationToken.None));
    }

    /// <summary>
    ///     Test that listing honors cancellation instead of completing.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_AlreadyCanceled_ThrowsOperationCanceledException()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var provider = CreateProvider("Feed", feed);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ListVersionsAsync(id, cancellation.Token));
    }

    /// <summary>
    ///     Test that a token that is already canceled stops a listing before any preparation: no
    ///     missing-folder report and no cache root is created.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_PreCanceledToken_ThrowsOperationCanceledWithoutPreparingAnything()
    {
        // Arrange: a feed folder that does not exist, so any preparation would report it instead
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_feed_" + Guid.NewGuid());
        var provider = CreateProvider("Feed", missing);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert: cancellation wins, not DirectoryNotFoundException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ListVersionsAsync(UniqueId(), cancellation.Token));
        Assert.False(Directory.Exists(provider.RootFolder));
    }

    /// <summary>
    ///     Test that a token that is already canceled stops an acquisition before any preparation:
    ///     no missing-folder report and no cache root is created.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_PreCanceledToken_ThrowsOperationCanceledWithoutPreparingAnything()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_feed_" + Guid.NewGuid());
        var provider = CreateProvider("Feed", missing);
        var package = new DiscoveredPackage(UniqueId(), ParseVersion("1.0.0"), "1.0.0", "Feed");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.AcquireAsync(package, cancellation.Token));
        Assert.False(Directory.Exists(provider.RootFolder));
    }

    /// <summary>
    ///     Test that the options every library call is made with are non-interactive - with and
    ///     without a credential callback - so that nothing the library or a plug-in does can open a
    ///     user interface of its own.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_CreateOptions_AreNonInteractive()
    {
        // Arrange
        var folder = CreateProvider("Feed", CreateTempDirectory());
        var server = new NuGetSourceProvider(
            new PackageSourceDefinition
            {
                Alias = "Server",
                Type = PackageSourceType.NuGet,
                Location = "https://feeds.example.com/v3/index.json"
            },
            CreateTempDirectory());
        var callback = new PackageCredentialCallback(
            FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json")),
            "Server",
            new SessionCredentialStore());

        // Act
        var folderOptions = folder.CreateOptions(null);
        var serverOptions = server.CreateOptions(callback);

        // Assert
        Assert.False(folderOptions.Interactive);
        Assert.False(serverOptions.Interactive);
        Assert.NotNull(serverOptions.CredentialCallback);
    }

    /// <summary>
    ///     Test that acquiring a package unpacks it and returns a reader over its content folder,
    ///     exposing the managed folders, release notes, and AGENTS.md template.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_ExistingPackage_ReturnsReaderOverContentFolder()
    {
        // Arrange: a feed holding an agent package with release notes and an AGENTS.md template
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(
            feed, id, "1.0.0", marker: "agents v1", releaseNotes: "notes v1", agentsMdTemplate: "template v1");
        var provider = CreateProvider("Feed", feed);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert: the reader sees the content folder exactly as a zip reader sees a zip root
        var files = reader.ListFiles();
        Assert.Contains(".github/agents/copilot.md", files);
        Assert.Contains(".github/skills/skill.md", files);
        Assert.Equal("notes v1", reader.ReadText("release-notes.md"));
        Assert.Equal("template v1", reader.ReadText("AGENTS.md"));
    }

    /// <summary>
    ///     Test that an acquired package can be applied to a repo through the shared extractor,
    ///     producing the same managed folders a zip package would.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_ThenExtract_WritesManagedFoldersToRepoOnly()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var repo = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0", marker: "agents v1");
        var provider = CreateProvider("Feed", feed);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        using (var reader = await provider.AcquireAsync(package, CancellationToken.None))
        {
            PackageContentExtractor.Extract(reader, repo);
        }

        // Assert: the managed folders are in the repo, and nothing else (no release notes file)
        Assert.Equal("agents v1", await File.ReadAllTextAsync(
                Path.Combine(repo, ".github", "agents", "copilot.md"), TestContext.Current.CancellationToken));
        Assert.True(PackageContentExtractor.AllManagedFoldersExist(repo));
        Assert.Equal([".github"], Directory.GetFileSystemEntries(repo).Select(Path.GetFileName));
    }

    /// <summary>
    ///     Test that a package without the mandatory content folder is rejected on acquisition,
    ///     so it can never cause a repo's managed folders to be deleted and replaced with nothing.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_PackageWithoutContentFolder_ThrowsInvalidOperationException()
    {
        // Arrange: a package that keeps its files outside content/
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreatePackage(feed, id, "1.0.0", (".github/agents/copilot.md", "wrong layout"));
        var provider = CreateProvider("Feed", feed);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act / Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, CancellationToken.None));
        Assert.Contains("content", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the root folder handed to the NuGet library is an AgentControl-owned folder
    ///     under the supplied base, holding a generated configuration that only redirects the
    ///     package cache into that same folder.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_UsesExplicitAgentControlOwnedRootWithGeneratedConfig()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var rootBase = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var provider = new NuGetSourceProvider(CreateDefinition("Feed", feed), rootBase);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert: the root is directly under the supplied base, its nuget.config only sets the
        // global packages folder (no sources, no credentials), and the package was unpacked there
        Assert.Equal(rootBase, Path.GetDirectoryName(provider.RootFolder));
        var config = await File.ReadAllTextAsync(
            Path.Combine(provider.RootFolder, "nuget.config"), TestContext.Current.CancellationToken);
        Assert.Contains("globalPackagesFolder", config, StringComparison.Ordinal);
        Assert.DoesNotContain("packageSources", config, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", config, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.Combine(provider.RootFolder, "packages", id.ToLowerInvariant())));
    }

    /// <summary>
    ///     Test that listing and acquiring never create or modify anything in the target repo or
    ///     read its nuget.config: a repo-level nuget.config that tries to redirect packages is
    ///     ignored and left byte-for-byte unchanged, and nothing is added next to it.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListAndAcquire_NeverTouchesRepoNuGetConfig()
    {
        // Arrange: a repo folder with a hostile nuget.config trying to redirect the package cache
        var repo = CreateTempDirectory();
        var repoCache = Path.Combine(repo, "hijacked-cache");
        var repoConfigPath = Path.Combine(repo, "nuget.config");
        var repoConfig =
            $"<configuration><config><add key=\"globalPackagesFolder\" value=\"{repoCache}\" /></config></configuration>";
        await File.WriteAllTextAsync(repoConfigPath, repoConfig, TestContext.Current.CancellationToken);

        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var provider = CreateProvider("Feed", feed);

        // Act
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert: the repo holds only its original config, unchanged, and no cache was created
        Assert.Equal([repoConfigPath], Directory.GetFileSystemEntries(repo));
        Assert.Equal(repoConfig, await File.ReadAllTextAsync(repoConfigPath, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(repoCache));
        Assert.False(provider.RootFolder.StartsWith(repo, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Test that listing and supplying packages leaves NuGet's process-wide credential
    ///     service exactly as it was, so other NuGet operations in the process authenticate as
    ///     before.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListAndAcquire_LeavesProcessWideCredentialServiceUnchanged()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var provider = CreateProvider("Feed", feed);
        var before = global::NuGet.Protocol.HttpHandlerResourceV3.CredentialService;

        // Act
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert: the same (possibly absent) service instance is still registered
        Assert.Same(before, global::NuGet.Protocol.HttpHandlerResourceV3.CredentialService);
    }

    /// <summary>
    ///     Test that two NuGet sources offering the same package ID and version with different
    ///     content each yield their own content: a package unpacked for one source is never served
    ///     for the other (each source has a private package cache).
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_SamePackageInTwoSources_EachSourceGetsItsOwnContent()
    {
        // Arrange: two feeds with the same ID and version but different content
        var feedA = CreateTempDirectory();
        var feedB = CreateTempDirectory();
        var rootBase = CreateTempDirectory();
        var id = UniqueId();
        NuGetFeedBuilder.CreateAgentPackage(feedA, id, "1.0.0", marker: "from A");
        NuGetFeedBuilder.CreateAgentPackage(feedB, id, "1.0.0", marker: "from B");
        var providerA = new NuGetSourceProvider(CreateDefinition("A", feedA), rootBase);
        var providerB = new NuGetSourceProvider(CreateDefinition("B", feedB), rootBase);
        var packageA = Assert.Single(await providerA.ListVersionsAsync(id, CancellationToken.None));
        var packageB = Assert.Single(await providerB.ListVersionsAsync(id, CancellationToken.None));

        // Act: acquire from A first (populating A's cache), then from B
        using var readerA = await providerA.AcquireAsync(packageA, CancellationToken.None);
        using var readerB = await providerB.AcquireAsync(packageB, CancellationToken.None);

        // Assert: each reader sees its own source's content
        Assert.Equal("from A", ReadFile(readerA, ".github/agents/copilot.md"));
        Assert.Equal("from B", ReadFile(readerB, ".github/agents/copilot.md"));
        Assert.NotEqual(providerA.RootFolder, providerB.RootFolder);
    }

    /// <summary>
    ///     Test that a package discovered at a different source is refused, so content is never
    ///     fetched on behalf of a source that did not report it.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_PackageFromOtherSource_ThrowsArgumentException()
    {
        // Arrange
        var feed = CreateTempDirectory();
        var provider = CreateProvider("Feed", feed);
        var foreignPackage = new DiscoveredPackage("pkg", ParseVersion("1.0.0"), "1.0.0", "Elsewhere");

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AcquireAsync(foreignPackage, CancellationToken.None));
    }

    /// <summary>
    ///     Test that a plain <c>http</c> server location is rejected with a clear message unless
    ///     the source explicitly allows insecure HTTP.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_Constructor_PlainHttpWithoutOptIn_ThrowsInvalidOperationExceptionWithClearMessage()
    {
        // Arrange
        var definition = CreateDefinition("Server", "http://feeds.example.com/v3/index.json");

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => new NuGetSourceProvider(definition, CreateTempDirectory()));

        // Assert: the message names the source and the remedy
        Assert.Contains("Server", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Plain http", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Allow insecure HTTP", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a relative folder location is rejected (a relative path would resolve against
    ///     the process's current directory).
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_Constructor_RelativeLocation_ThrowsInvalidOperationException()
    {
        // Arrange
        var definition = CreateDefinition("Feed", Path.Combine("relative", "feed"));

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => new NuGetSourceProvider(definition, CreateTempDirectory()));
    }

    /// <summary>
    ///     Test that a relative (or drive-relative) cache root base is rejected, so the NuGet root
    ///     can never be resolved against the current directory or a target repository.
    /// </summary>
    /// <param name="rootBase">The invalid root base.</param>
    [Theory]
    [InlineData("nuget-cache")]
    [InlineData("..")]
    [InlineData(".")]
    public void NuGetSourceProvider_Constructor_RelativeRootBase_ThrowsArgumentException(string rootBase)
    {
        // Arrange
        var definition = CreateDefinition("Feed", CreateTempDirectory());

        // Act
        var ex = Assert.Throws<ArgumentException>(() => new NuGetSourceProvider(definition, rootBase));

        // Assert
        Assert.Equal("rootBase", ex.ParamName);
    }

    /// <summary>
    ///     Test that the default cache root (no base supplied) is an absolute path under the
    ///     per-user local application data folder, never relative to the current directory.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_Constructor_NoRootBase_RootIsAbsoluteUnderLocalApplicationData()
    {
        // Arrange
        var definition = CreateDefinition("Feed", CreateTempDirectory());

        // Act
        var provider = new NuGetSourceProvider(definition);

        // Assert
        Assert.True(Path.IsPathFullyQualified(provider.RootFolder));
        Assert.StartsWith(
            Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            provider.RootFolder,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a definition whose package-ID list is null (as an explicit JSON null can
    ///     produce) is tolerated and offers no packages instead of crashing the constructor.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Constructor_NullPackageIds_OffersNoPackages()
    {
        // Arrange
        var definition = CreateDefinition("Feed", CreateTempDirectory());
        definition.PackageIds = null!;

        // Act
        var provider = new NuGetSourceProvider(definition, CreateTempDirectory());
        var ids = await provider.ListPackageIdsAsync(CancellationToken.None);

        // Assert
        Assert.Empty(ids);
    }

    /// <summary>
    ///     Test that a zip-type definition is rejected, and a null definition is rejected.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_Constructor_ZipTypeOrNullDefinition_Throws()
    {
        // Arrange
        var zip = new PackageSourceDefinition { Alias = "Z", Type = PackageSourceType.Zip, Location = Path.GetTempPath() };

        // Act / Assert
        Assert.Throws<ArgumentException>(() => new NuGetSourceProvider(zip, CreateTempDirectory()));
        Assert.Throws<ArgumentNullException>(() => new NuGetSourceProvider(null!, CreateTempDirectory()));
    }

    /// <summary>
    ///     Test that the package IDs offered are exactly the configured ones: trimmed, de-duplicated
    ///     case-insensitively, blanks dropped, and sorted.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListPackageIds_ConfiguredIds_ReturnsDistinctSortedTrimmedIds()
    {
        // Arrange
        var definition = CreateDefinition("Feed", CreateTempDirectory());
        definition.PackageIds = ["  zeta.agents ", "Alpha.Agents", "alpha.agents", "", "  "];
        var provider = new NuGetSourceProvider(definition, CreateTempDirectory());

        // Act
        var ids = await provider.ListPackageIdsAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["Alpha.Agents", "zeta.agents"], ids);
    }

    /// <summary>
    ///     Test that the per-source root key is stable for the same alias and location (alias case
    ///     and trailing separators ignored), changes when the location changes, and is safe as a
    ///     folder name.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_BuildSourceKey_AliasAndLocation_StableDistinctAndFolderSafe()
    {
        // Act
        var key = NuGetSourceProvider.BuildSourceKey("My Feed", @"C:\feeds\one");
        var sameKey = NuGetSourceProvider.BuildSourceKey("MY FEED", @"C:\feeds\one\");
        var otherLocation = NuGetSourceProvider.BuildSourceKey("My Feed", @"C:\feeds\two");
        var otherAlias = NuGetSourceProvider.BuildSourceKey("Other", @"C:\feeds\one");

        // Assert
        Assert.StartsWith("My_Feed-", key, StringComparison.Ordinal);
        Assert.Equal(key, sameKey.Replace("MY_FEED", "My_Feed", StringComparison.Ordinal));
        Assert.NotEqual(key, otherLocation);
        Assert.NotEqual(key, otherAlias);
        Assert.DoesNotContain(key, c => Path.GetInvalidFileNameChars().Contains(c));
    }

    /// <summary>
    ///     Test that the product assembly never references NuGet's credential-service types, since
    ///     the NuGet credential service is process-wide and AgentControl must never register its
    ///     own.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_ProductAssembly_DoesNotReferenceNuGetCredentialService()
    {
        // Arrange: read the metadata of the product assembly that contains the provider
        using var stream = File.OpenRead(typeof(NuGetSourceProvider).Assembly.Location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        // Act: collect the names of every type the assembly references
        var referencedTypes = metadata.TypeReferences
            .Select(handle => metadata.GetString(metadata.GetTypeReference(handle).Name))
            .ToList();

        // Assert: no credential-service or HTTP-handler-resource type is referenced
        Assert.DoesNotContain(referencedTypes, name => name.Contains("CredentialService", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedTypes, name => name.Contains("HttpHandlerResource", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Reads a file's text from a package reader.
    /// </summary>
    /// <param name="reader">The package reader.</param>
    /// <param name="relativePath">The package-relative path.</param>
    /// <returns>The file's text.</returns>
    private static string ReadFile(IPackageContentReader reader, string relativePath)
    {
        using var stream = reader.OpenRead(relativePath);
        using var streamReader = new StreamReader(stream);
        return streamReader.ReadToEnd();
    }

    /// <summary>
    ///     Parses a version for test setup, failing loudly if invalid.
    /// </summary>
    /// <param name="version">The version text.</param>
    /// <returns>The parsed version.</returns>
    private static PackageVersion ParseVersion(string version) =>
        PackageVersion.TryParse(version, out var parsed) && parsed is not null
            ? parsed
            : throw new ArgumentException($"Invalid test version '{version}'.", nameof(version));

    /// <summary>
    ///     Generates a package ID unique to one test, so packages never collide across tests even
    ///     if a shared NuGet cache were in play.
    /// </summary>
    /// <returns>A unique package ID.</returns>
    private static string UniqueId() => "agentcontrol.test." + Guid.NewGuid().ToString("N");

    /// <summary>
    ///     Creates a NuGet source definition for a folder.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="location">The location.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition CreateDefinition(string alias, string location) =>
        new() { Alias = alias, Type = PackageSourceType.NuGet, Location = location };

    /// <summary>
    ///     Creates a provider over a feed folder with its own private cache base folder.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="feed">The feed folder.</param>
    /// <returns>The provider.</returns>
    private NuGetSourceProvider CreateProvider(string alias, string feed) =>
        new(CreateDefinition(alias, feed), CreateTempDirectory());

    /// <summary>
    ///     Creates a unique temporary directory tracked for cleanup in <see cref="Dispose"/>.
    /// </summary>
    /// <returns>The directory path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_nuget_provider_test_" + Guid.NewGuid());
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; leftover temp directories do not fail the test.
            }
        }
    }
}
