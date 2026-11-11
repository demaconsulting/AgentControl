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

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.AgentControl.RepoSync;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     The outcome of comparing the content identity a repo's pin recorded with the identity of the
///     package content a source delivers now.
/// </summary>
internal enum ContentIdentityComparison
{
    /// <summary>
    ///     There is nothing to compare: the pin recorded no files identity (it predates identity
    ///     recording, or was made before identities were computed from the files) or the source
    ///     reported none. Never a reason to warn.
    /// </summary>
    NotComparable,

    /// <summary>
    ///     Both identities are known and equal: the same files the pin was made from.
    /// </summary>
    Same,

    /// <summary>
    ///     Both identities are known and differ: the files behind the same package version are not
    ///     what the pin was made from (for example the package was republished).
    /// </summary>
    Changed
}

/// <summary>
///     Computes, formats and compares the <em>content identity</em> of an agent package: a short
///     text that names exactly which agent files a package version delivered, so a repo's pin can
///     tell a republished package from the one it was made from - and recognize the same files
///     delivered by a different source.
/// </summary>
/// <remarks>
///     <para>
///     Package versions are meant to be immutable, but nothing in a zip folder or a NuGet feed
///     enforces that. An identity makes a silent change of a version detectable: a pin records the
///     identity when a package is applied, and later syncs of the same version are compared with it.
///     The identity has the form <c>files-sha256:{64 lower-case hex}</c> and is computed from the
///     <em>delivered agent files themselves</em> - exactly the files AgentControl writes into the
///     four managed folders - and not from the zip, <c>.nupkg</c> or Git repository they came in.
///     It is therefore the same for the same files whatever kind of source, container format,
///     compression, timestamps or entry order delivered them: a teammate whose source is a NuGet
///     feed, a zip folder or a Git tag gets the same identity as long as the files are identical.
///     </para>
///     <para>
///     <b>Canonical encoding.</b> The identity is the SHA-256 of one byte stream built as follows.
///     The files considered are those <see cref="PackageContentExtractor.GetManagedPackagePath"/>
///     accepts (the single rule that also decides what <see cref="PackageContentExtractor.Extract"/>
///     writes, so <c>release-notes.md</c>, <c>AGENTS.md</c> and anything outside the managed folders
///     are excluded). They are sorted by ordinal (UTF-16 code unit) comparison of their normalized,
///     <c>/</c>-separated, package-relative path. For each file in that order the stream receives
///     the UTF-8 bytes of the path, one <c>0x00</c> byte, the file's length in bytes as an 8-byte
///     little-endian unsigned integer, and then the file's raw bytes. The path and length framing
///     means that moving bytes between files, or renaming a file, changes the identity. Nothing else
///     (timestamps, permissions, directory entries, entry order) takes part.
///     </para>
///     <para>
///     Pure functions of their parameters (plus the content that is read) and thread-safe. The
///     identity is an integrity check against accidental or unannounced republishing - and, because
///     it describes the files and nothing else, it is all that is needed to notice a package whose
///     files were replaced, including a Git tag moved to a commit with other files; it is not a
///     signature and does not prove who published the package.
///     </para>
/// </remarks>
internal static class PackageContentIdentity
{
    /// <summary>
    ///     The prefix of an identity computed from a package's delivered agent files.
    /// </summary>
    internal const string FilesSha256Prefix = "files-sha256:";

    /// <summary>
    ///     How many characters of the value <see cref="Describe"/> shows, enough to tell two
    ///     identities apart in a message without printing a 64-character hash.
    /// </summary>
    private const int DescribedValueLength = 12;

    /// <summary>
    ///     The number of hexadecimal digits in a SHA-256 value.
    /// </summary>
    private const int Sha256HexLength = 64;

    /// <summary>
    ///     The size of the buffer a file is streamed through; the only memory a computation holds
    ///     however large the package is.
    /// </summary>
    private const int ReadBufferSize = 81920;

    /// <summary>
    ///     Computes the <c>files-sha256:</c> identity of the agent files a reader delivers.
    /// </summary>
    /// <param name="reader">The package content to identify. Read only; not disposed.</param>
    /// <returns>The identity, <c>files-sha256:</c> followed by 64 lower-case hex digits, encoded as
    ///     described in the class remarks. A package that delivers no managed file has the identity
    ///     of the empty stream.</returns>
    /// <remarks>
    ///     Computed centrally from <see cref="IPackageContentReader"/> so every kind of source -
    ///     zip, NuGet, Git - produces the identity the same way. Each file is streamed, so memory
    ///     use is bounded by one buffer however large the package is. The file's length has to be
    ///     hashed before its bytes: a seekable stream states it, and any other stream (an entry of
    ///     a compressed zip) is read once to count it and then opened again to hash it.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the package cannot be listed or
    ///     read, lists a path <see cref="PackageContentExtractor.Extract"/> would also reject, lists
    ///     two files with the same canonical path (the identity would depend on which one wins) or
    ///     with paths that differ only in letter case (they would be one file on a case-insensitive
    ///     file system), or a file changes length while it is being read.</exception>
    public static string ComputeFilesIdentity(IPackageContentReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // Select the files with the extraction's own rule, so the identity covers exactly what
        // would be written - then put them into the canonical order
        var files = new List<(string CanonicalPath, string ListedPath)>();
        foreach (var listedPath in reader.ListFiles())
        {
            var canonicalPath = PackageContentExtractor.GetManagedPackagePath(listedPath);
            if (canonicalPath is not null)
            {
                files.Add((canonicalPath, listedPath));
            }
        }

        files.Sort((left, right) => string.CompareOrdinal(left.CanonicalPath, right.CanonicalPath));

        // Duplicate or case-colliding paths are refused by the same rule the extractor applies
        PackageContentExtractor.RejectCaseCollisions(reader.Description, files.Select(file => file.CanonicalPath));

        // Frame every file as path, 0x00, length (8 bytes, little-endian), bytes; the framing keeps
        // two different sets of files from ever producing the same stream
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ReadBufferSize];
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        try
        {
            foreach (var (canonicalPath, listedPath) in files)
            {
                var length = MeasureLength(reader, listedPath, buffer);
                hash.AppendData(Encoding.UTF8.GetBytes(canonicalPath));
                hash.AppendData("\0"u8);
                BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
                hash.AppendData(lengthBytes);
                AppendContent(hash, reader, listedPath, length, buffer);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Package '{reader.Description}' could not be read to identify its content: {ex.Message}", ex);
        }

        return FilesSha256Prefix + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    ///     Computes the <c>files-sha256:</c> identity of a reader's files, or reports that none can
    ///     be named.
    /// </summary>
    /// <param name="reader">The package content to identify.</param>
    /// <returns>The identity (see <see cref="ComputeFilesIdentity"/>), or <see langword="null"/>
    ///     when the content cannot be identified (it cannot be read, or holds a path that extraction
    ///     rejects). That is "nothing to compare", never "changed": extracting such a package fails
    ///     with the specific reason, which is how the user learns about it.</returns>
    /// <remarks>
    ///     What <see cref="IPackageContentReader.ContentIdentity"/> implementations use, so a reader
    ///     that is opened only to read release notes never has to read the whole package, and a
    ///     reader over an unacceptable package still opens (the extractor reports the problem).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is
    ///     <see langword="null"/>.</exception>
    public static string? TryComputeFilesIdentity(IPackageContentReader reader)
    {
        try
        {
            return ComputeFilesIdentity(reader);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Determines the length of one package file in bytes.
    /// </summary>
    /// <param name="reader">The package content.</param>
    /// <param name="listedPath">The path as listed by the reader.</param>
    /// <param name="buffer">A scratch buffer for a stream that does not state its length.</param>
    /// <returns>The file's length.</returns>
    private static long MeasureLength(IPackageContentReader reader, string listedPath, byte[] buffer)
    {
        using var stream = reader.OpenRead(listedPath);
        if (stream.CanSeek)
        {
            return stream.Length;
        }

        // A compressed entry cannot state its length: count it by reading it through once
        long length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            length += read;
        }

        return length;
    }

    /// <summary>
    ///     Feeds the bytes of one package file into the running hash.
    /// </summary>
    /// <param name="hash">The running hash.</param>
    /// <param name="reader">The package content.</param>
    /// <param name="listedPath">The path as listed by the reader.</param>
    /// <param name="expectedLength">The length that was hashed ahead of the bytes.</param>
    /// <param name="buffer">The buffer the file is streamed through.</param>
    /// <exception cref="InvalidOperationException">Thrown when the file does not hold exactly
    ///     <paramref name="expectedLength"/> bytes (it changed while it was being read).</exception>
    private static void AppendContent(
        IncrementalHash hash, IPackageContentReader reader, string listedPath, long expectedLength, byte[] buffer)
    {
        using var stream = reader.OpenRead(listedPath);
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            total += read;
        }

        if (total != expectedLength)
        {
            throw new InvalidOperationException(
                $"'{listedPath}' in package '{reader.Description}' changed while its content was being identified.");
        }
    }

    /// <summary>
    ///     Determines whether a text is a well-formed files identity.
    /// </summary>
    /// <param name="identity">The text to test; <see langword="null"/> or blank is not an
    ///     identity.</param>
    /// <returns><see langword="true"/> when the text (ignoring surrounding whitespace and letter
    ///     case) is <c>files-sha256:</c> followed by exactly 64 hexadecimal digits; otherwise
    ///     <see langword="false"/> - including for an identity recorded by an older version
    ///     (<c>sha256:</c>, <c>git-commit:</c>), which cannot be compared with one computed from
    ///     the files.</returns>
    public static bool IsFilesIdentity(string? identity)
    {
        var trimmed = identity?.Trim();
        return trimmed is not null &&
               trimmed.StartsWith(FilesSha256Prefix, StringComparison.OrdinalIgnoreCase) &&
               trimmed.Length == FilesSha256Prefix.Length + Sha256HexLength &&
               trimmed[FilesSha256Prefix.Length..].All(Uri.IsHexDigit);
    }

    /// <summary>
    ///     Compares the identity a pin recorded with the identity of the content now delivered.
    /// </summary>
    /// <param name="pinned">The identity recorded in the pin, or <see langword="null"/>/empty for
    ///     a pin that recorded none.</param>
    /// <param name="actual">The identity of the content a source delivered, or
    ///     <see langword="null"/>/empty when the source reports none.</param>
    /// <returns><see cref="ContentIdentityComparison.NotComparable"/> when either side is not a
    ///     files identity (missing, or of an older kind such as <c>sha256:</c> that was computed
    ///     from a container and cannot be compared with one computed from the files); otherwise
    ///     <see cref="ContentIdentityComparison.Same"/> when they are equal (hex digits compared
    ///     without regard to case, so a hand-edited pin is not a false alarm) or
    ///     <see cref="ContentIdentityComparison.Changed"/> when they differ.</returns>
    public static ContentIdentityComparison Compare(string? pinned, string? actual)
    {
        // Only two files identities can be told apart: a pin made before identities existed (or
        // before they were computed from the files) adopts one silently, and a source that reports
        // none cannot be checked
        if (!IsFilesIdentity(pinned) || !IsFilesIdentity(actual))
        {
            return ContentIdentityComparison.NotComparable;
        }

        return string.Equals(pinned!.Trim(), actual!.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ContentIdentityComparison.Same
            : ContentIdentityComparison.Changed;
    }

    /// <summary>
    ///     Produces a short form of an identity for messages and logs.
    /// </summary>
    /// <param name="identity">An identity as produced by this class; <see langword="null"/> or
    ///     empty when none is known.</param>
    /// <returns>The identity with its value cut to 12 characters (so two different identities can
    ///     still be told apart), or <c>none</c> when no identity is known. The text is split at its
    ///     first colon: what follows the first colon is the value that is cut, so an identity of an
    ///     unrecognized shape is shortened the same way and never echoed in full.</returns>
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
