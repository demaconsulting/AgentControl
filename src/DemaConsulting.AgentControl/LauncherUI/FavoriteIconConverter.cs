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
using Avalonia.Data.Converters;
using Material.Icons;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Converts <see cref="RepoCardViewModel.IsFavorite"/>'s <see langword="bool"/> value into
///     the filled or outlined star <see cref="MaterialIconKind"/> shown on the repo card's
///     favorite <c>ToggleButton</c>, so the glyph itself communicates the toggle state instead
///     of relying solely on the button's checked visual style.
/// </summary>
internal sealed class FavoriteIconConverter : IValueConverter
{
    /// <summary>
    ///     A shared, stateless instance for use as a XAML <c>StaticResource</c>.
    /// </summary>
    public static readonly FavoriteIconConverter Instance = new();

    /// <summary>
    ///     Converts a favorite <see langword="bool"/> to <see cref="MaterialIconKind.Star"/>
    ///     (favorited) or <see cref="MaterialIconKind.StarOutline"/> (not favorited).
    /// </summary>
    /// <param name="value">The bound <see langword="bool"/> value (expected to be
    ///     <see cref="RepoCardViewModel.IsFavorite"/>).</param>
    /// <param name="targetType">The binding target type (unused).</param>
    /// <param name="parameter">The converter parameter (unused).</param>
    /// <param name="culture">The culture to use (unused).</param>
    /// <returns><see cref="MaterialIconKind.Star"/> if <paramref name="value"/> is
    ///     <see langword="true"/>; otherwise <see cref="MaterialIconKind.StarOutline"/>.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? MaterialIconKind.Star : MaterialIconKind.StarOutline;
    }

    /// <summary>
    ///     Not supported: this converter is used one-way (icon reflects state; the
    ///     <c>ToggleButton</c>'s own <c>IsChecked</c> binding, not the icon, drives state changes).
    /// </summary>
    /// <param name="value">Unused.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"{nameof(FavoriteIconConverter)} does not support ConvertBack.");
    }
}
