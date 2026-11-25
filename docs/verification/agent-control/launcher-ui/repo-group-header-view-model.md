### RepoGroupHeaderViewModel

#### Verification Approach

`RepoGroupHeaderViewModel` is verified with unit tests in `RepoGroupHeaderViewModelTests.cs`. Each test constructs a
real header, drives it through its constructor, `Update`, `UpdateColor`, `UpdateOrder`, `UpdateSort`, `UpdateContents`
and its commands, and reads its properties and the events it raises; nothing is mocked and no Avalonia `Window` is
constructed. How the owning `MainWindowViewModel` creates, updates and reacts to headers is verified by that unit, and
how the window shows them by the headless window tests of the `LauncherUI` subsystem (`RepoGroupWindowTests.cs`).

#### Test Environment

N/A - standard test environment (no window, no files and no processes are involved).

#### Acceptance Criteria

- Each kind of header sets exactly its own section flag and leaves the other two off (a full truth table over the
  three kinds); only a user group is a user group.
- The accessible name says the section, its repo count (singular for one) and whether it is collapsed or expanded,
  is also the header's text form, and the tooltips and change notifications follow `Update`, on the same header
  instance.
- The toggle command can execute only while toggling is enabled and raises its request when executed; while it
  cannot, no request is raised and its tooltip explains the filter.
- The rename and remove commands raise exactly one request per execution and change nothing themselves.
- The add-repository command raises exactly one request per execution and changes nothing itself; so does the
  new-subgroup command.
- A nested group exposes its depth, its parent and the indent that depth gives (capped), names its parent in its
  accessible name and, when its count includes groups inside it, says how many repos are directly in it; the built-in
  sections stay top level.
- A user group's color is exposed by name (canonical spelling), as the accent's style class, as the palette heading
  and as the accent's description, with change notifications; an unknown color or none leaves no color, and
  *Favorites* and *Ungrouped* never show one.
- The Move up and Move down commands are unavailable until the owner says where the group stands, raise their request
  only while available, explain an unavailable move in their tooltips, and are never available for *Favorites* and
  *Ungrouped*.
- The Sort by name command is unavailable until the owner says the group can be sorted, raises its request only while
  available, explains an unavailable sort in its tooltip, and is never available for *Favorites* and *Ungrouped*.
- The delete-with-contents command is available only for a group that has contents (a repo or a group inside it), is
  hidden otherwise with the plain delete reading `Delete group`, raises exactly one request per execution, and is
  never available for *Favorites* and *Ungrouped*.
- A header without a name (blank or `null`) is refused.

#### Test Scenarios

**RepoGroupHeaderViewModel_SectionKind**: A header of each of the three kinds is created and all three section flags
are read, so exactly the matching flag is set for each kind. This scenario is tested by the theory
`RepoGroupHeaderViewModel_Kind_SelectsExactlyTheMatchingSectionFlag`, covering
`AgentControl-RepoGroupHeaderViewModel-SectionKind`.

**RepoGroupHeaderViewModel_DescribesSection**: A header is updated to one repo expanded and then to three repos
collapsed; the accessible name, the toggle and count tooltips and the expanded flag are checked. This scenario is
tested by `RepoGroupHeaderViewModel_Update_DescribesCountAndState`, covering
`AgentControl-RepoGroupHeaderViewModel-DescribesSection`.

**RepoGroupHeaderViewModel_AccessibleNameIsText**: The text form of a favorites header that was updated to two repos
collapsed is its accessible name. This scenario is tested by `RepoGroupHeaderViewModel_ToString_IsTheAccessibleName`,
covering `AgentControl-RepoGroupHeaderViewModel-AccessibleNameIsText`.

**RepoGroupHeaderViewModel_UpdatesInPlace**: The same header instance is updated twice, to one repo expanded and to
three repos collapsed, and raises the change notification of its accessible name. This scenario is tested by
`RepoGroupHeaderViewModel_Update_DescribesCountAndState`, covering
`AgentControl-RepoGroupHeaderViewModel-UpdatesInPlace`.

**RepoGroupHeaderViewModel_Toggle**: With toggling enabled the toggle command can execute and raises one request.
This scenario is tested by `RepoGroupHeaderViewModel_ToggleCommand_FollowsToggleEnabled`, covering
`AgentControl-RepoGroupHeaderViewModel-Toggle`.

**RepoGroupHeaderViewModel_ToggleUnavailable**: With toggling disabled (a filter is active) the toggle command
cannot execute. This scenario is tested by `RepoGroupHeaderViewModel_ToggleCommand_FollowsToggleEnabled`, covering
`AgentControl-RepoGroupHeaderViewModel-ToggleUnavailable`.

**RepoGroupHeaderViewModel_ToggleExplained**: With toggling disabled the toggle tooltip names the filter. This
scenario is tested by `RepoGroupHeaderViewModel_ToggleCommand_FollowsToggleEnabled`, covering
`AgentControl-RepoGroupHeaderViewModel-ToggleExplained`.

**RepoGroupHeaderViewModel_GroupActions**: The rename command is executed once and the remove command twice; one
rename request and two removal requests are raised, so each command raises its own request exactly once per
execution. This scenario is tested by `RepoGroupHeaderViewModel_RenameAndRemoveCommands_RaiseRequests`, covering
`AgentControl-RepoGroupHeaderViewModel-GroupActions` (the rename request) and
`AgentControl-RepoGroupHeaderViewModel-RemoveGroupRequest` (the removal requests).

**RepoGroupHeaderViewModel_AddRepoRequest**: The add-repository command is executed twice and two requests are raised.
This scenario is tested by `RepoGroupHeaderViewModel_AddRepoCommand_RaisesRequest`, covering
`AgentControl-RepoGroupHeaderViewModel-AddRepoRequest`.

**RepoGroupHeaderViewModel_NewSubgroupRequest**: The new-subgroup command is executed once and one request is raised.
This scenario is tested by `RepoGroupHeaderViewModel_NewSubgroupCommand_RaisesRequest`, covering
`AgentControl-RepoGroupHeaderViewModel-NewSubgroupRequest`.

**RepoGroupHeaderViewModel_NestedPlace**: A group updated to three repos with one directly in it is a top-level group
(depth 0, no parent, no indent, a count tooltip that names the one directly in the group); given depth 2 and the parent
`Client` it exposes both, an indent of twice the indent of a level and an accessible name that ends `, inside Client`;
given depth 40 its indent stops at the deepest indented level while its depth stays 40. *Favorites* and *Ungrouped*
headers given a depth and a parent stay top level. This scenario is tested by
`RepoGroupHeaderViewModel_UpdatePlace_DescribesDepthParentIndentAndDirectCount` and the theory
`RepoGroupHeaderViewModel_UpdatePlace_BuiltInSection_StaysTopLevel`, covering
`AgentControl-RepoGroupHeaderViewModel-NestedPlace`.

**RepoGroupHeaderViewModel_ShowsGroupColor**: A user group's header starts without a color (`Group color: None`); given
`teal` it exposes `Teal`, `HasColor`, the style class `groupAccent5`, the heading and description `Group color: Teal`
and change notifications of the style class and `HasColor`; given an unknown name (`Plaid`) it has no color, and given
a color and then `null` it has none again with an empty style class. *Favorites* and *Ungrouped* headers given `Teal`
stay uncolored. This scenario is tested by `RepoGroupHeaderViewModel_UpdateColor_ExposesNameClassHeadingAndDescription`
and the theory `RepoGroupHeaderViewModel_UpdateColor_BuiltInSection_StaysUncolored`, covering
`AgentControl-RepoGroupHeaderViewModel-ShowsGroupColor`.

**RepoGroupHeaderViewModel_MoveRequests**: A new user group's Move up and Move down commands cannot execute; after the
owner makes both available, executing them raises an up and then a down request and `CanExecuteChanged` was raised;
after the owner gives a reason for up only, up cannot execute and down can. A move made unavailable with a reason shows
that reason as its tooltip (with the tooltip's change notification) while the available one says what it does, and a
*Favorites* header told that both moves are available still offers neither and still has a non-empty tooltip. This
scenario is tested by `RepoGroupHeaderViewModel_MoveCommands_RaiseRequestsAndFollowAvailability` and
`RepoGroupHeaderViewModel_UpdateOrder_DisabledMoveExplainsWhy`, covering
`AgentControl-RepoGroupHeaderViewModel-MoveRequests`.

**RepoGroupHeaderViewModel_SortByNameRequest**: A new user group's Sort by name command cannot execute; after the owner
makes sorting available, executing it raises a sort request and the tooltip says what the item does; after the owner
gives a reason the command cannot execute and the reason is the tooltip (with its change notification). An *Ungrouped*
header told that sorting is available still offers none and still has a non-empty tooltip. This scenario is tested by
`RepoGroupHeaderViewModel_UpdateSort_AvailabilityTooltipAndRequest`, covering
`AgentControl-RepoGroupHeaderViewModel-SortByNameRequest`.

**RepoGroupHeaderViewModel_RemoveGroupWithContentsRequest**: A new user group has no contents, so its
delete-with-contents command cannot execute and its plain delete item is named "Delete group"; after the owner says it
has contents the command can execute, executing it raises a delete-with-contents request (and not the plain delete's),
executing the plain delete raises its own request, and the plain item is named "Delete group, keep contents" with a
tooltip that says the repos and subgroups are kept. A *Favorites* header told that it has contents has none. This
scenario is tested by `RepoGroupHeaderViewModel_UpdateContents_OffersDeleteWithContentsOnlyForGroupsWithContents`,
covering `AgentControl-RepoGroupHeaderViewModel-RemoveGroupWithContentsRequest`.

**RepoGroupHeaderViewModel_RequiresName**: A blank name and a `null` name are refused. This scenario is tested by
`RepoGroupHeaderViewModel_Constructor_BlankName_Throws`, covering `AgentControl-RepoGroupHeaderViewModel-RequiresName`.
