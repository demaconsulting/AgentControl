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

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     Tests for choosing the innermost open dialog from an instance's roots (no window is touched).
/// </summary>
public class DialogSelectorTests
{
    /// <summary>
    ///     A candidate for the main window (never chosen).
    /// </summary>
    private static readonly DialogCandidate Main = new("AgentControl", false, true);

    /// <summary>
    ///     Validates that with no dialog open there is nothing to choose.
    /// </summary>
    [Fact]
    public void DialogSelector_InnermostDialog_OnlyMainWindow_ReturnsNone()
    {
        // Act
        var index = DialogSelector.InnermostDialog([Main]);

        // Assert
        Assert.Equal(-1, index);
    }

    /// <summary>
    ///     Validates that the last visible titled window is the innermost dialog.
    /// </summary>
    [Fact]
    public void DialogSelector_InnermostDialog_NestedDialogs_ReturnsTheLast()
    {
        // Arrange: main, Settings, a source editor opened from it
        var roots = new[] { Main, new DialogCandidate("Settings", false, true), new DialogCandidate("Edit source", false, true) };

        // Act
        var index = DialogSelector.InnermostDialog(roots);

        // Assert
        Assert.Equal(2, index);
    }

    /// <summary>
    ///     Validates that a tooltip pop-up listed after the dialog is not chosen, although it is visible and titled.
    /// </summary>
    [Fact]
    public void DialogSelector_InnermostDialog_TooltipAfterDialog_IsSkipped()
    {
        // Arrange: main, Settings, then a tooltip pop-up (named, visible, but not a window)
        var roots = new[] { Main, new DialogCandidate("Settings", false, true), new DialogCandidate("Save the settings", false, false) };

        // Act
        var index = DialogSelector.InnermostDialog(roots);

        // Assert
        Assert.Equal(1, index);
    }

    /// <summary>
    ///     Validates that with only a tooltip open, the main window is the target (no dialog).
    /// </summary>
    [Fact]
    public void DialogSelector_InnermostDialog_OnlyATooltipOpen_ReturnsNone()
    {
        // Act
        var index = DialogSelector.InnermostDialog([Main, new DialogCandidate("Add a repository", false, false)]);

        // Assert
        Assert.Equal(-1, index);
    }

    /// <summary>
    ///     Validates that off-screen and untitled windows are skipped in favor of an earlier visible titled one.
    /// </summary>
    [Fact]
    public void DialogSelector_InnermostDialog_OffscreenOrUntitledWindows_AreSkipped()
    {
        // Arrange
        var roots = new[]
        {
            Main,
            new DialogCandidate("Settings", false, true),
            new DialogCandidate("Hidden", true, true),
            new DialogCandidate(" ", false, true),
            new DialogCandidate(null, false, true),
        };

        // Act
        var index = DialogSelector.InnermostDialog(roots);

        // Assert
        Assert.Equal(1, index);
    }
}
