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
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Avalonia.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Identifies one validated candidate image file in a repo's root, and doubles as the cache key
///     that tells whether the file changed: the same root, path, last-write time and size means the
///     same picture.
/// </summary>
/// <param name="Root">The repo's root folder the file was found in; carried along so that the file
///     is checked against it again when it is read.</param>
/// <param name="Path">The full path of the file.</param>
/// <param name="LastWriteTimeUtc">The file's last-write time (UTC).</param>
/// <param name="Length">The file's size in bytes.</param>
internal readonly record struct RepoImageFile(string Root, string Path, DateTime LastWriteTimeUtc, long Length);

/// <summary>
///     Finds and decodes the picture a repo offers for its card: <c>icon.png</c>, else
///     <c>logo.png</c>, in the repo's root folder.
/// </summary>
/// <remarks>
///     <para>
///     The repo may be untrusted, so everything about the file is checked before it is decoded:
///     the repo root itself must not be a link (a linked root, for this feature only, simply has no
///     picture), only the repo root is searched (never the folders below it), the name is matched
///     without regard to case, links (symbolic links, junctions and other reparse points) are
///     refused so the file can never be a stand-in for something outside the repo, the size is
///     capped, the file must start with the PNG signature (the extension alone proves nothing), and
///     the declared dimensions must be sane so a tiny file cannot expand into a huge bitmap - this
///     bound is what keeps a hostile file from exhausting memory in the decoder. The file is then
///     opened and, once it is open, the operating system's own answer to "which file is this
///     handle?" must be a file directly in the root, so a file or folder swapped for a link between
///     the checks and the open cannot redirect the read. <see cref="Decode"/> repeats every check
///     against the root <see cref="Find"/> recorded, because the file may have changed since. The
///     decode is scaled down to the tile size, and every failure - I/O or decode, of any exception
///     type - means "no image" so the card falls back to its generated tile and the background
///     refresh is never faulted. File contents are never logged.
///     </para>
///     <para>
///     Stateless and thread-safe. <see cref="Find"/> and <see cref="Decode"/> do blocking file
///     I/O and CPU work and are meant to run off the UI thread; the bitmap they return is then
///     handed to the UI.
///     </para>
/// </remarks>
internal static partial class RepoImageLocator
{
    /// <summary>
    ///     The largest image file that is considered, in bytes (1 MB). A card icon has no need
    ///     for more, and the cap bounds the memory a hostile file can make the application use.
    /// </summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>
    ///     The largest width or height, in pixels, an image may declare (so at most 4096 by 4096
    ///     pixels, 64 MB as a 32-bit bitmap): the bound on the memory the decoder can need.
    /// </summary>
    public const int MaxDimension = 4096;

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

    /// <summary>Win32 <c>FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE</c>.</summary>
    private const uint FileShareAll = 7;

    /// <summary>Win32 <c>OPEN_EXISTING</c>.</summary>
    private const uint OpenExisting = 3;

    /// <summary>Win32 <c>FILE_FLAG_BACKUP_SEMANTICS</c>, needed to open a folder.</summary>
    private const uint FileFlagBackupSemantics = 0x02000000;

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
            // A repo root that is itself a link (or does not exist) has no picture
            var root = OpenRoot(repoRoot);
            if (root is null)
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
                        return new RepoImageFile(root.FullName, file.FullName, file.LastWriteTimeUtc, file.Length);
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
    ///     The root and the file are validated again as the file is read, against the root
    ///     <see cref="Find"/> recorded in <paramref name="file"/>, because either may have changed
    ///     (been replaced by a link, or by another file) since <see cref="Find"/> looked at them.
    ///     Resource exhaustion is prevented rather than handled: the declared dimensions are bounded
    ///     before the decoder sees the bytes. Any exception that still comes out of reading or
    ///     decoding an untrusted file means "no image". Needs Avalonia's platform to be initialized
    ///     (the decoder is part of it).
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
            var bytes = ReadValidated(file);
            if (bytes is null)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes, writable: false);
            return Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception)
        {
            // Any I/O or decoder failure on an untrusted file means "no image"; the decoder
            // reports a corrupt file with several exception types, so none is singled out, and
            // nothing from here may fault the card's background refresh. (The memory the decode
            // can need is bounded by the dimension check above; this is the last resort, not the
            // protection against a hostile size.)
            return null;
        }
    }

    /// <summary>
    ///     Opens a repo's root folder for searching, refusing a root that is not usable.
    /// </summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <returns>The folder, or <see langword="null"/> when it does not exist or is itself a link
    ///     (a symbolic link, a junction or another reparse point).</returns>
    private static DirectoryInfo? OpenRoot(string repoRoot)
    {
        var root = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot)));
        return root.Exists && !IsLink(root) ? root : null;
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

        try
        {
            using var stream = OpenVerified(root.FullName, file.FullName);
            if (stream is null)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[HeaderLength];
            return stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) == HeaderLength && HasPngHeader(header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Reads a whole file that passes the same checks as <see cref="IsAcceptable"/>, checked
    ///     against the root the file was found in.
    /// </summary>
    /// <param name="file">The file found by <see cref="Find"/>.</param>
    /// <returns>The file's bytes, or <see langword="null"/> when it is not acceptable (anymore).</returns>
    private static byte[]? ReadValidated(RepoImageFile file)
    {
        // The root may have been replaced by a link since it was found
        if (OpenRoot(file.Root) is not { } root)
        {
            return null;
        }

        using var stream = OpenVerified(root.FullName, file.Path);
        if (stream is null || stream.Length is 0 or > MaxFileBytes)
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
    ///     Opens a candidate file for reading only if, once it is open, it really is a regular file
    ///     directly inside the repo root.
    /// </summary>
    /// <remarks>
    ///     The name and folder are checked before the open (cheap refusals), and the opened handle
    ///     is checked after it: the operating system reports the final path of the file the handle
    ///     is for, which has every link on the way resolved, and that must be a file of the same
    ///     name directly in the root's own resolved folder. Checking the handle rather than the
    ///     path closes the gap in which a path is checked and then swapped for a link before it is
    ///     opened.
    /// </remarks>
    /// <param name="rootPath">The full path of the repo root, already known not to be a link.</param>
    /// <param name="filePath">The full path of the candidate file.</param>
    /// <returns>The open stream (the caller disposes it), or <see langword="null"/> when the file
    ///     is not acceptable.</returns>
    private static FileStream? OpenVerified(string rootPath, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fileName = Path.GetFileName(fullPath);

        // The file must be one of the candidates and really live in the root, not merely be reported there
        if (!CandidateNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || !PathsEqual(Path.GetDirectoryName(fullPath), rootPath))
        {
            return null;
        }

        var info = new FileInfo(fullPath);
        if (!info.Exists || IsLink(info))
        {
            return null;
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (IsDirectlyInRoot(stream, rootPath, fileName))
            {
                return stream;
            }
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        stream.Dispose();
        return null;
    }

    /// <summary>
    ///     Tells whether the file an open stream is for is a file of the given name directly inside
    ///     the root, by the operating system's resolved paths of both.
    /// </summary>
    /// <param name="stream">The open file.</param>
    /// <param name="rootPath">The repo root.</param>
    /// <param name="fileName">The expected file name.</param>
    /// <returns><see langword="true"/> when the opened file is the named file in the root.</returns>
    private static bool IsDirectlyInRoot(FileStream stream, string rootPath, string fileName)
    {
        var finalFile = GetFinalPath(stream);
        var finalRoot = GetFinalDirectoryPath(rootPath);
        return finalFile is not null
            && finalRoot is not null
            && PathsEqual(Path.GetDirectoryName(finalFile), finalRoot)
            && string.Equals(Path.GetFileName(finalFile), fileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Compares two full paths the way the platform's file system does.
    /// </summary>
    /// <param name="left">One path (or <see langword="null"/>).</param>
    /// <param name="right">The other path (or <see langword="null"/>).</param>
    /// <returns><see langword="true"/> when both are the same path.</returns>
    private static bool PathsEqual(string? left, string? right) =>
        left is not null
        && right is not null
        && string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
        return width is > 0 and <= MaxDimension && height is > 0 and <= MaxDimension;
    }

    /// <summary>
    ///     Tells whether a file or folder is a link of any kind: a symbolic link, or any other
    ///     reparse point such as a junction or a mount point.
    /// </summary>
    /// <param name="entry">The file or folder to check.</param>
    /// <returns><see langword="true"/> for a link.</returns>
    private static bool IsLink(FileSystemInfo entry) =>
        entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// <summary>
    ///     Gets the final path - every link resolved - of the file an open stream is for.
    /// </summary>
    /// <param name="stream">The open file.</param>
    /// <returns>The final full path, or <see langword="null"/> when it cannot be determined.</returns>
    private static string? GetFinalPath(FileStream stream)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetFinalPathOfHandle(stream.SafeFileHandle);
        }

        // Linux names the file behind a descriptor in /proc; elsewhere the best that can be done is
        // to resolve the path the stream was opened by. The descriptor number is only read (the
        // stream, which owns the handle, outlives this call), never used to close or duplicate it
#pragma warning disable S3869 // Reading the descriptor number is the only way to name it in /proc
        var viaProc = new FileInfo($"/proc/self/fd/{stream.SafeFileHandle.DangerousGetHandle()}").LinkTarget;
#pragma warning restore S3869
        return viaProc is not null && Path.IsPathRooted(viaProc) ? viaProc : ResolveLinks(stream.Name);
    }

    /// <summary>
    ///     Gets the final path - every link resolved - of a folder.
    /// </summary>
    /// <param name="directoryPath">The folder's full path.</param>
    /// <returns>The final full path, or <see langword="null"/> when it cannot be determined.</returns>
    private static string? GetFinalDirectoryPath(string directoryPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ResolveLinks(directoryPath);
        }

        // A directory can only be opened by asking for backup semantics; no access is needed to
        // ask where it is
        using var handle = CreateFile(directoryPath, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        return handle.IsInvalid ? null : GetFinalPathOfHandle(handle);
    }

    /// <summary>
    ///     Resolves every link along a path by walking it from the root, one component at a time
    ///     (the platform-neutral way of finding a path's real location).
    /// </summary>
    /// <param name="fullPath">The full path.</param>
    /// <returns>The path with each symbolic link replaced by its final target.</returns>
    private static string? ResolveLinks(string fullPath)
    {
        var current = Path.GetPathRoot(fullPath);
        if (current is null)
        {
            return null;
        }

        foreach (var part in fullPath[current.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var target = new FileInfo(current).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                current = target.FullName;
            }
        }

        return current;
    }

    /// <summary>
    ///     Asks Windows for the final path of an open handle (file or folder), with the
    ///     <c>\\?\</c> prefixes removed so it compares with an ordinary path.
    /// </summary>
    /// <param name="handle">The open handle.</param>
    /// <returns>The final path, or <see langword="null"/> when Windows cannot give one.</returns>
    private static string? GetFinalPathOfHandle(SafeFileHandle handle)
    {
        var buffer = new char[1024];
        var length = (int)GetFinalPathNameByHandle(handle, buffer, buffer.Length, 0);
        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = (int)GetFinalPathNameByHandle(handle, buffer, buffer.Length, 0);
        }

        if (length <= 0 || length > buffer.Length)
        {
            return null;
        }

        var path = new string(buffer, 0, length);
        return path switch
        {
            _ when path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) => @"\\" + path[@"\\?\UNC\".Length..],
            _ when path.StartsWith(@"\\?\", StringComparison.Ordinal) => path[@"\\?\".Length..],
            _ => path
        };
    }

    /// <summary>
    ///     Opens a file or folder to ask about it (Win32 <c>CreateFileW</c>).
    /// </summary>
    /// <param name="fileName">The path.</param>
    /// <param name="desiredAccess">The access wanted (none).</param>
    /// <param name="shareMode">What others may do meanwhile.</param>
    /// <param name="securityAttributes">Unused.</param>
    /// <param name="creationDisposition">What to do when it exists or not.</param>
    /// <param name="flagsAndAttributes">The open flags.</param>
    /// <param name="templateFile">Unused.</param>
    /// <returns>The handle, which is invalid on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    /// <summary>
    ///     Gets the final path of an open handle (Win32 <c>GetFinalPathNameByHandleW</c>).
    /// </summary>
    /// <param name="file">The open handle.</param>
    /// <param name="buffer">Receives the path.</param>
    /// <param name="bufferLength">The size of <paramref name="buffer"/> in characters.</param>
    /// <param name="flags">The form of path wanted (zero: normalized, with drive letter).</param>
    /// <returns>The path's length, or the size needed when the buffer is too small, or zero on failure.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        [Out][MarshalUsing(CountElementName = nameof(bufferLength))] char[] buffer,
        int bufferLength,
        uint flags);
}
