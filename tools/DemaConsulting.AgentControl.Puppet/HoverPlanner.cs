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

using System.Globalization;

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     The options of the hover command: how long the cursor rests on the target and where to save a screen copy.
/// </summary>
/// <param name="WaitMs">How long the cursor rests on the target before the command returns (or takes its shot), in milliseconds.</param>
/// <param name="ShotPath">Where to save a screen copy taken while the cursor rests on the target, or <see langword="null"/> for none.</param>
internal sealed record HoverOptions(int WaitMs, string? ShotPath)
{
    /// <summary>
    ///     The default rest, in milliseconds: comfortably longer than Avalonia's default tooltip delay (<c>ToolTip.ShowDelay</c>,
    ///     400 ms), so the tooltip is open when the command returns or takes its shot.
    /// </summary>
    internal const int DefaultWaitMs = 700;

    /// <summary>
    ///     Reads the <c>--wait &lt;ms&gt;</c> and <c>--shot &lt;path.png&gt;</c> options of a hover command.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The options; <see cref="DefaultWaitMs"/> when <c>--wait</c> is absent.</returns>
    /// <exception cref="PuppetException">Thrown with a usage error when <c>--wait</c> is not a non-negative integer or <c>--shot</c> has no path.</exception>
    internal static HoverOptions Parse(ParsedCommand parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        // The wait must be a whole, non-negative number of milliseconds
        var wait = DefaultWaitMs;
        if (parsed.HasOption("wait"))
        {
            var text = parsed.GetOption("wait");
            if (text is null || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out wait))
            {
                throw PuppetException.Usage($"--wait must be a non-negative number of milliseconds, not '{text}'.");
            }
        }

        // A shot needs somewhere to go
        var shot = parsed.GetOption("shot");
        if (parsed.HasOption("shot") && string.IsNullOrWhiteSpace(shot))
        {
            throw PuppetException.Usage("--shot needs a path, for example --shot tip.png.");
        }

        return new HoverOptions(wait, shot);
    }
}

/// <summary>
///     The pure planning behind <c>hover</c>: where to park the cursor before it rests on an element.
/// </summary>
/// <remarks>
///     A tooltip only opens fresh when the pointer arrives at its target from somewhere else: the control keeps the
///     tooltip of what it already describes and does not reopen the one of an element that was just pressed. Parking the
///     cursor beside the element first (outside its bounds, still on the instance's window) resets that, so the hover
///     command always shows the tooltip a user would see.
/// </remarks>
internal static class HoverPlanner
{
    /// <summary>
    ///     How far beyond the element's edge the park points are tried, nearest first, in pixels.
    /// </summary>
    private static readonly double[] ParkDistances = [24, 60, 120, 200];

    /// <summary>
    ///     How far inside the window's edge a park point must be, in pixels (so it is not on the resize border).
    /// </summary>
    private const double WindowInset = 6;

    /// <summary>
    ///     Lists the places to try parking the cursor before hovering an element: rings of increasing distance around its
    ///     bounds, above, below, left and right first, then the corners.
    /// </summary>
    /// <param name="target">The element's bounds.</param>
    /// <returns>The candidates, nearest first.</returns>
    internal static IReadOnlyList<ScreenPoint> ParkCandidates(ScreenRect target)
    {
        var middle = target.Center;
        return
        [
            .. ParkDistances.SelectMany(d => new[]
            {
                new ScreenPoint(middle.X, target.Y - d),
                new ScreenPoint(middle.X, target.Bottom + d),
                new ScreenPoint(target.X - d, middle.Y),
                new ScreenPoint(target.Right + d, middle.Y),
                new ScreenPoint(target.X - d, target.Y - d),
                new ScreenPoint(target.Right + d, target.Y - d),
                new ScreenPoint(target.X - d, target.Bottom + d),
                new ScreenPoint(target.Right + d, target.Bottom + d),
            }),
        ];
    }

    /// <summary>
    ///     Chooses where to park the cursor before hovering an element: the nearest candidate that is outside the element,
    ///     inside the window and accepted by the caller (which checks that the window under that pixel is the instance's).
    /// </summary>
    /// <param name="target">The element's bounds.</param>
    /// <param name="window">The instance's main window bounds.</param>
    /// <param name="isAcceptable">Whether a candidate may be used (for example: the instance's window is topmost there).</param>
    /// <returns>The park point, or <see langword="null"/> when none qualifies.</returns>
    internal static ScreenPoint? ChooseParkPoint(ScreenRect target, ScreenRect window, Func<ScreenPoint, bool> isAcceptable)
    {
        ArgumentNullException.ThrowIfNull(isAcceptable);
        var area = window.Inset(WindowInset);
        foreach (var candidate in ParkCandidates(target))
        {
            if (area.Contains(candidate) && !target.Contains(candidate) && isAcceptable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
