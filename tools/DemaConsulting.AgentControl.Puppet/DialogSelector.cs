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
internal readonly record struct DialogCandidate(string? Name, bool IsOffscreen, bool IsWindow);

/// <summary>
///     Chooses which root of an instance is the innermost open dialog (no window is touched).
/// </summary>
internal static class DialogSelector
{
    /// <summary>
    ///     Finds the innermost visible, titled dialog among the roots that follow the main window.
    /// </summary>
    /// <remarks>
    ///     The roots of an instance list the main window first, then its dialogs and then any open pop-ups, so a tooltip that
    ///     is still showing is among them. Only real windows qualify: focusing or raising a tooltip would send the keyboard
    ///     to the wrong window while a modal dialog is open.
    /// </remarks>
    /// <param name="roots">The roots in the order the instance lists them (the main window first, the innermost last).</param>
    /// <returns>The index in <paramref name="roots"/> of the innermost dialog, or -1 when there is none.</returns>
    internal static int InnermostDialog(IReadOnlyList<DialogCandidate> roots)
    {
        for (var index = roots.Count - 1; index >= 1; index--)
        {
            var root = roots[index];
            if (root.IsWindow && !root.IsOffscreen && !string.IsNullOrWhiteSpace(root.Name))
            {
                return index;
            }
        }

        return -1;
    }
}
