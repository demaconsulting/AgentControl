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
///     section's name, how many repos match in it, and (for the user's own groups) the group's
///     color and the actions that add a repo to, move, color, rename or remove the group.
/// </summary>
/// <remarks>
///     Headers sit among the repo cards in <see cref="MainWindowViewModel.DisplayedItems"/>. The
///     owning <see cref="MainWindowViewModel"/> keeps one instance per section and updates its
///     <see cref="Count"/>, collapsed state, color (<see cref="UpdateColor"/>) and move availability
///     (<see cref="UpdateOrder"/>) in place (so a header keeps keyboard focus when it is toggled), and
///     handles the request events; the actions that need a dialog are forwarded to the window. Not
///     thread-safe; used on the UI thread only.
/// </remarks>
internal sealed class RepoGroupHeaderViewModel : ViewModelBase
{
    /// <summary>Backing field for <see cref="Count"/>.</summary>
    private int _count;

    /// <summary>Backing field for <see cref="DirectCount"/>.</summary>
    private int _directCount;

    /// <summary>Backing field for <see cref="Depth"/>.</summary>
    private int _depth;

    /// <summary>Backing field for <see cref="ParentName"/>.</summary>
    private string? _parentName;

    /// <summary>Backing field for <see cref="HasContents"/>.</summary>
    private bool _hasContents;

    /// <summary>Backing field for <see cref="IsCollapsed"/>.</summary>
    private bool _isCollapsed;

    /// <summary>Backing field for <see cref="IsToggleEnabled"/>.</summary>
    private bool _isToggleEnabled = true;

    /// <summary>Backing field for <see cref="ColorName"/>.</summary>
    private string? _colorName;

    /// <summary>Why "Move up" is unavailable now, or <see langword="null"/> when it is available.</summary>
    private string? _moveUpDisabledReason = NotMovableReason;

    /// <summary>Why "Move down" is unavailable now, or <see langword="null"/> when it is available.</summary>
    private string? _moveDownDisabledReason = NotMovableReason;

    /// <summary>Why "Sort by name" is unavailable now, or <see langword="null"/> when it is available.</summary>
    private string? _sortDisabledReason = NotSortableReason;

    /// <summary>
    ///     The explanation of a disabled Sort by name item before the owning view model has said where
    ///     the group stands (a header that is not in the list yet has nothing to sort).
    /// </summary>
    private const string NotSortableReason = "This section cannot be sorted.";

    /// <summary>
    ///     The explanation of a disabled Move item before the owning view model has said where the
    ///     group stands (a header that is not in the list yet cannot be moved).
    /// </summary>
    private const string NotMovableReason = "This section cannot be moved.";

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
        NewSubgroupCommand = new RelayCommand(() => NewSubgroupRequested?.Invoke(this, EventArgs.Empty));
        RemoveGroupCommand = new RelayCommand(() => RemoveGroupRequested?.Invoke(this, EventArgs.Empty));
        RemoveGroupWithContentsCommand = new RelayCommand(
            () => RemoveGroupWithContentsRequested?.Invoke(this, EventArgs.Empty), () => _hasContents);
        MoveUpCommand = new RelayCommand(
            () => MoveRequested?.Invoke(this, RepoGroupMove.Up), () => _moveUpDisabledReason is null);
        MoveDownCommand = new RelayCommand(
            () => MoveRequested?.Invoke(this, RepoGroupMove.Down), () => _moveDownDisabledReason is null);
        SortByNameCommand = new RelayCommand(
            () => SortByNameRequested?.Invoke(this, EventArgs.Empty), () => _sortDisabledReason is null);
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
    ///     when there is no filter), whether or not the section is collapsed. For a group this
    ///     includes the repos in the groups inside it, however deeply.
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
    ///     Gets the number of repos listed directly under this section (those in a group inside it are
    ///     not counted); equal to <see cref="Count"/> for a group with no group inside it and for the
    ///     built-in sections.
    /// </summary>
    public int DirectCount
    {
        get => _directCount;
        private set
        {
            if (SetField(ref _directCount, value))
            {
                OnPropertyChanged(nameof(CountTooltip));
            }
        }
    }

    /// <summary>
    ///     Gets how many groups this section is inside: 0 for a top-level group and for the built-in
    ///     sections.
    /// </summary>
    public int Depth
    {
        get => _depth;
        private set
        {
            if (SetField(ref _depth, value))
            {
                OnPropertyChanged(nameof(IndentWidth));
            }
        }
    }

    /// <summary>
    ///     Gets the name of the group this group is inside, or <see langword="null"/> for a top-level
    ///     group and for the built-in sections.
    /// </summary>
    public string? ParentName
    {
        get => _parentName;
        private set
        {
            if (SetField(ref _parentName, value))
            {
                OnPropertyChanged(nameof(HasParent));
                OnPropertyChanged(nameof(AccessibleName));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether this group is inside another group.
    /// </summary>
    public bool HasParent => _parentName is not null;

    /// <summary>
    ///     Gets how far the header is indented from the left edge of the list, in device-independent
    ///     pixels: <see cref="IndentPerLevel"/> for each level the group is inside, up to
    ///     <see cref="MaxIndentedDepth"/> levels (a deeper group is shown as deep as that, so the
    ///     header can never be pushed out of view; its depth in the data is not limited).
    /// </summary>
    public double IndentWidth => GetIndentWidth(Depth);

    /// <summary>
    ///     The indent for each level a group is inside, in device-independent pixels.
    /// </summary>
    public const double IndentPerLevel = 20;

    /// <summary>
    ///     The deepest level that is indented further; deeper groups are drawn at this level.
    /// </summary>
    public const int MaxIndentedDepth = 8;

    /// <summary>
    ///     Gets the indent for a depth, shared by the headers and the repo cards under them so a group
    ///     and its repos line up.
    /// </summary>
    /// <param name="depth">How many groups the row is inside; a negative depth counts as 0.</param>
    /// <returns>The indent in device-independent pixels.</returns>
    public static double GetIndentWidth(int depth) => Math.Clamp(depth, 0, MaxIndentedDepth) * IndentPerLevel;

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
    ///     whether it is collapsed, so a screen reader (and a UI test) needs no other context. A group
    ///     that is inside another group says which one, so nested groups are told apart.
    /// </summary>
    public string AccessibleName =>
        $"{Name}, {Count} {(Count == 1 ? "repo" : "repos")}, {(IsCollapsed ? "collapsed" : "expanded")}" +
        (_parentName is null ? string.Empty : $", inside {_parentName}");

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
    ///     Gets the tooltip of the repo count; for a group with a group inside it, it also says how many
    ///     of the repos are directly in the group.
    /// </summary>
    public string CountTooltip =>
        (Count == 1 ? "1 repo" : $"{Count} repos") +
        (DirectCount == Count ? string.Empty : $" ({DirectCount} directly in this group)");

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
    ///     Gets the command that asks to create a new group inside this group; it raises
    ///     <see cref="NewSubgroupRequested"/>. Offered for the user's own groups only.
    /// </summary>
    public RelayCommand NewSubgroupCommand { get; }

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
    ///     Gets the command that asks to delete the group together with its repos and the groups inside
    ///     it; it raises <see cref="RemoveGroupWithContentsRequested"/>. Offered for the user's own groups
    ///     that have contents only (see <see cref="HasContents"/>).
    /// </summary>
    public RelayCommand RemoveGroupWithContentsCommand { get; }

    /// <summary>
    ///     Gets a value indicating whether the group has a repo (a favorite too) or a group inside it, so
    ///     that deleting it has contents to keep or to delete as well; the menu then offers both ways of
    ///     deleting, otherwise one plain delete. Always <see langword="false"/> for Favorites and
    ///     Ungrouped.
    /// </summary>
    public bool HasContents
    {
        get => _hasContents;
        private set
        {
            if (SetField(ref _hasContents, value))
            {
                OnPropertyChanged(nameof(RemoveGroupText));
                OnPropertyChanged(nameof(RemoveGroupTooltip));
                RemoveGroupWithContentsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    ///     Gets the text of the delete item that keeps what is in the group: <c>Delete group, keep
    ///     contents</c>, or just <c>Delete group</c> for a group with nothing in it.
    /// </summary>
    public string RemoveGroupText => _hasContents ? "Delete group, keep contents" : "Delete group";

    /// <summary>
    ///     Gets the tooltip of the delete item that keeps what is in the group.
    /// </summary>
    public string RemoveGroupTooltip => _hasContents
        ? "Delete this group but keep its repos and subgroups: they stay in the list, in the group this one is inside (or ungrouped)"
        : "Delete this group; it has no repos or subgroups";

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
    ///     Raised when the user asks to create a group inside this group; the window shows the name
    ///     prompt.
    /// </summary>
    public event EventHandler? NewSubgroupRequested;

    /// <summary>
    ///     Raised when the user asks to rename the group; the window shows the name prompt.
    /// </summary>
    public event EventHandler? RenameRequested;

    /// <summary>
    ///     Raised when the user asks to remove the group; the window confirms first.
    /// </summary>
    public event EventHandler? RemoveGroupRequested;

    /// <summary>
    ///     Raised when the user asks to delete the group together with its contents; the window confirms
    ///     first.
    /// </summary>
    public event EventHandler? RemoveGroupWithContentsRequested;

    /// <summary>
    ///     Brings what the header knows about the group's contents up to date.
    /// </summary>
    /// <param name="hasContents">Whether the group has a repo (a favorite too) or a group inside it.
    ///     Ignored (no contents) for Favorites and Ungrouped.</param>
    public void UpdateContents(bool hasContents) => HasContents = IsUserGroup && hasContents;

    /// <summary>
    ///     Gets the command that asks to move the group one place up; it raises
    ///     <see cref="MoveRequested"/> with <see cref="RepoGroupMove.Up"/>. Available only when
    ///     <see cref="UpdateOrder"/> said so (never for Favorites or Ungrouped).
    /// </summary>
    public RelayCommand MoveUpCommand { get; }

    /// <summary>
    ///     Gets the command that asks to move the group one place down; it raises
    ///     <see cref="MoveRequested"/> with <see cref="RepoGroupMove.Down"/>. Available only when
    ///     <see cref="UpdateOrder"/> said so (never for Favorites or Ungrouped).
    /// </summary>
    public RelayCommand MoveDownCommand { get; }

    /// <summary>
    ///     Gets the tooltip of the "Move up" item: what it does, or why it is unavailable now.
    /// </summary>
    public string MoveUpTooltip => _moveUpDisabledReason ?? "Move this group one place up in the list";

    /// <summary>
    ///     Gets the tooltip of the "Move down" item: what it does, or why it is unavailable now.
    /// </summary>
    public string MoveDownTooltip => _moveDownDisabledReason ?? "Move this group one place down in the list";

    /// <summary>
    ///     Raised when the user asks to move the group up or down; the owning view model moves it.
    /// </summary>
    public event EventHandler<RepoGroupMove>? MoveRequested;

    /// <summary>
    ///     Gets the command that asks to sort the repos listed directly under the group by name; it
    ///     raises <see cref="SortByNameRequested"/>. Available only when <see cref="UpdateSort"/> said so
    ///     (never for Favorites or Ungrouped).
    /// </summary>
    public RelayCommand SortByNameCommand { get; }

    /// <summary>
    ///     Gets the tooltip of the "Sort by name" item: what it does, or why it is unavailable now.
    /// </summary>
    public string SortByNameTooltip =>
        _sortDisabledReason ?? "Sort the repos in this group alphabetically by name (the repos in its subgroups are left as they are)";

    /// <summary>
    ///     Raised when the user asks to sort the group's repos by name; the owning view model sorts them.
    /// </summary>
    public event EventHandler? SortByNameRequested;

    /// <summary>
    ///     Brings the availability of "Sort by name" up to date.
    /// </summary>
    /// <param name="sortDisabledReason">Why the group's repos cannot be sorted now (shown as the item's
    ///     tooltip), or <see langword="null"/> when they can. Ignored for Favorites and Ungrouped,
    ///     which are never sorted.</param>
    public void UpdateSort(string? sortDisabledReason)
    {
        var reason = IsUserGroup ? sortDisabledReason : NotSortableReason;
        if (!string.Equals(reason, _sortDisabledReason, StringComparison.Ordinal))
        {
            _sortDisabledReason = reason;
            OnPropertyChanged(nameof(SortByNameTooltip));
            SortByNameCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    ///     Gets the name of the palette color the user chose for this group, or
    ///     <see langword="null"/> for none (always none for Favorites and Ungrouped).
    /// </summary>
    public string? ColorName
    {
        get => _colorName;
        private set
        {
            if (SetField(ref _colorName, value))
            {
                OnPropertyChanged(nameof(HasColor));
                OnPropertyChanged(nameof(ColorStyleClass));
                OnPropertyChanged(nameof(ColorHeading));
                OnPropertyChanged(nameof(ColorDescription));
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the group has a color, which shows the accent bar on its
    ///     header.
    /// </summary>
    public bool HasColor => _colorName is not null;

    /// <summary>
    ///     Gets the style class (<c>groupAccent0</c>, <c>groupAccent1</c>, ...) that selects the
    ///     accent bar's theme-aware brush, or an empty string when the group has no color, so no color
    ///     value lives in the view model or the template.
    /// </summary>
    public string ColorStyleClass =>
        CardColorPalette.Find(_colorName) is { } color ? $"groupAccent{color.Index}" : string.Empty;

    /// <summary>
    ///     Gets the group palette's heading: <c>Group color: Teal</c>, or <c>Group color: None</c>, so
    ///     the chosen color is named in words in the palette itself.
    /// </summary>
    public string ColorHeading => $"Group color: {_colorName ?? "None"}";

    /// <summary>
    ///     Gets the accent bar's tooltip and accessible name, <c>Group color: Teal</c>, so the color is
    ///     never conveyed by color alone.
    /// </summary>
    public string ColorDescription => $"Group color: {_colorName ?? "None"}";

    /// <summary>
    ///     Brings the header's color up to date.
    /// </summary>
    /// <param name="colorName">The group's palette color name (letter case ignored), or
    ///     <see langword="null"/> for none. Ignored (no color) for Favorites and Ungrouped, which
    ///     cannot be colored, and for a name the palette does not know.</param>
    public void UpdateColor(string? colorName) =>
        ColorName = IsUserGroup ? CardColorPalette.Find(colorName)?.Name : null;

    /// <summary>
    ///     Brings the availability of "Move up" and "Move down" up to date.
    /// </summary>
    /// <param name="moveUpDisabledReason">Why the group cannot move up now (shown as the item's
    ///     tooltip), or <see langword="null"/> when it can. Ignored for Favorites and Ungrouped,
    ///     which never move.</param>
    /// <param name="moveDownDisabledReason">Why the group cannot move down now, or
    ///     <see langword="null"/> when it can. Ignored for Favorites and Ungrouped.</param>
    public void UpdateOrder(string? moveUpDisabledReason, string? moveDownDisabledReason)
    {
        var up = IsUserGroup ? moveUpDisabledReason : NotMovableReason;
        var down = IsUserGroup ? moveDownDisabledReason : NotMovableReason;
        if (!string.Equals(up, _moveUpDisabledReason, StringComparison.Ordinal))
        {
            _moveUpDisabledReason = up;
            OnPropertyChanged(nameof(MoveUpTooltip));
            MoveUpCommand.RaiseCanExecuteChanged();
        }

        if (!string.Equals(down, _moveDownDisabledReason, StringComparison.Ordinal))
        {
            _moveDownDisabledReason = down;
            OnPropertyChanged(nameof(MoveDownTooltip));
            MoveDownCommand.RaiseCanExecuteChanged();
        }
    }

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
    /// <param name="count">The number of repos in the section that match the filter (for a group, in
    ///     the group and all the groups inside it).</param>
    /// <param name="isCollapsed">Whether the section's cards are hidden.</param>
    /// <param name="isToggleEnabled">Whether the user can collapse or expand the section now.</param>
    /// <param name="directCount">The number of those repos listed directly under the section, or
    ///     <see langword="null"/> when that is all of them.</param>
    public void Update(int count, bool isCollapsed, bool isToggleEnabled, int? directCount = null)
    {
        Count = count;
        DirectCount = directCount ?? count;
        IsCollapsed = isCollapsed;
        IsToggleEnabled = isToggleEnabled;
    }

    /// <summary>
    ///     Brings the header's place in the group tree up to date.
    /// </summary>
    /// <param name="depth">How many groups the section is inside; ignored (0) for Favorites and
    ///     Ungrouped, which are never inside a group.</param>
    /// <param name="parentName">The group it is inside, or <see langword="null"/> for a top-level
    ///     group; ignored for Favorites and Ungrouped.</param>
    public void UpdatePlace(int depth, string? parentName)
    {
        Depth = IsUserGroup ? Math.Max(depth, 0) : 0;
        ParentName = IsUserGroup ? parentName : null;
    }
}
