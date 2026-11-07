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
///     network unprotected. The dialog states that the credential is used for this session only:
///     AgentControl keeps it in memory and never saves it.
///     </para>
///     <para>
///     The view model holds the typed password only until the dialog closes and raises
///     <see cref="Completed"/> exactly once, with a credential or <see langword="null"/> when the
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
    ///     The sentence that states how long the credential is kept; shown in the dialog.
    /// </summary>
    public const string SessionNoteText = "Used for this session only - AgentControl does not save it.";

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
    ///     Raised once when the question is answered: carrying the credential the user entered, or
    ///     <see langword="null"/> when the user cancelled.
    /// </summary>
    public event EventHandler<NuGetCredential?>? Completed;

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

        Complete(new NuGetCredential(_userName.Trim(), _password));
    }

    /// <summary>
    ///     Raises <see cref="Completed"/> once, however many times it is called.
    /// </summary>
    /// <param name="credential">The answer.</param>
    private void Complete(NuGetCredential? credential)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        Completed?.Invoke(this, credential);
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
