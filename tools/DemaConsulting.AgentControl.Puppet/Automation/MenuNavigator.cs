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
///     Walks a menu path such as <c>File/Open</c> or <c>RefreshMenuItem</c> through the menus that are open or can be opened.
/// </summary>
/// <remarks>
///     AgentControl has no menu bar; its menus are the pop-up of a repo card's "more actions" button (open it first with
///     <c>click MoreActionsButton --item &lt;repo&gt;</c>) and any menu a later version adds. A segment matches a menu item by
///     its AutomationId or by its text.
/// </remarks>
internal static class MenuNavigator
{
    /// <summary>
    ///     How long a menu gets to open its pop-up before the next item is searched for.
    /// </summary>
    private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     Splits a menu path into its segments.
    /// </summary>
    /// <param name="path">The path, segments joined with <c>/</c>.</param>
    /// <returns>The non-empty segments.</returns>
    internal static IReadOnlyList<string> Split(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    ///     Tests whether a menu item's name is the one a path segment asks for.
    /// </summary>
    /// <remarks>
    ///     Access-key markers (<c>_</c>, <c>&amp;</c>), white space and case are ignored, and a trailing <c>...</c> may be omitted.
    /// </remarks>
    /// <param name="name">The item's name.</param>
    /// <param name="segment">The segment.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    internal static bool NameMatches(string name, string segment)
    {
        var left = Compact(name).TrimEnd('.');
        var right = Compact(segment).TrimEnd('.');
        return left.Length > 0 && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Opens every menu on the path and invokes the last item.
    /// </summary>
    /// <param name="roots">The windows to look for menu items in.</param>
    /// <param name="segments">The path segments.</param>
    /// <returns>The outcome: the AutomationId (or text) invoked, or the path segment that was missing or disabled.</returns>
    internal static MenuResult Invoke(IReadOnlyList<AutomationElement> roots, IReadOnlyList<string> segments)
    {
        var invoked = string.Empty;
        for (var i = 0; i < segments.Count; i++)
        {
            var item = Find(roots, segments[i]);
            if (item is null)
            {
                return new MenuResult(null, segments[i], false);
            }

            invoked = UiActions.Describe(item);
            if (!UiActions.Safe(() => item.IsEnabled, true))
            {
                return new MenuResult(invoked, segments[i], true);
            }

            var last = i == segments.Count - 1;
            Open(item, last);
            if (!last)
            {
                Thread.Sleep(OpenDelay);
            }
        }

        return new MenuResult(invoked, null, false);
    }

    /// <summary>
    ///     Finds the menu item a path segment names.
    /// </summary>
    /// <param name="roots">The windows to look in.</param>
    /// <param name="segment">The segment: an AutomationId or the item text.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    private static AutomationElement? Find(IReadOnlyList<AutomationElement> roots, string segment)
    {
        foreach (var root in roots)
        {
            var items = UiActions.Safe(() => root.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)), []);
            var match = items.FirstOrDefault(e => UiActions.Safe(() => e.AutomationId, string.Empty) == segment)
                ?? items.FirstOrDefault(e => NameMatches(UiActions.Safe(() => e.Name, string.Empty), segment));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    ///     Opens a submenu or invokes a leaf item.
    /// </summary>
    /// <param name="item">The menu item.</param>
    /// <param name="invoke">Whether this is the last segment, so the item is invoked rather than expanded.</param>
    private static void Open(AutomationElement item, bool invoke)
    {
        if (!invoke && item.Patterns.ExpandCollapse.IsSupported)
        {
            item.Patterns.ExpandCollapse.Pattern.Expand();
        }
        else if (item.Patterns.Invoke.IsSupported)
        {
            item.Patterns.Invoke.Pattern.Invoke();
        }
        else if (item.Patterns.ExpandCollapse.IsSupported)
        {
            item.Patterns.ExpandCollapse.Pattern.Expand();
        }
        else
        {
            item.Click();
        }
    }

    /// <summary>
    ///     Removes access-key markers and white space.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The compact text.</returns>
    private static string Compact(string text) =>
        new([.. text.Where(c => !char.IsWhiteSpace(c) && c is not '_' and not '&')]);
}

/// <summary>
///     The outcome of <see cref="MenuNavigator.Invoke"/>.
/// </summary>
/// <param name="InvokedId">The AutomationId (or text) of the item invoked (or found disabled), or <see langword="null"/> when an item was missing.</param>
/// <param name="FailedSegment">The path segment that could not be used, or <see langword="null"/> on success.</param>
/// <param name="Disabled">Whether <paramref name="FailedSegment"/> was found but is disabled.</param>
internal sealed record MenuResult(string? InvokedId, string? FailedSegment, bool Disabled)
{
    /// <summary>
    ///     Gets a value indicating whether the whole path was walked.
    /// </summary>
    internal bool Succeeded => FailedSegment is null;
}
