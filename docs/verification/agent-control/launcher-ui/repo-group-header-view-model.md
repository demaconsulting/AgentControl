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

- Each kind of header sets exactly its own section flag; only a user group is a user group.
- The accessible name says the section, its repo count (singular for one) and whether it is collapsed or expanded,
  is also the header's text form, and the tooltips and change notifications follow `Update`.
- The toggle command can execute only while toggling is enabled, raises its request when executed, and its tooltip
  explains the filter when it cannot.
- The rename and remove commands raise exactly one request per execution and change nothing themselves.
- A header without a name (blank or `null`) is refused.

#### Test Scenarios

**RepoGroupHeaderViewModel_SectionKind**: Headers of each kind are created and their section flags are read. This
scenario is tested by `RepoGroupHeaderViewModel_Kind_SelectsSectionFlags`, covering
`AgentControl-RepoGroupHeaderViewModel-SectionKind`.

**RepoGroupHeaderViewModel_DescribesSection**: A header is updated to one repo expanded and then to three repos
collapsed; the accessible name, the toggle and count tooltips, the expanded flag and the change notification of the
accessible name are checked, and the text form of a favorites header is its accessible name. This scenario is tested
by `RepoGroupHeaderViewModel_Update_DescribesCountAndState` and `RepoGroupHeaderViewModel_ToString_IsTheAccessibleName`,
covering `AgentControl-RepoGroupHeaderViewModel-DescribesSection`.

**RepoGroupHeaderViewModel_Toggle**: With toggling enabled the command can execute and raises one request; with it
disabled (a filter is active) the command cannot execute and the tooltip names the filter. This scenario is tested by
`RepoGroupHeaderViewModel_ToggleCommand_FollowsToggleEnabled`, covering `AgentControl-RepoGroupHeaderViewModel-Toggle`.

**RepoGroupHeaderViewModel_GroupActions**: The rename command is executed once and the remove command twice; one
rename request and two removal requests are raised. This scenario is tested by
`RepoGroupHeaderViewModel_RenameAndRemoveCommands_RaiseRequests`, covering
`AgentControl-RepoGroupHeaderViewModel-GroupActions`.

**RepoGroupHeaderViewModel_RequiresName**: A blank name and a `null` name are refused. This scenario is tested by
`RepoGroupHeaderViewModel_Constructor_BlankName_Throws`, covering `AgentControl-RepoGroupHeaderViewModel-RequiresName`.
