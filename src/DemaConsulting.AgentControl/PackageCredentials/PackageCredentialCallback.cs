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

using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     Answers the NuGet library's "this URL needs credentials" questions for one operation
///     against one feed: scoped to the feed's origin, asked at most once, and consulting the
///     session store before the user.
/// </summary>
/// <remarks>
///     <para>
///     One instance serves exactly one operation (a version listing, a download). That lifetime is
///     what makes the memoization safe: the NuGet library may challenge several URLs of the feed
///     (its service index, its version list, a package file) and may retry, but the user is asked
///     at most once for the operation, and a "no" - the user cancelling, or nothing being
///     available - is remembered too, so a retry cannot re-open the dialog. The answer is shared
///     by concurrent challenges rather than computed twice.
///     </para>
///     <para>
///     <b>Scoping.</b> A request for any URI whose origin differs from the feed's - a redirect to
///     another host, another port, another scheme - is answered with <see langword="null"/>
///     without consulting the store or the prompt, so a credential is only ever offered to the
///     feed it was meant for. <b>Tier order.</b> The credential comes from the session store
///     first, then from the user's own credential store (<see cref="IRememberedCredentialStore"/>,
///     consulted once per feed per session, without any user interface, and only when one was
///     supplied), then from the prompt (when one was supplied; without one the instance is
///     "silent"). A credential obtained from the prompt is put into the session store only when
///     <see cref="CommitAsync"/> is called after the operation succeeded - and, when the user
///     asked to remember it, handed to the user's credential store at the same moment - and
///     <see cref="RejectAsync"/> forgets whatever credential was offered after the feed refused it
///     (removing it from the user's credential store too when it came from there), so a wrong
///     credential is never kept or reused.
///     </para>
///     <para>
///     Secrets never reach logs: the optional logger receives only the feed origin (scheme, host,
///     port) and which tier answered. Thread-safe.
///     </para>
/// </remarks>
internal sealed class PackageCredentialCallback
{
    /// <summary>
    ///     The origin of the feed; the only origin that ever receives a credential.
    /// </summary>
    private readonly FeedOrigin _feed;

    /// <summary>
    ///     The alias of the configured source, shown by the prompt.
    /// </summary>
    private readonly string _sourceAlias;

    /// <summary>
    ///     The session store consulted first, and updated by <see cref="CommitAsync"/> and
    ///     <see cref="RejectAsync"/>.
    /// </summary>
    private readonly SessionCredentialStore _store;

    /// <summary>
    ///     Asks the user, or <see langword="null"/> for a silent callback.
    /// </summary>
    private readonly ICredentialPrompt? _prompt;

    /// <summary>
    ///     The user's own credential store, or <see langword="null"/> when credentials cannot be
    ///     kept beyond the session.
    /// </summary>
    private readonly IRememberedCredentialStore? _remembered;

    /// <summary>
    ///     Whether the prompt should say that an earlier credential was just rejected.
    /// </summary>
    private readonly bool _previousCredentialRejected;

    /// <summary>
    ///     Receives origin-only diagnostics, or <see langword="null"/> for none.
    /// </summary>
    private readonly ILogger? _logger;

    /// <summary>
    ///     Serializes creation of the memoized answer.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The memoized answer to the feed's first challenge (a credential, or <see langword="null"/>
    ///     for "none"), shared by every later challenge of the same operation.
    /// </summary>
    private Task<NuGetCredential?>? _answer;

    /// <summary>
    ///     This operation's redaction lease on the credential it uses (a remembered one or one the
    ///     user typed), held until <see cref="Release"/>.
    /// </summary>
    private IDisposable? _lease;

    /// <summary>
    ///     Whether the look-up of a saved sign-in in the user's credential store failed (so
    ///     nothing is known about what that store holds, and keeping a new sign-in there must not
    ///     be offered). Written by the single resolution of the memoized answer.
    /// </summary>
    private volatile bool _savedLookupFailed;

    /// <summary>
    ///     Initializes the callback for one operation against one feed.
    /// </summary>
    /// <param name="feed">The origin of the feed the operation talks to.</param>
    /// <param name="sourceAlias">The alias of the configured source, shown by the prompt.</param>
    /// <param name="store">The session credential store.</param>
    /// <param name="prompt">Asks the user for a credential, or <see langword="null"/> to answer
    ///     from the store only (a silent operation).</param>
    /// <param name="previousCredentialRejected">Whether an earlier credential for this feed was
    ///     already rejected during this operation, so the prompt can say so.</param>
    /// <param name="logger">Receives origin-only diagnostics, or <see langword="null"/>.</param>
    /// <param name="remembered">The user's own credential store, consulted once per feed per
    ///     session for a saved sign-in and asked to keep a credential the user chose to
    ///     remember; <see langword="null"/> when there is none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="feed"/>,
    ///     <paramref name="sourceAlias"/> or <paramref name="store"/> is <see langword="null"/>.</exception>
    public PackageCredentialCallback(
        FeedOrigin feed,
        string sourceAlias,
        SessionCredentialStore store,
        ICredentialPrompt? prompt = null,
        bool previousCredentialRejected = false,
        ILogger? logger = null,
        IRememberedCredentialStore? remembered = null)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(sourceAlias);
        ArgumentNullException.ThrowIfNull(store);

        _feed = feed;
        _sourceAlias = sourceAlias;
        _store = store;
        _prompt = prompt;
        _previousCredentialRejected = previousCredentialRejected;
        _logger = logger;
        _remembered = remembered;
    }

    /// <summary>
    ///     Gets the credential this callback handed to the NuGet library, or <see langword="null"/>
    ///     when it supplied none.
    /// </summary>
    public NuGetCredential? SuppliedCredential { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the credential this callback supplied came from the
    ///     user's own credential store (saved earlier, or found there this session), so a
    ///     rejection of it removes it from there as well.
    /// </summary>
    public bool SuppliedCredentialWasSaved { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the user ticked "Remember on this computer" when asked
    ///     for the credential this callback supplied.
    /// </summary>
    public bool RememberRequested { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the user was asked for a credential during this
    ///     operation.
    /// </summary>
    public bool WasPrompted { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the user was asked and declined to sign in (cancelled
    ///     the dialog), so the failure that follows can be reported as "sign-in cancelled" rather
    ///     than "access denied".
    /// </summary>
    public bool UserDeclined { get; private set; }

    /// <summary>
    ///     Gets the callback in the shape the NuGet library expects for
    ///     <c>NuGetCacheOptions.CredentialCallback</c>.
    /// </summary>
    public Func<Uri, CancellationToken, Task<NuGetCredential?>> Callback => GetCredentialAsync;

    /// <summary>
    ///     Answers a challenge for a URI: the feed's credential when the URI belongs to the feed,
    ///     otherwise nothing.
    /// </summary>
    /// <param name="challengedUri">The URI the NuGet library was challenged on.</param>
    /// <param name="cancellationToken">Cancelled when the operation is cancelled or the sign-in
    ///     times out.</param>
    /// <returns>The credential, or <see langword="null"/> when the URI is foreign, nothing is
    ///     remembered and no prompt is available, or the user declined. Never throws for a
    ///     decline; a cancelled token surfaces as <see cref="OperationCanceledException"/>.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is cancelled while waiting for the user.</exception>
    public Task<NuGetCredential?> GetCredentialAsync(Uri challengedUri, CancellationToken cancellationToken)
    {
        // A challenge from anywhere but the feed (a redirect target, a CDN, another port) never
        // gets the credential, and never triggers a prompt either
        if (!_feed.Matches(challengedUri))
        {
            _logger?.LogDebug("Credential request for a different origin than {FeedOrigin} was refused.", _feed);
            return Task.FromResult<NuGetCredential?>(null);
        }

        // One answer per operation: the first challenge resolves it and every other challenge
        // (including the NuGet library's retries) shares the result, a "no" included
        Task<NuGetCredential?> answer;
        lock (_gate)
        {
            _answer ??= ResolveAsync(cancellationToken);
            answer = _answer;
        }

        return answer.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Keeps a credential the user just typed for the rest of the session - and, when the user
    ///     asked to remember it, in their own credential store - for when the operation that used
    ///     it succeeded.
    /// </summary>
    /// <remarks>
    ///     Only a credential that came from the prompt is stored; one that came from the store is
    ///     already there. Calling it after a failed operation would remember a credential that may
    ///     be wrong, which is why the caller decides. A credential store that cannot keep the
    ///     credential (or takes too long) does not fail the operation: the credential is still
    ///     kept for the session and the problem is logged without any secret.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request to the user's credential store.</param>
    /// <returns>A task that completes when the credential has been kept.</returns>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (WasPrompted && SuppliedCredential is not null)
        {
            var saved = false;
            if (RememberRequested && _remembered is not null)
            {
                saved = await TrySaveAsync(SuppliedCredential, cancellationToken);
            }

            _store.Set(_feed, SuppliedCredential, saved);
            _logger?.LogInformation(
                "A credential for {FeedOrigin} is remembered for this session (saved on this computer: {Saved}).",
                _feed, saved);
        }

        Release();
    }

    /// <summary>
    ///     Ends this operation's redaction lease on the credential it used - a remembered session
    ///     credential or the one the user typed - when the operation is over. A credential kept by
    ///     <see cref="CommitAsync"/> stays registered by the store; one that was not kept stops
    ///     being tracked once no other operation uses it. Safe to call more than once.
    /// </summary>
    public void Release()
    {
        IDisposable? lease;
        lock (_gate)
        {
            lease = _lease;
            _lease = null;
        }

        lease?.Dispose();
    }

    /// <summary>
    ///     Forgets the credential this operation offered, for when the feed rejected it - from the
    ///     session and, when it came from the user's own credential store, from there too.
    /// </summary>
    /// <remarks>
    ///     Only the credential this operation actually offered is forgotten: a concurrent
    ///     operation may have stored a newer one meanwhile, and that one is left alone. A
    ///     credential store that cannot remove it (or takes too long) is logged without any secret.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request to the user's credential store.</param>
    /// <returns>A task that completes when the credential has been forgotten.</returns>
    public async Task RejectAsync(CancellationToken cancellationToken)
    {
        var rejected = SuppliedCredential;
        if (rejected is not null)
        {
            if (_store.Forget(_feed, rejected))
            {
                _logger?.LogInformation("The remembered credential for {FeedOrigin} was rejected and forgotten.", _feed);
            }

            if (SuppliedCredentialWasSaved && _remembered is not null)
            {
                await TryRemoveSavedAsync(rejected, cancellationToken);
            }
        }

        Release();
    }

    /// <summary>
    ///     Resolves the credential for the feed: the session store first, then the user's own
    ///     credential store, then the prompt.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the user.</param>
    /// <returns>The credential, or <see langword="null"/> when there is none.</returns>
    private async Task<NuGetCredential?> ResolveAsync(CancellationToken cancellationToken)
    {
        // Tier: a credential already known this session (typed earlier, or found in the user's
        // credential store earlier)
        if (TryUseSessionCredential())
        {
            return SuppliedCredential;
        }

        // Tier: the user's own credential store, asked once per feed per session and never with
        // any user interface; what it finds joins the session store, so it is read back from there
        if (_remembered is not null)
        {
            await LookUpSavedCredentialAsync(cancellationToken);
            if (TryUseSessionCredential())
            {
                return SuppliedCredential;
            }
        }

        // A silent callback never asks
        if (_prompt is null)
        {
            _logger?.LogDebug("No session credential for {FeedOrigin} and prompting is not allowed.", _feed);
            return null;
        }

        // Tier: ask the user, once; the option to remember is offered only when something can keep
        // it - and not after a failed look-up, which says the user's credential store is unusable
        var canRemember = _remembered is not null && !_savedLookupFailed && await IsRememberAvailableAsync(cancellationToken);
        WasPrompted = true;
        _logger?.LogDebug("Asking the user for a credential for {FeedOrigin}.", _feed);
        var answer = await _prompt.PromptAsync(
            new CredentialPromptRequest(_feed, _sourceAlias, _previousCredentialRejected, canRemember),
            cancellationToken);
        if (answer is null)
        {
            UserDeclined = true;
            _logger?.LogDebug("The user declined to sign in to {FeedOrigin}.", _feed);
            return null;
        }

        // From here the typed secret is in use: keep it redacted until Release
        HoldLease(SecretRedactor.Lease(answer.Credential));
        SuppliedCredential = answer.Credential;
        RememberRequested = answer.Remember && canRemember;
        return answer.Credential;
    }

    /// <summary>
    ///     Uses the credential the session store holds for the feed, if any; this operation takes
    ///     its own redaction lease on it, so another operation forgetting the credential cannot end
    ///     redaction while this one still uses it.
    /// </summary>
    /// <returns><see langword="true"/> when a session credential was found and is now supplied.</returns>
    private bool TryUseSessionCredential()
    {
        if (!_store.TryAcquire(_feed, out var remembered, out var lease, out var fromRememberedStore) ||
            remembered is null || lease is null)
        {
            return false;
        }

        HoldLease(lease);
        SuppliedCredential = remembered;
        SuppliedCredentialWasSaved = fromRememberedStore;
        _logger?.LogDebug("Using the session credential for {FeedOrigin}.", _feed);
        return true;
    }

    /// <summary>
    ///     Makes sure the user's credential store has been asked for the feed's saved sign-in this
    ///     session. A failure to ask is not a failure to sign in: the user is simply asked instead,
    ///     but without the option to remember the sign-in, because a store that could not be read
    ///     cannot be relied on to keep anything.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the lookup is done.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled.</exception>
    private async Task LookUpSavedCredentialAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _store.EnsureRememberedLookupAsync(_feed, FillIntoSessionStoreAsync).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The lookup is shared, so every operation that waited for it learns of the failure
            _savedLookupFailed = true;
            _logger?.LogWarning(ex, "Looking up a saved sign-in for {FeedOrigin} failed.", _feed);
        }
    }

    /// <summary>
    ///     Asks the user's credential store for the feed's saved sign-in and, when it has one,
    ///     puts it into the session store. Runs at most once per feed per session and does not
    ///     depend on any one operation's cancellation, because other operations wait for it too
    ///     (the store enforces its own time limit). A failure is not handled here: it faults the
    ///     shared lookup so that every operation that waits for it sees it.
    /// </summary>
    /// <returns>A task that completes when the lookup is done.</returns>
    private async Task FillIntoSessionStoreAsync()
    {
        var found = await _remembered!.FillAsync(_feed, CancellationToken.None);
        if (found is not null)
        {
            _store.Set(_feed, found, fromRememberedStore: true);
            _logger?.LogInformation("A saved sign-in for {FeedOrigin} was found on this computer.", _feed);
        }
        else
        {
            _logger?.LogDebug("No saved sign-in for {FeedOrigin} was found on this computer.", _feed);
        }
    }

    /// <summary>
    ///     Determines whether a sign-in for the feed could be kept on this computer.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when the user's credential store can keep it; a failure to
    ///     find out counts as "no".</returns>
    private async Task<bool> IsRememberAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _remembered!.IsAvailableAsync(_feed, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Checking for a credential helper for {FeedOrigin} failed.", _feed);
            return false;
        }
    }

    /// <summary>
    ///     Asks the user's credential store to keep a credential, tolerating its failure.
    /// </summary>
    /// <param name="credential">The credential to keep.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> when the store accepted the credential.</returns>
    private async Task<bool> TrySaveAsync(NuGetCredential credential, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _remembered!.ApproveAsync(_feed, credential, cancellationToken);
            if (!saved)
            {
                _logger?.LogWarning("The credential for {FeedOrigin} could not be saved on this computer.", _feed);
            }

            return saved;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Saving the credential for {FeedOrigin} failed.", _feed);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Asks the user's credential store to remove a credential the feed rejected, tolerating
    ///     its failure.
    /// </summary>
    /// <param name="credential">The rejected credential.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the request has been made.</returns>
    private async Task TryRemoveSavedAsync(NuGetCredential credential, CancellationToken cancellationToken)
    {
        try
        {
            if (await _remembered!.RejectAsync(_feed, credential, cancellationToken))
            {
                _logger?.LogInformation("The rejected sign-in for {FeedOrigin} was removed from this computer.", _feed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Removing the sign-in for {FeedOrigin} failed.", _feed);
        }
        catch (OperationCanceledException)
        {
            // The request was abandoned; the credential is still forgotten for this session
        }
    }

    /// <summary>
    ///     Keeps a redaction lease for the credential this operation uses.
    /// </summary>
    /// <param name="lease">The lease to hold until <see cref="Release"/>.</param>
    private void HoldLease(IDisposable lease)
    {
        IDisposable? previous;
        lock (_gate)
        {
            previous = _lease;
            _lease = lease;
        }

        previous?.Dispose();
    }
}
