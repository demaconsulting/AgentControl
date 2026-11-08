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
using Avalonia.Threading;
using Avalonia.VisualTree;
using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Tests.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests of the signature options and the "Forget saved sign-in" button in
///     <see cref="SourceEditorWindow"/>: what is shown, enabled and explained, and what the buttons
///     do.
/// </summary>
public class SourceEditorSignatureWindowTests
{
    /// <summary>
    ///     A valid https feed address.
    /// </summary>
    private const string FeedAddress = "https://feeds.example.com:8443/v3/index.json";

    /// <summary>
    ///     A fingerprint as certificate viewers print it.
    /// </summary>
    private const string ColonFingerprint =
        "a3:af:7a:f1:1e:bb:8e:f7:29:d2:d9:15:48:50:97:17:e7:e0:ff:55:a1:29:ab:c3:ae:aa:8a:69:40:26:76:41";

    /// <summary>
    ///     Test that the signature section shows the checkbox (unchecked), the help text and the
    ///     disabled fingerprint field; ticking the box enables the field, an unreadable fingerprint
    ///     shows an error and disables Save, and a readable one saves in normalized form.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_SignatureOptions_TickingEnablesTheFingerprintAndValidatesIt() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var editor = CreateServerEditor();
        var owner = new Window();
        owner.Show();
        try
        {
            var window = new SourceEditorWindow(editor);
            var result = window.ShowDialog<PackageSourceDefinition?>(owner);
            var checkBox = window.FindControl<CheckBox>("SourceRequireSignedCheckBox")!;
            var fingerprint = window.FindControl<TextBox>("SourceSignerFingerprintTextBox")!;
            var save = window.FindControl<Button>("SourceEditorSaveButton")!;

            // Assert: offered, off, explained
            Assert.True(window.FindControl<StackPanel>("SourceEditorSignatureSection")!.IsVisible);
            Assert.Equal("Require signed packages", checkBox.Content);
            Assert.False(checkBox.IsChecked);
            Assert.True(checkBox.IsEffectivelyEnabled);
            Assert.False(fingerprint.IsEffectivelyEnabled);
            Assert.Contains("unrelated publishers", window.FindControl<TextBlock>("SourceSignatureHelpText")!.Text, StringComparison.Ordinal);
            Assert.Contains("dotnet nuget verify", window.FindControl<TextBlock>("SourceFingerprintHelpText")!.Text, StringComparison.Ordinal);
            Assert.True(save.IsEffectivelyEnabled);

            // Act: tick the box, type garbage
            checkBox.IsChecked = true;
            fingerprint.Text = "garbage";

            // Assert: usable, with an error, and no saving
            Assert.True(fingerprint.IsEffectivelyEnabled);
            Assert.True(window.FindControl<TextBlock>("SourceFingerprintErrorText")!.IsVisible);
            Assert.Contains("64 hexadecimal characters", window.FindControl<TextBlock>("SourceFingerprintErrorText")!.Text, StringComparison.Ordinal);
            Assert.False(save.IsEffectivelyEnabled);

            // Act: a readable fingerprint, then save
            fingerprint.Text = ColonFingerprint;
            Assert.False(window.FindControl<TextBlock>("SourceFingerprintErrorText")!.IsVisible);
            Assert.True(save.IsEffectivelyEnabled);
            DialogWindowTests.Click(window, "SourceEditorSaveButton");

            // Assert
            var saved = await result;
            Assert.NotNull(saved);
            Assert.True(saved.RequireSignedPackages);
            Assert.Equal("A3AF7AF11EBB8EF729D2D91548509717E7E0FF55A129ABC3AEAA8A6940267641", saved.SignerFingerprint);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that where signature verification is unsupported the checkbox is disabled and carries
    ///     the explanation as a tooltip that is shown even though the control is disabled.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_SignatureOptions_UnsupportedPlatform_CheckboxDisabledWithTooltip() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new SourceEditorWindow(CreateServerEditor(signaturesSupported: false));
        window.Show();
        var checkBox = window.FindControl<CheckBox>("SourceRequireSignedCheckBox")!;

        // Assert
        Assert.False(checkBox.IsEffectivelyEnabled);
        Assert.False(checkBox.IsChecked);
        Assert.Equal("Package signature verification is not supported on this platform", ToolTip.GetTip(checkBox));
        Assert.True(ToolTip.GetShowOnDisabled(checkBox));
        Assert.False(window.FindControl<TextBox>("SourceSignerFingerprintTextBox")!.IsEffectivelyEnabled);
        window.Close();
    });

    /// <summary>
    ///     Test that the server editor is a stack of titled sections - Source, Packages, Sign-in,
    ///     Package signatures, Connection test - in that order, that "Forget saved sign-in" sits in
    ///     the Sign-in section and not beside the connection test, and that a folder editor shows
    ///     only the sections that apply to it.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_Sections_ServerEditorHasTitledSectionsAndForgetBelongsToSignIn() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new SourceEditorWindow(CreateServerEditor());
        window.Show();
        try
        {
            // Assert: the section headers, in order
            var headers = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.Classes.Contains("section") && text.IsEffectivelyVisible)
                .Select(text => text.Text)
                .ToList();
            Assert.Equal(["Source", "Packages", "Sign-in", "Package signatures", "Connection test"], headers);

            // Assert: Forget is inside the Sign-in section, Test connection inside the test section
            var signIn = window.FindControl<StackPanel>("SourceEditorSignInSection")!;
            var test = window.FindControl<StackPanel>("SourceEditorTestSection")!;
            var forget = window.FindControl<Button>("SourceForgetSignInButton")!;
            var testButton = window.FindControl<Button>("SourceTestConnectionButton")!;
            Assert.Contains(forget, signIn.GetVisualDescendants());
            Assert.DoesNotContain(forget, test.GetVisualDescendants());
            Assert.Contains(testButton, test.GetVisualDescendants());
            Assert.Equal(SourceEditorViewModel.SignInHelpText, window.FindControl<TextBlock>("SourceSignInHelpText")!.Text);
        }
        finally
        {
            window.Close();
        }

        // Arrange: a NuGet folder has no sign-in and no connection test
        var folderWindow = new SourceEditorWindow(
            new SourceEditorViewModel(PackageSourceType.NuGet, null, _ => false));
        folderWindow.Show();
        try
        {
            // Assert
            var folderHeaders = folderWindow.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.Classes.Contains("section") && text.IsEffectivelyVisible)
                .Select(text => text.Text)
                .ToList();
            Assert.Equal(["Source", "Packages", "Package signatures"], folderHeaders);
        }
        finally
        {
            folderWindow.Close();
        }
    });

    /// <summary>
    ///     Test that a zip-package editor has no signature section.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_ZipSource_HasNoSignatureSection() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new SourceEditorWindow(new SourceEditorViewModel(PackageSourceType.Zip, null, _ => false));
        window.Show();

        // Assert
        Assert.False(window.FindControl<StackPanel>("SourceEditorSignatureSection")!.IsVisible);
        window.Close();
    });

    /// <summary>
    ///     Test that "Forget saved sign-in" asks for confirmation first - declining leaves the saved
    ///     sign-in alone - and, once confirmed, removes it and says so.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_ForgetSavedSignIn_ConfirmationDecidesAndResultIsShown() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange: a saved sign-in in the user's store
        var origin = FeedOrigin.Create(new Uri(FeedAddress));
        var usersStore = new FakeRememberedCredentialStore();
        usersStore.Save(origin, new NuGetCredential("svc", "pw-forget-window-4422"));
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        editor.Location = FeedAddress;
        var window = new SourceEditorWindow(editor);
        window.Show();
        try
        {
            var button = window.FindControl<Button>("SourceForgetSignInButton")!;
            Assert.True(button.IsEffectivelyEnabled);

            // Act: ask, then decline
            DialogWindowTests.Click(window, "SourceForgetSignInButton");
            Dispatcher.UIThread.RunJobs();
            var confirmation = Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>());
            Assert.Contains("https://feeds.example.com:8443", confirmation.FindControl<TextBlock>("ConfirmationMessageText")!.Text, StringComparison.Ordinal);
            DialogWindowTests.Click(confirmation, "ConfirmationNoButton");
            Dispatcher.UIThread.RunJobs();

            // Assert: kept
            Assert.NotNull(usersStore.Peek(origin));
            Assert.False(window.FindControl<TextBlock>("SourceForgetResultText")!.IsVisible);

            // Act: ask again and agree
            DialogWindowTests.Click(window, "SourceForgetSignInButton");
            Dispatcher.UIThread.RunJobs();
            DialogWindowTests.Click(Assert.Single(window.OwnedWindows.OfType<ConfirmationWindow>()), "ConfirmationYesButton");
            await WaitUntilAsync(() => editor.HasForgetResult);

            // Assert: removed, and the outcome is shown
            Assert.Null(usersStore.Peek(origin));
            Assert.True(window.FindControl<TextBlock>("SourceForgetResultText")!.IsVisible);
            Assert.Equal(
                "The saved sign-in for https://feeds.example.com:8443 was forgotten.",
                window.FindControl<TextBlock>("SourceForgetResultText")!.Text);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    ///     Test that the button is disabled, with its explanation shown on hover, when there is no
    ///     credential store to ask.
    /// </summary>
    [Fact]
    public void SourceEditorWindow_ForgetSavedSignIn_NoCredentialStore_ButtonDisabledWithTooltip() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var editor = CreateServerEditor();
        editor.Location = FeedAddress;
        var window = new SourceEditorWindow(editor);
        window.Show();
        var button = window.FindControl<Button>("SourceForgetSignInButton")!;

        // Assert
        Assert.False(button.IsEffectivelyEnabled);
        Assert.Contains("no Git credential helper", (string?)ToolTip.GetTip(button), StringComparison.Ordinal);
        Assert.True(ToolTip.GetShowOnDisabled(button));
        window.Close();
    });

    /// <summary>
    ///     Test that the button is disabled, with the explanation shown on hover, when the Git
    ///     credential helper is not available for the server (so nothing can have been saved).
    /// </summary>
    [Fact]
    public void SourceEditorWindow_ForgetSavedSignIn_NoCredentialHelper_ButtonDisabledWithTooltip() => HeadlessTestApp.Run(() =>
    {
        // Arrange: a credential store that has no helper
        var usersStore = new FakeRememberedCredentialStore { Available = false };
        var editor = CreateServerEditor(credentials: new PackageCredentialContext(new SessionCredentialStore(), null, usersStore));
        editor.Location = FeedAddress;
        var window = new SourceEditorWindow(editor);
        window.Show();
        var button = window.FindControl<Button>("SourceForgetSignInButton")!;

        // Assert
        Assert.False(button.IsEffectivelyEnabled);
        Assert.Equal(SourceEditorViewModel.NoHelperForgetTooltip, ToolTip.GetTip(button));
        Assert.True(ToolTip.GetShowOnDisabled(button));
        window.Close();
    });

    /// <summary>
    ///     Creates a server editor.
    /// </summary>
    /// <param name="signaturesSupported">Whether the platform is taken to support signature
    ///     verification.</param>
    /// <param name="credentials">The credential facilities, or <see langword="null"/>.</param>
    /// <returns>The editor, with an alias, a package ID and an address filled in.</returns>
    private static SourceEditorViewModel CreateServerEditor(
        bool signaturesSupported = true, PackageCredentialContext? credentials = null)
    {
        var editor = new SourceEditorViewModel(
            PackageSourceType.NuGet,
            null,
            _ => false,
            isServer: true,
            credentials: credentials,
            signatureVerificationSupported: signaturesSupported)
        {
            Alias = "Feed",
            Location = FeedAddress
        };
        editor.NewPackageId = "contoso-agents";
        editor.AddPackageIdCommand.Execute(null);
        return editor;
    }

    /// <summary>
    ///     Polls on the UI thread until a condition holds, processing queued UI work meanwhile.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
