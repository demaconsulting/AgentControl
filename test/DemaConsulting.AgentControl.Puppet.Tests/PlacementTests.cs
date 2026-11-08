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

using System.Drawing;

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     Tests for the argument parsing and rectangle arithmetic of the <c>place</c> command (no window is touched).
/// </summary>
public class PlacementTests
{
    /// <summary>
    ///     The work area of a 1920x1080 screen with a 60 pixel taskbar at the bottom.
    /// </summary>
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1020);

    /// <summary>
    ///     Validates that the four explicit options give exactly that rectangle.
    /// </summary>
    [Fact]
    public void PlacementRequest_Parse_ExplicitRectangle_IsReturnedUnchanged()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["place", "--x", "130", "--y", "8", "--width", "1782", "--height", "1004", "--instance", "a"]);

        // Act
        var rectangle = PlacementRequest.Parse(parsed).Resolve(WorkArea);

        // Assert
        Assert.Equal(new Rectangle(130, 8, 1782, 1004), rectangle);
    }

    /// <summary>
    ///     Validates that a negative position (a monitor left of the primary one) is accepted.
    /// </summary>
    [Fact]
    public void PlacementRequest_Parse_NegativePosition_IsAccepted()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["place", "--x", "-1500", "--y", "-20", "--width", "800", "--height", "600"]);

        // Act
        var rectangle = PlacementRequest.Parse(parsed).Resolve(WorkArea);

        // Assert
        Assert.Equal(new Rectangle(-1500, -20, 800, 600), rectangle);
    }

    /// <summary>
    ///     Validates that the work-area form leaves the left gap and the margins free.
    /// </summary>
    [Fact]
    public void PlacementRequest_Resolve_WorkAreaWithLeftGap_FillsTheRest()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["place", "--work-area", "--left-gap", "130", "--margin", "8"]);

        // Act
        var rectangle = PlacementRequest.Parse(parsed).Resolve(WorkArea);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(new Rectangle(130, 8, 1782, 1004), rectangle),
            () => Assert.Equal(WorkArea.Right - 8, rectangle.Right),
            () => Assert.Equal(WorkArea.Bottom - 8, rectangle.Bottom));
    }

    /// <summary>
    ///     Validates that the work-area form defaults to an 8 pixel margin and no left gap, and follows an offset work area.
    /// </summary>
    [Fact]
    public void PlacementRequest_Resolve_WorkAreaDefaults_UsesMarginOnly()
    {
        // Arrange: a taskbar on the left leaves the work area starting at x = 48
        var parsed = CommandLineParser.Parse(["place", "--work-area"]);

        // Act
        var rectangle = PlacementRequest.Parse(parsed).Resolve(new Rectangle(48, 0, 1872, 1080));

        // Assert
        Assert.Equal(new Rectangle(48, 8, 1864, 1064), rectangle);
    }

    /// <summary>
    ///     Validates that the work-area flag does not swallow an option after it.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_WorkAreaFlag_DoesNotSwallowNextOption()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["place", "--work-area", "--margin", "4"]);

        // Assert
        Assert.Multiple(
            () => Assert.True(parsed.HasOption("work-area")),
            () => Assert.Null(parsed.GetOption("work-area")),
            () => Assert.Equal("4", parsed.GetOption("margin")));
    }

    /// <summary>
    ///     Validates that a missing option, a non-number and a non-positive size are usage errors.
    /// </summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("place --x 1 --y 2 --width 3")]
    [InlineData("place")]
    [InlineData("place --x one --y 2 --width 3 --height 4")]
    [InlineData("place --x 1 --y 2 --width 0 --height 4")]
    [InlineData("place --x 1 --y 2 --width 3 --height -4")]
    [InlineData("place --work-area --margin -1")]
    [InlineData("place --work-area --left-gap wide")]
    public void PlacementRequest_Parse_BadArguments_IsUsageError(string line)
    {
        // Arrange
        var parsed = CommandLineParser.Parse(line.Split(' '));

        // Act
        var exception = Assert.Throws<PuppetException>(() => PlacementRequest.Parse(parsed));

        // Assert
        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
    }

    /// <summary>
    ///     Validates that gaps larger than the work area are a usage error.
    /// </summary>
    [Fact]
    public void PlacementRequest_Resolve_GapsLargerThanWorkArea_IsUsageError()
    {
        // Arrange
        var request = PlacementRequest.Parse(CommandLineParser.Parse(["place", "--work-area", "--left-gap", "2000"]));

        // Act
        var exception = Assert.Throws<PuppetException>(() => request.Resolve(WorkArea));

        // Assert
        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
    }
}
