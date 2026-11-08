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

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace DemaConsulting.AgentControl.Puppet.Automation;

/// <summary>
///     Safe, pattern-aware actions on UI Automation elements.
/// </summary>
internal static class UiActions
{
    /// <summary>
    ///     Gets the items of a list: its list-item children, or its list-item descendants when the items sit deeper.
    /// </summary>
    /// <param name="list">The list element.</param>
    /// <returns>The items, in order.</returns>
    internal static IReadOnlyList<AutomationElement> ListItemsOf(AutomationElement list)
    {
        var children = Safe(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)), []);
        return children.Length > 0
            ? children
            : Safe(() => list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)), []);
    }

    /// <summary>
    ///     Lists the texts a list item shows: its own name and the text of its descendants.
    /// </summary>
    /// <param name="item">The list item.</param>
    /// <returns>The distinct, non-empty texts; the first is the one used to list the item in messages.</returns>
    internal static IReadOnlyList<string> ItemTexts(AutomationElement item)
    {
        var texts = new List<string>();
        var own = Safe(() => item.Name, string.Empty);
        if (!string.IsNullOrWhiteSpace(own))
        {
            texts.Add(own);
        }

        texts.AddRange(Safe(
            () => item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .Select(e => Safe(() => e.Name, string.Empty))
                .Where(text => !string.IsNullOrWhiteSpace(text)),
            []));
        return [.. texts.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    ///     Lists the texts a list item shows, as matching an item by text needs them: for the repo list only the repo's name
    ///     (a card also shows its path, branch and version), for any other list the item's own name and descendant texts.
    /// </summary>
    /// <param name="listId">The AutomationId of the list.</param>
    /// <param name="item">The list item.</param>
    /// <returns>The texts; the first is the one used to list the item in messages.</returns>
    internal static IReadOnlyList<string> ListItemTexts(string listId, AutomationElement item) =>
        listId == ElementScope.RepoListId ? RepoCardTexts(item) : ItemTexts(item);

    /// <summary>
    ///     Reads the name a repo card shows (its <c>RepoNameText</c>).
    /// </summary>
    /// <param name="card">The list item of the repo list.</param>
    /// <returns>The name (one entry), or an empty list when the card shows none.</returns>
    internal static IReadOnlyList<string> RepoCardTexts(AutomationElement card)
    {
        var name = Safe(() => card.FindFirstDescendant(cf => cf.ByAutomationId(ElementScope.RepoNameId)), null);
        var text = name is null ? string.Empty : ReadText(name);
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }

    /// <summary>
    ///     Reads the help text (the tooltip, when UI Automation exposes it) of an element.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The help text, or an empty string.</returns>
    internal static string HelpTextOf(AutomationElement element)
    {
        var help = Safe(() => element.Properties.HelpText.ValueOrDefault, null);
        if (string.IsNullOrWhiteSpace(help))
        {
            help = Safe(() => element.Properties.FullDescription.ValueOrDefault, null);
        }

        return help ?? string.Empty;
    }

    /// <summary>
    ///     Activates an element the way a user would: invoke, select, toggle or, failing those, click.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <exception cref="InvalidOperationException">Thrown when the element is disabled.</exception>
    internal static void Activate(AutomationElement element)
    {
        ScrollIntoView(element);
        if (!Safe(() => element.IsEnabled, true))
        {
            throw new InvalidOperationException($"Cannot activate '{Describe(element)}' because it is disabled.");
        }

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
        }
        else if (element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
        }
        else if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
        }
        else
        {
            element.Click();
        }
    }

    /// <summary>
    ///     Gives an element keyboard focus and waits until it has it.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="timeout">How long to wait; two seconds by default.</param>
    /// <returns><see langword="true"/> when the element holds focus.</returns>
    internal static bool TryFocus(AutomationElement element, TimeSpan? timeout = null)
    {
        ScrollIntoView(element);
        _ = Safe(() => { element.FocusNative(); return true; }, false);
        _ = Safe(() => { element.Focus(); return true; }, false);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (DateTime.UtcNow <= deadline)
        {
            if (Safe(() => element.Properties.HasKeyboardFocus.ValueOrDefault, false))
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    /// <summary>
    ///     Scrolls an element into view when it supports it.
    /// </summary>
    /// <param name="element">The element.</param>
    internal static void ScrollIntoView(AutomationElement element)
    {
        _ = Safe(
            () =>
            {
                if (element.Patterns.ScrollItem.IsSupported)
                {
                    element.Patterns.ScrollItem.Pattern.ScrollIntoView();
                }

                return true;
            },
            false);
    }

    /// <summary>
    ///     Names an element for a message: its AutomationId, else its name.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The description.</returns>
    internal static string Describe(AutomationElement element)
    {
        var id = Safe(() => element.AutomationId, string.Empty);
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        var name = Safe(() => element.Name, string.Empty);
        return string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name;
    }

    /// <summary>
    ///     Reads the text an element shows: its value, else its name, else the text of its children.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The text.</returns>
    internal static string ReadText(AutomationElement element)
    {
        if (element.Patterns.Value.IsSupported)
        {
            var value = Safe(() => element.Patterns.Value.Pattern.Value.ValueOrDefault, null);
            if (value is not null)
            {
                return value;
            }
        }

        var name = Safe(() => element.Name, string.Empty);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var parts = Safe(
            () => element.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .Select(e => Safe(() => e.Name, string.Empty))
                .Where(text => !string.IsNullOrWhiteSpace(text)),
            []);
        return string.Join(" ", parts);
    }

    /// <summary>
    ///     Runs a UI Automation read that may fail because the element went away.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="read">The read.</param>
    /// <param name="fallback">The value to use when the read throws.</param>
    /// <returns>The value read, or the fallback.</returns>
    internal static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return fallback;
        }
    }
}
