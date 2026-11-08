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

namespace DemaConsulting.AgentControl.Puppet;

/// <summary>
///     One window to place in a shot's stack.
/// </summary>
/// <param name="Handle">The window handle, or zero for a window that has none (a tooltip copied from the screen).</param>
/// <param name="Owner">The handle of the window that owns it (zero when it has no owner).</param>
internal readonly record struct StackEntry(IntPtr Handle, IntPtr Owner);

/// <summary>
///     Plans the order in which the windows of a shot are drawn, so the picture stacks them as the screen does.
/// </summary>
/// <remarks>
///     A dialog is owned by the window it was opened from, and that can be another dialog (Settings, then a source editor,
///     then a credential prompt), so owner chains can be any depth. An owned window is always above its owner.
/// </remarks>
internal static class WindowStacking
{
    /// <summary>
    ///     Plans the draw order: from the bottom of the stack to the top.
    /// </summary>
    /// <param name="entries">The windows to draw.</param>
    /// <param name="topDownZOrder">The z-order of the process's top-level windows, topmost first.</param>
    /// <returns>
    ///     The indices of <paramref name="entries"/> in draw order. Windows follow the z-order (a window the z-order does not
    ///     list goes to the bottom); an owner is always drawn before every window it owns; windows without a handle are drawn
    ///     last (on top) in their given order.
    /// </returns>
    internal static IReadOnlyList<int> DrawOrder(IReadOnlyList<StackEntry> entries, IReadOnlyList<IntPtr> topDownZOrder)
    {
        var rank = new Dictionary<IntPtr, int>();
        for (var i = 0; i < topDownZOrder.Count; i++)
        {
            _ = rank.TryAdd(topDownZOrder[i], i);
        }

        // Bottom of the stack first: the highest top-down rank is the lowest window
        var byZOrder = Enumerable.Range(0, entries.Count)
            .Where(i => entries[i].Handle != IntPtr.Zero)
            .OrderByDescending(i => rank.GetValueOrDefault(entries[i].Handle, int.MaxValue))
            .ToList();

        var byHandle = new Dictionary<IntPtr, int>();
        foreach (var i in byZOrder)
        {
            _ = byHandle.TryAdd(entries[i].Handle, i);
        }

        var order = new List<int>(entries.Count);
        var placed = new HashSet<int>();
        foreach (var i in byZOrder)
        {
            Place(i, entries, byHandle, placed, order);
        }

        order.AddRange(Enumerable.Range(0, entries.Count).Where(i => entries[i].Handle == IntPtr.Zero));
        return order;
    }

    /// <summary>
    ///     Adds a window to the draw order after its owner chain.
    /// </summary>
    /// <param name="index">The window's index.</param>
    /// <param name="entries">The windows.</param>
    /// <param name="byHandle">The index of each handle.</param>
    /// <param name="placed">The windows already in the order (also guards against an owner cycle).</param>
    /// <param name="order">The order being built.</param>
    private static void Place(int index, IReadOnlyList<StackEntry> entries, Dictionary<IntPtr, int> byHandle, HashSet<int> placed, List<int> order)
    {
        if (!placed.Add(index))
        {
            return;
        }

        if (entries[index].Owner != IntPtr.Zero && byHandle.TryGetValue(entries[index].Owner, out var owner))
        {
            Place(owner, entries, byHandle, placed, order);
        }

        order.Add(index);
    }
}
