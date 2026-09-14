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

using System.IO.Compression;
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     Implements the blind-delete-and-replace upgrade sequence for a repo's four managed agent
///     folders, from a package zip file.
/// </summary>
/// <remarks>
///     Per architecture.md's "Upgrade sequence and failure handling" decision, this performs:
///     (1) open/validate the zip; (2) delete the four known folders if present; (3) extract only
///     those same four folders from the zip (root-level files such as <c>release-notes.md</c> are
///     never extracted to disk). Rewriting the <c>.agentcontrol.json</c> pin is the caller's
///     responsibility (see the <c>RepoConfig</c> subsystem) so this class stays focused on file
///     operations alone. There is intentionally no rollback on failure — per architecture.md, a
///     failed upgrade is surfaced to the caller (Phase 2 will show a message box) and the
///     developer is expected to resolve it manually. Stateless and thread-safe: every member is a
///     pure function of its parameters plus the filesystem.
/// </remarks>
internal static class PackageZipExtractor
{
    /// <summary>
    ///     Name of the root <c>.github</c> folder under which every managed agent folder lives.
    /// </summary>
    private const string GitHubFolderName = ".github";

    /// <summary>
    ///     The four agent folders (relative to a repo root) that are blind-deleted and replaced
    ///     on every sync/upgrade, per architecture.md.
    /// </summary>
    private static readonly string[] ManagedFolders =
    [
        Path.Combine(GitHubFolderName, "agents"),
        Path.Combine(GitHubFolderName, "standards"),
        Path.Combine(GitHubFolderName, "templates"),
        Path.Combine(GitHubFolderName, "skills")
    ];

    /// <summary>
    ///     Name of the root-level release notes entry within a package zip; never extracted to
    ///     disk, only readable via <see cref="ReadReleaseNotes"/>.
    /// </summary>
    private const string ReleaseNotesEntryName = "release-notes.md";

    /// <summary>
    ///     Opens/validates the package zip, deletes the four managed folders under
    ///     <paramref name="repoRoot"/> if present, and extracts the same four folders from the
    ///     zip into the repo.
    /// </summary>
    /// <param name="zipPath">Path to the agent package zip file.</param>
    /// <param name="repoRoot">Absolute path to the repository root to sync.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipPath"/> or
    ///     <paramref name="repoRoot"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the zip cannot be opened/is not a valid zip archive, when a managed folder
    ///     cannot be deleted, or when extraction fails partway through. Per the class remarks,
    ///     there is no rollback: a partially-applied change is possible and must be resolved
    ///     manually by the caller.
    /// </exception>
    /// <exception cref="UnsafeRepositoryStateException">
    ///     Thrown (derives from <see cref="InvalidOperationException"/>) when the repo root, a
    ///     managed folder's ancestor, the folder itself, or any descendant of it is a reparse
    ///     point (symlink/junction) - callers that must react differently to this specific
    ///     security concern (rather than an ordinary extraction failure) can catch it before the
    ///     general <see cref="InvalidOperationException"/> case.
    /// </exception>
    public static void Extract(string zipPath, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(zipPath);
        ArgumentNullException.ThrowIfNull(repoRoot);

        // Step 1: open/validate the zip. If it opens without error, its contents are assumed
        // good per architecture.md — no checksum/signature verification is performed.
        using var archive = OpenArchive(zipPath);

        // Step 2: blind-delete the four known folders (if present)
        foreach (var folder in ManagedFolders)
        {
            DeleteManagedFolder(repoRoot, folder);
        }

        // Step 3: extract only the same four folders from the zip, skipping root-level files
        // such as release-notes.md
        try
        {
            foreach (var entry in archive.Entries)
            {
                ExtractEntryIfManaged(entry, repoRoot);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Failed to extract package '{zipPath}' into '{repoRoot}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Determines whether all four managed agent folders currently exist under a repo root.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root to check.</param>
    /// <returns><see langword="true"/> if every managed folder (<see cref="ManagedFolders"/>)
    ///     exists under <paramref name="repoRoot"/>; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    ///     Built on the same <see cref="ManagedFolders"/> array <see cref="Extract"/> itself uses,
    ///     so the managed-folder list has a single source of truth - callers (e.g.
    ///     <c>RepoCardViewModel</c>'s ensure-synced-before-launch check) never need to duplicate
    ///     it. Does not inspect folder contents; a managed folder that exists but is empty (or
    ///     only partially populated) still counts as "existing" here. A managed folder that is
    ///     only reachable through a reparse-point (symlink/junction) repo root or ancestor - e.g.
    ///     a symlinked <c>.github</c> - is deliberately treated as <b>not</b> existing: trusting
    ///     it here would let a caller (such as <c>RepoCardViewModel.EnsureAgentFilesSyncedBeforeLaunch</c>)
    ///     skip <see cref="Extract"/> entirely and launch using files outside <paramref name="repoRoot"/>
    ///     without any of <see cref="Extract"/>'s reparse-point protections ever running.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> is
    ///     <see langword="null"/>.</exception>
    public static bool AllManagedFoldersExist(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);

        var normalizedRoot = Path.GetFullPath(repoRoot);
        return ManagedFolders.All(folder => ManagedFolderGenuinelyExists(normalizedRoot, folder));
    }

    /// <summary>
    ///     Determines whether a single managed folder exists under a repo root without being
    ///     reached through a reparse point (symlink/junction) repo root, ancestor, or the folder
    ///     itself.
    /// </summary>
    /// <param name="normalizedRoot">The already-resolved (<see cref="Path.GetFullPath(string)"/>)
    ///     repo root.</param>
    /// <param name="relativeFolder">The managed folder's path relative to the repo root.</param>
    /// <returns><see langword="true"/> if the folder exists and no reparse point sits between it
    ///     and <paramref name="normalizedRoot"/> (inclusive); otherwise <see langword="false"/>.</returns>
    /// <remarks>
    ///     Deliberately swallows (as "not genuinely present") both a found reparse point and any
    ///     I/O failure while inspecting the ancestor chain - e.g. an
    ///     <see cref="UnauthorizedAccessException"/> from an ACL-restricted ancestor, which
    ///     <see cref="PathHelpers.FindReparsePointInAncestry"/> does not itself catch. This keeps
    ///     <see cref="AllManagedFoldersExist"/>'s contract to only ever throw
    ///     <see cref="ArgumentNullException"/> (its callers, e.g.
    ///     <c>RepoCardViewModel.EnsureAgentFilesSyncedBeforeLaunch</c>, only guard against
    ///     <see cref="Extract"/>'s own documented exceptions and do not expect this read-only
    ///     check to throw anything else).
    /// </remarks>
    private static bool ManagedFolderGenuinelyExists(string normalizedRoot, string relativeFolder)
    {
        var folderPath = PathHelpers.SafePathCombine(normalizedRoot, relativeFolder);
        if (!Directory.Exists(folderPath))
        {
            return false;
        }

        try
        {
            return PathHelpers.FindReparsePointInAncestry(normalizedRoot, folderPath) is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An inability to even inspect an ancestor (e.g. an ACL-restricted directory) means
            // this folder cannot be confirmed as genuinely present under normalizedRoot; treat it
            // the same as missing so callers fall back to Extract, which will itself surface the
            // underlying failure as a hard error instead of silently trusting - or crashing on -
            // content reached through a symlink/junction.
            return false;
        }
    }

    /// <summary>
    ///     Reads the content of the package zip's root-level <c>release-notes.md</c> entry
    ///     without extracting it to disk.
    /// </summary>
    /// <param name="zipPath">Path to the agent package zip file.</param>
    /// <returns>
    ///     The release notes text, or <see langword="null"/> if the zip has no root-level
    ///     <c>release-notes.md</c> entry.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipPath"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the zip cannot be opened or the
    ///     release notes entry cannot be read.</exception>
    public static string? ReadReleaseNotes(string zipPath)
    {
        ArgumentNullException.ThrowIfNull(zipPath);

        using var archive = OpenArchive(zipPath);

        var entry = archive.GetEntry(ReleaseNotesEntryName);
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"Failed to read '{ReleaseNotesEntryName}' from '{zipPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Opens a package zip archive for reading, wrapping any failure in a clear exception.
    /// </summary>
    /// <param name="zipPath">Path to the zip file.</param>
    /// <returns>The opened <see cref="ZipArchive"/>; the caller owns disposal.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the file does not exist, is not a
    ///     valid zip archive, or cannot be opened for another I/O reason.</exception>
    private static ZipArchive OpenArchive(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidOperationException($"Failed to open package zip '{zipPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Blind-deletes a single managed folder under a repo root, if it currently exists.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <param name="relativeFolder">The managed folder's path relative to the repo root.</param>
    /// <exception cref="InvalidOperationException">Thrown when the folder exists but cannot be
    ///     deleted.</exception>
    /// <exception cref="UnsafeRepositoryStateException">Thrown when the repo root, an ancestor,
    ///     the folder itself, or any descendant of the folder is a reparse point
    ///     (symlink/junction).</exception>
    private static void DeleteManagedFolder(string repoRoot, string relativeFolder)
    {
        var folderPath = PathHelpers.SafePathCombine(repoRoot, relativeFolder);

        try
        {
            // Reject a reparse-point repo root/ancestor (e.g. a symlinked/junctioned '.github')
            // before the blind delete below: Directory.Delete(recursive: true) follows filesystem
            // links, so without this guard a crafted/pre-existing junction could cause content
            // outside repoRoot to be deleted before extraction's own EnsureNoSymlinkAncestors check
            // is ever reached. Wrapped alongside the delete itself so an UnauthorizedAccessException
            // from an ACL-restricted ancestor surfaces as the same documented InvalidOperationException,
            // consistent with ExtractEntryIfManaged's equivalent call (protected by Extract's step-3
            // try/catch).
            EnsureNoSymlinkAncestors(Path.GetFullPath(repoRoot), folderPath, relativeFolder);

            if (!Directory.Exists(folderPath))
            {
                return;
            }

            // A plain Directory.Delete(folderPath, recursive: true) would also follow any
            // reparse point nested *inside* the managed folder (not just its ancestors),
            // potentially deleting content outside repoRoot. DeleteDirectoryRejectingReparsePoints
            // walks the tree itself and fails closed the moment it finds one.
            DeleteDirectoryRejectingReparsePoints(folderPath, relativeFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to delete folder '{folderPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Recursively deletes <paramref name="directoryPath"/>, rejecting the delete if it or any
    ///     descendant directory is itself a reparse point (symlink/junction) - unlike
    ///     <see cref="Directory.Delete(string, bool)"/>'s recursive mode, which follows such links
    ///     and can delete content outside the directory being cleaned up.
    /// </summary>
    /// <param name="directoryPath">The directory to delete.</param>
    /// <param name="context">A short description of the folder being deleted, for the exception
    ///     message.</param>
    /// <remarks>
    ///     The entire tree is preflighted for reparse points (<see cref="PathHelpers.FindReparsePointInDescendants"/>)
    ///     <b>before</b> anything is deleted. Interleaving the reparse-point check with the actual
    ///     delete (checking each directory immediately before deleting its files) would still let
    ///     an ordinary sibling file be permanently deleted before a reparse point discovered
    ///     later in the same tree aborts the operation, leaving the managed folder partially
    ///     destroyed instead of untouched.
    /// </remarks>
    /// <exception cref="UnsafeRepositoryStateException">Thrown when a nested reparse point is
    ///     encountered anywhere in the tree; nothing is deleted in this case.</exception>
    private static void DeleteDirectoryRejectingReparsePoints(string directoryPath, string context)
    {
        var reparsePoint = PathHelpers.FindReparsePointInDescendants(directoryPath);
        if (reparsePoint is not null)
        {
            throw new UnsafeRepositoryStateException(
                $"'{context}' contains a symlinked directory '{reparsePoint}'; refusing to delete through it.");
        }

        DeleteDirectoryTree(directoryPath);
    }

    /// <summary>
    ///     Recursively deletes every file and subdirectory under <paramref name="directoryPath"/>,
    ///     then the now-empty directory itself.
    /// </summary>
    /// <param name="directoryPath">The directory to delete.</param>
    /// <remarks>
    ///     Assumes <see cref="PathHelpers.FindReparsePointInDescendants"/> has already verified the
    ///     whole tree contains no reparse points; this method performs no such check itself, since
    ///     re-checking here would re-introduce the same interleaved check-then-delete race the
    ///     two-phase split in <see cref="DeleteDirectoryRejectingReparsePoints"/> exists to avoid.
    /// </remarks>
    private static void DeleteDirectoryTree(string directoryPath)
    {
        foreach (var filePath in Directory.GetFiles(directoryPath))
        {
            File.Delete(filePath);
        }

        foreach (var subdirectoryPath in Directory.GetDirectories(directoryPath))
        {
            DeleteDirectoryTree(subdirectoryPath);
        }

        Directory.Delete(directoryPath, recursive: false);
    }

    /// <summary>
    ///     Extracts a single zip entry into the repo root, but only if it falls within one of the
    ///     four managed folders; entries elsewhere (including root-level files like
    ///     <c>release-notes.md</c>) are skipped.
    /// </summary>
    /// <param name="entry">The zip entry to consider.</param>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <exception cref="InvalidOperationException">Thrown when the entry's path is invalid,
    ///     including resolving outside <paramref name="repoRoot"/>.</exception>
    /// <exception cref="UnsafeRepositoryStateException">Thrown when the repo root, an ancestor, or
    ///     the destination directory itself is a reparse point (symlink/junction).</exception>
    private static void ExtractEntryIfManaged(ZipArchiveEntry entry, string repoRoot)
    {
        // Directory entries have an empty Name (only FullName ends with '/'); skip them, as
        // CreateDirectory below (driven by file entries) recreates any needed structure.
        if (string.IsNullOrEmpty(entry.Name))
        {
            return;
        }

        // Zip entries always use '/' regardless of platform; normalize before combining.
        var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

        // PathHelpers.SafePathCombine is the single source of truth for containment validation
        // (see its own doc remarks); it throws ArgumentException/NotSupportedException both when
        // the entry's path would resolve outside repoRoot and for other malformed-path failures,
        // so the original message is preserved here rather than assuming every failure is a
        // traversal attempt.
        string destinationPath;
        try
        {
            destinationPath = PathHelpers.SafePathCombine(repoRoot, relativePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Zip entry '{entry.FullName}' has an invalid destination path: {ex.Message}", ex);
        }

        // The managed-folder membership check must run against the *canonical* (".."-resolved)
        // relative path, not the raw entry-supplied one: a crafted entry such as
        // ".github/agents/../../outside.txt" textually starts with a managed-folder prefix but
        // resolves elsewhere. Deriving the relative path from the already-validated
        // destinationPath closes that gap.
        var canonicalRelativePath = Path.GetRelativePath(Path.GetFullPath(repoRoot), destinationPath);
        if (!IsInsideManagedFolder(canonicalRelativePath))
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            // Path.GetFullPath (used internally by SafePathCombine) performs lexical
            // normalization only - it does not resolve filesystem links - so a symlinked
            // ancestor directory could otherwise still cause extraction to escape the repo root
            // despite the containment check above passing. Reject any ancestor between the repo
            // root and the destination that is itself a reparse point (symlink/junction) before
            // creating anything.
            EnsureNoSymlinkAncestors(Path.GetFullPath(repoRoot), destinationDirectory, entry.FullName);

            Directory.CreateDirectory(destinationDirectory);
        }

        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    /// <summary>
    ///     Rejects the operation if <paramref name="repoRoot"/> or any path segment between it and
    ///     <paramref name="path"/> (inclusive of both ends) is itself a reparse point
    ///     (symlink/junction), throwing a domain-specific <see cref="UnsafeRepositoryStateException"/>
    ///     with a message naming <paramref name="context"/> and the offending path.
    /// </summary>
    /// <param name="repoRoot">The already-resolved (<see cref="Path.GetFullPath(string)"/>) repo
    ///     root; also checked, since a symlinked/junctioned repo root would otherwise let every
    ///     managed-folder operation write through it undetected.</param>
    /// <param name="path">The path whose ancestry is being validated - either a zip entry's
    ///     destination directory (before extraction) or a managed folder about to be blind-deleted.</param>
    /// <param name="context">A short description of the path/entry, for the exception message.</param>
    /// <remarks>
    ///     A thin, exception-throwing policy wrapper around
    ///     <see cref="PathHelpers.FindReparsePointInAncestry"/>, which owns the actual
    ///     filesystem-aware detection logic (see its own doc remarks for why a lexical-only check
    ///     is insufficient and why <see cref="File.GetAttributes(string)"/> is used over
    ///     <see cref="Directory.Exists(string)"/>). This only guards against paths that already
    ///     exist at the time of the check; it does not eliminate a race where a path is replaced
    ///     with a symlink between this check and
    ///     <see cref="Directory.CreateDirectory(string)"/>/<see cref="ZipFileExtensions.ExtractToFile(ZipArchiveEntry, string, bool)"/>/
    ///     <see cref="Directory.Delete(string, bool)"/>.
    /// </remarks>
    /// <exception cref="UnsafeRepositoryStateException">Thrown when a path in the walk is a
    ///     reparse point.</exception>
    private static void EnsureNoSymlinkAncestors(string repoRoot, string path, string context)
    {
        var reparsePoint = PathHelpers.FindReparsePointInAncestry(repoRoot, path);
        if (reparsePoint is not null)
        {
            throw new UnsafeRepositoryStateException(
                $"'{context}' resolves through a symlinked directory '{reparsePoint}'.");
        }
    }

    /// <summary>
    ///     Determines whether a zip-relative path falls within one of the four managed folders.
    /// </summary>
    /// <param name="relativePath">A path relative to the repo root, using OS directory
    ///     separators.</param>
    /// <returns><see langword="true"/> if the path is inside a managed folder; otherwise
    ///     <see langword="false"/>.</returns>
    private static bool IsInsideManagedFolder(string relativePath) =>
        ManagedFolders.Any(folder =>
            relativePath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal));
}
