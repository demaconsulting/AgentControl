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
}
