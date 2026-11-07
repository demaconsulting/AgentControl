### CredentialPromptViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`CredentialPromptViewModel` backs the sign-in dialog that asks the user for the user name and password (or
access token) a package server needs. The dialog exists so a credential is only ever typed into a window that
says, prominently, which server will receive it, and that the credential is kept for this session only. It
satisfies `AgentControl-CredentialPromptViewModel-ShowsDestination` and
`AgentControl-CredentialPromptViewModel-SignIn`, and together with `CredentialPromptWindow` and
`AvaloniaCredentialPrompt` (view-layer types documented in the `LauncherUI` subsystem design) provides
`AgentControl-LauncherUI-SignIn`.

#### Data Model

**OriginText**: `string` - the server's scheme, host and port (`FeedOrigin.ToString`), shown prominently so
the user can check where what they type will go; it can never contain a path, query string or secret.

**SourceAlias**, **Explanation**: the alias of the configured source and the sentence that says that source
needs a user name and a password or token.

**IsInsecure**: `bool` - the server uses plain `http`, so the dialog warns that what is typed is not
encrypted.

**PreviousCredentialRejected**: `bool` - a credential was already supplied for this server during this
operation and was rejected, so the dialog says the last sign-in did not work.

**SessionNoteText** (constant): "Used for this session only - AgentControl does not save it." There is no
"remember" option in this version.

**UserName** / **Password**: the two entered values. The password is held only until the dialog closes and is
never logged or exposed other than in the answer; the window masks it.

**CanSignIn**: `bool` - both a non-blank user name and a non-empty password have been entered.

**SignInCommand** / **CancelCommand**: the commands bound to the buttons. **Completed** is raised exactly once
with the answer.

#### Key Methods

**Constructor(request)**: Copies the display facts from the `CredentialPromptRequest`; throws
`ArgumentNullException` for a null request.

**SignIn** (via `SignInCommand`): When `CanSignIn`, completes with a `NuGetCredential` whose user name is
trimmed and whose password is exactly as typed (a trimmed password could fail for no visible reason); does
nothing otherwise.

**Cancel**: Completes with no credential. It is also called by the window when it is closed by any other
means (the title-bar button, Escape) and by `AvaloniaCredentialPrompt` when the operation that asked is
cancelled, so every way out is an answer.

**Complete (private)**: Raises `Completed` once, however many times it is reached, so a second answer cannot
be mistaken for a second question.

#### Error Handling

Only argument validation throws (`ArgumentNullException`). An incomplete form is not an error: `CanSignIn` is
false and signing in does nothing.

#### Dependencies

- **ICredentialPrompt** / **CredentialPromptRequest**, **FeedOrigin** (`PackageCredentials` subsystem) - the
  request and the origin text.
- **NuGetCaching** (OTS) - the `NuGetCredential` it returns.
- **RelayCommand**, **ViewModelBase** (`LauncherUI`) - command and change-notification support.

#### Callers

- **AvaloniaCredentialPrompt** (`LauncherUI`) - creates one view model per question and awaits its
  `Completed` event.
- **CredentialPromptWindow** - binds to it and closes with its answer.
