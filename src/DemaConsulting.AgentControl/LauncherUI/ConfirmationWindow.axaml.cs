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
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     A minimal modal Yes/No confirmation dialog, mirroring <see cref="MessageBoxWindow"/>'s
///     structure/conventions, used to confirm destructive actions (e.g. "Remove from list")
///     before a view model mutates any state.
/// </summary>
/// <remarks>
///     Deliberately simple (a wrapped text block plus "Yes"/"No" buttons), matching
///     <see cref="MessageBoxWindow"/>'s scope. Shown modally via
///     <see cref="Window.ShowDialog{TResult}(Window)"/> from view code-behind only - never from a
///     view model - so business-logic classes stay Avalonia-free and unit-testable. The caller
///     receives the user's choice as the awaited <see cref="bool"/> result: <see langword="true"/>
///     for "Yes", <see langword="false"/> for "No" or the window being closed/dismissed any other
///     way.
/// </remarks>
internal sealed partial class ConfirmationWindow : Window
{
    /// <summary>
    ///     The <see cref="TextBlock"/> displaying the confirmation message text, resolved from
    ///     the loaded XAML in the constructor.
    /// </summary>
    private readonly TextBlock _messageText;

    /// <summary>
    ///     Initializes a new <see cref="ConfirmationWindow"/> with no message text; used by the
    ///     Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="ConfirmationWindow(string)"/>.
    /// </summary>
    public ConfirmationWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _messageText = this.FindControl<TextBlock>("ConfirmationMessageText")
                        ?? throw new InvalidOperationException(
                            "ConfirmationMessageText control not found in ConfirmationWindow.axaml.");
    }

    /// <summary>
    ///     Initializes a new <see cref="ConfirmationWindow"/> displaying the given confirmation
    ///     message.
    /// </summary>
    /// <param name="message">The confirmation message text to display.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is
    ///     <see langword="null"/>.</exception>
    public ConfirmationWindow(string message) : this()
    {
        ArgumentNullException.ThrowIfNull(message);
        _messageText.Text = message;
    }

    /// <summary>
    ///     Closes the window reporting the user's confirmation.
    /// </summary>
    /// <param name="sender">The clicked "Yes" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void YesButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    /// <summary>
    ///     Closes the window reporting the user's declination.
    /// </summary>
    /// <param name="sender">The clicked "No" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void NoButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
