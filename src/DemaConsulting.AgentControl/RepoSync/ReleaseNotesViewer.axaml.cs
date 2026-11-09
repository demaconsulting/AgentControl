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

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DemaConsulting.AgentControl.Logging;
using Microsoft.Extensions.Logging;

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
    ///     Opens an address in the user's browser: the platform launcher in production, a test
    ///     double in tests. Set by the constructor; never called with an address that failed
    ///     <see cref="ReleaseNotesViewerViewModel.IsOpenableUrl"/>.
    /// </summary>
    private readonly Func<string, Task> _openUrl;

    /// <summary>
    ///     Initializes a new <see cref="ReleaseNotesViewer"/> with no bound view model; used by
    ///     the Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="ReleaseNotesViewer(ReleaseNotesViewerViewModel, Func{string, Task}?)"/>.
    /// </summary>
    public ReleaseNotesViewer()
    {
        AvaloniaXamlLoader.Load(this);
        _openUrl = OpenInBrowserAsync;
    }

    /// <summary>
    ///     Initializes a new <see cref="ReleaseNotesViewer"/> bound to the given view model,
    ///     rendering its parsed <see cref="ReleaseNotesViewerViewModel.Blocks"/> into
    ///     <c>ReleaseNotesPanel</c>.
    /// </summary>
    /// <param name="viewModel">The view model supplying the title and release notes content.</param>
    /// <param name="openUrl">Opens an address in the user's default browser; <see langword="null"/>
    ///     uses the platform launcher. A seam so tests never open a real browser. It is only ever
    ///     called with an address that passed <see cref="ReleaseNotesViewerViewModel.IsOpenableUrl"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public ReleaseNotesViewer(ReleaseNotesViewerViewModel viewModel, Func<string, Task>? openUrl = null) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        _openUrl = openUrl ?? OpenInBrowserAsync;

        var panel = this.FindControl<StackPanel>("ReleaseNotesPanel")
                    ?? throw new InvalidOperationException(
                        "ReleaseNotesPanel control not found in ReleaseNotesViewer.axaml.");

        foreach (var block in viewModel.Blocks)
        {
            panel.Children.Add(BuildBlockTextBlock(block, ActivateLinkAsync));
        }
    }

    /// <summary>
    ///     Opens <paramref name="url"/> in the user's default browser through the platform launcher
    ///     (no shell is involved, so only a real URI is ever handed to the system).
    /// </summary>
    /// <param name="url">The absolute http/https address to open.</param>
    /// <returns>A task that completes when the launcher has been asked to open the address.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the system could not open the address.</exception>
    private async Task OpenInBrowserAsync(string url)
    {
        var launched = await Launcher.LaunchUriAsync(new Uri(url));
        if (!launched)
        {
            throw new InvalidOperationException("The system could not open the link.");
        }
    }

    /// <summary>
    ///     Opens a clicked link, never letting a failure escape: an address that is not an openable
    ///     http/https URL is refused (release notes are untrusted), and any failure to open is
    ///     logged and reported to the user.
    /// </summary>
    /// <remarks>
    ///     Internal so tests can verify the exact address reaching the opener seam and the refusal
    ///     of other schemes without a window being shown.
    /// </remarks>
    /// <param name="url">The address of the clicked link.</param>
    /// <returns>A task that completes with <see langword="null"/> when the address was handed to
    ///     the opener, or with a message describing why it was not opened.</returns>
    internal async Task<string?> OpenLinkAsync(string url)
    {
        // Re-check at the moment of opening: the gate must not depend on how the control was built
        if (!ReleaseNotesViewerViewModel.IsOpenableUrl(url))
        {
            return "Only web addresses (http or https) can be opened from release notes.";
        }

        try
        {
            await _openUrl(url);
            return null;
        }
        catch (Exception ex)
        {
            // Opening a browser is a convenience: log it and tell the user, but never crash
            AppLogging.Factory.CreateLogger<ReleaseNotesViewer>().LogWarning(ex, "Could not open a release-notes link.");
            return $"The link could not be opened: {ex.Message}\n\n{url}";
        }
    }

    /// <summary>
    ///     Handles a click (or Enter/Space) on a rendered link: opens it and reports a failure in
    ///     a message box.
    /// </summary>
    /// <param name="url">The address of the activated link.</param>
    /// <returns>A task that completes when the link has been handled.</returns>
    private async Task ActivateLinkAsync(string url)
    {
        var problem = await OpenLinkAsync(url);
        if (problem is not null && IsVisible)
        {
            await new LauncherUI.MessageBoxWindow(problem).ShowDialog(this);
        }
    }

    /// <summary>
    ///     Builds a <see cref="TextBlock"/> rendering a single parsed <see cref="MarkdownBlock"/>:
    ///     a bullet glyph prefix if the block is a list item, a larger bold font for headings,
    ///     bold/italic <see cref="Run"/> inlines for the block's emphasized text spans, and a
    ///     focusable link button for each link run.
    /// </summary>
    /// <param name="block">The parsed block to render.</param>
    /// <param name="linkActivated">Called with a link's address when the link is clicked or
    ///     activated from the keyboard; <see langword="null"/> renders links as plain text, so
    ///     nothing clickable exists without something to handle the click.</param>
    /// <returns>A <see cref="TextBlock"/> ready to add to <c>ReleaseNotesPanel</c>.</returns>
    /// <remarks>
    ///     Internal (rather than private) so <c>DemaConsulting.AgentControl.Tests</c> can verify
    ///     the rendered run styling directly, via <c>InternalsVisibleTo</c>. A link run is shown
    ///     as a link only when its address passes <see cref="ReleaseNotesViewerViewModel.IsOpenableUrl"/>.
    /// </remarks>
    internal static TextBlock BuildBlockTextBlock(MarkdownBlock block, Func<string, Task>? linkActivated = null)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Inlines = []
        };

        if (block.HeadingLevel > 0)
        {
            textBlock.FontWeight = FontWeight.Bold;
            textBlock.FontSize = Math.Max(BaseFontSize, 20.0 - (2.0 * (block.HeadingLevel - 1)));
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

        var isHeading = block.HeadingLevel > 0;
        foreach (var run in block.Runs)
        {
            var weight = isHeading || run.Bold ? FontWeight.Bold : FontWeight.Normal;
            var style = run.Italic ? FontStyle.Italic : FontStyle.Normal;

            if (linkActivated is not null && ReleaseNotesViewerViewModel.IsOpenableUrl(run.Url))
            {
                textBlock.Inlines.Add(BuildLink(run, weight, style, textBlock.FontSize, linkActivated));
                continue;
            }

            textBlock.Inlines.Add(new Run(run.Text) { FontWeight = weight, FontStyle = style });
        }

        return textBlock;
    }

    /// <summary>
    ///     Builds the inline for one link run: a flat, focusable button (so Enter and Space
    ///     activate it like a click) showing the link text styled as a link, with the full address
    ///     as its tooltip so the user sees where it leads before clicking.
    /// </summary>
    /// <param name="run">The link run; its <see cref="MarkdownRun.Url"/> is an openable address.</param>
    /// <param name="weight">The text weight.</param>
    /// <param name="style">The text style.</param>
    /// <param name="fontSize">The font size of the surrounding paragraph.</param>
    /// <param name="linkActivated">Called with the address when the button is clicked.</param>
    /// <returns>The inline to add to the paragraph.</returns>
    private static InlineUIContainer BuildLink(
        MarkdownRun run, FontWeight weight, FontStyle style, double fontSize, Func<string, Task> linkActivated)
    {
        var url = run.Url!;
        var label = new TextBlock { Text = run.Text, FontWeight = weight, FontStyle = style, FontSize = fontSize };
        var button = new Button
        {
            Content = new Border { Child = label, CornerRadius = new Avalonia.CornerRadius(2) },
            Classes = { "link" },
            Tag = url
        };
        ToolTip.SetTip(button, url);
        AutomationProperties.SetName(button, run.Text);
        AutomationProperties.SetHelpText(button, url);

        // The handler never throws: the owner's callback reports failures itself
        button.Click += async (_, _) => await linkActivated(url);

        return new InlineUIContainer(button);
    }
}
