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
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "pw-foreign-test"));
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
        var remembered = new NuGetCredential("svc", "pw-session-test");
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
        await callback.CommitAsync(CancellationToken.None);

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
        await callback.CommitAsync(CancellationToken.None);

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
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "pw-stale-test"));
        var callback = Create(store, prompt: null);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act
        await callback.RejectAsync(CancellationToken.None);

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
        var remembered = new NuGetCredential("svc", "pw-delegate-test");
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
    ///     Test that a credential the user typed is registered for redaction as soon as it is
    ///     entered and stays registered only as long as it is used: it is withdrawn by Release or
    ///     Reject, and by Commit once the store - which then holds and tracks it - forgets it.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_PromptedCredential_IsRegisteredForRedactionUntilTheOperationEnds()
    {
        // Arrange: three operations, each with its own typed secret
        var released = "sec-" + Guid.NewGuid().ToString("N");
        var rejected = "sec-" + Guid.NewGuid().ToString("N");
        var committed = "sec-" + Guid.NewGuid().ToString("N");
        var store = new SessionCredentialStore();

        // Act / Assert: released
        var releaseCallback = await PromptAsync(store, released);
        Assert.DoesNotContain(released, SecretRedactor.Redact(released), StringComparison.Ordinal);
        releaseCallback.Release();
        releaseCallback.Release();
        Assert.Equal(released, SecretRedactor.Redact(released));

        // Act / Assert: rejected
        var rejectCallback = await PromptAsync(store, rejected);
        Assert.DoesNotContain(rejected, SecretRedactor.Redact(rejected), StringComparison.Ordinal);
        await rejectCallback.RejectAsync(CancellationToken.None);
        Assert.Equal(rejected, SecretRedactor.Redact(rejected));
        Assert.Equal(0, store.Count);

        // Act / Assert: committed - the store keeps tracking it until it is forgotten
        var commitCallback = await PromptAsync(store, committed);
        await commitCallback.CommitAsync(CancellationToken.None);
        Assert.DoesNotContain(committed, SecretRedactor.Redact(committed), StringComparison.Ordinal);
        Assert.Equal(1, store.Count);
        Assert.True(store.Forget(FeedOrigin.Create(FeedIndex)));
        Assert.Equal(committed, SecretRedactor.Redact(committed));
    }

    /// <summary>
    ///     Test that an operation using a remembered credential keeps it redacted until that
    ///     operation ends, even when another operation's rejection forgets the credential (or the
    ///     store is cleared) meanwhile; once every operation has ended, the secret is unregistered.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_ConcurrentOperationRejectsCredential_ActiveOperationStaysRedactedUntilItEnds()
    {
        // Arrange: a remembered credential used by two concurrent operations, A and B
        var secret = "sec-" + Guid.NewGuid().ToString("N");
        var store = new SessionCredentialStore();
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", secret));
        var operationA = Create(store, prompt: null);
        var operationB = Create(store, prompt: null);
        await operationA.GetCredentialAsync(FeedIndex, CancellationToken.None);
        await operationB.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act: B's server rejects the credential, so B forgets it and ends
        await operationB.RejectAsync(CancellationToken.None);

        // Assert: the store no longer holds it, but A is still running and still protected, so what
        // A logs or throws now is redacted
        Assert.Equal(0, store.Count);
        var thrown = new InvalidOperationException($"request failed for {secret}");
        Assert.DoesNotContain(secret, SecretRedactor.Sanitize(thrown).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, SecretRedactor.Redact($"x {secret}"), StringComparison.Ordinal);

        // Act: A ends
        operationA.Release();

        // Assert: nobody uses the secret any more, so it is no longer tracked
        Assert.Equal($"x {secret}", SecretRedactor.Redact($"x {secret}"));
    }

    /// <summary>
    ///     Test that clearing the store (application exit) also does not end redaction for an
    ///     operation that still holds a lease, and that an operation that finishes by committing
    ///     leaves the store's own registration in place.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_StoreClearedWhileOperationRuns_OperationStaysRedactedUntilItEnds()
    {
        // Arrange
        var secret = "sec-" + Guid.NewGuid().ToString("N");
        var store = new SessionCredentialStore();
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", secret));
        var operation = Create(store, prompt: null);
        await operation.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act
        store.Clear();

        // Assert
        Assert.DoesNotContain(secret, SecretRedactor.Redact($"x {secret}"), StringComparison.Ordinal);
        await operation.CommitAsync(CancellationToken.None);
        Assert.Equal($"x {secret}", SecretRedactor.Redact($"x {secret}"));
    }

    /// <summary>
    ///     Test that a sign-in the user saved earlier is used without asking, the user's credential
    ///     store is asked once, and the credential is remembered for the session as one that the
    ///     store holds too.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_SavedCredentialExists_UsesItWithoutPrompting()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var remembered = new FakeRememberedCredentialStore();
        var saved = new NuGetCredential("svc", "pw-saved-" + Guid.NewGuid().ToString("N"));
        remembered.Save(origin, saved);
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        var callback = Create(store, prompt, remembered: remembered);

        // Act
        var credential = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Equal(saved, credential);
        Assert.Equal(0, prompt.AskCount);
        Assert.True(callback.SuppliedCredentialWasSaved);
        Assert.Equal(1, remembered.CountOf("fill"));
        Assert.True(store.IsFromRememberedStore(origin));
        callback.Release();
    }

    /// <summary>
    ///     Test that a silent callback (the background upgrade check) also reads a saved sign-in,
    ///     because looking one up needs no dialog.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_SilentCallbackAndSavedCredential_UsesIt()
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore();
        remembered.Save(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "pw-silent-" + Guid.NewGuid().ToString("N")));
        var callback = Create(new SessionCredentialStore(), prompt: null, remembered: remembered);

        // Act
        var credential = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.NotNull(credential);
        Assert.False(callback.WasPrompted);
        callback.Release();
    }

    /// <summary>
    ///     Test that the user's credential store is asked once per feed for the whole session: a
    ///     miss is not repeated by the next operation, which goes straight to the prompt.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_NothingSaved_LooksUpOncePerSessionThenPrompts()
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore();
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw"));

        // Act: two operations in a row, as two upgrade checks would be
        var first = Create(store, prompt, remembered: remembered);
        var second = Create(store, prompt, remembered: remembered);
        await first.GetCredentialAsync(FeedIndex, CancellationToken.None);
        await second.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert: one lookup only, and the user was asked each time because nothing was kept
        Assert.Equal(1, remembered.CountOf("fill"));
        Assert.Equal(2, prompt.AskCount);
        first.Release();
        second.Release();
    }

    /// <summary>
    ///     Test that operations that start at the same moment share one lookup and all get its
    ///     result (at application start-up several repo cards check the same server at once).
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_ConcurrentOperations_ShareOneLookup()
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore { FillDelay = TimeSpan.FromMilliseconds(300) };
        remembered.Save(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "pw-shared-" + Guid.NewGuid().ToString("N")));
        var store = new SessionCredentialStore();
        var callbacks = Enumerable.Range(0, 5).Select(_ => Create(store, prompt: null, remembered: remembered)).ToList();

        // Act
        var credentials = await Task.WhenAll(callbacks.Select(callback => callback.GetCredentialAsync(FeedIndex, CancellationToken.None)));

        // Assert
        Assert.Equal(1, remembered.CountOf("fill"));
        Assert.All(credentials, credential => Assert.NotNull(credential));
        callbacks.ForEach(callback => callback.Release());
    }

    /// <summary>
    ///     Test that a credential already known this session is used without consulting the user's
    ///     credential store at all.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_SessionCredentialExists_DoesNotConsultTheUsersStore()
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore();
        var store = new SessionCredentialStore();
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "pw-session-first-test"));
        var callback = Create(store, prompt: null, remembered: remembered);

        // Act
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Empty(remembered.Calls);
        callback.Release();
    }

    /// <summary>
    ///     Test that a credential store that throws is not a failure: the user is asked instead and
    ///     the dialog does not offer to remember.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_UsersStoreThrows_FallsThroughToPromptWithoutRememberOption()
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore { ThrowOnEveryCall = true };
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw-broken-store"), remember: true);
        var callback = Create(new SessionCredentialStore(), prompt, remembered: remembered);

        // Act
        var credential = await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Equal("typed-pw-broken-store", credential!.Secret);
        Assert.False(Assert.Single(prompt.Requests).CanRemember);
        Assert.False(callback.RememberRequested);
        callback.Release();
    }

    /// <summary>
    ///     Test that the prompt is told whether a sign-in can be remembered: yes when a credential
    ///     helper is available, no when none is configured or there is no store at all.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task PackageCredentialCallback_GetCredential_Prompting_ReportsWhetherRememberingIsPossible(bool available, bool expected)
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore { Available = available };
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(null);
        var callback = Create(new SessionCredentialStore(), prompt, remembered: remembered);

        // Act
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Equal(expected, Assert.Single(prompt.Requests).CanRemember);
    }

    /// <summary>
    ///     Test that without any user's credential store the prompt is told that remembering is
    ///     not possible.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_GetCredential_NoUsersStore_PromptCannotRemember()
    {
        // Arrange
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(null);
        var callback = Create(new SessionCredentialStore(), prompt);

        // Act
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.False(Assert.Single(prompt.Requests).CanRemember);
    }

    /// <summary>
    ///     Test that a credential the user asked to remember is handed to their credential store
    ///     once the operation succeeded, and then counts as saved.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Commit_RememberTicked_ApprovesInTheUsersStore()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var remembered = new FakeRememberedCredentialStore();
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw-remember"), remember: true);
        var callback = Create(store, prompt, remembered: remembered);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert before the commit: nothing is saved just because it was typed
        Assert.Equal(0, remembered.CountOf("approve"));

        // Act
        await callback.CommitAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, remembered.CountOf("approve"));
        Assert.Equal("typed-pw-remember", remembered.Peek(origin)!.Secret);
        Assert.True(store.IsFromRememberedStore(origin));
    }

    /// <summary>
    ///     Test that without the user's choice to remember, nothing is ever handed to their
    ///     credential store.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Commit_RememberNotTicked_NeverApproves()
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore();
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw-forget-me"), remember: false);
        var callback = Create(store, prompt, remembered: remembered);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act
        await callback.CommitAsync(CancellationToken.None);

        // Assert: kept for the session only
        Assert.Equal(0, remembered.CountOf("approve"));
        Assert.True(store.TryGet(FeedOrigin.Create(FeedIndex), out _));
        Assert.False(store.IsFromRememberedStore(FeedOrigin.Create(FeedIndex)));
    }

    /// <summary>
    ///     Test that a credential store that refuses (or fails) to keep the credential does not fail
    ///     the operation: the credential is still kept for the session, not marked as saved.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageCredentialCallback_Commit_UsersStoreRefusesOrFails_StillKeepsTheSessionCredential(bool throws)
    {
        // Arrange: a store that is available for the dialog but then fails to save
        var remembered = new FakeRememberedCredentialStore { RefuseChanges = !throws };
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw-refused"), remember: true);
        var callback = Create(store, prompt, remembered: remembered);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);
        remembered.ThrowOnEveryCall = throws;

        // Act
        await callback.CommitAsync(CancellationToken.None);

        // Assert
        var origin = FeedOrigin.Create(FeedIndex);
        Assert.True(store.TryGet(origin, out _));
        Assert.False(store.IsFromRememberedStore(origin));
    }

    /// <summary>
    ///     Test that a saved credential the feed rejects is forgotten for the session and removed
    ///     from the user's credential store, and that the next operation asks the user once.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Reject_SavedCredential_IsRemovedFromTheUsersStoreAndTheNextOperationPrompts()
    {
        // Arrange
        var origin = FeedOrigin.Create(FeedIndex);
        var remembered = new FakeRememberedCredentialStore();
        remembered.Save(origin, new NuGetCredential("svc", "pw-rejected-" + Guid.NewGuid().ToString("N")));
        var store = new SessionCredentialStore();
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", "typed-pw-after-reject"));
        var silent = Create(store, prompt: null, remembered: remembered);
        await silent.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act: the feed rejects what the silent attempt offered
        await silent.RejectAsync(CancellationToken.None);
        var asking = Create(store, prompt, previousCredentialRejected: true, remembered: remembered);
        var typed = await asking.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Assert
        Assert.Equal(1, remembered.CountOf("reject"));
        Assert.Null(remembered.Peek(origin));
        Assert.Equal(0, store.Count);
        Assert.Equal("typed-pw-after-reject", typed!.Secret);
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(1, remembered.CountOf("fill"));
        asking.Release();
    }

    /// <summary>
    ///     Test that a credential that was only typed this session is not looked for in, or removed
    ///     from, the user's credential store when the feed rejects it.
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Reject_SessionOnlyCredential_LeavesTheUsersStoreAlone()
    {
        // Arrange
        var remembered = new FakeRememberedCredentialStore();
        var store = new SessionCredentialStore();
        store.Set(FeedOrigin.Create(FeedIndex), new NuGetCredential("svc", "pw-session-only-test"));
        var callback = Create(store, prompt: null, remembered: remembered);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act
        await callback.RejectAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, store.Count);
        Assert.Empty(remembered.Calls);
    }

    /// <summary>
    ///     Test that rejecting a credential never forgets a newer credential that another operation
    ///     stored for the same feed in the meantime (the late rejection concerns only the credential
    ///     that operation offered).
    /// </summary>
    [Fact]
    public async Task PackageCredentialCallback_Reject_CredentialReplacedMeanwhile_KeepsTheNewerCredential()
    {
        // Arrange: an operation uses credential A
        var origin = FeedOrigin.Create(FeedIndex);
        var store = new SessionCredentialStore();
        store.Set(origin, new NuGetCredential("svc", "pw-old-A-" + Guid.NewGuid().ToString("N")));
        var slow = Create(store, prompt: null);
        await slow.GetCredentialAsync(FeedIndex, CancellationToken.None);

        // Act: meanwhile another operation signed in again (credential B), then A's rejection arrives
        var newer = new NuGetCredential("svc", "pw-new-B-" + Guid.NewGuid().ToString("N"));
        store.Set(origin, newer);
        await slow.RejectAsync(CancellationToken.None);

        // Assert
        Assert.True(store.TryGet(origin, out var current));
        Assert.Equal(newer, current);
    }

    /// <summary>
    ///     Makes a callback ask the scripted prompt, which answers with a credential carrying the
    ///     given secret.
    /// </summary>
    /// <param name="store">The session store.</param>
    /// <param name="secret">The secret the user "types".</param>
    /// <returns>The callback after it has been challenged.</returns>
    private static async Task<PackageCredentialCallback> PromptAsync(SessionCredentialStore store, string secret)
    {
        var prompt = new FakeCredentialPrompt();
        prompt.EnqueueAnswer(new NuGetCredential("typed", secret));
        var callback = Create(store, prompt);
        await callback.GetCredentialAsync(FeedIndex, CancellationToken.None);
        return callback;
    }

    /// <summary>
    ///     Creates a callback for the test feed.
    /// </summary>
    /// <param name="store">The session store.</param>
    /// <param name="prompt">The prompt, or <see langword="null"/> for a silent callback.</param>
    /// <param name="previousCredentialRejected">Whether an earlier credential was rejected.</param>
    /// <param name="remembered">The user's credential store, or <see langword="null"/> for none.</param>
    /// <returns>The callback.</returns>
    private static PackageCredentialCallback Create(
        SessionCredentialStore store,
        ICredentialPrompt? prompt,
        bool previousCredentialRejected = false,
        IRememberedCredentialStore? remembered = null) =>
        new(FeedOrigin.Create(FeedIndex), "Feed", store, prompt, previousCredentialRejected, remembered: remembered);
}
