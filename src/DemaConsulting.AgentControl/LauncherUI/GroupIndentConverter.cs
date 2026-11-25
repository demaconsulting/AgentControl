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

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Turns the indent of a row of the repo list (<see cref="RepoGroupHeaderViewModel.IndentWidth"/> or
///     <see cref="RepoCardViewModel.IndentWidth"/>, a number) into the margin that draws it that far from
///     the list's left edge, so nested groups and the repos under them are shown as a tree.
/// </summary>
/// <remarks>
///     A converter keeps the view models free of UI types and the card template free of an extra
///     wrapper element. Stateless and thread-safe.
/// </remarks>
internal sealed class GroupIndentConverter : IValueConverter
{
    /// <summary>
    ///     A shared, stateless instance for use from XAML.
    /// </summary>
    public static readonly GroupIndentConverter Instance = new();

    /// <summary>
    ///     Converts an indent to a margin with that left edge.
    /// </summary>
    /// <param name="value">The bound indent in device-independent pixels (a <see cref="double"/>);
    ///     anything else, and a negative number, means no indent.</param>
    /// <param name="targetType">The binding target type (unused).</param>
    /// <param name="parameter">The top margin in device-independent pixels, as a number or as text
    ///     (invariant culture), or <see langword="null"/> or anything unreadable for none.</param>
    /// <param name="culture">The culture to use (unused).</param>
    /// <returns>A <see cref="Thickness"/> with the indent as its left edge, the parameter as its top
    ///     edge and no right or bottom edge.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value is double indent && indent > 0 ? indent : 0;
        var top = parameter switch
        {
            double number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };

        return new Thickness(left, top, 0, 0);
    }

    /// <summary>
    ///     Not supported: the margin is derived from the indent, never the other way round.
    /// </summary>
    /// <param name="value">Unused.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"{nameof(GroupIndentConverter)} does not support ConvertBack.");
    }
}
