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

using Avalonia;
using Avalonia.Controls;
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of <see cref="PathEllipsisText"/>, the card's path text that gives way in the folder
///     part first and keeps the start and the end of a long folder name with a middle ellipsis.
/// </summary>
public sealed class PathEllipsisTextTests
{
    private const string Parent = @"C:\work\clients\contoso\platform-with-a-long-name";
    private const string Leaf = @"\05-very-long-repository-name-for-narrow-window-checks-alpha";

    /// <summary>
    ///     Test that a middle ellipsis keeps the start and the end of the text, the extra character of
    ///     an odd count at the start, and leaves a text that fits alone.
    /// </summary>
    [Theory]
    [InlineData("abcdefghij", 4, "ab\u2026ij")]
    [InlineData("abcdefghij", 5, "abc\u2026ij")]
    [InlineData("abcdefghij", 1, "a\u2026")]
    [InlineData("abcdefghij", 0, "\u2026")]
    [InlineData("abcdefghij", 10, "abcdefghij")]
    [InlineData("abcdefghij", 99, "abcdefghij")]
    [InlineData("", 3, "")]
    public void PathEllipsisText_MiddleEllipsis_KeepsStartAndEnd(string text, int keep, string expected)
    {
        // Act
        var shortened = PathEllipsisText.MiddleEllipsis(text, keep);

        // Assert
        Assert.Equal(expected, shortened);
    }

    /// <summary>
    ///     Test that an end ellipsis keeps the start of the text and leaves a text that fits alone.
    /// </summary>
    [Theory]
    [InlineData("abcdefghij", 4, "abcd\u2026")]
    [InlineData("abcdefghij", 0, "\u2026")]
    [InlineData("abcdefghij", 10, "abcdefghij")]
    [InlineData("abc", 7, "abc")]
    public void PathEllipsisText_EndEllipsis_KeepsStart(string text, int keep, string expected)
    {
        // Act
        var shortened = PathEllipsisText.EndEllipsis(text, keep);

        // Assert
        Assert.Equal(expected, shortened);
    }

    /// <summary>
    ///     Test that neither ellipsis cuts a surrogate pair (an emoji in a folder name) in two.
    /// </summary>
    [Fact]
    public void PathEllipsisText_Ellipsis_NeverSplitsASurrogatePair()
    {
        // Arrange: a pair where the cut would fall, at the start and at the end of the kept text
        const string text = "ab\U0001F600cd\U0001F600ef";

        // Act
        var end = PathEllipsisText.EndEllipsis(text, 3);
        var middle = PathEllipsisText.MiddleEllipsis(text, 5);

        // Assert: every kept character is a whole code point
        Assert.Equal("ab\u2026", end);
        Assert.Equal("ab\u2026ef", middle);
    }

    /// <summary>
    ///     Test that a path that fits is drawn whole, and is drawn whole again when the width grows back.
    /// </summary>
    [Fact]
    public void PathEllipsisText_WideEnough_DrawsTheWholePath() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var control = new PathEllipsisText { ParentText = Parent, LeafText = Leaf };

        // Act: far more room than the path needs, then a narrow width, then wide again
        control.Measure(new Size(2000, 40));
        var wide = control.DisplayText;
        control.Measure(new Size(150, 40));
        var narrow = control.DisplayText;
        control.InvalidateMeasure();
        control.Measure(new Size(2000, 40));

        // Assert
        Assert.Equal(Parent + Leaf, wide);
        Assert.NotEqual(Parent + Leaf, narrow);
        Assert.Equal(Parent + Leaf, control.DisplayText);
    });

    /// <summary>
    ///     Test that when the path does not fit, the folder part gives way first (an ellipsis at its
    ///     end) while the folder name, which still fits, stays whole.
    /// </summary>
    [Fact]
    public void PathEllipsisText_SlightlyTooNarrow_ShortensTheFolderPartAndKeepsTheFolderName() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a short folder name in a long folder part
        const string leaf = @"\repo";
        var control = new PathEllipsisText { ParentText = Parent, LeafText = leaf };
        control.Measure(Size.Infinity);
        var fullWidth = control.DesiredSize.Width;

        // Act: a little less than the whole path needs
        control.Measure(new Size(fullWidth * 0.7, 40));

        // Assert: the folder name is whole at the end, the folder part ends in the ellipsis before it
        Assert.EndsWith(PathEllipsisText.Ellipsis + leaf, control.DisplayText);
        Assert.StartsWith(@"C:\", control.DisplayText);
        Assert.True(control.DesiredSize.Width <= fullWidth * 0.7 + 1, "the shortened path should fit the width");
    });

    /// <summary>
    ///     Test that a folder name that does not fit even when the folder part has given way gets a
    ///     middle ellipsis that keeps both its start and its end, that the result fits the width, and that
    ///     more room keeps more of the name.
    /// </summary>
    [Fact]
    public void PathEllipsisText_VeryLongFolderName_KeepsItsStartAndItsEnd() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var control = new PathEllipsisText { ParentText = Parent, LeafText = Leaf };
        control.Measure(Size.Infinity);
        var fullWidth = control.DesiredSize.Width;

        // Act
        control.Measure(new Size(fullWidth * 0.5, 40));
        var half = control.DisplayText;
        var halfWidth = control.DesiredSize.Width;
        control.Measure(new Size(fullWidth * 0.8, 40));
        var most = control.DisplayText;

        // Assert: the start and the end of the folder name are both there, the ellipsis is between them
        Assert.Contains(@"\05-", half);
        Assert.EndsWith("checks-alpha", half);
        Assert.Contains(PathEllipsisText.Ellipsis, half);
        Assert.True(halfWidth <= fullWidth * 0.5 + 1, $"{halfWidth} should fit {fullWidth * 0.5}");

        // Assert: more room, more of the name
        Assert.True(most.Length > half.Length, $"'{most}' should be longer than '{half}'");
        Assert.EndsWith("checks-alpha", most);
    });

    /// <summary>
    ///     Test that a path without a folder part (only a folder name) is shortened in the middle and a
    ///     missing path draws nothing.
    /// </summary>
    [Fact]
    public void PathEllipsisText_NoFolderPartOrNoPath_StillFitsOrDrawsNothing() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var bare = new PathEllipsisText { LeafText = "a-very-long-folder-name-without-any-folder-part" };
        var empty = new PathEllipsisText();

        // Act
        bare.Measure(Size.Infinity);
        var fullWidth = bare.DesiredSize.Width;
        bare.Measure(new Size(fullWidth / 2, 40));
        empty.Measure(new Size(100, 40));

        // Assert
        Assert.StartsWith("a-very", bare.DisplayText);
        Assert.EndsWith("part", bare.DisplayText);
        Assert.Contains(PathEllipsisText.Ellipsis, bare.DisplayText);
        Assert.Equal(string.Empty, empty.DisplayText);
    });
}
