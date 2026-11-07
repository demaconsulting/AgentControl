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

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     The modifier keys of a key chord.
/// </summary>
[Flags]
internal enum KeyChordModifiers
{
    /// <summary>
    ///     No modifier.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The Shift key.
    /// </summary>
    Shift = 1,

    /// <summary>
    ///     The Ctrl key.
    /// </summary>
    Control = 2,

    /// <summary>
    ///     The Alt key.
    /// </summary>
    Alt = 4,
}

/// <summary>
///     A key and the modifiers held while it is pressed, for example <c>Ctrl+Shift+Z</c>.
/// </summary>
/// <param name="KeyName">The canonical name of the key (for example <c>Z</c>, <c>F2</c>, <c>Delete</c>).</param>
/// <param name="Key">The virtual key to press.</param>
/// <param name="Modifiers">The modifiers to hold.</param>
internal readonly record struct KeyChordSpec(string KeyName, VirtualKeyShort Key, KeyChordModifiers Modifiers)
{
    /// <summary>
    ///     Gets a value indicating whether any modifier is held.
    /// </summary>
    internal bool HasModifiers => Modifiers != KeyChordModifiers.None;

    /// <summary>
    ///     Formats the chord the way it is parsed, for example <c>Ctrl+Shift+Z</c>.
    /// </summary>
    /// <returns>The chord text.</returns>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(KeyChordModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(KeyChordModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(KeyChordModifiers.Alt))
        {
            parts.Add("Alt");
        }

        parts.Add(KeyName);
        return string.Join("+", parts);
    }
}

/// <summary>
///     Parses key chords such as <c>Ctrl+C</c>, <c>F2</c>, <c>Delete</c> or <c>Ctrl+Enter</c>.
/// </summary>
internal static class KeyChord
{
    /// <summary>
    ///     The help text listing what can be pressed.
    /// </summary>
    internal const string UsageText =
        "Keys: A-Z, 0-9, F1-F12, Enter, Escape, Tab, Delete, Backspace, Space, Insert, Home, End, PageUp, PageDown, " +
        "Left, Right, Up, Down, Plus, Minus; modifiers: Ctrl, Shift, Alt (joined with '+', for example Ctrl+Shift+Z).";

    /// <summary>
    ///     The named (non letter, non digit, non function) keys and their aliases.
    /// </summary>
    private static readonly Dictionary<string, (string Name, VirtualKeyShort Key)> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = ("Enter", VirtualKeyShort.RETURN),
        ["Return"] = ("Enter", VirtualKeyShort.RETURN),
        ["Escape"] = ("Escape", VirtualKeyShort.ESCAPE),
        ["Esc"] = ("Escape", VirtualKeyShort.ESCAPE),
        ["Tab"] = ("Tab", VirtualKeyShort.TAB),
        ["Delete"] = ("Delete", VirtualKeyShort.DELETE),
        ["Del"] = ("Delete", VirtualKeyShort.DELETE),
        ["Backspace"] = ("Backspace", VirtualKeyShort.BACK),
        ["Space"] = ("Space", VirtualKeyShort.SPACE),
        ["Insert"] = ("Insert", VirtualKeyShort.INSERT),
        ["Home"] = ("Home", VirtualKeyShort.HOME),
        ["End"] = ("End", VirtualKeyShort.END),
        ["PageUp"] = ("PageUp", VirtualKeyShort.PRIOR),
        ["PageDown"] = ("PageDown", VirtualKeyShort.NEXT),
        ["Left"] = ("Left", VirtualKeyShort.LEFT),
        ["Right"] = ("Right", VirtualKeyShort.RIGHT),
        ["Up"] = ("Up", VirtualKeyShort.UP),
        ["Down"] = ("Down", VirtualKeyShort.DOWN),
        ["Plus"] = ("Plus", VirtualKeyShort.OEM_PLUS),
        ["Minus"] = ("Minus", VirtualKeyShort.OEM_MINUS),
    };

    /// <summary>
    ///     Parses a chord.
    /// </summary>
    /// <param name="input">The chord text, modifiers first, joined with <c>+</c>.</param>
    /// <param name="chord">The parsed chord when the result is <see langword="true"/>.</param>
    /// <param name="error">Why the text was rejected when the result is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text is a valid chord.</returns>
    internal static bool TryParse(string? input, out KeyChordSpec chord, out string error)
    {
        chord = default;
        error = UsageText;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = KeyChordModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!TryParseModifier(parts[i], out var modifier))
            {
                error = $"'{parts[i]}' is not a supported modifier. {UsageText}";
                return false;
            }

            modifiers |= modifier;
        }

        if (!TryParseKey(parts[^1], out var name, out var key))
        {
            error = $"'{parts[^1]}' is not a supported key. {UsageText}";
            return false;
        }

        chord = new KeyChordSpec(name, key, modifiers);
        error = string.Empty;
        return true;
    }

    /// <summary>
    ///     Parses one modifier name.
    /// </summary>
    /// <param name="text">The text, such as <c>Ctrl</c>.</param>
    /// <param name="modifier">The modifier.</param>
    /// <returns><see langword="true"/> when the text names a modifier.</returns>
    private static bool TryParseModifier(string text, out KeyChordModifiers modifier)
    {
        modifier = text.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => KeyChordModifiers.Control,
            "SHIFT" => KeyChordModifiers.Shift,
            "ALT" => KeyChordModifiers.Alt,
            _ => KeyChordModifiers.None,
        };
        return modifier != KeyChordModifiers.None;
    }

    /// <summary>
    ///     Parses the key part of a chord.
    /// </summary>
    /// <param name="text">The key text.</param>
    /// <param name="name">The canonical key name.</param>
    /// <param name="key">The virtual key.</param>
    /// <returns><see langword="true"/> when the text names a key.</returns>
    private static bool TryParseKey(string text, out string name, out VirtualKeyShort key)
    {
        if (NamedKeys.TryGetValue(text, out var named))
        {
            (name, key) = named;
            return true;
        }

        var upper = text.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is >= 'A' and <= 'Z')
        {
            name = upper;
            return Enum.TryParse("KEY_" + upper, out key);
        }

        if (upper.Length == 1 && upper[0] is >= '0' and <= '9')
        {
            name = upper;
            return Enum.TryParse("KEY_" + upper, out key);
        }

        if (upper[0] == 'F' && int.TryParse(upper.AsSpan(1), out var number) && number is >= 1 and <= 12)
        {
            name = upper;
            return Enum.TryParse(upper, out key);
        }

        name = string.Empty;
        key = default;
        return false;
    }
}
