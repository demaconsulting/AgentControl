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

using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests that a dialog really is modal: while About or Settings is open, the main
///     window behind it is disabled and neither a real mouse click nor real key presses can reach it.
/// </summary>
/// <remarks>
///     The tests send real operating-system input (the mouse and the keyboard), because a UI
///     Automation call can act on a window that a real user could not. Input is only sent after the
///     dialog is the foreground window and, for a click, after the target point is shown to belong
///     to the application under test. The proof that nothing reached the main window is what the
///     input would have done there: a click on a main-window button would open another window, and
///     typing into the main window's filter box would change its text.
/// </remarks>
public sealed class ModalDialogTests
{
    /// <summary>
    ///     While About is open the operating system has disabled the main window (and About itself is
    ///     enabled); closing About enables the main window again and hands it the focus back.
    /// </summary>
    [Fact]
    public void AboutWindow_Opened_MainWindowIsDisabledUntilItCloses_AndRegainsFocus()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a running application whose main window accepts input
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        Assert.True(DialogInputProbe.IsEnabled(mainWindow));

        // Act: open About
        Find(mainWindow, "OpenAboutButton").AsButton().Invoke();
        var aboutWindow = context.WaitForDialog("AboutCloseButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(aboutWindow);

        // Assert: the main window behind the dialog is disabled, the dialog itself is not
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: false));
        Assert.True(DialogInputProbe.IsEnabled(aboutWindow));

        // Act: close About
        Find(aboutWindow, "AboutCloseButton").AsButton().Invoke();

        // Assert: the main window accepts input again and has the focus
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));
        Assert.True(DialogInputProbe.WaitForForeground(mainWindow));
    }

    /// <summary>
    ///     While About is open, a real click on a main-window button opens nothing, typed keys do not
    ///     change the main window's filter box, and Tab and Shift+Tab never move the focus out of
    ///     About.
    /// </summary>
    [Fact]
    public void AboutWindow_Opened_ClicksAndKeysOnMainWindowDoNotReachIt()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: About open over the main window
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Find(mainWindow, "OpenAboutButton").AsButton().Invoke();
        var aboutWindow = context.WaitForDialog("AboutCloseButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(aboutWindow);

        // Act / Assert: the Settings button would open Settings if the click reached the main window
        AssertInputCannotReachMainWindow(context, mainWindow, aboutWindow, "OpenSettingsButton", "ThemeComboBox", 12);

        // Act: close About
        Find(aboutWindow, "AboutCloseButton").AsButton().Invoke();

        // Assert: the main window is usable again, and the same click and typing now do reach it
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));
        Assert.True(DialogInputProbe.BringToForeground(mainWindow));
        DialogInputProbe.ClickAt(context, DialogInputProbe.CenterOf(Find(mainWindow, "OpenSettingsButton")));
        var settingsWindow = context.WaitForDialog("ThemeComboBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);
        settingsWindow.Close();
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));
        var filterTextBox = Find(mainWindow, "FilterTextBox").AsTextBox();
        filterTextBox.Focus();
        DialogInputProbe.TypeText("zzz");
        Assert.Equal("zzz", filterTextBox.Text);
    }

    /// <summary>
    ///     While Settings is open, a real click on a main-window button opens nothing, typed keys do
    ///     not change the main window's filter box, and Tab and Shift+Tab never move the focus out of
    ///     Settings; leaving Settings without saving enables the main window again.
    /// </summary>
    [Fact]
    public void SettingsWindow_Opened_ClicksAndKeysOnMainWindowDoNotReachIt()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: Settings open over the main window
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        mainWindow.SetForeground();
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForDialog("ThemeComboBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);

        // Act / Assert: the About button would open About if the click reached the main window
        AssertInputCannotReachMainWindow(
            context, mainWindow, settingsWindow, "OpenAboutButton", "AboutCloseButton", 30);

        // Act: leave Settings without saving
        settingsWindow.Close();

        // Assert: the main window is usable again
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));
    }

    /// <summary>
    ///     Sends a real click and real key presses meant for the main window while a dialog is open
    ///     and asserts that none of them reached the main window.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window behind the dialog.</param>
    /// <param name="dialog">The open dialog.</param>
    /// <param name="clickTargetId">The AutomationId of the main-window button to click; it must not
    ///     be covered by the dialog.</param>
    /// <param name="openedByClickId">The AutomationId of an element in the window the button would
    ///     open, which must therefore not appear.</param>
    /// <param name="tabPresses">How many Tab and Shift+Tab presses to send; more than the number of
    ///     controls in the dialog, so the focus goes all the way round.</param>
    private static void AssertInputCannotReachMainWindow(
        AgentControlTestContext context,
        Window mainWindow,
        Window dialog,
        string clickTargetId,
        string openedByClickId,
        int tabPresses)
    {
        var dialogHandle = dialog.Properties.NativeWindowHandle.Value;
        var filterTextBox = Find(mainWindow, "FilterTextBox").AsTextBox();
        var clickPoint = DialogInputProbe.CenterOf(Find(mainWindow, clickTargetId));
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: false));
        MoveDialogAwayFrom(dialog, clickPoint);

        // Act: click the main window's button with the real mouse
        Assert.True(DialogInputProbe.BringToForeground(dialog));
        DialogInputProbe.ClickAt(context, clickPoint);

        // Assert: the click opened nothing and the main window is still disabled
        Assert.Null(context.WaitForDialog(openedByClickId, TimeSpan.FromSeconds(2)));
        Assert.False(DialogInputProbe.IsEnabled(mainWindow));

        // Act: aim the keyboard at the main window's filter box, then type and tab around
        FocusIfPossible(filterTextBox);
        Assert.True(DialogInputProbe.BringToForeground(dialog));
        DialogInputProbe.TypeText("zzz");

        // Assert: the typed text did not reach the main window, and the focus is still in the dialog
        Assert.Equal(string.Empty, filterTextBox.Text);
        Assert.Equal(dialogHandle, DialogInputProbe.FocusedWindowHandle(context));

        // Act / Assert: Tab and Shift+Tab go all the way round without leaving the dialog
        for (var press = 0; press < tabPresses; press++)
        {
            DialogInputProbe.Press(VirtualKeyShort.TAB);
            Assert.Equal(dialogHandle, DialogInputProbe.FocusedWindowHandle(context));
        }

        for (var press = 0; press < tabPresses; press++)
        {
            DialogInputProbe.PressShiftTab();
            Assert.Equal(dialogHandle, DialogInputProbe.FocusedWindowHandle(context));
        }

        Assert.Equal(string.Empty, filterTextBox.Text);
        Assert.False(DialogInputProbe.IsEnabled(mainWindow));
    }

    /// <summary>
    ///     Moves a dialog sideways when it covers a screen point, so a click at that point reaches the
    ///     window behind the dialog instead of the dialog itself.
    /// </summary>
    /// <param name="dialog">The dialog to move.</param>
    /// <param name="point">The screen point that must not be covered.</param>
    private static void MoveDialogAwayFrom(Window dialog, System.Drawing.Point point)
    {
        if (!dialog.BoundingRectangle.Contains(point))
        {
            return;
        }

        var transform = dialog.Patterns.Transform.PatternOrDefault;
        Assert.NotNull(transform);
        transform.Move(point.X - dialog.BoundingRectangle.Width - 20, dialog.BoundingRectangle.Top);
        Assert.True(Retry.WhileFalse(
            () => !dialog.BoundingRectangle.Contains(point),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Asks UI Automation to focus an element in the (disabled) main window, as far as the
    ///     operating system allows; whether it takes the focus is not what the tests assert, only
    ///     that typing afterwards does not change it.
    /// </summary>
    /// <param name="element">The element to focus.</param>
    private static void FocusIfPossible(AutomationElement element)
    {
        try
        {
            element.Focus();
        }
        catch (COMException)
        {
            // The operating system refuses to focus an element of a disabled window, as expected
        }
    }

    /// <summary>
    ///     Finds an element by AutomationId, waiting briefly for it to appear.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId to find.</param>
    /// <returns>The element.</returns>
    private static AutomationElement Find(AutomationElement parent, string automationId)
    {
        var element = Retry.WhileNull(
            () => parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(element);
        return element;
    }
}
