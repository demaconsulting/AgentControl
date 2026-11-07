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

using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     Zip-package entry point to the blind-delete-and-replace upgrade sequence for a repo's four
///     managed agent folders: a thin facade that opens a package zip and delegates to
///     <see cref="PackageContentExtractor"/>.
/// </summary>
/// <remarks>
///     Kept as the stable zip-file API (and the single entry point for checking whether the
///     managed folders exist) so existing callers and their requirements remain valid; every rule
///     - managed-folder list, path containment, delete-then-copy semantics, no rollback - lives in
///     <see cref="PackageContentExtractor"/> and is shared with the unpacked-folder packages used
///     by NuGet sources. Rewriting the <c>.agentcontrol.json</c> pin is the caller's
///     responsibility (see the <c>RepoConfig</c> subsystem). Stateless and thread-safe: every
///     member is a pure function of its parameters plus the filesystem.
/// </remarks>
internal static class PackageZipExtractor
{
    /// <summary>
    ///     Opens/validates the package zip, validates every entry's destination path, deletes the
    ///     four managed folders under <paramref name="repoRoot"/> if present, and extracts the
    ///     same four folders from the zip into the repo.
    /// </summary>
    /// <param name="zipPath">Path to the agent package zip file.</param>
    /// <param name="repoRoot">Absolute path to the repository root to sync.</param>
    /// <param name="logger">Logger for extraction diagnostics, or <see langword="null"/> to fall
    ///     back to the application logging factory (see
    ///     <see cref="PackageContentExtractor.Extract"/>).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipPath"/> or
    ///     <paramref name="repoRoot"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the zip cannot be opened/is not a valid zip archive, when an entry's
    ///     destination path is invalid (including resolving outside <paramref name="repoRoot"/>),
    ///     when a managed folder cannot be deleted, or when extraction fails partway through. An
    ///     invalid entry path is rejected before any managed folder is deleted; a delete or
    ///     extraction failure can leave the repo partially upgraded (there is no rollback).
    /// </exception>
    public static void Extract(string zipPath, string repoRoot, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(zipPath);
        ArgumentNullException.ThrowIfNull(repoRoot);

        using var reader = ZipPackageContentReader.Open(zipPath);
        PackageContentExtractor.Extract(reader, repoRoot, logger);
    }

    /// <summary>
    ///     Determines whether all four managed agent folders currently exist under a repo root.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root to check.</param>
    /// <returns><see langword="true"/> if every managed folder exists under
    ///     <paramref name="repoRoot"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> is
    ///     <see langword="null"/>.</exception>
    public static bool AllManagedFoldersExist(string repoRoot) =>
        PackageContentExtractor.AllManagedFoldersExist(repoRoot);

    /// <summary>
    ///     Reads the content of the package zip's root-level <c>release-notes.md</c> entry
    ///     without extracting it to disk.
    /// </summary>
    /// <param name="zipPath">Path to the agent package zip file.</param>
    /// <returns>The release notes text, or <see langword="null"/> if the zip has no root-level
    ///     <c>release-notes.md</c> entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipPath"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the zip cannot be opened or the
    ///     release notes entry cannot be read.</exception>
    public static string? ReadReleaseNotes(string zipPath)
    {
        ArgumentNullException.ThrowIfNull(zipPath);

        using var reader = ZipPackageContentReader.Open(zipPath);
        return PackageContentExtractor.ReadReleaseNotes(reader);
    }

    /// <summary>
    ///     Reads the content of the package zip's optional root-level <c>AGENTS.md</c> template
    ///     entry without extracting it to disk.
    /// </summary>
    /// <param name="zipPath">Path to the agent package zip file.</param>
    /// <returns>The template's text, or <see langword="null"/> if the zip has no root-level
    ///     <c>AGENTS.md</c> entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zipPath"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the zip cannot be opened or the
    ///     <c>AGENTS.md</c> entry cannot be read.</exception>
    public static string? ReadAgentsMdTemplate(string zipPath)
    {
        ArgumentNullException.ThrowIfNull(zipPath);

        using var reader = ZipPackageContentReader.Open(zipPath);
        return PackageContentExtractor.ReadAgentsMdTemplate(reader);
    }
}
