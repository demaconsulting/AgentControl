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

using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     View model of the small prompt that asks for a repo group's name, used both to create a
///     group (and move a repo into it) and to rename one: it validates as the user types and only
///     lets a usable name through.
/// </summary>
/// <remarks>
///     The rules themselves live in <see cref="RepoGroupName"/>; this class adds the messages, the
///     "does it clash with another group" check that needs the existing groups, and the dialog's
///     accept/cancel lifecycle (<see cref="Completed"/>), so the window itself stays free of logic.
///     Not thread-safe; used on the UI thread only.
/// </remarks>
internal sealed class GroupNameViewModel : ViewModelBase
{
    /// <summary>
    ///     The names of the groups that exist now, for the "already exists" check and for adopting
    ///     an existing group's spelling.
    /// </summary>
    private readonly IReadOnlyList<string> _existingGroups;

    /// <summary>
    ///     The group being renamed, or <see langword="null"/> when a group is being created.
    /// </summary>
    private readonly string? _renaming;

    /// <summary>Backing field for <see cref="Text"/>.</summary>
    private string _text;

    /// <summary>
    ///     Whether <see cref="Completed"/> was already raised.
    /// </summary>
    private bool _isCompleted;

    /// <summary>
    ///     Initializes a new prompt.
    /// </summary>
    /// <param name="title">The window title.</param>
    /// <param name="prompt">The sentence above the text box.</param>
    /// <param name="acceptText">The text of the accept button, for example <c>Create</c>.</param>
    /// <param name="existingGroups">The names of the groups that exist now.</param>
    /// <param name="renaming">The group being renamed (its name is the initial text), or
    ///     <see langword="null"/> to create a group, in which case an existing group's name is
    ///     allowed (the repo simply joins it) while in a rename it is refused.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="title"/>,
    ///     <paramref name="prompt"/>, <paramref name="acceptText"/> or
    ///     <paramref name="existingGroups"/> is <see langword="null"/>.</exception>
    public GroupNameViewModel(
        string title, string prompt, string acceptText, IEnumerable<string?> existingGroups, string? renaming = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(acceptText);
        ArgumentNullException.ThrowIfNull(existingGroups);

        Title = title;
        Prompt = prompt;
        AcceptText = acceptText;
        _existingGroups = RepoGroupName.DistinctNames(existingGroups);
        _renaming = renaming;
        _text = renaming ?? string.Empty;

        AcceptCommand = new RelayCommand(Accept, () => CanAccept);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>
    ///     Gets the window title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    ///     Gets the sentence shown above the text box.
    /// </summary>
    public string Prompt { get; }

    /// <summary>
    ///     Gets the text of the accept button.
    /// </summary>
    public string AcceptText { get; }

    /// <summary>
    ///     Gets or sets the name as typed; changing it re-validates.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ErrorText));
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(HintText));
                OnPropertyChanged(nameof(HasHint));
                OnPropertyChanged(nameof(CanAccept));
                AcceptCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the message that says why the typed name cannot be used, or <see langword="null"/>
    ///     when it can - and while nothing has been typed yet, so the prompt does not open already
    ///     scolding.
    /// </summary>
    public string? ErrorText
    {
        get
        {
            if (_text.Length == 0)
            {
                return null;
            }

            return RepoGroupName.Validate(_text) switch
            {
                RepoGroupNameProblem.Empty => "A group name cannot be blank.",
                RepoGroupNameProblem.TooLong =>
                    $"Use at most {RepoGroupName.MaxLength} characters (this name has {RepoGroupName.Normalize(_text)!.Length}).",
                RepoGroupNameProblem.Reserved =>
                    $"\"{RepoGroupName.FavoritesName}\" and \"{RepoGroupName.UngroupedName}\" are built-in sections. Choose another name.",
                _ => FindClashingGroup() is { } clash && _renaming is not null
                    ? $"A group named \"{clash}\" already exists."
                    : null
            };
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="ErrorText"/> has a message.
    /// </summary>
    public bool HasError => ErrorText is not null;

    /// <summary>
    ///     Gets a friendly note about the typed name that is not an error: when creating a group, that
    ///     the name matches an existing group and the repo will join it; otherwise
    ///     <see langword="null"/>.
    /// </summary>
    public string? HintText =>
        _renaming is null && HasUsableName && FindClashingGroup() is { } existing
            ? $"A group named \"{existing}\" already exists. The repo will be added to it."
            : null;

    /// <summary>
    ///     Gets a value indicating whether <see cref="HintText"/> has a note.
    /// </summary>
    public bool HasHint => HintText is not null;

    /// <summary>
    ///     Gets a value indicating whether the typed name can be accepted now.
    /// </summary>
    public bool CanAccept => _text.Length > 0 && HasUsableName && ErrorText is null;

    /// <summary>
    ///     Gets the command that accepts the name; available while <see cref="CanAccept"/> is
    ///     <see langword="true"/>.
    /// </summary>
    public RelayCommand AcceptCommand { get; }

    /// <summary>
    ///     Gets the command that closes the prompt without a name.
    /// </summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>
    ///     Raised once when the prompt ends: with the chosen name (an existing group's own spelling
    ///     when the typed name matches one ignoring letter case) or <see langword="null"/> when
    ///     canceled.
    /// </summary>
    public event EventHandler<string?>? Completed;

    /// <summary>
    ///     Gets a value indicating whether the typed text is a valid name by the shared rules (before
    ///     the existing-group check).
    /// </summary>
    private bool HasUsableName => RepoGroupName.Validate(_text) == RepoGroupNameProblem.None;

    /// <summary>
    ///     Finds the existing group the typed name matches ignoring letter case, other than the
    ///     group being renamed itself.
    /// </summary>
    /// <returns>The existing group's name, or <see langword="null"/> for none.</returns>
    private string? FindClashingGroup()
    {
        var typed = RepoGroupName.Normalize(_text);
        if (typed is null)
        {
            return null;
        }

        return _existingGroups.FirstOrDefault(name =>
            RepoGroupName.Comparer.Equals(name, typed) &&
            !(_renaming is not null && RepoGroupName.Comparer.Equals(name, _renaming)));
    }

    /// <summary>
    ///     Ends the prompt with the typed name, if it can be accepted.
    /// </summary>
    private void Accept()
    {
        if (!CanAccept)
        {
            return;
        }

        // A rename keeps exactly what was typed (so changing only the letter case works); a new
        // group adopts the spelling of an existing group the name matches
        var typed = RepoGroupName.Normalize(_text)!;
        Complete(_renaming is null ? RepoGroupName.Canonicalize(typed, _existingGroups) : typed);
    }

    /// <summary>
    ///     Ends the prompt without a name. Safe to call more than once and after the prompt ended
    ///     (the window closes by any route through here, and listeners must not be told twice).
    /// </summary>
    public void Cancel() => Complete(null);

    /// <summary>
    ///     Raises <see cref="Completed"/> the first time only.
    /// </summary>
    /// <param name="result">The chosen name, or <see langword="null"/> when canceled.</param>
    private void Complete(string? result)
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        Completed?.Invoke(this, result);
    }
}
