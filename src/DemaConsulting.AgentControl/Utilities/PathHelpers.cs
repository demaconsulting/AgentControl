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

namespace DemaConsulting.AgentControl.Utilities;

/// <summary>
///     Helper utilities for safe path operations.
/// </summary>
/// <remarks>
///     Combines two complementary halves of path safety: <see cref="SafePathCombine"/> is purely
///     lexical (string-level containment, no file-system I/O), while
///     <see cref="FindReparsePointInAncestry"/>/<see cref="FindReparsePointInDescendants"/> are
///     filesystem-aware (detecting symlinks/junctions that a lexical check alone cannot see).
///     Callers that assemble a path and then touch the filesystem at it should normally use both:
///     first <see cref="SafePathCombine"/> to reject a textually-escaping path, then one of the
///     reparse-point finders to reject a link that would otherwise redirect an apparently-safe
///     path outside its intended root.
/// </remarks>
internal static class PathHelpers
{
    /// <summary>
    ///     Safely combines two paths, ensuring the resolved combined path stays within the base directory.
    /// </summary>
    /// <param name="basePath">The base path.</param>
    /// <param name="relativePath">The relative path to combine.</param>
    /// <returns>The combined path.</returns>
    /// <remarks>
    ///     Provides a security boundary for caller-supplied path components. Stateless and thread-safe.
    ///     Performs no file-system I/O; only string-level path normalization is applied.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="basePath"/> or <paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the resolved combined path escapes the base directory, or when a supplied path is invalid.
    /// </exception>
    /// <exception cref="NotSupportedException">Thrown when a supplied path contains an unsupported format.</exception>
    /// <exception cref="PathTooLongException">Thrown when the combined or resolved path exceeds the system-defined maximum length.</exception>
    internal static string SafePathCombine(string basePath, string relativePath)
    {
        // Validate inputs
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(relativePath);

        // Combine the paths (preserves the caller's relative/absolute style)
        var combinedPath = Path.Combine(basePath, relativePath);

        // Security check: resolve both paths to absolute form and verify the combined
        // path is still inside the base directory. Path.GetRelativePath handles root
        // paths, platform case-sensitivity, and directory-separator normalization natively.
        var absoluteBase = Path.GetFullPath(basePath);
        var absoluteCombined = Path.GetFullPath(combinedPath);
        var checkRelative = Path.GetRelativePath(absoluteBase, absoluteCombined);

        if (string.Equals(checkRelative, "..", StringComparison.Ordinal)
            || checkRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || checkRelative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(checkRelative))
        {
            throw new ArgumentException($"Invalid path component: {relativePath}", nameof(relativePath));
        }

        return combinedPath;
    }

    /// <summary>
    ///     Searches upward from <paramref name="path"/> to (and including) <paramref name="root"/>,
    ///     looking for a directory that is itself a reparse point (symlink/junction).
    /// </summary>
    /// <param name="root">The already fully-resolved (<see cref="Path.GetFullPath(string)"/>)
    ///     boundary at which the upward walk stops; also checked, since a symlinked/junctioned
    ///     root would otherwise let every operation beneath it silently write through the link.</param>
    /// <param name="path">The already fully-resolved path whose ancestry (up to and including
    ///     <paramref name="root"/>) is inspected.</param>
    /// <returns>
    ///     The closest-to-<paramref name="path"/> segment that is a reparse point, or
    ///     <see langword="null"/> if no segment between <paramref name="path"/> and
    ///     <paramref name="root"/> (inclusive of both ends) is one.
    /// </returns>
    /// <remarks>
    ///     This is the filesystem-aware counterpart to <see cref="SafePathCombine"/>: that method
    ///     is purely lexical and never touches the filesystem, so it cannot detect a symlinked/
    ///     junctioned ancestor silently redirecting a nominally-contained path outside its
    ///     intended root. Reparse-point status is queried via
    ///     <see cref="File.GetAttributes(string)"/> rather than <see cref="Directory.Exists(string)"/>:
    ///     the latter resolves (follows) a link to test whether its target exists, and so returns
    ///     <see langword="false"/> (silently missing the reparse point) for a *dangling*
    ///     symlink/junction, whereas <see cref="File.GetAttributes(string)"/> reports the reparse
    ///     point's own attributes without requiring its target to exist. A path segment that does
    ///     not exist yet (e.g. a destination directory a caller is about to create) is not itself
    ///     a finding; the walk simply continues upward past it. Callers that need to react to a
    ///     found reparse point (e.g. by throwing, or by treating the path as unusable) decide that
    ///     policy themselves - this method only ever reports what it found. This only reflects the
    ///     state of the filesystem at the moment of the call; it does not eliminate a race where a
    ///     segment is replaced with a reparse point between this check and a caller's subsequent
    ///     file operation. Stateless and thread-safe, but - unlike <see cref="SafePathCombine"/> -
    ///     performs real file-system I/O.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> or
    ///     <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when a path segment's attributes cannot be read for a
    ///     reason other than the segment not existing.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to
    ///     read a path segment's attributes.</exception>
    internal static string? FindReparsePointInAncestry(string root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);

        var current = Path.TrimEndingDirectorySeparator(path);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);

        while (!string.IsNullOrEmpty(current))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                if (string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
                continue;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return current;
            }

            if (string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>
    ///     Recursively searches <paramref name="directory"/> (inclusive) for the first nested
    ///     directory that is a reparse point (symlink/junction).
    /// </summary>
    /// <param name="directory">The already-existing directory (and its descendants) to search.</param>
    /// <returns>
    ///     The path of the first reparse point found (<paramref name="directory"/> itself, or a
    ///     descendant), or <see langword="null"/> if none exists anywhere in the tree.
    /// </returns>
    /// <remarks>
    ///     Exists for callers that need to recursively delete, copy, or otherwise walk a directory
    ///     tree without following filesystem links nested inside it - unlike
    ///     <see cref="Directory.Delete(string, bool)"/>'s recursive mode, which follows such links
    ///     and can affect content outside the tree being processed. The whole tree is searched up
    ///     front, rather than interleaving this check with file-by-file processing, so a caller
    ///     can preflight an entire operation and fail closed before acting on (e.g. deleting) any
    ///     part of the tree if a reparse point exists anywhere within it. As with
    ///     <see cref="FindReparsePointInAncestry"/>, this only reflects a single point in time and
    ///     performs real file-system I/O.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="IOException">Thrown when a directory's attributes or contents cannot be
    ///     read for a reason other than a permission failure.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller lacks permission to
    ///     read a directory's attributes or contents.</exception>
    internal static string? FindReparsePointInDescendants(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
        {
            return directory;
        }

        foreach (var subdirectory in Directory.GetDirectories(directory))
        {
            var found = FindReparsePointInDescendants(subdirectory);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
