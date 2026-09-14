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
    ///     Opens/validates the package zip, validates every entry's destination path, deletes the
    ///     four managed folders under <paramref name="repoRoot"/> if present, and extracts the
    ///     same four folders from the zip into the repo.
    /// </summary>
    /// <param name="zipPath">Path to the agent package zip file.</param>
    /// <param name="repoRoot">Absolute path to the repository root to sync.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipPath"/> or
    ///     <paramref name="repoRoot"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the zip cannot be opened/is not a valid zip archive, when an entry's
    ///     destination path is invalid (including resolving outside <paramref name="repoRoot"/>),
    ///     when a managed folder cannot be deleted, or when extraction fails partway through. An
    ///     invalid entry path is rejected before any managed folder is deleted, so that failure
    ///     mode never leaves the repo partially upgraded; a delete or extraction failure can
    ///     still do so, and per the class remarks there is no rollback for that case — it must be
    ///     resolved manually by the caller.
    /// </exception>
    public static void Extract(string zipPath, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(zipPath);
        ArgumentNullException.ThrowIfNull(repoRoot);

        // Step 1: open/validate the zip. If it opens without error, its contents are assumed
        // good per architecture.md — no checksum/signature verification is performed.
        using var archive = OpenArchive(zipPath);

        // Step 2: resolve and validate every entry's destination path up front, before anything
        // is deleted. A malformed or path-traversing entry (one that fails SafePathCombine's
        // containment check) must reject the whole upgrade before any managed folder is
        // touched — otherwise a malicious/corrupt zip could blind-delete the existing managed
        // folders and then fail partway through extraction, leaving the repo unsynced while
        // still reporting only an "ordinary" failure to callers such as RepoCardViewModel's
        // best-effort, never-block-launch sync path.
        var managedEntries = new List<(ZipArchiveEntry Entry, string DestinationPath)>();
        foreach (var entry in archive.Entries)
        {
            var destinationPath = ResolveManagedDestination(entry, repoRoot);
            if (destinationPath is not null)
            {
                managedEntries.Add((entry, destinationPath));
            }
        }

        // Step 3: blind-delete the four known folders (if present)
        foreach (var folder in ManagedFolders)
        {
            DeleteManagedFolder(repoRoot, folder);
        }

        // Step 4: extract the already-validated managed entries, skipping any non-managed/root-
        // level files such as release-notes.md (those were never added to managedEntries above)
        try
        {
            foreach (var (entry, destinationPath) in managedEntries)
            {
                ExtractEntry(entry, destinationPath);
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
    ///     only partially populated) still counts as "existing" here.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> is
    ///     <see langword="null"/>.</exception>
    public static bool AllManagedFoldersExist(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);

        return ManagedFolders.All(folder => Directory.Exists(PathHelpers.SafePathCombine(repoRoot, folder)));
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
    private static void DeleteManagedFolder(string repoRoot, string relativeFolder)
    {
        var folderPath = PathHelpers.SafePathCombine(repoRoot, relativeFolder);

        if (!Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            Directory.Delete(folderPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Failed to delete folder '{folderPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Resolves a single zip entry's destination path if it falls within one of the four
    ///     managed folders, validating that path but not yet writing anything to disk.
    /// </summary>
    /// <param name="entry">The zip entry to consider.</param>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <returns>
    ///     The entry's absolute destination path if it falls within a managed folder; otherwise
    ///     <see langword="null"/> (a directory entry, or a file entry that does not resolve
    ///     inside any managed folder, including root-level files like <c>release-notes.md</c>).
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the entry's path is invalid,
    ///     including resolving outside <paramref name="repoRoot"/>.</exception>
    private static string? ResolveManagedDestination(ZipArchiveEntry entry, string repoRoot)
    {
        // Directory entries have an empty Name (only FullName ends with '/'); skip them, as
        // CreateDirectory in ExtractEntry (driven by file entries) recreates any needed structure.
        if (string.IsNullOrEmpty(entry.Name))
        {
            return null;
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
        return IsInsideManagedFolder(canonicalRelativePath) ? destinationPath : null;
    }

    /// <summary>
    ///     Extracts a single zip entry to an already-validated destination path, creating any
    ///     missing parent directories first.
    /// </summary>
    /// <param name="entry">The zip entry to extract.</param>
    /// <param name="destinationPath">
    ///     The entry's destination path, as previously resolved and validated by
    ///     <see cref="ResolveManagedDestination"/>.
    /// </param>
    private static void ExtractEntry(ZipArchiveEntry entry, string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        entry.ExtractToFile(destinationPath, overwrite: true);
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
