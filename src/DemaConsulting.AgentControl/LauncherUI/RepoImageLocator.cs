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
using Avalonia.Media.Imaging;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Identifies one validated candidate image file in a repo's root, and doubles as the cache key
///     that tells whether the file changed: the same path, last-write time and size means the same
///     picture.
/// </summary>
/// <param name="Path">The full path of the file.</param>
/// <param name="LastWriteTimeUtc">The file's last-write time (UTC).</param>
/// <param name="Length">The file's size in bytes.</param>
internal readonly record struct RepoImageFile(string Path, DateTime LastWriteTimeUtc, long Length);

/// <summary>
///     Finds and decodes the picture a repo offers for its card: <c>icon.png</c>, else
///     <c>logo.png</c>, in the repo's root folder.
/// </summary>
/// <remarks>
///     <para>
///     The repo may be untrusted, so everything about the file is checked before it is decoded:
///     only the repo root is searched (never the folders below it), the name is matched without regard to
///     case, links (symbolic links, junctions and other reparse points) are refused so the file
///     can never be a stand-in for something outside the repo, the size is capped, the file must
///     start with the PNG signature (the extension alone proves nothing), and the declared
///     dimensions must be sane so a tiny file cannot expand into a huge bitmap. The decode is
///     scaled down to the tile size, and every failure - I/O or decode - means "no image" so the
///     card falls back to its generated tile. File contents are never logged.
///     </para>
///     <para>
///     Stateless and thread-safe. <see cref="Find"/> and <see cref="Decode"/> do blocking file
///     I/O and CPU work and are meant to run off the UI thread; the bitmap they return is then
///     handed to the UI.
///     </para>
/// </remarks>
internal static class RepoImageLocator
{
    /// <summary>
    ///     The largest image file that is considered, in bytes (1 MB). A card icon has no need
    ///     for more, and the cap bounds the memory a hostile file can make the application use.
    /// </summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>
    ///     The largest width or height, in pixels, an image may declare.
    /// </summary>
    public const int MaxDimension = 8192;

    /// <summary>
    ///     The largest number of pixels an image may declare (4096 by 4096), which bounds the
    ///     memory the decode can need even for an image within <see cref="MaxDimension"/> on each
    ///     side.
    /// </summary>
    public const long MaxPixels = 4096L * 4096L;

    /// <summary>
    ///     The width, in pixels, the image is decoded at: twice the 44 pixel tile, so it stays sharp
    ///     on a display scaled to 200 percent.
    /// </summary>
    public const int TileDecodeWidth = 96;

    /// <summary>
    ///     The length of the PNG signature, the PNG header chunk's length/type and its width and
    ///     height: all that is read to validate a file before it is decoded.
    /// </summary>
    private const int HeaderLength = 24;

    /// <summary>
    ///     The file names looked for, in order of preference.
    /// </summary>
    private static readonly string[] CandidateNames = ["icon.png", "logo.png"];

    /// <summary>
    ///     The eight bytes every PNG file starts with.
    /// </summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    ///     Looks in a repo's root folder for the picture to show on its card.
    /// </summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <returns>The first acceptable file among <c>icon.png</c> and <c>logo.png</c> (an
    ///     <c>icon.png</c> that is not acceptable does not stop <c>logo.png</c> being used), or
    ///     <see langword="null"/> when the folder does not exist, cannot be read or has no
    ///     acceptable file.</returns>
    public static RepoImageFile? Find(string repoRoot)
    {
        try
        {
            var root = new DirectoryInfo(repoRoot);
            if (!root.Exists)
            {
                return null;
            }

            // Root only: files in the folders below it are never considered
            var files = root.EnumerateFiles().ToList();
            foreach (var name in CandidateNames)
            {
                foreach (var file in files.Where(file => string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (IsAcceptable(file, root))
                    {
                        return new RepoImageFile(file.FullName, file.LastWriteTimeUtc, file.Length);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // An unreadable folder simply has no image
        }

        return null;
    }

    /// <summary>
    ///     Reads and decodes a file found by <see cref="Find"/>, scaled down to the tile size.
    /// </summary>
    /// <remarks>
    ///     The file is validated again as it is read, because it may have changed since
    ///     <see cref="Find"/> looked at it. Needs Avalonia's platform to be initialized (the
    ///     decoder is part of it).
    /// </remarks>
    /// <param name="file">The file to decode.</param>
    /// <param name="decodeWidth">The width, in pixels, to decode at; the height follows the aspect
    ///     ratio, and a smaller image is not enlarged beyond its own size by the decoder's choice.</param>
    /// <returns>The bitmap, or <see langword="null"/> when the file is no longer acceptable or
    ///     cannot be decoded.</returns>
    public static Bitmap? Decode(RepoImageFile file, int decodeWidth = TileDecodeWidth)
    {
        try
        {
            var bytes = ReadValidated(file.Path);
            if (bytes is null)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes, writable: false);
            return Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any I/O or decoder failure on an untrusted file means "no image"; the decoder
            // reports a corrupt file with several exception types, so none is singled out
            return null;
        }
    }

    /// <summary>
    ///     Tells whether a file may be used as the card's picture.
    /// </summary>
    /// <param name="file">The candidate.</param>
    /// <param name="root">The repo root it was found in.</param>
    /// <returns><see langword="true"/> for a regular file directly inside the root, within the size
    ///     cap, that starts with a PNG signature and declares sane dimensions.</returns>
    private static bool IsAcceptable(FileInfo file, DirectoryInfo root)
    {
        // A link could point anywhere (a secret file, a network share): refuse it outright
        if (IsLink(file) || file.Length is 0 or > MaxFileBytes)
        {
            return false;
        }

        // The file must really live in the root, not merely be reported there
        var directory = Path.GetFullPath(file.DirectoryName ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPath = Path.GetFullPath(root.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(directory, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[HeaderLength];
            return stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) == HeaderLength && HasPngHeader(header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Reads a whole file that passes the same checks as <see cref="IsAcceptable"/>.
    /// </summary>
    /// <param name="path">The file's full path.</param>
    /// <returns>The file's bytes, or <see langword="null"/> when it is not acceptable (anymore).</returns>
    private static byte[]? ReadValidated(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || IsLink(info))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is 0 or > MaxFileBytes)
        {
            return null;
        }

        // The length was checked against the cap just above, so this never reads more than the cap;
        // a file that shrank meanwhile fails the length comparison below
        var buffer = new byte[stream.Length];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return read == buffer.Length && read >= HeaderLength && HasPngHeader(buffer.AsSpan(0, HeaderLength))
            ? buffer
            : null;
    }

    /// <summary>
    ///     Tells whether the first bytes of a file are a PNG signature followed by a header chunk
    ///     that declares dimensions within the limits.
    /// </summary>
    /// <param name="header">The first <see cref="HeaderLength"/> bytes of the file.</param>
    /// <returns><see langword="true"/> for a plausible PNG header.</returns>
    private static bool HasPngHeader(ReadOnlySpan<byte> header)
    {
        if (!header[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return false;
        }

        // cspell:disable-next-line
        // The header chunk follows the signature: 4 bytes of length, "IHDR", then width and height
        // cspell:disable-next-line
        if (!header.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20, 4));
        return width is > 0 and <= MaxDimension && height is > 0 and <= MaxDimension && (long)width * height <= MaxPixels;
    }

    /// <summary>
    ///     Tells whether a file is a link of any kind: a symbolic link, or any other reparse point
    ///     such as a junction or a mount point.
    /// </summary>
    /// <param name="file">The file to check.</param>
    /// <returns><see langword="true"/> for a link.</returns>
    private static bool IsLink(FileSystemInfo file) =>
        file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
