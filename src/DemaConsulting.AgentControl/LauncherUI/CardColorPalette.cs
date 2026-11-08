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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     One color the user can choose for a repo card's tile.
/// </summary>
/// <param name="Name">The stable name that is stored in the settings and shown as the swatch's
///     tooltip and accessible name, for example <c>Teal</c>.</param>
/// <param name="Index">The position in the theme-aware avatar palette (the resources
///     <c>AvatarBackground{Index}</c> and <c>AvatarForeground{Index}</c>, defined for both themes).</param>
internal sealed record CardColor(string Name, int Index);

/// <summary>
///     The curated set of colors a user can give a repo card's tile, and how a stored color name
///     is understood.
/// </summary>
/// <remarks>
///     The palette is the same one the automatic color is drawn from, so a chosen color never looks
///     foreign next to an automatic one. Each entry's fill and the color of the letters on it are
///     resources defined for the light and the dark theme (the letters were chosen, and are checked
///     by a test, for at least 4.5:1 contrast with the fill in both themes), so this unit carries no
///     color values. Entries are looked up by name, never by position, so reordering the palette in a
///     later version cannot change anyone's choice. Stateless and thread-safe.
/// </remarks>
internal static class CardColorPalette
{
    /// <summary>
    ///     The entries in the order the palette popup shows them; an entry's position is its
    ///     palette index.
    /// </summary>
    private static readonly CardColor[] Entries =
    [
        new("Blue", 0),
        new("Violet", 1),
        new("Magenta", 2),
        new("Green", 3),
        new("Amber", 4),
        new("Teal", 5),
        new("Orange", 6),
        new("Indigo", 7),
        new("Red", 8),
        new("Lime", 9)
    ];

    /// <summary>
    ///     Gets the number of colors in the palette.
    /// </summary>
    public static int Count => Entries.Length;

    /// <summary>
    ///     Gets every color, in the order the palette popup shows them.
    /// </summary>
    public static IReadOnlyList<CardColor> All => Entries;

    /// <summary>
    ///     Finds the palette color a stored name refers to.
    /// </summary>
    /// <param name="name">The stored name; may be <see langword="null"/>, blank or unknown. Leading
    ///     and trailing white space and letter case are ignored.</param>
    /// <returns>The color, or <see langword="null"/> when there is none - which means the
    ///     automatic color, so an unknown name (a typo, or a color from a newer version) is never an
    ///     error.</returns>
    public static CardColor? Find(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : Entries.FirstOrDefault(entry => string.Equals(entry.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
