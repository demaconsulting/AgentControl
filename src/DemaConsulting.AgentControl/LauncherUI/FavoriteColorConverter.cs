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
using Avalonia.Media;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Converts <see cref="RepoCardViewModel.IsFavorite"/>'s <see langword="bool"/> value into
///     the <see cref="IBrush"/> used to color the repo card's favorite-star glyph, so a
///     favorited repo's star reads as "activated" (gold) rather than relying solely on the
///     filled-vs-outline glyph shape from <see cref="FavoriteIconConverter"/>.
/// </summary>
internal sealed class FavoriteColorConverter : IValueConverter
{
    /// <summary>
    ///     A shared, stateless instance for use as a XAML <c>StaticResource</c>.
    /// </summary>
    public static readonly FavoriteColorConverter Instance = new();

    /// <summary>
    ///     The gold color used for a favorited repo's star, chosen for enough contrast against
    ///     both light and dark theme variants.
    /// </summary>
    private static readonly IBrush FavoriteBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));

    /// <summary>
    ///     Converts a favorite <see langword="bool"/> to <see cref="FavoriteBrush"/> (favorited)
    ///     or <see langword="null"/> (not favorited, so the glyph falls back to its default
    ///     theme foreground).
    /// </summary>
    /// <param name="value">The bound <see langword="bool"/> value (expected to be
    ///     <see cref="RepoCardViewModel.IsFavorite"/>).</param>
    /// <param name="targetType">The binding target type (unused).</param>
    /// <param name="parameter">The converter parameter (unused).</param>
    /// <param name="culture">The culture to use (unused).</param>
    /// <returns><see cref="FavoriteBrush"/> if <paramref name="value"/> is
    ///     <see langword="true"/>; otherwise <see cref="AvaloniaProperty.UnsetValue"/>, so the
    ///     binding clears back to the icon's normal inherited/themed foreground instead of
    ///     forcing an explicit <see langword="null"/> brush (which paints nothing at all).</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? FavoriteBrush : AvaloniaProperty.UnsetValue;
    }

    /// <summary>    ///     Not supported: this converter is used one-way (color reflects state; the
    ///     <c>ToggleButton</c>'s own <c>IsChecked</c> binding, not the color, drives state
    ///     changes).
    /// </summary>
    /// <param name="value">Unused.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"{nameof(FavoriteColorConverter)} does not support ConvertBack.");
    }
}
