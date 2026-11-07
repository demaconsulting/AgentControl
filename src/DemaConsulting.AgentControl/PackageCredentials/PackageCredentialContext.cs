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

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     The credential facilities the application shares across every package operation: the
///     session store and (when a user is available to ask) the prompt.
/// </summary>
/// <remarks>
///     Bundled into one small value so it can be handed from the application root down to the
///     places that create source providers without widening every constructor. A context with a
///     <see langword="null"/> <see cref="Prompt"/> is "silent": it may use remembered credentials
///     but never asks the user, which is what background work (such as the upgrade check at
///     start-up) must use so that no dialog appears unprompted. Immutable and thread-safe.
/// </remarks>
/// <param name="Store">The session credential store shared by the whole application.</param>
/// <param name="Prompt">Asks the user for a credential, or <see langword="null"/> when no user
///     is available to ask.</param>
/// <param name="Remembered">The user's own credential store (their Git credential helper), where a
///     sign-in can be kept beyond the session when the user asks, or <see langword="null"/> when
///     there is none.</param>
internal sealed record PackageCredentialContext(
    SessionCredentialStore Store, ICredentialPrompt? Prompt, IRememberedCredentialStore? Remembered = null)
{
    /// <summary>
    ///     Gets a context that shares this context's store but never asks the user, for work that
    ///     must not interrupt the user with a dialog.
    /// </summary>
    /// <returns>A silent context over the same store (it may still read a saved sign-in, which
    ///     needs no interaction).</returns>
    public PackageCredentialContext WithoutPrompt() => this with { Prompt = null };

    /// <summary>
    ///     Forgets the sign-in for a feed everywhere AgentControl can: the session store and the
    ///     user's own credential store.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What was done: nothing was saved, the saved sign-in was removed, or the user's
    ///     credential store could not remove it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origin"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<ForgetSignInResult> ForgetSavedSignInAsync(FeedOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        // This session's copy first, so the credential is not offered again whatever happens next
        var result = Store.Forget(origin) ? ForgetSignInResult.Removed : ForgetSignInResult.NothingSaved;
        if (Remembered is null)
        {
            return result;
        }

        // The user's credential store removes a credential it can identify; look up what it holds
        // for this origin (while the secret is registered for redaction) and remove exactly that
        var saved = await Remembered.FillAsync(origin, cancellationToken);
        if (saved is null)
        {
            return result;
        }

        using var lease = SecretRedactor.Lease(saved);
        return await Remembered.RejectAsync(origin, saved, cancellationToken)
            ? ForgetSignInResult.Removed
            : ForgetSignInResult.Failed;
    }
}

/// <summary>
///     The outcome of asking AgentControl to forget the sign-in for a feed.
/// </summary>
internal enum ForgetSignInResult
{
    /// <summary>
    ///     There was no sign-in for the feed to forget.
    /// </summary>
    NothingSaved,

    /// <summary>
    ///     The sign-in was forgotten.
    /// </summary>
    Removed,

    /// <summary>
    ///     A saved sign-in exists but the user's credential store did not remove it.
    /// </summary>
    Failed
}
