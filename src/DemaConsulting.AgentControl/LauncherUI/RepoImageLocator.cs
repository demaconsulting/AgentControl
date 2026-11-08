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

// cspell:ignore fcntl getpath maxpathlen vnode libc
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
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
///     picture), only the repo root is searched (never the folders below it, and the root is probed
///     for the two names rather than listed, so a root holding millions of files costs no more), the
///     name is matched without regard to case, links (symbolic links, junctions and other reparse
///     points) are refused so the file can never be a stand-in for something outside the repo, the size is
///     capped, the file must start with the PNG signature (the extension alone proves nothing), and
///     the declared dimensions must be sane (and the header chunk's length must be the 13 bytes a PNG
///     header always has) so a tiny file cannot expand into a huge bitmap - this
///     bound is what keeps a hostile file from exhausting memory in the decoder. The file is then
///     opened and, once it is open, the operating system's own answer to "which file is this
///     handle?" (<c>GetFinalPathNameByHandle</c> on Windows, <c>/proc/self/fd</c> on Linux,
///     <c>fcntl(F_GETPATH)</c> on macOS) must be a file directly in the root, so a file or folder
///     swapped for a link between the checks and the open cannot redirect the read; where the
///     operating system cannot answer, the file is not used. <see cref="Decode"/> repeats every check
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

    /// <summary>
    ///     The most files that differ from a candidate name only in letter case, other than the exact
    ///     spelling, that are looked at (in ordinal order of their names), so a root crowded with
    ///     similar names cannot make a lookup long. A spelling beyond the bound is ignored.
    /// </summary>
    private const int MaxCaseVariants = 8;

    /// <summary>
    ///     The value of the PNG header chunk's length field: the header chunk always holds exactly
    ///     13 bytes (width, height, bit depth, color type, compression, filter, interlace).
    /// </summary>
    private const uint HeaderChunkLength = 13;

    /// <summary>
    ///     macOS <c>F_GETPATH</c>: the <c>fcntl</c> command that returns the path of an open file.
    /// </summary>
    private const int FcntlGetPathCommand = 50;

    /// <summary>
    ///     macOS <c>MAXPATHLEN</c>: the size, in bytes, of the buffer <c>F_GETPATH</c> writes into.
    /// </summary>
    private const int MacMaxPathLength = 1024;

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

            // Root only: files in the folders below it are never considered. Only the two candidate
            // names are looked for - the folder is never listed - so the work does not depend on how
            // many files an untrusted repo root holds
            foreach (var name in CandidateNames)
            {
                foreach (var file in EnumerateNamed(root, name))
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
    ///     Yields the files in a repo root that have the given name (in any letter case), probing for
    ///     them instead of reading the whole folder, in a deterministic order.
    /// </summary>
    /// <remarks>
    ///     Streaming enumerations filtered by the literal name: the file system produces only the
    ///     entries spelled like the name and the folder is never listed into a collection, so neither
    ///     the memory nor the work done on the matches depends on how many unrelated files an untrusted
    ///     repo root holds (a name of seven letters has at most 128 spellings in any case, so the
    ///     matches are few). The choice never depends on the order the file system enumerates in: the
    ///     exact (lower-case) spelling is probed for first and always offered first; the other spellings
    ///     follow in ordinal order of their names, and only the first <see cref="MaxCaseVariants"/> of
    ///     them are offered - a spelling beyond that bound is ignored, however it would have sorted in
    ///     the file system's own order.
    /// </remarks>
    /// <param name="root">The repo root.</param>
    /// <param name="name">The candidate file name.</param>
    /// <returns>The matching files: the exact spelling first, then the other spellings in ordinal
    ///     order of their names.</returns>
    private static IEnumerable<FileInfo> EnumerateNamed(DirectoryInfo root, string name)
    {
        var options = new EnumerationOptions
        {
            MatchCasing = MatchCasing.CaseSensitive,
            MatchType = MatchType.Simple,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            IgnoreInaccessible = true
        };

        // The exact spelling, as the file system names the file (a case-sensitive match on the listed
        // name, so on a case-insensitive file system "Icon.PNG" is not mistaken for it)
        foreach (var exact in root.EnumerateFiles(name, options).Take(1))
        {
            yield return exact;
        }

        // The other spellings, in a fixed order, bounded
        options.MatchCasing = MatchCasing.CaseInsensitive;
        var others = root.EnumerateFiles(name, options)
            .Where(file => !string.Equals(file.Name, name, StringComparison.Ordinal))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .Take(MaxCaseVariants);
        foreach (var other in others)
        {
            yield return other;
        }
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
        if (stream is null)
        {
            return null;
        }

        // The length is read once, checked against the cap, and that same value sizes the buffer, so
        // a file that grows between the check and the allocation cannot enlarge the allocation; a
        // a file that shrank meanwhile fails the length comparison below
        var length = stream.Length;
        if (length is 0 or > MaxFileBytes)
        {
            return null;
        }

        var buffer = new byte[checked((int)length)];
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

        // The root's real location is fixed BEFORE the file is opened, and the root is confirmed
        // not to be a link after that. If the root is swapped for a link at any point, then either
        // this check sees the link, or the file below resolves into the link's target - which is
        // not the real location recorded here - and the comparison after the open refuses it.
        // (Resolving the root after the open instead would let both follow the same swapped link.)
        var finalRoot = GetFinalDirectoryPath(rootPath);
        if (finalRoot is null || IsLink(new DirectoryInfo(rootPath)))
        {
            return null;
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (IsDirectlyInRoot(stream, finalRoot, fileName))
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
    ///     the root, by the operating system's resolved path of the file.
    /// </summary>
    /// <param name="stream">The open file.</param>
    /// <param name="finalRoot">The repo root's resolved path, determined before the file was opened.</param>
    /// <param name="fileName">The expected file name.</param>
    /// <returns><see langword="true"/> when the opened file is the named file in the root.</returns>
    private static bool IsDirectlyInRoot(FileStream stream, string finalRoot, string fileName)
    {
        var finalFile = GetFinalPath(stream);
        return finalFile is not null
            && PathsEqual(Path.GetDirectoryName(finalFile), finalRoot)
            && string.Equals(Path.GetFileName(finalFile), fileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Compares two full paths the way the platform's file system does (without regard to letter
    ///     case on Windows and macOS, whose usual file systems are case-insensitive).
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
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

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
        // The header chunk follows the signature: 4 bytes of length (always 13), "IHDR", then width
        // and height
        if (BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4)) != HeaderChunkLength)
        {
            return false;
        }

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
    ///     Gets the final path - every link resolved - of the file an open stream is for, as the
    ///     operating system reports it for the open handle.
    /// </summary>
    /// <remarks>
    ///     Windows, Linux and macOS each have a way to ask the kernel which file a handle is for, and
    ///     only that answer is trusted. Re-resolving the stream's name instead would look the path up a
    ///     second time, after the open, and a link swapped in between would be followed to a different
    ///     file than the one that is open; so on any other platform, or when the question cannot be
    ///     answered, there is no answer and the picture is not used.
    /// </remarks>
    /// <param name="stream">The open file.</param>
    /// <returns>The final full path, or <see langword="null"/> when the operating system cannot say
    ///     (which means the file is not used).</returns>
    internal static string? GetFinalPath(FileStream stream)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetFinalPathOfHandle(stream.SafeFileHandle);
        }

        if (OperatingSystem.IsMacOS())
        {
            return GetFinalPathOfDescriptor(stream.SafeFileHandle);
        }

        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        // Linux names the file behind a descriptor in /proc. The descriptor number is only read (the
        // stream, which owns the handle, outlives this call), never used to close or duplicate it
#pragma warning disable S3869 // Reading the descriptor number is the only way to name it in /proc
        var viaProc = new FileInfo($"/proc/self/fd/{stream.SafeFileHandle.DangerousGetHandle()}").LinkTarget;
#pragma warning restore S3869
        return viaProc is not null && Path.IsPathRooted(viaProc) ? viaProc : null;
    }

    /// <summary>
    ///     Asks macOS for the path of the file an open descriptor is for
    ///     (<c>fcntl(fd, F_GETPATH)</c>).
    /// </summary>
    /// <remarks>
    ///     The kernel writes the path of the vnode behind the descriptor, with every link on the way
    ///     resolved, into a buffer that must hold at least <see cref="MacMaxPathLength"/> bytes. Any
    ///     failure (a non-zero result, an empty or unreadable answer, an exception from the call
    ///     itself) is "unknown". <c>fcntl</c> is a variadic C function, whose extra argument Apple's
    ///     ARM64 calling convention passes on the stack rather than in a register; see
    ///     <see cref="FcntlGetPathArm64"/>. The descriptor is kept alive for the duration of the call.
    /// </remarks>
    /// <param name="handle">The open file's handle.</param>
    /// <returns>The path, or <see langword="null"/> when macOS cannot give one.</returns>
    private static string? GetFinalPathOfDescriptor(SafeFileHandle handle)
    {
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
#pragma warning disable S3869 // The descriptor number is what fcntl takes; the reference added above keeps it open
            var descriptor = (int)handle.DangerousGetHandle();
#pragma warning restore S3869

            var buffer = new byte[MacMaxPathLength];
            var result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? FcntlGetPathArm64(descriptor, FcntlGetPathCommand, 0, 0, 0, 0, 0, 0, buffer)
                : FcntlGetPathX64(descriptor, FcntlGetPathCommand, buffer);
            if (result != 0)
            {
                return null;
            }

            var length = Array.IndexOf(buffer, (byte)0);
            return length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException
                                       or ObjectDisposedException or DecoderFallbackException)
        {
            // The question could not be asked or answered: unknown
            return null;
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
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
    /// <remarks>
    ///     The target of a link can itself lie below another link (on macOS a temporary folder is
    ///     under <c>/var</c>, which is a link to <c>/private/var</c>), so a link's target is resolved
    ///     the same way before the walk goes on; otherwise the result would not be the canonical
    ///     path that the operating system reports for an open file in the same place.
    /// </remarks>
    /// <param name="fullPath">The full path.</param>
    /// <param name="depth">How many links deep this resolution is; guards against a cycle.</param>
    /// <returns>The path with each symbolic link replaced by its final target, or
    ///     <see langword="null"/> when the links nest too deeply.</returns>
    private static string? ResolveLinks(string fullPath, int depth = 0)
    {
        const int maxDepth = 32;
        var current = Path.GetPathRoot(fullPath);
        if (current is null || depth > maxDepth)
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
                current = ResolveLinks(target.FullName, depth + 1);
                if (current is null)
                {
                    return null;
                }
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

    /// <summary>
    ///     Calls <c>fcntl(fd, command, buffer)</c> on x86-64 macOS, where the variadic argument is
    ///     passed in a register like any other.
    /// </summary>
    /// <param name="descriptor">The open file's descriptor.</param>
    /// <param name="command">The fcntl command (<see cref="FcntlGetPathCommand"/>).</param>
    /// <param name="path">Receives the path; at least <see cref="MacMaxPathLength"/> bytes.</param>
    /// <returns>Zero on success, otherwise -1.</returns>
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlGetPathX64(int descriptor, int command, Span<byte> path);

    /// <summary>
    ///     Calls <c>fcntl(fd, command, buffer)</c> on ARM64 macOS.
    /// </summary>
    /// <remarks>
    ///     Apple's ARM64 calling convention passes the arguments of a variadic function on the
    ///     stack, where the callee expects them, while a call declared with a fixed argument list puts
    ///     the third argument in a register. The six unused arguments fill the registers so that the
    ///     buffer is the ninth argument, which is passed on the stack - exactly where the variadic
    ///     <c>fcntl</c> reads its first extra argument.
    /// </remarks>
    /// <param name="descriptor">The open file's descriptor.</param>
    /// <param name="command">The fcntl command (<see cref="FcntlGetPathCommand"/>).</param>
    /// <param name="unused0">Unused register filler.</param>
    /// <param name="unused1">Unused register filler.</param>
    /// <param name="unused2">Unused register filler.</param>
    /// <param name="unused3">Unused register filler.</param>
    /// <param name="unused4">Unused register filler.</param>
    /// <param name="unused5">Unused register filler.</param>
    /// <param name="path">Receives the path; at least <see cref="MacMaxPathLength"/> bytes.</param>
    /// <returns>Zero on success, otherwise -1.</returns>
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int FcntlGetPathArm64(
        int descriptor,
        int command,
        nint unused0,
        nint unused1,
        nint unused2,
        nint unused3,
        nint unused4,
        nint unused5,
        Span<byte> path);
}
