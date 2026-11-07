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
///     Unit tests for <see cref="AgentToolKindDisplayNameConverter"/>, which gives the agent tool
///     picker friendly names without changing the persisted enum values.
/// </summary>
public class AgentToolKindDisplayNameConverterTests
{
    /// <summary>
    ///     Test that every tool the picker offers has a friendly name that is not the raw enum name.
    /// </summary>
    [Theory]
    [InlineData("CopilotCli", "GitHub Copilot CLI")]
    [InlineData("Cursor", "Cursor")]
    [InlineData("ClaudeCode", "Claude Code")]
    [InlineData("Custom", "Custom command")]
    public void AgentToolKindDisplayNameConverter_Convert_KnownTool_ReturnsFriendlyName(
        string kindName, string expected)
    {
        // Arrange
        var kind = Enum.Parse<AgentToolKind>(kindName);

        // Act
        var text = AgentToolKindDisplayNameConverter.Instance.Convert(
            kind, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, text);
        Assert.Equal(expected, AgentToolKindDisplayNameConverter.GetDisplayName(kind));
    }

    /// <summary>
    ///     Test that every choice the Settings window offers has a friendly name that reads as words
    ///     (not an unknown value falling back to its enum name), so a tool
    ///     added to the picker later cannot silently show an unpolished name.
    /// </summary>
    [Fact]
    public void AgentToolKindDisplayNameConverter_GetDisplayName_EveryAvailableTool_ReadsAsWords()
    {
        // Act / Assert
        foreach (var kind in SettingsWindowViewModel.AvailableAgentTools)
        {
            var name = AgentToolKindDisplayNameConverter.GetDisplayName(kind);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.DoesNotContain("CopilotCli", name, StringComparison.Ordinal);
            Assert.DoesNotContain("ClaudeCode", name, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Test that a value that is not a tool converts to empty text instead of failing.
    /// </summary>
    [Fact]
    public void AgentToolKindDisplayNameConverter_Convert_NotATool_ReturnsEmpty()
    {
        // Act
        var text = AgentToolKindDisplayNameConverter.Instance.Convert(
            null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(string.Empty, text);
    }

    /// <summary>
    ///     Test that converting back is refused, since the picker binds the enum itself.
    /// </summary>
    [Fact]
    public void AgentToolKindDisplayNameConverter_ConvertBack_ThrowsNotSupported()
    {
        // Act / Assert
        Assert.Throws<NotSupportedException>(() =>
            AgentToolKindDisplayNameConverter.Instance.ConvertBack(
                "Cursor", typeof(AgentToolKind), null, CultureInfo.InvariantCulture));
    }
}
