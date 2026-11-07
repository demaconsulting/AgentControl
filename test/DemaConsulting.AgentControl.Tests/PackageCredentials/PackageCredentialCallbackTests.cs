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

namespace DemaConsulting.AgentControl.Tests.PackageCredentials;

/// <summary>
///     Unit tests for <see cref="PackageCredentialCallback"/>, using a scripted prompt so no UI is
///     involved.
/// </summary>
public sealed class PackageCredentialCallbackTests
{
    /// <summary>
    ///     The feed index every test challenges.
    /// </summary>
    private static readonly Uri FeedIndex = new("https://feeds.example.com/v3/index.json");

    /// <summary>
    ///     Test that a challenge from a different origin gets no credential and never opens the
    ///     prompt, even when a credential is remembered for the feed.
    /// </summary>
    [Theory]
    [InlineData("https://evil.example.com/v3/index.json")]
    [InlineData("https://feeds.example.com:8443/v3/index.json")]
    [InlineData("http://feeds.example.com/v3/index.json")]
    public async Task PackageCredentialCallback_GetCredential_ForeignOrigin_ReturnsNullWithoutPrompting(string foreign)
    {
        // Arrange: a remembered credential and a prompt that would answer
        var store = new SessionCredentialStore();
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "pw"));
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw"));
        var callback = Create(store, prompt);

        // Act
        var credential = await callback.GetCredentialAsync(new Uri(foreign), CancellationToken.None);

        // Assert
        Assert.Null(credential);
        Assert.Equal(0, prompt.AskCount);
        Assert.Null(callback.SuppliedCredential);
    }

    /// <summary>
    ///     Test that a remembered session credential is used without asking the user.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_SessionCredentialStored_UsesItWithoutPrompting()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var remembered = new NuGetCredential("svc", "pw");
        store.Set(FeedOrigin.Create(FeedIndex), remembered);
        var prompt = new FakeCredentialPrompt();
        var callback = Create(store, prompt);

        // Act
        var credential = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Same(remembered, credential);
        Assert.Equal(0, prompt.AskCount);
        Assert.False(callback.WasPrompted);
    }

    /// <summary>
    ///     Test that without a remembered credential the user is asked, once, and told which feed
    ///     and source the credential is for.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_NothingStored_AsksTheUserForThatFeed()
    {
        // Arrange
        var prompt = new FakeCredentialPrompt();
        var typed = new NuGetCredential("typed", "typed-pw");
        prompt.EnqueueAnswer(typed);
        var callback = Create(new SessionCredentialStore(), prompt, previousCredentialRejected: true);

        // Act
        var credential = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Same(typed, credential);
        var request = Assert.Single(prompt.Requests);
        Assert.Equal("https://feeds.example.com", request.Origin.ToString());
        Assert.Equal("Feed", request.SourceAlias);
        Assert.True(request.PreviousCredentialRejected);
        Assert.True(callback.WasPrompted);
        Assert.False(callback.UserDeclined);
    }

    /// <summary>
    ///     Test that several challenges of one operation (the NuGet library asks for the index,
    ///     the version list and the package file) cause one prompt only.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_SeveralChallenges_PromptsOnce()
    {
        // Arrange
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw"));
        var callback = Create(new SessionCredentialStore(), prompt);

        // Act: three different URLs of the feed, one after the other and concurrently
        var first = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);
        var second = await callback.GetCredentialAsync(new Uri("https://feeds.example.com/v3/flat/pkg/index.json"), CancellationToken.None);
        var concurrent = await Task.WhenAll(
            callback.GetCredentialAsync(new Uri("https://feeds.example.com/v3/reg/pkg/index.json"), CancellationToken.None),
            callback.GetCredentialAsync(new Uri("https://feeds.example.com/v3/flat/pkg/1.0.0/pkg.1.0.0.nupkg"), CancellationToken.None));

        // Assert: the same credential every time and a single question
        Assert.Same(first, second);
        Assert.All(concurrent, credential => Assert.Same(first, credential));
        Assert.Equal(1, prompt.AskCount);
    }

    /// <summary>
    ///     Test that a cancelled dialog is remembered for the operation, so a retry by the NuGet
    ///     library does not open the dialog again, and the decline is reported.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_UserDeclines_NullIsMemoizedAndNotAskedAgain()
    {
        // Arrange: the user cancels the dialog
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(null);
        var callback = Create(new SessionCredentialStore(), prompt);

        // Act
        var first = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);
        var second = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, prompt.AskCount);
        Assert.True(callback.UserDeclined);
        Assert.Null(callback.SuppliedCredential);
    }

    /// <summary>
    ///     Test that a silent callback (no prompt) answers null when nothing is remembered and
    ///     never asks.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_NoPromptAndNothingStored_ReturnsNull()
    {
        // Arrange
        var callback = Create(new SessionCredentialStore(), prompt: null);

        // Act
        var credential = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Null(credential);
        Assert.False(callback.WasPrompted);
        Assert.False(callback.UserDeclined);
    }

    /// <summary>
    ///     Test that a credential from the prompt is only remembered when the operation succeeded
    ///     (Commit), never merely because it was typed.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Commit_PromptedCredential_IsStoredOnlyAfterCommit()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw"));
        var callback = Create(store, prompt);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert before the commit: typed but not remembered
        Assert.Equal(0, store.Count);

        // Act
        callback.Commit();

        // Assert
        Assert.True(store.TryGet(FeedOrigin.Create(FeedIndex), out var stored));
        Assert.Equal("typed-pw", stored!.Secret);
    }

    /// <summary>
    ///     Test that Commit stores nothing when no credential was prompted for (a silent success).
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Commit_NothingPrompted_StoresNothing()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var callback = Create(store, prompt: null);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act
        callback.Commit();

        // Assert
        Assert.Equal(0, store.Count);
    }

    /// <summary>
    ///     Test that Reject forgets the feed's remembered credential after an authentication
    ///     failure.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Reject_SessionCredential_IsForgotten()
    {
        // Arrange
        var store = new SessionCredentialStore();
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "stale"));
        var callback = Create(store, prompt: null);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act
        callback.Reject();

        // Assert
        Assert.Equal(0, store.Count);
    }

    /// <summary>
    ///     Test that cancelling the operation while the dialog is open abandons the question and
    ///     throws a cancellation, which is not a decline.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_CancelledWhilePrompting_ThrowsOperationCanceled()
    {
        // Arrange: a prompt nobody answers
        var prompt = new FakeCredentialPrompt { NeverAnswer = true };
        var callback = Create(new SessionCredentialStore(), prompt);
        using var cancellation = new CancellationTokenSource();

        // Act
        var pending = callback.GetCredentialAsync(FeedIndex, cancellation.Token);
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(callback.UserDeclined);
    }

    /// <summary>
    ///     Test that the callback property has the shape the NuGet library expects and behaves
    ///     like the method.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Callback_InvokedLikeTheNuGetLibraryDoes_AnswersFromTheStore()
    {
        // Arrange
        var store = new SessionCredentialStore();
        var remembered = new NuGetCredential("svc", "pw");
        store.Set(FeedOrigin.Create(FeedIndex), remembered);
        Func<Uri, CancellationToken, Task<NuGetCredential?>> callback = Create(store, prompt: null).Callback;

        // Act
        var credential = await callback(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Same(remembered, credential);
    }

    /// <summary>
    ///     Test that missing constructor arguments are refused.
    /// </summary>
    [Fact]
    public void PackageCredentialCallback_Constructor_NullArguments_Throw()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var store = new SessionCredentialStore();

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new PackageCredentialCallback(null!, "Feed", store));
        Assert.Throws<ArgumentNullException>(() => new PackageCredentialCallback(origin, null!, store));
        Assert.Throws<ArgumentNullException>(() => new PackageCredentialCallback(origin, "Feed", null!));
    }

    /// <summary>
    ///     Creates a callback for the test feed.
    /// </summary>
    /// <param name="store">The session store.</param>
    /// <param name="prompt">The prompt, or <see langword="null"/> for a silent callback.</param>
    /// <param name="previousCredentialRejected">Whether an earlier credential was rejected.</param>
    /// <returns>The callback.</returns>
    private static PackageCredentialCallback Create(
        SessionCredentialStore store, ICredentialPrompt? prompt, bool previousCredentialRejected = false) =>
        new(FeedOrigin.Create(FeedIndex), "Feed", store, prompt, previousCredentialRejected);
}
