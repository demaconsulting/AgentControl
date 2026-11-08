### RepoGroupHeaderViewModel

#### Verification Approach

`RepoGroupHeaderViewModel` is verified with unit tests in `RepoGroupHeaderViewModelTests.cs`. Each test constructs a
real header, drives it through its constructor, `Update` and its commands, and reads its properties and the events it
raises; nothing is mocked and no Avalonia `Window` is constructed. How the owning `MainWindowViewModel` creates,
updates and reacts to headers is verified by that unit, and how the window shows them by the headless window tests of
the `LauncherUI` subsystem (`RepoGroupWindowTests.cs`).

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
`RepoGroupHeaderViewModel_Update_DescribesCountAndState`, covering `AgentControl-RepoGroupHeaderViewModel-UpdatesInPlace`.

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

**RepoGroupHeaderViewModel_RequiresName**: A blank name and a `null` name are refused. This scenario is tested by
`RepoGroupHeaderViewModel_Constructor_BlankName_Throws`, covering `AgentControl-RepoGroupHeaderViewModel-RequiresName`.
