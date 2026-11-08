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
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     Converts a <see cref="ThemePreference"/> into the friendly name the Settings window's
///     Appearance picker shows (for example <c>System default</c> instead of <c>System</c>), so the
///     picker reads as plain words while the persisted value stays the enum's name.
/// </summary>
internal sealed class ThemePreferenceDisplayNameConverter : IValueConverter
{
    /// <summary>
    ///     A shared, stateless instance for use from XAML.
    /// </summary>
    public static readonly ThemePreferenceDisplayNameConverter Instance = new();

    /// <summary>
    ///     Gets the friendly name of a theme choice.
    /// </summary>
    /// <param name="preference">The theme choice.</param>
    /// <returns>The name shown in the picker; the enum's own name for a value this method does not
    ///     know, so a choice added later is still shown (just unpolished) rather than blank.</returns>
    public static string GetDisplayName(ThemePreference preference) => preference switch
    {
        ThemePreference.System => "System default",
        ThemePreference.Light => "Light",
        ThemePreference.Dark => "Dark",
        _ => preference.ToString()
    };

    /// <summary>
    ///     Converts a <see cref="ThemePreference"/> to its friendly name.
    /// </summary>
    /// <param name="value">The bound value (expected to be a <see cref="ThemePreference"/>).</param>
    /// <param name="targetType">The binding target type (unused).</param>
    /// <param name="parameter">The converter parameter (unused).</param>
    /// <param name="culture">The culture to use (unused).</param>
    /// <returns>The friendly name, or an empty string when <paramref name="value"/> is not a
    ///     <see cref="ThemePreference"/>.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ThemePreference preference ? GetDisplayName(preference) : string.Empty;
    }

    /// <summary>
    ///     Not supported: the picker's selected item is bound to the enum itself, so this converter is
    ///     only used to display items.
    /// </summary>
    /// <param name="value">Unused.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="culture">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException($"{nameof(ThemePreferenceDisplayNameConverter)} does not support ConvertBack.");
    }
}
