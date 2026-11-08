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
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     What the pixels of one text element in a window show: the brightness of the surface behind
///     the text and how well the text stands out from it.
/// </summary>
/// <param name="BackgroundBrightness">The brightness of the surface behind the text, from 0 (black)
///     to 1 (white).</param>
/// <param name="ContrastRatio">The WCAG contrast ratio between that surface and the text pixel that
///     differs most from it (1 for no difference, 21 for black on white).</param>
internal readonly record struct RenderedTextMeasurement(double BackgroundBrightness, double ContrastRatio);

/// <summary>
///     Measures how legible a text element really is on screen, from a screenshot of its window, so a
///     test can prove a window is readable in a theme rather than only that a theme resource exists.
/// </summary>
/// <remarks>
///     The surface behind the text is the most frequent color inside the element's bounds; the text
///     color is the color there that contrasts most with the surface (anti-aliased edges are blends
///     of the two, so the extreme is the text itself). The window must be on top of the desktop, or
///     the screenshot shows whatever covers it.
/// </remarks>
internal static class RenderedTextProbe
{
    /// <summary>
    ///     The WCAG minimum contrast ratio for normal-size text.
    /// </summary>
    public const double MinimumContrastRatio = 4.5;

    /// <summary>
    ///     The most pixels read from one element: enough for a line or paragraph of text, bounded so
    ///     a large panel is read from its top-left part only.
    /// </summary>
    private static readonly Size MaximumArea = new(600, 300);

    /// <summary>
    ///     Waits until a text element in a window is legible in the given theme (a light surface
    ///     for Light, a dark one for Dark, and text that contrasts with it by at least the WCAG
    ///     minimum), and returns the last measurement.
    /// </summary>
    /// <param name="window">The window that shows the element; it must be on top.</param>
    /// <param name="element">The text element (or a panel of text) to measure.</param>
    /// <param name="theme">The theme the window should be drawn in, <c>Light</c> or <c>Dark</c>.</param>
    /// <returns>The last measurement, and whether it met the expectation.</returns>
    public static (RenderedTextMeasurement Measurement, bool IsLegible) WaitForLegible(
        Window window, AutomationElement element, string theme)
    {
        var measurement = default(RenderedTextMeasurement);
        var legible = Retry.WhileFalse(
            () =>
            {
                measurement = Measure(window, element);
                return MatchesTheme(measurement, theme);
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(250)).Result;
        return (measurement, legible);
    }

    /// <summary>
    ///     Counts the pixels of a screen area that differ clearly from the area's most frequent color,
    ///     which is how a test sees that something (an icon, a glyph) is drawn on a plain surface.
    /// </summary>
    /// <param name="area">The area of the screen to examine.</param>
    /// <returns>The number of pixels that differ from the area's background.</returns>
    public static int CountInkPixels(Rectangle area)
    {
        using var capture = Capture.Rectangle(area);
        var bitmap = capture.Bitmap;
        var colors = new List<Color>(bitmap.Width * bitmap.Height);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                colors.Add(bitmap.GetPixel(x, y));
            }
        }

        var background = colors.GroupBy(color => color.ToArgb()).MaxBy(group => group.Count())!.First();
        return colors.Count(color =>
            Math.Abs(color.R - background.R)
            + Math.Abs(color.G - background.G)
            + Math.Abs(color.B - background.B) > 60);
    }

    /// <summary>
    ///     Describes a measurement for an assertion message.
    /// </summary>
    /// <param name="measurement">The measurement.</param>
    /// <param name="theme">The theme that was expected.</param>
    /// <returns>A short description.</returns>
    public static string Describe(RenderedTextMeasurement measurement, string theme) =>
        $"{theme} theme: background brightness {measurement.BackgroundBrightness:0.00}, " +
        $"text contrast {measurement.ContrastRatio:0.0}:1";

    /// <summary>
    ///     Reads the colors that fill an element's area as it is drawn in a window: the one that covers
    ///     most of it, which is the surface behind any text or glyph on it.
    /// </summary>
    /// <param name="window">The window that shows the element; it must be on top.</param>
    /// <param name="element">The element to read.</param>
    /// <returns>The most frequent color in the element's area.</returns>
    public static Color SurfaceColor(Window window, AutomationElement element) =>
        Color.FromArgb(ReadColorCounts(window, element).MaxBy(pair => pair.Value).Key);

    /// <summary>
    ///     Measures the background brightness and the text contrast of an element.
    /// </summary>
    /// <param name="window">The window that shows the element.</param>
    /// <param name="element">The element to measure.</param>
    /// <returns>The measurement.</returns>
    public static RenderedTextMeasurement Measure(Window window, AutomationElement element)
    {
        var counts = ReadColorCounts(window, element);
        if (counts.Count == 0)
        {
            return default;
        }

        var background = Color.FromArgb(counts.MaxBy(pair => pair.Value).Key);
        var contrast = counts.Keys.Max(argb => ContrastRatio(background, Color.FromArgb(argb)));
        return new RenderedTextMeasurement(Brightness(background), contrast);
    }

    /// <summary>
    ///     Counts how often each color appears in an element's area of a screenshot of its window.
    /// </summary>
    /// <param name="window">The window that shows the element.</param>
    /// <param name="element">The element to read.</param>
    /// <returns>The number of pixels of each color, by ARGB value.</returns>
    private static Dictionary<int, int> ReadColorCounts(Window window, AutomationElement element)
    {
        using var capture = Capture.Element(window);
        var bitmap = capture.Bitmap;
        var windowBounds = window.BoundingRectangle;
        var bounds = element.BoundingRectangle;

        var left = Math.Clamp(bounds.Left - windowBounds.Left, 0, bitmap.Width - 1);
        var top = Math.Clamp(bounds.Top - windowBounds.Top, 0, bitmap.Height - 1);
        var right = Math.Min(Math.Min(left + bounds.Width, left + MaximumArea.Width), bitmap.Width);
        var bottom = Math.Min(Math.Min(top + bounds.Height, top + MaximumArea.Height), bitmap.Height);

        var counts = new Dictionary<int, int>();
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var argb = bitmap.GetPixel(x, y).ToArgb();
                counts[argb] = counts.GetValueOrDefault(argb) + 1;
            }
        }

        return counts;
    }

    /// <summary>
    ///     Tells whether a measurement shows legible text in the given theme.
    /// </summary>
    /// <param name="measurement">The measurement.</param>
    /// <param name="theme">The expected theme, <c>Light</c> or <c>Dark</c>.</param>
    /// <returns><see langword="true"/> when the surface has the theme's brightness and the text meets
    ///     the minimum contrast.</returns>
    private static bool MatchesTheme(RenderedTextMeasurement measurement, string theme)
    {
        var surfaceFits = string.Equals(theme, "Light", StringComparison.Ordinal)
            ? measurement.BackgroundBrightness > 0.6
            : measurement.BackgroundBrightness < 0.4;
        return surfaceFits && measurement.ContrastRatio >= MinimumContrastRatio;
    }

    /// <summary>
    ///     Computes how bright a color looks.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>A brightness from 0 (black) to 1 (white).</returns>
    private static double Brightness(Color color) =>
        ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;

    /// <summary>
    ///     Computes the WCAG contrast ratio between two colors.
    /// </summary>
    /// <param name="first">The first color.</param>
    /// <param name="second">The second color.</param>
    /// <returns>The ratio, from 1 to 21.</returns>
    private static double ContrastRatio(Color first, Color second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    ///     Computes the WCAG relative luminance of a color.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The luminance, from 0 to 1.</returns>
    private static double RelativeLuminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    /// <summary>
    ///     Converts one sRGB channel to its linear value.
    /// </summary>
    /// <param name="channel">The channel, 0 to 255.</param>
    /// <returns>The linear value, 0 to 1.</returns>
    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
