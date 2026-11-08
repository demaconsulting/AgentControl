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

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using DemaConsulting.AgentControl.LauncherUI;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Tests of <see cref="DialogBehavior"/> on a plain headless window, so each part of the keyboard
///     behavior every AgentControl dialog shares - Tab staying inside, the first sensible control taking
///     the (visible) focus, Escape canceling - is proven without the dialog it is applied to. The real
///     dialogs are checked with it applied in <c>DialogWindowTests</c> and <c>UsabilityWindowTests</c>.
/// </summary>
public sealed class DialogBehaviorTests
{
    /// <summary>
    ///     Test that a missing dialog is refused.
    /// </summary>
    [Fact]
    public void DialogBehavior_Apply_NullDialog_Throws() =>
        Assert.Throws<ArgumentNullException>(() => DialogBehavior.Apply(null!));

    /// <summary>
    ///     Test that Tab navigation cycles inside the dialog (from the last control to the first).
    /// </summary>
    [Fact]
    public void DialogBehavior_Apply_TabNavigation_CyclesInsideTheDialog() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, _, _) = CreateDialog();

        // Act
        DialogBehavior.Apply(window);

        // Assert
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(window));
    });

    /// <summary>
    ///     Test that opening the dialog puts the keyboard focus on the named control, as a keyboard
    ///     navigation (which is what makes the focus ring visible).
    /// </summary>
    [Fact]
    public void DialogBehavior_Apply_Opened_FocusesNamedControlWithVisibleKeyboardFocus() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, first, second) = CreateDialog();
        NavigationMethod? how = null;
        second.GotFocus += (_, e) => how = e.NavigationMethod;
        DialogBehavior.Apply(window, "SecondBox");

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Assert
        try
        {
            Assert.Same(second, window.FocusManager.GetFocusedElement());
            Assert.NotSame(first, window.FocusManager.GetFocusedElement());
            Assert.Equal(NavigationMethod.Tab, how);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the first control Tab reaches takes the focus when no name is given, when the named
    ///     control does not exist and when it is disabled.
    /// </summary>
    /// <param name="initialFocusName">The name given to <see cref="DialogBehavior.Apply"/>.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("NoSuchControl")]
    [InlineData("DisabledBox")]
    public void DialogBehavior_Apply_NoUsableNamedControl_FocusesFirstTabStop(string? initialFocusName) => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, first, _) = CreateDialog();
        DialogBehavior.Apply(window, initialFocusName);

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Assert
        try
        {
            Assert.Same(first, window.FocusManager.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Escape runs the cancel action once, and that Escape handled by the focused control
    ///     itself (an open drop-down, say) does not cancel the dialog.
    /// </summary>
    [Fact]
    public void DialogBehavior_Apply_Escape_RunsCancelOnceUnlessTheFocusedControlUsedIt() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var canceled = 0;
        var (window, first, second) = CreateDialog();
        DialogBehavior.Apply(window, "FirstButton", () => canceled++);
        second.AddHandler(InputElement.KeyDownEvent, (_, e) => e.Handled = e.Key == Key.Escape);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            // Act: Escape on the first control
            first.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            // Assert
            Assert.Equal(1, canceled);

            // Act: Escape on a control that uses it itself
            second.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            // Assert: not canceled again
            Assert.Equal(1, canceled);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that Escape does nothing in a dialog that has no way to cancel.
    /// </summary>
    [Fact]
    public void DialogBehavior_Apply_EscapeWithoutCancelAction_DoesNothing() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var (window, first, _) = CreateDialog();
        DialogBehavior.Apply(window, "FirstButton");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            // Act
            first.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            // Assert: still open
            Assert.True(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Creates a small dialog window: a button, a text box, and a disabled text box.
    /// </summary>
    /// <returns>The window, its first control and its second control.</returns>
    private static (Window Window, Button First, TextBox Second) CreateDialog()
    {
        var first = new Button { Name = "FirstButton", Content = "First" };
        var second = new TextBox { Name = "SecondBox" };
        var disabled = new TextBox { Name = "DisabledBox", IsEnabled = false };
        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = new StackPanel { Children = { first, second, disabled } }
        };

        // Controls created in code are found by name through the window's name scope
        var scope = new NameScope();
        NameScope.SetNameScope(window, scope);
        scope.Register("FirstButton", first);
        scope.Register("SecondBox", second);
        scope.Register("DisabledBox", disabled);

        return (window, first, second);
    }
}
