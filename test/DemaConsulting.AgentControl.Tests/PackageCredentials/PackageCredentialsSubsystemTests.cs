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
        first.Commit();

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
        store.Set(origin, new NuGetCredential("svc", "stale"));
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("svc", SentinelPassword));

        // Act: the silent attempt offers the stale credential and the feed rejects it
        var silent = new PackageCredentialCallback(origin, "Feed", store);
        var offered = await silent.Callback(FeedIndex, CancellationToken.None);
        silent.Reject();

        // Act: the next attempt may ask
        var asking = new PackageCredentialCallback(origin, "Feed", store, prompt, previousCredentialRejected: true);
        var typed = await asking.Callback(FeedIndex, CancellationToken.None);
        asking.Commit();

        // Assert
        Assert.Equal("stale", offered!.Secret);
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
        callback.Commit();
        callback.Reject();
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
