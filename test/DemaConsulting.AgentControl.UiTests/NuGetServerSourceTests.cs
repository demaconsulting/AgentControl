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
///     End-to-end test of the NuGet-package-server source: adding it through the Settings window,
///     testing the connection with a credential typed into the sign-in dialog, selecting a package
///     from it and seeing the repo card name the source - against an in-process mock feed served
///     over plain HTTP (the source's "Allow insecure HTTP" option is ticked for that reason).
/// </summary>
/// <remarks>
///     The mock feed runs inside this test process while the application under test runs as its
///     own process, so the application's real NuGet client talks to it over the network stack. The
///     test also checks the credential rule end to end: the password the test typed is never found
///     in the settings file, the repo pin or the application's log.
/// </remarks>
public sealed class NuGetServerSourceTests
{
    /// <summary>
    ///     The feed account's user name.
    /// </summary>
    private const string FeedUser = "svc-agents";

    /// <summary>
    ///     The feed account's password; must never be written to any file by the application.
    /// </summary>
    private const string FeedPassword = "ui-test-sentinel-pw-8e41";

    /// <summary>
    ///     Adding a NuGet server through the Settings window, testing it with a typed credential,
    ///     saving it, selecting a package from it and reading the card chip works end to end, and
    ///     the password is stored nowhere.
    /// </summary>
    [Fact]
    public async Task SettingsWindow_AddNuGetServerWithSignIn_SelectPackagePinsServerSourceAndStoresNoPassword()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        await using var feed = await MockNuGetFeed.StartAsync();
        feed.AddPackage(packageName, "1.0.0");
        feed.RequiredUser = FeedUser;
        feed.RequiredPassword = FeedPassword;

        // A never-pinned repo and no package sources yet
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // Act: open Settings and the Add NuGet-Package Server dialog
        var settingsWindow = OpenSettings(context, mainWindow);
        AgentControlTestContext.InvokeAddSourceMenuItem(settingsWindow, "AddNuGetServerSourceButton");
        var editor = context.WaitForWindowWithDescendant("SourceTestConnectionButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);

        // Assert: the dialog starts invalid, with the test button disabled
        Assert.False(Find(editor, "SourceEditorSaveButton").AsButton().IsEnabled);
        Assert.False(Find(editor, "SourceTestConnectionButton").AsButton().IsEnabled);

        // Act: plain http is refused until the user opts in, then fill in the source
        Find(editor, "SourceAliasTextBox").AsTextBox().Text = "Feed";
        Find(editor, "SourceLocationTextBox").AsTextBox().Text = feed.IndexUrl;
        Assert.Contains("Plain http", Find(editor, "SourceLocationErrorText").Name, StringComparison.Ordinal);
        Find(editor, "SourceAllowInsecureHttpCheckBox").AsCheckBox().IsChecked = true;
        Find(editor, "SourcePackageIdTextBox").AsTextBox().Text = packageName;
        Find(editor, "SourceAddPackageIdButton").AsButton().Invoke();

        // Act: test the connection; the server needs a sign-in, so the dialog appears
        var testButton = Find(editor, "SourceTestConnectionButton").AsButton();
        Assert.True(Retry.WhileFalse(() => testButton.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        testButton.Invoke();
        var signIn = context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(30));
        Assert.NotNull(signIn);

        // Assert: the dialog says which server receives the credential and that it is not saved
        Assert.Equal(feed.BaseUrl, Find(signIn, "CredentialPromptOriginText").Name);
        Assert.Contains("session only", Find(signIn, "CredentialPromptSessionNoteText").Name, StringComparison.Ordinal);

        // Act: sign in
        Find(signIn, "CredentialPromptUserNameTextBox").AsTextBox().Text = FeedUser;
        Find(signIn, "CredentialPromptPasswordTextBox").AsTextBox().Text = FeedPassword;
        var signInButton = Find(signIn, "CredentialPromptSignInButton").AsButton();
        Assert.True(Retry.WhileFalse(() => signInButton.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        signInButton.Invoke();

        // Assert: the connection test reports success
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "Connection OK"));

        // Act: save the source and the settings
        var save = Find(editor, "SourceEditorSaveButton").AsButton();
        Assert.True(Retry.WhileFalse(() => save.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        save.Invoke();
        Assert.True(WaitForText(settingsWindow, "SourceAliasText", "Feed"));
        Assert.True(WaitForText(settingsWindow, "SourceStatusText", "Not checked"));
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();
        var settingsPath = Path.Combine(context.ConfigDirectory, "settings.json");
        Assert.True(Retry.WhileFalse(
            () => File.Exists(settingsPath) && ReadSharedText(settingsPath).Contains("\"Feed\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        var settingsJson = ReadSharedText(settingsPath);
        Assert.Contains("\"Type\": \"NuGet\"", settingsJson, StringComparison.Ordinal);
        Assert.Contains("\"AllowInsecureHttp\": true", settingsJson, StringComparison.Ordinal);
        Assert.Contains(feed.IndexUrl, settingsJson, StringComparison.Ordinal);

        // Act: select the package from the server through the card's menu
        Find(mainWindow, "MoreActionsButton").AsButton().Invoke();
        var selectPackageMenuItem = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageMenuItem")),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result?.AsMenuItem();
        Assert.NotNull(selectPackageMenuItem);
        selectPackageMenuItem.Click();
        var dialog = context.WaitForWindowWithDescendant("SelectPackageSourceList", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);
        SelectListItemByText(dialog, "SelectPackageSourceAliasText", "Feed");
        var nameItem = Retry.WhileNull(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("SelectPackageNameList"))
                ?.FindFirstDescendant(cf => cf.ByName(packageName)),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200)).Result?.AsListBoxItem();
        Assert.NotNull(nameItem);
        nameItem.Select();

        // Assert: the credential typed earlier is remembered for the session, so no second
        // sign-in dialog appears and the version loads
        var confirm = Find(dialog, "SelectPackageConfirmButton").AsButton();
        Assert.True(Retry.WhileFalse(() => confirm.IsEnabled, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200)).Result);
        Assert.Null(context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(1)));
        confirm.Invoke();

        // Assert: the package was applied from the server and the pin names the source
        Assert.NotNull(context.WaitForWindow(
            title => title.StartsWith("Release Notes", StringComparison.Ordinal), TimeSpan.FromSeconds(30)));
        Assert.Equal(
            "agent content from the feed 1.0.0",
            ReadSharedText(Path.Combine(context.RepoPath, ".github", "agents", "copilot.md")));
        var pinPath = Path.Combine(context.RepoPath, ".agentcontrol.json");
        var pinJson = ReadSharedText(pinPath);
        Assert.Contains("\"Source\": \"Feed\"", pinJson, StringComparison.Ordinal);

        // Assert: the card's chip shows the package, version and the source's alias
        Assert.True(WaitForText(mainWindow, "RepoPinnedVersionText", $"{packageName} 1.0.0"));
        Assert.True(WaitForText(mainWindow, "PackageSourceAliasText", "Feed"));

        // Assert: the password is nowhere AgentControl writes
        Assert.DoesNotContain(FeedPassword, ReadSharedText(settingsPath), StringComparison.Ordinal);
        Assert.DoesNotContain(FeedPassword, pinJson, StringComparison.Ordinal);
        foreach (var file in EnumerateTextFiles(context.ConfigDirectory))
        {
            Assert.DoesNotContain(FeedPassword, ReadSharedText(file), StringComparison.Ordinal);
        }

        // Assert: nor is it anywhere in the target repo
        foreach (var file in Directory.EnumerateFiles(context.RepoPath, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(FeedPassword, ReadSharedText(file), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A repo pinned to a server that needs a sign-in never gets a sign-in dialog from the
    ///     background upgrade check: the card says a sign-in is needed instead, and the server
    ///     receives no credential.
    /// </summary>
    [Fact]
    public async Task RepoCard_PinnedToServerNeedingSignIn_BackgroundCheckNeverOpensSignInDialog()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string packageName = "contoso-agents";
        await using var feed = await MockNuGetFeed.StartAsync();
        feed.AddPackage(packageName, "2.0.0");
        feed.RequiredUser = FeedUser;
        feed.RequiredPassword = FeedPassword;
        using var context = new AgentControlTestContext(
            pinnedPackageName: packageName,
            pinnedPackageVersion: "1.0.0",
            nuGetPackageIds: [packageName],
            pinnedSource: "Server",
            nuGetServerIndexUrl: feed.IndexUrl);

        // Act: start the application; the background check of the pinned server runs on its own
        var mainWindow = context.Launch();

        // Assert: the card says a sign-in is needed
        Assert.True(WaitForText(mainWindow, "SourceProblemBadge", "Sign-in needed"));

        // Assert: no sign-in dialog opened for work the user did not start
        Assert.Null(context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(2)));
    }

    /// <summary>
    ///     Opens the Settings window from the main window.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The Settings window.</returns>
    private static Window OpenSettings(AgentControlTestContext context, Window mainWindow)
    {
        Find(mainWindow, "OpenSettingsButton").AsButton().Invoke();
        var settingsWindow = context.WaitForWindowWithDescendant("AddSourceButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(settingsWindow);
        return settingsWindow;
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

    /// <summary>
    ///     Waits until an element with the given AutomationId shows the given text.
    /// </summary>
    /// <param name="parent">The window or element to search under.</param>
    /// <param name="automationId">The AutomationId of the text element.</param>
    /// <param name="expected">The text expected.</param>
    /// <returns><see langword="true"/> when the text appeared in time.</returns>
    private static bool WaitForText(AutomationElement parent, string automationId, string expected) =>
        Retry.WhileFalse(
            () => parent.FindAllDescendants(cf => cf.ByAutomationId(automationId))
                .Any(element => string.Equals(element.Name, expected, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result;

    /// <summary>
    ///     Selects the list item that contains a text element with the given AutomationId and text.
    /// </summary>
    /// <param name="window">The window containing the list.</param>
    /// <param name="textAutomationId">The AutomationId of the text element inside each item.</param>
    /// <param name="text">The text of the wanted item.</param>
    private static void SelectListItemByText(AutomationElement window, string textAutomationId, string text)
    {
        var item = Retry.WhileNull(
            () =>
            {
                var current = window.FindAllDescendants(cf => cf.ByAutomationId(textAutomationId))
                    .FirstOrDefault(element => string.Equals(element.Name, text, StringComparison.Ordinal));
                while (current is not null && current.ControlType != ControlType.ListItem)
                {
                    current = current.Parent;
                }

                return current;
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200)).Result;
        Assert.NotNull(item);
        item.AsListBoxItem().Select();
    }

    /// <summary>
    ///     Lists the log, settings and configuration text files under a folder, leaving out the
    ///     NuGet package cache (package content, not application output).
    /// </summary>
    /// <param name="folder">The folder to search.</param>
    /// <returns>The paths of the text files.</returns>
    private static IEnumerable<string> EnumerateTextFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "nuget" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Reads a file that the running application may still hold open for writing.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The file's text.</returns>
    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
