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

using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     Subsystem tests for PackageCredentials: the units working together as the NuGet library
///     drives them - the feed origin, the session store, the per-operation callback with its
///     prompt, and the redacting logger - without any server or UI.
/// </summary>
public class PackageCredentialsSubsystemTests
{
    /// <summary>
    ///     The password that must never be remembered beyond the session or reach a log.
    /// </summary>
    private const string SentinelPassword = "sentinel-subsystem-pw-4410";

    /// <summary>
    ///     The feed every test signs in to.
    /// </summary>
    private static readonly Uri FeedIndex = new("https://feeds.example.com/v3/index.json");

    /// <summary>
    ///     Test that a credential typed during one operation is remembered only after that
    ///     operation succeeds and is then used by the next operation without asking again.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_TwoOperations_FirstPromptsAndSecondReusesTheSessionCredential()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc", SentinelPassword));
        var origin = FeedOrigin.Create(FeedIndex);

        // Act: the first operation asks, the library uses the answer, and the operation succeeds
        var first = new PackageCredentialCallback(origin, "Feed", store, prompt);
        var typed = await first.Callback(FeedIndex, CancellationToken.None);
        Assert.Equal(0, store.Count);
        await first.CommitAsync(CancellationToken.None);

        // Act: the second operation answers from the store
        var second = new PackageCredentialCallback(origin, "Feed", store, prompt);
        var reused = await second.Callback(new Uri("https://feeds.example.com/v3/flat/pkg/index.json"), CancellationToken.None);

        // Assert
        Assert.Equal(SentinelPassword, typed!.Secret);
        Assert.Equal(SentinelPassword, reused!.Secret);
        Assert.Equal(1, prompt.AskCount);
        Assert.False(second.WasPrompted);
    }

    /// <summary>
    ///     Test that a credential the feed rejects is forgotten, so the next operation asks the user
    ///     again instead of repeating a credential that does not work.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_RejectedCredential_IsForgottenAndTheNextOperationAsksAgain()
    {
        // Arrange: a remembered credential that the feed will reject, and a user who types a new one
        var store = new SessionCredentialStore();
        var origin = FeedOrigin.Create(FeedIndex);
        store.Set(origin, new NuGetCredential("svc", "pw-stale-sub"));
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc", SentinelPassword));

        // Act: the silent attempt offers the stale credential and the feed rejects it
        var silent = new PackageCredentialCallback(origin, "Feed", store);
        var offered = await silent.Callback(FeedIndex, CancellationToken.None);
        await silent.RejectAsync(CancellationToken.None);

        // Act: the next attempt may ask
        var asking = new PackageCredentialCallback(origin, "Feed", store, prompt, previousCredentialRejected: true);
        var typed = await asking.Callback(FeedIndex, CancellationToken.None);
        await asking.CommitAsync(CancellationToken.None);

        // Assert
        Assert.Equal("pw-stale-sub", offered!.Secret);
        Assert.Equal(SentinelPassword, typed!.Secret);
        Assert.True(store.TryGet(origin, out var remembered));
        Assert.Equal(SentinelPassword, remembered!.Secret);
        Assert.True(Assert.Single(prompt.Requests).PreviousCredentialRejected);
    }

    /// <summary>
    ///     Test that a credential remembered for one feed is never offered for another feed, and that
    ///     a challenge from a foreign origin is refused even while the user is being asked for the
    ///     feed's own credential.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_CredentialForOneFeed_IsNeverOfferedToAnotherOrigin()
    {
        // Arrange
        var store = new SessionCredentialStore();
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", SentinelPassword));
        var prompt = new FakeCredentialPrompt();
        var otherFeed = new Uri("https://other.example.com/v3/index.json");

        // Act: an operation against another feed has nothing remembered and is not allowed to ask
        var other = new PackageCredentialCallback(FeedOrigin.Create(otherFeed), "Other", store);
        var forOther = await other.Callback(otherFeed, CancellationToken.None);

        // Act: an operation against the first feed is challenged by a foreign host
        var own = new PackageCredentialCallback(FeedOrigin.Create(FeedIndex), "Feed", store, prompt);
        var forForeign = await own.Callback(new Uri("https://evil.example.com/download.nupkg"), CancellationToken.None);

        // Assert
        Assert.Null(forOther);
        Assert.Null(forForeign);
        Assert.Equal(0, prompt.AskCount);
    }

    /// <summary>
    ///     Test that the whole "remember" life cycle works across operations and sessions: the user
    ///     ticks Remember and signs in, the credential is saved by the user's credential store, a
    ///     later session (a fresh session store) finds it there without asking, and forgetting the
    ///     saved sign-in makes the next session ask again.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_RememberLifeCycle_SavedByTheUsersStoreAndFoundInALaterSession()
    {
        // Arrange: the user's credential store outlives sessions; the session store does not
        var origin = FeedOrigin.Create(FeedIndex);
        var usersStore = new FakeRememberedCredentialStore();
        var firstSession = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc", SentinelPassword), remember: true);

        // Act: session one asks, the user ticks Remember, the operation succeeds
        var first = new PackageCredentialCallback(origin, "Feed", firstSession, prompt, remembered: usersStore);
        await first.Callback(FeedIndex, CancellationToken.None);
        await first.CommitAsync(CancellationToken.None);

        // Act: session two (a new process) is silent but finds the saved sign-in
        var secondSession = new SessionCredentialStore();
        var second = new PackageCredentialCallback(origin, "Feed", secondSession, prompt: null, remembered: usersStore);
        var found = await second.Callback(FeedIndex, CancellationToken.None);
        second.Release();

        // Act: the user forgets the saved sign-in; session three has to ask again
        var context = new PackageCredentialContext(secondSession, prompt, usersStore);
        var result = await context.ForgetSavedSignInAsync(origin, CancellationToken.None);
        var thirdSession = new SessionCredentialStore();
        var third = new PackageCredentialCallback(origin, "Feed", thirdSession, prompt: null, remembered: usersStore);
        var afterForget = await third.Callback(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Equal(SentinelPassword, found!.Secret);
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(ForgetSignInResult.Removed, result);
        Assert.Null(afterForget);
        Assert.Equal(0, secondSession.Count);
    }

    /// <summary>
    ///     Test the outcomes of forgetting a sign-in: nothing saved, saved (and removed), only this
    ///     session's copy, the credential store refusing, and no credential store at all.
    /// </summary>
    [Fact]
    public async Task PackageCredentialContext_ForgetSavedSignIn_ReportsWhatWasDone()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var usersStore = new FakeRememberedCredentialStore();

        // Act / Assert: nothing anywhere
        var empty = new PackageCredentialContext(new SessionCredentialStore(), null, usersStore);
        Assert.Equal(ForgetSignInResult.NothingSaved, await empty.ForgetSavedSignInAsync(origin, CancellationToken.None));

        // Act / Assert: saved in the user's store, and also remembered by the session
        usersStore.Save(origin, new NuGetCredential("svc", SentinelPassword));
        var session = new SessionCredentialStore();
        session.Set(origin, new NuGetCredential("svc", SentinelPassword), fromRememberedStore: true);
        var saved = new PackageCredentialContext(session, null, usersStore);
        Assert.Equal(ForgetSignInResult.Removed, await saved.ForgetSavedSignInAsync(origin, CancellationToken.None));
        Assert.Null(usersStore.Peek(origin));
        Assert.Equal(0, session.Count);

        // Act / Assert: only this session's copy
        var onlySession = new SessionCredentialStore();
        onlySession.Set(origin, new NuGetCredential("svc", "pw-session-forget-test"));
        var sessionOnly = new PackageCredentialContext(onlySession, null, usersStore);
        Assert.Equal(ForgetSignInResult.Removed, await sessionOnly.ForgetSavedSignInAsync(origin, CancellationToken.None));

        // Act / Assert: the store refuses to remove what it holds
        usersStore.Save(origin, new NuGetCredential("svc", SentinelPassword));
        usersStore.RefuseChanges = true;
        var refusing = new PackageCredentialContext(new SessionCredentialStore(), null, usersStore);
        Assert.Equal(ForgetSignInResult.Failed, await refusing.ForgetSavedSignInAsync(origin, CancellationToken.None));

        // Act / Assert: no credential store at all
        var none = new PackageCredentialContext(new SessionCredentialStore(), null);
        Assert.Equal(ForgetSignInResult.NothingSaved, await none.ForgetSavedSignInAsync(origin, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => none.ForgetSavedSignInAsync(null!, CancellationToken.None));
    }

    /// <summary>
    ///     Test that a context without a prompt (background work) keeps the user's credential store,
    ///     because reading a saved sign-in needs no dialog.
    /// </summary>
    [Fact]
    public void PackageCredentialContext_WithoutPrompt_KeepsStoreAndTheUsersCredentialStore()
    {
        // Arrange
        var usersStore = new FakeRememberedCredentialStore();
        var context = new PackageCredentialContext(new SessionCredentialStore(), new FakeCredentialPrompt(), usersStore);

        // Act
        var silent = context.WithoutPrompt();

        // Assert
        Assert.Null(silent.Prompt);
        Assert.Same(context.Store, silent.Store);
        Assert.Same(usersStore, silent.Remembered);
    }

    /// <summary>
    ///     Test that repeated challenges on one operation's callback - different addresses of the
    ///     feed, retries, concurrent requests - ask the user at most once and all receive the same
    ///     answer, a declined sign-in included.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageCredentials_RepeatedChallengesOnOneCallback_PromptsOnceAndSharesTheAnswer(bool userDeclines)
    {
        // Arrange
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(userDeclines ? null : new NuGetCredential("svc", "pw-repeat-" + Guid.NewGuid().ToString("N")));
        var callback = new PackageCredentialCallback(FeedOrigin.Create(FeedIndex), "Feed", new SessionCredentialStore(), prompt);
        var challenges = new[]
        {
            FeedIndex,
            new Uri("https://feeds.example.com/v3/flat/pkg/index.json"),
            new Uri("https://FEEDS.example.com:443/v3/flat/pkg/1.0.0/pkg.1.0.0.nupkg"),
            FeedIndex
        };

        // Act: sequential retries, then a burst of concurrent requests
        var sequential = new List<NuGetCredential?>();
        foreach (var challenge in challenges)
        {
            sequential.Add(await callback.Callback(challenge, CancellationToken.None));
        }

        var concurrent = await Task.WhenAll(challenges.Select(challenge => callback.Callback(challenge, CancellationToken.None)));

        // Assert
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(userDeclines, callback.UserDeclined);
        Assert.All(sequential.Concat(concurrent), credential =>
        {
            if (userDeclines)
            {
                Assert.Null(credential);
            }
            else
            {
                Assert.Same(callback.SuppliedCredential, credential);
            }
        });
        callback.Release();
    }

    /// <summary>
    ///     Test that a challenge from a different scheme, host, sub-domain or port than the feed's
    ///     gets no credential, never opens the prompt and never consults either credential store -
    ///     as a redirect or a link inside a feed would produce.
    /// </summary>
    [Theory]
    [InlineData("https://feeds.example.com:8443/v3/index.json")]
    [InlineData("http://feeds.example.com/v3/index.json")]
    [InlineData("https://sub.feeds.example.com/v3/index.json")]
    [InlineData("https://feeds.example.com.evil.test/v3/index.json")]
    [InlineData("https://evil.example.com/redirect")]
    public async Task PackageCredentials_ChallengeFromDifferentSchemeHostOrPort_GetsNoCredentialAndNoPrompt(string foreign)
    {
        // Arrange: a credential in the session, another saved by the user, and a prompt ready to answer
        var origin = FeedOrigin.Create(FeedIndex);
        var store = new SessionCredentialStore();
        store.Set(origin, new NuGetCredential("svc", "pw-scope-" + Guid.NewGuid().ToString("N")));
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(origin, new NuGetCredential("svc", "pw-scope-saved-" + Guid.NewGuid().ToString("N")));
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "pw-scope-typed-" + Guid.NewGuid().ToString("N")));
        var callback = new PackageCredentialCallback(origin, "Feed", store, prompt, remembered: usersStore);

        // Act
        var credential = await callback.Callback(new Uri(foreign), CancellationToken.None);

        // Assert
        Assert.Null(credential);
        Assert.Equal(0, prompt.AskCount);
        Assert.Empty(usersStore.Calls);
        Assert.Null(callback.SuppliedCredential);
    }

    /// <summary>
    ///     Test that a late rejection of an old credential - arriving after the user saved a newer
    ///     one for the same feed - removes nothing that is newer, neither in the session nor in the
    ///     user's own credential store.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_LateRejectionOfAnOldCredential_KeepsTheNewerRememberedCredential()
    {
        // Arrange: a slow operation offers the saved credential A
        var origin = FeedOrigin.Create(FeedIndex);
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(origin, new NuGetCredential("svc", "pw-old-" + Guid.NewGuid().ToString("N")));
        var store = new SessionCredentialStore();
        var slow = new PackageCredentialCallback(origin, "Feed", store, prompt: null, remembered: usersStore);
        await slow.Callback(FeedIndex, CancellationToken.None);

        // Act: meanwhile another operation signs in again and the new credential is saved
        var newer = new NuGetCredential("svc", "pw-new-" + Guid.NewGuid().ToString("N"));
        usersStore.Save(origin, newer);
        store.Set(origin, newer, fromRememberedStore: true);
        await slow.RejectAsync(CancellationToken.None);

        // Assert
        Assert.Equal(newer, usersStore.Peek(origin));
        Assert.True(store.TryGet(origin, out var current));
        Assert.Equal(newer, current);
    }

    /// <summary>
    ///     Test that clearing the session store (as at exit) forgets every credential in it, so the
    ///     next operation has to ask again and the secrets are no longer tracked for redaction.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_ClearedSessionStore_ForgetsEveryCredentialAndTheNextOperationAsksAgain()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var other = FeedOrigin.Create(new Uri("https://other.example.com/v3/index.json"));
        var secret = "pw-clear-" + Guid.NewGuid().ToString("N");
        var store = new SessionCredentialStore();
        store.Set(origin, new NuGetCredential("svc", secret));
        store.Set(other, new NuGetCredential("svc", "pw-clear-other-" + Guid.NewGuid().ToString("N")));
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(null);

        // Act
        store.Clear();
        var next = new PackageCredentialCallback(origin, "Feed", store, prompt);
        var answer = await next.Callback(FeedIndex, CancellationToken.None);
        var nextOther = new PackageCredentialCallback(other, "Other", store, prompt);
        var answerOther = await nextOther.Callback(new Uri("https://other.example.com/v3/index.json"), CancellationToken.None);

        // Assert
        Assert.Equal(0, store.Count);
        Assert.False(store.TryGet(origin, out _));
        Assert.False(store.TryGet(other, out _));
        Assert.Null(answer);
        Assert.Null(answerOther);
        Assert.Equal(2, prompt.AskCount);
        Assert.Equal(secret, SecretRedactor.Redact(secret));
    }

    /// <summary>
    ///     Test that nothing is ever handed to the user's own credential store unless the user
    ///     ticked "Remember" and the operation succeeded.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task PackageCredentials_RememberNotTickedOrOperationNotSucceeded_NothingIsSavedInTheUsersStore(
        bool ticked, bool succeeded)
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var usersStore = new FakeRememberedCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc", "pw-unsaved-" + Guid.NewGuid().ToString("N")), remember: ticked);
        var callback = new PackageCredentialCallback(
            origin, "Feed", new SessionCredentialStore(), prompt, remembered: usersStore);
        await callback.Callback(FeedIndex, CancellationToken.None);

        // Act
        if (succeeded)
        {
            await callback.CommitAsync(CancellationToken.None);
        }
        else
        {
            await callback.RejectAsync(CancellationToken.None);
        }

        // Assert
        Assert.Equal(0, usersStore.CountOf("approve"));
        Assert.Null(usersStore.Peek(origin));
    }

    /// <summary>
    ///     Test that the sign-in prompt offers to remember only when the user's own credential store
    ///     exists and can keep a sign-in for the feed.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public async Task PackageCredentials_PromptRequest_OffersRememberOnlyWhenTheUsersStoreCanKeepIt(
        bool hasUsersStore, bool helperAvailable, bool expected)
    {
        // Arrange
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(null);
        IRememberedCredentialStore? usersStore = hasUsersStore
            ? new FakeRememberedCredentialStore { Available = helperAvailable }
            : null;
        var callback = new PackageCredentialCallback(
            FeedOrigin.Create(FeedIndex), "Feed", new SessionCredentialStore(), prompt, remembered: usersStore);

        // Act
        await callback.Callback(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Equal(expected, Assert.Single(prompt.Requests).CanRemember);
    }

    /// <summary>
    ///     Test that operations on one server that start together look in the user's credential
    ///     store once between them, and all use what it held.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_ConcurrentOperationsOnOneServer_LookUpTheSavedCredentialOnce()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var usersStore = new FakeRememberedCredentialStore { FillDelay = TimeSpan.FromMilliseconds(200) };
        usersStore.Save(origin, new NuGetCredential("svc", "pw-lookup-" + Guid.NewGuid().ToString("N")));
        var store = new SessionCredentialStore();
        var callbacks = Enumerable.Range(0, 6)
            .Select(_ => new PackageCredentialCallback(origin, "Feed", store, prompt: null, remembered: usersStore))
            .ToList();

        // Act
        var credentials = await Task.WhenAll(callbacks.Select(callback => callback.Callback(FeedIndex, CancellationToken.None)));

        // Assert
        Assert.Equal(1, usersStore.CountOf("fill"));
        Assert.All(credentials, credential => Assert.NotNull(credential));
        callbacks.ForEach(callback => callback.Release());
    }

    /// <summary>
    ///     Test that a saved credential the server rejects is removed from the user's own credential
    ///     store, so the next session asks the user instead of repeating it.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_RejectedSavedCredential_IsRemovedFromTheUsersStoreAndTheNextSessionAsks()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(origin, new NuGetCredential("svc", "pw-saved-bad-" + Guid.NewGuid().ToString("N")));
        var session = new SessionCredentialStore();
        var silent = new PackageCredentialCallback(origin, "Feed", session, prompt: null, remembered: usersStore);
        await silent.Callback(FeedIndex, CancellationToken.None);

        // Act: the server rejects it; a later session (a fresh session store) is allowed to ask
        await silent.RejectAsync(CancellationToken.None);
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(null);
        var nextSession = new PackageCredentialCallback(
            origin, "Feed", new SessionCredentialStore(), prompt, remembered: usersStore);
        var answer = await nextSession.Callback(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Null(usersStore.Peek(origin));
        Assert.False(session.TryGet(origin, out _));
        Assert.Null(answer);
        Assert.Equal(1, prompt.AskCount);
    }

    /// <summary>
    ///     Test that no part of a logged address - user information, query string or fragment -
    ///     reaches the log, however the address is given to the logger.
    /// </summary>
    [Theory]
    [InlineData("https://svc:sentinel-sub-1@feeds.example.com/v3/index.json")]
    [InlineData("https://feeds.example.com/v3/index.json?sig=sentinel-sub-2&se=2026")]
    [InlineData("https://feeds.example.com/v3/index.json#sentinel-sub-3")]
    [InlineData("https://svc:sentinel-sub-4@feeds.example.com/v3/index.json?sig=sentinel-sub-4#sentinel-sub-4")]
    [InlineData("(https://feeds.example.com/v3/index.json?token=sentinel-sub-5)")]
    public void PackageCredentials_LoggedUrlParts_NeverReachTheLog(string url)
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);

        // Act: as message text, as a structured property and in a scope
        LogText(logger, "Requesting " + url);
        logger.LogInformation("Requesting {Url}", url);
        using (logger.BeginScope("Source {Url}", url))
        {
            logger.LogDebug("inside the scope");
        }

        // Assert
        var everything = inner.Snapshot().Concat(inner.Scopes).ToList();
        Assert.NotEmpty(everything);
        Assert.All(everything, line => Assert.DoesNotContain("sentinel-sub", line, StringComparison.Ordinal));
        Assert.Contains(everything, line => line.Contains("feeds.example.com", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that the whole value of an Authorization header is removed whatever its scheme -
    ///     Basic, Bearer, Digest, NTLM, Negotiate or a custom scheme - in message text and in a
    ///     structured property.
    /// </summary>
    [Theory]
    [InlineData("Basic sentinel-sub-a")]
    [InlineData("Bearer sentinel-sub-b")]
    [InlineData("Digest username=\"u\", realm=\"r\", response=\"sentinel-sub-c\"")]
    [InlineData("NTLM sentinel-sub-d")]
    [InlineData("Negotiate sentinel-sub-e")]
    [InlineData("Custom-Scheme sentinel-sub-f and more")]
    public void PackageCredentials_LoggedAuthorizationSchemes_NeverReachTheLog(string headerValue)
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);

        // Act
        LogText(logger, "Request failed. Authorization: " + headerValue);
        logger.LogWarning("Headers were {Headers}", "Accept: */*\nAuthorization: " + headerValue);

        // Assert
        var lines = inner.Snapshot();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.DoesNotContain("sentinel-sub", line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.Contains("[redacted]", line, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a logged exception reaches the log only as a secret-free description: its type,
    ///     the redacted messages of its whole inner chain and a redacted stack trace, with nothing
    ///     from its data or the original object.
    /// </summary>
    [Fact]
    public void PackageCredentials_LoggedExceptions_AreSanitized()
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);
        var secret = "sentinel-sub-reg-" + Guid.NewGuid().ToString("N");
        var credential = new NuGetCredential("svc", secret);
        using var lease = SecretRedactor.Lease(credential);
        Exception thrown;
        try
        {
            throw new InvalidOperationException(
                "failed https://feeds.example.com/x?sig=sentinel-sub-m Authorization: Bearer sentinel-sub-n " + secret,
                new IOException("inner https://svc:sentinel-sub-o@h.example.com/y?t=sentinel-sub-p"))
            {
                Data = { ["token"] = "sentinel-sub-q" }
            };
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        // Act
        logger.LogError(thrown, "The operation failed for {Alias}", "Feed");

        // Assert
        var line = Assert.Single(inner.Snapshot());
        Assert.DoesNotContain("sentinel-sub", line, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", line, StringComparison.Ordinal);
        Assert.Contains("IOException", line, StringComparison.Ordinal);
        Assert.Contains("https://feeds.example.com/x", line, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that structured property values and scope values are redacted while plain value types
    ///     keep their values, so structured sinks stay useful.
    /// </summary>
    [Fact]
    public void PackageCredentials_LoggedScopesAndProperties_AreRedacted()
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);
        var scopeState = new Dictionary<string, object?>
        {
            ["Request"] = "GET https://feeds.example.com/v3/index.json?sig=sentinel-sub-s",
            ["Header"] = "Authorization: Basic sentinel-sub-t",
            ["Attempt"] = 3
        };

        // Act
        using (logger.BeginScope(scopeState))
        {
            logger.LogInformation(
                "Fetched {Url} in {Attempts} attempts with {Header}",
                "https://feeds.example.com/v3/index.json?sig=sentinel-sub-u",
                2,
                "Cookie: session=sentinel-sub-v");
        }

        // Assert
        var scope = Assert.Single(inner.Scopes);
        var line = Assert.Single(inner.Snapshot());
        Assert.DoesNotContain("sentinel-sub", scope, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-sub", line, StringComparison.Ordinal);
        Assert.Contains("Attempt=3", scope, StringComparison.Ordinal);
        Assert.Contains("Attempts=2", line, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test the redaction lease life cycle: the credential a user typed is redacted from the
    ///     moment it is entered until the operation ends, and - when the operation did not commit
    ///     it - not tracked afterwards; a committed credential stays redacted while the store holds
    ///     it and stops when the store is cleared.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_RedactionLease_SecretIsRedactedUntilTheLeaseEnds()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var notKept = "pw-lease-discarded-" + Guid.NewGuid().ToString("N");
        var kept = "pw-lease-kept-" + Guid.NewGuid().ToString("N");
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc", notKept));
        prompt.EnqueueAnswer(new NuGetCredential("svc", kept));

        // Act / Assert: the typed secret is redacted while the operation runs and not tracked after
        // an operation that did not keep it
        var failed = new PackageCredentialCallback(origin, "Feed", store, prompt);
        await failed.Callback(FeedIndex, CancellationToken.None);
        Assert.Equal("value [redacted]", SecretRedactor.Redact("value " + notKept));
        await failed.RejectAsync(CancellationToken.None);
        Assert.Equal("value " + notKept, SecretRedactor.Redact("value " + notKept));

        // Act / Assert: a committed credential stays redacted after the operation, until the store clears
        var succeeded = new PackageCredentialCallback(origin, "Feed", store, prompt);
        await succeeded.Callback(FeedIndex, CancellationToken.None);
        await succeeded.CommitAsync(CancellationToken.None);
        Assert.Equal("value [redacted]", SecretRedactor.Redact("value " + kept));
        store.Clear();
        Assert.Equal("value " + kept, SecretRedactor.Redact("value " + kept));
    }

    /// <summary>
    ///     Test that when the callback, the store and the redacting logger work together, neither the
    ///     typed password nor a header or URL secret reaches the log.
    /// </summary>
    [Fact]
    public async Task PackageCredentials_SignInThroughCallbackAndRedactingLogger_NeverLogsASecret()
    {
        // Arrange
        var inner = new ListLogger();
        var logger = new SecretRedactingLogger(inner);
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc", SentinelPassword));
        var callback = new PackageCredentialCallback(FeedOrigin.Create(FeedIndex), "Feed", store, prompt, logger: logger);

        // Act: sign in, commit, and let the library-style messages through the redacting logger
        await callback.Callback(FeedIndex, CancellationToken.None);
        await callback.CommitAsync(CancellationToken.None);
        await callback.RejectAsync(CancellationToken.None);
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("svc:" + SentinelPassword));
        logger.LogInformation("GET https://feeds.example.com/v3/index.json?sig={Token}", SentinelPassword);
        logger.LogWarning("Authorization: Basic {Encoded}", encoded);

        // Assert
        var lines = inner.Snapshot();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.DoesNotContain(SentinelPassword, line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotContain(encoded, line, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Writes text to a logger exactly as given, as a message that is not a template, the way
    ///     a library that formats its own messages does.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="text">The finished message text.</param>
    private static void LogText(ILogger logger, string text) =>
        logger.Log(LogLevel.Information, default, text, null, static (message, _) => message);
}
