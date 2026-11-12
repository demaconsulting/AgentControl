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

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     Why a package source could not be used, in the terms a user can act on.
/// </summary>
internal enum PackageSourceFailureKind
{
    /// <summary>
    ///     The server needs credentials, or rejected the ones supplied (HTTP 401). A sign-in can
    ///     help.
    /// </summary>
    AuthenticationFailed,

    /// <summary>
    ///     The server knows who the user is but refuses access (HTTP 403): the account lacks the
    ///     permission to read the feed. Signing in again cannot help, so the user is never asked
    ///     for a credential and no remembered sign-in is forgotten; the feed's administrator has
    ///     to grant the permission.
    /// </summary>
    AccessDenied,

    /// <summary>
    ///     The server needs credentials and the user declined to provide them.
    /// </summary>
    SignInCanceled,

    /// <summary>
    ///     The server answered but the package or version is not there.
    /// </summary>
    NotFound,

    /// <summary>
    ///     The server could not be reached or did not answer as a NuGet feed.
    /// </summary>
    Unreachable,

    /// <summary>
    ///     Signing in took longer than the allowed time and was stopped.
    /// </summary>
    TimedOut,

    /// <summary>
    ///     A package was found but its signature does not satisfy the source's requirement: it is
    ///     unsigned, invalidly signed, or not signed by the trusted publisher the source is
    ///     pinned to - or the source requires signatures and the platform cannot verify them, so
    ///     the package is refused unseen.
    /// </summary>
    SignatureRejected
}

/// <summary>
///     A failure to use a package source whose cause is known well enough to tell the user what to
///     do, with a message that is safe to show and to log.
/// </summary>
/// <remarks>
///     Derives from <see cref="InvalidOperationException"/> so every caller that already reports
///     source failures by catching that type keeps working, while code that wants to react to the
///     cause (the connection test, the retry action of the Select Package window) reads
///     <see cref="Kind"/>. The message never contains a credential or a URL beyond the feed's
///     scheme, host and port, because it ends up in dialogs and logs.
/// </remarks>
internal sealed class PackageSourceAccessException : InvalidOperationException
{
    /// <summary>
    ///     Initializes the exception.
    /// </summary>
    /// <param name="kind">The cause.</param>
    /// <param name="message">A user-readable, secret-free message.</param>
    /// <param name="innerException">The underlying failure, or <see langword="null"/>. Only a
    ///     secret-free description of it (its redacted type name, message chain and stack trace, as
    ///     a <see cref="SanitizedException"/>) is kept: the original object could carry headers,
    ///     URLs or credentials. Its text is never part of <paramref name="message"/>.</param>
    public PackageSourceAccessException(PackageSourceFailureKind kind, string message, Exception? innerException = null)
        : base(SecretRedactor.Redact(message), innerException is null ? null : SecretRedactor.Sanitize(innerException))
    {
        Kind = kind;
    }

    /// <summary>
    ///     Gets why the source could not be used.
    /// </summary>
    public PackageSourceFailureKind Kind { get; }
}
