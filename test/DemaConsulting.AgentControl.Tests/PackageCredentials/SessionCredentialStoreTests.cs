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

using System.Text.Json;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     Unit tests for <see cref="SessionCredentialStore"/>.
/// </summary>
public sealed class SessionCredentialStoreTests
{
    /// <summary>
    ///     Test that a stored credential is found for exactly the origin it was stored under.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_SetAndTryGet_SameOrigin_ReturnsTheCredential()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://feeds.example.com/v3/index.json");
        var credential = new NuGetCredential("svc", "pw-1");

        // Act
        store.Set(origin, credential);
        var found = store.TryGet(Origin("HTTPS://feeds.example.com:443/other"), out var result);

        // Assert
        Assert.True(found);
        Assert.Same(credential, result);
        Assert.Equal(1, store.Count);
    }

    /// <summary>
    ///     Test that a credential is never found for a different origin.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_TryGet_DifferentOrigin_ReturnsFalse()
    {
        // Arrange
        var store = new SessionCredentialStore();
        store.Set(Origin("https://feeds.example.com/v3/index.json"), new NuGetCredential("svc", "pw-collection-test"));

        // Act
        var found = store.TryGet(Origin("https://other.example.com/v3/index.json"), out var result);

        // Assert
        Assert.False(found);
        Assert.Null(result);
    }

    /// <summary>
    ///     Test that Forget removes only the named origin's credential and reports whether one
    ///     was removed.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_Forget_StoredOrigin_RemovesOnlyThatCredential()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var a = Origin("https://a.example.com/index.json");
        var b = Origin("https://b.example.com/index.json");
        store.Set(a, new NuGetCredential("a", "pw-a"));
        store.Set(b, new NuGetCredential("b", "pw-b"));

        // Act
        var removed = store.Forget(a);
        var removedAgain = store.Forget(a);

        // Assert
        Assert.True(removed);
        Assert.False(removedAgain);
        Assert.False(store.TryGet(a, out _));
        Assert.True(store.TryGet(b, out _));
    }

    /// <summary>
    ///     Test that Clear forgets every credential, as happens when the application exits.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_Clear_StoredCredentials_ForgetsAll()
    {
        // Arrange
        var store = new SessionCredentialStore();
        store.Set(Origin("https://a.example.com/index.json"), new NuGetCredential("a", "pw-a"));
        store.Set(Origin("https://b.example.com/index.json"), new NuGetCredential("b", "pw-b"));

        // Act
        store.Clear();

        // Assert
        Assert.Equal(0, store.Count);
    }

    /// <summary>
    ///     Test that Set replaces an earlier credential for the same origin.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_Set_ExistingOrigin_ReplacesTheCredential()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://a.example.com/index.json");
        store.Set(origin, new NuGetCredential("a", "pw-old-test"));

        // Act
        store.Set(origin, new NuGetCredential("a", "pw-new-test"));

        // Assert
        Assert.True(store.TryGet(origin, out var result));
        Assert.Equal("pw-new-test", result!.Secret);
        Assert.Equal(1, store.Count);
    }

    /// <summary>
    ///     Test that null arguments are refused.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_NullArguments_Throw()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://a.example.com/index.json");

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => store.TryGet(null!, out _));
        Assert.Throws<ArgumentNullException>(() => store.Set(null!, new NuGetCredential("a", "b")));
        Assert.Throws<ArgumentNullException>(() => store.Set(origin, null!));
        Assert.Throws<ArgumentNullException>(() => store.Forget(null!));
    }

    /// <summary>
    ///     Test that serializing the store, the only way it could be persisted by accident, never
    ///     reveals a stored secret, and that the store does not print one either.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_Serialize_NeverContainsStoredSecret()
    {
        // Arrange
        const string sentinel = "sentinel-store-secret-7781";
        var store = new SessionCredentialStore();
        store.Set(Origin("https://a.example.com/index.json"), new NuGetCredential("svc", sentinel));

        // Act
        var json = JsonSerializer.Serialize(store);
        var text = store.ToString();

        // Assert
        Assert.DoesNotContain(sentinel, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that every secret the store holds is registered for redaction while stored and
    ///     withdrawn when it is replaced, forgotten or cleared, so a stale secret is not tracked.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_SetReplaceForgetAndClear_RegistersAndWithdrawsTheSecretForRedaction()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://a.example.com/index.json");
        var first = "sec-" + Guid.NewGuid().ToString("N");
        var second = "sec-" + Guid.NewGuid().ToString("N");
        var third = "sec-" + Guid.NewGuid().ToString("N");
        var text = $"{first} {second} {third}";

        // Act / Assert: stored -> redacted
        store.Set(origin, new NuGetCredential("a", first));
        Assert.Equal($"[redacted] {second} {third}", SecretRedactor.Redact(text));

        // Replaced -> the old secret is withdrawn, the new one registered
        store.Set(origin, new NuGetCredential("a", second));
        Assert.Equal($"{first} [redacted] {third}", SecretRedactor.Redact(text));

        // Forgotten -> withdrawn
        Assert.True(store.Forget(origin));
        Assert.Equal(text, SecretRedactor.Redact(text));

        // Cleared -> withdrawn
        store.Set(origin, new NuGetCredential("a", third));
        Assert.Equal($"{first} {second} [redacted]", SecretRedactor.Redact(text));
        store.Clear();
        Assert.Equal(text, SecretRedactor.Redact(text));
        Assert.False(store.Forget(origin));
    }

    /// <summary>
    ///     Test that a lease taken from the store keeps the secret redacted independently of the
    ///     store's own registration - through replace, forget and clear - and that disposing it
    ///     (also twice) ends exactly that registration; a missing credential yields no lease.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_TryAcquire_LeaseOutlivesTheStoresOwnRegistrationUntilDisposed()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://a.example.com/index.json");
        var secret = "sec-" + Guid.NewGuid().ToString("N");
        Assert.False(store.TryAcquire(origin, out var none, out var noLease));
        Assert.Null(none);
        Assert.Null(noLease);
        store.Set(origin, new NuGetCredential("a", secret));
        Assert.True(store.TryAcquire(origin, out var credential, out var lease));
        Assert.Equal(secret, credential!.Secret);

        // Act: the store lets go of it
        Assert.True(store.Forget(origin));

        // Assert: the lease still protects it
        Assert.DoesNotContain(secret, SecretRedactor.Redact($"x {secret}"), StringComparison.Ordinal);
        lease!.Dispose();
        lease.Dispose();
        Assert.Equal($"x {secret}", SecretRedactor.Redact($"x {secret}"));
        Assert.Throws<ArgumentNullException>(() => store.TryAcquire(null!, out _, out _));
    }

    /// <summary>
    ///     Test that forgetting a credential by value removes it only while it is still the
    ///     remembered one: a newer credential for the same feed survives a late rejection of an
    ///     older one.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_ForgetExactCredential_OnlyRemovesTheSameCredential()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://feeds.example.com/v3/index.json");
        var older = new NuGetCredential("svc", "pw-older-" + Guid.NewGuid().ToString("N"));
        var newer = new NuGetCredential("svc", "pw-newer-" + Guid.NewGuid().ToString("N"));
        store.Set(origin, newer);

        // Act / Assert: the older credential is not the remembered one, so nothing is forgotten
        Assert.False(store.Forget(origin, older));
        Assert.True(store.TryGet(origin, out var kept));
        Assert.Equal(newer, kept);

        // Act / Assert: the remembered one is forgotten
        Assert.True(store.Forget(origin, new NuGetCredential(newer.UserName, newer.Secret)));
        Assert.Equal(0, store.Count);
        Assert.False(store.Forget(origin, newer));
        Assert.Equal(newer.Secret, SecretRedactor.Redact(newer.Secret));
        Assert.Throws<ArgumentNullException>(() => store.Forget(null!, newer));
        Assert.Throws<ArgumentNullException>(() => store.Forget(origin, null!));
    }

    /// <summary>
    ///     Test that a credential knows whether it came from, or was saved to, the user's own
    ///     credential store - reported with the lease in one step - and that replacing or forgetting
    ///     it clears the mark.
    /// </summary>
    [Fact]
    public void SessionCredentialStore_FromRememberedStoreMark_FollowsTheCredential()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://feeds.example.com/v3/index.json");
        Assert.False(store.IsFromRememberedStore(origin));

        // Act / Assert: marked when set so
        store.Set(origin, new NuGetCredential("svc", "pw-marked-" + Guid.NewGuid().ToString("N")), fromRememberedStore: true);
        Assert.True(store.IsFromRememberedStore(origin));
        Assert.True(store.TryAcquire(origin, out _, out var lease, out var marked));
        Assert.True(marked);
        lease!.Dispose();

        // Act / Assert: replaced by a credential that is not saved
        store.Set(origin, new NuGetCredential("svc", "pw-plain-" + Guid.NewGuid().ToString("N")));
        Assert.False(store.IsFromRememberedStore(origin));

        // Act / Assert: forgetting clears it too
        store.Set(origin, new NuGetCredential("svc", "pw-marked-again-" + Guid.NewGuid().ToString("N")), fromRememberedStore: true);
        store.Forget(origin);
        Assert.False(store.IsFromRememberedStore(origin));
        Assert.False(store.TryAcquire(origin, out _, out var none, out var notMarked));
        Assert.Null(none);
        Assert.False(notMarked);
        Assert.Throws<ArgumentNullException>(() => store.IsFromRememberedStore(null!));
    }

    /// <summary>
    ///     Test that the once-per-origin lookup of the user's credential store runs a single time
    ///     however many callers ask (also at once), runs again for another origin, and is started
    ///     afresh after the store is cleared.
    /// </summary>
    [Fact]
    public async Task SessionCredentialStore_EnsureRememberedLookup_RunsOncePerOrigin()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var first = Origin("https://feeds.example.com/v3/index.json");
        var second = Origin("https://other.example.com/v3/index.json");
        var runs = 0;
        Task Lookup()
        {
            Interlocked.Increment(ref runs);
            return Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Act: many callers at once for the first origin, then another origin
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.EnsureRememberedLookupAsync(first, Lookup)));
        await store.EnsureRememberedLookupAsync(first, Lookup);
        await store.EnsureRememberedLookupAsync(second, Lookup);

        // Assert
        Assert.Equal(2, runs);

        // Act: after the store is cleared the lookup is allowed again
        store.Clear();
        await store.EnsureRememberedLookupAsync(first, Lookup);
        Assert.Equal(3, runs);
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.EnsureRememberedLookupAsync(null!, Lookup));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.EnsureRememberedLookupAsync(first, null!));
    }

    /// <summary>
    ///     Test that a lookup still in flight when the store is cleared cannot put its credential
    ///     back afterwards: the late <c>Set</c> is discarded and the secret is not registered with
    ///     the redactor (so the exit-time clearing cannot be undone).
    /// </summary>
    [Fact]
    public async Task SessionCredentialStore_EnsureRememberedLookup_ClearWhileLookupInFlight_LateSetIsDiscardedAndNotRedacted()
    {
        // Arrange: a lookup that waits until after Clear before it stores what it found
        var store = new SessionCredentialStore();
        var origin = Origin("https://feeds.example.com/v3/index.json");
        var secret = "late-" + Guid.NewGuid().ToString("N");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Lookup()
        {
            started.SetResult();
            await release.Task;
            store.Set(origin, new NuGetCredential("svc", secret), fromRememberedStore: true);
        }

        var lookup = store.EnsureRememberedLookupAsync(origin, Lookup);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Act: the application exits while the lookup runs, then the lookup finishes
        store.Clear();
        release.SetResult();
        await lookup.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Assert: nothing came back, and the late secret is not being tracked for redaction
        Assert.Equal(0, store.Count);
        Assert.False(store.TryGet(origin, out _));
        Assert.False(store.IsFromRememberedStore(origin));
        Assert.Equal(secret, SecretRedactor.Redact(secret));

        // Assert: a credential stored by an ordinary operation after Clear is still accepted
        var typed = new NuGetCredential("svc", "typed-" + Guid.NewGuid().ToString("N"));
        store.Set(origin, typed);
        Assert.True(store.TryGet(origin, out var found));
        Assert.Same(typed, found);
        store.Clear();
    }

    /// <summary>
    ///     Test that a lookup that fails surfaces its fault to every caller - also callers that
    ///     arrive later - and is not run again.
    /// </summary>
    [Fact]
    public async Task SessionCredentialStore_EnsureRememberedLookup_LookupFaults_SurfacesToEveryCallerAndIsNotRepeated()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://feeds.example.com/v3/index.json");
        var runs = 0;
        Task Lookup()
        {
            Interlocked.Increment(ref runs);
            return Task.FromException(new InvalidOperationException("the helper failed"));
        }

        // Act
        var first = store.EnsureRememberedLookupAsync(origin, Lookup);
        var second = store.EnsureRememberedLookupAsync(origin, Lookup);

        // Assert: both waiting callers see the fault, and so does a caller that arrives afterwards
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.EnsureRememberedLookupAsync(origin, Lookup));
        Assert.Equal(1, runs);
        Assert.Equal(0, store.Count);
    }

    /// <summary>
    ///     Test that after the store was cleared a new lookup starts fresh and its credential is
    ///     stored normally (only lookups that were in flight at the time of the clear are
    ///     invalidated).
    /// </summary>
    [Fact]
    public async Task SessionCredentialStore_EnsureRememberedLookup_LookupAfterClear_StartsFreshAndStoresNormally()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var origin = Origin("https://feeds.example.com/v3/index.json");
        var credential = new NuGetCredential("svc", "saved-" + Guid.NewGuid().ToString("N"));
        store.Clear();

        // Act
        await store.EnsureRememberedLookupAsync(
            origin,
            async () =>
            {
                await Task.Yield();
                store.Set(origin, credential, fromRememberedStore: true);
            });

        // Assert
        Assert.True(store.TryGet(origin, out var found));
        Assert.Same(credential, found);
        Assert.True(store.IsFromRememberedStore(origin));
        store.Clear();
    }

    /// <summary>
    ///     Creates a feed origin for test setup.
    /// </summary>
    /// <param name="url">The feed URL.</param>
    /// <returns>The origin.</returns>
    private static FeedOrigin Origin(string url) => FeedOrigin.Create(new Uri(url));
}
