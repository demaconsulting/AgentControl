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
///     Tests for the hover options, the park-point planning and the mouse-move interpolation (no cursor is moved).
/// </summary>
public class HoverTests
{
    /// <summary>
    ///     An element in the middle of a 800x600 window at (100, 100).
    /// </summary>
    private static readonly ScreenRect Target = new(400, 300, 100, 40);

    /// <summary>
    ///     The window the element sits in.
    /// </summary>
    private static readonly ScreenRect Window = new(100, 100, 800, 600);

    /// <summary>
    ///     Validates that the hover options default to a 700 ms rest and no shot.
    /// </summary>
    [Fact]
    public void HoverOptions_Parse_NoOptions_UsesDefaults()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["hover", "SaveSettingsButton"]);

        // Act
        var options = HoverOptions.Parse(parsed);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(HoverOptions.DefaultWaitMs, options.WaitMs),
            () => Assert.Equal(700, options.WaitMs),
            () => Assert.Null(options.ShotPath));
    }

    /// <summary>
    ///     Validates that <c>--wait</c> and <c>--shot</c> are read.
    /// </summary>
    [Fact]
    public void HoverOptions_Parse_WaitAndShot_AreRead()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["hover", "SaveSettingsButton", "--wait", "1500", "--shot", "tip.png"]);

        // Act
        var options = HoverOptions.Parse(parsed);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(1500, options.WaitMs),
            () => Assert.Equal("tip.png", options.ShotPath));
    }

    /// <summary>
    ///     Validates that a wait that is not a non-negative whole number, and a shot without a path, are usage errors.
    /// </summary>
    /// <param name="line">The command line.</param>
    [Theory]
    [InlineData("hover X --wait slow")]
    [InlineData("hover X --wait -5")]
    [InlineData("hover X --wait 1.5")]
    [InlineData("hover X --wait")]
    [InlineData("hover X --shot")]
    public void HoverOptions_Parse_Invalid_IsUsageError(string line)
    {
        // Arrange
        var parsed = CommandLineParser.Parse(line.Split(' '));

        // Act
        var exception = Assert.Throws<PuppetException>(() => HoverOptions.Parse(parsed));

        // Assert
        Assert.Equal(ExitCodes.UsageError, exception.ExitCode);
    }

    /// <summary>
    ///     Validates that the nearest park candidate is directly above the element and that no candidate is on it.
    /// </summary>
    [Fact]
    public void HoverPlanner_ParkCandidates_NearestIsAboveAndNoneIsOnTheElement()
    {
        // Arrange / Act
        var candidates = HoverPlanner.ParkCandidates(Target);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(new ScreenPoint(450, 276), candidates[0]),
            () => Assert.DoesNotContain(candidates, c => Target.Contains(c)),
            () => Assert.Equal(32, candidates.Count));
    }

    /// <summary>
    ///     Validates that the park point is the nearest candidate when everything is acceptable.
    /// </summary>
    [Fact]
    public void HoverPlanner_ChooseParkPoint_EverythingAcceptable_IsAboveTheElement()
    {
        // Arrange / Act
        var park = HoverPlanner.ChooseParkPoint(Target, Window, _ => true);

        // Assert
        Assert.Equal(new ScreenPoint(450, 276), park);
    }

    /// <summary>
    ///     Validates that a candidate outside the window is skipped (the element is at the top edge, so the park point is below).
    /// </summary>
    [Fact]
    public void HoverPlanner_ChooseParkPoint_ElementAtWindowTop_ParksBelow()
    {
        // Arrange: the element touches the top of the window, so there is no room above it
        var target = new ScreenRect(400, 105, 100, 40);

        // Act
        var park = HoverPlanner.ChooseParkPoint(target, Window, _ => true);

        // Assert
        Assert.Equal(new ScreenPoint(450, 169), park);
    }

    /// <summary>
    ///     Validates that candidates the caller rejects (another program's window is on top there) are skipped.
    /// </summary>
    [Fact]
    public void HoverPlanner_ChooseParkPoint_RejectedCandidates_AreSkipped()
    {
        // Arrange: reject everything above the element
        static bool NotAbove(ScreenPoint p) => p.Y > Target.Y;

        // Act
        var park = HoverPlanner.ChooseParkPoint(Target, Window, NotAbove);

        // Assert
        Assert.Equal(new ScreenPoint(450, 364), park);
    }

    /// <summary>
    ///     Validates that no park point is found when none is acceptable, so the cursor goes straight to the element.
    /// </summary>
    [Fact]
    public void HoverPlanner_ChooseParkPoint_NothingAcceptable_IsNull()
    {
        // Arrange / Act
        var park = HoverPlanner.ChooseParkPoint(Target, Window, _ => false);

        // Assert
        Assert.Null(park);
    }

    /// <summary>
    ///     Validates that an element that fills the whole window has no park point.
    /// </summary>
    [Fact]
    public void HoverPlanner_ChooseParkPoint_ElementFillsWindow_IsNull()
    {
        // Arrange / Act
        var park = HoverPlanner.ChooseParkPoint(Window, Window, _ => true);

        // Assert
        Assert.Null(park);
    }

    /// <summary>
    ///     Validates that a mouse move is split into steps that end exactly on the target.
    /// </summary>
    [Fact]
    public void InputDriver_Interpolate_EndsOnTargetInSmallSteps()
    {
        // Arrange
        var from = new ScreenPoint(0, 0);
        var to = new ScreenPoint(120, 0);

        // Act
        var points = InputDriver.Interpolate(from, to, 12);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(10, points.Count),
            () => Assert.Equal(new ScreenPoint(12, 0), points[0]),
            () => Assert.Equal(to, points[^1]));
    }

    /// <summary>
    ///     Validates that a zero-length move still yields the target as its only point.
    /// </summary>
    [Fact]
    public void InputDriver_Interpolate_ZeroDistance_YieldsTarget()
    {
        // Arrange
        var point = new ScreenPoint(5, 5);

        // Act
        var points = InputDriver.Interpolate(point, point);

        // Assert
        Assert.Equal([point], points);
    }
}
