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

using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     <see cref="IPackageContentReader"/> over an unpacked agent package folder (for example a
///     NuGet package extracted into a package cache), whose content root is the folder's
///     <c>content</c> sub-folder.
/// </summary>
/// <remarks>
///     <para>
///     The content root is read exactly as a zip's root is: <c>.github/...</c> managed folders plus
///     <c>release-notes.md</c> and the optional <c>AGENTS.md</c> at the top. A package folder with
///     no <c>content</c> sub-folder is rejected at <see cref="Open"/> so that a wrong-layout
///     package can never make the extractor blind-delete a repo's managed folders and replace them
///     with nothing.
///     </para>
///     <para>
///     The package folder is typically a shared cache, so this reader is strictly read-only. It
///     also rejects any symbolic link, junction or other reparse point at the package folder
///     itself, at the content root, and at every directory or file along each path it reads
///     (any of which could redirect a read to an arbitrary file) rather than following it, and
///     every file path it opens is validated with <see cref="PathHelpers.SafePathCombine"/> to stay inside the
///     content root. Not thread-safe; holds no file handles between calls.
///     </para>
/// </remarks>
internal sealed class DirectoryPackageContentReader : IPackageContentReader
{
    /// <summary>
    ///     Name of the folder inside an unpacked package that holds the agent content.
    /// </summary>
    internal const string ContentFolderName = "content";

    /// <summary>
    ///     The separator used in package-relative paths, regardless of platform.
    /// </summary>
    private const char PackagePathSeparator = '/';

    /// <summary>
    ///     Absolute path of the content root (<c>{packageFolder}/content</c>).
    /// </summary>
    private readonly string _contentRoot;

    /// <summary>
    ///     Initializes a reader for a package folder whose content root has been validated.
    /// </summary>
    /// <param name="packageFolder">The unpacked package folder.</param>
    /// <param name="contentRoot">The validated content root inside it.</param>
    /// <param name="contentIdentity">The identity the opener computed for the package, if any.</param>
    private DirectoryPackageContentReader(string packageFolder, string contentRoot, string? contentIdentity)
    {
        Description = packageFolder;
        _contentRoot = contentRoot;
        ContentIdentity = contentIdentity;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>
    ///     Gets the identity the opener supplied for this package (for a NuGet package, the
    ///     SHA-256 of the unpacked <c>.nupkg</c>), or <see langword="null"/> when none was given.
    ///     The reader only carries it: a folder reader does not hash anything itself because the
    ///     folder has no single file whose bytes define the package.
    /// </summary>
    public string? ContentIdentity { get; }

    /// <summary>
    ///     Opens an unpacked package folder for reading.
    /// </summary>
    /// <param name="packageFolder">The unpacked package folder (the one that contains
    ///     <c>content</c>).</param>
    /// <param name="contentIdentity">The content identity of the package, as computed by the caller
    ///     (see <see cref="IPackageContentReader.ContentIdentity"/>); <see langword="null"/> when
    ///     the caller has none.</param>
    /// <returns>A reader over the folder's <c>content</c> sub-folder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="packageFolder"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the folder has no <c>content</c>
    ///     sub-folder, or the package folder or the content root is itself a link.</exception>
    public static DirectoryPackageContentReader Open(string packageFolder, string? contentIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(packageFolder);

        // A linked package folder could redirect every read to an arbitrary location, so the
        // folder itself is checked before anything under it is trusted
        if (Directory.Exists(packageFolder) && IsLink(new DirectoryInfo(packageFolder)))
        {
            throw new InvalidOperationException(
                $"Package folder '{packageFolder}' is a link, which is not allowed.");
        }

        // The content folder is mandatory: without it there is nothing to sync, and proceeding
        // would let the extractor delete the repo's managed folders for no replacement.
        var contentRoot = Path.Combine(packageFolder, ContentFolderName);
        if (!Directory.Exists(contentRoot))
        {
            throw new InvalidOperationException(
                $"Package folder '{packageFolder}' has no '{ContentFolderName}' folder; " +
                $"agent packages must place their files under '{ContentFolderName}/'.");
        }

        if (IsLink(new DirectoryInfo(contentRoot)))
        {
            throw new InvalidOperationException(
                $"Package content folder '{contentRoot}' is a link, which is not allowed.");
        }

        return new DirectoryPackageContentReader(packageFolder, contentRoot, contentIdentity);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles()
    {
        var results = new List<string>();
        try
        {
            Walk(new DirectoryInfo(_contentRoot), string.Empty, results);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Failed to enumerate package content in '{Description}': {ex.Message}", ex);
        }

        return results;
    }

    /// <inheritdoc />
    public Stream OpenRead(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var path = ResolveFile(relativePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Package '{Description}' has no file '{relativePath}'.");
        }

        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Failed to open '{relativePath}' from '{Description}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public string? ReadText(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var path = ResolveFile(relativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Failed to read '{relativePath}' from '{Description}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release: no handle is held between calls.
    }

    /// <summary>
    ///     Resolves a relative path to an absolute file path inside the content root, rejecting
    ///     traversal and links anywhere along the path.
    /// </summary>
    /// <param name="relativePath">The relative path using <c>/</c> separators.</param>
    /// <returns>The absolute path (which may not exist).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the path escapes the content root
    ///     or the file or any directory leading to it is a link.</exception>
    private string ResolveFile(string relativePath)
    {
        string path;
        try
        {
            path = PathHelpers.SafePathCombine(_contentRoot, relativePath.Replace(PackagePathSeparator, Path.DirectorySeparatorChar));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Package file '{relativePath}' has an invalid path: {ex.Message}", ex);
        }

        RejectLinksAlongPath(relativePath);
        return path;
    }

    /// <summary>
    ///     Rejects a requested path when any directory component between the content root and the
    ///     file, or the file itself, is a link of any kind.
    /// </summary>
    /// <param name="relativePath">The relative path using <c>/</c> separators; already validated
    ///     to stay inside the content root.</param>
    /// <exception cref="InvalidOperationException">Thrown when a component that exists - or a
    ///     broken link that would exist if its target did - is a link.</exception>
    /// <remarks>
    ///     Checking only the last component would let a linked parent directory containing an
    ///     ordinary file redirect the read outside the package, so every component is examined,
    ///     stopping at the first one that does not exist.
    /// </remarks>
    private void RejectLinksAlongPath(string relativePath)
    {
        var current = _contentRoot;
        foreach (var component in relativePath.Split(PackagePathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!entry.Exists)
            {
                // A broken link does not "exist" (Exists follows the link) but it is still a link
                // that a later write or read could be redirected through, so it is refused like
                // any other; only a genuinely missing entry ends the check
                if (entry.LinkTarget is not null)
                {
                    throw new InvalidOperationException(
                        $"Package file '{relativePath}' in '{Description}' passes through a broken link, which is not allowed.");
                }

                return;
            }

            if (IsLink(entry))
            {
                throw new InvalidOperationException(
                    $"Package file '{relativePath}' in '{Description}' passes through a link, which is not allowed.");
            }
        }
    }

    /// <summary>
    ///     Recursively collects the files under a directory, rejecting any link on the way.
    /// </summary>
    /// <param name="directory">The directory to walk.</param>
    /// <param name="relativePrefix">The relative path of <paramref name="directory"/> using
    ///     <c>/</c> separators, or empty for the content root.</param>
    /// <param name="results">Receives the relative file paths.</param>
    /// <exception cref="InvalidOperationException">Thrown when a link is found.</exception>
    private void Walk(DirectoryInfo directory, string relativePrefix, List<string> results)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            var relativePath = relativePrefix.Length == 0 ? entry.Name : string.Concat(relativePrefix, PackagePathSeparator, entry.Name);
            if (IsLink(entry))
            {
                throw new InvalidOperationException(
                    $"Package content '{relativePath}' in '{Description}' is a link, which is not allowed.");
            }

            if (entry is DirectoryInfo subDirectory)
            {
                Walk(subDirectory, relativePath, results);
            }
            else
            {
                results.Add(relativePath);
            }
        }
    }

    /// <summary>
    ///     Determines whether a file system entry is a symbolic link, junction or other reparse
    ///     point.
    /// </summary>
    /// <param name="entry">The entry to test.</param>
    /// <returns><see langword="true"/> if the entry is a link of any kind.</returns>
    private static bool IsLink(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || entry.LinkTarget is not null;
}
