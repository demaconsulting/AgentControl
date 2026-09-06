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
///     Non-modal "About AgentControl" window showing the app name, <see cref="Program.Version"/>,
///     copyright, and a short license summary.
/// </summary>
/// <remarks>
///     Callers MUST use <see cref="Window.Show()"/>, never <c>ShowDialog</c>, mirroring
///     <c>ReleaseNotesViewer</c>'s non-modal convention - the main window remains fully usable
///     while this informational window is open. Content is entirely static (no view model is
///     needed); this code-behind only sets the version/copyright/license text once at
///     construction.
/// </remarks>
internal sealed partial class AboutWindow : Window
{
    /// <summary>
    ///     Initializes a new <see cref="AboutWindow"/>, populating its version/copyright/license
    ///     text.
    /// </summary>
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var versionText = this.FindControl<TextBlock>("AboutVersionText")
                           ?? throw new InvalidOperationException(
                               "AboutVersionText control not found in AboutWindow.axaml.");
        var copyrightText = this.FindControl<TextBlock>("AboutCopyrightText")
                             ?? throw new InvalidOperationException(
                                 "AboutCopyrightText control not found in AboutWindow.axaml.");
        var licenseText = this.FindControl<TextBlock>("AboutLicenseText")
                           ?? throw new InvalidOperationException(
                               "AboutLicenseText control not found in AboutWindow.axaml.");

        versionText.Text = $"AgentControl {Program.Version}";
        copyrightText.Text = "Copyright (c) DEMA Consulting";
        licenseText.Text = "Licensed under the MIT License. See LICENSE for details.";
    }

    /// <summary>
    ///     Closes this window.
    /// </summary>
    /// <param name="sender">The clicked "Close" button.</param>
    /// <param name="e">Routed event arguments (unused).</param>
    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
