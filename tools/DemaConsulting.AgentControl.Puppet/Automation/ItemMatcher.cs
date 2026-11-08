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

namespace DemaConsulting.AgentControl.Puppet.Automation;

/// <summary>
///     Picks one item of a list by the text it shows or by its position.
/// </summary>
internal static class ItemMatcher
{
    /// <summary>
    ///     Resolves an item specification against the texts of a list's items.
    /// </summary>
    /// <remarks>
    ///     The specification is tried as an exact text (ignoring case), then as a zero-based index, then as a part of a
    ///     text. A text or part that fits several items is an error rather than a guess.
    /// </remarks>
    /// <param name="itemTexts">For every item, the texts it shows (the first is the one listed in messages).</param>
    /// <param name="spec">The text or index asked for.</param>
    /// <param name="index">The zero-based index of the item when the result is <see langword="true"/>.</param>
    /// <param name="error">Why no single item was found when the result is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when exactly one item fits.</returns>
    internal static bool TryResolve(IReadOnlyList<IReadOnlyList<string>> itemTexts, string spec, out int index, out string error)
    {
        ArgumentNullException.ThrowIfNull(itemTexts);
        ArgumentNullException.ThrowIfNull(spec);
        index = -1;
        var wanted = spec.Trim();
        var exact = Matching(itemTexts, text => string.Equals(text.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (exact.Count > 0)
        {
            return Single(exact, itemTexts, wanted, out index, out error);
        }

        if (int.TryParse(wanted, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
        {
            if (position < itemTexts.Count)
            {
                index = position;
                error = string.Empty;
                return true;
            }

            error = $"index {position} is out of range ({itemTexts.Count} item(s): {Describe(itemTexts)}).";
            return false;
        }

        var partial = wanted.Length == 0
            ? []
            : Matching(itemTexts, text => text.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (partial.Count > 0)
        {
            return Single(partial, itemTexts, wanted, out index, out error);
        }

        error = itemTexts.Count == 0
            ? "the list has no items."
            : $"no item matches \"{wanted}\" (items: {Describe(itemTexts)}).";
        return false;
    }

    /// <summary>
    ///     Lists the indexes of the items with a text that satisfies a test.
    /// </summary>
    /// <param name="itemTexts">The items' texts.</param>
    /// <param name="test">The test.</param>
    /// <returns>The indexes.</returns>
    private static List<int> Matching(IReadOnlyList<IReadOnlyList<string>> itemTexts, Func<string, bool> test) =>
        [.. Enumerable.Range(0, itemTexts.Count).Where(i => itemTexts[i].Any(test))];

    /// <summary>
    ///     Accepts a match list only when it holds exactly one item.
    /// </summary>
    /// <param name="matches">The matching indexes (at least one).</param>
    /// <param name="itemTexts">The items' texts.</param>
    /// <param name="wanted">The specification.</param>
    /// <param name="index">The index when the result is <see langword="true"/>.</param>
    /// <param name="error">The reason when the result is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when there is exactly one match.</returns>
    private static bool Single(List<int> matches, IReadOnlyList<IReadOnlyList<string>> itemTexts, string wanted, out int index, out string error)
    {
        if (matches.Count == 1)
        {
            index = matches[0];
            error = string.Empty;
            return true;
        }

        index = -1;
        error = $"\"{wanted}\" fits {matches.Count} items (indexes {string.Join(", ", matches)}: {string.Join(", ", matches.Select(i => First(itemTexts[i])))}); use the index.";
        return false;
    }

    /// <summary>
    ///     Lists the first text of every item for a message.
    /// </summary>
    /// <param name="itemTexts">The items' texts.</param>
    /// <returns>The description.</returns>
    private static string Describe(IReadOnlyList<IReadOnlyList<string>> itemTexts) =>
        string.Join(", ", itemTexts.Select((texts, i) => $"{i}=\"{First(texts)}\""));

    /// <summary>
    ///     Gets the text an item is listed by.
    /// </summary>
    /// <param name="texts">The item's texts.</param>
    /// <returns>The first text, or an empty string.</returns>
    private static string First(IReadOnlyList<string> texts) => texts.Count > 0 ? texts[0] : string.Empty;
}
