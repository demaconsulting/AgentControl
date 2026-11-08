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

using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DemaConsulting.AgentControl.Puppet.Automation;

/// <summary>
///     Narrows where an element lookup searches: one list item (a repo card) and/or one window.
/// </summary>
/// <param name="Item">The <c>--item</c> value (the card's repo name or a zero-based index), or <see langword="null"/>.</param>
/// <param name="Window">The <c>--window</c> value (a window title), or <see langword="null"/> for every window of the instance.</param>
/// <param name="ItemList">The AutomationId of the list the item belongs to.</param>
internal sealed record ElementScope(string? Item, string? Window, string ItemList = ElementScope.RepoListId)
{
    /// <summary>
    ///     The AutomationId of the main window's repo list, whose items are the repo cards.
    /// </summary>
    internal const string RepoListId = "RepoListBox";

    /// <summary>
    ///     The AutomationId of the text that names a repo card.
    /// </summary>
    internal const string RepoNameId = "RepoNameText";

    /// <summary>
    ///     Gets a scope that does not narrow anything.
    /// </summary>
    internal static ElementScope None { get; } = new(null, null);

    /// <summary>
    ///     Reads the <c>--item</c>, <c>--item-list</c> and <c>--window</c> options of a command.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="PuppetException">Thrown when an option is given without a value.</exception>
    internal static ElementScope FromCommand(ParsedCommand parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return new ElementScope(
            Required(parsed, "item"),
            Required(parsed, "window"),
            Required(parsed, "item-list") ?? RepoListId);
    }

    /// <summary>
    ///     Describes the scope for a message.
    /// </summary>
    /// <returns>The description, or an empty string when the scope does not narrow anything.</returns>
    internal string Describe() =>
        (Item is null ? string.Empty : $" in item \"{Item}\" of {ItemList}") + (Window is null ? string.Empty : $" in window \"{Window}\"");

    /// <summary>
    ///     Reads an option that needs a value.
    /// </summary>
    /// <param name="parsed">The parsed command.</param>
    /// <param name="name">The option name.</param>
    /// <returns>The value, or <see langword="null"/> when the option is absent.</returns>
    private static string? Required(ParsedCommand parsed, string name)
    {
        if (!parsed.HasOption(name))
        {
            return null;
        }

        return parsed.GetOption(name) is { Length: > 0 } value
            ? value
            : throw PuppetException.Usage($"--{name} needs a value.");
    }
}

/// <summary>
///     Finds elements by AutomationId across all the windows of an instance.
/// </summary>
internal static class ElementFinder
{
    /// <summary>
    ///     Finds the first element with an AutomationId, looking in the main window first, then in dialogs and pop-ups.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <param name="automationId">The AutomationId.</param>
    /// <param name="scope">The scope that narrows the search.</param>
    /// <returns>The element, or <see langword="null"/> when there is none.</returns>
    /// <exception cref="PuppetException">Thrown when the scope names a list item that does not exist.</exception>
    internal static AutomationElement? FindById(AttachContext context, string automationId, ElementScope scope)
    {
        foreach (var root in SearchRoots(context, scope))
        {
            var found = UiActions.Safe(() => root.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), null);
            if (found is not null)
            {
                return found;
            }

            if (UiActions.Safe(() => root.AutomationId, string.Empty) == automationId)
            {
                return root;
            }
        }

        return null;
    }

    /// <summary>
    ///     Gets the elements a lookup with a scope searches in: the list item when there is one, else the windows.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <param name="scope">The scope.</param>
    /// <returns>The roots to search, in order.</returns>
    /// <exception cref="PuppetException">Thrown when the scope names a list item that does not exist.</exception>
    internal static IReadOnlyList<AutomationElement> SearchRoots(AttachContext context, ElementScope scope)
    {
        var windows = WindowsOf(context, scope);
        return scope.Item is null ? windows : [FindItem(windows, scope)];
    }

    /// <summary>
    ///     Lists the windows of an instance that a scope allows.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <param name="scope">The scope.</param>
    /// <returns>The windows: all of them, or the ones whose title is the scope's window.</returns>
    internal static IReadOnlyList<AutomationElement> WindowsOf(AttachContext context, ElementScope scope)
    {
        var roots = context.Roots();
        if (scope.Window is null)
        {
            return roots;
        }

        var exact = roots.Where(w => string.Equals(TitleOf(w), scope.Window, StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0
            ? exact
            : [.. roots.Where(w => TitleOf(w).Contains(scope.Window, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    ///     Finds the list item a scope names.
    /// </summary>
    /// <param name="windows">The windows to look for the list in.</param>
    /// <param name="scope">The scope with an item.</param>
    /// <returns>The list item.</returns>
    /// <exception cref="PuppetException">Thrown when the list or the item does not exist.</exception>
    internal static AutomationElement FindItem(IReadOnlyList<AutomationElement> windows, ElementScope scope)
    {
        var item = scope.Item ?? throw new ArgumentException("The scope has no item.", nameof(scope));
        var list = windows
            .Select(w => UiActions.Safe(() => w.FindFirstDescendant(cf => cf.ByAutomationId(scope.ItemList)), null))
            .FirstOrDefault(e => e is not null)
            ?? throw new PuppetException(ExitCodes.NotFound, "NOT_FOUND", $"The list '{scope.ItemList}' (needed for --item) was not found{(scope.Window is null ? string.Empty : $" in window \"{scope.Window}\"")}.");
        var items = UiActions.ListItemsOf(list);
        var texts = items.Select(i => UiActions.ListItemTexts(scope.ItemList, i)).ToList();
        return ItemMatcher.TryResolve(texts, item, out var index, out var error)
            ? items[index]
            : throw new PuppetException(ExitCodes.NotFound, "NOT_FOUND", $"--item \"{item}\" of {scope.ItemList}: {error}");
    }

    /// <summary>
    ///     Builds the "not found" message with the closest AutomationIds as suggestions.
    /// </summary>
    /// <param name="context">The attached instance.</param>
    /// <param name="automationId">The AutomationId that was asked for.</param>
    /// <param name="scope">The scope that was searched.</param>
    /// <returns>The message.</returns>
    internal static string BuildNotFoundMessage(AttachContext context, string automationId, ElementScope scope)
    {
        var ids = SearchRoots(context, scope).SelectMany(root => UiTreeDumper.CollectAutomationIds(root, 12)).Distinct(StringComparer.Ordinal).ToList();
        var suggestions = Suggest(automationId, ids);
        var where = scope.Describe();
        return suggestions.Count == 0
            ? $"'{automationId}' was not found{where}; {ids.Count} automation ids are present."
            : $"'{automationId}' was not found{where}. Did you mean: {string.Join(", ", suggestions)}?";
    }

    /// <summary>
    ///     Picks the identifiers closest to a misspelled one.
    /// </summary>
    /// <param name="wanted">The identifier that was asked for.</param>
    /// <param name="candidates">The identifiers that exist.</param>
    /// <returns>Up to three candidates, closest first.</returns>
    internal static IReadOnlyList<string> Suggest(string wanted, IEnumerable<string> candidates) =>
        [.. candidates
            .Select(id => (Id: id, Score: Distance(wanted, id)))
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(3)
            .Select(x => x.Id)];

    /// <summary>
    ///     Computes the case-insensitive edit distance of two strings.
    /// </summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns>The number of single-character edits between them.</returns>
    internal static int Distance(string left, string right)
    {
        var d = new int[left.Length + 1, right.Length + 1];
        for (var i = 0; i <= left.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= right.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToUpperInvariant(left[i - 1]) == char.ToUpperInvariant(right[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[left.Length, right.Length];
    }

    /// <summary>
    ///     Reads a window's title.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The title, or an empty string.</returns>
    private static string TitleOf(AutomationElement window) => UiActions.Safe(() => window.Name, string.Empty) ?? string.Empty;
}

/// <summary>
///     Prints the UI Automation tree.
/// </summary>
internal static class UiTreeDumper
{
    /// <summary>
    ///     Dumps an element and its descendants, one indented line each.
    /// </summary>
    /// <param name="root">The root element.</param>
    /// <param name="maxDepth">How many levels below the root to show.</param>
    /// <returns>The text.</returns>
    internal static string Dump(AutomationElement root, int maxDepth)
    {
        var builder = new StringBuilder();
        Dump(root, builder, 0, maxDepth);
        return builder.ToString();
    }

    /// <summary>
    ///     Collects the AutomationIds below an element.
    /// </summary>
    /// <param name="root">The root element.</param>
    /// <param name="maxDepth">How many levels below the root to visit.</param>
    /// <returns>The distinct ids, sorted.</returns>
    internal static IReadOnlyList<string> CollectAutomationIds(AutomationElement root, int maxDepth)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Collect(root, ids, 0, maxDepth);
        return [.. ids.OrderBy(id => id, StringComparer.Ordinal)];
    }

    /// <summary>
    ///     Escapes the quotes and line breaks of a value for a one-line description.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    internal static string Escape(string text) =>
        text.Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    ///     Writes one element and recurses into its children.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="builder">The output.</param>
    /// <param name="depth">The current depth.</param>
    /// <param name="maxDepth">The deepest level to show.</param>
    private static void Dump(AutomationElement element, StringBuilder builder, int depth, int maxDepth)
    {
        var id = UiActions.Safe(() => element.AutomationId, string.Empty);
        var name = UiActions.Safe(() => element.Name, string.Empty);
        builder.Append(' ', depth * 2).Append(UiActions.Safe(() => element.ControlType.ToString(), "?"));
        if (!string.IsNullOrWhiteSpace(id))
        {
            builder.Append(" id=").Append(id);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            builder.Append(" name=\"").Append(Escape(name)).Append('"');
        }

        if (!UiActions.Safe(() => element.IsEnabled, true))
        {
            builder.Append(" [disabled]");
        }

        if (UiActions.HelpTextOf(element) is { Length: > 0 } help)
        {
            builder.Append(" help=\"").Append(Escape(help)).Append('"');
        }

        builder.AppendLine();
        if (depth >= maxDepth)
        {
            return;
        }

        foreach (var child in UiActions.Safe(() => element.FindAllChildren(), []))
        {
            Dump(child, builder, depth + 1, maxDepth);
        }
    }

    /// <summary>
    ///     Adds the ids of an element and its descendants.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="ids">The ids found so far.</param>
    /// <param name="depth">The current depth.</param>
    /// <param name="maxDepth">The deepest level to visit.</param>
    private static void Collect(AutomationElement element, HashSet<string> ids, int depth, int maxDepth)
    {
        var id = UiActions.Safe(() => element.AutomationId, string.Empty);
        if (!string.IsNullOrWhiteSpace(id))
        {
            ids.Add(id);
        }

        if (depth >= maxDepth)
        {
            return;
        }

        foreach (var child in UiActions.Safe(() => element.FindAllChildren(), []))
        {
            Collect(child, ids, depth + 1, maxDepth);
        }
    }
}
