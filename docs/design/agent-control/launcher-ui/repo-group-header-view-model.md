### RepoGroupHeaderViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`RepoGroupHeaderViewModel` is the collapsible header row that starts a section of the main window's repo list: the
section's name, how many repos match in it, and - for the user's own groups - the group's color and the actions that add
a repository to, move, sort, color, rename or remove the group. Its single responsibility is to describe one section to
the user and to assistive technology and to report what the user asked for (collapse or expand, add a repository, move
up or down, sort by name, rename, remove); it never changes any state itself. The `RepoGroupKind` enumeration that
accompanies it (`Favorites`, `Group`, `Ungrouped`) is part of this unit. Its behavior is specified by the unit
requirements `AgentControl-RepoGroupHeaderViewModel-SectionKind`, `-DescribesSection`, `-AccessibleNameIsText`,
`-UpdatesInPlace`, `-Toggle`, `-ToggleUnavailable`, `-ToggleExplained`, `-GroupActions`, `-RemoveGroupRequest`,
`-AddRepoRequest`, `-NewSubgroupRequest`, `-NestedPlace`, `-RequiresName`, `-ShowsGroupColor`, `-MoveRequests` and
`-SortByNameRequest` (each prefixed `AgentControl-RepoGroupHeaderViewModel`), and together with `MainWindowViewModel`
(which decides which headers the list has) it provides `AgentControl-LauncherUI-RepoGroups`,
`AgentControl-LauncherUI-GroupColor`, `AgentControl-LauncherUI-GroupMoveMenu` and
`AgentControl-LauncherUI-GroupSortByName`.

#### Data Model

**Kind** (`RepoGroupKind`) and **Name**: which kind of section the header introduces and the section's display name -
the user's group name, or the built-in *Favorites* / *Ungrouped*. The name is also the key under which the owner
stores the section's collapsed state. **IsFavoritesSection**, **IsUserGroup**, **IsUngroupedSection** are derived
flags for the view, exactly one of which is true for any kind; the rename and remove actions are offered for
`IsUserGroup` only (`AgentControl-RepoGroupHeaderViewModel-SectionKind`).

**Count**: the number of repos in the section that match the current filter (all of them without a filter), whether
or not the section is collapsed; for a group it includes the repos of the groups inside it. **DirectCount**: how many
of them are listed directly under the section (equal to `Count` when no group is inside it). **IsCollapsed** /
**IsExpanded**: whether the section's cards are hidden (never true
while a filter is active - a filter shows every match). **IsToggleEnabled**: whether the user can collapse or expand
now - false while a filter is active.

**Depth**, **ParentName**, **HasParent**, **IndentWidth**: where the group is in the tree - how many groups it is
inside, the group it is directly inside (`null` at the top level and always for *Favorites* and *Ungrouped*, which are
never inside a group) and the indent that depth gives: `IndentPerLevel` (20 device-independent pixels) for each level
up to `MaxIndentedDepth` (8) levels, so a group nested deeper is drawn no further in while its depth in the data is not
limited. The static `GetIndentWidth(depth)` is shared with `RepoCardViewModel` so a group and its repos line up
(`AgentControl-RepoGroupHeaderViewModel-NestedPlace`).

**AccessibleName**: the section, its count and its state in one sentence, for example `Work, 2 repos, collapsed`
(singular for one repo), with `, inside {parent}` appended for a nested group so nested groups can be told apart (a
top-level group's name is unchanged), also the result of `ToString()`, which names the hosting list item for assistive
technology (otherwise the item would be named after the type)
(`AgentControl-RepoGroupHeaderViewModel-AccessibleNameIsText`, `-NestedPlace`). **ToggleTooltip** says `Collapse {Name}`
or `Expand {Name}`, or - while toggling is disabled - that groups stay expanded while a filter is active so no match is
hidden; **CountTooltip** says `1 repo` or `{n} repos`, followed by `({m} directly in this group)` when `DirectCount`
differs from `Count` (`AgentControl-RepoGroupHeaderViewModel-DescribesSection`, `-ToggleExplained`, `-NestedPlace`).

**ToggleCommand**, **RenameCommand**, **RemoveGroupCommand**, **AddRepoCommand**, **NewSubgroupCommand**
(`RelayCommand`) and the events
**ToggleRequested**, **RenameRequested**, **RemoveGroupRequested**, **AddRepoRequested**, **NewSubgroupRequested**:
each command only raises its
event; `ToggleCommand` can execute only while `IsToggleEnabled` (`AgentControl-RepoGroupHeaderViewModel-Toggle`,
`-ToggleUnavailable`, `-GroupActions`, `-RemoveGroupRequest`, `-AddRepoRequest`, `-NewSubgroupRequest`).

**ColorName** (`string?`): the palette color the user chose for the group, in its canonical spelling, or `null` for
none - always `null` for *Favorites* and *Ungrouped*, which cannot be colored. **HasColor** shows the accent bar on the
header. **ColorStyleClass** is `groupAccent{index}` (the palette index of the color, for example `groupAccent5` for
Teal) or an empty string, so the bar's theme-aware brush is chosen by a style class and no color value lives in the view
model or the template. **ColorHeading** is the group palette's heading (`Group color: Teal`, or `Group color: None`)
and **ColorDescription** the bar's tooltip and accessible name (`Group color: Teal`), so the color is never conveyed by
color alone. The header's `AccessibleName` is unchanged by the color
(`AgentControl-RepoGroupHeaderViewModel-ShowsGroupColor`).

**MoveUpCommand**, **MoveDownCommand** (`RelayCommand`) and the event **MoveRequested** (`EventHandler<RepoGroupMove>`,
with `RepoGroupMove.Up` or `.Down`; the enumeration is declared beside `MainWindowViewModel`): each command only raises
the event, and can execute only while its move is available. **MoveUpTooltip** / **MoveDownTooltip** say what the item
does (`Move this group one place up in the list`, `... down ...`) or, while it is unavailable, why. A move is
unavailable until the owner has called `UpdateOrder`, and always for *Favorites* and *Ungrouped*
(`AgentControl-RepoGroupHeaderViewModel-MoveRequests`).

**SortByNameCommand** (`RelayCommand`) and the event **SortByNameRequested**: the command only raises the event and can
execute only while sorting is available. **SortByNameTooltip** says what the item does (it sorts the group's repos
alphabetically by name and leaves the repos of its subgroups as they are) or, while it is unavailable, why. Sorting is
unavailable until the owner has called `UpdateSort`, and always for *Favorites* and *Ungrouped*
(`AgentControl-RepoGroupHeaderViewModel-SortByNameRequest`).

#### Key Methods

**Constructor(kind, name)**: Stores the kind and name and creates the commands. *Preconditions*: `name` is not
blank. *Postconditions*: `Count` is 0, the section is expanded, toggling is enabled, there is no color and neither
move is available. Throws `ArgumentException`
(`ArgumentNullException` for `null`) for a blank name (`AgentControl-RepoGroupHeaderViewModel-RequiresName`).

**Update(count, isCollapsed, isToggleEnabled, directCount = null)**: Brings the header up to date with the current list,
in place, raising change notifications only for what changed (and for the derived accessible name and tooltips). The
owner keeps one header instance per section and updates it rather than replacing it, so a header keeps the keyboard
focus when it is toggled (`AgentControl-RepoGroupHeaderViewModel-UpdatesInPlace`).

**UpdatePlace(depth, parentName)**: Sets where the group is in the tree - `Depth` and `ParentName` - and raises the
notifications for `IndentWidth`, `HasParent` and the accessible name when they change; ignored (top level) for
*Favorites* and *Ungrouped* (`AgentControl-RepoGroupHeaderViewModel-NestedPlace`).

**UpdateColor(colorName)**: Sets `ColorName` to the palette color named by `colorName` (letter case ignored, through
`CardColorPalette.Find`), or to `null` for `null`, a name the palette does not know, or a *Favorites* or *Ungrouped*
header; raises change notifications for `ColorName`, `HasColor`, `ColorStyleClass`, `ColorHeading` and
`ColorDescription` only when the color changed (`AgentControl-RepoGroupHeaderViewModel-ShowsGroupColor`).

**UpdateOrder(moveUpDisabledReason, moveDownDisabledReason)**: Records why each move is unavailable now (`null` means
available); for *Favorites* and *Ungrouped* both are always unavailable whatever is passed. When a reason changes it
raises the change notification of the matching tooltip and the command's `CanExecuteChanged`
(`AgentControl-RepoGroupHeaderViewModel-MoveRequests`).

**UpdateSort(sortDisabledReason)**: Records why sorting the group's repos by name is unavailable now (`null` means
available); for *Favorites* and *Ungrouped* it is always unavailable whatever is passed. When the reason changes it
raises the change notification of `SortByNameTooltip` and the command's `CanExecuteChanged`
(`AgentControl-RepoGroupHeaderViewModel-SortByNameRequest`).

**ToString()**: Returns `AccessibleName`.

#### Error Handling

Only the constructor's argument validation throws. The commands never throw: a request raised with no listener is
simply ignored.

#### Dependencies

- **ViewModelBase**, **RelayCommand** (`LauncherUI`) - change notification and commands.
- **CardColorPalette** (`LauncherUI` supporting type) - finds a palette color by name and gives its index for the
  accent's style class.
- **RepoGroupMove** (declared with `MainWindowViewModel`) - the direction of a move request.
- **RepoGroupName** (`Settings` subsystem unit) - the built-in section names the owner passes as `Name`.

#### Callers

- **MainWindowViewModel** (`LauncherUI`) - creates one header per section, calls `Update`, `UpdateColor`,
  `UpdateOrder` and `UpdateSort` whenever the list is recomputed and handles the request events (a move request moves
  the group, a sort request sorts its repos).
- **MainWindow** (view) - binds each header row to it (the accent bar, the Move up / Move down / Sort by name items with
  their tooltips, the group palette's heading and chosen color) and shows the group-name prompt, the removal
  confirmation and the folder picker for the requests the view model forwards.
