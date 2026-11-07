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

namespace DemaConsulting.AgentControl.PackageCredentials;

/// <summary>
///     A place outside AgentControl where the user's sign-in for a package feed can be kept on
///     request and found again later, abstracting that place away from the credential logic so the
///     logic can be tested without one.
/// </summary>
/// <remarks>
///     <para>
///     The application's only implementation hands the work to the user's own Git credential
///     helper (Git Credential Manager, the macOS keychain, libsecret and so on), so AgentControl
///     itself never stores a secret: it asks the helper to keep one only when the user ticked
///     "Remember on this computer", and asks the helper for it again when it is needed. Every
///     method works without any user interface: an implementation must never show a dialog or
///     wait for input, and must give up after a bounded time rather than hang the operation that
///     asked. The credentials passed in and returned are held in memory only; implementations must
///     not log or persist them anywhere but in the place they stand for.
///     </para>
///     <para>
///     Implementations never throw for an unavailable or failing store: lookups find nothing and
///     requests to save or remove report <see langword="false"/>. Only a cancelled token surfaces
///     as <see cref="OperationCanceledException"/>.
///     </para>
/// </remarks>
internal interface IRememberedCredentialStore
{
    /// <summary>
    ///     Determines whether a sign-in for a feed can be kept and found again: whether the user
    ///     has a Git credential helper configured that applies to the feed's origin.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when a credential helper applies to the origin.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled.</exception>
    Task<bool> IsAvailableAsync(FeedOrigin origin, CancellationToken cancellationToken);

    /// <summary>
    ///     Looks up the sign-in kept for a feed, without ever asking the user.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The kept credential, or <see langword="null"/> when none is kept, the store is
    ///     unavailable or the lookup took too long.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled.</exception>
    Task<NuGetCredential?> FillAsync(FeedOrigin origin, CancellationToken cancellationToken);

    /// <summary>
    ///     Keeps a credential that the feed accepted, so it can be found again.
    /// </summary>
    /// <param name="origin">The feed origin the credential is for.</param>
    /// <param name="credential">The credential to keep.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> when the store accepted the credential.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled.</exception>
    Task<bool> ApproveAsync(FeedOrigin origin, NuGetCredential credential, CancellationToken cancellationToken);

    /// <summary>
    ///     Removes a credential that the feed rejected (or that the user asked to forget), so it
    ///     is not offered again.
    /// </summary>
    /// <param name="origin">The feed origin the credential is for.</param>
    /// <param name="credential">The credential to remove, or <see langword="null"/> to remove
    ///     whatever is kept for the origin.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><see langword="true"/> when the store accepted the request.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled.</exception>
    Task<bool> RejectAsync(FeedOrigin origin, NuGetCredential? credential, CancellationToken cancellationToken);
}
