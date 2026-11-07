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
///     first, then from the prompt (when one was supplied; without one the instance is "silent").
///     A credential obtained from the prompt is put into the store only when
///     <see cref="Commit"/> is called after the operation succeeded, and
///     <see cref="Reject"/> forgets whatever credential was offered after the feed refused it, so
///     a wrong credential is never kept or reused.
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
    ///     The session store consulted first, and updated by <see cref="Commit"/> and
    ///     <see cref="Reject"/>.
    /// </summary>
    private readonly SessionCredentialStore _store;

    /// <summary>
    ///     Asks the user, or <see langword="null"/> for a silent callback.
    /// </summary>
    private readonly ICredentialPrompt? _prompt;

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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="feed"/>,
    ///     <paramref name="sourceAlias"/> or <paramref name="store"/> is <see langword="null"/>.</exception>
    public PackageCredentialCallback(
        FeedOrigin feed,
        string sourceAlias,
        SessionCredentialStore store,
        ICredentialPrompt? prompt = null,
        bool previousCredentialRejected = false,
        ILogger? logger = null)
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
    }

    /// <summary>
    ///     Gets the credential this callback handed to the NuGet library, or <see langword="null"/>
    ///     when it supplied none.
    /// </summary>
    public NuGetCredential? SuppliedCredential { get; private set; }

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
    ///     Keeps a credential the user just typed for the rest of the session, for when the
    ///     operation that used it succeeded.
    /// </summary>
    /// <remarks>
    ///     Only a credential that came from the prompt is stored; one that came from the store is
    ///     already there. Calling it after a failed operation would remember a credential that may
    ///     be wrong, which is why the caller decides.
    /// </remarks>
    public void Commit()
    {
        if (WasPrompted && SuppliedCredential is not null)
        {
            _store.Set(_feed, SuppliedCredential);
            _logger?.LogInformation("A credential for {FeedOrigin} is remembered for this session.", _feed);
        }
    }

    /// <summary>
    ///     Forgets the feed's remembered credential, for when the feed rejected a credential this
    ///     operation offered.
    /// </summary>
    public void Reject()
    {
        if (_store.Forget(_feed))
        {
            _logger?.LogInformation("The remembered credential for {FeedOrigin} was rejected and forgotten.", _feed);
        }
    }

    /// <summary>
    ///     Resolves the credential for the feed: the session store first, then the prompt.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the user.</param>
    /// <returns>The credential, or <see langword="null"/> when there is none.</returns>
    private async Task<NuGetCredential?> ResolveAsync(CancellationToken cancellationToken)
    {
        // Tier: a credential the user already typed this session
        if (_store.TryGet(_feed, out var remembered) && remembered is not null)
        {
            SuppliedCredential = remembered;
            _logger?.LogDebug("Using the session credential for {FeedOrigin}.", _feed);
            return remembered;
        }

        // A silent callback never asks
        if (_prompt is null)
        {
            _logger?.LogDebug("No session credential for {FeedOrigin} and prompting is not allowed.", _feed);
            return null;
        }

        // Tier: ask the user, once
        WasPrompted = true;
        _logger?.LogDebug("Asking the user for a credential for {FeedOrigin}.", _feed);
        var entered = await _prompt.PromptAsync(
            new CredentialPromptRequest(_feed, _sourceAlias, _previousCredentialRejected), cancellationToken);
        if (entered is null)
        {
            UserDeclined = true;
            _logger?.LogDebug("The user declined to sign in to {FeedOrigin}.", _feed);
            return null;
        }

        SuppliedCredential = entered;
        return entered;
    }
}
