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
///     Converts an <see cref="AgentToolKind"/> into the friendly name the Settings window's agent tool
///     picker shows (for example <c>GitHub Copilot CLI</c> instead of <c>CopilotCli</c>), so the picker
///     reads as plain words while the persisted value stays the enum's name.
/// </summary>
internal sealed class AgentToolKindDisplayNameConverter : IValueConverter
{
    /// <summary>
    ///     A shared, stateless instance for use from XAML.
    /// </summary>
    public static readonly AgentToolKindDisplayNameConverter Instance = new();

    /// <summary>
    ///     Gets the friendly name of an agent tool choice.
    /// </summary>
    /// <param name="kind">The agent tool.</param>
    /// <returns>The name shown in the picker; the enum's own name for a value this method does not
    ///     know, so a tool added later is still shown (just unpolished) rather than blank.</returns>
    public static string GetDisplayName(AgentToolKind kind) => kind switch
    {
        AgentToolKind.CopilotCli => "GitHub Copilot CLI",
        AgentToolKind.Cursor => "Cursor",
        AgentToolKind.ClaudeCode => "Claude Code",
        AgentToolKind.Custom => "Custom command",
        _ => kind.ToString()
    };

    /// <summary>
    ///     Converts an <see cref="AgentToolKind"/> to its friendly name.
    /// </summary>
    /// <param name="value">The bound value (expected to be an <see cref="AgentToolKind"/>).</param>
    /// <param name="targetType">The binding target type (unused).</param>
    /// <param name="parameter">The converter parameter (unused).</param>
    /// <param name="culture">The culture to use (unused).</param>
    /// <returns>The friendly name, or an empty string when <paramref name="value"/> is not an
    ///     <see cref="AgentToolKind"/>.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AgentToolKind kind ? GetDisplayName(kind) : string.Empty;
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
        throw new NotSupportedException($"{nameof(AgentToolKindDisplayNameConverter)} does not support ConvertBack.");
    }
}
