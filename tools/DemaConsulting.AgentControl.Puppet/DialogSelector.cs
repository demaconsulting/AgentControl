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
///     What the dialog choice needs to know about one root of the instance (a window or an open pop-up).
/// </summary>
/// <param name="Name">The element's name (a dialog's title); empty or <see langword="null"/> when it has none.</param>
/// <param name="IsOffscreen">Whether the element is not visible on the screen.</param>
/// <param name="IsWindow">Whether the element is a real window (supports the UI Automation window pattern), not a pop-up
///     such as a tooltip.</param>
/// <param name="Handle">The native window handle, or zero when the root has none (it keeps the order given).</param>
/// <param name="Owner">The handle of the window that owns it (the window a dialog was opened from), or zero.</param>
/// <param name="NonModal">Whether the window is a non-modal window (its owner, if any, still accepts input, as the
///     main window does while the release-notes window is open), which does not take the keyboard from the window
///     behind it.</param>
internal readonly record struct DialogCandidate(
    string? Name,
    bool IsOffscreen,
    bool IsWindow,
    IntPtr Handle = default,
    IntPtr Owner = default,
    bool NonModal = false);

/// <summary>
///     Chooses which root of an instance is the innermost open dialog (no window is touched).
/// </summary>
internal static class DialogSelector
{
    /// <summary>
    ///     Finds the innermost visible, titled, modal dialog among the roots that follow the main window.
    /// </summary>
    /// <remarks>
    ///     The roots are stacked as <see cref="WindowStacking"/> stacks a shot: by the process's z-order, with an owner
    ///     always below the dialogs it owns, so the innermost dialog is the topmost one that qualifies, however UI
    ///     Automation happens to enumerate the roots. A root without a handle keeps the order given and counts as on
    ///     top; that order lists the main window first, then its dialogs and then any open pop-ups (a tooltip still
    ///     showing is among them). Only real windows qualify: focusing or raising a tooltip would send the keyboard to
    ///     the wrong window while a modal dialog is open. A non-modal window (the release-notes or About window) does not
    ///     qualify either: it leaves the main window usable, so the main window stays the target of keyboard input.
    /// </remarks>
    /// <param name="roots">The roots in the order the instance lists them (the main window first).</param>
    /// <param name="topDownZOrder">The z-order of the process's top-level windows, topmost first; empty when unknown.
    ///     </param>
    /// <returns>The index in <paramref name="roots"/> of the innermost dialog, or -1 when there is none.</returns>
    internal static int InnermostDialog(
        IReadOnlyList<DialogCandidate> roots,
        IReadOnlyList<IntPtr>? topDownZOrder = null)
    {
        var entries = roots.Select(root => new StackEntry(root.Handle, root.Owner)).ToList();
        var bottomUp = WindowStacking.DrawOrder(entries, topDownZOrder ?? []);

        // The top of the stack is the end of the draw order; the main window (index 0) is never the dialog
        for (var position = bottomUp.Count - 1; position >= 0; position--)
        {
            var index = bottomUp[position];
            var root = roots[index];
            if (index >= 1 && root.IsWindow && !root.NonModal && !root.IsOffscreen && !string.IsNullOrWhiteSpace(root.Name))
            {
                return index;
            }
        }

        return -1;
    }
}
