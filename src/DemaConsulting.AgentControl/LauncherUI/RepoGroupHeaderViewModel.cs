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
///     Which kind of section of the main window's repo list a header introduces.
/// </summary>
internal enum RepoGroupKind
{
    /// <summary>
    ///     The built-in section of favorite repos, always first.
    /// </summary>
    Favorites,

    /// <summary>
    ///     One of the user's own groups.
    /// </summary>
    Group,

    /// <summary>
    ///     The built-in section of repos that belong to no group, always last.
    /// </summary>
    Ungrouped
}

/// <summary>
///     The collapsible header row that starts a section of the main window's repo list: the
///     section's name, how many repos match in it, and (for the user's own groups) the actions
///     that rename or remove the group.
/// </summary>
/// <remarks>
///     Headers sit among the repo cards in <see cref="MainWindowViewModel.DisplayedItems"/>. The
///     owning <see cref="MainWindowViewModel"/> keeps one instance per section and updates its
///     <see cref="Count"/> and collapsed state in place (so a header keeps keyboard focus when it
///     is toggled), and handles the three request events; the actions that need a dialog are
///     forwarded to the window. Not thread-safe; used on the UI thread only.
/// </remarks>
internal sealed class RepoGroupHeaderViewModel : ViewModelBase
{
    /// <summary>Backing field for <see cref="Count"/>.</summary>
    private int _count;

    /// <summary>Backing field for <see cref="IsCollapsed"/>.</summary>
    private bool _isCollapsed;

    /// <summary>Backing field for <see cref="IsToggleEnabled"/>.</summary>
    private bool _isToggleEnabled = true;

    /// <summary>
    ///     Initializes a new header.
    /// </summary>
    /// <param name="kind">Which kind of section the header introduces.</param>
    /// <param name="name">The section's display name: the user's group name, or the built-in
    ///     <see cref="RepoGroupName.FavoritesName"/> / <see cref="RepoGroupName.UngroupedName"/>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    public RepoGroupHeaderViewModel(RepoGroupKind kind, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Kind = kind;
        Name = name;
        ToggleCommand = new RelayCommand(
            () => ToggleRequested?.Invoke(this, EventArgs.Empty), () => IsToggleEnabled);
        RenameCommand = new RelayCommand(() => RenameRequested?.Invoke(this, EventArgs.Empty));
        AddRepoCommand = new RelayCommand(() => AddRepoRequested?.Invoke(this, EventArgs.Empty));
        RemoveGroupCommand = new RelayCommand(() => RemoveGroupRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    ///     Gets which kind of section this header introduces.
    /// </summary>
    public RepoGroupKind Kind { get; }

    /// <summary>
    ///     Gets the section's display name; also the key under which its collapsed state is stored.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets a value indicating whether this is the built-in favorites section.
    /// </summary>
    public bool IsFavoritesSection => Kind == RepoGroupKind.Favorites;

    /// <summary>
    ///     Gets a value indicating whether this is one of the user's own groups, whose header offers
    ///     the rename and remove actions.
    /// </summary>
    public bool IsUserGroup => Kind == RepoGroupKind.Group;

    /// <summary>
    ///     Gets a value indicating whether this is the built-in section of repos in no group.
    /// </summary>
    public bool IsUngroupedSection => Kind == RepoGroupKind.Ungrouped;

    /// <summary>
    ///     Gets the number of repos in the section that match the current filter (all of its repos
    ///     when there is no filter), whether or not the section is collapsed.
    /// </summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (SetField(ref _count, value))
            {
                OnPropertyChanged(nameof(AccessibleName));
                OnPropertyChanged(nameof(CountTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the section's cards are hidden. Never
    ///     <see langword="true"/> while a filter is active: a filter shows every match.
    /// </summary>
    public bool IsCollapsed
    {
        get => _isCollapsed;
        private set
        {
            if (SetField(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(AccessibleName));
                OnPropertyChanged(nameof(ToggleTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the section's cards are shown (the opposite of
    ///     <see cref="IsCollapsed"/>), for the view's expanded-chevron binding.
    /// </summary>
    public bool IsExpanded => !_isCollapsed;

    /// <summary>
    ///     Gets a value indicating whether the user can collapse or expand the section now. False
    ///     while a filter is active, because the filter shows every match regardless.
    /// </summary>
    public bool IsToggleEnabled
    {
        get => _isToggleEnabled;
        private set
        {
            if (SetField(ref _isToggleEnabled, value))
            {
                ToggleCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ToggleTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets the header's accessible name, which says the section, how many repos it has and
    ///     whether it is collapsed, so a screen reader (and a UI test) needs no other context.
    /// </summary>
    public string AccessibleName =>
        $"{Name}, {Count} {(Count == 1 ? "repo" : "repos")}, {(IsCollapsed ? "collapsed" : "expanded")}";

    /// <summary>
    ///     Gets the tooltip of the collapse toggle.
    /// </summary>
    public string ToggleTooltip
    {
        get
        {
            if (!IsToggleEnabled)
            {
                return "Groups stay expanded while a filter is active, so no match is hidden.";
            }

            return IsCollapsed ? $"Expand {Name}" : $"Collapse {Name}";
        }
    }

    /// <summary>
    ///     Gets the tooltip of the repo count.
    /// </summary>
    public string CountTooltip => Count == 1 ? "1 repo" : $"{Count} repos";

    /// <summary>
    ///     Gets the command that collapses or expands the section; it raises
    ///     <see cref="ToggleRequested"/>.
    /// </summary>
    public RelayCommand ToggleCommand { get; }

    /// <summary>
    ///     Gets the command that asks to add a repository straight into this group; it raises
    ///     <see cref="AddRepoRequested"/>. Offered for the user's own groups only.
    /// </summary>
    public RelayCommand AddRepoCommand { get; }

    /// <summary>
    ///     Gets the command that asks to rename the group; it raises <see cref="RenameRequested"/>.
    ///     Offered for the user's own groups only.
    /// </summary>
    public RelayCommand RenameCommand { get; }

    /// <summary>
    ///     Gets the command that asks to remove the group, leaving its repos ungrouped; it raises
    ///     <see cref="RemoveGroupRequested"/>. Offered for the user's own groups only.
    /// </summary>
    public RelayCommand RemoveGroupCommand { get; }

    /// <summary>
    ///     Raised when the user asks to collapse or expand the section.
    /// </summary>
    public event EventHandler? ToggleRequested;

    /// <summary>
    ///     Raised when the user asks to add a repository to the group; the window shows the folder
    ///     picker and adds the chosen folder to this group.
    /// </summary>
    public event EventHandler? AddRepoRequested;

    /// <summary>
    ///     Raised when the user asks to rename the group; the window shows the name prompt.
    /// </summary>
    public event EventHandler? RenameRequested;

    /// <summary>
    ///     Raised when the user asks to remove the group; the window confirms first.
    /// </summary>
    public event EventHandler? RemoveGroupRequested;

    /// <summary>
    ///     Gets the text that identifies this row to assistive technology: the list item that hosts
    ///     the header takes its accessible name from the row's text, which would otherwise be the
    ///     type's name.
    /// </summary>
    /// <returns><see cref="AccessibleName"/>.</returns>
    public override string ToString() => AccessibleName;

    /// <summary>
    ///     Brings the header up to date with the current list.
    /// </summary>
    /// <param name="count">The number of repos in the section that match the filter.</param>
    /// <param name="isCollapsed">Whether the section's cards are hidden.</param>
    /// <param name="isToggleEnabled">Whether the user can collapse or expand the section now.</param>
    public void Update(int count, bool isCollapsed, bool isToggleEnabled)
    {
        Count = count;
        IsCollapsed = isCollapsed;
        IsToggleEnabled = isToggleEnabled;
    }
}
