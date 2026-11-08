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

using DemaConsulting.AgentControl.Tests.AgentPackageManagement;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests that every window of the application is legible in both the Light and the Dark
///     theme: the theme is seeded in the settings file (the way a previous session left it), each
///     window is opened in the running application and the pixels of one of its texts are measured, so
///     the test fails when a window keeps the other theme's surface or text colors.
/// </summary>
/// <remarks>
///     The windows are the main window, Settings, the source editor, About, the group-name prompt, the
///     confirmation, the message box, the package picker, the release notes window and the sign-in
///     prompt. A window is measured on the text it shows (the surface behind the text must have the
///     theme's brightness, and the text must contrast with it by at least the WCAG minimum of 4.5:1),
///     which is what makes a window legible; the contrast of every control state is not measured here.
/// </remarks>
public sealed class WindowAppearanceTests
{
    /// <summary>
    ///     The user name the sign-in prompt test's feed accepts.
    /// </summary>
    private const string FeedUser = "svc-appearance";

    /// <summary>
    ///     The password the sign-in prompt test's feed accepts.
    /// </summary>
    private const string FeedPassword = "ui-test-appearance-pw-61d4";

    /// <summary>
    ///     The package every fixture source offers.
    /// </summary>
    private const string PackageName = "contoso-agents";

    /// <summary>
    ///     In the Light theme the main window, Settings, the source editor and About are legible.
    /// </summary>
    [Fact]
    public void MainSettingsSourceEditorAndAbout_LightTheme_RenderLegibly() =>
        AssertMainSettingsSourceEditorAndAbout("Light");

    /// <summary>
    ///     In the Dark theme the main window, Settings, the source editor and About are legible.
    /// </summary>
    [Fact]
    public void MainSettingsSourceEditorAndAbout_DarkTheme_RenderLegibly() =>
        AssertMainSettingsSourceEditorAndAbout("Dark");

    /// <summary>
    ///     In the Light theme the group-name prompt, the confirmation and the message box are legible.
    /// </summary>
    [Fact]
    public void GroupNameConfirmationAndMessageBox_LightTheme_RenderLegibly() =>
        AssertGroupNameConfirmationAndMessageBox("Light");

    /// <summary>
    ///     In the Dark theme the group-name prompt, the confirmation and the message box are legible.
    /// </summary>
    [Fact]
    public void GroupNameConfirmationAndMessageBox_DarkTheme_RenderLegibly() =>
        AssertGroupNameConfirmationAndMessageBox("Dark");

    /// <summary>
    ///     In the Light theme the package picker and the release notes window are legible.
    /// </summary>
    [Fact]
    public void SelectPackageAndReleaseNotes_LightTheme_RenderLegibly() =>
        AssertSelectPackageAndReleaseNotes("Light");

    /// <summary>
    ///     In the Dark theme the package picker and the release notes window are legible.
    /// </summary>
    [Fact]
    public void SelectPackageAndReleaseNotes_DarkTheme_RenderLegibly() =>
        AssertSelectPackageAndReleaseNotes("Dark");

    /// <summary>
    ///     In the Light theme the sign-in prompt is legible.
    /// </summary>
    [Fact]
    public async Task CredentialPrompt_LightTheme_RendersLegibly() =>
        await AssertCredentialPromptAsync("Light");

    /// <summary>
    ///     In the Dark theme the sign-in prompt is legible.
    /// </summary>
    [Fact]
    public async Task CredentialPrompt_DarkTheme_RendersLegibly() =>
        await AssertCredentialPromptAsync("Dark");

    /// <summary>
    ///     Opens the main window, Settings, the source editor and About in a theme and checks each.
    /// </summary>
    /// <param name="theme">The theme to seed, <c>Light</c> or <c>Dark</c>.</param>
    private static void AssertMainSettingsSourceEditorAndAbout(string theme)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a launch with the theme persisted, as a previous session left it
        using var context = new AgentControlTestContext(customizeSettings: (settings, _) => settings["Theme"] = theme);
        var mainWindow = context.Launch();

        // Assert: the main window shows its repo card legibly
        AssertLegible(mainWindow, () => Find(mainWindow, "RepoNameText"), theme, "The main window");

        // Act / Assert: Settings, and the source editor opened from it
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForDialog("ThemeComboBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);
        AssertLegible(settingsWindow, () => Find(settingsWindow, "AppearanceHeadingText"), theme, "Settings");

        AgentControlTestContext.InvokeAddSourceMenuItem(settingsWindow, "AddZipSourceButton");
        var editor = context.WaitForDialog("SourceAliasTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);
        AssertLegible(
            editor,
            () => editor.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text).And(cf.ByName("Alias"))),
            theme,
            "The source editor");
        Find(editor, "SourceEditorCancelButton").AsButton().Invoke();
        settingsWindow.Close();

        // Act / Assert: About
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));
        Find(mainWindow, "OpenAboutButton").AsButton().Invoke();
        var aboutWindow = context.WaitForDialog("AboutCloseButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(aboutWindow);
        AssertLegible(aboutWindow, () => Find(aboutWindow, "AboutCopyrightText"), theme, "About");
    }

    /// <summary>
    ///     Opens the group-name prompt, the confirmation and the message box in a theme and checks each.
    /// </summary>
    /// <param name="theme">The theme to seed, <c>Light</c> or <c>Dark</c>.</param>
    private static void AssertGroupNameConfirmationAndMessageBox(string theme)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: the theme persisted, and no agent tool command, so Launch ends in a message
        using var context = new AgentControlTestContext(customizeSettings: (settings, _) =>
        {
            settings["Theme"] = theme;
            settings["CustomAgentCommand"] = string.Empty;
        });
        var mainWindow = context.Launch();
        mainWindow.SetForeground();

        // Act / Assert: ... > Move to group > New group... opens the group-name prompt
        OpenCardMenu(mainWindow);
        var moveToGroup = Find(mainWindow, "MoveToGroupMenuItem");
        moveToGroup.Patterns.ExpandCollapse.Pattern.Expand();
        Find(mainWindow, "NewGroupMenuItem").AsMenuItem().Invoke();
        var prompt = context.WaitForDialog("GroupNameTextBox", TimeSpan.FromSeconds(15));
        Assert.NotNull(prompt);
        AssertLegible(prompt, () => Find(prompt, "GroupNamePromptText"), theme, "The group-name prompt");
        Find(prompt, "GroupNameCancelButton").AsButton().Invoke();
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));

        // Act / Assert: ... > Remove from list opens the confirmation
        OpenCardMenu(mainWindow);
        Find(mainWindow, "RemoveMenuItem").AsMenuItem().Invoke();
        var confirmation = context.WaitForDialog("ConfirmationMessageText", TimeSpan.FromSeconds(15));
        Assert.NotNull(confirmation);
        AssertLegible(confirmation, () => Find(confirmation, "ConfirmationMessageText"), theme, "The confirmation");
        Find(confirmation, "ConfirmationNoButton").AsButton().Invoke();
        Assert.True(DialogInputProbe.WaitForEnabled(mainWindow, expected: true));

        // Act / Assert: Launch without a configured tool shows a message box
        Find(mainWindow, "LaunchButton").AsButton().Invoke();
        var messageBox = context.WaitForDialog("MessageText", TimeSpan.FromSeconds(15));
        Assert.NotNull(messageBox);
        AssertLegible(messageBox, () => Find(messageBox, "MessageText"), theme, "The message box");
    }

    /// <summary>
    ///     Opens the package picker and the release notes window in a theme and checks each.
    /// </summary>
    /// <param name="theme">The theme to seed, <c>Light</c> or <c>Dark</c>.</param>
    private static void AssertSelectPackageAndReleaseNotes(string theme)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        var sourceDirectory = Path.Combine(Path.GetTempPath(), "AgentControlUiTests-source-" + Guid.NewGuid());
        try
        {
            // Arrange: a never-pinned repo, a package source and the theme persisted
            FixturePackageBuilder.Create(sourceDirectory, PackageName, "1.0.0", "# v1.0.0\n\nA release note.\n");
            using var context = new AgentControlTestContext(
                packageSourcePath: sourceDirectory,
                customizeSettings: (settings, _) => settings["Theme"] = theme);
            var mainWindow = context.Launch();
            mainWindow.SetForeground();

            // Act / Assert: ... > Select Package opens the picker
            OpenCardMenu(mainWindow);
            Find(mainWindow, "SelectPackageMenuItem").AsMenuItem().Invoke();
            var picker = context.WaitForDialog("SelectPackageNameList", TimeSpan.FromSeconds(15));
            Assert.NotNull(picker);
            AssertLegible(picker, () => Find(picker, "SelectPackageSourceList"), theme, "The package picker");

            // Act: pin the package, which opens the release notes
            var nameItem = Retry.WhileNull(
                () => picker.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageNameList"))
                    ?.FindFirstDescendant(cf => cf.ByName(PackageName))?.AsListBoxItem(),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result;
            Assert.NotNull(nameItem);
            nameItem.Select();
            var confirm = Find(picker, "SelectPackageConfirmButton").AsButton();
            Assert.True(Retry.WhileFalse(
                () => confirm.IsEnabled,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(200)).Result);
            confirm.Invoke();

            // Assert: the release notes window is legible
            var releaseNotes = context.WaitForWindow(
                title => title.StartsWith("Release Notes", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15));
            Assert.NotNull(releaseNotes);
            AssertLegible(
                releaseNotes, () => Find(releaseNotes, "ReleaseNotesHeading"), theme, "The release notes window");
        }
        finally
        {
            try
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only.
            }
        }
    }

    /// <summary>
    ///     Opens the sign-in prompt in a theme (by testing a connection to a server that asks for a
    ///     sign-in) and checks it.
    /// </summary>
    /// <param name="theme">The theme to seed, <c>Light</c> or <c>Dark</c>.</param>
    /// <returns>A task that completes when the check has finished.</returns>
    private static async Task AssertCredentialPromptAsync(string theme)
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        // Arrange: a feed that needs a sign-in, and the theme persisted
        await using var feed = await MockNuGetFeed.StartAsync();
        feed.AddPackage(PackageName, "1.0.0");
        feed.RequiredUser = FeedUser;
        feed.RequiredPassword = FeedPassword;
        using var context = new AgentControlTestContext(customizeSettings: (settings, _) => settings["Theme"] = theme);
        var mainWindow = context.Launch();

        // Act: add the server in Settings and test the connection, which asks for a sign-in
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForDialog("AddSourceButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);
        AgentControlTestContext.InvokeAddSourceMenuItem(settingsWindow, "AddNuGetServerSourceButton");
        var editor = context.WaitForDialog("SourceTestConnectionButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);
        Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Feed";
        Find(editor, "SourceLocationTextBox").AsTextBox().Text = feed.IndexUrl;
        Find(editor, "SourceAllowInsecureHttpCheckBox").AsCheckBox().IsChecked = true;
        Find(editor, "SourcePackageIdTextBox").AsTextBox().Text = PackageName;
        Find(editor, "SourceAddPackageIdButton").AsButton().Invoke();
        var testButton = Find(editor, "SourceTestConnectionButton").AsButton();
        Assert.True(Retry.WhileFalse(
            () => testButton.IsEnabled,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        testButton.Invoke();

        // Assert: the sign-in prompt is legible
        var signIn = context.WaitForDialog("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(30));
        Assert.NotNull(signIn);
        AssertLegible(signIn, () => Find(signIn, "CredentialPromptExplanationText"), theme, "The sign-in prompt");
    }

    /// <summary>
    ///     Brings a window to the front and asserts that a text in it is legible in a theme.
    /// </summary>
    /// <param name="window">The window that shows the text.</param>
    /// <param name="findText">Finds the text element to measure.</param>
    /// <param name="theme">The theme the window should be drawn in, <c>Light</c> or <c>Dark</c>.</param>
    /// <param name="description">What the window is called in a failure message.</param>
    private static void AssertLegible(
        Window window, Func<AutomationElement?> findText, string theme, string description)
    {
        Assert.True(DialogInputProbe.BringToForeground(window), $"{description} could not be brought to the front.");
        var text = Retry.WhileNull(findText, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(text);
        var (measurement, legible) = RenderedTextProbe.WaitForLegible(window, text, theme);
        Assert.True(legible, $"{description} is not legible: " + RenderedTextProbe.Describe(measurement, theme));
    }

    /// <summary>
    ///     Opens the seeded repo card's "..." menu.
    /// </summary>
    /// <param name="mainWindow">The main window.</param>
    private static void OpenCardMenu(Window mainWindow) =>
        Find(mainWindow, "MoreActionsButton").AsButton().Invoke();

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
