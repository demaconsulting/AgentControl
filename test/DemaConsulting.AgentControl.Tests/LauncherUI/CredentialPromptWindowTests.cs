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
using Avalonia.Input;
using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Headless tests for <see cref="CredentialPromptWindow"/> and the application's
///     <see cref="AvaloniaCredentialPrompt"/>, which shows it on the UI thread on behalf of worker
///     threads, one dialog at a time.
/// </summary>
public class CredentialPromptWindowTests
{
    /// <summary>
    ///     Test that the dialog shows the feed's origin and the session-only note, masks the
    ///     password box, and keeps Sign in disabled until both fields are filled.
    /// </summary>
    [Fact]
    public void CredentialPromptWindow_Shown_ShowsOriginSessionNoteAndMaskedPassword() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new CredentialPromptWindow(CreateViewModel("https://feeds.example.com:8443/v3/index.json"));
        window.Show();

        // Assert: origin and note are prominent, the password is masked
        Assert.Equal("https://feeds.example.com:8443", window.FindControl<TextBlock>("CredentialPromptOriginText")!.Text);
        Assert.Equal(
            "Used for this session only - AgentControl does not save it.",
            window.FindControl<TextBlock>("CredentialPromptSessionNoteText")!.Text);
        Assert.Equal('●', window.FindControl<TextBox>("CredentialPromptPasswordTextBox")!.PasswordChar);
        Assert.False(window.FindControl<Button>("CredentialPromptSignInButton")!.IsEffectivelyEnabled);
        Assert.False(window.FindControl<TextBlock>("CredentialPromptInsecureText")!.IsVisible);
        Assert.False(window.FindControl<TextBlock>("CredentialPromptRejectedText")!.IsVisible);

        // Act: type both values
        window.FindControl<TextBox>("CredentialPromptUserNameTextBox")!.Text = "svc";
        window.FindControl<TextBox>("CredentialPromptPasswordTextBox")!.Text = "pw";

        // Assert
        Assert.True(window.FindControl<Button>("CredentialPromptSignInButton")!.IsEffectivelyEnabled);
        window.Close();
    });

    /// <summary>
    ///     Test that a plain-http feed shows a warning, and a rejected earlier credential shows
    ///     that the last sign-in did not work.
    /// </summary>
    [Fact]
    public void CredentialPromptWindow_InsecureAndRejected_ShowWarningAndRejectedNotice() => HeadlessTestApp.Run(() =>
    {
        // Arrange
        var window = new CredentialPromptWindow(CreateViewModel("http://127.0.0.1:5000/v3/index.json", rejected: true));
        window.Show();

        // Assert
        Assert.Equal("http://127.0.0.1:5000", window.FindControl<TextBlock>("CredentialPromptOriginText")!.Text);
        Assert.True(window.FindControl<TextBlock>("CredentialPromptInsecureText")!.IsVisible);
        Assert.True(window.FindControl<TextBlock>("CredentialPromptRejectedText")!.IsVisible);
        window.Close();
    });

    /// <summary>
    ///     Test that Sign in closes the dialog with the typed credential.
    /// </summary>
    [Fact]
    public void CredentialPromptWindow_SignInClicked_ReturnsTheTypedCredential() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var dialog = new CredentialPromptWindow(CreateViewModel());
            var result = dialog.ShowDialog<NuGetCredential?>(owner);
            dialog.FindControl<TextBox>("CredentialPromptUserNameTextBox")!.Text = "svc";
            dialog.FindControl<TextBox>("CredentialPromptPasswordTextBox")!.Text = "secret-pw";

            // Act
            DialogWindowTests.Click(dialog, "CredentialPromptSignInButton");

            // Assert
            var credential = await result;
            Assert.NotNull(credential);
            Assert.Equal("svc", credential.UserName);
            Assert.Equal("secret-pw", credential.Secret);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that Cancel, Escape and closing the window all count as "no credential".
    /// </summary>
    [Fact]
    public void CredentialPromptWindow_CancelEscapeOrClose_ReturnNoCredential() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            // Act / Assert: the Cancel button
            var byButton = new CredentialPromptWindow(CreateViewModel());
            var buttonResult = byButton.ShowDialog<NuGetCredential?>(owner);
            DialogWindowTests.Click(byButton, "CredentialPromptCancelButton");
            Assert.Null(await buttonResult);

            // Act / Assert: the Escape key
            var byKey = new CredentialPromptWindow(CreateViewModel());
            var keyResult = byKey.ShowDialog<NuGetCredential?>(owner);
            byKey.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Null(await keyResult);

            // Act / Assert: closing the window (the title-bar button)
            var viewModel = CreateViewModel();
            var answered = new List<NuGetCredential?>();
            viewModel.Completed += (_, credential) => answered.Add(credential);
            var byClose = new CredentialPromptWindow(viewModel);
            var closeResult = byClose.ShowDialog<NuGetCredential?>(owner);
            byClose.Close();
            Assert.Null(await closeResult);
            Assert.Null(Assert.Single(answered));
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that the constructors reject a null view model and the parameterless previewer
    ///     constructor works.
    /// </summary>
    [Fact]
    public void CredentialPromptWindow_Constructors_NullViewModelThrows_ParameterlessWorks() => HeadlessTestApp.Run(() =>
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new CredentialPromptWindow(null!));
        new CredentialPromptWindow().Close();
    });

    /// <summary>
    ///     Test that a question asked from a worker thread is shown on the UI thread over the
    ///     owner window, and the credential typed there is returned to the asking thread.
    /// </summary>
    [Fact]
    public void AvaloniaCredentialPrompt_PromptAsync_FromWorkerThread_ShowsDialogAndReturnsCredential() =>
        HeadlessTestApp.RunAsync(async () =>
        {
            // Arrange
            var owner = new Window();
            owner.Show();
            try
            {
                var prompt = new AvaloniaCredentialPrompt(() => owner);

                // Act: ask from a worker thread, as the NuGet library does
                var pending = Task.Run(() => prompt.PromptAsync(CreateRequest(), CancellationToken.None));
                var dialog = await WaitForDialogAsync(owner);
                dialog.FindControl<TextBox>("CredentialPromptUserNameTextBox")!.Text = "svc";
                dialog.FindControl<TextBox>("CredentialPromptPasswordTextBox")!.Text = "worker-pw";
                DialogWindowTests.Click(dialog, "CredentialPromptSignInButton");

                // Assert
                var credential = await pending;
                Assert.NotNull(credential);
                Assert.Equal("svc", credential.UserName);
                Assert.Equal("worker-pw", credential.Secret);
            }
            finally
            {
                owner.Close();
            }
        });

    /// <summary>
    ///     Test that a user cancelling the dialog is reported as no credential.
    /// </summary>
    [Fact]
    public void AvaloniaCredentialPrompt_PromptAsync_UserCancels_ReturnsNull() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var prompt = new AvaloniaCredentialPrompt(() => owner);

            // Act
            var pending = Task.Run(() => prompt.PromptAsync(CreateRequest(), CancellationToken.None));
            DialogWindowTests.Click(await WaitForDialogAsync(owner), "CredentialPromptCancelButton");

            // Assert
            Assert.Null(await pending);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that cancelling the question's token closes the open dialog and surfaces as a
    ///     cancellation, not as a decline.
    /// </summary>
    [Fact]
    public void AvaloniaCredentialPrompt_PromptAsync_TokenCancelled_ClosesDialogAndThrows() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var prompt = new AvaloniaCredentialPrompt(() => owner);
            using var cancellation = new CancellationTokenSource();
            var pending = Task.Run(() => prompt.PromptAsync(CreateRequest(), cancellation.Token));
            var dialog = await WaitForDialogAsync(owner);

            // Act
            await cancellation.CancelAsync();

            // Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that two operations asking at once get one dialog at a time: the second appears
    ///     only after the first is answered.
    /// </summary>
    [Fact]
    public void AvaloniaCredentialPrompt_PromptAsync_TwoQuestionsAtOnce_AreShownOneAtATime() => HeadlessTestApp.RunAsync(async () =>
    {
        // Arrange
        var owner = new Window();
        owner.Show();
        try
        {
            var prompt = new AvaloniaCredentialPrompt(() => owner);

            // Act: two questions at once
            var first = Task.Run(() => prompt.PromptAsync(CreateRequest(), CancellationToken.None));
            var firstDialog = await WaitForDialogAsync(owner);
            var second = Task.Run(() => prompt.PromptAsync(CreateRequest(), CancellationToken.None));
            await Task.Delay(300, TestContext.Current.CancellationToken);

            // Assert: still just the first dialog
            Assert.Single(owner.OwnedWindows.OfType<CredentialPromptWindow>());
            Assert.False(second.IsCompleted);

            // Act: answer the first; the second dialog appears
            DialogWindowTests.Click(firstDialog, "CredentialPromptCancelButton");
            Assert.Null(await first);
            var secondDialog = await WaitForDialogAsync(owner, except: firstDialog);
            DialogWindowTests.Click(secondDialog, "CredentialPromptCancelButton");

            // Assert
            Assert.Null(await second);
        }
        finally
        {
            owner.Close();
        }
    });

    /// <summary>
    ///     Test that a null request is refused.
    /// </summary>
    [Fact]
    public void AvaloniaCredentialPrompt_PromptAsync_NullRequest_Throws() => HeadlessTestApp.RunAsync(async () =>
    {
        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new AvaloniaCredentialPrompt().PromptAsync(null!, CancellationToken.None));
    });

    /// <summary>
    ///     Waits (on the UI thread, letting it run) for a sign-in dialog to open over an owner.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="except">A dialog to ignore, or <see langword="null"/>.</param>
    /// <returns>The dialog.</returns>
    private static async Task<CredentialPromptWindow> WaitForDialogAsync(Window owner, CredentialPromptWindow? except = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var dialog = owner.OwnedWindows.OfType<CredentialPromptWindow>()
                .FirstOrDefault(window => window.IsVisible && !ReferenceEquals(window, except));
            if (dialog is not null)
            {
                return dialog;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The sign-in dialog did not open.");
    }

    /// <summary>
    ///     Creates a prompt request for a feed.
    /// </summary>
    /// <returns>The request.</returns>
    private static CredentialPromptRequest CreateRequest() =>
        new(FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json")), "Feed", false);

    /// <summary>
    ///     Creates a view model for a feed.
    /// </summary>
    /// <param name="indexUrl">The feed address.</param>
    /// <param name="rejected">Whether an earlier credential was rejected.</param>
    /// <returns>The view model.</returns>
    private static CredentialPromptViewModel CreateViewModel(
        string indexUrl = "https://feeds.example.com/v3/index.json", bool rejected = false) =>
        new(new CredentialPromptRequest(FeedOrigin.Create(new Uri(indexUrl)), "Feed", rejected));
}
