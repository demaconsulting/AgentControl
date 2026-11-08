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

using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model for the dialog that asks the user for the user name and password (or access
///     token) a package feed needs.
/// </summary>
/// <remarks>
///     <para>
///     The dialog exists so a credential is only ever typed into a window that says, prominently,
///     which server will receive it: <see cref="OriginText"/> is the feed's scheme, host and port.
///     A feed on plain <c>http</c> is flagged with a warning because what is typed would cross the
///     network unprotected. The dialog states what becomes of the credential: it is used for this
///     session only (AgentControl keeps it in memory and never saves it) unless the user ticks
///     "Remember on this computer", an opt-in that is off by default and disabled - with an
///     explanatory tooltip - when no Git credential helper is available; the credential is then
///     saved by the user's own helper, never by AgentControl.
///     </para>
///     <para>
///     The view model holds the typed password only until the dialog closes and raises
///     <see cref="Completed"/> exactly once, with an answer or <see langword="null"/> when the
///     user cancelled. It never logs or otherwise exposes the password. Not thread-safe; every
///     member is expected to be called from the UI thread.
///     </para>
/// </remarks>
internal sealed class CredentialPromptViewModel : ViewModelBase
{
    /// <summary>
    ///     Backing field for <see cref="UserName"/>.
    /// </summary>
    private string _userName = string.Empty;

    /// <summary>
    ///     Backing field for <see cref="Password"/>.
    /// </summary>
    private string _password = string.Empty;

    /// <summary>
    ///     Backing field for <see cref="Remember"/>.
    /// </summary>
    private bool _remember;

    /// <summary>
    ///     Whether <see cref="Completed"/> has been raised already.
    /// </summary>
    private bool _completed;

    /// <summary>
    ///     Initializes the view model for one question.
    /// </summary>
    /// <param name="request">Which feed and source the credential is for.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is
    ///     <see langword="null"/>.</exception>
    public CredentialPromptViewModel(CredentialPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        OriginText = request.Origin.ToString();
        SourceAlias = request.SourceAlias;
        IsInsecure = !request.Origin.IsSecure;
        PreviousCredentialRejected = request.PreviousCredentialRejected;
        CanRemember = request.CanRemember;
        SignInCommand = new RelayCommand(SignIn, () => CanSignIn);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>
    ///     Gets the feed's origin (scheme, host and port) - the server that will receive the
    ///     credential - shown prominently so the user can check it.
    /// </summary>
    public string OriginText { get; }

    /// <summary>
    ///     Gets the alias of the configured source the sign-in is for.
    /// </summary>
    public string SourceAlias { get; }

    /// <summary>
    ///     Gets a value indicating whether the feed uses plain <c>http</c>, so the dialog warns
    ///     that what is typed is not encrypted.
    /// </summary>
    public bool IsInsecure { get; }

    /// <summary>
    ///     Gets a value indicating whether a credential for this feed was just rejected, so the
    ///     dialog says the last sign-in did not work.
    /// </summary>
    public bool PreviousCredentialRejected { get; }

    /// <summary>
    ///     Gets the sentence that explains what is being asked and why.
    /// </summary>
    public string Explanation =>
        $"The package source '{SourceAlias}' needs a user name and a password or access token.";

    /// <summary>
    ///     The sentence that states how long the credential is kept when it is not remembered;
    ///     shown in the dialog.
    /// </summary>
    public const string SessionNoteText = "Used for this session only - AgentControl does not save it.";

    /// <summary>
    ///     The sentence that states where the credential is kept when the user chose to remember
    ///     it; shown in the dialog instead of <see cref="SessionNoteText"/>.
    /// </summary>
    public const string RememberedNoteText =
        "Saved by your Git credential helper on this computer - AgentControl does not store it.";

    /// <summary>
    ///     The text of the "Remember" checkbox.
    /// </summary>
    public const string RememberLabel = "Remember on this computer (saved by your Git credential helper)";

    /// <summary>
    ///     The tooltip of the "Remember" checkbox when it can be used.
    /// </summary>
    public const string RememberEnabledTooltip =
        "Keep this sign-in in your Git credential helper (for example Git Credential Manager) so you " +
        "are not asked again on this computer. You can forget it later in the source's settings.";

    /// <summary>
    ///     The tooltip of the "Remember" checkbox when no Git credential helper is available.
    /// </summary>
    public const string RememberUnavailableTooltip =
        "Not available: no Git credential helper is configured on this computer, so a sign-in cannot be " +
        "saved. Install Git with a credential helper (for example Git Credential Manager) to enable this.";

    /// <summary>
    ///     Gets a value indicating whether the user's Git credential helper can keep this sign-in,
    ///     so the "Remember" checkbox can be used. When <see langword="false"/> the checkbox is
    ///     shown disabled, with <see cref="RememberTooltip"/> explaining why.
    /// </summary>
    public bool CanRemember { get; }

    /// <summary>
    ///     Gets or sets a value indicating whether the sign-in is to be saved by the user's Git
    ///     credential helper. Off by default, and never on while <see cref="CanRemember"/> is
    ///     <see langword="false"/>.
    /// </summary>
    public bool Remember
    {
        get => _remember;
        set
        {
            if (SetField(ref _remember, value && CanRemember))
            {
                OnPropertyChanged(nameof(NoteText));
            }
        }
    }

    /// <summary>
    ///     Gets the tooltip of the "Remember" checkbox: what ticking it does, or why it cannot be
    ///     ticked.
    /// </summary>
    public string RememberTooltip => CanRemember ? RememberEnabledTooltip : RememberUnavailableTooltip;

    /// <summary>
    ///     Gets the sentence that says what happens to the credential: used for this session only,
    ///     or saved by the Git credential helper when "Remember" is ticked.
    /// </summary>
    public string NoteText => _remember ? RememberedNoteText : SessionNoteText;

    /// <summary>
    ///     Gets or sets the user name.
    /// </summary>
    public string UserName
    {
        get => _userName;
        set
        {
            if (SetField(ref _userName, value ?? string.Empty))
            {
                RaiseCanSignInChanged();
            }
        }
    }

    /// <summary>
    ///     Gets or sets the password or access token, which the window masks.
    /// </summary>
    public string Password
    {
        get => _password;
        set
        {
            if (SetField(ref _password, value ?? string.Empty))
            {
                RaiseCanSignInChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether both a user name and a password have been entered.
    /// </summary>
    public bool CanSignIn => _userName.Trim().Length > 0 && _password.Length > 0;

    /// <summary>
    ///     Gets the command bound to the "Sign in" button; executable while
    ///     <see cref="CanSignIn"/> is <see langword="true"/>.
    /// </summary>
    public RelayCommand SignInCommand { get; }

    /// <summary>
    ///     Gets the command bound to the "Cancel" button.
    /// </summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>
    ///     Raised once when the question is answered: carrying the credential the user entered
    ///     (and whether to remember it), or <see langword="null"/> when the user cancelled.
    /// </summary>
    public event EventHandler<CredentialPromptAnswer?>? Completed;

    /// <summary>
    ///     Cancels the question as if the user pressed "Cancel" (for example when the operation
    ///     that asked was cancelled or timed out).
    /// </summary>
    public void Cancel() => Complete(null);

    /// <summary>
    ///     Answers the question with the entered credential when it is complete.
    /// </summary>
    private void SignIn()
    {
        if (!CanSignIn)
        {
            return;
        }

        Complete(new CredentialPromptAnswer(new NuGetCredential(_userName.Trim(), _password), Remember));
    }

    /// <summary>
    ///     Raises <see cref="Completed"/> once, however many times it is called.
    /// </summary>
    /// <param name="answer">The answer.</param>
    private void Complete(CredentialPromptAnswer? answer)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        Completed?.Invoke(this, answer);
    }

    /// <summary>
    ///     Raises the change notifications that follow an edit of the user name or password.
    /// </summary>
    private void RaiseCanSignInChanged()
    {
        OnPropertyChanged(nameof(CanSignIn));
        SignInCommand.RaiseCanExecuteChanged();
    }
}
