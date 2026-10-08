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
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     A single inline run of release-notes text, carrying the bold/italic emphasis (if any)
///     parsed from <c>**bold**</c>/<c>*italic*</c> Markdown syntax.
/// </summary>
/// <param name="Text">The run's literal text, with emphasis markers already stripped.</param>
/// <param name="Bold">Whether the run was wrapped in <c>**double asterisks**</c>.</param>
/// <param name="Italic">Whether the run was wrapped in <c>*single asterisks*</c>.</param>
internal sealed record MarkdownRun(string Text, bool Bold, bool Italic);

/// <summary>
///     A single rendered line of release notes: either a heading, a bullet-list item, or a plain
///     paragraph line, made up of one or more <see cref="MarkdownRun"/>s.
/// </summary>
/// <param name="Runs">The line's inline runs, in display order.</param>
/// <param name="HeadingLevel">The Markdown heading level (1 for <c>#</c>, 2 for <c>##</c>, etc.),
///     or 0 if the line is not a heading.</param>
/// <param name="IsBullet">Whether the line is a <c>- </c>/<c>* </c> bullet-list item.</param>
internal sealed record MarkdownBlock(IReadOnlyList<MarkdownRun> Runs, int HeadingLevel, bool IsBullet);

/// <summary>
///     View model for <see cref="ReleaseNotesViewer"/>: holds the window title and the release
///     notes text (both as raw text and as parsed <see cref="MarkdownBlock"/>s) to display after
///     a successful package upgrade.
/// </summary>
/// <remarks>
///     <c>PackageZipExtractor.ReadReleaseNotes</c> already does the only I/O (reading the zip
///     entry); this view model's own job is the lightweight line-by-line Markdown parsing in
///     <see cref="ParseBlocks"/> that turns that raw text into headings/bold/italic/bullet runs
///     for <see cref="ReleaseNotesViewer"/> to render - not a full CommonMark implementation,
///     just the handful of constructs release notes actually use. Immutable and thread-safe once
///     constructed.
/// </remarks>
internal sealed class ReleaseNotesViewerViewModel : ViewModelBase
{
    /// <summary>
    ///     Initializes a new <see cref="ReleaseNotesViewerViewModel"/>.
    /// </summary>
    /// <param name="repoName">The repo's display name, used to build <see cref="Title"/>.</param>
    /// <param name="releaseNotes">The release notes text read from the upgraded package's
    ///     <c>release-notes.md</c>, or an empty string if the package had none.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoName"/> or
    ///     <paramref name="releaseNotes"/> is <see langword="null"/>.</exception>
    public ReleaseNotesViewerViewModel(string repoName, string releaseNotes)
    {
        ArgumentNullException.ThrowIfNull(repoName);
        ArgumentNullException.ThrowIfNull(releaseNotes);

        Title = $"Release Notes - {repoName}";
        ReleaseNotes = string.IsNullOrEmpty(releaseNotes) ? "(This package has no release notes.)" : releaseNotes;
        Blocks = ParseBlocks(ReleaseNotes);
    }

    /// <summary>
    ///     Gets the window title, identifying which repo's upgrade produced these release notes.
    /// </summary>
    public string Title { get; }

    /// <summary>
    ///     Gets the raw release notes text (unparsed), kept for callers/tests that only need the
    ///     original content rather than its parsed rendering.
    /// </summary>
    public string ReleaseNotes { get; }

    /// <summary>
    ///     Gets the release notes parsed into renderable <see cref="MarkdownBlock"/>s (headings,
    ///     bullet items, and plain paragraph lines with bold/italic runs), one per non-blank
    ///     source line.
    /// </summary>
    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    /// <summary>
    ///     Parses release-notes Markdown text into a sequence of <see cref="MarkdownBlock"/>s,
    ///     recognizing <c>#</c>/<c>##</c>/<c>###</c> headings, <c>- </c>/<c>* </c> bullet items,
    ///     and <c>**bold**</c>/<c>*italic*</c> inline emphasis. Blank lines are skipped rather
    ///     than rendered as empty blocks.
    /// </summary>
    /// <param name="markdown">The raw Markdown text to parse.</param>
    /// <returns>The parsed blocks, in source order.</returns>
    private static IReadOnlyList<MarkdownBlock> ParseBlocks(string markdown)
    {
        var blocks = new List<MarkdownBlock>();

        foreach (var rawLine in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine;
            var headingLevel = 0;
            while (headingLevel < line.Length && line[headingLevel] == '#')
            {
                headingLevel++;
            }

            var isBullet = false;
            if (headingLevel > 0 && headingLevel < line.Length && line[headingLevel] == ' ')
            {
                line = line[(headingLevel + 1)..];
            }
            else
            {
                headingLevel = 0;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                    trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    isBullet = true;
                    line = trimmed[2..];
                }
            }

            blocks.Add(new MarkdownBlock(ParseInlineRuns(line), headingLevel, isBullet));
        }

        return blocks;
    }

    /// <summary>
    ///     Parses a single line's text into <see cref="MarkdownRun"/>s, recognizing
    ///     <c>**bold**</c> and <c>*italic*</c> spans. Unterminated markers (no matching closing
    ///     <c>*</c>/<c>**</c>) are treated as literal text rather than emphasis.
    /// </summary>
    /// <param name="text">The line text (with any heading/bullet prefix already stripped).</param>
    /// <returns>The parsed inline runs, in source order.</returns>
    private static IReadOnlyList<MarkdownRun> ParseInlineRuns(string text)
    {
        var runs = new List<MarkdownRun>();
        var plain = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '*' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var end = text.IndexOf("**", index + 2, StringComparison.Ordinal);
                if (end > index + 1)
                {
                    FlushPlain(runs, plain);
                    runs.Add(new MarkdownRun(text[(index + 2)..end], Bold: true, Italic: false));
                    index = end + 2;
                    continue;
                }
            }
            else if (text[index] == '*')
            {
                var end = text.IndexOf('*', index + 1);
                if (end > index)
                {
                    FlushPlain(runs, plain);
                    runs.Add(new MarkdownRun(text[(index + 1)..end], Bold: false, Italic: true));
                    index = end + 1;
                    continue;
                }
            }

            plain.Append(text[index]);
            index++;
        }

        FlushPlain(runs, plain);
        return runs;
    }

    /// <summary>
    ///     Appends <paramref name="plain"/>'s accumulated text as a plain (non-bold, non-italic)
    ///     run, if any, then clears it for the next span.
    /// </summary>
    private static void FlushPlain(List<MarkdownRun> runs, StringBuilder plain)
    {
        if (plain.Length == 0)
        {
            return;
        }

        runs.Add(new MarkdownRun(plain.ToString(), Bold: false, Italic: false));
        plain.Clear();
    }
}
