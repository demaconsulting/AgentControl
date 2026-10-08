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
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     Non-modal, resizable window displaying a package's release notes after a successful
///     upgrade, per architecture.md's <c>ReleaseNotesViewer</c> subsystem description.
/// </summary>
/// <remarks>
///     Callers MUST use <see cref="Window.Show()"/>, never <c>ShowDialog</c>, so the main window
///     remains usable while release notes are being read - this is an explicit Phase 2 task
///     requirement, not merely a style preference, since a modal release-notes dialog would block
///     the user from launching their agent tool while reading them. The window title and raw
///     release-notes text come from data binding to <see cref="ReleaseNotesViewerViewModel"/>,
///     but its parsed <see cref="MarkdownBlock"/>s are rendered here in code-behind (rather than
///     via an Avalonia DataTemplate) since each block's bold/italic/heading styling applies
///     per-run, which Avalonia's declarative templating cannot express without a comparable
///     amount of code anyway.
/// </remarks>
internal sealed partial class ReleaseNotesViewer : Window
{
    /// <summary>
    ///     Base font size used for a non-heading, non-bullet paragraph line.
    /// </summary>
    private const double BaseFontSize = 13;

    /// <summary>
    ///     Initializes a new <see cref="ReleaseNotesViewer"/> with no bound view model; used by
    ///     the Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="ReleaseNotesViewer(ReleaseNotesViewerViewModel)"/>.
    /// </summary>
    public ReleaseNotesViewer()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    ///     Initializes a new <see cref="ReleaseNotesViewer"/> bound to the given view model,
    ///     rendering its parsed <see cref="ReleaseNotesViewerViewModel.Blocks"/> into
    ///     <c>ReleaseNotesPanel</c>.
    /// </summary>
    /// <param name="viewModel">The view model supplying the title and release notes content.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public ReleaseNotesViewer(ReleaseNotesViewerViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;

        var panel = this.FindControl<StackPanel>("ReleaseNotesPanel")
                    ?? throw new InvalidOperationException(
                        "ReleaseNotesPanel control not found in ReleaseNotesViewer.axaml.");

        foreach (var block in viewModel.Blocks)
        {
            panel.Children.Add(BuildBlockTextBlock(block));
        }
    }

    /// <summary>
    ///     Builds a <see cref="TextBlock"/> rendering a single parsed <see cref="MarkdownBlock"/>:
    ///     a bullet glyph prefix if the block is a list item, a larger bold font for headings,
    ///     and bold/italic <see cref="Run"/> inlines for the block's emphasized text spans.
    /// </summary>
    /// <param name="block">The parsed block to render.</param>
    /// <returns>A <see cref="TextBlock"/> ready to add to <c>ReleaseNotesPanel</c>.</returns>
    private static TextBlock BuildBlockTextBlock(MarkdownBlock block)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Inlines = []
        };

        if (block.HeadingLevel > 0)
        {
            textBlock.FontWeight = FontWeight.Bold;
            textBlock.FontSize = Math.Max(BaseFontSize, 20 - (2 * (block.HeadingLevel - 1)));
            textBlock.Margin = new Avalonia.Thickness(0, 8, 0, 2);
        }
        else
        {
            textBlock.FontSize = BaseFontSize;
        }

        if (block.IsBullet)
        {
            textBlock.Inlines.Add(new Run("• "));
        }

        foreach (var run in block.Runs)
        {
            textBlock.Inlines.Add(new Run(run.Text)
            {
                FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal
            });
        }

        return textBlock;
    }
}
