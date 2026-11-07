### SourceEditorViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SourceEditorViewModel` backs the dialog in which the user adds or edits one package source: a
folder of zip packages, a folder of NuGet packages or a NuGet package server. It validates what the
user types as they type it, keeps its Save command disabled while anything is invalid, and reports the
resulting `PackageSourceDefinition` to the owner. A single view model serves all three kinds of
source, since they differ only in details: a NuGet-package source additionally lists the package
identifiers it offers, and a server is addressed by a web address rather than a folder, carries an
"Allow insecure HTTP" choice and can be tested with **Test connection** before it is saved.

#### Data Model

**Type**: `PackageSourceType` — The kind of source being edited, fixed at construction (an
existing source keeps its own kind).

**IsServer**: `bool` — `true` for a NuGet package server: chosen by the caller for a new source, and
decided by the location (a web address) for an existing one.

**IsNew** / **IsAliasEditable**: `bool` — `true` when a new source is being added. The alias of an
existing source cannot be changed because repos pin to a source by its alias.

**IsNuGet**, **Title**, **LocationLabel**, **LocationPlaceholder**, **LocationHelp**: Derived from
`Type`, `IsServer` and `IsNew` to title the dialog ("Add NuGet-Package Server") and explain what to
enter - a folder, or for a server the "Feed address" with the feed's V3 service index address.
**LocationExamplesText** shows the example addresses for JFrog Artifactory
(`https://<host>/artifactory/api/nuget/v3/<repo>/index.json`) and Azure Artifacts
(`https://pkgs.dev.azure.com/<org>/_packaging/<feed>/nuget/v3/index.json`).

**Alias**, **Location**: `string` — The editable values. A field shows no error until the user
has typed in it, so a freshly opened dialog is not covered in red.

**AllowInsecureHttp**: `bool` — The "Allow insecure HTTP" choice for a server, off by default;
**ShowInsecureWarning** is `true` while it is ticked, to warn that a password typed for this server would
cross the network unprotected.

**PackageIds**: `ObservableCollection<string>` — The package identifiers a NuGet-package source
offers, with **NewPackageId** (the "add" box) and **SelectedPackageId** (the entry the remove
button removes).

**AliasError**, **LocationError**, **NewPackageIdError**, **PackageIdsError**: `string?` — The
inline messages, `null` when the value is acceptable.

**CanSave**: `bool` (derived) — `true` only when the alias, the location and (for NuGet) the
package identifier list are all acceptable.

**CanTestConnection**, **TestConnectionCommand**, **CancelTestCommand**, **IsTesting**,
**TestProgressText**, **TestResultKind**, **TestResultTitle**, **TestResultDetail**: The connection
test. It can run when the source is a server with an acceptable address and at least one package ID
(the alias is not needed). `TestConnectionCommand` is an `AsyncRelayCommand`, so a second request while
one runs is ignored and `CancelTestCommand` cancels it. The result is a `ConnectionTestResultKind`
(`None`, `Succeeded`, `Failed`, `Canceled`) with a headline and an explanation.

**_providerFactory**: `Func<PackageSourceDefinition, IPackageSourceProvider>` — creates the provider the
test uses; by default a plain `NuGetSourceProvider` without credential facilities, supplied by the owner
so that the test signs in through the application's session credentials and tests can use a fake.

#### Key Methods

**Constructor**: Prepares the dialog.

- *Parameters*: The source kind, the source to edit (or `null` for a new one), a callback telling
  whether an alias is already configured, whether a new NuGet source is a server, and the provider
  factory.
- *Preconditions*: The alias callback is not null.
- *Postconditions*: The values are seeded from a copy of the edited source, which is never
  modified; a new editor starts with empty values and `CanSave` false.

**Validation**: The alias must be present, use the characters `PackageSourceDefinition` allows,
and differ from every configured alias ignoring case. A folder location must be a full folder path,
and one that embeds a credential or is a web address is refused with its own message. A server address
is judged by `PackageSourceDefinition.GetServerLocationProblem`: `https` (or `http` only while
**AllowInsecureHttp** is ticked), ending in `index.json`, with no user information, query string or
fragment. A NuGet package identifier must use the characters a package identifier allows and not repeat
one already listed (`AgentControl-SourceEditorViewModel-Validation`). A NuGet source needs at least one
identifier, with a server-specific message (`AgentControl-SourceEditorViewModel-PackageIds`).

**AddPackageIdCommand**, **RemovePackageIdCommand**: Add the typed identifier to, or remove the
selected identifier from, `PackageIds`; adding is available only when the typed text is a
valid, new identifier.

**TestConnectionAsync** (private, bound to `TestConnectionCommand`): Clears the previous result, builds a
definition from the dialog (a placeholder alias is used when none was typed yet), creates the provider
through the factory and lists the versions of the **first** package ID, so only a version list is read and
nothing is saved or downloaded. The outcome is turned into a sentence that says what to do: *Connection OK*
with the number of versions and the newest; *Package not found* when the server answered with no such
package; *Sign-in failed*, *Sign-in canceled*, *Timed out* or *Server unreachable* from the
`PackageSourceAccessException.Kind`; *Connection failed* for any other failure; and a neutral *Test
canceled* when the user cancelled. The test may open the sign-in dialog like any other use of the server.
A result is dropped as soon as the address, the insecure choice or the package IDs change, so a stale
"Connection OK" never sits beside an edited address
(`AgentControl-SourceEditorViewModel-TestConnection`).

**Save** (private, bound to `SaveCommand`): Raises `Saved` with the definition when `CanSave`.

- *Postconditions*: `Saved` carries a copy of the edited source (so any value of it that the
  dialog does not show is preserved) or a new definition, with the trimmed location and - for a
  NuGet source only - the package identifiers and - for a server only - the insecure-HTTP choice
  (`AgentControl-SourceEditorViewModel-Save`, `AgentControl-SourceEditorViewModel-ServerSource`).

#### Error Handling

The constructor throws `ArgumentNullException` for a null alias callback. Invalid input is never
an exception: it is shown as an inline message and keeps `CanSave` false, and `Save` does nothing
when invoked while invalid. A connection test never lets a failure escape: every anticipated failure
(`PackageSourceAccessException`, other `InvalidOperationException`) becomes a result, and an unexpected one
is reported by the command's error callback as "Connection failed"; a cancelled test is a neutral result, not
an error. Messages shown never repeat a credential.

#### Dependencies

- **PackageSourceDefinition**, **PackageSourceType**, **PackageSourceAccessException**,
  **IPackageSourceProvider**, **NuGetSourceProvider** (`AgentPackageManagement` subsystem) — the
  source data type, its alias, credential and server-address validation, its location kind, the
  failure causes, and the provider the connection test uses.
- **RelayCommand**, **AsyncRelayCommand**, **ConnectionTestResultKind** (`LauncherUI` subsystem, shared
  MVVM support) — back the commands and describe the test outcome.

#### Callers

- **SettingsWindowViewModel** — creates the editor through `CreateEditor` and applies the saved
  definition to its source list.
- **SourceEditorWindow** (code-behind) — closes the window when `Saved` is raised, cancels a running
  test when the window closes, and provides the folder picker for the location.
