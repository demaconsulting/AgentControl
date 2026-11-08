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
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="ThemePreferenceDisplayNameConverter"/>, which gives the Appearance
///     picker friendly names without changing the persisted enum values.
/// </summary>
public class ThemePreferenceDisplayNameConverterTests
{
    /// <summary>
    ///     Test that every theme the picker offers has a friendly name.
    /// </summary>
    [Theory]
    [InlineData("System", "System default")]
    [InlineData("Light", "Light")]
    [InlineData("Dark", "Dark")]
    public void ThemePreferenceDisplayNameConverter_Convert_KnownTheme_ReturnsFriendlyName(
        string preferenceName, string expected)
    {
        // Arrange
        var preference = Enum.Parse<ThemePreference>(preferenceName);
        // Act
        var text = ThemePreferenceDisplayNameConverter.Instance.Convert(
            preference, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, text);
        Assert.Equal(expected, ThemePreferenceDisplayNameConverter.GetDisplayName(preference));
    }

    /// <summary>
    ///     Test that a value that is not a theme converts to empty text instead of failing, and an
    ///     unknown theme value falls back to its enum name.
    /// </summary>
    [Fact]
    public void ThemePreferenceDisplayNameConverter_Convert_NotATheme_ReturnsEmpty()
    {
        // Act
        var text = ThemePreferenceDisplayNameConverter.Instance.Convert(
            null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(string.Empty, text);
        Assert.Equal("7", ThemePreferenceDisplayNameConverter.GetDisplayName((ThemePreference)7));
    }

    /// <summary>
    ///     Test that converting back is refused, since the picker binds the enum itself.
    /// </summary>
    [Fact]
    public void ThemePreferenceDisplayNameConverter_ConvertBack_ThrowsNotSupported()
    {
        // Act / Assert
        Assert.Throws<NotSupportedException>(() => ThemePreferenceDisplayNameConverter.Instance.ConvertBack(
            "Light", typeof(ThemePreference), null, CultureInfo.InvariantCulture));
    }
}
