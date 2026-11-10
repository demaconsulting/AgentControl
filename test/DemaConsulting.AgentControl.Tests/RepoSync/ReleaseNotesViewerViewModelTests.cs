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

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Tests.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.RepoSync;

/// <summary>
///     Unit tests for <see cref="ReleaseNotesViewerViewModel"/>.
/// </summary>
public class ReleaseNotesViewerViewModelTests
{
    /// <summary>
    ///     Test that the constructor builds a title including the repo name and exposes the
    ///     release notes text unchanged when non-empty.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Constructor_NonEmptyReleaseNotes_ExposesTitleAndContent()
    {
        // Act: construct with a repo name and release notes text
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", "## 2.0.0\n\nBug fixes.");

        // Assert: the title mentions the repo, and the content is unchanged
        Assert.Contains("my-repo", viewModel.Title, StringComparison.Ordinal);
        Assert.Equal("## 2.0.0\n\nBug fixes.", viewModel.ReleaseNotes);
    }

    /// <summary>
    ///     Test that an empty release notes string is replaced with a friendly placeholder
    ///     message rather than shown as a blank window.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Constructor_EmptyReleaseNotes_UsesPlaceholderMessage()
    {
        // Act: construct with an empty release notes string (package had no release-notes.md)
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", string.Empty);

        // Assert: a friendly placeholder is shown instead of a blank window
        Assert.Contains("no release notes", viewModel.ReleaseNotes, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Test that a null repo name throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Constructor_NullRepoName_ThrowsArgumentNullException()
    {
        // Act / Assert: a null repo name is rejected
        Assert.Throws<ArgumentNullException>(() => new ReleaseNotesViewerViewModel(null!, "notes"));
    }

    /// <summary>
    ///     Test that a null release notes string throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Constructor_NullReleaseNotes_ThrowsArgumentNullException()
    {
        // Act / Assert: a null release notes string is rejected
        Assert.Throws<ArgumentNullException>(() => new ReleaseNotesViewerViewModel("my-repo", null!));
    }

    /// <summary>
    ///     Test that a heading line (<c>##</c>) parses into a block with the correct heading
    ///     level and its text stripped of the marker.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_HeadingLine_ParsesHeadingLevelAndText()
    {
        // Act: construct with a level-2 heading
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", "## 0.1.0");

        // Assert: one block, heading level 2, single plain run with the heading text
        var block = Assert.Single(viewModel.Blocks);
        Assert.Equal(2, block.HeadingLevel);
        Assert.False(block.IsBullet);
        var run = Assert.Single(block.Runs);
        Assert.Equal("0.1.0", run.Text);
        Assert.False(run.Bold);
        Assert.False(run.Italic);
    }

    /// <summary>
    ///     Test that headings up to level 6 are recognized, and that a line with more than six
    ///     leading hashes is kept as plain text, including its hashes.
    /// </summary>
    [Theory]
    [InlineData("#### Fixed", 4, "Fixed")]
    [InlineData("###### Deep", 6, "Deep")]
    [InlineData("####### Too deep", 0, "####### Too deep")]
    public void ReleaseNotesViewerViewModel_Blocks_HeadingDepth_RecognizesOnlyLevelsOneToSix(
        string markdown,
        int expectedLevel,
        string expectedText)
    {
        // Act
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", markdown);

        // Assert
        var block = Assert.Single(viewModel.Blocks);
        Assert.Equal(expectedLevel, block.HeadingLevel);
        Assert.Equal(expectedText, Assert.Single(block.Runs).Text);
    }

    /// <summary>
    ///     Test that a bullet list line (<c>- </c>) parses into a block flagged as a bullet, with
    ///     the marker stripped from its text.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_BulletLine_ParsesAsBulletWithoutMarker()
    {
        // Act: construct with a bullet list item
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", "- First item");

        // Assert: one block, flagged as a bullet, text has the "- " prefix stripped
        var block = Assert.Single(viewModel.Blocks);
        Assert.Equal(0, block.HeadingLevel);
        Assert.True(block.IsBullet);
        Assert.Equal("First item", Assert.Single(block.Runs).Text);
    }

    /// <summary>
    ///     Test that <c>**bold**</c> and <c>*italic*</c> inline spans parse into separate runs
    ///     with the correct emphasis flags, surrounded by plain-text runs.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_BoldAndItalicSpans_ParseIntoSeparateRuns()
    {
        // Act: construct with a line mixing plain, bold, and italic spans
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", "Plain **bold** and *italic* text.");

        // Assert: five runs alternating plain/bold/plain/italic/plain
        var block = Assert.Single(viewModel.Blocks);
        Assert.Equal(5, block.Runs.Count);
        Assert.Equal("Plain ", block.Runs[0].Text);
        Assert.False(block.Runs[0].Bold);
        Assert.Equal("bold", block.Runs[1].Text);
        Assert.True(block.Runs[1].Bold);
        Assert.Equal(" and ", block.Runs[2].Text);
        Assert.Equal("italic", block.Runs[3].Text);
        Assert.True(block.Runs[3].Italic);
        Assert.Equal(" text.", block.Runs[4].Text);
    }

    /// <summary>
    ///     Test that blank lines between content lines are skipped entirely rather than rendered
    ///     as empty blocks.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_BlankLines_AreSkipped()
    {
        // Act: construct with blank lines separating two headings
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", "# Release Notes\n\n## 0.1.0\n");

        // Assert: only the two non-blank lines produced blocks
        Assert.Equal(2, viewModel.Blocks.Count);
        Assert.Equal(1, viewModel.Blocks[0].HeadingLevel);
        Assert.Equal(2, viewModel.Blocks[1].HeadingLevel);
    }

    /// <summary>
    ///     Test that an unterminated <c>*</c> marker (no matching closing asterisk) is treated as
    ///     literal text rather than emphasis.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_UnterminatedAsterisk_TreatedAsLiteralText()
    {
        // Act: construct with a lone, unmatched asterisk
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", "Price: $5 * 2 = $10");

        // Assert: a single plain run containing the literal text unchanged
        var block = Assert.Single(viewModel.Blocks);
        var run = Assert.Single(block.Runs);
        Assert.Equal("Price: $5 * 2 = $10", run.Text);
        Assert.False(run.Bold);
        Assert.False(run.Italic);
    }

    /// <summary>
    ///     Test that <see cref="ReleaseNotesViewer.BuildBlockTextBlock"/> renders every inline run
    ///     of a heading block as bold, even runs that weren't themselves marked
    ///     <c>**bold**</c> in the source Markdown, so the heading's visual weight matches its
    ///     larger font size.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_BuildBlockTextBlock_HeadingBlock_RendersAllRunsBold()
    {
        // Arrange: a level-1 heading with a plain run and an explicitly-italic run
        var block = new MarkdownBlock(
            [new MarkdownRun("Release ", false, false), new MarkdownRun("1.0.0", false, true)],
            HeadingLevel: 1,
            IsBullet: false);

        // Act: build the rendered TextBlock
        var textBlock = ReleaseNotesViewer.BuildBlockTextBlock(block);

        // Assert: both runs are rendered bold, matching the heading's bold TextBlock-level style
        var inlines = Assert.IsType<InlineCollection>(textBlock.Inlines, exactMatch: false);
        Assert.All(inlines.OfType<Run>(), run => Assert.Equal(FontWeight.Bold, run.FontWeight));
    }

    /// <summary>
    ///     Test that <see cref="ReleaseNotesViewer.BuildBlockTextBlock"/> only bolds runs
    ///     explicitly marked <c>**bold**</c> for non-heading (body) blocks.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_BuildBlockTextBlock_BodyBlock_OnlyBoldsMarkedRuns()
    {
        // Arrange: a non-heading block with one plain run and one bold run
        var block = new MarkdownBlock(
            [new MarkdownRun("Plain ", false, false), new MarkdownRun("bold", true, false)],
            HeadingLevel: 0,
            IsBullet: false);

        // Act: build the rendered TextBlock
        var textBlock = ReleaseNotesViewer.BuildBlockTextBlock(block);

        // Assert: only the explicitly-bold run is rendered bold
        var runs = textBlock.Inlines?.OfType<Run>().ToList();
        Assert.NotNull(runs);
        Assert.Equal(FontWeight.Normal, runs[0].FontWeight);
        Assert.Equal(FontWeight.Bold, runs[1].FontWeight);
    }

    /// <summary>
    ///     Test that <c>`code`</c> spans parse into code runs without their backticks, and that
    ///     asterisks and link syntax inside a code span stay literal.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_CodeSpan_ParsesIntoLiteralCodeRun()
    {
        // Act: a line with a code span, and one whose code span holds emphasis and link syntax
        var viewModel = new ReleaseNotesViewerViewModel(
            "my-repo",
            "Initial release of the `contoso-agents` package.\n**Bold `a*b` code** and `[x](https://example.com)`");

        // Assert: the backticks are gone and the code text is one literal code run
        var first = viewModel.Blocks[0].Runs;
        Assert.Equal(3, first.Count);
        Assert.Equal("Initial release of the ", first[0].Text);
        Assert.False(first[0].Code);
        Assert.Equal("contoso-agents", first[1].Text);
        Assert.True(first[1].Code);
        Assert.Equal(" package.", first[2].Text);

        var second = viewModel.Blocks[1].Runs;
        var boldCode = Assert.Single(second, run => run.Code && run.Bold);
        Assert.Equal("a*b", boldCode.Text);
        var linkCode = Assert.Single(second, run => run.Code && !run.Bold);
        Assert.Equal("[x](https://example.com)", linkCode.Text);
        Assert.Null(linkCode.Url);
    }

    /// <summary>
    ///     Test that a backtick with no closing backtick, or an empty pair, stays literal text.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_UnterminatedOrEmptyBackticks_StayLiteral()
    {
        // Act
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", "Use `` or a ` alone.");

        // Assert: one plain run holding the original text
        var run = Assert.Single(Assert.Single(viewModel.Blocks).Runs);
        Assert.Equal("Use `` or a ` alone.", run.Text);
        Assert.False(run.Code);
    }

    /// <summary>
    ///     Test that a code run renders in a monospace font, and other runs keep the default font.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_BuildBlockTextBlock_CodeRun_RendersMonospace() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var block = new MarkdownBlock(
            [new MarkdownRun("Install ", false, false), new MarkdownRun("contoso-agents", false, false, Code: true)],
            HeadingLevel: 0,
            IsBullet: false);

        // Act
        var textBlock = ReleaseNotesViewer.BuildBlockTextBlock(block);

        // Assert
        var runs = textBlock.Inlines!.OfType<Run>().ToList();
        Assert.Equal("contoso-agents", runs[1].Text);
        Assert.Contains("monospace", runs[1].FontFamily.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("monospace", runs[0].FontFamily.ToString(), StringComparison.Ordinal);
    });

    /// <summary>
    ///     Test that <c>[text](url)</c> with an https or http address parses into a link run carrying
    ///     the link text and the exact address, between the surrounding plain text.
    /// </summary>
    /// <param name="url">The address in the Markdown link.</param>
    [Theory]
    [InlineData("https://example.com/releases/1.2.3")]
    [InlineData("http://example.com/path?query=1&other=2#frag")]
    [InlineData("HTTPS://Example.com/Upper")]
    [InlineData("https://en.wikipedia.org/wiki/Foo_(bar)")]
    public void ReleaseNotesViewerViewModel_Blocks_InlineLink_ParsesIntoLinkRun(string url)
    {
        // Act
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", $"See [the notes]({url}) for details.");

        // Assert: plain, link, plain
        var block = Assert.Single(viewModel.Blocks);
        Assert.Equal(3, block.Runs.Count);
        Assert.Equal("See ", block.Runs[0].Text);
        Assert.Null(block.Runs[0].Url);
        Assert.Equal("the notes", block.Runs[1].Text);
        Assert.Equal(url, block.Runs[1].Url);
        Assert.Equal(" for details.", block.Runs[2].Text);
        Assert.Null(block.Runs[2].Url);
    }

    /// <summary>
    ///     Test that a link inside a bullet item, inside bold text and with a quoted title all become
    ///     links, so links work in list items and paragraphs.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_LinkInBulletBoldAndWithTitle_AreLinks()
    {
        // Act
        var viewModel = new ReleaseNotesViewerViewModel(
            "my-repo",
            "- Fixed [#12](https://example.com/12)\n**[bold link](https://example.com/b)**\n[titled](https://example.com/t \"A title\")");

        // Assert
        Assert.True(viewModel.Blocks[0].IsBullet);
        Assert.Equal("https://example.com/12", Assert.Single(viewModel.Blocks[0].Runs, run => run.Url is not null).Url);
        var bold = Assert.Single(viewModel.Blocks[1].Runs);
        Assert.True(bold.Bold);
        Assert.Equal("https://example.com/b", bold.Url);
        var titled = Assert.Single(viewModel.Blocks[2].Runs);
        Assert.Equal("titled", titled.Text);
        Assert.Equal("https://example.com/t", titled.Url);
    }

    /// <summary>
    ///     Test that a link whose address is not an absolute http/https URL is never a link: it is
    ///     shown as plain text with its target visible, and no run carries an address.
    /// </summary>
    /// <param name="target">The link target in the Markdown.</param>
    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("CHANGELOG.md")]
    [InlineData("../docs/readme.md")]
    [InlineData("//example.com/protocol-relative")]
    [InlineData("https:example.com")]
    [InlineData("https://")]
    public void ReleaseNotesViewerViewModel_Blocks_LinkWithOtherSchemeOrRelativeTarget_IsPlainText(string target)
    {
        // Act
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", $"Read [docs]({target}) now");

        // Assert: nothing is a link, and the target stays visible
        var block = Assert.Single(viewModel.Blocks);
        Assert.All(block.Runs, run => Assert.Null(run.Url));
        Assert.Equal($"Read docs ({target}) now", string.Concat(block.Runs.Select(run => run.Text)));
    }

    /// <summary>
    ///     Test that malformed link syntax stays literal text.
    /// </summary>
    /// <param name="markdown">The malformed text.</param>
    [Theory]
    [InlineData("[text](https://example.com")]
    [InlineData("[text] (https://example.com)")]
    [InlineData("[text]https://example.com")]
    [InlineData("[text(https://example.com)]")]
    [InlineData("[a [b]](https://example.com)")]
    [InlineData("[text]()")]
    [InlineData("[text](https://example.com some words)")]
    [InlineData("just [brackets] and (parens)")]
    public void ReleaseNotesViewerViewModel_Blocks_MalformedLink_StaysLiteralText(string markdown)
    {
        // Act
        var viewModel = new ReleaseNotesViewerViewModel("my-repo", markdown);

        // Assert: no link run, and the text is exactly what was written (a bare address inside it
        // may become a link, but the visible text is unchanged)
        var block = Assert.Single(viewModel.Blocks);
        Assert.Equal(markdown, string.Concat(block.Runs.Select(run => run.Text)));
        Assert.DoesNotContain(block.Runs, run => run.Url is not null && run.Text != run.Url);
    }

    /// <summary>
    ///     Test that a bare https/http address and an angle-bracket autolink become links, without
    ///     the sentence punctuation that follows a bare address, and that other angle-bracket text
    ///     stays literal.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewerViewModel_Blocks_BareAndAngleAddresses_BecomeLinksWithoutTrailingPunctuation()
    {
        // Act
        var viewModel = new ReleaseNotesViewerViewModel(
            "my-repo",
            "Visit https://example.com/a, or (https://example.com/b) or <https://example.com/c> or <mailto:x@y.z> or xhttps://nope.example.");

        // Assert
        var block = Assert.Single(viewModel.Blocks);
        var urls = block.Runs.Where(run => run.Url is not null).Select(run => run.Url).ToList();
        Assert.Equal(["https://example.com/a", "https://example.com/b", "https://example.com/c"], urls);
        Assert.Equal(
            "Visit https://example.com/a, or (https://example.com/b) or https://example.com/c or <mailto:x@y.z> or xhttps://nope.example.",
            string.Concat(block.Runs.Select(run => run.Text)));
    }

    /// <summary>
    ///     Test that only absolute http/https addresses without whitespace are openable.
    /// </summary>
    /// <param name="url">The address to check.</param>
    /// <param name="expected">Whether it may be opened.</param>
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/x", true)]
    [InlineData("HTTP://EXAMPLE.COM", true)]
    [InlineData("file:///C:/x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("mailto:a@b.c", false)]
    [InlineData("/relative", false)]
    [InlineData("example.com", false)]
    [InlineData("https://example.com/a b", false)]
    [InlineData("https://example.com/\u0001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReleaseNotesViewerViewModel_IsOpenableUrl_AllowsOnlyAbsoluteHttpAndHttps(string? url, bool expected)
    {
        // Act / Assert
        Assert.Equal(expected, ReleaseNotesViewerViewModel.IsOpenableUrl(url));
    }

    /// <summary>
    ///     Test that a link run renders as a focusable button styled as a link whose tooltip is the full
    ///     address, inside the paragraph next to ordinary text.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_BuildBlockTextBlock_LinkRun_RendersFocusableLinkButtonWithAddressTooltip() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var block = new MarkdownBlock(
            [new MarkdownRun("See ", false, false), new MarkdownRun("the notes", false, false, "https://example.com/n")],
            HeadingLevel: 0,
            IsBullet: true);

        // Act
        var textBlock = ReleaseNotesViewer.BuildBlockTextBlock(block, _ => Task.CompletedTask);

        // Assert: bullet, text, then the link
        var inlines = textBlock.Inlines!;
        Assert.Equal(3, inlines.Count);
        var button = Assert.IsType<Button>(Assert.IsType<InlineUIContainer>(inlines[2]).Child);
        Assert.Contains("link", button.Classes);
        Assert.True(button.Focusable);
        Assert.True(button.IsTabStop);
        Assert.Equal("https://example.com/n", ToolTip.GetTip(button)?.ToString());
        Assert.Equal("the notes", Avalonia.Automation.AutomationProperties.GetName(button));
    });

    /// <summary>
    ///     Test that a link run is plain text when nothing handles clicks, or when its address is not an
    ///     openable http/https URL, so nothing clickable can open anything else.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_BuildBlockTextBlock_LinkRunWithoutHandlerOrWithUnsafeAddress_RendersPlainText() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var safe = new MarkdownBlock([new MarkdownRun("notes", false, false, "https://example.com/n")], 0, false);
        var unsafeBlock = new MarkdownBlock([new MarkdownRun("notes", false, false, "javascript:alert(1)")], 0, false);

        // Act
        var withoutHandler = ReleaseNotesViewer.BuildBlockTextBlock(safe);
        var withUnsafeAddress = ReleaseNotesViewer.BuildBlockTextBlock(unsafeBlock, _ => Task.CompletedTask);

        // Assert
        Assert.Equal("notes", Assert.IsType<Run>(Assert.Single(withoutHandler.Inlines!)).Text);
        Assert.Equal("notes", Assert.IsType<Run>(Assert.Single(withUnsafeAddress.Inlines!)).Text);
    });

    /// <summary>
    ///     Test that clicking a rendered link hands its exact address to the opener and nothing else
    ///     opens, and that a rendered non-http link has no button at all.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_LinkClick_InvokesOpenerWithExactUrl_NonHttpLinkIsNotClickable() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var opened = new List<string>();
        var viewModel = new ReleaseNotesViewerViewModel(
            "my-repo",
            "- [good](https://example.com/a?b=1&c=2)\n[bad](javascript:alert(1)) and [file](file:///C:/x.exe)");
        var viewer = new ReleaseNotesViewer(viewModel, url =>
        {
            opened.Add(url);
            return Task.CompletedTask;
        });
        viewer.Show();
        try
        {
            var buttons = FindLinkButtons(viewer);

            // Assert: only the http link is a button
            var button = Assert.Single(buttons);

            // Act
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["https://example.com/a?b=1&c=2"], opened);
        }
        finally
        {
            viewer.Close();
        }

        await Task.CompletedTask;
    });

    /// <summary>
    ///     Test that pressing Enter on a focused link opens it, like a click.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_LinkFocusedEnterKey_InvokesOpener() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var opened = new List<string>();
        var viewer = new ReleaseNotesViewer(
            new ReleaseNotesViewerViewModel("my-repo", "[good](https://example.com/a)"),
            url =>
            {
                opened.Add(url);
                return Task.CompletedTask;
            });
        viewer.Show();
        try
        {
            var button = Assert.Single(FindLinkButtons(viewer));
            button.Focus();
            Dispatcher.UIThread.RunJobs();

            // Act
            viewer.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            viewer.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            // Assert
            Assert.Equal(["https://example.com/a"], opened);
        }
        finally
        {
            viewer.Close();
        }
    });

    /// <summary>
    ///     Test that opening a link passes an openable address to the opener and reports no problem,
    ///     refuses any other address without calling the opener, and turns an opener failure into a
    ///     message instead of an exception.
    /// </summary>
    [Fact]
    public void ReleaseNotesViewer_OpenLinkAsync_OpensOnlyHttpAndReportsFailures() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var opened = new List<string>();
        var fail = false;
        var viewer = new ReleaseNotesViewer(
            new ReleaseNotesViewerViewModel("my-repo", "notes"),
            url =>
            {
                opened.Add(url);
                return fail ? Task.FromException(new InvalidOperationException("no browser")) : Task.CompletedTask;
            });

        // Act / Assert: an https address is opened
        Assert.Null(await viewer.OpenLinkAsync("https://example.com/x"));
        Assert.Equal(["https://example.com/x"], opened);

        // Act / Assert: other addresses are refused, and the opener is not called
        foreach (var refused in new[] { "file:///C:/x.exe", "javascript:alert(1)", "mailto:a@b.c", "relative.md", string.Empty })
        {
            Assert.NotNull(await viewer.OpenLinkAsync(refused));
        }

        Assert.Single(opened);

        // Act / Assert: a failing opener is reported, not thrown
        fail = true;
        var message = await viewer.OpenLinkAsync("https://example.com/y");
        Assert.NotNull(message);
        Assert.Contains("no browser", message, StringComparison.Ordinal);
        Assert.Contains("https://example.com/y", message, StringComparison.Ordinal);
    });

    /// <summary>
    ///     Finds the link buttons rendered into a release-notes window.
    /// </summary>
    /// <param name="viewer">The window.</param>
    /// <returns>The link buttons, in order.</returns>
    private static List<Button> FindLinkButtons(ReleaseNotesViewer viewer) =>
        [.. viewer.FindControl<StackPanel>("ReleaseNotesPanel")!.Children
            .OfType<TextBlock>()
            .SelectMany(textBlock => textBlock.Inlines!.OfType<InlineUIContainer>())
            .Select(container => container.Child)
            .OfType<Button>()];
}
