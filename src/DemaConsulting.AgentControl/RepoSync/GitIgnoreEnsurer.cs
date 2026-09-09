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
///     additive. Stateless and thread-safe: every member is a pure function of its parameter
///     plus the filesystem.
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
    ///     or removed.
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
            var existingContent = File.Exists(gitIgnorePath) ? File.ReadAllText(gitIgnorePath) : string.Empty;

            // Idempotency gate: a marker-comment scan only, never a gitignore-pattern/glob
            // analysis and never git check-ignore - see the class remarks for rationale.
            if (existingContent.Contains(MarkerComment, StringComparison.Ordinal))
            {
                return;
            }

            var newLine = DetectNewLine(existingContent);
            var newContent = existingContent + BuildSeparator(existingContent, newLine) + BuildManagedFoldersBlock(newLine);
            File.WriteAllText(gitIgnorePath, newContent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Failed to update '{gitIgnorePath}' with the managed agent-folder entries: {ex.Message}", ex);
        }
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
