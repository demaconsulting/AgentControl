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
- A blank name, a name over 40 characters (the message gives the length) and the built-in names *Favorites* and
  *Ungrouped* show their message and cannot be accepted.
- Creating a group with an existing group's name in another letter case shows a note, can be accepted and completes
  with the existing group's spelling.
- Renaming starts with the group's own name, refuses another group's name, accepts a change of letter case only and
  completes with exactly what was typed.
- The prompt completes exactly once - with the name, or with none when canceled - whatever is done afterwards.
- A missing title, prompt, accept text or list of groups is refused.

#### Test Scenarios

**GroupNameViewModel_ValidatesAsTyped**: A new-group prompt is checked empty, with a valid name padded with spaces
and repeated inner spaces (accepted, cleaned), with a blank name and the reserved names in other letter case
(message, cannot be accepted, no completion), and with a name one over the limit and exactly at it. This
scenario is tested by `GroupNameViewModel_NewGroup_ValidName_AcceptsCleanedName`,
`GroupNameViewModel_InvalidName_ShowsMessageAndCannotAccept` and `GroupNameViewModel_TooLongName_ShowsLengthMessage`,
covering `AgentControl-GroupNameViewModel-ValidatesAsTyped`.

**GroupNameViewModel_NewGroup**: With the group *Work* existing, the name `work` is typed; there is no error, a note
names the existing group, and accepting completes with `Work`. This scenario is tested by
`GroupNameViewModel_NewGroup_ExistingName_HintsAndAdoptsSpelling`, covering `AgentControl-GroupNameViewModel-NewGroup`.

**GroupNameViewModel_Rename**: Renaming *Work* starts with `Work`; `HOME` (another group) is refused with the
"already exists" message; `work` (the group's own name in another letter case) has no error and no note and completes
with `work`. This scenario is tested by `GroupNameViewModel_Rename_RefusesOtherGroupsAcceptsCaseChange`, covering
`AgentControl-GroupNameViewModel-Rename`.

**GroupNameViewModel_CompletesOnce**: A name is typed and the prompt is canceled, canceled again and then
accepted; the single completion carried no name. This scenario is tested by
`GroupNameViewModel_Cancel_CompletesOnceWithNull`, covering `AgentControl-GroupNameViewModel-CompletesOnce`.

**GroupNameViewModel_RequiresArguments**: Each of the four required arguments is passed as `null`. This scenario is
tested by `GroupNameViewModel_Constructor_NullArguments_Throw`, covering
`AgentControl-GroupNameViewModel-RequiresArguments`.
