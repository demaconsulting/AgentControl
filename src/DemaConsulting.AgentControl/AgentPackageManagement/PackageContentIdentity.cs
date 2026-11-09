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

using System.Security.Cryptography;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     The outcome of comparing the content identity a repo's pin recorded with the identity of the
///     package content a source delivers now.
/// </summary>
internal enum ContentIdentityComparison
{
    /// <summary>
    ///     There is nothing to compare: the pin recorded no identity (it predates identity
    ///     recording) or the source reported none. Never a reason to warn.
    /// </summary>
    NotComparable,

    /// <summary>
    ///     Both identities are known and equal: the same bytes the pin was made from.
    /// </summary>
    Same,

    /// <summary>
    ///     Both identities are known and differ: the content behind the same package version is not
    ///     what the pin was made from (for example the package was republished).
    /// </summary>
    Changed
}

/// <summary>
///     Computes, formats and compares the <em>content identity</em> of an agent package: a short
///     text that names exactly which bytes a package version delivered, so a repo's pin can tell a
///     republished package from the one it was made from.
/// </summary>
/// <remarks>
///     <para>
///     Package versions are meant to be immutable, but nothing in a zip folder or a NuGet feed
///     enforces that. An identity makes a silent change of a version detectable: a pin records the
///     identity when a package is applied, and later syncs of the same version are compared with it.
///     The text is <c>{algorithm}:{value}</c>. <c>sha256:{64 lower-case hex}</c> is the SHA-256 of
///     the package file (the zip, or the <c>.nupkg</c> a NuGet source unpacked). The
///     <c>git-commit:{hex}</c> form is the commit a Git tag pointed to; <see cref="GitSourceProvider"/>
///     delivers it through <see cref="FromGitCommit"/> for a package it fetched, so a Git source
///     takes part in the same comparison as the others.
///     </para>
///     <para>
///     Pure functions of their parameters (plus the stream that is read) and thread-safe. The
///     identity is an integrity check against accidental or unannounced republishing; it is not a
///     signature and does not prove who published the package.
///     </para>
/// </remarks>
internal static class PackageContentIdentity
{
    /// <summary>
    ///     The prefix of an identity that is the SHA-256 of a package file.
    /// </summary>
    internal const string Sha256Prefix = "sha256:";

    /// <summary>
    ///     The prefix of an identity that is the commit a Git tag pointed to (produced by the Git
    ///     package source).
    /// </summary>
    internal const string GitCommitPrefix = "git-commit:";

    /// <summary>
    ///     How many characters of the value <see cref="Describe"/> shows, enough to tell two
    ///     identities apart in a message without printing a 64-character hash.
    /// </summary>
    private const int DescribedValueLength = 12;

    /// <summary>
    ///     Computes the <c>sha256:</c> identity of the bytes a stream delivers from its current
    ///     position to its end.
    /// </summary>
    /// <param name="content">The readable stream to hash. Left open; its position is at the end
    ///     afterwards.</param>
    /// <returns>The identity, <c>sha256:</c> followed by 64 lower-case hex digits.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when the stream cannot be read.</exception>
    public static string ComputeSha256(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // The caller keeps the stream: the zip reader hashes and then reads the very same handle,
        // so the bytes that were hashed are the bytes that are later extracted
        return Sha256Prefix + Convert.ToHexStringLower(SHA256.HashData(content));
    }

    /// <summary>
    ///     Formats a commit hash as a <c>git-commit:</c> identity.
    /// </summary>
    /// <param name="commit">The commit hash: 40 (SHA-1) or 64 (SHA-256 object format) hex digits,
    ///     in either case.</param>
    /// <returns>The identity with the hash lower-cased.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="commit"/> is not 40 or 64
    ///     hex digits (an unchecked value would let a malformed identity into a pin).</exception>
    public static string FromGitCommit(string commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if ((commit.Length != 40 && commit.Length != 64) || !commit.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A commit hash must be 40 or 64 hexadecimal digits.", nameof(commit));
        }

        return GitCommitPrefix + commit.ToLowerInvariant();
    }

    /// <summary>
    ///     Compares the identity a pin recorded with the identity of the content now delivered.
    /// </summary>
    /// <param name="pinned">The identity recorded in the pin, or <see langword="null"/>/empty for
    ///     a pin that recorded none.</param>
    /// <param name="actual">The identity of the content a source delivered, or
    ///     <see langword="null"/>/empty when the source reports none.</param>
    /// <returns><see cref="ContentIdentityComparison.NotComparable"/> when either side is missing;
    ///     otherwise <see cref="ContentIdentityComparison.Same"/> when they are equal (hex digits
    ///     compared without regard to case, so a hand-edited pin is not a false alarm) or
    ///     <see cref="ContentIdentityComparison.Changed"/> when they differ - including when they
    ///     are of different kinds, which means the same version is now delivered by a different
    ///     kind of source.</returns>
    public static ContentIdentityComparison Compare(string? pinned, string? actual)
    {
        // A missing side is never a mismatch: a pin made before identities existed adopts one
        // silently, and a source that reports none cannot be checked
        if (string.IsNullOrWhiteSpace(pinned) || string.IsNullOrWhiteSpace(actual))
        {
            return ContentIdentityComparison.NotComparable;
        }

        return string.Equals(pinned.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ContentIdentityComparison.Same
            : ContentIdentityComparison.Changed;
    }

    /// <summary>
    ///     Produces a short form of an identity for messages and logs.
    /// </summary>
    /// <param name="identity">An identity as produced by this class; <see langword="null"/> or
    ///     empty when none is known.</param>
    /// <returns>The identity with its value cut to 12 characters (so two different identities can
    ///     still be told apart), or <c>none</c> when no identity is known. An identity of an
    ///     unrecognized shape is cut the same way after its last colon, never echoed in full.</returns>
    public static string Describe(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return "none";
        }

        var trimmed = identity.Trim();
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var prefix = colon >= 0 ? trimmed[..(colon + 1)] : string.Empty;
        var value = colon >= 0 ? trimmed[(colon + 1)..] : trimmed;
        return prefix + (value.Length <= DescribedValueLength ? value : value[..DescribedValueLength]);
    }
}
