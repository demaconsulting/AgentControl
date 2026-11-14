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

using FlaUI.Core.WindowsAPI;

namespace DemaConsulting.AgentControl.Puppet.Tests;

/// <summary>
///     Tests for the command-line parsing and key-chord parsing of the Puppet (no UI is touched).
/// </summary>
public class CommandLineTests
{
    /// <summary>
    ///     Validates that the command name, positional arguments and valued options are separated.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_SplitsCommandPositionalAndOptions()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["type", "FilterTextBox", "my repo", "--instance", "a"]);

        // Assert
        Assert.Multiple(
            () => Assert.Equal("type", parsed.CommandName),
            () => Assert.Equal(["FilterTextBox", "my repo"], parsed.Positional),
            () => Assert.Equal("a", parsed.GetOption("instance")),
            () => Assert.Equal("a", parsed.Instance));
    }

    /// <summary>
    ///     Validates that no arguments give an empty command and the default instance alias.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_NoArguments_IsEmptyWithDefaultInstance()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse([]);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(string.Empty, parsed.CommandName),
            () => Assert.Empty(parsed.Positional),
            () => Assert.Equal("main", parsed.Instance));
    }

    /// <summary>
    ///     Validates that a misspelled option is reported instead of silently ignored.
    /// </summary>
    [Fact]
    public void CommandOptions_FindUnknown_MisspelledInstanceOption_IsReported()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["click", "Button", "--instnace", "other"]);

        // Act / Assert
        Assert.Equal("instnace", CommandOptions.FindUnknown(parsed));
    }

    /// <summary>
    ///     Validates that the options a command documents are all accepted.
    /// </summary>
    [Theory]
    [InlineData("launch", "--instance", "a", "--exe", "x.exe", "--config-dir", "c", "--git-path", "g", "--agent-tool-command", "t", "--allow-existing")]
    [InlineData("tree", "--depth", "3", "--under", "id", "--item", "r", "--window", "w", "--trace")]
    [InlineData("click-at", "1", "2", "--right", "--double", "--shift", "--ctrl")]
    [InlineData("wheel", "3", "--ctrl", "--on", "id", "--item", "r")]
    [InlineData("hover", "id", "--wait", "100", "--shot", "a.png")]
    [InlineData("place", "--x", "1", "--y", "2", "--width", "3", "--height", "4")]
    [InlineData("fixture", "dir", "--packages", "both", "--repo", "--pin", "p@1")]
    [InlineData("quit", "--kill")]
    public void CommandOptions_FindUnknown_DocumentedOptions_AreAccepted(params string[] args)
    {
        // Arrange
        var parsed = CommandLineParser.Parse(args);

        // Act / Assert
        Assert.Null(CommandOptions.FindUnknown(parsed));
    }

    /// <summary>
    ///     Validates that an option of another command is refused.
    /// </summary>
    [Fact]
    public void CommandOptions_FindUnknown_OptionOfAnotherCommand_IsReported()
    {
        // Arrange
        var parsed = CommandLineParser.Parse(["instances", "--kill"]);

        // Act / Assert
        Assert.Equal("kill", CommandOptions.FindUnknown(parsed));
    }

    /// <summary>
    ///     Validates that wheel notches must be whole numbers within a safe range.
    /// </summary>
    [Theory]
    [InlineData("-2147483648")]
    [InlineData("2147483647")]
    [InlineData("1001")]
    [InlineData("1.5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e3")]
    public void Commands_WheelNotches_UnsafeValue_IsUsageError(string text)
    {
        // Act / Assert
        var ex = Assert.Throws<PuppetException>(() => Commands.WheelNotches(text));
        Assert.Equal(ExitCodes.UsageError, ex.ExitCode);
    }

    /// <summary>
    ///     Validates that notches within range, positive or negative, are accepted.
    /// </summary>
    [Theory]
    [InlineData("3", 3)]
    [InlineData("-5", -5)]
    [InlineData("1000", 1000)]
    [InlineData("-1000", -1000)]
    public void Commands_WheelNotches_ValidValue_IsParsed(string text, int expected) =>
        Assert.Equal(expected, Commands.WheelNotches(text));

    /// <summary>
    ///     Validates that the scope options <c>--item</c> and <c>--window</c> take their values.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_ScopeOptions_TakeValues()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["click", "LaunchButton", "--item", "my repo", "--window", "Agent Control Settings"]);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(["LaunchButton"], parsed.Positional),
            () => Assert.Equal("my repo", parsed.GetOption("item")),
            () => Assert.Equal("Agent Control Settings", parsed.GetOption("window")));
    }

    /// <summary>
    ///     Validates that known flags do not swallow the argument after them.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_FlagBeforePositional_KeepsPositional()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["fixture", "--repo", "C:\\temp\\fx"]);

        // Assert
        Assert.Multiple(
            () => Assert.True(parsed.HasOption("repo")),
            () => Assert.Null(parsed.GetOption("repo")),
            () => Assert.Equal(["C:\\temp\\fx"], parsed.Positional));
    }

    /// <summary>
    ///     Validates that the click flags take no value, so the coordinates after them stay positional.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_ClickFlags_DoNotSwallowPositionals()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["click-at", "--double", "--right", "645", "360"]);

        // Assert
        Assert.Multiple(
            () => Assert.True(parsed.HasOption("double")),
            () => Assert.True(parsed.HasOption("right")),
            () => Assert.Equal(["645", "360"], parsed.Positional));
    }

    /// <summary>
    ///     Validates that negative numbers are positional arguments, not options.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_NegativeNumbers_ArePositional()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["drag-element", "Splitter", "-40", "25", "--ctrl"]);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(["Splitter", "-40", "25"], parsed.Positional),
            () => Assert.True(parsed.HasOption("ctrl")));
    }

    /// <summary>
    ///     Validates that an option value may start with a single dash and that option names are case-insensitive.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_AtOptionWithNegativeValue_IsKept()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["wheel", "3", "--at", "-10,20", "--CTRL"]);

        // Assert
        Assert.Multiple(
            () => Assert.Equal("-10,20", parsed.GetOption("AT")),
            () => Assert.True(parsed.HasOption("ctrl")),
            () => Assert.Equal(["3"], parsed.Positional));
    }

    /// <summary>
    ///     Validates that an option at the end of the line with no value is a flag.
    /// </summary>
    [Fact]
    public void CommandLineParser_Parse_TrailingOptionWithoutValue_IsFlag()
    {
        // Arrange / Act
        var parsed = CommandLineParser.Parse(["quit", "--whatever"]);

        // Assert
        Assert.Multiple(
            () => Assert.True(parsed.HasOption("whatever")),
            () => Assert.Null(parsed.GetOption("whatever")));
    }

    /// <summary>
    ///     Validates that simple chords parse to the expected virtual keys and canonical names.
    /// </summary>
    /// <param name="text">The chord text.</param>
    /// <param name="name">The expected canonical key name.</param>
    /// <param name="key">The expected virtual key.</param>
    [Theory]
    [InlineData("Delete", "Delete", VirtualKeyShort.DELETE)]
    [InlineData("del", "Delete", VirtualKeyShort.DELETE)]
    [InlineData("F2", "F2", VirtualKeyShort.F2)]
    [InlineData("f12", "F12", VirtualKeyShort.F12)]
    [InlineData("Escape", "Escape", VirtualKeyShort.ESCAPE)]
    [InlineData("esc", "Escape", VirtualKeyShort.ESCAPE)]
    [InlineData("Enter", "Enter", VirtualKeyShort.RETURN)]
    [InlineData("z", "Z", VirtualKeyShort.KEY_Z)]
    [InlineData("7", "7", VirtualKeyShort.KEY_7)]
    [InlineData("PageDown", "PageDown", VirtualKeyShort.NEXT)]
    [InlineData("Plus", "Plus", VirtualKeyShort.OEM_PLUS)]
    public void KeyChord_TryParse_SingleKey_ParsesWithoutModifiers(string text, string name, VirtualKeyShort key)
    {
        // Arrange / Act
        var ok = KeyChord.TryParse(text, out var chord, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(name, chord.KeyName),
            () => Assert.Equal(key, chord.Key),
            () => Assert.False(chord.HasModifiers));
    }

    /// <summary>
    ///     Validates that modifiers are collected in any order and formatted canonically.
    /// </summary>
    [Fact]
    public void KeyChord_TryParse_Modifiers_AreCombinedAndFormatted()
    {
        // Arrange / Act
        var ok = KeyChord.TryParse("shift+Control+z", out var chord, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok, error),
            () => Assert.Equal(KeyChordModifiers.Control | KeyChordModifiers.Shift, chord.Modifiers),
            () => Assert.Equal("Ctrl+Shift+Z", chord.ToString()));
    }

    /// <summary>
    ///     Validates that Ctrl+Enter parses.
    /// </summary>
    [Fact]
    public void KeyChord_TryParse_CtrlEnter_Parses()
    {
        // Arrange / Act
        var ok = KeyChord.TryParse("Ctrl+Enter", out var chord, out _);

        // Assert
        Assert.Multiple(
            () => Assert.True(ok),
            () => Assert.Equal(VirtualKeyShort.RETURN, chord.Key),
            () => Assert.Equal(KeyChordModifiers.Control, chord.Modifiers),
            () => Assert.Equal("Ctrl+Enter", chord.ToString()));
    }

    /// <summary>
    ///     Validates that bad chords are rejected with the usage text.
    /// </summary>
    /// <param name="text">The chord text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hyper+C")]
    [InlineData("Ctrl+Banana")]
    [InlineData("F13")]
    [InlineData("Ctrl+")]
    public void KeyChord_TryParse_Invalid_IsRejected(string text)
    {
        // Arrange / Act
        var ok = KeyChord.TryParse(text, out _, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.False(ok),
            () => Assert.Contains("Keys:", error, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that a null chord is rejected.
    /// </summary>
    [Fact]
    public void KeyChord_TryParse_Null_IsRejected()
    {
        // Arrange / Act
        var ok = KeyChord.TryParse(null, out _, out var error);

        // Assert
        Assert.Multiple(
            () => Assert.False(ok),
            () => Assert.NotEmpty(error));
    }
}
