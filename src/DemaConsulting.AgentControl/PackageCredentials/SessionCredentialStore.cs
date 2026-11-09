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

using System.Collections.Concurrent;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     Remembers the credentials the user typed for package feeds for as long as the application
///     runs - in memory only - so a feed that needs a sign-in asks once per session rather than
///     once per operation.
/// </summary>
/// <remarks>
///     <para>
///     This store is the whole of AgentControl's own credential memory: it has no persistence of
///     any kind (nothing is written to the settings file, a repo pin, a log or the target repo),
///     offers callers no way to enumerate its contents (only <see cref="Clear"/> walks the stored
///     credentials, internally, to withdraw each one from the redactor), and is emptied by
///     <see cref="Clear"/> when the application exits - which also invalidates any lookup of the
///     user's credential store that is still in flight, so a late result cannot refill it.
///     Credentials are keyed by <see cref="FeedOrigin"/>, so a credential is
///     only ever found by the exact feed it was typed for. The secret lives in an ordinary managed
///     string (the NuGet library needs one), which cannot be wiped from memory; the guarantee is
///     "never persisted", not "never in process memory".
///     </para>
///     <para>
///     A credential the user typed is stored only after an operation that used it succeeded
///     (<see cref="PackageCredentialCallback.CommitAsync"/>), and is forgotten
///     (<see cref="Forget(FeedOrigin)"/>) as soon as a feed rejects it, so a wrong or revoked
///     credential is never reused. A credential the user's own credential store already held is
///     different: the once-per-session lookup (<see cref="EnsureRememberedLookupAsync"/>) puts it
///     here before any operation has used it, because it was saved on purpose earlier, and a
///     rejection removes it from here and from that store. Every stored secret is registered with
///     <see cref="SecretRedactor"/> for as long as it is stored, so an occurrence of it in any log
///     text or exception message is removed. Thread-safe: operations on different sources run
///     concurrently.
///     </para>
/// </remarks>
internal sealed class SessionCredentialStore
{
    /// <summary>
    ///     The remembered credentials by feed origin.
    /// </summary>
    private readonly ConcurrentDictionary<FeedOrigin, NuGetCredential> _credentials = new();

    /// <summary>
    ///     Serializes changes, so each stored credential is registered with, and withdrawn from,
    ///     the secret redactor exactly once.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The origins whose remembered credential came from (or was saved to) the user's
    ///     credential store, so a rejection can also remove it from there. Guarded by
    ///     <see cref="_gate"/>.
    /// </summary>
    private readonly HashSet<FeedOrigin> _fromRememberedStore = [];

    /// <summary>
    ///     The once-per-origin lookups of the user's credential store, shared by every operation
    ///     that needs the answer.
    /// </summary>
    private readonly ConcurrentDictionary<FeedOrigin, Lazy<Task>> _rememberedLookups = new();

    /// <summary>
    ///     Counts how many times the store has been cleared. A remembered-store lookup records the
    ///     value current when it started; a lookup that finishes after <see cref="Clear"/> sees a
    ///     different value and its credential is discarded. Changed only under
    ///     <see cref="_gate"/>.
    /// </summary>
    private long _generation;

    /// <summary>
    ///     The generation in which the current asynchronous flow's remembered-store lookup started,
    ///     or <see langword="null"/> outside a lookup. It flows with the lookup's continuations so
    ///     that <see cref="Set"/>, called from the lookup, can tell that the store was cleared
    ///     while the lookup was in flight.
    /// </summary>
    private readonly AsyncLocal<long?> _lookupGeneration = new();

    /// <summary>
    ///     Gets how many feeds currently have a remembered credential. Exposed for display and
    ///     tests; the credentials themselves cannot be enumerated.
    /// </summary>
    public int Count => _credentials.Count;

    /// <summary>
    ///     Looks up the credential remembered for a feed.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="credential">The credential when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a credential is remembered for exactly this
    ///     origin.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origin"/> is
    ///     <see langword="null"/>.</exception>
    public bool TryGet(FeedOrigin origin, out NuGetCredential? credential)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return _credentials.TryGetValue(origin, out credential);
    }

    /// <summary>
    ///     Looks up the credential remembered for a feed and, in the same step, takes a redaction
    ///     lease on its secret for the caller, so the credential cannot become unprotected between
    ///     being found and being used.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="credential">The credential when the method returns <see langword="true"/>.</param>
    /// <param name="lease">The lease that keeps the secret redacted until it is disposed, when the
    ///     method returns <see langword="true"/>. It is independent of the store's own
    ///     registration, so <see cref="Forget(FeedOrigin)"/> or <see cref="Clear"/> by another operation does not
    ///     end redaction for a caller that is still using the credential.</param>
    /// <returns><see langword="true"/> when a credential is remembered for exactly this
    ///     origin.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origin"/> is
    ///     <see langword="null"/>.</exception>
    public bool TryAcquire(FeedOrigin origin, out NuGetCredential? credential, out IDisposable? lease) =>
        TryAcquire(origin, out credential, out lease, out _);

    /// <summary>
    ///     Looks up the credential remembered for a feed, taking a redaction lease on its secret
    ///     (see <see cref="TryAcquire(FeedOrigin, out NuGetCredential?, out IDisposable?)"/>) and
    ///     also reporting, in the same locked step, whether the user's own credential store holds
    ///     that credential too.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="credential">The credential when the method returns <see langword="true"/>.</param>
    /// <param name="lease">The redaction lease when the method returns <see langword="true"/>.</param>
    /// <param name="fromRememberedStore"><see langword="true"/> when the credential came from, or
    ///     was saved to, the user's credential store.</param>
    /// <returns><see langword="true"/> when a credential is remembered for exactly this
    ///     origin.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origin"/> is
    ///     <see langword="null"/>.</exception>
    public bool TryAcquire(
        FeedOrigin origin, out NuGetCredential? credential, out IDisposable? lease, out bool fromRememberedStore)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_gate)
        {
            if (_credentials.TryGetValue(origin, out credential))
            {
                lease = SecretRedactor.Lease(credential);
                fromRememberedStore = _fromRememberedStore.Contains(origin);
                return true;
            }
        }

        lease = null;
        fromRememberedStore = false;
        return false;
    }

    /// <summary>
    ///     Remembers a credential for a feed, replacing any earlier one. A call made from a
    ///     remembered-store lookup that <see cref="Clear"/> has invalidated is ignored: the
    ///     credential is neither stored nor registered with the redactor.
    /// </summary>
    /// <param name="origin">The feed origin the credential was typed for.</param>
    /// <param name="credential">The credential to remember for the rest of the session.</param>
    /// <param name="fromRememberedStore"><see langword="true"/> when the credential came from, or
    ///     was just saved to, the user's credential store (see
    ///     <see cref="IRememberedCredentialStore"/>), so that a rejection of it by the feed must
    ///     also remove it from there.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is
    ///     <see langword="null"/>.</exception>
    public void Set(FeedOrigin origin, NuGetCredential credential, bool fromRememberedStore = false)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(credential);

        // The secret is registered for redaction before it is visible to anything else, and the
        // replaced credential (if any) is withdrawn
        lock (_gate)
        {
            // A saved credential found by a lookup that was started before Clear must not come back
            // after the store was emptied (the application is exiting): drop it unregistered
            if (_lookupGeneration.Value is { } lookupGeneration && lookupGeneration != _generation)
            {
                return;
            }

            SecretRedactor.Register(credential);
            if (_credentials.TryGetValue(origin, out var replaced))
            {
                SecretRedactor.Unregister(replaced);
            }

            _credentials[origin] = credential;
            if (fromRememberedStore)
            {
                _fromRememberedStore.Add(origin);
            }
            else
            {
                _fromRememberedStore.Remove(origin);
            }
        }
    }

    /// <summary>
    ///     Determines whether the credential remembered for a feed came from, or was saved to,
    ///     the user's credential store.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <returns><see langword="true"/> when a credential is remembered for the origin and the
    ///     user's credential store holds it too.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origin"/> is
    ///     <see langword="null"/>.</exception>
    public bool IsFromRememberedStore(FeedOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_gate)
        {
            return _fromRememberedStore.Contains(origin);
        }
    }

    /// <summary>
    ///     Makes sure the user's credential store has been consulted for a feed - once per origin
    ///     for the whole session, however many operations ask at once - and lets every caller wait
    ///     for that one lookup.
    /// </summary>
    /// <remarks>
    ///     The lookup itself is expected to put what it finds into this store (see
    ///     <see cref="Set"/>); callers then read it back with
    ///     <see cref="TryAcquire(FeedOrigin, out NuGetCredential?, out IDisposable?, out bool)"/>. A lookup
    ///     that found nothing is not repeated, so a feed without a saved sign-in costs one
    ///     lookup per session rather than one per operation. A fault in the lookup is surfaced to
    ///     every waiting caller (and is not retried). A lookup still running when <see cref="Clear"/>
    ///     is called is invalidated: whatever it passes to <see cref="Set"/> afterwards is discarded.
    /// </remarks>
    /// <param name="origin">The feed origin.</param>
    /// <param name="lookup">Performs the lookup; run at most once per origin.</param>
    /// <returns>A task that completes when the lookup for the origin has finished.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is
    ///     <see langword="null"/>.</exception>
    public Task EnsureRememberedLookupAsync(FeedOrigin origin, Func<Task> lookup)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(lookup);

        // Lazy so that a race between callers starts exactly one lookup. The lookup runs in its own
        // flow tagged with the generation it started in, so Clear can invalidate it
        return _rememberedLookups.GetOrAdd(origin, _ => new Lazy<Task>(() => StartLookup(lookup))).Value;
    }

    /// <summary>
    ///     Starts a remembered-store lookup on a worker thread, tagged with the current generation.
    /// </summary>
    /// <param name="lookup">The lookup.</param>
    /// <returns>The task of the lookup.</returns>
    private Task StartLookup(Func<Task> lookup)
    {
        long generation;
        lock (_gate)
        {
            generation = _generation;
        }

        return Task.Run(() =>
        {
            // Set inside the worker's own execution context, so it reaches the lookup's
            // continuations and nothing else
            _lookupGeneration.Value = generation;
            return lookup();
        });
    }

    /// <summary>
    ///     Forgets the credential remembered for a feed, for when the feed rejected it.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <returns><see langword="true"/> when a credential was remembered and has been
    ///     removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origin"/> is
    ///     <see langword="null"/>.</exception>
    public bool Forget(FeedOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_gate)
        {
            if (!_credentials.TryRemove(origin, out var removed))
            {
                return false;
            }

            _fromRememberedStore.Remove(origin);
            SecretRedactor.Unregister(removed);
            return true;
        }
    }

    /// <summary>
    ///     Forgets the credential remembered for a feed, but only when it is still the given one,
    ///     for when a feed rejected that credential.
    /// </summary>
    /// <remarks>
    ///     A concurrent operation may have replaced the credential since the rejected one was
    ///     handed out (for example the user signed in again while a slow request was failing);
    ///     forgetting by origin alone would then discard the newer, possibly valid, credential.
    /// </remarks>
    /// <param name="origin">The feed origin.</param>
    /// <param name="rejected">The credential the feed rejected.</param>
    /// <returns><see langword="true"/> when the remembered credential equaled
    ///     <paramref name="rejected"/> and has been removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is
    ///     <see langword="null"/>.</exception>
    public bool Forget(FeedOrigin origin, NuGetCredential rejected)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(rejected);
        lock (_gate)
        {
            if (!_credentials.TryGetValue(origin, out var current) || !current.Equals(rejected))
            {
                return false;
            }

            _credentials.TryRemove(origin, out _);
            _fromRememberedStore.Remove(origin);
            SecretRedactor.Unregister(current);
            return true;
        }
    }

    /// <summary>
    ///     Forgets every remembered credential, for when the application exits, and invalidates
    ///     every remembered-store lookup that is still in flight: a credential such a lookup finds
    ///     afterwards is discarded rather than stored, so clearing cannot be undone by a late
    ///     lookup.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            // Invalidate in-flight lookups first, under the same lock Set takes
            _generation++;

            // Enumerated only here, to withdraw each secret from the redactor; callers cannot
            // enumerate the store
            foreach (var credential in _credentials.Values)
            {
                SecretRedactor.Unregister(credential);
            }

            _credentials.Clear();
            _fromRememberedStore.Clear();
            _rememberedLookups.Clear();
        }
    }
}
