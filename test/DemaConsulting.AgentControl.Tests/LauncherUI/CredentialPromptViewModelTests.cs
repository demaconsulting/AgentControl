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

using DemaConsulting.AgentControl.LauncherUI;
using DemaConsulting.AgentControl.PackageCredentials;

namespace DemaConsulting.AgentControl.Tests.LauncherUI;

/// <summary>
///     Unit tests for <see cref="CredentialPromptViewModel"/>, the logic of the sign-in dialog.
/// </summary>
public class CredentialPromptViewModelTests
{
    /// <summary>
    ///     Test that the dialog names the feed's origin and the source, warns about plain http only
    ///     for an insecure feed, and states that the credential is kept for the session only.
    /// </summary>
    [Fact]
    public void CredentialPromptViewModel_Constructor_ShowsOriginSourceAndSessionNote()
    {
        // Act
        var secure = Create("https://feeds.example.com:8443/v3/index.json");
        var insecure = Create("http://127.0.0.1:5000/v3/index.json");

        // Assert
        Assert.Equal("https://feeds.example.com:8443", secure.OriginText);
        Assert.Equal("Feed", secure.SourceAlias);
        Assert.Contains("'Feed'", secure.Explanation, StringComparison.Ordinal);
        Assert.False(secure.IsInsecure);
        Assert.True(insecure.IsInsecure);
        Assert.Equal("http://127.0.0.1:5000", insecure.OriginText);
        Assert.Equal(
            "Used for this session only - AgentControl does not save it.",
            CredentialPromptViewModel.SessionNoteText);
    }

    /// <summary>
    ///     Test that signing in needs both a user name and a password.
    /// </summary>
    [Fact]
    public void CredentialPromptViewModel_CanSignIn_NeedsUserNameAndPassword()
    {
        // Arrange
        var viewModel = Create();

        // Assert: nothing entered
        Assert.False(viewModel.CanSignIn);
        Assert.False(viewModel.SignInCommand.CanExecute(null));

        // Act / Assert: only a user name, only blanks, then both
        viewModel.UserName = "svc";
        Assert.False(viewModel.CanSignIn);
        viewModel.UserName = "   ";
        viewModel.Password = "pw";
        Assert.False(viewModel.CanSignIn);
        viewModel.UserName = "svc";
        Assert.True(viewModel.CanSignIn);
        Assert.True(viewModel.SignInCommand.CanExecute(null));
    }

    /// <summary>
    ///     Test that signing in completes once with the entered credential, trimming the user name
    ///     but keeping the password exactly as typed.
    /// </summary>
    [Fact]
    public void CredentialPromptViewModel_SignIn_EnteredCredential_CompletesOnceWithTrimmedUserName()
    {
        // Arrange
        var viewModel = Create();
        var answers = new List<CredentialPromptAnswer?>();
        viewModel.Completed += (_, given) => answers.Add(given);
        viewModel.UserName = "  svc  ";
        viewModel.Password = " pw with spaces ";

        // Act: sign in, then try to answer again
        viewModel.SignInCommand.Execute(null);
        viewModel.Cancel();
        viewModel.SignInCommand.Execute(null);

        // Assert
        var answer = Assert.Single(answers);
        Assert.NotNull(answer);
        Assert.Equal("svc", answer.Credential.UserName);
        Assert.Equal(" pw with spaces ", answer.Credential.Secret);
        Assert.False(answer.Remember);
    }

    /// <summary>
    ///     Test that cancelling answers with no credential, and that signing in before the fields
    ///     are complete does nothing.
    /// </summary>
    [Fact]
    public void CredentialPromptViewModel_Cancel_CompletesWithNoCredential()
    {
        // Arrange
        var viewModel = Create();
        var answers = new List<CredentialPromptAnswer?>();
        viewModel.Completed += (_, given) => answers.Add(given);

        // Act: an incomplete sign-in is ignored, then cancel
        viewModel.SignInCommand.Execute(null);
        Assert.Empty(answers);
        viewModel.CancelCommand.Execute(null);

        // Assert
        Assert.Null(Assert.Single(answers));
    }

    /// <summary>
    ///     Test that "Remember on this computer" is off by default, that ticking it answers with the
    ///     remember choice and changes the note to say the Git credential helper keeps it.
    /// </summary>
    [Fact]
    public void CredentialPromptViewModel_Remember_TickedWhenAvailable_AnswersWithRememberAndChangesTheNote()
    {
        // Arrange
        var viewModel = Create(canRemember: true);
        var answers = new List<CredentialPromptAnswer?>();
        viewModel.Completed += (_, given) => answers.Add(given);
        viewModel.UserName = "svc";
        viewModel.Password = "pw";

        // Assert: unchecked by default, enabled, explained
        Assert.True(viewModel.CanRemember);
        Assert.False(viewModel.Remember);
        Assert.Equal(CredentialPromptViewModel.SessionNoteText, viewModel.NoteText);
        Assert.Equal(CredentialPromptViewModel.RememberEnabledTooltip, viewModel.RememberTooltip);

        // Act
        viewModel.Remember = true;
        viewModel.SignInCommand.Execute(null);

        // Assert
        Assert.Equal(CredentialPromptViewModel.RememberedNoteText, viewModel.NoteText);
        Assert.True(Assert.Single(answers)!.Remember);
    }

    /// <summary>
    ///     Test that when no Git credential helper is available the option cannot be turned on, says
    ///     why, and the answer never asks to remember.
    /// </summary>
    [Fact]
    public void CredentialPromptViewModel_Remember_NoCredentialHelper_IsDisabledAndNeverRemembers()
    {
        // Arrange
        var viewModel = Create(canRemember: false);
        var answers = new List<CredentialPromptAnswer?>();
        viewModel.Completed += (_, given) => answers.Add(given);
        viewModel.UserName = "svc";
        viewModel.Password = "pw";

        // Act: try to tick it anyway
        viewModel.Remember = true;
        viewModel.SignInCommand.Execute(null);

        // Assert
        Assert.False(viewModel.CanRemember);
        Assert.False(viewModel.Remember);
        Assert.Contains("no Git credential helper", viewModel.RememberTooltip, StringComparison.Ordinal);
        Assert.Equal(CredentialPromptViewModel.SessionNoteText, viewModel.NoteText);
        Assert.False(Assert.Single(answers)!.Remember);
    }

    /// <summary>
    ///     Test that a null request is refused.
    /// </summary>
    [Fact]
    public void CredentialPromptViewModel_Constructor_NullRequest_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new CredentialPromptViewModel(null!));
    }

    /// <summary>
    ///     Creates a view model for a feed.
    /// </summary>
    /// <param name="indexUrl">The feed address.</param>
    /// <param name="previousRejected">Whether a previous credential was rejected.</param>
    /// <param name="canRemember">Whether a Git credential helper can keep the sign-in.</param>
    /// <returns>The view model.</returns>
    private static CredentialPromptViewModel Create(
        string indexUrl = "https://feeds.example.com/v3/index.json",
        bool previousRejected = false,
        bool canRemember = false) =>
        new(new CredentialPromptRequest(FeedOrigin.Create(new Uri(indexUrl)), "Feed", previousRejected, canRemember));
}
