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

using System.Text;
using DemaConsulting.AgentControl.Utilities;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     Proactively ensures a repo's root <c>.gitignore</c> covers the four managed agent
///     folders, so an agentic tool (or a developer) is far less likely to accidentally commit
///     package-managed content into a repo that is meant to keep it untracked.
/// </summary>
/// <remarks>
///     Per the request that introduced this unit, idempotency is deliberately implemented as a
///     fixed marker-comment scan only - never <c>git check-ignore</c>, and never any kind of
///     gitignore-pattern/glob analysis. This keeps the check simple, dependency-free (no git
///     invocation at all), and predictable: if a developer manually removes the marker comment
///     but leaves the four managed-folder lines behind, a subsequent call adds a second copy
///     under a fresh marker. That duplicate-block outcome is an explicitly accepted edge case,
///     not a defect - precision was deliberately traded for simplicity. This class never edits,
///     reorders, or removes any pre-existing line in <c>.gitignore</c>: every change is purely
///     additive. Stateless, but <b>not</b> safe for concurrent calls against the same repo's
///     <c>.gitignore</c>: <see cref="Ensure"/> performs an unsynchronized read-modify-write, so
///     concurrent invocations can both pass the marker check before either writes, racing to
///     produce a duplicate block. Callers are expected to invoke this sequentially per repo (as
///     <c>RepoCardViewModel</c> does, immediately after a successful extraction completes).
/// </remarks>
internal static class GitIgnoreEnsurer
{
    /// <summary>
    ///     Fixed marker comment that identifies a previously-added managed-folders block. Its
    ///     presence anywhere in the file - not any particular line/pattern - is the sole
    ///     idempotency gate for <see cref="Ensure"/>.
    /// </summary>
    private const string MarkerComment = "# Added by AgentControl - agent package folders";

    /// <summary>
    ///     The four managed agent folders (relative to a repo root, using gitignore's forward-
    ///     slash directory convention) that are added to <c>.gitignore</c> alongside the marker
    ///     comment.
    /// </summary>
    private static readonly string[] ManagedFolderPatterns =
    [
        ".github/agents/",
        ".github/standards/",
        ".github/templates/",
        ".github/skills/"
    ];

    /// <summary>
    ///     Ensures the repo's root <c>.gitignore</c> contains a marker-delimited block covering
    ///     the four managed agent folders, creating the file if it does not yet exist.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repository root.</param>
    /// <remarks>
    ///     If <see cref="MarkerComment"/> is already present anywhere in the existing file, this
    ///     does nothing at all - no write, no duplicate block. Otherwise, the marker comment and
    ///     the four managed-folder lines are appended to the end of the file (creating it first
    ///     if absent), preceded by a single blank-line separator only when the existing content
    ///     is non-empty and does not already end in a blank line, so the appended block never
    ///     visually runs into existing content. No pre-existing line is ever edited, reordered,
    ///     or removed. The updated content is written atomically (via a temp file in the same
    ///     directory, then an atomic replace/move) so a mid-write failure (disk full, crash,
    ///     etc.) can never leave a truncated or corrupted <c>.gitignore</c> behind.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoRoot"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the <c>.gitignore</c> file cannot
    ///     be read or written for an I/O reason.</exception>
    public static void Ensure(string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);

        var gitIgnorePath = PathHelpers.SafePathCombine(repoRoot, ".gitignore");

        try
        {
            var (existingContent, encoding) = ReadExistingFile(gitIgnorePath);

            // Idempotency gate: a marker-comment scan only, never a gitignore-pattern/glob
            // analysis and never git check-ignore - see the class remarks for rationale.
            if (existingContent.Contains(MarkerComment, StringComparison.Ordinal))
            {
                return;
            }

            var newLine = DetectNewLine(existingContent);
            var newContent = existingContent + BuildSeparator(existingContent, newLine) + BuildManagedFoldersBlock(newLine);
            WriteAtomic(gitIgnorePath, newContent, encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Failed to update '{gitIgnorePath}' with the managed agent-folder entries: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Writes <paramref name="content"/> to <paramref name="path"/> atomically, so a
    ///     mid-write failure can never leave a truncated or partially-written file in place of a
    ///     pre-existing, user-owned <c>.gitignore</c>.
    /// </summary>
    /// <param name="path">Absolute path to the file to write.</param>
    /// <param name="content">The full content to write.</param>
    /// <param name="encoding">The encoding to write with.</param>
    /// <remarks>
    ///     Writes to a uniquely-named temporary file in the same directory as
    ///     <paramref name="path"/> (so the subsequent replace/move is a same-volume, effectively
    ///     atomic rename rather than a copy), then atomically replaces the destination with
    ///     <see cref="File.Replace(string, string, string?)"/> if it already exists, or moves the
    ///     temp file into place with <see cref="File.Move(string, string)"/> if it does not. The
    ///     temp file is best-effort deleted if anything goes wrong before the swap completes.
    /// </remarks>
    private static void WriteAtomic(string path, string content, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var tempPath = Path.Combine(directory, $".gitignore.agentcontrol-tmp-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(tempPath, content, encoding);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // Best-effort cleanup only - the original exception is what matters here.
                }
            }

            throw;
        }
    }

    /// <summary>
    ///     Reads a possibly-missing file's existing content and encoding, so
    ///     <see cref="Ensure"/> can write the updated content back using the same encoding the
    ///     file already had, rather than silently normalizing it to UTF-8 as a side effect of a
    ///     purely additive update.
    /// </summary>
    /// <param name="path">Absolute path to the file.</param>
    /// <returns>The file's text content and detected encoding, or an empty string and UTF-8
    ///     (no BOM) - the conventional default for a newly created <c>.gitignore</c> - when the
    ///     file does not yet exist.</returns>
    /// <remarks>
    ///     Encoding detection relies solely on a byte-order mark (BOM), since that is the only
    ///     encoding signal <see cref="StreamReader"/> can reliably determine from file content
    ///     without an explicit hint. A legacy non-UTF8, non-BOM encoding (e.g. plain Windows-1252)
    ///     is not detected as such; such a file is read (and subsequently written back) as UTF-8,
    ///     which is safe for the ASCII-range gitignore syntax this unit appends but does not
    ///     preserve the original byte-level encoding of any pre-existing non-ASCII content.
    /// </remarks>
    private static (string Content, Encoding Encoding) ReadExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return (string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return (content, reader.CurrentEncoding);
    }

    /// <summary>
    ///     Detects the newline style already used by <paramref name="existingContent"/>, so
    ///     appended lines match the file's existing convention instead of always using the
    ///     current OS's <see cref="Environment.NewLine"/> (which would produce mixed line
    ///     endings, e.g. appending LF-only lines to a CRLF file when running on Linux/macOS).
    /// </summary>
    /// <param name="existingContent">The <c>.gitignore</c> file's content before appending.</param>
    /// <returns><c>"\r\n"</c> or <c>"\n"</c> when detected from the first line break in
    ///     <paramref name="existingContent"/>; otherwise <see cref="Environment.NewLine"/> for a
    ///     new or line-break-free file.</returns>
    private static string DetectNewLine(string existingContent)
    {
        var index = existingContent.IndexOf('\n');
        if (index < 0)
        {
            return Environment.NewLine;
        }

        return index > 0 && existingContent[index - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>
    ///     Builds a sensible, simple blank-line separator to place before the appended block, so
    ///     it never visually runs into any pre-existing content.
    /// </summary>
    /// <param name="existingContent">The <c>.gitignore</c> file's content before appending.</param>
    /// <param name="newLine">The newline style to use, as detected by
    ///     <see cref="DetectNewLine"/>.</param>
    /// <returns>An empty string when <paramref name="existingContent"/> is empty or already ends
    ///     in a blank line; otherwise a newline pair providing one blank-line separator.</returns>
    private static string BuildSeparator(string existingContent, string newLine)
    {
        if (existingContent.Length == 0)
        {
            return string.Empty;
        }

        // Normalize CRLF to LF purely for this ends-with check, so mixed line-ending files
        // (e.g. a trailing "\n\r\n") are still recognized as already ending in a blank line -
        // a "sensible, simple separator" per the request, not a full line-ending analysis.
        var normalized = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);

        // A "blank line at the end" means the content already ends in two consecutive line
        // breaks - in that case no further separator is needed. A single trailing line break
        // only terminates the last line of content, so one more line break is added to form the
        // blank-line separator. No trailing line break at all needs a line break to terminate
        // the current line plus a blank-line separator.
        if (normalized.EndsWith("\n\n", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalized.EndsWith('\n') ? newLine : newLine + newLine;
    }

    /// <summary>
    ///     Builds the marker comment plus the four managed-folder lines to append.
    /// </summary>
    /// <param name="newLine">The newline style to use, as detected by
    ///     <see cref="DetectNewLine"/>.</param>
    /// <returns>The marker-delimited managed-folders block text.</returns>
    private static string BuildManagedFoldersBlock(string newLine) =>
        MarkerComment + newLine + string.Join(newLine, ManagedFolderPatterns) + newLine;
}
