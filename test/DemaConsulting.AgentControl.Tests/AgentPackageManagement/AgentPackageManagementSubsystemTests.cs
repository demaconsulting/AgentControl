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
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoSync;
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
    ///     Test that, through the registry, zip and NuGet sources each report the identity of the
    ///     package bytes they deliver, that the identity is stable across acquisitions, and that a
    ///     zip republished under the same version is reported as a changed identity.
    /// </summary>
    [Fact]
    public async Task AgentPackageManagementSubsystem_ZipAndNuGetSources_ReportContentIdentityAndDetectRepublishedZip()
    {
        // Arrange: a zip source and a NuGet folder source, each with one package
        var zipFolder = CreateTempDirectory();
        var feed = CreateTempDirectory();
        var id = UniqueId();
        var zipPath = Path.Combine(zipFolder, "contoso-agents-1.0.0.zip");
        WriteZip(zipPath, "original");
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

        var expectedZipFirst = Sha256Identity(await File.ReadAllBytesAsync(zipPath, TestContext.Current.CancellationToken));
        var expectedNuGet = Sha256Identity(await File.ReadAllBytesAsync(
            Assert.Single(Directory.GetFiles(feed, "*.nupkg", SearchOption.AllDirectories)), TestContext.Current.CancellationToken));
        File.Delete(zipPath);
        WriteZip(zipPath, "republished");
        var expectedZipRepublished = Sha256Identity(await File.ReadAllBytesAsync(zipPath, TestContext.Current.CancellationToken));
        using var zipAgain = await zips.AcquireAsync(zipPackage, CancellationToken.None);
        using var nugetAgain = await nuget.AcquireAsync(nugetPackage, CancellationToken.None);

        // Assert: each identity is exactly the SHA-256 of the delivered file's bytes, computed here
        // independently of the production hashing code
        Assert.Equal(expectedZipFirst, zipFirst);
        Assert.Equal(expectedNuGet, nugetFirst);
        Assert.Equal(expectedZipRepublished, zipAgain.ContentIdentity);
        Assert.NotEqual(expectedZipFirst, expectedZipRepublished);

        // Assert: the NuGet identity is stable, and only the zip changed
        Assert.Equal(ContentIdentityComparison.Same, PackageContentIdentity.Compare(nugetFirst, nugetAgain.ContentIdentity));
        Assert.Equal(ContentIdentityComparison.Changed, PackageContentIdentity.Compare(zipFirst, zipAgain.ContentIdentity));
    }

    /// <summary>
    ///     Computes the expected <c>sha256:</c> identity of some bytes directly with the platform's
    ///     hash, so a test does not rely on the code it checks.
    /// </summary>
    /// <param name="bytes">The bytes of the delivered file.</param>
    /// <returns><c>sha256:</c> followed by the lower-case hexadecimal digest.</returns>
    private static string Sha256Identity(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

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
    ///     Writes a minimal agent package zip whose agents file holds the given text.
    /// </summary>
    /// <param name="zipPath">The zip file to create.</param>
    /// <param name="marker">The text of the agents file.</param>
    private static void WriteZip(string zipPath, string marker)
    {
        using var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(".github/agents/copilot.md").Open());
        writer.Write(marker);
    }

    /// <summary>
    ///     Test the Git source through the subsystem's own parts, against a real local repository:
    ///     the registry resolves the source, the cache lists tag versions with their commits and
    ///     reports the pinned version's listing entry, a moved tag shows as a different revision on
    ///     the next listing, and an acquired package reports the commit as its content identity (and
    ///     compares as changed against the identity of the original commit).
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

        // Assert: the content identity is the commit
        Assert.Equal(PackageContentIdentity.FromGitCommit(first), reader.ContentIdentity);
        Assert.Equal("one", reader.ReadText(".github/agents/copilot.md"));

        // Act: the maintainer moves v1.0.0 and a new session lists again
        files[".github/agents/copilot.md"] = "moved";
        fixture.Commit(files, "moved");
        fixture.Tag("v1.0.0", force: true);
        fixture.Push();
        var later = await new PackageVersionCache().IsNewerVersionAvailableAsync(
            registry, "Repo", "contoso-agents", "1.0.0", TestContext.Current.CancellationToken);

        // Assert: the pinned version is now at another commit, which compares as changed
        Assert.NotEqual(check.Pinned!.Revision, later.Pinned?.Revision);
        Assert.Equal(
            ContentIdentityComparison.Changed,
            PackageContentIdentity.Compare(
                reader.ContentIdentity, PackageContentIdentity.FromGitCommit(later.Pinned!.Revision!)));
    }

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
