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
}
