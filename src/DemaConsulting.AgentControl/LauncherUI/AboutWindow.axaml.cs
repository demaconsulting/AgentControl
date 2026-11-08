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
///     Modal "About AgentControl" window showing the app name, logo, a short mission
///     tagline, a labeled "Version" row (<see cref="Program.Version"/> without its build metadata,
///     which is the row's tooltip), copyright, a short license summary, and a
///     scrollable list of this application's direct third-party runtime dependencies with their
///     SPDX license identifiers.
/// </summary>
/// <remarks>
///     Callers show it with <c>ShowDialog</c> and the main window as owner, like every other
///     dialog, so keyboard focus and Tab stay inside it (see <see cref="DialogBehavior"/>); the Close
///     button has the focus and Escape closes it. (It was once non-modal, but a window that stays
///     open beside the main window let Tab and clicks wander between the two.) This window
///     remains code-behind only (no view
///     model is needed); this code-behind sets the tagline/version/copyright/license text once at
///     construction, and populates the dependency list's <c>ItemsSource</c> from the static
///     <see cref="ThirdPartyDependencies"/> list rather than from a view model or by parsing the
///     project file at runtime. The logo reuses the existing <c>Assets/AppIcon.ico</c> (the same
///     icon already used for the main window's title bar and the Windows taskbar/exe icon)
///     rather than adding a new binary image asset.
/// </remarks>
internal sealed partial class AboutWindow : Window
{
    /// <summary>
    ///     Short mission tagline shown below the app name, adapted (condensed, not verbatim)
    ///     from the lead paragraph of docs/design/introduction.md (and README.md's opening
    ///     sentence, which conveys the same idea).
    /// </summary>
    private const string Tagline =
        "Distribute proprietary AI-agent configuration alongside public repositories, "
        + "without ever committing that proprietary content to source control.";

    /// <summary>
    ///     Initializes a new <see cref="AboutWindow"/>, populating its tagline/version/copyright/
    ///     license text.
    /// </summary>
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var taglineText = this.FindControl<TextBlock>("AboutTaglineText")
                           ?? throw new InvalidOperationException(
                               "AboutTaglineText control not found in AboutWindow.axaml.");
        var versionText = this.FindControl<TextBlock>("AboutVersionText")
                           ?? throw new InvalidOperationException(
                               "AboutVersionText control not found in AboutWindow.axaml.");
        var copyrightText = this.FindControl<TextBlock>("AboutCopyrightText")
                             ?? throw new InvalidOperationException(
                                 "AboutCopyrightText control not found in AboutWindow.axaml.");
        var licenseText = this.FindControl<TextBlock>("AboutLicenseText")
                           ?? throw new InvalidOperationException(
                               "AboutLicenseText control not found in AboutWindow.axaml.");
        var dependenciesList = this.FindControl<ItemsControl>("AboutDependenciesList")
                                ?? throw new InvalidOperationException(
                                    "AboutDependenciesList control not found in AboutWindow.axaml.");

        taglineText.Text = Tagline;
        versionText.Text = DisplayVersion(Program.Version);
        if (!string.Equals(versionText.Text, Program.Version, StringComparison.Ordinal))
        {
            ToolTip.SetTip(versionText, $"Full build version: {Program.Version}");
        }

        copyrightText.Text = "Copyright (c) DEMA Consulting";
        licenseText.Text = "Licensed under the MIT License. See LICENSE for details.";
        dependenciesList.ItemsSource = ThirdPartyDependencies.All;

        // Escape and the Close button both close it; the Close button has the focus
        DialogBehavior.Apply(this, "AboutCloseButton", () => Close());
    }

    /// <summary>
    ///     Shortens a full informational version to the one the About window shows: the part before
    ///     the build metadata (the text after the first <c>+</c>, typically a commit hash), so the
    ///     version reads <c>1.2.3</c> instead of <c>1.2.3+0123456789abcdef</c> and fits on one line.
    ///     The full text stays available as the version's tooltip.
    /// </summary>
    /// <param name="fullVersion">The full informational version, as <see cref="Program.Version"/>
    ///     reports it. Must not be <see langword="null"/>.</param>
    /// <returns>The version without build metadata; <paramref name="fullVersion"/> itself when it has
    ///     none, or when removing the metadata would leave nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fullVersion"/> is
    ///     <see langword="null"/>.</exception>
    internal static string DisplayVersion(string fullVersion)
    {
        ArgumentNullException.ThrowIfNull(fullVersion);
        var plus = fullVersion.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? fullVersion[..plus] : fullVersion;
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
