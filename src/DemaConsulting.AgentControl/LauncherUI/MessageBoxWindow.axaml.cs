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
///     A minimal modal message-box window used to surface Launch/Pull/Upgrade/Settings errors to
///     the user, per architecture.md's "an error message box is considered sufficient" decision.
/// </summary>
/// <remarks>
///     Deliberately simple (a wrapped text block plus a single "OK" button) since
///     architecture.md explicitly scopes error reporting down to this level rather than a log
///     file or a richer error-details dialog. Shown modally (<see cref="Window.ShowDialog(Window)"/>)
///     from view code-behind only - never from a view model - so business-logic classes stay
///     Avalonia-free and unit-testable.
/// </remarks>
internal sealed partial class MessageBoxWindow : Window
{
    /// <summary>
    ///     The <see cref="TextBlock"/> displaying the message text, resolved from the loaded
    ///     XAML in the constructor.
    /// </summary>
    private readonly TextBlock _messageText;

    /// <summary>
    ///     Initializes a new <see cref="MessageBoxWindow"/> with no message text; used by the
    ///     Avalonia XAML previewer/loader only. Production code should use
    ///     <see cref="MessageBoxWindow(string)"/>.
    /// </summary>
    public MessageBoxWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _messageText = this.FindControl<TextBlock>("MessageText")
                       ?? throw new InvalidOperationException("MessageText control not found in MessageBoxWindow.axaml.");
    }

    /// <summary>
    ///     Initializes a new <see cref="MessageBoxWindow"/> displaying the given message.
    /// </summary>
    /// <param name="message">The message text to display.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is
    ///     <see langword="null"/>.</exception>
    public MessageBoxWindow(string message) : this()
    {
        ArgumentNullException.ThrowIfNull(message);
        _messageText.Text = message;
    }

    /// <summary>
    ///     Closes the window when the user acknowledges the message.
    /// </summary>
    /// <param name="sender">The clicked "OK" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
