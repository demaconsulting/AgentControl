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
///     exposes no enumeration of its contents, and is emptied by <see cref="Clear"/> when the
///     application exits. Credentials are keyed by <see cref="FeedOrigin"/>, so a credential is
///     only ever found by the exact feed it was typed for. The secret lives in an ordinary managed
///     string (the NuGet library needs one), which cannot be wiped from memory; the guarantee is
///     "never persisted", not "never in process memory".
///     </para>
///     <para>
///     A credential is stored only after an operation that used it succeeded, and is forgotten
///     (<see cref="Forget"/>) as soon as a feed rejects it, so a wrong or revoked credential is
///     never reused. Every stored secret is registered with <see cref="SecretRedactor"/> for as long
///     as it is stored, so an occurrence of it in any log text or exception message is removed.
///     Thread-safe: operations on different sources run concurrently.
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
    ///     Remembers a credential for a feed, replacing any earlier one.
    /// </summary>
    /// <param name="origin">The feed origin the credential was typed for.</param>
    /// <param name="credential">The credential to remember for the rest of the session.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is
    ///     <see langword="null"/>.</exception>
    public void Set(FeedOrigin origin, NuGetCredential credential)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(credential);

        // The secret is registered for redaction before it is visible to anything else, and the
        // replaced credential (if any) is withdrawn
        lock (_gate)
        {
            SecretRedactor.Register(credential);
            if (_credentials.TryGetValue(origin, out var replaced))
            {
                SecretRedactor.Unregister(replaced);
            }

            _credentials[origin] = credential;
        }
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

            SecretRedactor.Unregister(removed);
            return true;
        }
    }

    /// <summary>
    ///     Forgets every remembered credential, for when the application exits.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            foreach (var credential in _credentials.Values)
            {
                SecretRedactor.Unregister(credential);
            }

            _credentials.Clear();
        }
    }
}
