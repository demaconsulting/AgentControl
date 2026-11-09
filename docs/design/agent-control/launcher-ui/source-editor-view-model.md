### SourceEditorViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SourceEditorViewModel` backs the dialog in which the user adds or edits one package source: a
folder of zip packages, a folder of NuGet packages, a NuGet package server or a Git repository. It validates what the
user types as they type it, keeps its Save command disabled while anything is invalid, and reports the
resulting `PackageSourceDefinition` to the owner. A single view model serves all four kinds of
source, since they differ only in details: a NuGet-package source additionally lists the package
identifiers it offers, and a server is addressed by a web address rather than a folder, carries an
"Allow insecure HTTP" choice and can be tested with **Test connection** before it is saved. A Git repository is
addressed by a repository URL or path, delivers one package whose versions are the tags that match a tag pattern, and
can be tested with **Test connection** too; its fields live in the second file of the class,
`SourceEditorViewModel.Git.cs` (the class is `partial`). A NuGet-package source
(folder or server) can also require signed packages, optionally pinned to one publisher's certificate; a server
source can forget the sign-in saved for it. The unit requirements this view model satisfies are in
`docs/reqstream/agent-control/launcher-ui/source-editor-view-model.yaml`, one observable behavior each, grouped as
validation (`AgentControl-SourceEditorViewModel-Validation` and its alias, location, credential, server-address and
package-ID rules), location tooltip (`-LocationTooltip`), server source (`-ServerSource` and its examples,
insecure-HTTP and saved-settings rules),
connection test
(`-TestConnection` and its availability, progress, cancel, outcome and stale-result rules), package IDs, save,
signature options (`-SignatureOptions`), signer fingerprint (`-SignerFingerprint`), forgetting a saved sign-in
(`-ForgetSavedSignIn`) and the Git repository source (`-GitEditor`, `-GitValidation`, `-GitTestConnection`,
`-GitTestFailureReasons`, `-GitTestCancelAndStale`, `-GitTagPreview` and `-GitSignInNote`); together they provide
`AgentControl-LauncherUI-PackageSourceValidation`,
`AgentControl-LauncherUI-PackageSourcePackageIds`, `AgentControl-LauncherUI-AddPackageSource`,
`AgentControl-LauncherUI-EditPackageSource`, `AgentControl-LauncherUI-ServerSourceEditor`,
`AgentControl-LauncherUI-InsecureHttpChoice`, `AgentControl-LauncherUI-ServerSourceSettingsKept`,
`AgentControl-LauncherUI-TestConnection`, `AgentControl-LauncherUI-SignatureOptions`,
`AgentControl-LauncherUI-ForgetSavedSignIn`, `AgentControl-LauncherUI-GitSourceEditor` and
`AgentControl-LauncherUI-GitSignInGuidance`.

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

**LocationTooltip**: `string?` (read-only, derived from `Location`) — The tooltip of the location box: the whole
entered folder or feed address, exactly as typed, because a long UNC path or feed address does not fit the box even
when it wraps and the user must be able to verify the whole value before saving
(`AgentControl-SourceEditorViewModel-LocationTooltip`). It is `null` - no tooltip at all, rather than an empty one -
while the location is empty or only white space. Every change of `Location` raises the change notification for
`LocationTooltip` together with the one for `Location`, so the bound tooltip follows what is typed.

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
one runs is ignored and `CancelTestCommand` cancels it; the view labels its button "Cancel test" so it cannot be
mistaken for the dialog's own Cancel button, which is visible at the same time. The result is a `ConnectionTestResultKind`
(`None`, `Succeeded`, `Failed`, `Canceled`) with a headline and an explanation.

**_providerFactory**: `Func<PackageSourceDefinition, IPackageSourceProvider>` — creates the provider the
test uses; by default (`CreateDefaultProvider`) an interactive `GitSourceProvider` that enforces the minimum git
version for a Git source and a plain `NuGetSourceProvider` without credential facilities for any other source. The
owner supplies one so that the test signs in through the application's session credentials and uses the
configured git executable, and tests can use a fake.

**ShowSignatureOptions**: `bool` — `true` for a NuGet source (folder or server); a zip package carries no
signature, so a zip source has no such section and always saves `RequireSignedPackages` as off.

**RequireSignedPackages**: `bool` — the "Require signed packages" choice, **off by default**.
**IsRequireSignedEnabled** is `false` where `SignatureVerificationSupport` says signatures cannot be verified; the
checkbox is then shown **disabled** (rather than hidden) with **RequireSignedTooltip** set to "Package signature
verification is not supported on this platform", so the user learns why. **SignatureHelpText** explains what the
option does and the limit of using it without a pin: "Only accept packages that carry a valid signature. Without a
fingerprint, any validly signed package that chains to a trusted root is accepted - including packages signed by
unrelated publishers - so pinning your publisher's certificate below is recommended."

**SignerFingerprint**: `string` — the text the user typed or pasted for the trusted publisher's (author)
certificate SHA-256 fingerprint. **IsFingerprintEnabled** is `true` only while signed packages are required (and
can be). **FingerprintError** explains what is wrong with text that is not 64 hexadecimal characters (colons, dashes
and spaces are fine) and is `null` for empty text (the pin is optional), when the field is not in use, or before the
user has typed in it. **FingerprintHelpText** says what the value is and how to find it
(`dotnet nuget verify --all <package>.nupkg`). The pin is the **author** certificate because only the author
signature says who published a package.

**CanForgetSavedSignIn**, **ForgetSavedSignInCommand**, **ForgetTooltip**, **NoHelperForgetTooltip**,
**ForgetResultText**, **HasForgetResult**, **ForgetConfirmationRequested**, **HelperCheckCompleted**: "Forget
saved sign-in". Available only for a server source whose address is **acceptable for saving the source** - the
same rules `CanSave` applies to the address (`https`, or `http` only while **AllowInsecureHttp** is ticked,
ending in `index.json`, no embedded credential, query string or fragment, as judged by
`PackageSourceDefinition.GetServerLocationProblem`) **and** that parses as a feed origin; being a parseable origin
alone is not enough, because the action removes a stored secret and must act only on a server the dialog would
itself accept (`AgentControl-SourceEditorViewModel-ForgetAcceptableAddressOnly`). It is further available only when the
`PackageCredentialContext` has a user's credential store to ask **and** that store reports a Git credential helper
that applies to the server's origin (without one nothing can have been saved). Otherwise the button is shown
disabled with a tooltip that says why: no store to ask, an address that is not acceptable, "checking" while the
helper question is still being answered, or that no helper is configured for the server (`NoHelperForgetTooltip`).
The helper question is asked only for an acceptable address, whenever the address changes to a new origin,
answered once per origin (an earlier answer is reused while the address is edited further), superseded when the
address changes again, and a failure to answer counts as "no helper"; ticking or clearing **AllowInsecureHttp**
re-evaluates the address, so an `http` address becomes available (and the helper is asked) when the option is
ticked. `HelperCheckCompleted` is the task of the latest question, for the view and tests.
The command does **not** forget anything: it
raises `ForgetConfirmationRequested` with a question that names the server and says what is removed, and the view
calls `ForgetSavedSignInAsync` only after the user agrees. A confirmation comes first because the action removes a
secret the user may not be able to recreate from memory, and an accidental click must not do it.

**_credentials**: `PackageCredentialContext?` — the credential facilities used to forget a saved sign-in, and
`_signaturesSupported` — the platform answer, injectable so tests can exercise both.

**Git source fields** (`SourceEditorViewModel.Git.cs`): **IsGit** (`Type` is `Git`) and **ShowTestSection**
(`IsServer || IsGit`) select what the window shows. **TagPattern** (default `DefaultTagPattern`, `v{version}`),
**Subfolder** (the optional folder inside the repository) and **GitPackageId** (the package name; empty means the
alias, shown as the placeholder `GitPackageIdPlaceholder`) are the editable values, each with a live error
(**TagPatternError**, **SubfolderError**, **GitPackageIdError**) that stays `null` until the user has typed in the
box. The repository address is the shared **Location** with **LocationLabel** "Repository URL", an example text
(**GitLocationExamplesText**: `https://host/org/repo.git`, `git@host:org/repo.git` / `ssh://git@host/org/repo.git`
and a local or UNC path) and **LocationHelp** that a password or token must not be in it. Help texts are constants
the view binds: **TagPatternHelpText** (what `{version}` stands for, the examples `v{version}`,
`release/V{version}` and `ver{version}`, and that the fixed parts are case-sensitive), **SubfolderHelpText**,
**GitPackageIdHelpText** and **GitSignInHelpText** (AgentControl uses the user's own credential helper or SSH keys,
never stores credentials, Test connection and Select Package may show Git's sign-in, background checks never prompt
and the card says "Sign-in needed" instead, and SSH needs a key that works without a prompt and a known host).
**TagPreviewItems** (the matching tags, each shown as `version   (tag name)`, newest first, at most ten),
**TagPreviewSummary** (for example "2 of 4 tags match the pattern.", with "Showing the newest 10." and the number of
other tags when they apply) and **HasTagPreview** hold the preview of the last test.

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
one already listed (`AgentControl-SourceEditorViewModel-PackageIdMalformed`,
`AgentControl-SourceEditorViewModel-PackageIdDuplicate`). A NuGet source needs at least one
identifier, with a server-specific message (`AgentControl-SourceEditorViewModel-PackageIdRequired`,
`AgentControl-SourceEditorViewModel-PackageIdRequiredServerMessage`). Each rule has its own requirement:
`-AliasRules`, `-FolderLocation`, `-NoEmbeddedCredential`, `-CredentialNeverEchoed`, `-ServerAddressIndexJson`,
`-InsecureHttpAddress` and `-NoErrorsUntilTouched`; `AgentControl-SourceEditorViewModel-Validation` is the
requirement that `CanSave` follows all of them.

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
A result that is showing is dropped as soon as the address, the insecure choice or the package IDs change, so a
stale "Connection OK" never sits beside an edited address
(`AgentControl-SourceEditorViewModel-TestConnectionResultDropped`).
A test that is **still running** when one of those three inputs changes is canceled and its eventual result is
**ignored** - never shown: the input change cancels the running test, and a test that finishes after its inputs
changed applies nothing to the dialog (no result, headline or error). A result therefore never describes inputs that
are no longer in the dialog; after the edit the dialog shows no result and the test can be started again
(`AgentControl-SourceEditorViewModel-TestConnectionStaleResult`). The connection-test behavior is split into the
requirements `-TestConnectionAvailability`, `-TestConnection`, `-TestConnectionProgress`, `-TestConnectionCancel`,
`-TestConnectionSuccess`, `-TestConnectionPackageNotFound`, `-TestConnectionFailureReasons` and
`-TestConnectionOtherFailures`, each prefixed `AgentControl-SourceEditorViewModel`.

**Git validation and saving** (`SourceEditorViewModel.Git.cs`): For a Git source the address is first judged by
`PackageSourceDefinition.GetCredentialProblem` (a password or token in it has its own message) and then by
`GitRepositoryAddress.GetProblem` (an https, SSH or local-path address); the tag pattern by
`GitTagPattern.GetProblem` (the version placeholder exactly once, usable as a tag name); the folder, after
backslashes are turned into slashes and a leading or trailing slash is dropped, by
`PackageSourceDefinition.GetSubfolderProblem`; and the package name (the typed name, or the alias when empty) by
`PackageSourceDefinition.IsValidGitPackageId`, with a message that asks for a name when the alias cannot be used as
one. These are the same rules `PackageSourceDefinition.GetGitSettingsProblem` applies when settings are saved, so a
source the dialog accepts is never refused later. `CanSave` additionally requires every Git field to be acceptable,
and `CanTestConnection` for a Git source needs the address and those fields but no alias when the package name is
given. The saved definition carries the trimmed address, the trimmed tag pattern, the normalized folder (or none),
the single package name as the only package identifier, and the insecure-HTTP, signature and fingerprint settings
switched off, which do not apply to Git (`AgentControl-SourceEditorViewModel-GitEditor`, `-GitEditorDefaults`,
`-GitSubfolderNormalized`, `-GitSaveNew`, `-GitSaveExisting`, `-GitSaveGate`, `-GitValidation`,
`-GitValidationTagPattern`, `-GitValidationSubfolder`, `-GitValidationPackageName`). The editor also shows the examples
and the case-sensitivity note of the tag pattern and the sign-in note (`-GitTagPatternHelp`, `-GitSignInNote`).

**TestGitConnectionAsync** (private, used by `TestConnectionAsync` for a Git source): Clears the previous result and
preview, builds the definition from the dialog and creates the provider through the factory; by default
(`CreateDefaultProvider`) a Git source gets a `GitSourceProvider` that is interactive (git may show its own sign-in,
because the user started the test) and enforces the minimum git version, while the owner (`MainWindowViewModel`)
supplies one with the configured git executable. It then asks the provider for a tag preview - `IGitTagPreview` when
the provider offers it (no clone: tags are listed with `git ls-remote`), otherwise a preview made from its version
list - and shows it: when at least one tag matches, the preview list and summary and *Connection OK* with the number
of versions and the newest; when none matches, *No matching tags* with the tag names the repository does have (a few,
safe names only) and a hint to check the prefix and the capitalization, or, when the repository has no tags at all,
that it has none yet and how to create one (`AgentControl-SourceEditorViewModel-GitTestConnection`, `-GitTestRuns`,
`-GitTestDefaultProvider`, `-GitTagPreview`, `-GitTagPreviewNoMatch`, `-GitTagPreviewNoTags`).
The outcome of a failure is chosen from `PackageSourceAccessException.Kind` - *Sign-in needed*, *Repository not
found*, *Timed out*, otherwise *Repository unreachable* - with the provider's message; a `GitRemoteException` of kind
`GitTooOld` is *Git is too old* with its message (the minimum supported Git version is required); any other
`InvalidOperationException`
is *Connection failed*; and a cancelled test is the neutral *Test canceled*
(`AgentControl-SourceEditorViewModel-GitTestFailureReasons`, `-GitTestGitTooOld`, `-GitTestOtherFailure`,
`-GitTestCancel`, `-GitTestCancelAndStale`). A result of a test whose inputs
changed (address, tag pattern, folder, package name) is not shown, and any change clears a showing result and the
preview, so a stale "Connection OK" or tag list never sits beside an edited pattern (`-GitTagPreviewCleared`).

**Save** (private, bound to `SaveCommand`): Raises `Saved` with the definition when `CanSave`.

- *Postconditions*: `Saved` carries a copy of the edited source (so any value of it that the
  dialog does not show is preserved) or a new definition, with the trimmed location and - for a
  NuGet source only - the package identifiers, the `RequireSignedPackages` choice and the fingerprint, and - for a
  server only - the insecure-HTTP choice
  (`AgentControl-SourceEditorViewModel-Save`, `-ServerSettingsSaved`, `-SignatureChoiceSaved`,
  `-ZipNeverRequiresSignatures`, each prefixed `AgentControl-SourceEditorViewModel`, with the edit rules
  `-EditKeepsAlias`, `-EditKeepsHiddenSettings`, `-SourceNotModified` and `-SaveBlockedWhenInvalid`). The
  fingerprint is saved in its **normalized** form (64
  upper-case hexadecimal characters), so every reader of the settings sees one spelling, and only while signatures
  are required: a fingerprint typed and then abandoned by unticking the checkbox is not saved
  (`AgentControl-SourceEditorViewModel-FingerprintNormalized`, `-FingerprintNotSavedWhenUnused`). `CanSave` is also
  `false` while the fingerprint in use
  is not readable (`-FingerprintError`). An existing source that requires signatures keeps that setting when it is
  opened on a platform
  that cannot verify (the checkbox is disabled, not cleared), so the user's decision is not silently lost; the
  provider fails closed there (`AgentControl-SourceEditorViewModel-SignatureSettingsKept`).

**RequestForgetSavedSignIn** (private, bound to `ForgetSavedSignInCommand`): Clears the previous result and raises
`ForgetConfirmationRequested`; does nothing when unavailable, which includes an address that is not acceptable for
saving the source (`AgentControl-SourceEditorViewModel-ForgetConfirmation`).

**ForgetSavedSignInAsync**: Called by the view after the user confirmed; does nothing when "Forget saved sign-in"
is not available (an address that is not acceptable for saving the source never reaches the credential helper or
the store). First clears the last connection-test
result (it reported a connection made with the sign-in that is about to go). Asks
`PackageCredentialContext.ForgetSavedSignInAsync` to forget the server's sign-in (the session's copy and the one
saved in the user's credential store) and reports the outcome in words: "The saved sign-in for {server} was
forgotten.", "No saved sign-in was found for {server}.", or - when the credential store could not remove it - that
the credential helper could not remove it and it must be removed with the helper's own tools (for example Windows
Credential Manager). A failure is reported (with a secret-free message), never thrown
(`AgentControl-SourceEditorViewModel-ForgetResultForgotten`, `-ForgetResultNothingSaved`, `-ForgetResultNotRemoved`;
availability: `-ForgetSavedSignIn`, `-ForgetAcceptableAddressOnly`, `-ForgetUnavailableExplained`, `-ForgetChecking`).

#### Error Handling

The constructor throws `ArgumentNullException` for a null alias callback. Invalid input is never
an exception: it is shown as an inline message and keeps `CanSave` false, and `Save` does nothing
when invoked while invalid. A connection test never lets a failure escape: every anticipated failure
(`PackageSourceAccessException`, other `InvalidOperationException`, and for a Git source the `GitTooOld` failure)
becomes a result - a refused signature is
reported as "Signature requirement not met" - and an unexpected one
is reported by the command's error callback as "Connection failed"; a cancelled test is a neutral result, not
an error. Forgetting a saved sign-in reports its outcome as text and never throws. Messages shown never repeat a
credential.

#### Dependencies

- **PackageSourceDefinition**, **PackageSourceType**, **PackageSourceAccessException**,
  **IPackageSourceProvider**, **NuGetSourceProvider**, **SignatureVerificationSupport**
  (`AgentPackageManagement` subsystem) — the
  source data type, its alias, credential, server-address and fingerprint validation and normalization, its
  location kind, the failure causes, the provider the connection test uses, and the platform answer for signature
  verification.
- **GitRepositoryAddress**, **GitTagPattern**, **GitSourceProvider**, **GitSourceOptions**, **IGitTagPreview**,
  **GitTagPreview** (`AgentPackageManagement` subsystem) and **GitRemoteException**, **GitFailureKind**
  (`GitIntegration` subsystem) — the Git address and tag-pattern rules, the provider a Git connection test uses by
  default, the tag preview it produces and the "git too old" failure.
- **PackageCredentialContext**, **ForgetSignInResult**, **FeedOrigin**, **SecretRedactor**
  (`PackageCredentials` subsystem) — forgetting a saved sign-in and secret-free messages.
- **RelayCommand**, **AsyncRelayCommand**, **ConnectionTestResultKind** (`LauncherUI` subsystem, shared
  MVVM support) — back the commands and describe the test outcome.

#### Callers

- **SettingsWindowViewModel** — creates the editor through `CreateEditor` (passing the application's
  `PackageCredentialContext`) and applies the saved definition to its source list.
- **SourceEditorWindow** (code-behind) — closes the window when `Saved` is raised, cancels a running
  test when the window closes, provides the folder picker for the location, and shows the confirmation (through
  `ConfirmationWindow`) that `ForgetConfirmationRequested` asks for before calling `ForgetSavedSignInAsync`. The
  form is long for a server source, so the window limits its own height to the screen's working area, scrolls the
  form (with Save and Cancel kept in view below it) and scrolls newly shown progress, test results and forget
  results into view. The form is a stack of titled sections - Source, Packages (NuGet), Sign-in (server; holds
  "Forget saved sign-in" and **SignInHelpText**), Package signatures (NuGet) and Connection test (server; holds
  "Test connection" and its "Cancel test") - so each action sits beside the thing it affects. For a Git repository
  the window shows Source (the repository URL with its examples), Package, Versions (the tag pattern and its help),
  Location in repo, Sign-in (the note) and Connection test, whose result is followed by the summary and the list of
  matching tags (`SourceTagPreviewPanel`); the NuGet package, signature and server sign-in sections are hidden.
