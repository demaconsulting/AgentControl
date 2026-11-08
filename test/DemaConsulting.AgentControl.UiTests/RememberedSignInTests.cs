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
using FlaUI.Core.Tools;

namespace DemaConsulting.AgentControl.UiTests;

/// <summary>
///     End-to-end tests of remembering a package server's sign-in: with the user's Git credential
///     helper (emulated by the arg-logger stub standing in for <c>git</c>, backed by a file outside
///     AgentControl's own folders) the sign-in is kept only there when the user ticks "Remember on
///     this computer", is used again without a dialog, and is removed by "Forget saved sign-in";
///     without a helper the choice is unavailable.
/// </summary>
/// <remarks>
///     The mock feed runs inside this test process while the application under test runs as its
///     own process. The tests also check the credential rule end to end: the password is never
///     found in anything AgentControl itself wrote.
/// </remarks>
public sealed class RememberedSignInTests
{
    /// <summary>
    ///     The feed account's user name.
    /// </summary>
    private const string FeedUser = "svc-remember";

    /// <summary>
    ///     The feed account's password; must only ever be found in the emulated helper's store.
    /// </summary>
    private const string FeedPassword = "ui-test-remember-pw-5b27";

    /// <summary>
    ///     The package the feed offers.
    /// </summary>
    private const string PackageName = "contoso-agents";

    /// <summary>
    ///     Ticking "Remember on this computer" while signing in hands the sign-in to the user's Git
    ///     credential helper - and to nothing AgentControl itself writes.
    /// </summary>
    [Fact]
    public async Task SettingsWindow_SignInWithRememberTicked_SavedOnlyByTheUsersCredentialHelper()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        await using var feed = await StartFeedAsync();
        using var context = new AgentControlTestContext();
        var helperStore = Path.Combine(context.RootDirectory, "helper-credentials.tsv");
        context.CredentialStoreFile = helperStore;
        var mainWindow = context.Launch();

        // Act: add the server, test it and sign in with "Remember" ticked
        var settingsWindow = OpenSettings(context, mainWindow);
        var editor = FillServerEditor(context, settingsWindow, feed);
        Find(editor, "SourceTestConnectionButton").AsButton().Invoke();
        var signIn = context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(30));
        Assert.NotNull(signIn);

        // Assert: the choice is offered, off, and the note says what ticking it means
        var remember = Find(signIn, "CredentialPromptRememberCheckBox").AsCheckBox();
        Assert.True(remember.IsEnabled);
        Assert.NotEqual(true, remember.IsChecked);
        Assert.Contains("session only", Find(signIn, "CredentialPromptSessionNoteText").Name, StringComparison.Ordinal);
        remember.IsChecked = true;
        Assert.Contains("Git credential helper", Find(signIn, "CredentialPromptSessionNoteText").Name, StringComparison.Ordinal);

        // Act: sign in
        Find(signIn, "CredentialPromptUserNameTextBox").AsTextBox().Text = FeedUser;
        Find(signIn, "CredentialPromptPasswordTextBox").AsTextBox().Text = FeedPassword;
        var signInButton = Find(signIn, "CredentialPromptSignInButton").AsButton();
        Assert.True(Retry.WhileFalse(() => signInButton.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        signInButton.Invoke();
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "Connection OK"));

        // Assert: the user's helper was asked to keep it
        Assert.True(Retry.WhileFalse(
            () => File.Exists(helperStore) && ReadSharedText(helperStore).Contains(FeedPassword, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.Contains(feed.BaseUrl, ReadSharedText(helperStore), StringComparison.Ordinal);

        // Act: save the source and the settings
        var save = Find(editor, "SourceEditorSaveButton").AsButton();
        Assert.True(Retry.WhileFalse(() => save.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        save.Invoke();
        Assert.True(WaitForText(settingsWindow, "SourceAliasText", "Feed"));
        Find(settingsWindow, "SaveSettingsButton").AsButton().Invoke();
        var settingsPath = Path.Combine(context.ConfigDirectory, "settings.json");
        Assert.True(Retry.WhileFalse(
            () => File.Exists(settingsPath) && ReadSharedText(settingsPath).Contains("\"Feed\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(200)).Result);

        // Assert: nothing AgentControl wrote - settings, pins, logs - holds the password
        foreach (var file in EnumerateTextFiles(context.ConfigDirectory))
        {
            Assert.DoesNotContain(FeedPassword, ReadSharedText(file), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     A sign-in the user's helper saved earlier is used without any dialog, and "Forget saved
    ///     sign-in" - after a confirmation - removes it from the helper.
    /// </summary>
    [Fact]
    public async Task SettingsWindow_SavedSignIn_UsedWithoutDialogAndForgottenOnRequest()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        await using var feed = await StartFeedAsync();
        using var context = new AgentControlTestContext();
        var helperStore = Path.Combine(context.RootDirectory, "helper-credentials.tsv");
        await File.WriteAllTextAsync(helperStore, $"{feed.BaseUrl}\t{FeedUser}\t{FeedPassword}\n", TestContext.Current.CancellationToken);
        context.CredentialStoreFile = helperStore;
        var mainWindow = context.Launch();

        // Act: add the server and test it - the saved sign-in is used
        var settingsWindow = OpenSettings(context, mainWindow);
        var editor = FillServerEditor(context, settingsWindow, feed);
        Find(editor, "SourceTestConnectionButton").AsButton().Invoke();

        // Assert: it connects and no sign-in dialog ever appeared
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "Connection OK"));
        Assert.Null(context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(1)));

        // Act: forget the saved sign-in, declining first
        var forget = Find(editor, "SourceForgetSignInButton").AsButton();
        Assert.True(Retry.WhileFalse(() => forget.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        forget.Invoke();
        var confirmation = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(confirmation);
        Assert.Contains(feed.BaseUrl, Find(confirmation, "ConfirmationMessageText").Name, StringComparison.Ordinal);
        Find(confirmation, "ConfirmationNoButton").AsButton().Invoke();

        // Assert: declining leaves the saved sign-in alone
        Assert.Contains(FeedPassword, ReadSharedText(helperStore), StringComparison.Ordinal);

        // Act: forget it for real
        forget.Invoke();
        confirmation = context.WaitForWindowWithDescendant("ConfirmationYesButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(confirmation);
        Find(confirmation, "ConfirmationYesButton").AsButton().Invoke();

        // Assert: the result is said in words and the helper no longer holds it
        Assert.True(Retry.WhileFalse(
            () => Find(editor, "SourceForgetResultText").Name.Contains("was forgotten", StringComparison.Ordinal),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(200)).Result);
        Assert.DoesNotContain(FeedPassword, ReadSharedText(helperStore), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Without a Git credential helper "Remember on this computer" is offered disabled and
    ///     "Forget saved sign-in" is unavailable, so the user is never offered a choice that would
    ///     silently do nothing.
    /// </summary>
    [Fact]
    public async Task SettingsWindow_NoCredentialHelper_RememberAndForgetAreUnavailable()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        await using var feed = await StartFeedAsync();
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // Act: add the server and test it, which asks for a sign-in
        var settingsWindow = OpenSettings(context, mainWindow);
        var editor = FillServerEditor(context, settingsWindow, feed);
        Find(editor, "SourceTestConnectionButton").AsButton().Invoke();
        var signIn = context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(30));
        Assert.NotNull(signIn);

        // Assert: the choice is there but disabled, and nothing is saved beyond the session
        Assert.False(Find(signIn, "CredentialPromptRememberCheckBox").AsCheckBox().IsEnabled);
        Assert.Contains("session only", Find(signIn, "CredentialPromptSessionNoteText").Name, StringComparison.Ordinal);
        Assert.False(Find(editor, "SourceForgetSignInButton").AsButton().IsEnabled);

        // Act: leave without signing in
        Find(signIn, "CredentialPromptCancelButton").AsButton().Invoke();
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "Sign-in canceled"));
    }

    /// <summary>
    ///     A sign-in the user's helper saved earlier that the server no longer accepts is removed
    ///     from the helper, so the user is not stuck with a stale saved credential.
    /// </summary>
    [Fact]
    public async Task SettingsWindow_SavedSignInRejectedByServer_IsRemovedFromTheUsersHelper()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        const string staleSentinel = "ui-test-stale-pw-9c13";
        await using var feed = await StartFeedAsync();
        using var context = new AgentControlTestContext();
        var helperStore = Path.Combine(context.RootDirectory, "helper-credentials.tsv");
        await File.WriteAllTextAsync(helperStore, $"{feed.BaseUrl}\t{FeedUser}\t{staleSentinel}\n", TestContext.Current.CancellationToken);
        context.CredentialStoreFile = helperStore;
        var mainWindow = context.Launch();

        // Act: add the server and test it - the stale saved sign-in is tried and refused
        var settingsWindow = OpenSettings(context, mainWindow);
        var editor = FillServerEditor(context, settingsWindow, feed);
        Find(editor, "SourceTestConnectionButton").AsButton().Invoke();

        // Assert: the helper no longer holds the stale sign-in once the server has refused it
        Assert.True(Retry.WhileFalse(
            () => !ReadSharedText(helperStore).Contains(staleSentinel, StringComparison.Ordinal),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)).Result);

        // Act: leave without signing in, if the application asked
        var signIn = context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(5));
        if (signIn is not null)
        {
            Find(signIn, "CredentialPromptCancelButton").AsButton().Invoke();
        }
    }

    /// <summary>
    ///     A sign-in typed for one server is never offered to another server: the second server gets
    ///     its own sign-in dialog and never receives the first server's credential.
    /// </summary>
    [Fact]
    public async Task SettingsWindow_SignInToOneServer_IsNeverOfferedToAnotherServer()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            Assert.Skip("FlaUI end-to-end tests require Windows UI Automation.");
            return;
        }

        await using var firstFeed = await StartFeedAsync();
        await using var secondFeed = await MockNuGetFeed.StartAsync();
        secondFeed.AddPackage(PackageName, "1.0.0");
        secondFeed.RequiredUser = "svc-other";
        secondFeed.RequiredPassword = "ui-test-other-pw-4d82";
        using var context = new AgentControlTestContext();
        var mainWindow = context.Launch();

        // Act: add the first server, sign in to it and save it
        var settingsWindow = OpenSettings(context, mainWindow);
        var editor = FillServerEditor(context, settingsWindow, firstFeed);
        Find(editor, "SourceTestConnectionButton").AsButton().Invoke();
        var signIn = context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(30));
        Assert.NotNull(signIn);
        Find(signIn, "CredentialPromptUserNameTextBox").AsTextBox().Text = FeedUser;
        Find(signIn, "CredentialPromptPasswordTextBox").AsTextBox().Text = FeedPassword;
        var signInButton = Find(signIn, "CredentialPromptSignInButton").AsButton();
        Assert.True(Retry.WhileFalse(() => signInButton.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        signInButton.Invoke();
        Assert.True(WaitForText(editor, "SourceTestResultTitleText", "Connection OK"));
        var save = Find(editor, "SourceEditorSaveButton").AsButton();
        Assert.True(Retry.WhileFalse(() => save.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        save.Invoke();
        Assert.True(WaitForText(settingsWindow, "SourceAliasText", "Feed"));

        // Act: add a second server on another origin and test it
        var secondEditor = FillServerEditor(context, settingsWindow, secondFeed, "Other");
        Find(secondEditor, "SourceTestConnectionButton").AsButton().Invoke();

        // Assert: the second server asks for its own sign-in, naming itself
        var secondSignIn = context.WaitForWindowWithDescendant("CredentialPromptUserNameTextBox", TimeSpan.FromSeconds(30));
        Assert.NotNull(secondSignIn);
        Assert.Equal(secondFeed.BaseUrl, Find(secondSignIn, "CredentialPromptOriginText").Name);

        // Act: leave without signing in
        Find(secondSignIn, "CredentialPromptCancelButton").AsButton().Invoke();
        Assert.True(WaitForText(secondEditor, "SourceTestResultTitleText", "Sign-in canceled"));

        // Assert: the second server never received the first server's credential
        var firstServersHeader = "Basic " + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{FeedUser}:{FeedPassword}"));
        Assert.DoesNotContain(firstServersHeader, secondFeed.AuthorizationHeaders);
        Assert.Contains(firstServersHeader, firstFeed.AuthorizationHeaders);
    }

    /// <summary>
    ///     Starts a mock feed that requires the test account's sign-in.
    /// </summary>
    /// <returns>The running feed.</returns>
    private static async Task<MockNuGetFeed> StartFeedAsync()
    {
        var feed = await MockNuGetFeed.StartAsync();
        feed.AddPackage(PackageName, "1.0.0");
        feed.RequiredUser = FeedUser;
        feed.RequiredPassword = FeedPassword;
        return feed;
    }

    /// <summary>
    ///     Opens the Add NuGet-Package Server dialog and fills it in for the mock feed, leaving the
    ///     connection untested.
    /// </summary>
    /// <param name="context">The running test context.</param>
    /// <param name="settingsWindow">The Settings window.</param>
    /// <param name="feed">The mock feed.</param>
    /// <param name="alias">The alias to give the source.</param>
    /// <returns>The source editor window.</returns>
    private static Window FillServerEditor(
        AgentControlTestContext context, Window settingsWindow, MockNuGetFeed feed, string alias = "Feed")
    {
        Find(settingsWindow, "AddNuGetServerSourceButton").AsButton().Invoke();
        var editor = context.WaitForWindowWithDescendant("SourceTestConnectionButton", TimeSpan.FromSeconds(15));
        Assert.NotNull(editor);
        Find(editor, "SourceAliasTextBox").AsTextBox().Text = alias;
        Find(editor, "SourceLocationTextBox").AsTextBox().Text = feed.IndexUrl;
        Find(editor, "SourceAllowInsecureHttpCheckBox").AsCheckBox().IsChecked = true;
        Find(editor, "SourcePackageIdTextBox").AsTextBox().Text = PackageName;
        Find(editor, "SourceAddPackageIdButton").AsButton().Invoke();
        var testButton = Find(editor, "SourceTestConnectionButton").AsButton();
        Assert.True(Retry.WhileFalse(() => testButton.IsEnabled, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200)).Result);
        return editor;
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
        var settingsWindow = context.WaitForWindowWithDescendant("AddZipSourceButton", TimeSpan.FromSeconds(15));
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
