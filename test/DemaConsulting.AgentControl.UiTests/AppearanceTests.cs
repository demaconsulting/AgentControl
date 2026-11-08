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

using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end test of the Appearance setting: choosing Light and then Dark in the Settings
///     window and saving changes how the running application looks at once, without a restart.
/// </summary>
/// <remarks>
///     The look is checked from the pixels of the main window (the app bar background is light in the
///     light theme and dark in the dark theme), not from the settings file alone, so the test
///     fails if the choice is saved but never applied. The user's own operating-system theme is
///     never touched: the choices made here are explicit Light and Dark, not the system default.
/// </remarks>
public sealed class AppearanceTests
{
    /// <summary>
    ///     Choosing Light and then Dark in Settings re-themes the main window immediately and
    ///     persists the choice.
    /// </summary>
    [Fact]
    public void SettingsWindow_ChooseLightThenDark_ReThemesMainWindowWithoutRestart()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // Act: choose Light and save
        ChooseTheme(context, mainWindow, "Light");

        // Assert: the page is light, and the choice was persisted by name
        Assert.True(Retry.WhileFalse(
            () => PageBrightness(mainWindow) > 0.7,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(250)).Result);
        Assert.True(Retry.WhileFalse(
            () => ReadSettings(context).Contains("\"Theme\": \"Light\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: choose Dark and save
        ChooseTheme(context, mainWindow, "Dark");

        // Assert: the page is dark, and the choice was persisted
        Assert.True(Retry.WhileFalse(
            () => PageBrightness(mainWindow) < 0.3,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(250)).Result);
        Assert.Contains("\"Theme\": \"Dark\"", ReadSettings(context), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Settings shows Appearance as its own section: a heading, with the Theme picker under it, above
    ///     the Tools heading (and not under Tools with the git and agent tool settings).
    /// </summary>
    [Fact]
    public void SettingsWindow_AppearanceIsItsOwnSectionAboveTools()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // Act
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("ThemeComboBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);

        // Assert: Appearance heading, then the Theme picker, then the Tools heading, then a tool setting
        var appearance = Find(settingsWindow, "AppearanceHeadingText");
        var theme = Find(settingsWindow, "ThemeComboBox");
        var tools = Find(settingsWindow, "ToolsHeadingText");
        var git = Find(settingsWindow, "GitExecutablePathTextBox");
        Assert.Equal("Appearance", appearance.Name);
        Assert.Equal("Tools", tools.Name);
        Assert.Equal("Theme", theme.Name);
        Assert.True(appearance.BoundingRectangle.Top < theme.BoundingRectangle.Top);
        Assert.True(theme.BoundingRectangle.Top < tools.BoundingRectangle.Top);
        Assert.True(tools.BoundingRectangle.Top < git.BoundingRectangle.Top);
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();
    }

    /// <summary>
    ///     Settings is a modal dialog: while it is open the operating system has disabled the main window behind
    ///     it (so neither clicks nor Tab can reach it), and saving enables the main window again. The state of
    ///     the main window's own window handle is read (not the keyboard focus, which depends on which window
    ///     the desktop has active), so the test does not depend on the state of the desktop session.
    /// </summary>
    [Fact]
    public void SettingsWindow_Opened_MainWindowBehindItIsDisabledUntilItCloses()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();
        var mainHandle = mainWindow.Properties.NativeWindowHandle.Value;
        Assert.True(IsWindowEnabled(mainHandle));

        // Act
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("ThemeComboBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);

        // Assert: the main window is disabled while the dialog is open
        Assert.True(Retry.WhileTrue(
            () => IsWindowEnabled(mainHandle),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Success);

        // Act: save (which closes the dialog)
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();

        // Assert: the main window is enabled again
        Assert.True(Retry.WhileFalse(
            () => IsWindowEnabled(mainHandle),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Success);
    }

    /// <summary>
    ///     A Light theme saved by an earlier session is applied when the application starts: the main
    ///     window is light before anything is changed, whatever the operating system's own theme is.
    /// </summary>
    [Fact]
    public void MainWindow_LaunchWithPersistedLightTheme_AppliesLightFromStartup() =>
        AssertPersistedThemeAppliedAtStartup("Light", brightnessMatches: brightness => brightness > 0.7);

    /// <summary>
    ///     A Dark theme saved by an earlier session is applied when the application starts: the main
    ///     window is dark before anything is changed, whatever the operating system's own theme is.
    /// </summary>
    [Fact]
    public void MainWindow_LaunchWithPersistedDarkTheme_AppliesDarkFromStartup() =>
        AssertPersistedThemeAppliedAtStartup("Dark", brightnessMatches: brightness => brightness < 0.3);

    /// <summary>
    ///     Launches the application with a theme already saved in its settings and checks that the first
    ///     window shows it, that Settings offers it as the current choice, and that the start did not
    ///     overwrite the saved choice.
    /// </summary>
    /// <param name="theme">The saved theme's friendly name, <c>Light</c> or <c>Dark</c>.</param>
    /// <param name="brightnessMatches">Tells whether the measured app bar brightness is the theme's.</param>
    private static void AssertPersistedThemeAppliedAtStartup(string theme, Func<double, bool> brightnessMatches)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a settings file that already holds the theme, as an earlier session left it
        using var context = new AgentControlTestContext(customizeSettings: (settings, _) => settings["Theme"] = theme);

        // Act: start the application and look at the first window, without changing anything
        var mainWindow = context.Launch();

        // Assert: the window is drawn in the saved theme
        Assert.True(Retry.WhileFalse(
            () => brightnessMatches(PageBrightness(mainWindow)),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(250)).Result);

        // Act: look at the Appearance setting
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("ThemeComboBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);

        // Assert: Settings shows the saved theme as the current choice, and the file still holds it
        Assert.Equal(theme, Find(settingsWindow, "ThemeComboBox").AsComboBox().SelectedItem?.Text);
        Assert.Contains($"\"Theme\": \"{theme}\"", ReadSettings(context), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Win32 <c>IsWindowEnabled</c>: whether a window accepts input (a window that owns an open modal dialog
    ///     does not).
    /// </summary>
    /// <param name="window">The window handle.</param>
    /// <returns><see langword="true"/> when the window accepts input.</returns>
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
    /// <summary>
    ///     Opens Settings, picks a theme by its friendly name in the Appearance picker and saves.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <param name="themeName">The friendly name of the theme to pick.</param>
    private static void ChooseTheme(AgentControlTestContext context, Window mainWindow, string themeName)
    {
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("ThemeComboBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);

        Find(settingsWindow, "ThemeComboBox").AsComboBox().Select(themeName);
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();

        // Saving closes the Settings window
        Assert.True(Retry.WhileTrue(
            () => context.WaitForWindowWithDescendant("ThemeComboBox", TimeSpan.FromMilliseconds(200)) is not null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
    }

    /// <summary>
    ///     Measures how bright the main window's app bar background is, from the screen pixels of
    ///     the empty margin to the right of the last (About) button.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>A brightness from 0 (black) to 1 (white).</returns>
    private static double PageBrightness(Window mainWindow)
    {
        // The app bar is one row of controls with 12 px of padding to the right of the last button:
        // that margin shows the bare app bar background (white in the light theme, near-black in the
        // dark theme), whatever the window width. The position is derived from the button, not
        // hard-coded, so it does not depend on the window size, the title bar or the layout.
        var about = Find(mainWindow, "OpenAboutButton").BoundingRectangle;
        var window = mainWindow.BoundingRectangle;
        using var image = Capture.Element(mainWindow);
        var bitmap = image.Bitmap;

        var x = Math.Min(about.Right - window.Left + 6, bitmap.Width - 1);
        var y = Math.Min(about.Top - window.Top + (about.Height / 2), bitmap.Height - 1);
        var color = bitmap.GetPixel(x, y);
        return ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
    }

    /// <summary>
    ///     Reads the persisted settings file, or an empty string when it does not exist yet.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <returns>The file's text.</returns>
    private static string ReadSettings(AgentControlTestContext context)
    {
        var path = Path.Combine(context.ConfigDirectory, "settings.json");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
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
