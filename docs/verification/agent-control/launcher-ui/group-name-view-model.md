### GroupNameViewModel

#### Verification Approach

`GroupNameViewModel` is verified with unit tests in `GroupNameViewModelTests.cs`. Each test constructs a real prompt
over a list of existing group names (for creating a group or for renaming one), types names through `Text` and reads
the messages and `CanAccept`, then executes the commands and listens to `Completed`; the shared name rules of
`RepoGroupName` are real, not substituted (they have their own tests in the `Settings` subsystem). No window, file or
process is involved.

#### Test Environment

N/A - standard test environment (no window, no files and no processes are involved).

#### Acceptance Criteria

- An empty prompt is neutral (no error, cannot be accepted); a valid name is cleaned up and accepted.
- Whether the name can be accepted and the message shown follow the current text each time it changes.
- A blank name, a name over 40 characters (the message gives the limit; exactly 40 is accepted) and the built-in
  names *Favorites* and *Ungrouped* show their message and cannot be accepted.
- Creating a group with an existing group's name in another letter case shows a note (not an error), can be accepted
  and completes with the existing group's spelling.
- Renaming starts with the group's own name, refuses another group's name, accepts a change of letter case only and
  completes with the name as typed, cleaned of surrounding and repeated white space.
- A prompt for a new subgroup (a new name is required) refuses the name of every existing group in any letter case,
  shows no hint about joining it, and completes with the cleaned name when a fresh one is typed.
- The prompt completes exactly once - with the name when accepted, with none when canceled - whatever is done
  afterwards.
- A missing title, prompt, accept text or list of groups is refused.

#### Test Scenarios

**GroupNameViewModel_ValidatesAsTyped**: A new-group prompt has a name one over the limit typed and then exactly at
the limit, and a valid name typed; the message and `CanAccept` follow the current text each time. Changing the text
announces a change of the text, the message, the hint and whether the name can be accepted, once per real change. This
scenario is tested by `GroupNameViewModel_TooLongName_ShowsLengthMessage`,
`GroupNameViewModel_NewGroup_ValidName_AcceptsCleanedName` and
`GroupNameViewModel_Text_RaisesChangeNotificationsForTheDerivedState`, covering
`AgentControl-GroupNameViewModel-ValidatesAsTyped`.

**GroupNameViewModel_NeutralUntilTyped**: A new-group prompt with nothing typed has no error and cannot be accepted (the
accept command cannot execute). This scenario is tested by `GroupNameViewModel_NewGroup_ValidName_AcceptsCleanedName`,
covering `AgentControl-GroupNameViewModel-NeutralUntilTyped`.

**GroupNameViewModel_RefusesBlankName**: A blank name is typed; the message says it cannot be blank, the name cannot be
accepted and nothing completes - in the new-group prompt, in the rename prompt and in the new-subgroup prompt. This
scenario is tested by `GroupNameViewModel_InvalidName_ShowsMessageAndCannotAccept` and
`GroupNameViewModel_BlankBuiltInAndTooLongNames_AreRefusedInEveryMode`, covering
`AgentControl-GroupNameViewModel-RefusesBlankName`.

**GroupNameViewModel_RefusesTooLongName**: A name one over the limit shows the message that names the limit and cannot
be accepted, in the new-group, rename and new-subgroup prompts; a name of exactly the limit has no error and can be
accepted. This scenario is tested by `GroupNameViewModel_TooLongName_ShowsLengthMessage` and
`GroupNameViewModel_BlankBuiltInAndTooLongNames_AreRefusedInEveryMode`, covering
`AgentControl-GroupNameViewModel-RefusesTooLongName`.

**GroupNameViewModel_RefusesBuiltInNames**: The reserved names `favorites` and `Ungrouped` are typed; the message
says they are built in, they cannot be accepted and nothing completes - also in the rename and the new-subgroup prompt,
where no hint is shown either. This scenario is tested by `GroupNameViewModel_InvalidName_ShowsMessageAndCannotAccept`
and `GroupNameViewModel_BlankBuiltInAndTooLongNames_AreRefusedInEveryMode`, covering
`AgentControl-GroupNameViewModel-RefusesBuiltInNames`.

**GroupNameViewModel_AcceptsUsableName**: A valid name padded with spaces and with repeated inner spaces has no error
and no note, can be accepted, and the prompt completes with the cleaned name. This scenario is tested by
`GroupNameViewModel_NewGroup_ValidName_AcceptsCleanedName`, covering
`AgentControl-GroupNameViewModel-AcceptsUsableName`.

**GroupNameViewModel_NewGroup**: With the group *Work* existing, the name `work` is typed; there is no error and the
name can be accepted. This scenario is tested by `GroupNameViewModel_NewGroup_ExistingName_HintsAndAdoptsSpelling`,
covering `AgentControl-GroupNameViewModel-NewGroup`.

**GroupNameViewModel_NewGroupHint**: With the group *Work* existing, the name `work` is typed; a note, not an error,
names the existing group. This scenario is tested by `GroupNameViewModel_NewGroup_ExistingName_HintsAndAdoptsSpelling`,
covering `AgentControl-GroupNameViewModel-NewGroupHint`.

**GroupNameViewModel_NewGroupAdoptsSpelling**: With the group *Work* existing, the name `work` is typed and accepted;
the prompt completes with `Work`. This scenario is tested by
`GroupNameViewModel_NewGroup_ExistingName_HintsAndAdoptsSpelling`, covering
`AgentControl-GroupNameViewModel-NewGroupAdoptsSpelling`.

**GroupNameViewModel_Rename**: Renaming *Work*, `HOME` (another group) is refused with the "already exists" message
and cannot be accepted. This scenario is tested by `GroupNameViewModel_Rename_RefusesOtherGroupsAcceptsCaseChange`,
covering `AgentControl-GroupNameViewModel-Rename`.

**GroupNameViewModel_RenameStartsWithCurrentName**: Renaming *Work* starts with `Work` as the typed name. This
scenario is tested by `GroupNameViewModel_Rename_RefusesOtherGroupsAcceptsCaseChange`, covering
`AgentControl-GroupNameViewModel-RenameStartsWithCurrentName`.

**GroupNameViewModel_RenameAcceptsCaseChange**: Renaming *Work*, `work` (the group's own name in another letter case)
has no error and no note and completes with `work`. This scenario is tested by
`GroupNameViewModel_Rename_RefusesOtherGroupsAcceptsCaseChange`, covering
`AgentControl-GroupNameViewModel-RenameAcceptsCaseChange`.

**GroupNameViewModel_NewSubgroup**: A prompt that requires a new name, with the groups *Home* and *Work* existing,
refuses `HOME` with the "already exists" message and no hint of joining it, and cannot accept it; `  new   Name ` is
accepted and completes with `new Name`. This scenario is tested by
`GroupNameViewModel_NewSubgroup_RefusesExistingNamesAcceptsFreshName`, covering
`AgentControl-GroupNameViewModel-NewSubgroup`.

**GroupNameViewModel_CompletesOnce**: A name is typed and the prompt is accepted, accepted again, canceled twice and
the text changed and accepted once more; the single completion carried the first accepted name. This scenario is
tested by `GroupNameViewModel_Accept_CompletesOnceWithTheName`, covering
`AgentControl-GroupNameViewModel-CompletesOnce`.

**GroupNameViewModel_CancelCompletesOnce**: A name is typed and the prompt is canceled, canceled again and then
accepted; the single completion carried no name. This scenario is tested by
`GroupNameViewModel_Cancel_CompletesOnceWithNull`, covering `AgentControl-GroupNameViewModel-CancelCompletesOnce`.

**GroupNameViewModel_RequiresArguments**: Each of the four required arguments is passed as `null`. This scenario is
tested by `GroupNameViewModel_Constructor_NullArguments_Throw`, covering
`AgentControl-GroupNameViewModel-RequiresArguments`.
