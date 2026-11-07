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
    ///     Creates a feed origin for test setup.
    /// </summary>
    /// <param name="url">The feed URL.</param>
    /// <returns>The origin.</returns>
    private static FeedOrigin Origin(string url) => FeedOrigin.Create(new Uri(url));
}
