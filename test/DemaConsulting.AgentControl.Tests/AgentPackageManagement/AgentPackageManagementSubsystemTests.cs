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

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Tests.GitIntegration;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Subsystem tests for AgentPackageManagement: configured package-source definitions resolved
///     by the registry into providers and used together with the signature-support check, the way
///     the application drives them - without any user interface.
/// </summary>
/// <remarks>
///     The signed-package tests use a real signed package (see <see cref="SignedNuGetFixture"/>)
///     and are skipped only where nuget.org cannot be reached or the machine's trust store lacks
///     the needed roots; they share a non-parallel collection because they set a process-wide
///     environment variable. The server-source tests resolve sources through the registry and talk
///     to an in-process mock NuGet V3 feed (see <see cref="MockNuGetFeed"/>) over plain HTTP on
///     loopback, which the sources they use explicitly allow.
/// </remarks>
[Collection(SignedFixtureCollection.Name)]
public sealed class AgentPackageManagementSubsystemTests : IDisposable, IAsyncLifetime
{
    /// <summary>
    ///     A password that must never appear in a log, message or file.
    /// </summary>
    private const string SentinelPassword = "sentinel-pw-3f9c1b7a";

    /// <summary>
    ///     The user name of the feed's account.
    /// </summary>
    private const string FeedUser = "svc-agents";

    /// <summary>
    ///     The temporary folders created by the running test, removed on disposal.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <summary>
    ///     The mock feeds started by the running test, stopped on disposal.
    /// </summary>
    private readonly List<MockNuGetFeed> _feeds = [];

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var feed in _feeds)
        {
            await feed.DisposeAsync();
        }
    }

    /// <summary>
    ///     Test that, through the registry, a source that requires signed packages refuses an
    ///     unsigned package - nothing is installed - while a source of the same feed that does not
    ///     require them still serves it.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_RequireSignedSource_UnsignedPackageRefusedWhileOpenSourceServesIt()
    {
        // Arrange: one feed folder with an unsigned agent package, two sources over it
        var feed = CreateTempDirectory();
        var id = "contoso.agents." + Guid.NewGuid().ToString("N");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var registry = PackageSourceRegistry.Create(
            [Definition("Open", feed, requireSigned: false), Definition("Strict", feed, requireSigned: true)],
            CreateTempDirectory());

        // Act
        var open = registry.Resolve("Open");
        var openPackage = Assert.Single(await open.ListVersionsAsync(id, CancellationToken.None));
        using var reader = await open.AcquireAsync(openPackage, CancellationToken.None);
        var strict = registry.Resolve("Strict");
        var strictPackage = Assert.Single(await strict.ListVersionsAsync(id, CancellationToken.None));
        var refusal = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => strict.AcquireAsync(strictPackage, CancellationToken.None));

        // Assert
        Assert.NotEmpty(reader.ListFiles());
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, refusal.Kind);
        Assert.Contains("is not validly signed", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that, through the registry, zip and NuGet sources report the same content identity for the
    ///     same agent files (it is computed from the files, not from the zip or <c>.nupkg</c> they came
    ///     in), that the identity is stable across acquisitions, and that a zip republished under the same
    ///     version with other content is reported as a changed identity.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ZipAndNuGetSources_ReportFilesIdentityAndDetectRepublishedZip()
    {
        // Arrange: a zip source and a NuGet folder source, each with the same four agent files
        var zipFolder = CreateTempDirectory();
        var feed = CreateTempDirectory();
        var id = UniqueId();
        var zipPath = Path.Combine(zipFolder, "contoso-agents-1.0.0.zip");
        WriteZip(zipPath, "agents content");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var registry = PackageSourceRegistry.Create(
            [
                new PackageSourceDefinition { Alias = "Zips", Type = PackageSourceType.Zip, Location = zipFolder },
                Definition("Feed", feed, requireSigned: false)
            ],
            CreateTempDirectory());
        var zips = registry.Resolve("Zips");
        var nuget = registry.Resolve("Feed");
        var zipPackage = Assert.Single(await zips.ListVersionsAsync("contoso-agents", CancellationToken.None));
        var nugetPackage = Assert.Single(await nuget.ListVersionsAsync(id, CancellationToken.None));

        // Act: acquire each twice, republishing the zip between the two zip acquisitions
        string? zipFirst;
        using (var reader = await zips.AcquireAsync(zipPackage, CancellationToken.None))
        {
            zipFirst = reader.ContentIdentity;
        }

        string? nugetFirst;
        using (var reader = await nuget.AcquireAsync(nugetPackage, CancellationToken.None))
        {
            nugetFirst = reader.ContentIdentity;
        }

        File.Delete(zipPath);
        WriteZip(zipPath, "republished");
        using var zipAgain = await zips.AcquireAsync(zipPackage, CancellationToken.None);
        using var nugetAgain = await nuget.AcquireAsync(nugetPackage, CancellationToken.None);

        // Assert: both kinds report exactly the identity of the four files, computed here independently
        // of the production code, so the same files compare as the same whichever source delivers them
        var expected = FilesIdentity(AgentFiles("agents content"));
        Assert.Equal(expected, zipFirst);
        Assert.Equal(expected, nugetFirst);
        Assert.Equal(ContentIdentityComparison.Same, PackageContentIdentity.Compare(zipFirst, nugetFirst));

        // Assert: the NuGet identity is stable, and only the republished zip changed
        Assert.Equal(ContentIdentityComparison.Same, PackageContentIdentity.Compare(nugetFirst, nugetAgain.ContentIdentity));
        Assert.Equal(FilesIdentity(AgentFiles("republished")), zipAgain.ContentIdentity);
        Assert.Equal(ContentIdentityComparison.Changed, PackageContentIdentity.Compare(zipFirst, zipAgain.ContentIdentity));
    }

    /// <summary>
    ///     Test that a Git source reports the same content identity as a zip and a NuGet source for the
    ///     same agent files - a real local repository supplies the Git layout - and carries the commit
    ///     separately as its source revision, which the other kinds do not have.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_SameFilesFromZipNuGetAndGit_ReportTheSameIdentity()
    {
        // Arrange: the four agent files as a zip, a NuGet package and a tag of a repository
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var zipFolder = CreateTempDirectory();
        var feed = CreateTempDirectory();
        var id = UniqueId();
        WriteZip(Path.Combine(zipFolder, "contoso-agents-1.0.0.zip"), "agents content");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var files = AgentFiles("agents content").ToDictionary(file => file.Path, file => file.Text);
        files["release-notes.md"] = "notes that are never part of the identity";
        var commit = fixture.Commit(files, "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var registry = PackageSourceRegistry.Create(
            [
                new PackageSourceDefinition { Alias = "Zips", Type = PackageSourceType.Zip, Location = zipFolder },
                Definition("Feed", feed, requireSigned: false),
                fixture.CreateDefinition()
            ],
            CreateTempDirectory(),
            git: fixture.CreateOptions());

        // Act: acquire the version from each kind of source
        var identities = new Dictionary<string, string?>();
        foreach (var (alias, packageId) in new[] { ("Zips", "contoso-agents"), ("Feed", id), ("Repo", "contoso-agents") })
        {
            var provider = registry.Resolve(alias);
            var package = Assert.Single(await provider.ListVersionsAsync(packageId, TestContext.Current.CancellationToken));
            using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);
            identities[alias] = reader.ContentIdentity;
            Assert.Equal(alias == "Repo" ? PackageContentIdentity.FromGitCommit(commit) : null, reader.SourceRevision);
        }

        // Assert: one identity for the same files, whichever kind of source delivered them
        var expected = FilesIdentity(AgentFiles("agents content"));
        Assert.Multiple(
            () => Assert.Equal(expected, identities["Zips"]),
            () => Assert.Equal(expected, identities["Feed"]),
            () => Assert.Equal(expected, identities["Repo"]));
    }

    /// <summary>
    ///     The four agent files every kind of source in these tests delivers.
    /// </summary>
    /// <param name="marker">The text of the agents file.</param>
    /// <returns>The files as package-relative path and text.</returns>
    private static (string Path, string Text)[] AgentFiles(string marker) =>
    [
        (".github/agents/copilot.md", marker),
        (".github/standards/style.md", "standards content"),
        (".github/templates/template.md", "templates content"),
        (".github/skills/skill.md", "skills content")
    ];

    /// <summary>
    ///     Test that, through the registry and the cache, the offers of every configured zip source are
    ///     listed in the order the sources are configured (the excluded source is not read at all), an
    ///     unreadable source is skipped, and the newest version across the sources is found with a tie
    ///     attributed to the source configured first.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_AcrossSources_ListsOffersInConfigurationOrderAndNewestWins()
    {
        // Arrange: three zip sources with the same package (the first is "pinned" and left out), and a
        // fourth source whose folder does not exist
        var pinned = CreateTempDirectory();
        var first = CreateTempDirectory();
        var second = CreateTempDirectory();
        foreach (var (folder, version) in new[] { (pinned, "9.0.0"), (first, "1.0.0"), (first, "2.0.0"), (second, "2.0.0"), (second, "1.5.0") })
        {
            await File.WriteAllBytesAsync(
                Path.Combine(folder, $"contoso-agents-{version}.zip"), [], TestContext.Current.CancellationToken);
        }

        var registry = PackageSourceRegistry.Create(
            [
                new PackageSourceDefinition { Alias = "Pinned", Type = PackageSourceType.Zip, Location = pinned },
                new PackageSourceDefinition { Alias = "First", Type = PackageSourceType.Zip, Location = first },
                new PackageSourceDefinition { Alias = "Gone", Type = PackageSourceType.Zip, Location = Path.Combine(first, "missing") },
                new PackageSourceDefinition { Alias = "Second", Type = PackageSourceType.Zip, Location = second }
            ]);
        var cache = new PackageVersionCache();

        // Act
        var offers = await cache.GetVersionsAcrossSourcesAsync(
            registry, "contoso-agents", "Pinned", TestContext.Current.CancellationToken);
        var check = await cache.IsNewerVersionAvailableAcrossSourcesAsync(
            registry, "contoso-agents", "1.0.0", TestContext.Current.CancellationToken);

        // Assert: configuration order without the excluded and unreadable sources; the 2.0.0 tie goes to the first
        Assert.Equal(
            ["First 2.0.0", "First 1.0.0", "Second 2.0.0", "Second 1.5.0"],
            offers.Select(offer => $"{offer.SourceAlias} {offer.Version}"));
        Assert.True(check.IsNewerAvailable);
        Assert.Equal("9.0.0", check.Latest?.Version.ToString());
        Assert.Equal("Pinned", check.Latest?.SourceAlias);
        Assert.Equal("First", check.Pinned?.SourceAlias);
    }

    /// <summary>
    ///     Computes the expected files identity of some agent files directly with the platform's hash,
    ///     following the documented encoding, so a test does not rely on the code it checks.
    /// </summary>
    /// <param name="files">The files: package-relative path and text (UTF-8, no byte-order mark).</param>
    /// <returns><c>files-sha256:</c> followed by the lower-case hexadecimal digest.</returns>
    private static string FilesIdentity(IEnumerable<(string Path, string Text)> files)
    {
        using var stream = new MemoryStream();
        foreach (var (path, text) in files.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            var content = new UTF8Encoding(false).GetBytes(text);
            stream.Write(Encoding.UTF8.GetBytes(path));
            stream.WriteByte(0);
            stream.Write(BitConverter.GetBytes((long)content.Length));
            stream.Write(content);
        }

        return "files-sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
    }

    /// <summary>
    ///     Test that on a platform that cannot verify signatures a source whose settings require
    ///     them refuses to list or acquire (it never skips the check), while a source that does not
    ///     require them is unaffected.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_RequireSignedOnUnsupportedPlatform_SourceRefusesAndOthersWork()
    {
        // Arrange: the support check answers "unsupported", as it does for a failing probe
        var supported = SignatureVerificationSupport.Evaluate(
            isWindows: true, isLinux: false, isMacOS: false, () => throw new InvalidOperationException("no chains"));
        var feed = CreateTempDirectory();
        var id = "contoso.agents." + Guid.NewGuid().ToString("N");
        NuGetFeedBuilder.CreateAgentPackage(feed, id, "1.0.0");
        var rootBase = CreateTempDirectory();
        var registry = new PackageSourceRegistry(
            [Definition("Open", feed, requireSigned: false), Definition("Strict", feed, requireSigned: true)],
            definition => new NuGetSourceProvider(definition, rootBase, signatureVerificationSupported: supported));

        // Act
        var open = Assert.Single(await registry.Resolve("Open").ListVersionsAsync(id, CancellationToken.None));
        var refusal = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => registry.Resolve("Strict").ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.False(supported);
        Assert.Equal("1.0.0", open.Location);
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, refusal.Kind);
        Assert.Contains("not supported on this platform", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a hand-edited settings file with an unreadable signer fingerprint stops the
    ///     source when it is first used instead of widening trust to any signer.
    /// </summary>
    [Fact]
    public void AgentPackageManagementSubsystem_UnreadableSignerFingerprint_SourceIsRefusedWhenResolved()
    {
        // Arrange
        var definition = Definition("Strict", CreateTempDirectory(), requireSigned: true);
        definition.SignerFingerprint = "not a fingerprint";
        var registry = PackageSourceRegistry.Create([definition], CreateTempDirectory());

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("Strict"));
    }

    /// <summary>
    ///     Test that, through the registry, a validly signed package is accepted by a source that
    ///     requires signatures without a pinned publisher and by one pinned to the package's author
    ///     (however the fingerprint was typed), and rejected - and not installed - by a source pinned
    ///     to another publisher.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_SignedPackage_AcceptedWhenAnyOrAuthorPinnedAndRejectedWhenOtherPinned()
    {
        // Arrange: three sources over the same feed holding a real signed package
        using var scope = SignedNuGetFixture.Arrange();
        var feed = CreateTempDirectory();
        SignedNuGetFixture.CopyToFeed(feed);
        var typedAuthor = string.Join(' ', Enumerable.Range(0, 32).Select(i => SignedNuGetFixture.AuthorFingerprint.Substring(i * 2, 2))).ToLowerInvariant();
        var registry = PackageSourceRegistry.Create(
            [
                Definition("Any", feed, requireSigned: true),
                Definition("Author", feed, requireSigned: true, typedAuthor),
                Definition("Other", feed, requireSigned: true, SignedNuGetFixture.UnknownFingerprint)
            ],
            CreateTempDirectory());

        // Act
        var acceptedAny = await AcquireAsync(registry, "Any");
        var acceptedAuthor = await AcquireAsync(registry, "Author");
        var rejected = await AcquireAsync(registry, "Other");

        // Assert: accepted means the package was installed and the provider went on to the
        // content check (the fixture is a library, not an agent package)
        Assert.IsNotType<PackageSourceAccessException>(acceptedAny);
        Assert.Contains("has no 'content' folder", acceptedAny.Message, StringComparison.Ordinal);
        Assert.IsNotType<PackageSourceAccessException>(acceptedAuthor);
        Assert.Contains("has no 'content' folder", acceptedAuthor.Message, StringComparison.Ordinal);
        var refusal = Assert.IsType<PackageSourceAccessException>(rejected);
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, refusal.Kind);
        Assert.Contains("is not signed by the trusted publisher", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that, through the registry, a NuGet server source lists the versions of a package
    ///     it carries, labeled with its alias, without downloading any package, and lists none for
    ///     a package it does not carry.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_ListVersions_ReturnsVersionsWithoutDownloadingAndNoneForUnknownPackage()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0", "1.1.0");
        var provider = CreateServerRegistry(feed, credentials: null).Resolve("Server");

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);
        var unknown = await provider.ListVersionsAsync(UniqueId(), CancellationToken.None);

        // Assert
        Assert.Equal(["1.0.0", "1.1.0"], versions.OrderBy(v => v.Version).Select(v => v.Version.ToString()));
        Assert.All(versions, package => Assert.Equal("Server", package.SourceAlias));
        Assert.Empty(unknown);
        Assert.Equal(0, feed.DownloadCount);
    }

    /// <summary>
    ///     Test that, through the registry, a NuGet server source supplies the content of a chosen
    ///     version downloaded from the server.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_Acquire_SuppliesContentOfChosenVersion()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0", "1.1.0");
        var provider = CreateServerRegistry(feed, credentials: null).Resolve("Server");
        var package = (await provider.ListVersionsAsync(id, CancellationToken.None))
            .Single(p => p.Version.ToString() == "1.1.0");

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert
        Assert.Equal("agent content from the feed 1.1.0", ReadFile(reader, ".github/agents/copilot.md"));
        Assert.Equal(1, feed.DownloadCount);
    }

    /// <summary>
    ///     Test that a NuGet server source is only usable with an https address of a service index
    ///     ending in index.json - plain http only when the source explicitly allows it - and that
    ///     an address that embeds a credential is refused without repeating it.
    /// </summary>
    [Fact]
    public void AgentPackageManagementSubsystem_ServerSource_Address_RefusedUnlessHttpsServiceIndexOrOptedInHttp()
    {
        // Arrange
        var registry = PackageSourceRegistry.Create(
            [
                ServerDefinition("Plain", "http://127.0.0.1:5999/v3/index.json", allowInsecureHttp: false),
                ServerDefinition("Allowed", "http://127.0.0.1:5999/v3/index.json", allowInsecureHttp: true),
                ServerDefinition("Secure", "https://feeds.example.com/v3/index.json", allowInsecureHttp: false),
                ServerDefinition("NoIndex", "https://feeds.example.com/v3/", allowInsecureHttp: false),
                ServerDefinition("Secret", "https://feeds.example.com/v3/index.json?token=sentinel-secret", allowInsecureHttp: false)
            ],
            CreateTempDirectory());

        // Act
        var plain = Assert.Throws<InvalidOperationException>(() => registry.Resolve("Plain"));
        var noIndex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("NoIndex"));
        var secret = Assert.Throws<InvalidOperationException>(() => registry.Resolve("Secret"));

        // Assert: refused with the remedy, accepted after the opt-in or over https
        Assert.Contains("Allow insecure HTTP", plain.Message, StringComparison.Ordinal);
        Assert.NotNull(registry.Resolve("Allowed"));
        Assert.NotNull(registry.Resolve("Secure"));
        Assert.DoesNotContain("feeds.example.com", noIndex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-secret", secret.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a server that needs a sign-in is signed in to with credentials from the user's
    ///     NuGet configuration or a sign-in the user saved earlier without asking, and otherwise by
    ///     asking the user once, after which the answer is reused by the next operation without
    ///     asking again.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_NeedsSignIn_UsesSavedSignInOtherwiseAsksOnceAndReusesTheAnswer()
    {
        // Arrange: a feed with a saved sign-in and a feed without one
        var savedId = UniqueId();
        var savedFeed = await StartFeedAsync(savedId, "1.0.0");
        RequireSignIn(savedFeed);
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(FeedOrigin.Create(new Uri(savedFeed.IndexUrl)), new NuGetCredential(FeedUser, SentinelPassword));
        var savedPrompt = new FakeCredentialPrompt();
        var saved = CreateServerRegistry(
            savedFeed, new PackageCredentialContext(new SessionCredentialStore(), savedPrompt, usersStore)).Resolve("Server");

        var askedId = UniqueId();
        var askedFeed = await StartFeedAsync(askedId, "1.0.0");
        RequireSignIn(askedFeed);
        var askedPrompt = new FakeCredentialPrompt();
        askedPrompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var asked = CreateServerRegistry(
            askedFeed, new PackageCredentialContext(new SessionCredentialStore(), askedPrompt)).Resolve("Server");

        // A feed whose credentials are in the user's own NuGet configuration
        var configuredId = UniqueId();
        var configuredFeed = await StartFeedAsync(configuredId, "1.0.0");
        RequireSignIn(configuredFeed);
        var configuredBase = CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(configuredBase, "nuget.config"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n" +
            $"    <add key=\"Configured\" value=\"{configuredFeed.IndexUrl}\" allowInsecureConnections=\"true\" />\n" +
            "  </packageSources>\n  <packageSourceCredentials>\n    <Configured>\n" +
            $"      <add key=\"Username\" value=\"{FeedUser}\" />\n" +
            $"      <add key=\"ClearTextPassword\" value=\"{SentinelPassword}\" />\n" +
            "    </Configured>\n  </packageSourceCredentials>\n</configuration>\n",
            TestContext.Current.CancellationToken);
        var configuredPrompt = new FakeCredentialPrompt();
        var configured = CreateServerRegistry(
            configuredFeed,
            new PackageCredentialContext(new SessionCredentialStore(), configuredPrompt),
            rootBase: configuredBase).Resolve("Server");

        // Act
        var savedVersions = await saved.ListVersionsAsync(savedId, CancellationToken.None);
        var configuredVersions = await configured.ListVersionsAsync(configuredId, CancellationToken.None);
        var askedPackage = Assert.Single(await asked.ListVersionsAsync(askedId, CancellationToken.None));
        using var reader = await asked.AcquireAsync(askedPackage, CancellationToken.None);

        // Assert: the saved sign-in and the NuGet configuration needed no question; the other
        // feed asked once, for its own origin, and the later download reused the answer
        Assert.Single(savedVersions);
        Assert.Equal(0, savedPrompt.AskCount);
        Assert.Single(configuredVersions);
        Assert.Equal(0, configuredPrompt.AskCount);
        var request = Assert.Single(askedPrompt.Requests);
        Assert.Equal(FeedOrigin.Create(new Uri(askedFeed.IndexUrl)), request.Origin);
        Assert.Equal("agent content from the feed 1.0.0", ReadFile(reader, ".github/agents/copilot.md"));
    }

    /// <summary>
    ///     Test that a credential is kept only after the operation it was entered for succeeded: a
    ///     wrong password is neither remembered nor saved, a rejected saved sign-in is removed from
    ///     the user's store and replaced, and a working one the user chose to remember is saved.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_Credentials_KeptOnlyAfterSuccessAndForgottenWhenRejected()
    {
        // Arrange: a wrong password with Remember ticked
        var wrongId = UniqueId();
        var wrongFeed = await StartFeedAsync(wrongId, "1.0.0");
        RequireSignIn(wrongFeed);
        var wrongSaved = new FakeRememberedCredentialStore();
        var wrongSession = new SessionCredentialStore();
        var wrongPrompt = new FakeCredentialPrompt();
        wrongPrompt.EnqueueAnswer(new NuGetCredential(FeedUser, "wrong-" + SentinelPassword), remember: true);
        var wrong = CreateServerRegistry(
            wrongFeed, new PackageCredentialContext(wrongSession, wrongPrompt, wrongSaved)).Resolve("Server");

        // Arrange: a stale saved sign-in and a user who types the right password
        var staleId = UniqueId();
        var staleFeed = await StartFeedAsync(staleId, "1.0.0");
        RequireSignIn(staleFeed);
        var staleOrigin = FeedOrigin.Create(new Uri(staleFeed.IndexUrl));
        var staleSaved = new FakeRememberedCredentialStore();
        staleSaved.Save(staleOrigin, new NuGetCredential(FeedUser, "stale-" + SentinelPassword));
        var staleSession = new SessionCredentialStore();
        var stalePrompt = new FakeCredentialPrompt();
        stalePrompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword), remember: true);
        var stale = CreateServerRegistry(
            staleFeed, new PackageCredentialContext(staleSession, stalePrompt, staleSaved)).Resolve("Server");

        // Act
        await Assert.ThrowsAsync<PackageSourceAccessException>(() => wrong.ListVersionsAsync(wrongId, CancellationToken.None));
        var versions = await stale.ListVersionsAsync(staleId, CancellationToken.None);

        // Assert: nothing was kept for the wrong password
        Assert.Equal(0, wrongSession.Count);
        Assert.Equal(0, wrongSaved.CountOf("approve"));

        // The stale saved sign-in was removed once, the user asked once, and the working one is
        // kept for the session and saved because the user chose to remember it
        Assert.Single(versions);
        Assert.Equal(1, staleSaved.CountOf("reject"));
        Assert.Single(stalePrompt.Requests);
        Assert.True(staleSession.TryGet(staleOrigin, out var kept));
        Assert.Equal(SentinelPassword, kept!.Secret);
        Assert.Equal(1, staleSaved.CountOf("approve"));
        Assert.Equal(SentinelPassword, staleSaved.Peek(staleOrigin)!.Secret);
    }

    /// <summary>
    ///     Test that the user is asked for a credential only for the server's own origin, even
    ///     when its service index sends the client to resources on another host.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_ResourcesOnAnotherOrigin_PromptsOnlyForTheFeedOrigin()
    {
        // Arrange: the feed advertises its package resources on a second server that needs a
        // different account, so the operation cannot succeed
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        var other = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        other.RequiredUser = "other-user";
        other.RequiredPassword = "other-password";
        feed.ResourceHost = other;
        feed.IncludeRegistration = false;
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var provider = CreateServerRegistry(
            feed, new PackageCredentialContext(new SessionCredentialStore(), prompt)).Resolve("Server");

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert: the other origin was contacted and refused, but the user was asked once, for
        // the feed's own origin
        Assert.Equal(PackageSourceFailureKind.AuthenticationFailed, ex.Kind);
        Assert.NotEmpty(other.Requests);
        Assert.Equal(FeedOrigin.Create(new Uri(feed.IndexUrl)), Assert.Single(prompt.Requests).Origin);
    }

    /// <summary>
    ///     Test that each way a server can fail is reported as its own kind of failure - a sign-in
    ///     that is needed but cannot be asked for, a sign-in the user cancels, a sign-in that takes
    ///     too long, an address that is not a feed - with messages that carry no secret and not the
    ///     address path.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_Failures_AreDistinctAndNameNeitherSecretNorPath()
    {
        // Arrange: four servers, each failing in its own way
        var id = UniqueId();
        var noPromptFeed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(noPromptFeed);
        var noPrompt = CreateServerRegistry(
            noPromptFeed, new PackageCredentialContext(new SessionCredentialStore(), null)).Resolve("Server");

        var canceledFeed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(canceledFeed);
        var canceledPrompt = new FakeCredentialPrompt();
        canceledPrompt.EnqueueAnswer(null);
        var canceled = CreateServerRegistry(
            canceledFeed, new PackageCredentialContext(new SessionCredentialStore(), canceledPrompt)).Resolve("Server");

        var slowFeed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(slowFeed);
        var slow = CreateServerRegistry(
            slowFeed,
            new PackageCredentialContext(new SessionCredentialStore(), new FakeCredentialPrompt { NeverAnswer = true }),
            authenticationTimeout: TimeSpan.FromMilliseconds(500)).Resolve("Server");

        var anonymousFeed = await StartFeedAsync(id, "1.0.0");
        var notAFeed = PackageSourceRegistry.Create(
            [ServerDefinition("Server", anonymousFeed.BaseUrl + "/not-a-feed/index.json", allowInsecureHttp: true)],
            CreateTempDirectory()).Resolve("Server");

        // Act
        var needsSignIn = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => noPrompt.ListVersionsAsync(id, CancellationToken.None));
        var signInCanceled = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => canceled.ListVersionsAsync(id, CancellationToken.None));
        var timedOut = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => slow.ListVersionsAsync(id, CancellationToken.None));
        var unreachable = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => notAFeed.ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.AuthenticationFailed, needsSignIn.Kind);
        Assert.Equal(PackageSourceFailureKind.SignInCanceled, signInCanceled.Kind);
        Assert.Equal(PackageSourceFailureKind.TimedOut, timedOut.Kind);
        Assert.Equal(PackageSourceFailureKind.Unreachable, unreachable.Kind);
        Assert.DoesNotContain("not-a-feed", unreachable.Message, StringComparison.Ordinal);
        Assert.All(
            new[] { needsSignIn, signInCanceled, timedOut, unreachable },
            failure => Assert.DoesNotContain(SentinelPassword, failure.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a caller who cancels while the sign-in dialog is open ends the operation as a
    ///     cancellation, not as a failure.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_CallerCancelsWhileSigningIn_IsCancellationNotFailure()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt { NeverAnswer = true };
        var provider = CreateServerRegistry(
            feed, new PackageCredentialContext(new SessionCredentialStore(), prompt)).Resolve("Server");
        using var cancellation = new CancellationTokenSource();

        // Act: start, wait until the dialog is open, then cancel
        var pending = provider.ListVersionsAsync(id, cancellation.Token);
        var stopwatch = Stopwatch.StartNew();
        while (prompt.AskCount == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        await cancellation.CancelAsync();

        // Assert
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.IsNotType<PackageSourceAccessException>(canceled);
        Assert.Equal(1, prompt.AskCount);
    }

    /// <summary>
    ///     Test that no password, encoded credential or secret-bearing exception detail reaches the
    ///     application log or an exception, whether a sign-in succeeds or fails.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ServerSource_SignInFlows_NeverLogOrRaiseTheSecret()
    {
        // Arrange: one feed where the user types the right password, one where they mistype
        var id = UniqueId();
        var log = new ListLogger();
        var goodFeed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(goodFeed);
        var goodPrompt = new FakeCredentialPrompt();
        goodPrompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var good = CreateServerRegistry(
            goodFeed, new PackageCredentialContext(new SessionCredentialStore(), goodPrompt), log).Resolve("Server");

        var badFeed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(badFeed);
        var badPrompt = new FakeCredentialPrompt();
        badPrompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword + "-wrong"));
        var bad = CreateServerRegistry(
            badFeed, new PackageCredentialContext(new SessionCredentialStore(), badPrompt), log).Resolve("Server");

        // Act: an authenticated listing and download, then a failure
        var package = Assert.Single(await good.ListVersionsAsync(id, CancellationToken.None));
        using (await good.AcquireAsync(package, CancellationToken.None))
        {
            // The content is not needed; the download itself is the authenticated flow
        }

        var failure = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => bad.ListVersionsAsync(id, CancellationToken.None));

        // Assert: nothing logged, and no exception (inner exception included), carries a secret
        var forbidden = new[]
        {
            SentinelPassword,
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{FeedUser}:{SentinelPassword}")),
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{FeedUser}:{SentinelPassword}-wrong"))
        };
        var lines = log.Snapshot();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.All(forbidden, secret => Assert.DoesNotContain(secret, line, StringComparison.Ordinal)));
        Assert.All(forbidden, secret => Assert.DoesNotContain(secret, failure.ToString(), StringComparison.Ordinal));
        Assert.IsType<SanitizedException>(failure.InnerException);
    }

    /// <summary>
    ///     Starts a mock feed carrying versions of one package and tracks it for disposal.
    /// </summary>
    /// <param name="packageId">The package ID.</param>
    /// <param name="versions">The versions to serve.</param>
    /// <returns>The running feed.</returns>
    private async Task<MockNuGetFeed> StartFeedAsync(string packageId, params string[] versions)
    {
        var feed = await MockNuGetFeed.StartAsync();
        foreach (var version in versions)
        {
            feed.AddPackage(packageId, version);
        }

        _feeds.Add(feed);
        return feed;
    }

    /// <summary>
    ///     Makes a feed require the test account's credentials.
    /// </summary>
    /// <param name="feed">The feed.</param>
    private static void RequireSignIn(MockNuGetFeed feed)
    {
        feed.RequiredUser = FeedUser;
        feed.RequiredPassword = SentinelPassword;
    }

    /// <summary>
    ///     Creates a registry with one NuGet server source, aliased <c>Server</c>, over a mock feed.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="credentials">The credential facilities, or <see langword="null"/>.</param>
    /// <param name="logger">The logger to capture messages with, or <see langword="null"/>.</param>
    /// <param name="authenticationTimeout">The sign-in timeout, or <see langword="null"/> for the
    ///     default.</param>
    /// <param name="rootBase">The cache base folder, or <see langword="null"/> for a new temporary
    ///     one.</param>
    /// <returns>The registry.</returns>
    private PackageSourceRegistry CreateServerRegistry(
        MockNuGetFeed feed,
        PackageCredentialContext? credentials,
        ListLogger? logger = null,
        TimeSpan? authenticationTimeout = null,
        string? rootBase = null)
    {
        rootBase ??= CreateTempDirectory();
        return new PackageSourceRegistry(
            [ServerDefinition("Server", feed.IndexUrl, allowInsecureHttp: true)],
            definition => new NuGetSourceProvider(definition, rootBase, credentials, logger, authenticationTimeout));
    }

    /// <summary>
    ///     Creates a NuGet server source definition.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="location">The server address.</param>
    /// <param name="allowInsecureHttp">Whether plain http is allowed.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition ServerDefinition(string alias, string location, bool allowInsecureHttp) =>
        new()
        {
            Alias = alias,
            Type = PackageSourceType.NuGet,
            Location = location,
            PackageIds = [],
            AllowInsecureHttp = allowInsecureHttp
        };

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
    ///     Generates a package ID unique to one test.
    /// </summary>
    /// <returns>A unique package ID.</returns>
    private static string UniqueId() => "agentcontrol.test." + Guid.NewGuid().ToString("N");

    /// <summary>
    ///     Lists the signed fixture package from a source and tries to acquire it.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <param name="alias">The source alias.</param>
    /// <returns>The exception the acquisition ended with.</returns>
    private static async Task<InvalidOperationException> AcquireAsync(PackageSourceRegistry registry, string alias)
    {
        var provider = registry.Resolve(alias);
        var package = Assert.Single(await provider.ListVersionsAsync(SignedNuGetFixture.PackageId, CancellationToken.None));
        return await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, CancellationToken.None));
    }

    /// <summary>
    ///     Creates a NuGet folder-source definition.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="feed">The feed folder.</param>
    /// <param name="requireSigned">Whether the source requires signed packages.</param>
    /// <param name="fingerprint">The pinned publisher fingerprint, or <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    private static PackageSourceDefinition Definition(
        string alias, string feed, bool requireSigned, string? fingerprint = null) =>
        new()
        {
            Alias = alias,
            Type = PackageSourceType.NuGet,
            Location = feed,
            PackageIds = [],
            RequireSignedPackages = requireSigned,
            SignerFingerprint = fingerprint
        };

    /// <summary>
    ///     Writes a minimal agent package zip with the four agent files (<see cref="AgentFiles"/>), whose
    ///     agents file holds the given text.
    /// </summary>
    /// <param name="zipPath">The zip file to create.</param>
    /// <param name="marker">The text of the agents file.</param>
    private static void WriteZip(string zipPath, string marker)
    {
        using var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (path, text) in AgentFiles(marker))
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
    }

    /// <summary>
    ///     Test the Git source through the subsystem's own parts, against a real local repository:
    ///     the registry resolves the source, the cache lists tag versions with their commits and
    ///     reports the pinned version's listing entry, a moved tag shows as a different revision on
    ///     the next listing, and an acquired package reports the commit as its source revision (which
    ///     differs from the commit the moved tag points to now) and the identity of its files.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_GitSource_ListsTagsAcquiresCommitAndSeesMovedTag()
    {
        // Arrange: v1.0.0 and v1.1.0 in a local bare repository
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = new Dictionary<string, string>
        {
            [".github/agents/copilot.md"] = "one",
            ["release-notes.md"] = "notes"
        };
        var first = fixture.Commit(files, "one");
        fixture.Tag("v1.0.0", annotated: true);
        files[".github/agents/copilot.md"] = "two";
        fixture.Commit(files, "two");
        fixture.Tag("v1.1.0");
        fixture.Push();
        var registry = PackageSourceRegistry.Create([fixture.CreateDefinition()], git: fixture.CreateOptions());

        // Act: the version cache lists versions through the registry
        var cache = new PackageVersionCache();
        var check = await cache.IsNewerVersionAvailableAsync(
            registry, "Repo", "contoso-agents", "1.0.0", TestContext.Current.CancellationToken);

        // Assert: the newer version is offered, and the pinned one carries its commit
        Assert.True(check.IsNewerAvailable);
        Assert.Equal("1.1.0", check.Latest?.Version.ToString());
        Assert.Equal(first, check.Pinned?.Revision);

        // Act: the pinned version is acquired
        using var reader = await registry.Resolve("Repo").AcquireAsync(check.Pinned!, TestContext.Current.CancellationToken);

        // Assert: the source revision is the commit, and the content identity is that of the files
        Assert.Equal(PackageContentIdentity.FromGitCommit(first), reader.SourceRevision);
        Assert.True(PackageContentIdentity.IsFilesIdentity(reader.ContentIdentity));
        Assert.Equal("one", reader.ReadText(".github/agents/copilot.md"));

        // Act: the maintainer moves v1.0.0 and a new session lists again
        files[".github/agents/copilot.md"] = "moved";
        fixture.Commit(files, "moved");
        fixture.Tag("v1.0.0", force: true);
        fixture.Push();
        var later = await new PackageVersionCache().IsNewerVersionAvailableAsync(
            registry, "Repo", "contoso-agents", "1.0.0", TestContext.Current.CancellationToken);

        // Assert: the pinned version is now at another commit than the acquired package's source revision
        Assert.NotEqual(check.Pinned!.Revision, later.Pinned?.Revision);
        Assert.NotEqual(reader.SourceRevision, PackageContentIdentity.FromGitCommit(later.Pinned!.Revision!));
    }

    /// <summary>
    ///     Test that a Git source the user configured is described by its address, tag pattern,
    ///     sub-folder and exactly one package ID, written by the name of its type and nothing
    ///     secret-like, and read back as an equivalent description.
    /// </summary>
    [Fact]
    public void AgentPackageManagementSubsystem_GitSourceDefinition_IsPersistedByNameAndReadBackEquivalent()
    {
        // Arrange
        var original = ValidGitDefinition();

        // Act
        var json = JsonSerializer.Serialize(original);
        var loaded = JsonSerializer.Deserialize<PackageSourceDefinition>(json)!;

        // Assert: the type by name, every Git field kept, no property that could hold a secret
        Assert.Multiple(
            () => Assert.Contains("\"Type\":\"Git\"", json, StringComparison.Ordinal),
            () => Assert.Equal(PackageSourceType.Git, loaded.Type),
            () => Assert.Equal("git@example.invalid:org/agents.git", loaded.Location),
            () => Assert.Equal("release/{version}", loaded.TagPattern),
            () => Assert.Equal("packaging/agents", loaded.Subfolder),
            () => Assert.Equal(["contoso-agents"], loaded.PackageIds),
            () => Assert.True(original.IsEquivalentTo(loaded)),
            () => Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase),
            () => Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Test that the settings of a Git source are checked as a whole - the address, the tag pattern,
    ///     the sub-folder (a relative path of folder names that cannot leave the repository) and the
    ///     single package ID - and that a source with any of them wrong is refused when it is resolved,
    ///     with a message that does not repeat the address, while a valid one is resolved.
    /// </summary>
    [Theory]
    [InlineData("address")]
    [InlineData("pattern")]
    [InlineData("subfolder-traversal")]
    [InlineData("subfolder-git-folder")]
    [InlineData("no-package-id")]
    [InlineData("two-package-ids")]
    public void AgentPackageManagementSubsystem_GitSourceDefinition_SettingsAreCheckedAsAWholeAndInvalidSourceIsRefusedWhenResolved(
        string wrong)
    {
        // Arrange: a valid source with one part made wrong
        var valid = ValidGitDefinition();
        var invalid = ValidGitDefinition();
        invalid.Alias = "Broken";
        switch (wrong)
        {
            case "address":
                invalid.Location = "http://sentinel-host.invalid/org/agents.git";
                break;
            case "pattern":
                invalid.TagPattern = "release/";
                break;
            case "subfolder-traversal":
                invalid.Subfolder = "../outside";
                break;
            case "subfolder-git-folder":
                invalid.Subfolder = "packaging/.git";
                break;
            case "no-package-id":
                invalid.PackageIds = [];
                break;
            default:
                invalid.PackageIds = ["one", "two"];
                break;
        }

        var options = new GitSourceOptions(() => "git", CreateTempDirectory());
        var registry = PackageSourceRegistry.Create([valid, invalid], git: options);

        // Act
        var refusal = Assert.Throws<InvalidOperationException>(() => registry.Resolve("Broken"));
        var provider = registry.Resolve("Repo");

        // Assert: the whole description is judged, the wrong source is refused without repeating its address
        Assert.Multiple(
            () => Assert.Null(valid.GetGitSettingsProblem()),
            () => Assert.NotNull(invalid.GetGitSettingsProblem()),
            () => Assert.NotNull(provider),
            () => Assert.DoesNotContain("sentinel-host", refusal.Message, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the address of a Git source is judged by the rules of Git addresses: the https, ssh,
    ///     <c>user@host:path</c> and local forms are accepted (an ssh user name is no credential), while an
    ///     address that carries a password, token or query string, another transport, a remote-helper
    ///     form or an option-like text is refused when the source is resolved, without the message
    ///     repeating the address.
    /// </summary>
    [Theory]
    [InlineData("https://example.invalid/org/agents.git", true)]
    [InlineData("ssh://git@example.invalid/org/agents.git", true)]
    [InlineData("git@example.invalid:org/agents.git", true)]
    [InlineData("local-path", true)]
    [InlineData("https://user:SentinelSecretValue@example.invalid/org/agents.git", false)]
    [InlineData("https://SentinelSecretValue@example.invalid/org/agents.git", false)]
    [InlineData("ssh://user:SentinelSecretValue@example.invalid/org/agents.git", false)]
    [InlineData("https://example.invalid/org/agents.git?token=SentinelSecretValue", false)]
    [InlineData("http://example.invalid/SentinelSecretValue.git", false)]
    [InlineData("ext::sh -c SentinelSecretValue", false)]
    [InlineData("-uSentinelSecretValue", false)]
    public void AgentPackageManagementSubsystem_GitSourceAddress_FollowsTheRulesOfGitAddresses(string address, bool acceptable)
    {
        // Arrange: a source with the address (a fully qualified path stands for the local form)
        var definition = ValidGitDefinition();
        definition.Location = address == "local-path" ? Path.Combine(CreateTempDirectory(), "agents.git") : address;
        var registry = PackageSourceRegistry.Create(
            [definition], git: new GitSourceOptions(() => "git", CreateTempDirectory()));

        // Act
        var problem = definition.GetGitSettingsProblem();
        var refusal = Record.Exception(() => registry.Resolve("Repo"));

        // Assert: accepted exactly when the rules allow it, and a refusal never repeats the address
        Assert.Equal(acceptable, problem is null);
        Assert.Equal(acceptable, refusal is null);
        Assert.DoesNotContain("SentinelSecretValue", problem ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("SentinelSecretValue", refusal?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test, through the registry and with the real git, that a tag whose layout holds a symbolic link, a
    ///     submodule or names that collide on a case-insensitive file system is refused with a message that
    ///     names the problem, that nothing of it is left in the cache, and that the layout of an ordinary tag
    ///     of the same repository is still supplied.
    /// </summary>
    [Theory]
    [InlineData("symbolic-link", "a symbolic link")]
    [InlineData("submodule", "a submodule")]
    [InlineData("case-collision", "differ only in letter case or Unicode form")]
    public async Task AgentPackageManagementSubsystem_GitSource_HostileLayoutIsRefusedAndNothingIsCached(string kind, string expected)
    {
        // Arrange: an ordinary tag v1.0.0 and a hostile tag v1.1.0 in one repository
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = new Dictionary<string, string>
        {
            [".github/agents/copilot.md"] = "good",
            ["release-notes.md"] = "notes"
        };
        var good = fixture.Commit(files, "good");
        fixture.Tag("v1.0.0");
        switch (kind)
        {
            case "symbolic-link":
                fixture.AddSymbolicLink(".github/agents/evil.md", "../../../outside.txt");
                break;
            case "submodule":
                fixture.AddSubmodule(".github/skills/sub", good);
                break;
            default:
                fixture.AddFile(".github/agents/A.md", "first");
                fixture.AddFile(".github/agents/a.md", "second");
                break;
        }

        fixture.CommitStaged("hostile " + kind);
        fixture.Tag("v1.1.0");
        fixture.Push();
        var registry = PackageSourceRegistry.Create([fixture.CreateDefinition()], git: fixture.CreateOptions());
        var provider = (GitSourceProvider)registry.Resolve("Repo");
        var packages = (await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken))
            .ToDictionary(package => package.Version.ToString());

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(packages["1.1.0"], TestContext.Current.CancellationToken));
        using var ordinary = await provider.AcquireAsync(packages["1.0.0"], TestContext.Current.CancellationToken);

        // Assert: refused for the right reason with nothing of the hostile tag in the cache, the ordinary tag supplied
        Assert.Multiple(
            () => Assert.Contains(expected, refusal.Message, StringComparison.Ordinal),
            () => Assert.False(Directory.Exists(Path.Combine(provider.RepositoryCacheFolder, packages["1.1.0"].Revision!))),
            () => Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*")),
            () => Assert.Equal("good", ordinary.ReadText(".github/agents/copilot.md")));
    }

    /// <summary>
    ///     Test, through the registry and with the real git, that code which the user's own Git configuration
    ///     would run for a repository - a post-checkout hook installed into every new repository by a template
    ///     directory and a global hooks path - never runs while a package is listed and fetched, and that the
    ///     package is supplied normally.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_GitSource_HooksOfTheUsersConfigurationNeverRun()
    {
        // Arrange: a repository, and a user environment that installs a post-checkout hook everywhere
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string> { [".github/agents/copilot.md"] = "hooked", ["release-notes.md"] = "notes" }, "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var marker = Path.Combine(fixture.Root, "hook-ran.txt");
        var hostile = fixture.CreateHostileHooksEnvironment(marker);

        // Control: plain git with the same environment does run the hook, so the test is meaningful
        var control = Path.Combine(fixture.Root, "control");
        fixture.RunWithEnvironment(hostile, fixture.Root, "init", "-q", control);
        fixture.RunWithEnvironment(hostile, control, "fetch", "-q", fixture.RemotePath, "refs/tags/v1.0.0:refs/tags/v1.0.0");
        fixture.RunWithEnvironment(hostile, control, "checkout", "-q", "refs/tags/v1.0.0", "--", ".github");
        if (!File.Exists(marker))
        {
            Assert.Skip("Hooks do not run in this environment, so their neutralization cannot be demonstrated.");
        }

        File.Delete(marker);
        var registry = PackageSourceRegistry.Create(
            [fixture.CreateDefinition()], git: fixture.CreateOptions() with { ExtraEnvironment = hostile });
        var provider = registry.Resolve("Repo");

        // Act
        var package = Assert.Single(await provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert: supplied, and no hook ran
        Assert.Equal("hooked", reader.ReadText(".github/agents/copilot.md"));
        Assert.False(File.Exists(marker));
    }

    /// <summary>
    ///     Test, through the registry and with the real git, that a Git source that cannot be used reports
    ///     why in the failure type its callers act on, without repeating the address or git's own text: a
    ///     repository that is not there is "not found", a git that cannot be started points to Settings, and a
    ///     cancelled request stops with a cancellation.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_GitSource_FailuresAreClassifiedAndNeverRepeatTheAddress()
    {
        // Arrange: a source whose repository does not exist, and the same with a git that cannot be started
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var definition = fixture.CreateDefinition();
        definition.Location = Path.Combine(fixture.Root, "sentinel-missing-repository.git");
        var registry = PackageSourceRegistry.Create([definition], git: fixture.CreateOptions());
        var noGit = PackageSourceRegistry.Create(
            [definition], git: fixture.CreateOptions() with { ResolveGitExecutable = () => Path.Combine(fixture.Root, "no-git") });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var missing = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => registry.Resolve("Repo").ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));
        var unavailable = await Assert.ThrowsAsync<InvalidOperationException>(
            () => noGit.Resolve("Repo").ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));
        var stopped = await Record.ExceptionAsync(
            () => registry.Resolve("Repo").ListVersionsAsync("contoso-agents", cancelled.Token));

        // Assert
        Assert.Multiple(
            () => Assert.Equal(PackageSourceFailureKind.NotFound, missing.Kind),
            () => Assert.DoesNotContain("sentinel-missing-repository", missing.Message, StringComparison.Ordinal),
            () => Assert.Contains("Settings", unavailable.Message, StringComparison.Ordinal),
            () => Assert.IsAssignableFrom<OperationCanceledException>(stopped));
    }

    /// <summary>
    ///     Test, through the registry and with a stub standing in for git, that a Git source lets git show its
    ///     sign-in only for work the user started: the same listing started by the application itself runs git
    ///     with every prompt switched off, and started by the user it leaves the credential manager free.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_GitSource_OnlyUserStartedWorkMayPrompt()
    {
        // Arrange: a stub git that records its environment, and two registries over one source
        var stub = GitRemoteStub.Create();
        try
        {
            var definition = ValidGitDefinition();
            definition.Location = "https://example.invalid/org/agents.git";
            var folder = CreateTempDirectory();
            var background = PackageSourceRegistry.Create(
                [definition], git: new GitSourceOptions(() => stub.Path, Path.Combine(folder, "background")));
            var userStarted = PackageSourceRegistry.Create(
                [definition], git: new GitSourceOptions(() => stub.Path, Path.Combine(folder, "user"), Interactive: true));

            // Act
            await background.Resolve("Repo").ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);
            await userStarted.Resolve("Repo").ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken);
            var listings = stub.ReadInvocations().Where(invocation => invocation.Command == "ls-remote").ToList();

            // Assert: the application's own work cannot prompt, the user's can (git still never reads a terminal)
            Assert.Equal(2, listings.Count);
            Assert.Multiple(
                () => Assert.Equal("never", listings[0].Environment["GCM_INTERACTIVE"]),
                () => Assert.Equal("0", listings[0].Environment["GIT_TERMINAL_PROMPT"]),
                () => Assert.NotEqual("never", listings[1].Environment["GCM_INTERACTIVE"]),
                () => Assert.Equal("0", listings[1].Environment["GIT_TERMINAL_PROMPT"]));
        }
        finally
        {
            stub.Delete();
        }
    }

    /// <summary>
    ///     Test, through the registry and with a stub standing in for git, that a git older than the supported
    ///     minimum is refused with advice for every use of the source - listing, previewing and fetching - before
    ///     any other git command runs.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_GitSource_TooOldGitIsRefusedWithAdviceBeforeAnyTagIsListed()
    {
        // Arrange: a stub that reports an old git
        var stub = GitRemoteStub.Create().Respond("version", "git version 2.20.1\n");
        try
        {
            var registry = PackageSourceRegistry.Create(
                [ValidGitDefinition()],
                git: new GitSourceOptions(() => stub.Path, CreateTempDirectory(), EnforceMinimumGitVersion: true));
            var provider = registry.Resolve("Repo");

            // Act
            var failure = await Assert.ThrowsAsync<GitRemoteException>(
                () => provider.ListVersionsAsync("contoso-agents", TestContext.Current.CancellationToken));

            // Assert: refused with advice, and only the version was ever asked of git
            Assert.Multiple(
                () => Assert.Equal(GitFailureKind.GitTooOld, failure.Kind),
                () => Assert.Contains("Settings > Git executable", failure.Message, StringComparison.Ordinal),
                () => Assert.All(stub.ReadInvocations(), invocation => Assert.Equal("version", invocation.Command)));
        }
        finally
        {
            stub.Delete();
        }
    }

    /// <summary>
    ///     Creates a valid Git source description.
    /// </summary>
    /// <returns>The definition, with the alias <c>Repo</c>.</returns>
    private static PackageSourceDefinition ValidGitDefinition() => new()
    {
        Alias = "Repo",
        Type = PackageSourceType.Git,
        Location = "git@example.invalid:org/agents.git",
        TagPattern = "release/{version}",
        Subfolder = "packaging/agents",
        PackageIds = ["contoso-agents"]
    };

    /// <summary>
    ///     Creates a unique temporary directory removed when the test ends.
    /// </summary>
    /// <returns>The directory path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_pm_subsystem_" + Guid.NewGuid().ToString("N"));
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
