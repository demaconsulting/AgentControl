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
using Avalonia.Markup.Xaml;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     Non-modal, resizable window displaying a package's release notes after a successful
///     upgrade, per architecture.md's <c>ReleaseNotesViewer</c> subsystem description.
/// </summary>
/// <remarks>
///     Callers MUST use <see cref="Window.Show()"/>, never <c>ShowDialog</c>, so the main window
///     remains usable while release notes are being read - this is an explicit Phase 2 task
///     requirement, not merely a style preference, since a modal release-notes dialog would block
///     the user from launching their agent tool while reading them. All content comes from
///     <see cref="ReleaseNotesViewerViewModel"/>; this code-behind has no logic beyond the
///     Avalonia XAML-loading boilerplate every window requires.
/// </remarks>
internal sealed partial class ReleaseNotesViewer : Window
{
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
    ///     Initializes a new <see cref="ReleaseNotesViewer"/> bound to the given view model.
    /// </summary>
    /// <param name="viewModel">The view model supplying the title and release notes text.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is
    ///     <see langword="null"/>.</exception>
    public ReleaseNotesViewer(ReleaseNotesViewerViewModel viewModel) : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }
}
