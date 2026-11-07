### CredentialPromptViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`CredentialPromptViewModel` backs the sign-in dialog that asks the user for the user name and password (or
access token) a package server needs. The dialog exists so a credential is only ever typed into a window that
says, prominently, which server will receive it and what becomes of it: kept for this session only, or - when the
user ticks "Remember on this computer" - saved by their own Git credential helper. Its behavior is specified by
the unit requirements `AgentControl-CredentialPromptViewModel-ShowsDestination`,
`-InsecureHttpFlag`, `-SessionOnlyNote`, `-PreviousCredentialRejected`, `-RememberOption`,
`-RememberUnavailable`, `-RememberNeverWithoutHelper`, `-RememberAnswer`, `-RememberNote`,
`-SignInRequiresBoth`, `-SignIn`, `-PasswordVerbatim`, `-Cancel` and `-AnswerOnce` (each prefixed
`AgentControl-CredentialPromptViewModel`), and together with `CredentialPromptWindow` and
`AvaloniaCredentialPrompt` (view-layer types documented in the `LauncherUI` subsystem design) it provides
`AgentControl-LauncherUI-SignIn` and `AgentControl-LauncherUI-RememberSignIn`.

#### Data Model

**OriginText**: `string` - the server's scheme, host and port (`FeedOrigin.ToString`), shown prominently so
the user can check where what they type will go; it can never contain a path, query string or secret.

**SourceAlias**, **Explanation**: the alias of the configured source and the sentence that says that source
needs a user name and a password or token.

**IsInsecure**: `bool` - the server uses plain `http`, so the dialog warns that what is typed is not
encrypted.

**PreviousCredentialRejected**: `bool` - a credential was already supplied for this server during this
operation and was rejected, so the dialog says the last sign-in did not work; copied from the request, and
`false` on a first request so that no such warning is shown
(`AgentControl-CredentialPromptViewModel-PreviousCredentialRejected`).

**CanRemember**: `bool` - copied from the request: a credential helper applies to the server, so the sign-in
can be kept. When it is `false` the "Remember" checkbox is shown **disabled** (with an explanatory tooltip, shown
even though the control is disabled) rather than hidden, so the user learns that the option exists and why it is
unavailable.

**Remember**: `bool` - the "Remember on this computer (saved by your Git credential helper)" choice. **Off by
default** (keeping a secret is the user's explicit decision) and forced off while `CanRemember` is `false`, so the
answer can never ask for something that cannot be done. **RememberTooltip** explains what ticking it does, or why
it cannot be ticked.

**SessionNoteText** (constant) / **RememberedNoteText** (constant) / **NoteText**: "Used for this session only -
AgentControl does not save it." while "Remember" is off, and "Saved by your Git credential helper on this computer -
AgentControl does not store it." once it is ticked. The note always says who keeps the secret.

**UserName** / **Password**: the two entered values. The password is held only until the dialog closes and is
never logged or exposed other than in the answer; the window masks it.

**CanSignIn**: `bool` - both a non-blank user name and a non-empty password have been entered.

**SignInCommand** / **CancelCommand**: the commands bound to the buttons. **Completed** is raised exactly once
with the answer (a `CredentialPromptAnswer`, or `null` when cancelled).

#### Key Methods

**Constructor(request)**: Copies the display facts, including `CanRemember`, from the `CredentialPromptRequest`;
throws `ArgumentNullException` for a null request.

**SignIn** (via `SignInCommand`): When `CanSignIn`, completes with a `CredentialPromptAnswer` holding a
`NuGetCredential` whose user name is
trimmed and whose password is exactly as typed (a trimmed password could fail for no visible reason), and the
`Remember` choice; does nothing otherwise.

**Cancel**: Completes with no answer. It is also called by the window when it is closed by any other
means (the title-bar button, Escape) and by `AvaloniaCredentialPrompt` when the operation that asked is
cancelled, so every way out is an answer.

**Complete (private)**: Raises `Completed` once, however many times it is reached, so a second answer cannot
be mistaken for a second question.

#### Error Handling

Only argument validation throws (`ArgumentNullException`). An incomplete form is not an error: `CanSignIn` is
false and signing in does nothing.

#### Dependencies

- **ICredentialPrompt** / **CredentialPromptRequest** / **CredentialPromptAnswer**, **FeedOrigin**
  (`PackageCredentials` subsystem) - the request, the answer and the origin text.
- **NuGetCaching** (OTS) - the `NuGetCredential` it returns.
- **RelayCommand**, **ViewModelBase** (`LauncherUI`) - command and change-notification support.

#### Callers

- **AvaloniaCredentialPrompt** (`LauncherUI`) - creates one view model per question and awaits its
  `Completed` event.
- **CredentialPromptWindow** - binds to it and closes with its answer.
