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

using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     One entry of the "Clone repository" dialog's group box: a group the cloned repo can be put in,
///     or "Ungrouped".
/// </summary>
/// <param name="GroupName">The group's name as stored, or <see langword="null"/> for no group
///     (the repo is added ungrouped).</param>
/// <param name="DisplayName">The text shown: <c>Ungrouped</c>, or the group's path through the tree
///     such as <c>Work &gt; Backend</c>, so nested groups of the same name are told apart.</param>
internal sealed record CloneGroupChoice(string? GroupName, string DisplayName)
{
    /// <summary>
    ///     Gets the choice of no group, which the dialog selects by default.
    /// </summary>
    public static CloneGroupChoice Ungrouped { get; } = new(null, RepoGroupName.UngroupedName);

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>
///     What the user asked for in the "Clone repository" dialog.
/// </summary>
/// <param name="Url">The repository address, trimmed, already accepted by
///     <see cref="GitClient.CheckCloneUrl"/>.</param>
/// <param name="DestinationPath">The absolute path of the folder to clone into: the parent folder and
///     the folder name combined, checked at the time of the request to be unused or an empty folder.</param>
/// <param name="GroupName">The group to put the new card in, or <see langword="null"/> for none.</param>
internal sealed record CloneRepositoryRequest(string Url, string DestinationPath, string? GroupName);

/// <summary>
///     View model of the "Clone repository" dialog: collects the repository address, the folder to
///     clone into (a parent folder and a folder name that follows the address) and the group the new
///     card goes in, checks them as the user types, and completes with a
///     <see cref="CloneRepositoryRequest"/>.
/// </summary>
/// <remarks>
///     <para>
///     The rules that need no disk or git (the address is present and cannot be read as an option or a
///     remote helper, the folder name is one valid Windows file name, the parent folder is an absolute
///     path) are checked on every change and shown as text, and keep the Clone button disabled. The
///     rule that needs the disk - the destination must not exist or must be an empty folder, and its
///     parent must exist - is checked when the user presses Clone, <em>off the UI thread</em> through an
///     <see cref="IBackgroundWorkRunner"/>, so a slow or disconnected drive never freezes the dialog; a
///     problem is shown as text and the dialog stays open, and only a usable destination ends it.
///     </para>
///     <para>
///     The folder name follows the address (<see cref="DeriveFolderName"/>) until the user edits it
///     themselves. The dialog never clones: it only chooses, and the main window's view model clones in
///     the background with progress and Cancel. The address is never stored and is not logged here (it
///     may carry credentials). Like every view model here it is created and used on the UI thread only;
///     it is not thread-safe.
///     </para>
/// </remarks>
internal sealed class CloneRepositoryViewModel : ViewModelBase
{
    /// <summary>
    ///     The names Windows reserves for devices, which no file or folder may have (with or without an
    ///     extension).
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    ///     The characters no Windows file name may contain: <c>\ / : * ? " &lt; &gt; |</c> and every
    ///     control character. A fixed set, because <see cref="Path.GetInvalidFileNameChars"/> answers
    ///     for the host operating system and is far smaller on Linux and macOS.
    /// </summary>
    private static readonly char[] InvalidNameCharacters =
        [.. "\\/:*?\"<>|", .. Enumerable.Range(0, 32).Select(code => (char)code)];

    /// <summary>
    ///     Runs the destination check off the UI thread.
    /// </summary>
    private readonly IBackgroundWorkRunner _backgroundWork;

    /// <summary>Backing field for <see cref="RepositoryUrl"/>.</summary>
    private string _repositoryUrl = string.Empty;

    /// <summary>Backing field for <see cref="ParentFolder"/>.</summary>
    private string _parentFolder;

    /// <summary>Backing field for <see cref="FolderName"/>.</summary>
    private string _folderName = string.Empty;

    /// <summary>Backing field for <see cref="SelectedGroup"/>.</summary>
    private CloneGroupChoice _selectedGroup;

    /// <summary>Backing field for <see cref="IsChecking"/>.</summary>
    private bool _isChecking;

    /// <summary>
    ///     The reason the background check refused the destination, or <see langword="null"/>; cleared
    ///     by any change of the address or the destination.
    /// </summary>
    private string? _destinationProblem;

    /// <summary>
    ///     Whether the user typed in the folder name box, after which it no longer follows the address.
    /// </summary>
    private bool _folderNameEdited;

    /// <summary>
    ///     Whether <see cref="Completed"/> was already raised.
    /// </summary>
    private bool _isCompleted;

    /// <summary>
    ///     Initializes the dialog's view model.
    /// </summary>
    /// <param name="groups">The groups the repo can be put in, "Ungrouped" first (see
    ///     <see cref="MainWindowViewModel.GetGroupChoices"/>); when empty or without
    ///     <see cref="CloneGroupChoice.Ungrouped"/> that choice is added in front.</param>
    /// <param name="initialParentFolder">The parent folder to start with, or <see langword="null"/> for
    ///     none (the user then has to choose one).</param>
    /// <param name="backgroundWork">Runs the destination check off the UI thread, or
    ///     <see langword="null"/> for the thread pool.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="groups"/> is
    ///     <see langword="null"/>.</exception>
    public CloneRepositoryViewModel(
        IReadOnlyList<CloneGroupChoice> groups,
        string? initialParentFolder = null,
        IBackgroundWorkRunner? backgroundWork = null)
    {
        ArgumentNullException.ThrowIfNull(groups);

        Groups = groups.Any(choice => choice.GroupName is null) ? groups : [CloneGroupChoice.Ungrouped, .. groups];
        _selectedGroup = Groups.First(choice => choice.GroupName is null);
        _parentFolder = initialParentFolder ?? string.Empty;
        _backgroundWork = backgroundWork ?? ThreadPoolBackgroundWorkRunner.Instance;
        AcceptCommand = new AsyncRelayCommand(AcceptAsync, ReportUnexpectedFailure, () => CanAccept);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>
    ///     Gets the window title.
    /// </summary>
    public string Title { get; } = "Clone repository";

    /// <summary>
    ///     Gets or sets the address of the repository to clone: a URL, an scp-like address or a local
    ///     path. Changing it makes the folder name follow it, unless the user chose a name.
    /// </summary>
    public string RepositoryUrl
    {
        get => _repositoryUrl;
        set
        {
            if (!SetField(ref _repositoryUrl, value ?? string.Empty))
            {
                return;
            }

            if (!_folderNameEdited)
            {
                SetField(ref _folderName, DeriveFolderName(_repositoryUrl), nameof(FolderName));
            }

            InputChanged();
        }
    }

    /// <summary>
    ///     Gets or sets the folder that will contain the clone; typed or chosen with Browse.
    /// </summary>
    public string ParentFolder
    {
        get => _parentFolder;
        set
        {
            if (SetField(ref _parentFolder, value ?? string.Empty))
            {
                InputChanged();
            }
        }
    }

    /// <summary>
    ///     Gets or sets the name of the new folder inside <see cref="ParentFolder"/>. It follows
    ///     <see cref="RepositoryUrl"/> until it is set here (typing in the box), and then stays as chosen.
    /// </summary>
    public string FolderName
    {
        get => _folderName;
        set
        {
            if (SetField(ref _folderName, value ?? string.Empty))
            {
                _folderNameEdited = true;
                InputChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the choices of the group box: Ungrouped first, then every group in the order the list
    ///     shows them.
    /// </summary>
    public IReadOnlyList<CloneGroupChoice> Groups { get; }

    /// <summary>
    ///     Gets or sets the group the cloned repo is put in; never <see langword="null"/>, and
    ///     Ungrouped by default.
    /// </summary>
    public CloneGroupChoice SelectedGroup
    {
        get => _selectedGroup;
        set => SetField(ref _selectedGroup, value ?? CloneGroupChoice.Ungrouped);
    }

    /// <summary>
    ///     Gets where the clone will be made: <see cref="FolderName"/> inside
    ///     <see cref="ParentFolder"/>; empty while either is blank. Only combines the two texts - the
    ///     disk is not read.
    /// </summary>
    public string DestinationPath
    {
        get
        {
            var parent = _parentFolder.Trim();
            var name = _folderName.Trim();
            if (parent.Length == 0 || name.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Path.Combine(parent, name);
            }
            catch (ArgumentException)
            {
                // A path with characters no path may have; the rules report it
                return string.Empty;
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the destination is being checked on disk, so the dialog
    ///     shows its progress bar and cannot be accepted twice.
    /// </summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetField(ref _isChecking, value))
            {
                RaiseCanAcceptChanged();
            }
        }
    }

    /// <summary>
    ///     Gets what is wrong with the input, in words, or <see langword="null"/> when nothing is: the
    ///     disk check's finding when it refused the destination, otherwise the first of the address,
    ///     folder name and parent folder rules that fails. A field that is still empty is not an error
    ///     (the Clone button just stays disabled).
    /// </summary>
    public string? ErrorText => _destinationProblem ?? FindInputProblem();

    /// <summary>
    ///     Gets a value indicating whether <see cref="ErrorText"/> has a message.
    /// </summary>
    public bool HasError => ErrorText is not null;

    /// <summary>
    ///     Gets a value indicating whether every field is filled in, the rules that need no disk hold
    ///     and no check is running, so Clone can be pressed.
    /// </summary>
    public bool CanAccept =>
        !_isChecking &&
        !_isCompleted &&
        _repositoryUrl.Trim().Length > 0 &&
        _folderName.Trim().Length > 0 &&
        _parentFolder.Trim().Length > 0 &&
        FindInputProblem() is null;

    /// <summary>
    ///     Gets the command behind the Clone button: checks the destination on disk off the UI thread
    ///     and, when it can be used, ends the dialog with the request; otherwise shows why not.
    /// </summary>
    public AsyncRelayCommand AcceptCommand { get; }

    /// <summary>
    ///     Gets the command that closes the dialog without a request.
    /// </summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>
    ///     Raised once when the dialog ends: with what to clone, or <see langword="null"/> when
    ///     canceled.
    /// </summary>
    public event EventHandler<CloneRepositoryRequest?>? Completed;

    /// <summary>
    ///     Derives the folder name a repository address suggests: its last segment without a
    ///     <c>.git</c> ending, as <c>git clone</c> itself would name the folder.
    /// </summary>
    /// <remarks>
    ///     Works for URLs (<c>https://host/owner/repo.git</c>), scp-like addresses
    ///     (<c>git@host:owner/repo.git</c>, <c>git@host:repo</c>) and local paths
    ///     (<c>C:\repos\repo.git</c>), ignoring trailing separators, a query or a fragment. Pure.
    /// </remarks>
    /// <param name="url">The address as typed; may be <see langword="null"/>.</param>
    /// <returns>The suggested name; empty when the address names none.</returns>
    public static string DeriveFolderName(string? url)
    {
        var text = (url ?? string.Empty).Trim();
        var cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        text = text.TrimEnd('/', '\\');
        const string GitSuffix = ".git";
        if (text.EndsWith(GitSuffix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^GitSuffix.Length].TrimEnd('/', '\\');
        }

        var start = text.LastIndexOfAny(['/', '\\', ':']) + 1;
        return text[start..].Trim();
    }

    /// <summary>
    ///     Tells why a text cannot be the name of the folder to clone into, or that it can.
    /// </summary>
    /// <remarks>
    ///     The folder name must be one valid Windows file name: no path separators or other characters a
    ///     file name cannot have, not <c>.</c> or <c>..</c>, no space or dot at the end, and not a
    ///     reserved device name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>...), with or without an extension.
    ///     The set of forbidden characters is fixed and does not depend on the host operating system.
    ///     Pure; no disk access.
    /// </remarks>
    /// <param name="name">The folder name as typed.</param>
    /// <returns>The reason, or <see langword="null"/> when the name is acceptable.</returns>
    public static string? CheckFolderName(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "Enter a name for the new folder.";
        }

        if (text is "." or "..")
        {
            return "The folder name must not be '.' or '..'.";
        }

        if (text.IndexOfAny(InvalidNameCharacters) >= 0)
        {
            return "The folder name must not contain \\ / : * ? \" < > | or control characters.";
        }

        if (text.EndsWith('.') || text.EndsWith(' '))
        {
            return "The folder name must not end with a dot or a space.";
        }

        var firstDot = text.IndexOf('.', StringComparison.Ordinal);
        var baseName = firstDot < 0 ? text : text[..firstDot];
        return ReservedDeviceNames.Contains(baseName.TrimEnd())
            ? $"'{baseName}' is a reserved name on Windows and cannot be used as a folder name."
            : null;
    }

    /// <summary>
    ///     Ends the dialog without a request. Safe to call more than once and after the dialog ended (the
    ///     window closes by any route through here, and listeners must not be told twice).
    /// </summary>
    public void Cancel() => Complete(null);

    /// <summary>
    ///     Checks the destination on disk off the UI thread and ends the dialog with the request when it
    ///     can be used; the body of <see cref="AcceptCommand"/>.
    /// </summary>
    /// <param name="cancellationToken">Abandons the check when signaled.</param>
    /// <returns>A task that completes when the dialog ended or the problem is shown.</returns>
    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        if (!CanAccept)
        {
            return;
        }

        var url = _repositoryUrl.Trim();
        var destination = DestinationPath;
        var group = _selectedGroup.GroupName;

        // The disk check can be slow (network drive, sleeping disk): never on the UI thread
        IsChecking = true;
        try
        {
            var problem = await _backgroundWork.RunAsync(
                () => GitClient.CheckCloneDestination(destination), cancellationToken);
            if (_isCompleted)
            {
                return;
            }

            if (problem is not null)
            {
                SetDestinationProblem(problem);
                return;
            }
        }
        finally
        {
            IsChecking = false;
        }

        Complete(new CloneRepositoryRequest(url, destination, group));
    }

    /// <summary>
    ///     Shows an unexpected failure of the destination check as text instead of throwing out of the
    ///     command.
    /// </summary>
    /// <param name="error">What went wrong.</param>
    private void ReportUnexpectedFailure(Exception error) =>
        SetDestinationProblem($"The folder could not be checked: {SecretRedactor.Redact(error.Message)}");

    /// <summary>
    ///     Finds the first rule that needs no disk and that the input breaks.
    /// </summary>
    /// <returns>The reason in words, or <see langword="null"/> when none is broken.</returns>
    private string? FindInputProblem()
    {
        // An empty field is not an error yet: the button is disabled until it is filled in
        if (_repositoryUrl.Trim().Length > 0 && GitClient.CheckCloneUrl(_repositoryUrl) is { } urlProblem)
        {
            return urlProblem;
        }

        if (_folderName.Trim().Length > 0 && CheckFolderName(_folderName) is { } nameProblem)
        {
            return nameProblem;
        }

        var parent = _parentFolder.Trim();
        return parent.Length > 0 && !Path.IsPathFullyQualified(parent)
            ? "The folder to clone into must be an absolute path, such as C:\\Source."
            : null;
    }

    /// <summary>
    ///     Records the disk check's finding and tells the dialog.
    /// </summary>
    /// <param name="problem">The finding.</param>
    private void SetDestinationProblem(string problem)
    {
        _destinationProblem = problem;
        RaiseErrorChanged();
    }

    /// <summary>
    ///     Reacts to a change of the address, the parent folder or the folder name: the earlier disk
    ///     finding no longer applies and the derived texts are stale.
    /// </summary>
    private void InputChanged()
    {
        _destinationProblem = null;
        OnPropertyChanged(nameof(DestinationPath));
        RaiseErrorChanged();
        RaiseCanAcceptChanged();
    }

    /// <summary>
    ///     Tells the dialog that the error text changed.
    /// </summary>
    private void RaiseErrorChanged()
    {
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>
    ///     Tells the commands and the property that depend on the input and the checking state that they
    ///     changed.
    /// </summary>
    private void RaiseCanAcceptChanged()
    {
        OnPropertyChanged(nameof(CanAccept));
        AcceptCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    ///     Raises <see cref="Completed"/> the first time only.
    /// </summary>
    /// <param name="result">The request, or <see langword="null"/> when canceled.</param>
    private void Complete(CloneRepositoryRequest? result)
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        RaiseCanAcceptChanged();
        Completed?.Invoke(this, result);
    }
}
