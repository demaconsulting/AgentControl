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

using Avalonia.Controls.Documents;
using Avalonia.Media;
using DemaConsulting.AgentControl.RepoSync;

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
        var inlines = Assert.IsAssignableFrom<InlineCollection>(textBlock.Inlines);
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
}
