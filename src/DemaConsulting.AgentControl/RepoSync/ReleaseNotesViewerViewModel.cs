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

using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.RepoSync;

/// <summary>
///     View model for <see cref="ReleaseNotesViewer"/>: holds the window title and the release
///     notes text to display after a successful package upgrade.
/// </summary>
/// <remarks>
///     Deliberately trivial - <c>PackageZipExtractor.ReadReleaseNotes</c> already does the only
///     real work (reading the zip entry), so this view model exists purely to give the view a
///     bindable, testable surface rather than reading fields directly off a plain string.
///     Immutable and thread-safe once constructed.
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
    }

    /// <summary>
    ///     Gets the window title, identifying which repo's upgrade produced these release notes.
    /// </summary>
    public string Title { get; }

    /// <summary>
    ///     Gets the release notes text to display, rendered as plain text (v1 does not require a
    ///     full Markdown renderer per the Phase 2 task scope).
    /// </summary>
    public string ReleaseNotes { get; }
}
