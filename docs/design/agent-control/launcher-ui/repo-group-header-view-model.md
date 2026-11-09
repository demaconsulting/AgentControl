### RepoGroupHeaderViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`RepoGroupHeaderViewModel` is the collapsible header row that starts a section of the main window's repo list: the
section's name, how many repos match in it, and - for the user's own groups - the actions that add a repository to,
rename or remove the group. Its single responsibility is to describe one section to the user and to assistive
technology and to report what the user asked for (collapse or expand, add a repository, rename, remove); it never
changes any state itself. The
`RepoGroupKind` enumeration that accompanies it (`Favorites`, `Group`, `Ungrouped`) is part of this unit. Its
behavior is specified by the unit requirements `AgentControl-RepoGroupHeaderViewModel-SectionKind`,
`-DescribesSection`, `-AccessibleNameIsText`, `-UpdatesInPlace`, `-Toggle`, `-ToggleUnavailable`, `-ToggleExplained`,
`-GroupActions`, `-RemoveGroupRequest`, `-AddRepoRequest` and `-RequiresName` (each prefixed
`AgentControl-RepoGroupHeaderViewModel`), and
together with `MainWindowViewModel` (which decides which headers the list has) it provides
`AgentControl-LauncherUI-RepoGroups`.

#### Data Model

**Kind** (`RepoGroupKind`) and **Name**: which kind of section the header introduces and the section's display name -
the user's group name, or the built-in *Favorites* / *Ungrouped*. The name is also the key under which the owner
stores the section's collapsed state. **IsFavoritesSection**, **IsUserGroup**, **IsUngroupedSection** are derived
flags for the view, exactly one of which is true for any kind; the rename and remove actions are offered for
`IsUserGroup` only (`AgentControl-RepoGroupHeaderViewModel-SectionKind`).

**Count**: the number of repos in the section that match the current filter (all of them without a filter), whether
or not the section is collapsed. **IsCollapsed** / **IsExpanded**: whether the section's cards are hidden (never true
while a filter is active - a filter shows every match). **IsToggleEnabled**: whether the user can collapse or expand
now - false while a filter is active.

**AccessibleName**: the section, its count and its state in one sentence, for example `Work, 2 repos, collapsed`
(singular for one repo), also the result of `ToString()`, which names the hosting list item for assistive technology
(otherwise the item would be named after the type) (`AgentControl-RepoGroupHeaderViewModel-AccessibleNameIsText`).
**ToggleTooltip** says `Collapse {Name}` or `Expand {Name}`, or -
while toggling is disabled - that groups stay expanded while a filter is active so no match is hidden;
**CountTooltip** says `1 repo` or `{n} repos` (`AgentControl-RepoGroupHeaderViewModel-DescribesSection`,
`-ToggleExplained`).

**ToggleCommand**, **RenameCommand**, **RemoveGroupCommand**, **AddRepoCommand** (`RelayCommand`) and the events
**ToggleRequested**, **RenameRequested**, **RemoveGroupRequested**, **AddRepoRequested**: each command only raises its
event; `ToggleCommand` can execute only while `IsToggleEnabled` (`AgentControl-RepoGroupHeaderViewModel-Toggle`,
`-ToggleUnavailable`, `-GroupActions`, `-RemoveGroupRequest`, `-AddRepoRequest`).

#### Key Methods

**Constructor(kind, name)**: Stores the kind and name and creates the three commands. *Preconditions*: `name` is not
blank. *Postconditions*: `Count` is 0, the section is expanded and toggling is enabled. Throws `ArgumentException`
(`ArgumentNullException` for `null`) for a blank name (`AgentControl-RepoGroupHeaderViewModel-RequiresName`).

**Update(count, isCollapsed, isToggleEnabled)**: Brings the header up to date with the current list, in place, raising
change notifications only for what changed (and for the derived accessible name and tooltips). The owner keeps one
header instance per section and updates it rather than replacing it, so a header keeps the keyboard focus when it is
toggled (`AgentControl-RepoGroupHeaderViewModel-UpdatesInPlace`).

**ToString()**: Returns `AccessibleName`.

#### Error Handling

Only the constructor's argument validation throws. The commands never throw: a request raised with no listener is
simply ignored.

#### Dependencies

- **ViewModelBase**, **RelayCommand** (`LauncherUI`) - change notification and commands.
- **RepoGroupName** (`Settings` subsystem unit) - the built-in section names the owner passes as `Name`.

#### Callers

- **MainWindowViewModel** (`LauncherUI`) - creates one header per section, calls `Update` whenever the list is
  recomputed and handles the four request events.
- **MainWindow** (view) - binds each header row to it and shows the group-name prompt, the removal confirmation and the
  folder picker for the requests the view model forwards.
