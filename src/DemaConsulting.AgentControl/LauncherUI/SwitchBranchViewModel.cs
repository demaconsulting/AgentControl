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

namespace DemaConsulting.AgentControl.LauncherUI;

/// <summary>
///     The branch the user chose in the "Switch branch" dialog.
/// </summary>
/// <param name="Name">The branch's name as shown to the user: for a local branch its name, for a
///     remote branch its name on the remote (<c>feature/x</c>), which is also the name of the local
///     branch that will be created.</param>
/// <param name="FullName">The name git is given: the local branch's name, or for a remote branch
///     <c>&lt;remote&gt;/&lt;name&gt;</c> (<c>origin/feature/x</c>).</param>
/// <param name="IsRemote"><see langword="true"/> for a remote-tracking branch, for which a local
///     tracking branch is created when the switch is made.</param>
internal sealed record BranchChoice(string Name, string FullName, bool IsRemote);

/// <summary>
///     One row of the "Switch branch" dialog's list: a local branch or a remote-tracking branch.
/// </summary>
/// <remarks>
///     Immutable and created by <see cref="SwitchBranchViewModel"/> from what
///     <see cref="GitClient.GetBranches"/> reported; exists so the list binds to text and flags
///     instead of to git types.
/// </remarks>
internal sealed class BranchItemViewModel
{
    /// <summary>
    ///     Initializes a row.
    /// </summary>
    /// <param name="name">The name shown (see <see cref="BranchChoice.Name"/>).</param>
    /// <param name="fullName">The name git is given (see <see cref="BranchChoice.FullName"/>).</param>
    /// <param name="remote">The remote's name for a remote-tracking branch, or <see langword="null"/>
    ///     for a local branch.</param>
    /// <param name="isCurrent"><see langword="true"/> for the checked-out branch.</param>
    public BranchItemViewModel(string name, string fullName, string? remote, bool isCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        Name = name;
        FullName = fullName;
        Remote = remote;
        IsCurrent = isCurrent;
    }

    /// <summary>
    ///     Gets the name shown in the list.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the name git is given when the branch is switched to.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    ///     Gets the remote's name for a remote-tracking branch, or <see langword="null"/> for a local
    ///     branch.
    /// </summary>
    public string? Remote { get; }

    /// <summary>
    ///     Gets a value indicating whether this is a remote-tracking branch.
    /// </summary>
    public bool IsRemote => Remote is not null;

    /// <summary>
    ///     Gets a value indicating whether this is the checked-out branch.
    /// </summary>
    public bool IsCurrent { get; }

    /// <summary>
    ///     Gets the kind of branch in words: <c>Local</c> or <c>Remote</c>.
    /// </summary>
    public string Kind => IsRemote ? "Remote" : "Local";

    /// <summary>
    ///     Gets the small caption shown next to the name: <c>current</c> for the checked-out branch,
    ///     <c>remote origin</c> (the remote's name) for a remote-tracking branch, empty for any other
    ///     local branch.
    /// </summary>
    public string Caption
    {
        get
        {
            if (IsCurrent)
            {
                return "current";
            }

            return IsRemote ? $"remote {Remote}" : string.Empty;
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="Caption"/> has text.
    /// </summary>
    public bool HasCaption => Caption.Length > 0;

    /// <summary>
    ///     Gets the text screen readers announce for the row.
    /// </summary>
    public string AccessibleText => HasCaption ? $"{Name}, {Caption}" : Name;

    /// <summary>
    ///     Gets the choice this row stands for.
    /// </summary>
    /// <returns>The choice.</returns>
    public BranchChoice ToChoice() => new(Name, FullName, IsRemote);
}

/// <summary>
///     View model of the "Switch branch" dialog: lists the branches a repo already knows - local
///     first, then remote-tracking - lets the user filter them and choose one, and completes with the
///     choice.
/// </summary>
/// <remarks>
///     <para>
///     The branches are read through a function supplied by the card (so the dialog never builds a git
///     client) off the UI thread through an <see cref="IBackgroundWorkRunner"/>: <see cref="LoadAsync"/>
///     shows an indeterminate bar meanwhile and reports a failure as text instead of throwing. Nothing
///     is fetched - the list is what git already has locally, which the dialog's hint says, pointing
///     to Git Pull for newer remote branches - so the dialog opens quickly and never asks for a sign-in.
///     The checked-out branch is listed and marked but cannot be chosen.
///     </para>
///     <para>
///     The dialog only chooses; making the switch (and telling the user what git said) is the card's
///     job, so the dialog closes first and the busy card shows the progress. Like every view model
///     here it is created and used on the UI thread only; it is not thread-safe.
///     </para>
/// </remarks>
internal sealed class SwitchBranchViewModel : ViewModelBase
{
    /// <summary>
    ///     The hint under the list: why only some remote branches appear and what to do.
    /// </summary>
    internal const string Hint = "Remote branches are the ones Git already knows about. Use Git Pull to update them.";

    /// <summary>
    ///     Reads the branches (blocking; run off the UI thread).
    /// </summary>
    private readonly Func<CancellationToken, GitBranchList> _loadBranches;

    /// <summary>
    ///     Runs the read off the UI thread.
    /// </summary>
    private readonly IBackgroundWorkRunner _backgroundWork;

    /// <summary>
    ///     Every row, local first, then remote; empty until loaded.
    /// </summary>
    private IReadOnlyList<BranchItemViewModel> _allItems = [];

    /// <summary>Backing field for <see cref="FilterText"/>.</summary>
    private string _filterText = string.Empty;

    /// <summary>Backing field for <see cref="Items"/>.</summary>
    private IReadOnlyList<BranchItemViewModel> _items = [];

    /// <summary>Backing field for <see cref="SelectedItem"/>.</summary>
    private BranchItemViewModel? _selectedItem;

    /// <summary>Backing field for <see cref="IsLoading"/>.</summary>
    private bool _isLoading = true;

    /// <summary>Backing field for <see cref="ErrorText"/>.</summary>
    private string? _errorText;

    /// <summary>Backing field for <see cref="CurrentText"/>.</summary>
    private string _currentText = string.Empty;

    /// <summary>
    ///     Whether <see cref="Completed"/> was already raised.
    /// </summary>
    private bool _isCompleted;

    /// <summary>
    ///     Initializes the dialog's view model; call <see cref="LoadAsync"/> to read the branches.
    /// </summary>
    /// <param name="repoName">The repo's display name, for the title.</param>
    /// <param name="loadBranches">Reads the repo's branches; blocking, so it is run through
    ///     <paramref name="backgroundWork"/>. Must not be <see langword="null"/>.</param>
    /// <param name="backgroundWork">Runs the read off the UI thread, or <see langword="null"/> for
    ///     the thread pool.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoName"/> or
    ///     <paramref name="loadBranches"/> is <see langword="null"/>.</exception>
    public SwitchBranchViewModel(
        string repoName,
        Func<CancellationToken, GitBranchList> loadBranches,
        IBackgroundWorkRunner? backgroundWork = null)
    {
        ArgumentNullException.ThrowIfNull(repoName);
        ArgumentNullException.ThrowIfNull(loadBranches);

        Title = $"Switch branch - {repoName}";
        _loadBranches = loadBranches;
        _backgroundWork = backgroundWork ?? ThreadPoolBackgroundWorkRunner.Instance;
        SwitchCommand = new RelayCommand(Switch, () => CanSwitch);
        CancelCommand = new RelayCommand(Cancel);
    }

    /// <summary>
    ///     Gets the window title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    ///     Gets the hint shown under the list.
    /// </summary>
    public string HintText { get; } = Hint;

    /// <summary>
    ///     Gets or sets the text the list is filtered by: a branch is listed when its name (for a remote
    ///     branch including the remote's name) contains the text, ignoring letter case; blank lists
    ///     everything.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    ///     Gets the rows shown: the branches that match <see cref="FilterText"/>, local first, then
    ///     remote.
    /// </summary>
    public IReadOnlyList<BranchItemViewModel> Items
    {
        get => _items;
        private set
        {
            if (SetField(ref _items, value))
            {
                OnPropertyChanged(nameof(EmptyText));
                OnPropertyChanged(nameof(HasEmptyText));
            }
        }
    }

    /// <summary>
    ///     Gets or sets the row the user selected, or <see langword="null"/> for none.
    /// </summary>
    public BranchItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                RaiseCanSwitchChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the branches are being read, so the dialog shows its
    ///     progress bar.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                RaiseCanSwitchChanged();
                OnPropertyChanged(nameof(EmptyText));
                OnPropertyChanged(nameof(HasEmptyText));
            }
        }
    }

    /// <summary>
    ///     Gets why the branches could not be read, or <see langword="null"/> when they were (or are
    ///     still being read).
    /// </summary>
    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(EmptyText));
                OnPropertyChanged(nameof(HasEmptyText));
                RaiseCanSwitchChanged();
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="ErrorText"/> has a message.
    /// </summary>
    public bool HasError => _errorText is not null;

    /// <summary>
    ///     Gets where the repo is now: <c>Currently on main</c>, or <c>Detached HEAD at 1a2b3c4</c> when
    ///     no branch is checked out; empty until the branches are read.
    /// </summary>
    public string CurrentText
    {
        get => _currentText;
        private set => SetField(ref _currentText, value);
    }

    /// <summary>
    ///     Gets the sentence shown in place of an empty list - that the repo has no branches, or that
    ///     none matches the filter - or <see langword="null"/> while loading, on an error and when rows
    ///     are listed.
    /// </summary>
    public string? EmptyText
    {
        get
        {
            if (_isLoading || _errorText is not null || _items.Count > 0)
            {
                return null;
            }

            return _allItems.Count == 0 ? "This repository has no branches yet." : "No branch matches the filter.";
        }
    }

    /// <summary>
    ///     Gets a value indicating whether <see cref="EmptyText"/> has a sentence.
    /// </summary>
    public bool HasEmptyText => EmptyText is not null;

    /// <summary>
    ///     Gets a value indicating whether a branch other than the checked-out one is selected and the
    ///     list is ready, so the switch can be made.
    /// </summary>
    public bool CanSwitch => !_isLoading && _errorText is null && _selectedItem is { IsCurrent: false };

    /// <summary>
    ///     Gets the command that ends the dialog with the selected branch; available while
    ///     <see cref="CanSwitch"/>.
    /// </summary>
    public RelayCommand SwitchCommand { get; }

    /// <summary>
    ///     Gets the command that closes the dialog without a choice.
    /// </summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>
    ///     Raised once when the dialog ends: with the chosen branch, or <see langword="null"/> when
    ///     canceled.
    /// </summary>
    public event EventHandler<BranchChoice?>? Completed;

    /// <summary>
    ///     Reads the branches off the UI thread and lists them; a failure is shown as
    ///     <see cref="ErrorText"/> instead of being thrown.
    /// </summary>
    /// <remarks>
    ///     The read runs through the <see cref="IBackgroundWorkRunner"/>, so the UI thread stays free
    ///     (and, in the tests, the guard against blocking calls on it is never tripped) while git runs.
    ///     Every failure of the read - git cannot be started, the folder is not a repository, the read
    ///     timed out - becomes a sentence; credentials in git's text are removed first.
    /// </remarks>
    /// <param name="cancellationToken">Stops git and abandons the read when signaled (the dialog was
    ///     closed).</param>
    /// <returns>A task that completes when the list is shown or the failure is reported.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     was signaled; nothing is shown.</exception>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorText = null;
        try
        {
            var branches = await _backgroundWork.RunAsync(() => _loadBranches(cancellationToken), cancellationToken);
            ShowBranches(branches);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorText = $"The branches could not be read: {SecretRedactor.Redact(ex.Message)}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    ///     Chooses a branch at once: selects the row and ends the dialog with it, as a double-click or
    ///     Enter on the row does.
    /// </summary>
    /// <param name="item">The row to choose. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is
    ///     <see langword="null"/>.</exception>
    public void Confirm(BranchItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        SelectedItem = item;
        Switch();
    }

    /// <summary>
    ///     Ends the dialog without a choice. Safe to call more than once and after the dialog ended (the
    ///     window closes by any route through here, and listeners must not be told twice).
    /// </summary>
    public void Cancel() => Complete(null);

    /// <summary>
    ///     Lists the branches git reported and the repo's current state.
    /// </summary>
    /// <param name="branches">What git reported.</param>
    private void ShowBranches(GitBranchList branches)
    {
        _allItems =
        [
            .. branches.Local.Select(name => new BranchItemViewModel(
                name, name, null, string.Equals(name, branches.CurrentBranch, StringComparison.Ordinal))),
            .. branches.Remote.Select(branch => new BranchItemViewModel(branch.Name, branch.FullName, branch.Remote, false))
        ];
        CurrentText = DescribeWhereHeadIs(branches);
        ApplyFilter();
    }

    /// <summary>
    ///     Describes where <c>HEAD</c> is: on a branch, or detached (at a commit, when git named it).
    /// </summary>
    /// <param name="branches">What git reported.</param>
    /// <returns>The sentence, for example <c>Currently on main</c> or <c>Detached HEAD at 1a2b3c4</c>.</returns>
    private static string DescribeWhereHeadIs(GitBranchList branches)
    {
        if (branches.CurrentBranch is not null)
        {
            return $"Currently on {branches.CurrentBranch}";
        }

        return branches.DetachedAt is not null ? $"Detached HEAD at {branches.DetachedAt}" : "Detached HEAD";
    }

    /// <summary>
    ///     Applies <see cref="FilterText"/> to the rows, keeping the selection when its row is still
    ///     listed and choosing the only branch left when the user has typed a filter.
    /// </summary>
    private void ApplyFilter()
    {
        var text = _filterText.Trim();
        Items = text.Length == 0
            ? _allItems
            : [.. _allItems.Where(item => item.FullName.Contains(text, StringComparison.OrdinalIgnoreCase))];

        // A selection that is no longer listed is dropped; a filter that leaves exactly one branch to
        // switch to selects it, so typing a name and pressing Enter is enough
        if (_selectedItem is not null && !_items.Contains(_selectedItem))
        {
            SelectedItem = null;
        }

        if (_selectedItem is null && text.Length > 0)
        {
            var candidates = _items.Where(item => !item.IsCurrent).Take(2).ToList();
            if (candidates.Count == 1)
            {
                SelectedItem = candidates[0];
            }
        }
    }

    /// <summary>
    ///     Ends the dialog with the selected branch, if one can be switched to.
    /// </summary>
    private void Switch()
    {
        if (!CanSwitch)
        {
            return;
        }

        Complete(_selectedItem!.ToChoice());
    }

    /// <summary>
    ///     Tells the commands and the property that depend on the selection and the loading state that
    ///     they changed.
    /// </summary>
    private void RaiseCanSwitchChanged()
    {
        OnPropertyChanged(nameof(CanSwitch));
        SwitchCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    ///     Raises <see cref="Completed"/> the first time only.
    /// </summary>
    /// <param name="result">The chosen branch, or <see langword="null"/> when canceled.</param>
    private void Complete(BranchChoice? result)
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        Completed?.Invoke(this, result);
    }
}
