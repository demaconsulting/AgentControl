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
/// <param name="Url">The absolute <c>http</c>/<c>https</c> address the run links to, or
///     <see langword="null"/> for ordinary text; only an address that passed
///     <see cref="ReleaseNotesViewerViewModel.IsOpenableUrl"/> is ever set.</param>
internal sealed record MarkdownRun(string Text, bool Bold, bool Italic, string? Url = null);

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
    ///     The deepest Markdown heading level (<c>######</c>) recognized as a heading.
    /// </summary>
    private const int MaxHeadingLevel = 6;

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
    ///     recognizing <c>#</c> through <c>######</c> (levels 1-6) headings, <c>- </c>/<c>* </c>
    ///     bullet items, and <c>**bold**</c>/<c>*italic*</c> inline emphasis. A line with more than
    ///     six leading <c>#</c> characters is plain text, per Markdown. Blank lines are skipped
    ///     rather than rendered as empty blocks.
    /// </summary>
    /// <param name="markdown">The raw Markdown text to parse.</param>
    /// <returns>The parsed blocks, in source order.</returns>
    private static List<MarkdownBlock> ParseBlocks(string markdown)
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
            if (headingLevel is > 0 and <= MaxHeadingLevel && headingLevel < line.Length && line[headingLevel] == ' ')
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
    ///     <c>**bold**</c> and <c>*italic*</c> spans and links (see <see cref="TryParseLink"/>).
    ///     Unterminated markers (no matching closing <c>*</c>/<c>**</c>) are treated as literal
    ///     text rather than emphasis.
    /// </summary>
    /// <param name="text">The line text (with any heading/bullet prefix already stripped).</param>
    /// <returns>The parsed inline runs, in source order.</returns>
    private static List<MarkdownRun> ParseInlineRuns(string text)
    {
        var runs = new List<MarkdownRun>();
        var plain = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            // A link is recognized before emphasis so that its text and address are never
            // split by an asterisk that happens to occur inside them
            if (TryParseLink(text, index, bold: false, italic: false, out var linkLength, out var linkRun))
            {
                FlushPlain(runs, plain, bold: false, italic: false);
                runs.Add(linkRun);
                index += linkLength;
                continue;
            }

            if (text[index] == '*' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var end = text.IndexOf("**", index + 2, StringComparison.Ordinal);
                if (end > index + 1)
                {
                    FlushPlain(runs, plain, bold: false, italic: false);
                    AppendSpan(runs, text[(index + 2)..end], bold: true, italic: false);
                    index = end + 2;
                    continue;
                }
            }
            else if (text[index] == '*')
            {
                var end = text.IndexOf('*', index + 1);
                if (end > index)
                {
                    FlushPlain(runs, plain, bold: false, italic: false);
                    AppendSpan(runs, text[(index + 1)..end], bold: false, italic: true);
                    index = end + 1;
                    continue;
                }
            }

            plain.Append(text[index]);
            index++;
        }

        FlushPlain(runs, plain, bold: false, italic: false);
        return runs;
    }

    /// <summary>
    ///     Appends the text of one emphasis span as runs carrying the span's emphasis, turning any
    ///     link inside it into a link run so <c>**[text](url)**</c> is a bold link rather than
    ///     literal brackets. Emphasis nested inside the span is not interpreted.
    /// </summary>
    /// <param name="runs">The runs being built; receives the span's runs.</param>
    /// <param name="text">The span's text, without its emphasis markers.</param>
    /// <param name="bold">Whether the span is bold.</param>
    /// <param name="italic">Whether the span is italic.</param>
    private static void AppendSpan(List<MarkdownRun> runs, string text, bool bold, bool italic)
    {
        var plain = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            if (TryParseLink(text, index, bold, italic, out var linkLength, out var linkRun))
            {
                FlushPlain(runs, plain, bold, italic);
                runs.Add(linkRun);
                index += linkLength;
                continue;
            }

            plain.Append(text[index]);
            index++;
        }

        FlushPlain(runs, plain, bold, italic);
    }

    /// <summary>
    ///     Appends <paramref name="plain"/>'s accumulated text as a run with the given emphasis,
    ///     if any, then clears it for the next span.
    /// </summary>
    private static void FlushPlain(List<MarkdownRun> runs, StringBuilder plain, bool bold, bool italic)
    {
        if (plain.Length == 0)
        {
            return;
        }

        runs.Add(new MarkdownRun(plain.ToString(), bold, italic));
        plain.Clear();
    }

    /// <summary>
    ///     Tells whether <paramref name="url"/> is an address the release-notes window may open:
    ///     an absolute <c>http://</c> or <c>https://</c> URL with a host and no whitespace or
    ///     control characters. Every other scheme (<c>file:</c>, <c>javascript:</c>,
    ///     <c>mailto:</c>, ...) and every relative address is refused.
    /// </summary>
    /// <remarks>
    ///     Release notes come from packages, which are untrusted content, so this is the single
    ///     gate deciding whether text becomes a link; the window checks it again just before
    ///     opening an address.
    /// </remarks>
    /// <param name="url">The address to check; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the address may be opened.</returns>
    internal static bool IsOpenableUrl(string? url)
    {
        // The scheme must be written out with its "//", so "https:host" and "https:/host" are refused
        if (string.IsNullOrEmpty(url) ||
            !(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (url.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return false;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>
    ///     Tries to read a link starting at <paramref name="index"/>: <c>[text](url)</c>,
    ///     <c>&lt;https://...&gt;</c> or a bare <c>https://...</c> address.
    /// </summary>
    /// <param name="text">The line text.</param>
    /// <param name="index">Where to look for the start of a link.</param>
    /// <param name="bold">Whether the surrounding text is bold; copied to the run.</param>
    /// <param name="italic">Whether the surrounding text is italic; copied to the run.</param>
    /// <param name="consumed">When this returns <see langword="true"/>, how many characters the
    ///     link occupies.</param>
    /// <param name="run">When this returns <see langword="true"/>, the run to show: a link run
    ///     for an openable address, otherwise (for <c>[text](url)</c> with another scheme or a
    ///     relative address) plain text of the form <c>text (url)</c>, so the target stays visible
    ///     but is never clickable.</param>
    /// <returns><see langword="true"/> when a well-formed link starts here; malformed brackets
    ///     return <see langword="false"/> and stay literal text.</returns>
    private static bool TryParseLink(
        string text, int index, bool bold, bool italic, out int consumed, out MarkdownRun run)
    {
        consumed = 0;
        run = null!;

        return text[index] switch
        {
            '[' => TryParseInlineLink(text, index, bold, italic, out consumed, out run),
            '<' => TryParseAngleLink(text, index, bold, italic, out consumed, out run),
            'h' or 'H' => TryParseBareLink(text, index, bold, italic, out consumed, out run),
            _ => false
        };
    }

    /// <summary>
    ///     Reads <c>[text](url)</c> (with an optional quoted title after the address, which is
    ///     ignored) starting at <paramref name="index"/>; parentheses inside the address must balance.
    /// </summary>
    /// <returns><see langword="true"/> when a well-formed inline link starts here.</returns>
    private static bool TryParseInlineLink(
        string text, int index, bool bold, bool italic, out int consumed, out MarkdownRun run)
    {
        consumed = 0;
        run = null!;

        // The text ends at the first "]", which must be followed directly by "("
        var close = text.IndexOf(']', index + 1);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
        {
            return false;
        }

        var label = text[(index + 1)..close];
        if (label.Contains('['))
        {
            return false;
        }

        // The address ends at the ")" that balances the "(" after the text
        var depth = 1;
        var position = close + 2;
        while (position < text.Length)
        {
            if (text[position] == '(')
            {
                depth++;
            }
            else if (text[position] == ')' && --depth == 0)
            {
                break;
            }

            position++;
        }

        if (depth != 0)
        {
            return false;
        }

        // Address, optionally followed by a quoted title; anything else with a space is not a link
        var target = text[(close + 2)..position].Trim();
        var space = target.IndexOfAny([' ', '\t']);
        if (space > 0)
        {
            var title = target[space..].Trim();
            if (title.Length < 2 || title[0] != '"' || title[^1] != '"')
            {
                return false;
            }

            target = target[..space];
        }

        if (target.Length == 0)
        {
            return false;
        }

        consumed = position - index + 1;
        if (label.Length == 0)
        {
            label = target;
        }

        run = IsOpenableUrl(target)
            ? new MarkdownRun(label, bold, italic, target)
            : new MarkdownRun($"{label} ({target})", bold, italic);
        return true;
    }

    /// <summary>
    ///     Reads <c>&lt;https://...&gt;</c> starting at <paramref name="index"/>; only an openable
    ///     address is a link, any other <c>&lt;...&gt;</c> stays literal text.
    /// </summary>
    /// <returns><see langword="true"/> when an angle-bracket autolink starts here.</returns>
    private static bool TryParseAngleLink(
        string text, int index, bool bold, bool italic, out int consumed, out MarkdownRun run)
    {
        consumed = 0;
        run = null!;

        var close = text.IndexOf('>', index + 1);
        if (close < 0)
        {
            return false;
        }

        var target = text[(index + 1)..close];
        if (!IsOpenableUrl(target))
        {
            return false;
        }

        consumed = close - index + 1;
        run = new MarkdownRun(target, bold, italic, target);
        return true;
    }

    /// <summary>
    ///     Reads a bare <c>http://</c> or <c>https://</c> address starting at
    ///     <paramref name="index"/>: it must start a word, runs to the next whitespace (or angle
    ///     bracket) and does not include trailing sentence punctuation or an unbalanced closing
    ///     parenthesis.
    /// </summary>
    /// <returns><see langword="true"/> when a bare autolink starts here.</returns>
    private static bool TryParseBareLink(
        string text, int index, bool bold, bool italic, out int consumed, out MarkdownRun run)
    {
        consumed = 0;
        run = null!;

        var rest = text.AsSpan(index);
        if ((index > 0 && char.IsLetterOrDigit(text[index - 1])) ||
            !(rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Find where the address stops, then give back trailing punctuation that belongs to the sentence
        var end = index;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '<' && text[end] != '>')
        {
            end++;
        }

        while (end > index && IsTrailingPunctuation(text, index, end))
        {
            end--;
        }

        var target = text[index..end];
        if (!IsOpenableUrl(target))
        {
            return false;
        }

        consumed = end - index;
        run = new MarkdownRun(target, bold, italic, target);
        return true;
    }

    /// <summary>
    ///     Tells whether the last character of the bare address <c>text[start..end]</c> is
    ///     punctuation that ends a sentence rather than part of the address: <c>. , ; : ! ? ' " *</c>,
    ///     or a <c>)</c> / <c>]</c> with no matching opener inside the address.
    /// </summary>
    private static bool IsTrailingPunctuation(string text, int start, int end)
    {
        var last = text[end - 1];
        if (last is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"' or '*')
        {
            return true;
        }

        // A closing bracket belongs to the sentence only when the address has no opener for it
        char opener;
        if (last == ')')
        {
            opener = '(';
        }
        else if (last == ']')
        {
            opener = '[';
        }
        else
        {
            return false;
        }

        var span = text.AsSpan(start, end - start);
        return span.Count(last) > span.Count(opener);
    }
}
