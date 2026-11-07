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

using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.Utilities;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     Implements the blind-delete-and-replace upgrade sequence for a repo's four managed agent
///     folders, from the content of any package (zip file or unpacked folder) presented through an
///     <see cref="IPackageContentReader"/>.
/// </summary>
/// <remarks>
///     <para>
///     Per architecture.md's "Upgrade sequence and failure handling" decision, this performs:
///     (1) read/validate the package; (2) delete the four known folders if present; (3) copy only
///     those same four folders from the package into the repo (root-level files such as
///     <c>release-notes.md</c> and the optional <c>AGENTS.md</c> template are never copied to disk
///     - see <see cref="IsInsideManagedFolder"/>, whose managed-folder-prefix check excludes any
///     root-level entry regardless of name). Rewriting the <c>.agentcontrol.json</c> pin is the
///     caller's responsibility (see the <c>RepoConfig</c> subsystem). There is intentionally no
///     rollback on failure - a failed upgrade is surfaced to the caller and the developer is
///     expected to resolve it manually.
///     </para>
///     <para>
///     The same sequence, managed-folder list and containment rules apply to every package
///     format; only how files are listed and read differs between readers. Every destination path
///     is validated with <see cref="PathHelpers.SafePathCombine"/> before anything is deleted, so
///     a path-traversing entry can never cause a partial upgrade. The package itself is only ever
///     read: nothing is written to a zip or to a NuGet package cache. Stateless and thread-safe:
///     every member is a pure function of its parameters plus the filesystem.
///     </para>
/// </remarks>
internal static class PackageContentExtractor
{
    /// <summary>
    ///     Fully qualified logger category name used for this static class's diagnostics.
    /// </summary>
    /// <remarks>
    ///     A string category is used (rather than the generic <c>ILogger&lt;PackageContentExtractor&gt;</c>
    ///     form) because a static class cannot be used as a generic type argument.
    /// </remarks>
    private const string LoggerCategoryName = "DemaConsulting.AgentControl.RepoSync.PackageContentExtractor";

    /// <summary>
    ///     Name of the root <c>.github</c> folder under which every managed agent folder lives.
    /// </summary>
    private const string GitHubFolderName = ".github";

    /// <summary>
    ///     Name of the root-level release notes file within a package; never copied to disk, only
    ///     readable via <see cref="ReadReleaseNotes"/>.
    /// </summary>
    private const string ReleaseNotesEntryName = "release-notes.md";

    /// <summary>
    ///     Name of the optional root-level AGENTS.md template within a package; never copied to
    ///     disk by <see cref="Extract"/>, only readable via <see cref="ReadAgentsMdTemplate"/>.
    /// </summary>
    private const string AgentsMdEntryName = "AGENTS.md";

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
    ///     Validates every file path in the package, deletes the four managed folders under
    ///     <paramref name="repoRoot"/> if present, and copies the same four folders from the
    ///     package into the repo.
    /// </summary>
    /// <param name="reader">The package content to copy from. Not disposed by this method.</param>
    /// <param name="repoRoot">Absolute path to the repository root to sync.</param>
    /// <param name="logger">
    ///     Logger for extraction diagnostics, or <see langword="null"/> to fall back to
    ///     <see cref="AppLogging.Factory"/>. Resolved fresh per call rather than cached in a static
    ///     field, so tests that configure logging after this class is first touched are never stuck
    ///     with a stale no-op logger for the rest of the process.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> or
    ///     <paramref name="repoRoot"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the package cannot be read, when a file's destination path is invalid
    ///     (including resolving outside <paramref name="repoRoot"/>), when a managed folder cannot
    ///     be deleted, or when copying fails partway through. An invalid path is rejected before
    ///     any managed folder is deleted, so that failure mode never leaves the repo partially
    ///     upgraded; a delete or copy failure can still do so, and per the class remarks there is
    ///     no rollback for that case.
    /// </exception>
    public static void Extract(IPackageContentReader reader, string repoRoot, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(repoRoot);

        var effectiveLogger = logger ?? AppLogging.Factory.CreateLogger(LoggerCategoryName);

        if (effectiveLogger.IsEnabled(LogLevel.Debug))
        {
            effectiveLogger.LogDebug(
                "Extracting package '{Package}' into repo '{RepoRoot}'", reader.Description, repoRoot);
        }

        // Step 1: list and validate the package. If it lists without error, its contents are
        // assumed good per architecture.md - integrity/authenticity checks belong to the source.
        // Step 2: resolve and validate every file's destination path up front, before anything
        // is deleted. A malformed or path-traversing entry (one that fails SafePathCombine's
        // containment check) must reject the whole upgrade before any managed folder is touched -
        // otherwise a malicious/corrupt package could blind-delete the existing managed folders
        // and then fail partway through, leaving the repo unsynced while still reporting only an
        // "ordinary" failure to callers such as RepoCardViewModel's never-block-launch sync path.
        var managedFiles = new List<(string RelativePath, string DestinationPath)>();
        foreach (var relativePath in reader.ListFiles())
        {
            var destinationPath = ResolveManagedDestination(relativePath, repoRoot);
            if (destinationPath is not null)
            {
                managedFiles.Add((relativePath, destinationPath));
            }
        }

        // Step 3: blind-delete the four known folders (if present). Existing symbolic links or
        // junctions in the repo's layout are the repo owner's business and are deliberately not
        // inspected; only package-supplied paths are validated (step 2).
        foreach (var folder in ManagedFolders)
        {
            DeleteManagedFolder(repoRoot, folder, effectiveLogger);
        }

        // Step 4: copy the already-validated managed files, skipping any non-managed/root-level
        // files such as release-notes.md (those were never added to managedFiles above). A
        // failure here is reported to the caller (e.g. RepoCardViewModel, which already logs it
        // with repo/package context) rather than re-logged here, to avoid double-logging.
        try
        {
            foreach (var (relativePath, destinationPath) in managedFiles)
            {
                CopyFile(reader, relativePath, destinationPath, repoRoot);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Failed to extract package '{reader.Description}' into '{repoRoot}': {ex.Message}", ex);
        }

        if (effectiveLogger.IsEnabled(LogLevel.Information))
        {
            effectiveLogger.LogInformation(
                "Extracted {EntryCount} managed file(s) from package '{Package}' into repo '{RepoRoot}'",
                managedFiles.Count, reader.Description, repoRoot);
        }
    }

    /// <summary>
    ///     Determines whether all four managed agent folders currently exist under a repo root.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root to check.</param>
    /// <returns><see langword="true"/> if every managed folder exists under
    ///     <paramref name="repoRoot"/>; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    ///     Built on the same <see cref="ManagedFolders"/> array <see cref="Extract"/> itself uses,
    ///     so the managed-folder list has a single source of truth - callers never need to
    ///     duplicate it. Does not inspect folder contents; a managed folder that exists but is
    ///     empty still counts as "existing" here.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> is
    ///     <see langword="null"/>.</exception>
    public static bool AllManagedFoldersExist(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);

        return ManagedFolders.All(folder => Directory.Exists(PathHelpers.SafePathCombine(repoRoot, folder)));
    }

    /// <summary>
    ///     Reads the package's root-level <c>release-notes.md</c> without copying it to disk.
    /// </summary>
    /// <param name="reader">The package content to read from. Not disposed by this method.</param>
    /// <returns>The release notes text, or <see langword="null"/> if the package has none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the release notes cannot be
    ///     read.</exception>
    public static string? ReadReleaseNotes(IPackageContentReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.ReadText(ReleaseNotesEntryName);
    }

    /// <summary>
    ///     Reads the package's optional root-level <c>AGENTS.md</c> template without copying it to
    ///     disk.
    /// </summary>
    /// <param name="reader">The package content to read from. Not disposed by this method.</param>
    /// <returns>The template's text, or <see langword="null"/> if the package has none.</returns>
    /// <remarks>
    ///     Mirrors <see cref="ReadReleaseNotes"/>: the <c>AGENTS.md</c> file is a second optional
    ///     root-level file that <see cref="Extract"/> never copies. Callers decide whether to offer
    ///     writing this content to a repo's root, based on whether the repo already has an
    ///     <c>AGENTS.md</c> and whether the user previously declined the offer.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the template cannot be read.</exception>
    public static string? ReadAgentsMdTemplate(IPackageContentReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.ReadText(AgentsMdEntryName);
    }

    /// <summary>
    ///     Blind-deletes a single managed folder under a repo root, if it currently exists.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <param name="relativeFolder">The managed folder's path relative to the repo root.</param>
    /// <param name="logger">Logger for delete diagnostics, already resolved by the caller.</param>
    /// <exception cref="InvalidOperationException">Thrown when the folder exists but cannot be
    ///     deleted.</exception>
    private static void DeleteManagedFolder(string repoRoot, string relativeFolder, ILogger logger)
    {
        var folderPath = PathHelpers.SafePathCombine(repoRoot, relativeFolder);

        if (!Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            Directory.Delete(folderPath, recursive: true);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Deleted managed folder '{FolderPath}'", folderPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not re-logged here at Error level: the caller (Extract) wraps and rethrows this as
            // an InvalidOperationException, and its own caller (e.g. RepoCardViewModel) already
            // logs that failure with richer repo/package context.
            throw new InvalidOperationException($"Failed to delete folder '{folderPath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Resolves a single package file's destination path if it falls within one of the four
    ///     managed folders, validating that path but not yet writing anything to disk.
    /// </summary>
    /// <param name="relativePath">The file's package-relative path using <c>/</c> separators.</param>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <returns>
    ///     The file's absolute destination path if it falls within a managed folder; otherwise
    ///     <see langword="null"/> (a file that does not resolve inside any managed folder,
    ///     including root-level files like <c>release-notes.md</c>).
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when the path is invalid, including
    ///     resolving outside <paramref name="repoRoot"/>.</exception>
    private static string? ResolveManagedDestination(string relativePath, string repoRoot)
    {
        // Package paths always use '/' regardless of platform; normalize before combining.
        var nativeRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        // PathHelpers.SafePathCombine is the single source of truth for containment validation
        // (see its own doc remarks); it throws ArgumentException/NotSupportedException both when
        // the path would resolve outside repoRoot and for other malformed-path failures, so the
        // original message is preserved here rather than assuming every failure is a traversal.
        string destinationPath;
        try
        {
            destinationPath = PathHelpers.SafePathCombine(repoRoot, nativeRelativePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Package entry '{relativePath}' has an invalid destination path: {ex.Message}", ex);
        }

        // The managed-folder membership check must run against the *canonical* (".."-resolved)
        // relative path, not the raw package-supplied one: a crafted entry such as
        // ".github/agents/../../outside.txt" textually starts with a managed-folder prefix but
        // resolves elsewhere. Deriving the relative path from the already-validated
        // destinationPath closes that gap.
        var canonicalRelativePath = Path.GetRelativePath(Path.GetFullPath(repoRoot), destinationPath);
        return IsInsideManagedFolder(canonicalRelativePath) ? destinationPath : null;
    }

    /// <summary>
    ///     Copies a single package file to an already-validated destination path, creating any
    ///     missing parent directories first.
    /// </summary>
    /// <param name="reader">The package content to read from.</param>
    /// <param name="relativePath">The file's package-relative path.</param>
    /// <param name="destinationPath">The destination path, as previously resolved and validated by
    ///     <see cref="ResolveManagedDestination"/>.</param>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <exception cref="InvalidOperationException">Thrown when the destination is not inside
    ///     <paramref name="repoRoot"/> (defense in depth; already validated by
    ///     <see cref="ResolveManagedDestination"/>) or the package file cannot be read.</exception>
    private static void CopyFile(IPackageContentReader reader, string relativePath, string destinationPath, string repoRoot)
    {
        var fullRoot = Path.GetFullPath(repoRoot);
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        var fullDestination = Path.GetFullPath(destinationPath);
        if (!fullDestination.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Package entry '{relativePath}' resolves outside the repository root.");
        }

        var destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using var source = reader.OpenRead(relativePath);
        using var target = new FileStream(fullDestination, FileMode.Create, FileAccess.Write);
        source.CopyTo(target);
    }

    /// <summary>
    ///     Determines whether a package-relative path falls within one of the four managed folders.
    /// </summary>
    /// <param name="relativePath">A path relative to the repo root, using OS directory
    ///     separators.</param>
    /// <returns><see langword="true"/> if the path is inside a managed folder; otherwise
    ///     <see langword="false"/>.</returns>
    private static bool IsInsideManagedFolder(string relativePath) =>
        ManagedFolders.Any(folder =>
            relativePath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal));
}
