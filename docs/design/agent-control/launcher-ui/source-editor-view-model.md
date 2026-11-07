### SourceEditorViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SourceEditorViewModel` backs the dialog in which the user adds or edits one package source: a
folder of zip packages, a folder of NuGet packages or a NuGet package server. It validates what the
user types as they type it, keeps its Save command disabled while anything is invalid, and reports the
resulting `PackageSourceDefinition` to the owner. A single view model serves all three kinds of
source, since they differ only in details: a NuGet-package source additionally lists the package
identifiers it offers, and a server is addressed by a web address rather than a folder, carries an
"Allow insecure HTTP" choice and can be tested with **Test connection** before it is saved. A NuGet-package source
(folder or server) can also require signed packages, optionally pinned to one publisher's certificate; a server
source can forget the sign-in saved for it. The signature and forget behavior satisfy
`AgentControl-SourceEditorViewModel-SignatureOptions`, `AgentControl-SourceEditorViewModel-SignerFingerprint` and
`AgentControl-SourceEditorViewModel-ForgetSavedSignIn`, and provide `AgentControl-LauncherUI-SignatureOptions` and
`AgentControl-LauncherUI-ForgetSavedSignIn`.

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

**CanForgetSavedSignIn**, **ForgetSavedSignInCommand**, **ForgetTooltip**, **ForgetResultText**,
**HasForgetResult**, **ForgetConfirmationRequested**: "Forget saved sign-in". Available only for a server source
with an acceptable address and only when the `PackageCredentialContext` has a user's credential store to ask
(otherwise the button is shown disabled with a tooltip that says so). The command does **not** forget anything: it
raises `ForgetConfirmationRequested` with a question that names the server and says what is removed, and the view
calls `ForgetSavedSignInAsync` only after the user agrees. A confirmation comes first because the action removes a
secret the user may not be able to recreate from memory, and an accidental click must not do it.

**_credentials**: `PackageCredentialContext?` — the credential facilities used to forget a saved sign-in, and
`_signaturesSupported` — the platform answer, injectable so tests can exercise both.

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
  NuGet source only - the package identifiers, the `RequireSignedPackages` choice and the fingerprint, and - for a
  server only - the insecure-HTTP choice
  (`AgentControl-SourceEditorViewModel-Save`, `AgentControl-SourceEditorViewModel-ServerSource`,
  `AgentControl-SourceEditorViewModel-SignatureOptions`). The fingerprint is saved in its **normalized** form (64
  upper-case hexadecimal characters), so every reader of the settings sees one spelling, and only while signatures
  are required: a fingerprint typed and then abandoned by unticking the checkbox is not saved
  (`AgentControl-SourceEditorViewModel-SignerFingerprint`). `CanSave` is also `false` while the fingerprint in use
  is not readable. An existing source that requires signatures keeps that setting when it is opened on a platform
  that cannot verify (the checkbox is disabled, not cleared), so the user's decision is not silently lost; the
  provider fails closed there.

**RequestForgetSavedSignIn** (private, bound to `ForgetSavedSignInCommand`): Clears the previous result and raises
`ForgetConfirmationRequested`; does nothing when unavailable.

**ForgetSavedSignInAsync**: Called by the view after the user confirmed. First clears the last connection-test
result (it reported a connection made with the sign-in that is about to go). Asks
`PackageCredentialContext.ForgetSavedSignInAsync` to forget the server's sign-in (the session's copy and the one
saved in the user's credential store) and reports the outcome in words: "The saved sign-in for {server} was
forgotten.", "No saved sign-in was found for {server}.", or - when the credential store could not remove it - that
the credential helper could not remove it and it must be removed with the helper's own tools (for example Windows
Credential Manager). A failure is reported (with a secret-free message), never thrown
(`AgentControl-SourceEditorViewModel-ForgetSavedSignIn`).

#### Error Handling

The constructor throws `ArgumentNullException` for a null alias callback. Invalid input is never
an exception: it is shown as an inline message and keeps `CanSave` false, and `Save` does nothing
when invoked while invalid. A connection test never lets a failure escape: every anticipated failure
(`PackageSourceAccessException`, other `InvalidOperationException`) becomes a result - a refused signature is
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
  results into view.
