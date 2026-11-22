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

using System.Diagnostics;
using FlaUI.Core.AutomationElements;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     Waits until the main window has read its cards: the application shows the cards at once and
///     reads each repo's state in the background, so a card that is still being read does not show
///     its facts yet. A test that checks that something is absent from a card waits for this first,
///     so the check means what it says instead of passing because the card was not read yet.
/// </summary>
internal static class CardRefreshWait
{
    /// <summary>
    ///     How long the window must look settled on two looks in a row: the progress strip appears a
    ///     moment after the reads are queued, so one look straight after start-up is not enough.
    /// </summary>
    private static readonly TimeSpan ConfirmDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    ///     How often the window is looked at.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Waits until neither the progress strip nor a card's "refreshing..." indicator is shown, on
    ///     two looks <see cref="ConfirmDelay"/> apart.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="timeout">How long to wait at most.</param>
    /// <returns><see langword="true"/> when the cards were read in time.</returns>
    public static bool WaitUntilCardsAreRead(AutomationElement mainWindow, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (IsSettled(mainWindow))
            {
                Thread.Sleep(ConfirmDelay);
                if (IsSettled(mainWindow))
                {
                    return true;
                }
            }

            Thread.Sleep(PollInterval);
        }

        return false;
    }

    /// <summary>
    ///     Finds the first element with an AutomationId that is shown on the screen.
    /// </summary>
    /// <param name="root">Where to look.</param>
    /// <param name="automationId">The AutomationId.</param>
    /// <returns>The element, or <see langword="null"/> when none is shown.</returns>
    public static AutomationElement? FindShown(AutomationElement root, string automationId) =>
        root.FindAllDescendants(cf => cf.ByAutomationId(automationId)).FirstOrDefault(element => !element.IsOffscreen);

    /// <summary>
    ///     Finds every element with an AutomationId that is shown on the screen.
    /// </summary>
    /// <param name="root">Where to look.</param>
    /// <param name="automationId">The AutomationId.</param>
    /// <returns>The elements, top to bottom.</returns>
    public static List<AutomationElement> FindAllShown(AutomationElement root, string automationId) =>
        [.. root.FindAllDescendants(cf => cf.ByAutomationId(automationId))
            .Where(element => !element.IsOffscreen)
            .OrderBy(element => element.BoundingRectangle.Top)];

    /// <summary>
    ///     Tells whether the window shows neither the progress strip nor a "refreshing..." indicator.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns><see langword="true"/> when nothing is being read.</returns>
    private static bool IsSettled(AutomationElement mainWindow)
    {
        try
        {
            return FindShown(mainWindow, "GlobalProgressStrip") is null &&
                   FindShown(mainWindow, "RepoRefreshingText") is null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TimeoutException)
        {
            // An element that went away while it was read: look again
            return false;
        }
    }
}
