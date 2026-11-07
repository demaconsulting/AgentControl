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
///     What a credential prompt is told: which feed wants a sign-in, for which configured source,
///     and whether an earlier credential was just rejected.
/// </summary>
/// <param name="Origin">The feed origin the credential will be sent to. The prompt shows it
///     prominently so the user can see exactly which server receives what they type.</param>
/// <param name="SourceAlias">The alias of the configured source the sign-in is for.</param>
/// <param name="PreviousCredentialRejected"><see langword="true"/> when a credential was already
///     supplied for this feed during this operation and the feed rejected it, so the prompt can
///     say that the last sign-in did not work.</param>
internal sealed record CredentialPromptRequest(FeedOrigin Origin, string SourceAlias, bool PreviousCredentialRejected);

/// <summary>
///     Asks the user for a user name and password or token for a package feed, abstracting the
///     dialog away from the credential logic so that logic can be tested without a UI.
/// </summary>
/// <remarks>
///     Implementations decide how to show the question (the application shows a modal dialog on
///     the UI thread, marshalling there itself because callers run on worker threads) and must
///     honor the cancellation token by abandoning the question. They must show at most one
///     question at a time. The credential a prompt returns is held in memory only; implementations
///     must not log or persist it.
/// </remarks>
internal interface ICredentialPrompt
{
    /// <summary>
    ///     Asks the user for a credential.
    /// </summary>
    /// <param name="request">Which feed and source the credential is for.</param>
    /// <param name="cancellationToken">Cancels the question (the operation was cancelled or the
    ///     sign-in timed out); the implementation then withdraws any dialog it showed.</param>
    /// <returns>The credential the user entered, or <see langword="null"/> when the user declined
    ///     to sign in.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled before the user answered.</exception>
    Task<NuGetCredential?> PromptAsync(CredentialPromptRequest request, CancellationToken cancellationToken);
}
