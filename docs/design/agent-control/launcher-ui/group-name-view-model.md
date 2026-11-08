### GroupNameViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`GroupNameViewModel` backs the small prompt that asks for a repo group's name. It is used both to create a group (and
move a repo into it) and to rename one. Its single responsibility is to validate the typed name as the user types and
let only a usable name through, and to run the prompt's accept/cancel lifecycle so the window itself stays free of
logic. The rules for what a name may be live in `RepoGroupName` (`Settings` subsystem unit); this unit adds the messages,
the "does it clash with another group" check that needs the existing groups, and completion. Its behavior is
specified by the unit requirements `AgentControl-GroupNameViewModel-ValidatesAsTyped`, `-NeutralUntilTyped`,
`-RefusesBlankName`, `-RefusesTooLongName`, `-RefusesBuiltInNames`, `-AcceptsUsableName`, `-NewGroup`,
`-NewGroupHint`, `-NewGroupAdoptsSpelling`, `-Rename`, `-RenameStartsWithCurrentName`, `-RenameAcceptsCaseChange`,
`-CompletesOnce`, `-CancelCompletesOnce` and `-RequiresArguments` (each prefixed `AgentControl-GroupNameViewModel`),
and together with `MainWindowViewModel` and `RepoCardViewModel` it provides `AgentControl-LauncherUI-RepoGroups`.

#### Data Model

**Title**, **Prompt**, **AcceptText**: the window title, the sentence above the text box and the accept button's
text (for example `Create` or `Rename`), given at construction.

**Existing groups** (private): the distinct names of the groups that exist now (`RepoGroupName.DistinctNames`), for the
clash check and for adopting an existing group's spelling. **Renaming** (private): the group being renamed, or `null`
when a group is being created.

**Text**: the name as typed; its initial value is the group's current name when renaming and empty when creating.
Changing it re-validates and raises change notifications for the messages and for `CanAccept`, so the message and
whether the name can be accepted always follow the current text (`AgentControl-GroupNameViewModel-ValidatesAsTyped`).

**ErrorText** / **HasError**: why the typed name cannot be used, or `null`. Nothing is said while nothing has been
typed, so the prompt does not open already scolding (`AgentControl-GroupNameViewModel-NeutralUntilTyped`). A blank
name (`-RefusesBlankName`), a name longer than `RepoGroupName.MaxLength` (40 characters, with the length of the
cleaned-up name in the message; exactly 40 is fine) (`-RefusesTooLongName`) and the built-in names *Favorites* and
*Ungrouped* in any letter case (`-RefusesBuiltInNames`) are refused; when renaming, the name of another existing group
is refused as well (`-Rename`).

**HintText** / **HasHint**: a friendly note that is not an error: when creating a group, that the name matches an
existing group ignoring letter case and the repo will be added to it (`AgentControl-GroupNameViewModel-NewGroup`,
`-NewGroupHint`).

**CanAccept**: a name has been typed, it is valid by the shared rules and there is no error.
**AcceptCommand** (available while `CanAccept`) and **CancelCommand**; the **Completed** event
(`EventHandler<string?>`) is raised once with the chosen name, or `null` when canceled.

#### Key Methods

**Constructor(title, prompt, acceptText, existingGroups, renaming = null)**: Stores the texts, cleans the list of
existing groups and sets the initial text. Throws `ArgumentNullException` for a null title, prompt, accept text or
list of groups (`AgentControl-GroupNameViewModel-RequiresArguments`).

**Accept** (via `AcceptCommand`): Does nothing unless `CanAccept`. A usable name completes with the name cleaned of
surrounding and repeated white space (`AgentControl-GroupNameViewModel-AcceptsUsableName`). A *rename* completes with
exactly what was typed (cleaned of surrounding white space), so changing only the letter case works
(`-RenameAcceptsCaseChange`); when renaming, the prompt starts with the group's current name
(`-RenameStartsWithCurrentName`). Creating a group completes with the
spelling of an existing group the name matches ignoring letter case (`RepoGroupName.Canonicalize`), otherwise with the
typed name (`-NewGroupAdoptsSpelling`).

**Cancel** (via `CancelCommand`, also called by the window when it closes by any other route): Completes with `null`.

**Complete** (private): Raises `Completed` the first time only, so a prompt answered by the accept button and then
closed by the title bar - or accepted twice, canceled twice, or canceled and then accepted - tells its listener exactly
once, with the first answer (`AgentControl-GroupNameViewModel-CompletesOnce` for an accepted name,
`-CancelCompletesOnce` for a cancellation).

#### Error Handling

Only the constructor's argument validation throws. An unusable name is not an error: it produces a message, and
`CanAccept` is false, so accepting does nothing.

#### Dependencies

- **RepoGroupName** (`Settings` subsystem unit) - the shared rules: cleaning up, validation, the length limit, the
  built-in names, the case-insensitive comparer and canonical spelling.
- **ViewModelBase**, **RelayCommand** (`LauncherUI`) - change notification and commands.

#### Callers

- **MainWindow** (view) - creates one prompt for a new group (from a card's menu) and one for a rename (from a
  header), shows it in `GroupNameWindow` and passes the answer on to `RepoCardViewModel.MoveToGroup` or
  `MainWindowViewModel.RenameGroup`.
- **GroupNameWindow** (view) - binds to it and closes when it completes; it applies `DialogBehavior` and cancels the
  prompt when the window is closed by any other route.
