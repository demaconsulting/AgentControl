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
///     Tests for <see cref="NuGetSourceProvider"/> against a NuGet V3 server: an in-process Kestrel
///     mock feed (<see cref="MockNuGetFeed"/>) served over plain HTTP, which the real NuGet client
///     talks to, with a scripted credential prompt standing in for the dialog.
/// </summary>
/// <remarks>
///     HTTP is used so no TLS certificate has to be trusted in CI; the product refuses plain HTTP
///     unless a source opts in with <see cref="PackageSourceDefinition.AllowInsecureHttp"/>, which
///     these tests set. Every test starts its own feed (a fresh port, because NuGet caches service
///     indexes per URL), its own cache base folder, and uses package IDs unique to the test.
/// </remarks>
public sealed class NuGetSourceProviderServerTests : IAsyncLifetime
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
    ///     The feeds started by the running test, stopped on disposal.
    /// </summary>
    private readonly List<MockNuGetFeed> _feeds = [];

    /// <summary>
    ///     The temporary folders created by the running test, removed on disposal.
    /// </summary>
    private readonly List<string> _tempPaths = [];

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var feed in _feeds)
        {
            await feed.DisposeAsync();
        }

        foreach (var path in _tempPaths)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of a temporary folder
            }
        }
    }

    /// <summary>
    ///     Test that listing versions from an anonymous server returns the versions stamped with
    ///     the alias and downloads no package content.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_AnonymousServer_ReturnsVersionsWithoutDownloading()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0", "1.1.0");
        var provider = CreateProvider(feed, credentials: null);

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert: both versions, from the server, and not one package file was requested
        Assert.Equal(["1.0.0", "1.1.0"], versions.OrderBy(v => v.Version).Select(v => v.Version.ToString()));
        Assert.All(versions, package => Assert.Equal("Feed", package.SourceAlias));
        Assert.Equal(0, feed.DownloadCount);
        Assert.DoesNotContain(feed.Requests, request => request.EndsWith(".nupkg", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a package from an anonymous server is acquired and its content read.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_AnonymousServer_DownloadsPackageAndExposesContent()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        var provider = CreateProvider(feed, credentials: null);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert
        Assert.Equal("agent content from the feed 1.0.0", ReadFile(reader, ".github/agents/copilot.md"));
        Assert.Equal(1, feed.DownloadCount);
    }

    /// <summary>
    ///     Test that a package from an anonymous server reports, as its content identity, the SHA-256
    ///     of the package file that was downloaded into the source's private cache.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_AnonymousServer_ReportsSha256OfDownloadedPackageFile()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        var provider = CreateProvider(feed, credentials: null);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        using var reader = await provider.AcquireAsync(package, CancellationToken.None);

        // Assert: the identity is the SHA-256 of the downloaded .nupkg, computed here independently
        var downloaded = Assert.Single(Directory.GetFiles(reader.Description, "*.nupkg"));
        var expected = "sha256:" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(downloaded, TestContext.Current.CancellationToken)));
        Assert.Equal(expected, reader.ContentIdentity);
    }

    /// <summary>
    ///     Test that a server that needs a sign-in asks the user once, uses the answer, succeeds
    ///     and remembers the credential for the session.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_ServerNeedsSignIn_PromptsOnceAndRemembersCredentialForTheSession()
    {
        // Arrange: a protected feed and a user who types the right password
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var store = new SessionCredentialStore();
        var provider = CreateProvider(feed, new PackageCredentialContext(store, prompt));

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert: it worked after exactly one question, which named this feed, and the credential
        // that worked is remembered for the session
        Assert.Equal("1.0.0", Assert.Single(versions).Version.ToString());
        var request = Assert.Single(prompt.Requests);
        Assert.Equal(FeedOrigin.Create(new Uri(feed.IndexUrl)), request.Origin);
        Assert.Equal("Feed", request.SourceAlias);
        Assert.False(request.PreviousCredentialRejected);
        Assert.True(store.TryGet(request.Origin, out var remembered));
        Assert.Equal(SentinelPassword, remembered!.Secret);
        Assert.True(feed.UnauthorizedCount > 0);
    }

    /// <summary>
    ///     Test that a credential typed for one operation is reused by the next operation on the
    ///     same feed without asking again (the package download needs the credential again).
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_AfterSignInInAnEarlierOperation_ReusesSessionCredentialWithoutPrompting()
    {
        // Arrange: sign in through a version listing
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var context = new PackageCredentialContext(new SessionCredentialStore(), prompt);
        var provider = CreateProvider(feed, context);
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act: a second operation, on a second provider for the same source and the same context
        using var reader = await CreateProvider(feed, context).AcquireAsync(package, CancellationToken.None);

        // Assert: still one question in total, and the content came through authenticated
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal("agent content from the feed 1.0.0", ReadFile(reader, ".github/agents/copilot.md"));
    }

    /// <summary>
    ///     Test that a wrong password produces an authentication failure after a single prompt,
    ///     with no retry loop, a message without the password, and nothing remembered.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_WrongCredentials_ThrowsAuthenticationFailedAfterOnePrompt()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, "wrong-" + SentinelPassword));
        var store = new SessionCredentialStore();
        var provider = CreateProvider(feed, new PackageCredentialContext(store, prompt));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.AuthenticationFailed, ex.Kind);
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(0, store.Count);
        Assert.Contains("rejected the credentials", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, ex.ToString(), StringComparison.Ordinal);

        // The library's exception object is not kept, only a secret-free description of it
        Assert.IsType<SanitizedException>(ex.InnerException);
    }

    /// <summary>
    ///     Test that cancelling the sign-in dialog ends the operation with a "sign-in canceled"
    ///     failure after one prompt, with no retry loop.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_UserCancelsSignIn_ThrowsSignInCanceledAfterOnePrompt()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(null);
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), prompt));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignInCanceled, ex.Kind);
        Assert.Equal(1, prompt.AskCount);
    }

    /// <summary>
    ///     Test that an operation with no prompt available (background work) fails with "needs a
    ///     sign-in" and never asks.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_ServerNeedsSignInAndNoPrompt_ThrowsAuthenticationFailedWithoutAsking()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), null));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.AuthenticationFailed, ex.Kind);
        Assert.Contains("needs a sign-in", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a server that answers HTTP 403 (Forbidden) with no credential available is
    ///     reported as an authentication failure, the same way a 401 is, and never asks.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_ServerAnswersForbiddenAndNoPrompt_ThrowsAuthenticationFailedWithoutAsking()
    {
        // Arrange: a feed that refuses every request with 403, and no prompt available
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        feed.ForbidAllRequests = true;
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), null));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.AuthenticationFailed, ex.Kind);
        Assert.Contains("needs a sign-in", ex.Message, StringComparison.Ordinal);
        Assert.True(feed.ForbiddenCount > 0);
    }

    /// <summary>
    ///     Test that a server that answers HTTP 403 (Forbidden) even to the credential the user
    ///     typed is reported as an authentication failure after a single prompt, with no retry loop
    ///     and nothing remembered.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_ServerAnswersForbiddenToTheCredential_ThrowsAuthenticationFailedAfterOnePrompt()
    {
        // Arrange: a feed that refuses every request with 403, and a user who types a credential
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        feed.ForbidAllRequests = true;
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var store = new SessionCredentialStore();
        var provider = CreateProvider(feed, new PackageCredentialContext(store, prompt));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.AuthenticationFailed, ex.Kind);
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(0, store.Count);
        Assert.Contains("rejected the credentials", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelPassword, ex.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a session credential the server rejects is forgotten, the user is asked again
    ///     (and told the last one failed), and the new credential replaces it.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_SessionCredentialRejected_ForgetsItAndPromptsAgain()
    {
        // Arrange: a stale credential in the store, and a user who types the right one
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var store = new SessionCredentialStore();
        var origin = FeedOrigin.Create(new Uri(feed.IndexUrl));
        store.Set(origin, new NuGetCredential(FeedUser, "stale-password"));
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var provider = CreateProvider(feed, new PackageCredentialContext(store, prompt));

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert
        Assert.Single(versions);
        var request = Assert.Single(prompt.Requests);
        Assert.True(request.PreviousCredentialRejected);
        Assert.True(store.TryGet(origin, out var current));
        Assert.Equal(SentinelPassword, current!.Secret);
    }

    /// <summary>
    ///     Test that a challenge from a different origin - here a server the feed's service index
    ///     sends the client to - never opens a prompt: the user is asked for the feed's origin only,
    ///     and the other origin's own, different credentials are never requested from the user.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_ResourcesOnAnotherOrigin_PromptsOnlyForTheFeedOrigin()
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
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), prompt));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert: the other origin was contacted and refused, but the user was asked once, for
        // the feed's own origin
        Assert.Equal(PackageSourceFailureKind.AuthenticationFailed, ex.Kind);
        Assert.NotEmpty(other.Requests);
        var request = Assert.Single(prompt.Requests);
        Assert.Equal(FeedOrigin.Create(new Uri(feed.IndexUrl)), request.Origin);
    }

    /// <summary>
    ///     Test that a sign-in that takes longer than the allowed time is stopped and reported as a
    ///     timeout, and not as a cancellation by the caller.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_SignInTakesTooLong_ThrowsTimedOut()
    {
        // Arrange: a dialog nobody answers, with a short allowed time
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt { NeverAnswer = true };
        var provider = CreateProvider(
            feed,
            new PackageCredentialContext(new SessionCredentialStore(), prompt),
            authenticationTimeout: TimeSpan.FromMilliseconds(500));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(id, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.TimedOut, ex.Kind);
        Assert.Equal(1, prompt.AskCount);
    }

    /// <summary>
    ///     Test that cancelling the caller's token while the dialog is open propagates as a
    ///     cancellation, not as a failure.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_CallerCancelsWhileSigningIn_ThrowsOperationCanceled()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var prompt = new FakeCredentialPrompt { NeverAnswer = true };
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), prompt));
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, prompt.AskCount);
    }

    /// <summary>
    ///     Test that an address that does not answer as a NuGet feed (the server is up but has no
    ///     service index there) is reported as unreachable, with a message that names the feed's
    ///     origin but not its path.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_AddressIsNotANuGetFeed_ThrowsUnreachable()
    {
        // Arrange: a running server, but the configured address has no service index
        var feed = await StartFeedAsync(UniqueId(), "1.0.0");
        var definition = Definition(feed.BaseUrl + "/not-a-feed/index.json", allowInsecureHttp: true);
        var provider = new NuGetSourceProvider(definition, CreateTempDirectory());

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(UniqueId(), CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.Unreachable, ex.Kind);
        Assert.Contains("could not be reached", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-feed", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a package the server does not carry yields an empty version list, which a
    ///     caller can report as "not found".
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_PackageNotOnServer_ReturnsEmptyList()
    {
        // Arrange
        var feed = await StartFeedAsync(UniqueId(), "1.0.0");
        var provider = CreateProvider(feed, credentials: null);

        // Act
        var versions = await provider.ListVersionsAsync(UniqueId(), CancellationToken.None);

        // Assert
        Assert.Empty(versions);
    }

    /// <summary>
    ///     Test that credentials in the user's own NuGet configuration for the same feed address
    ///     are used without any prompt (credential tier 1: existing NuGet configuration).
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_NuGetConfigHasCredentialsForTheFeed_SignsInWithoutPrompting()
    {
        // Arrange: a protected feed, and a nuget.config in an ancestor of the source's cache root
        // that lists the same address with stored credentials
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var rootBase = CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(rootBase, "nuget.config"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration>\n  <packageSources>\n" +
            $"    <add key=\"Configured\" value=\"{feed.IndexUrl}\" allowInsecureConnections=\"true\" />\n" +
            "  </packageSources>\n  <packageSourceCredentials>\n    <Configured>\n" +
            $"      <add key=\"Username\" value=\"{FeedUser}\" />\n" +
            $"      <add key=\"ClearTextPassword\" value=\"{SentinelPassword}\" />\n" +
            "    </Configured>\n  </packageSourceCredentials>\n</configuration>\n",
            TestContext.Current.CancellationToken);
        var prompt = new FakeCredentialPrompt();
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), prompt), rootBase: rootBase);

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert
        Assert.Single(versions);
        Assert.Equal(0, prompt.AskCount);
    }

    /// <summary>
    ///     Test that no secret reaches the log during a flow that signs in successfully, and that
    ///     an authentication failure with a wrong password logs no secret either.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_SignInFlows_NeverLogTheSecret()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var log = new ListLogger();
        var goodPrompt = new FakeCredentialPrompt();
        goodPrompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var good = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), goodPrompt), log);

        // A second protected feed (its own port, so nothing is cached) where the user mistypes
        var badFeed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(badFeed);
        var badPrompt = new FakeCredentialPrompt();
        badPrompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword + "-wrong"));
        var bad = CreateProvider(badFeed, new PackageCredentialContext(new SessionCredentialStore(), badPrompt), log);

        // Act: a successful authenticated listing and download, then a failure
        var package = Assert.Single(await good.ListVersionsAsync(id, CancellationToken.None));
        using (await good.AcquireAsync(package, CancellationToken.None))
        {
            // The content is not needed; the download itself is the authenticated flow
        }

        var failure = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => bad.ListVersionsAsync(id, CancellationToken.None));

        // Assert: nothing logged (or in the failure text) contains a password or an encoded credential
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
    }

    /// <summary>
    ///     Test that a plain <c>http</c> location is refused unless the source allows insecure
    ///     HTTP, and accepted when it does.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_Constructor_PlainHttp_RefusedUnlessInsecureHttpIsAllowed()
    {
        // Arrange
        var definition = Definition("http://127.0.0.1:5999/v3/index.json", allowInsecureHttp: false);

        // Act / Assert: refused with the remedy, then accepted after the opt-in
        var ex = Assert.Throws<InvalidOperationException>(() => new NuGetSourceProvider(definition, CreateTempDirectory()));
        Assert.Contains("Allow insecure HTTP", ex.Message, StringComparison.Ordinal);
        definition.AllowInsecureHttp = true;
        Assert.NotNull(new NuGetSourceProvider(definition, CreateTempDirectory()));
    }

    /// <summary>
    ///     Test that server locations the NuGet client would not treat as a V3 index, or that carry
    ///     credentials, are refused without repeating the secret.
    /// </summary>
    /// <param name="location">The invalid location.</param>
    [Theory]
    [InlineData("https://feeds.example.com/v3/")]
    [InlineData("https://feeds.example.com/v3/index.json?token=sentinel-secret")]
    [InlineData("https://user:sentinel-secret@feeds.example.com/v3/index.json")]
    [InlineData("https://feeds.example.com/v3/index.json#sentinel-secret")]
    public void NuGetSourceProvider_Constructor_InvalidServerLocation_ThrowsWithoutRepeatingTheLocation(string location)
    {
        // Arrange
        var definition = Definition(location, allowInsecureHttp: false);

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => new NuGetSourceProvider(definition, CreateTempDirectory()));

        // Assert
        Assert.DoesNotContain("sentinel-secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("feeds.example.com", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a valid https server location is accepted and that the generated per-source
    ///     cache root is under the supplied base, never the repo or current directory.
    /// </summary>
    [Fact]
    public void NuGetSourceProvider_Constructor_HttpsServerLocation_IsAcceptedWithCacheRootUnderBase()
    {
        // Arrange
        var rootBase = CreateTempDirectory();
        var definition = Definition("https://feeds.example.com/artifactory/api/nuget/v3/agents/index.json", false);

        // Act
        var provider = new NuGetSourceProvider(definition, rootBase);

        // Assert
        Assert.StartsWith(rootBase, provider.RootFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a sign-in the user saved earlier with their credential helper is used without
    ///     asking - even though a prompt is available - and that the credential is remembered for
    ///     the session as one that the user's store holds too.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_SavedSignInExists_UsedWithoutAsking()
    {
        // Arrange: a protected feed, a saved sign-in, and a prompt that must not be used
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var origin = FeedOrigin.Create(new Uri(feed.IndexUrl));
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(origin, new NuGetCredential(FeedUser, SentinelPassword));
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        var provider = CreateProvider(feed, new PackageCredentialContext(store, prompt, usersStore));

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert
        Assert.Equal("1.0.0", Assert.Single(versions).Version.ToString());
        Assert.Equal(0, prompt.AskCount);
        Assert.Equal(1, usersStore.CountOf("fill"));
        Assert.True(store.IsFromRememberedStore(origin));
    }

    /// <summary>
    ///     Test that background work (no prompt available) also signs in with a saved sign-in, so a
    ///     repo card's upgrade check does not need the user once they chose to remember the sign-in.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_NoPromptButSavedSignIn_BackgroundCheckSucceeds()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(FeedOrigin.Create(new Uri(feed.IndexUrl)), new NuGetCredential(FeedUser, SentinelPassword));
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert
        Assert.Single(versions);
    }

    /// <summary>
    ///     Test that a saved sign-in the server rejects (the password was changed, say) is removed
    ///     from the user's credential store and forgotten for the session, and the user is then asked
    ///     exactly once - with the dialog saying the last sign-in did not work and offering to
    ///     remember the new one.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_SavedSignInRejected_RemovedFromTheUsersStoreAndUserAskedOnce()
    {
        // Arrange: the saved password is stale, the user types the right one
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var origin = FeedOrigin.Create(new Uri(feed.IndexUrl));
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(origin, new NuGetCredential(FeedUser, "stale-" + SentinelPassword));
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword));
        var provider = CreateProvider(feed, new PackageCredentialContext(store, prompt, usersStore));

        // Act
        var versions = await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert
        Assert.Single(versions);
        Assert.Equal(1, usersStore.CountOf("reject"));
        Assert.Null(usersStore.Peek(origin));
        var request = Assert.Single(prompt.Requests);
        Assert.True(request.PreviousCredentialRejected);
        Assert.True(request.CanRemember);
        Assert.False(store.IsFromRememberedStore(origin));
        Assert.True(store.TryGet(origin, out var kept));
        Assert.Equal(SentinelPassword, kept!.Secret);
    }

    /// <summary>
    ///     Test that a credential the user typed and chose to remember is handed to their credential
    ///     store once the server has accepted it, and that the session then counts it as saved.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_ListVersions_RememberTicked_SavedByTheUsersStoreAfterSuccess()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var origin = FeedOrigin.Create(new Uri(feed.IndexUrl));
        var usersStore = new FakeRememberedCredentialStore();
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential(FeedUser, SentinelPassword), remember: true);
        var provider = CreateProvider(feed, new PackageCredentialContext(store, prompt, usersStore));

        // Act
        await provider.ListVersionsAsync(id, CancellationToken.None);

        // Assert
        Assert.True(Assert.Single(prompt.Requests).CanRemember);
        Assert.Equal(1, usersStore.CountOf("approve"));
        Assert.Equal(SentinelPassword, usersStore.Peek(origin)!.Secret);
        Assert.True(store.IsFromRememberedStore(origin));
    }

    /// <summary>
    ///     Test that a credential the server rejects is never handed to the user's credential store,
    ///     even when the user ticked Remember, and that without ticking it nothing is ever saved.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NuGetSourceProvider_ListVersions_WrongPasswordOrRememberNotTicked_NothingIsSaved(bool rememberTicked)
    {
        // Arrange: a wrong password with Remember ticked, or the right password without it
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        RequireSignIn(feed);
        var usersStore = new FakeRememberedCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(
            new NuGetCredential(FeedUser, rememberTicked ? "wrong-" + SentinelPassword : SentinelPassword), rememberTicked);
        var provider = CreateProvider(feed, new PackageCredentialContext(new SessionCredentialStore(), prompt, usersStore));

        // Act
        try
        {
            await provider.ListVersionsAsync(id, CancellationToken.None);
        }
        catch (PackageSourceAccessException)
        {
            // The wrong password is rejected by the server; what matters is what was saved
        }

        // Assert
        Assert.Equal(0, usersStore.CountOf("approve"));
    }

    /// <summary>
    ///     Test that a server source that requires signed packages refuses an unsigned package with
    ///     a signature failure (not a "server unreachable" one), on every platform, and installs
    ///     nothing.
    /// </summary>
    [Fact]
    public async Task NuGetSourceProvider_Acquire_ServerRequiresSignaturesAndPackageUnsigned_IsRejectedAsASignatureFailure()
    {
        // Arrange
        var id = UniqueId();
        var feed = await StartFeedAsync(id, "1.0.0");
        var definition = Definition(feed.IndexUrl, allowInsecureHttp: true, packageIds: ["any.package"]);
        definition.RequireSignedPackages = true;
        var provider = new NuGetSourceProvider(definition, CreateTempDirectory());
        var package = Assert.Single(await provider.ListVersionsAsync(id, CancellationToken.None));

        // Act
        var ex = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.AcquireAsync(package, CancellationToken.None));

        // Assert
        Assert.Equal(PackageSourceFailureKind.SignatureRejected, ex.Kind);
        Assert.Contains($"Package '{id}' 1.0.0 from source 'Feed' is not validly signed", ex.Message, StringComparison.Ordinal);
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
    ///     Creates a provider for a mock feed.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="credentials">The credential facilities, or <see langword="null"/>.</param>
    /// <param name="logger">The logger to capture messages with, or <see langword="null"/>.</param>
    /// <param name="authenticationTimeout">The sign-in timeout, or <see langword="null"/> for the
    ///     default.</param>
    /// <param name="rootBase">The cache base folder, or <see langword="null"/> for a new temporary
    ///     one.</param>
    /// <returns>The provider.</returns>
    private NuGetSourceProvider CreateProvider(
        MockNuGetFeed feed,
        PackageCredentialContext? credentials,
        ListLogger? logger = null,
        TimeSpan? authenticationTimeout = null,
        string? rootBase = null) =>
        new(
            Definition(feed.IndexUrl, allowInsecureHttp: true, packageIds: ["any.package"]),
            rootBase ?? CreateTempDirectory(),
            credentials,
            logger,
            authenticationTimeout);

    /// <summary>
    ///     Creates a NuGet server source definition.
    /// </summary>
    /// <param name="location">The feed address.</param>
    /// <param name="allowInsecureHttp">Whether plain http is allowed.</param>
    /// <param name="packageIds">The package IDs, or <see langword="null"/> for none.</param>
    /// <returns>The definition, with alias <c>Feed</c>.</returns>
    private static PackageSourceDefinition Definition(
        string location, bool allowInsecureHttp, string[]? packageIds = null) =>
        new()
        {
            Alias = "Feed",
            Type = PackageSourceType.NuGet,
            Location = location,
            PackageIds = [.. packageIds ?? []],
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
    ///     Creates a new empty temporary directory that is removed when the test ends.
    /// </summary>
    /// <returns>The directory's absolute path.</returns>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_server_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }
}
