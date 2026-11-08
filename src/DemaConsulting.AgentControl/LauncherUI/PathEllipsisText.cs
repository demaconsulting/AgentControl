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
using Avalonia.Automation;
using Avalonia.Controls;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Shows a repo's path on one line and, when the line does not fit, shortens it so the part
///     that tells similar repos apart stays readable: the folder part before the last separator
///     gives way first (an ellipsis at its end, down to a small share of the width) and the last
///     segment - the repo's folder name - is shown whole while it fits, and otherwise gets a
///     <em>middle</em> ellipsis that keeps both the start and the end of the folder name.
/// </summary>
/// <remarks>
///     The control measures the real text, so the result follows the font, the font size and the
///     width the layout gives it. It only decides what is drawn; the full path stays in the card's
///     tooltip and accessible name, which are set on the control by the card template.
/// </remarks>
internal sealed class PathEllipsisText : Decorator
{
    /// <summary>
    ///     The ellipsis character that marks the removed text.
    /// </summary>
    internal const string Ellipsis = "\u2026";

    /// <summary>
    ///     The share of the available width the folder part may be shortened down to before the
    ///     folder name itself gives way (or the whole folder part when it is shorter than that).
    /// </summary>
    internal const double ParentShare = 0.35;

    /// <summary>
    ///     Identifies the <see cref="ParentText"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> ParentTextProperty =
        AvaloniaProperty.Register<PathEllipsisText, string?>(nameof(ParentText));

    /// <summary>
    ///     Identifies the <see cref="LeafText"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> LeafTextProperty =
        AvaloniaProperty.Register<PathEllipsisText, string?>(nameof(LeafText));

    /// <summary>
    ///     The text that draws the path (also used to measure candidates).
    /// </summary>
    private readonly TextBlock _text = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        TextTrimming = Avalonia.Media.TextTrimming.None,
    };

    /// <summary>
    ///     Initializes static members of the <see cref="PathEllipsisText"/> class.
    /// </summary>
    static PathEllipsisText()
    {
        AffectsMeasure<PathEllipsisText>(ParentTextProperty, LeafTextProperty);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PathEllipsisText"/> class.
    /// </summary>
    public PathEllipsisText()
    {
        // The card's path control carries the full path as its accessible name, so the drawn
        // (possibly shortened) text must not be read a second time, in pieces
        AutomationProperties.SetAccessibilityView(_text, AccessibilityView.Raw);
        ClipToBounds = true;
        Child = _text;
    }

    /// <summary>
    ///     Gets or sets the part of the path before its last separator (without it); shortened at
    ///     its end first.
    /// </summary>
    public string? ParentText
    {
        get => GetValue(ParentTextProperty);
        set => SetValue(ParentTextProperty, value);
    }

    /// <summary>
    ///     Gets or sets the last segment of the path with the separator before it; shown whole when
    ///     it fits and otherwise shortened in the middle.
    /// </summary>
    public string? LeafText
    {
        get => GetValue(LeafTextProperty);
        set => SetValue(LeafTextProperty, value);
    }

    /// <summary>
    ///     Gets the text that is drawn now: the whole path or its shortened form.
    /// </summary>
    internal string DisplayText => _text.Text ?? string.Empty;

    /// <summary>
    ///     Keeps the first and the last characters of a text, with an ellipsis between them.
    /// </summary>
    /// <param name="text">The text to shorten.</param>
    /// <param name="keep">The number of characters to keep in total (the head gets the extra one
    ///     of an odd number).</param>
    /// <returns>The shortened text, the text itself when it has no more than
    ///     <paramref name="keep"/> characters, or just the ellipsis when nothing is kept.</returns>
    internal static string MiddleEllipsis(string text, int keep)
    {
        if (text.Length <= keep)
        {
            return text;
        }

        if (keep <= 0)
        {
            return Ellipsis;
        }

        var head = (keep + 1) / 2;
        var tail = keep / 2;

        // Never cut a surrogate pair in two
        if (head > 0 && char.IsHighSurrogate(text[head - 1]))
        {
            head--;
        }

        if (tail > 0 && char.IsLowSurrogate(text[^tail]))
        {
            tail--;
        }

        return text[..head] + Ellipsis + text[^tail..];
    }

    /// <summary>
    ///     Keeps the first characters of a text, with an ellipsis after them.
    /// </summary>
    /// <param name="text">The text to shorten.</param>
    /// <param name="keep">The number of characters to keep.</param>
    /// <returns>The shortened text, the text itself when it has no more than
    ///     <paramref name="keep"/> characters, or just the ellipsis when nothing is kept.</returns>
    internal static string EndEllipsis(string text, int keep)
    {
        if (text.Length <= keep)
        {
            return text;
        }

        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
        {
            keep--;
        }

        return keep <= 0 ? Ellipsis : text[..keep] + Ellipsis;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Text = Fit(ParentText ?? string.Empty, LeafText ?? string.Empty, availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    /// <summary>
    ///     Chooses what to draw for a width.
    /// </summary>
    /// <param name="parent">The folder part.</param>
    /// <param name="leaf">The folder name with its separator.</param>
    /// <param name="available">The width the layout offers.</param>
    /// <returns>The path as it should be drawn.</returns>
    private string Fit(string parent, string leaf, double available)
    {
        var full = parent + leaf;
        if (double.IsInfinity(available) || double.IsNaN(available) || WidthOf(full) <= available)
        {
            return full;
        }

        // The folder part may give way down to a share of the width; the folder name gets the rest
        var parentFloor = Math.Min(WidthOf(parent), available * ParentShare);
        var leafBudget = available - parentFloor;
        var shownLeaf = WidthOf(leaf) <= leafBudget
            ? leaf
            : Shorten(leaf, leafBudget, MiddleEllipsis);

        // Whatever the folder name leaves is the folder part's, shortened at its end
        var parentBudget = available - WidthOf(shownLeaf);
        var shownParent = parent.Length == 0 ? string.Empty : Shorten(parent, parentBudget, EndEllipsis);
        return shownParent + shownLeaf;
    }

    /// <summary>
    ///     Finds the longest shortened form of a text that fits a width (binary search over the
    ///     number of kept characters; the width grows with it).
    /// </summary>
    /// <param name="text">The text to shorten; it does not fit.</param>
    /// <param name="budget">The width to fit.</param>
    /// <param name="shorten">Builds the shortened text for a number of kept characters.</param>
    /// <returns>The longest form that fits, or just the ellipsis when none does.</returns>
    private string Shorten(string text, double budget, Func<string, int, string> shorten)
    {
        var low = 0;
        var high = text.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (WidthOf(shorten(text, middle)) <= budget)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return shorten(text, low);
    }

    /// <summary>
    ///     Measures how wide a text is in the control's font.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The width of the text on one line.</returns>
    private double WidthOf(string text)
    {
        _text.Text = text;
        _text.Measure(Size.Infinity);
        return _text.DesiredSize.Width;
    }
}
