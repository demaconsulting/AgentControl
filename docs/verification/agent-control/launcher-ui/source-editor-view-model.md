### SourceEditorViewModel

#### Verification Approach

`SourceEditorViewModel` is verified with unit tests defined in `SourceEditorViewModelTests.cs` (folder
sources), `SourceEditorServerViewModelTests.cs` (server sources and the connection test) and
`SourceEditorSignatureViewModelTests.cs` (the signature options and "Forget saved sign-in").
No Avalonia `Window` is constructed; the tests drive the view model's properties and commands
directly and observe its validation messages, test results and `Saved` event, with the alias-exists
callback supplied by the test. The platform answer for signature verification is injected, so both the supported
and the unsupported platform are tested on any machine; the user's credential store is a
`FakeRememberedCredentialStore` inside a real `PackageCredentialContext`, so forgetting a saved sign-in is observed
through what the fake received. The connection test runs against a `FakePackageSourceProvider` supplied
through the provider factory: it can be gated (to hold a test in the "running" state while cancel and the
change-while-running rules are checked), return versions, return none, or throw a
`PackageSourceAccessException` of a given failure kind or another exception, so every outcome of the test is
reached without a network. The editor window is exercised by the headless Avalonia tests in
`DialogWindowTests.cs`, `SettingsWindowServerSourceTests.cs` and `SourceEditorSignatureWindowTests.cs` and end to
end by the FlaUI tests, which add a
zip source, a NuGet folder source and a NuGet server (with its sign-in) through the dialogs.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A new editor shows no error until the user types, and cannot be saved while empty.
- An invalid alias, an invalid location (including one embedding a credential, and for a server any address
  that is not an `https` `index.json` address unless insecure HTTP is ticked) and a missing NuGet package
  identifier each show an inline message and block saving.
- A package identifier that is invalid or already listed is refused; a listed one can be removed.
- Saving a valid source raises `Saved` with the entered values (for a server also the insecure-HTTP choice);
  saving an invalid editor raises nothing.
- Editing an existing source keeps its alias and any value of it the dialog does not show.
- The connection test needs a valid address and a package ID, lists the first package ID only, shows progress
  and a Cancel action, reports each outcome in plain words, and drops its result when the address changes.
- The signature options appear for NuGet folder and server sources and not for a zip source, start off, and where
  verification is unsupported the checkbox is disabled with the explanation while an existing requiring source keeps
  its settings; the fingerprint is usable only while signatures are required, is validated and saved normalized (and
  not at all when unused); an unreadable fingerprint blocks saving.
- "Forget saved sign-in" is available only for a usable server address with a credential store, asks for confirmation
  naming the server before doing anything, forgets only after the view confirms, and reports every outcome in words.

#### Test Scenarios

**SourceEditorViewModel_Validation_InvalidInputShowsInlineMessageAndBlocksSave**: A new editor of
each kind has the matching title and sections; a freshly opened editor cannot be saved but shows
no error; an invalid alias or location reports its problem and blocks saving; a NuGet-package
folder cannot be saved until a package identifier is added; a null alias callback is rejected. For a server,
addresses that are not acceptable (no `index.json`, a query string or fragment, a credential, plain `http`)
show a message that says what to do, ticking "Allow insecure HTTP" makes a plain `http` address acceptable
and shows the warning (clearing the tick brings the error back), and an empty package list shows a server-specific
message.
This scenario is tested by
`SourceEditorViewModel_Constructor_NewSourceOfEachKind_HasMatchingTitleAndSections`,
`SourceEditorViewModel_NewEmptyEditor_CannotSaveButShowsNoErrorsYet`,
`SourceEditorViewModel_Alias_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_Location_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_NuGetSource_NoPackageIds_CannotSaveUntilOneIsAdded`,
`SourceEditorViewModel_Constructor_NullAliasCheck_ThrowsArgumentNullException`,
`SourceEditorViewModel_ServerLocation_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_AllowInsecureHttp_Toggled_AcceptsHttpAndShowsWarning`, and
`SourceEditorViewModel_PackageIds_EmptyForServer_ReportsServerSpecificMessage`, covering
`AgentControl-SourceEditorViewModel-Validation`.

**SourceEditorViewModel_ServerSource_TitleLabelExamplesAndInsecureChoice**: A new server editor has the
server title, the "Feed address" label, the JFrog Artifactory and Azure Artifacts examples, the package ID
section and insecure HTTP off; a folder editor is not a server editor and does not offer the test; a valid
server is saved with its trimmed address, package IDs and insecure choice; editing an existing server keeps it
a server with its settings. This scenario is tested by
`SourceEditorViewModel_Constructor_NewServer_HasServerTitleLabelAndExamples`,
`SourceEditorViewModel_Constructor_NuGetFolder_IsNotAServerAndCannotTest`,
`SourceEditorViewModel_SaveCommand_ValidServer_SavesAddressPackageIdsAndInsecureChoice` and
`SourceEditorViewModel_EditExistingServer_StaysAServerWithItsSettings`, covering
`AgentControl-SourceEditorViewModel-ServerSource`.

**SourceEditorViewModel_TestConnection_ReportsEveryOutcomeInWords**: The test is available only with a valid
address and a package ID (the disabled button's tooltip says what is missing). A successful test lists the
first package ID only, uses a placeholder alias when none was typed, and reports the version count and newest;
a server without the package is "Package not found"; each failure kind - sign-in failed, sign-in canceled,
timed out, server unreachable - gets its own headline with the provider's message; a configuration error
raised when the provider is created and an unexpected failure are both reported in the dialog; cancelling a
running test shows progress naming the package, ends with a neutral "canceled" result, and the test can be run
again; a result is dropped when the address or package IDs change. This scenario is tested by
`SourceEditorViewModel_CanTestConnection_NeedsValidAddressAndPackageId`,
`SourceEditorViewModel_TestConnection_ServerAnswers_ReportsVersionCountAndLatest`,
`SourceEditorViewModel_TestConnection_PackageMissing_ReportsPackageNotFound`,
`SourceEditorViewModel_TestConnection_ProviderFails_ReportsHeadlineAndMessage`,
`SourceEditorViewModel_TestConnection_OtherFailures_AreReportedInTheDialog`,
`SourceEditorViewModel_TestConnection_CancelWhileRunning_StopsWithCanceledResult` and
`SourceEditorViewModel_TestConnection_ResultDroppedWhenAddressChanges`, covering
`AgentControl-SourceEditorViewModel-TestConnection`.

**SourceEditorViewModel_PackageIds_InvalidOrDuplicateRefusedAndRemovable**: An invalid or
duplicate package identifier cannot be added, and a listed identifier is removed by the remove
command. This scenario is tested by
`SourceEditorViewModel_AddPackageId_InvalidOrDuplicate_IsRefused` and
`SourceEditorViewModel_RemovePackageId_RemovesSelectedId`, covering
`AgentControl-SourceEditorViewModel-PackageIds`.

**SourceEditorViewModel_Save_ValidSourceRaisesSavedWithDefinition**: A valid zip source and a
valid NuGet source raise `Saved` with the entered alias, location and identifiers; editing an
existing source keeps its alias and the values the dialog does not show; an invalid editor does
not raise `Saved`. This scenario is tested by
`SourceEditorViewModel_SaveCommand_ValidZipSource_RaisesSavedWithDefinition`,
`SourceEditorViewModel_SaveCommand_ValidNuGetSource_SavesPackageIds`,
`SourceEditorViewModel_EditExistingSource_AliasFixedAndOtherSettingsPreserved`, and
`SourceEditorViewModel_SaveCommand_InvalidEditor_DoesNotRaiseSaved`, covering
`AgentControl-SourceEditorViewModel-Save`.

**SourceEditorViewModel_SignatureOptions_OfferedForNuGetAndDisabledWhereUnsupported**: The signature options are
offered for NuGet folders and servers and not for a zip folder, start unchecked with the fingerprint field unusable;
where verification is supported the checkbox is enabled, explains what it does (including the limit of using it
without a pinned publisher) and ticking it enables the fingerprint field; where it is not supported the checkbox is
disabled with the documented explanation; an existing source that requires signatures, opened where they cannot be
verified, shows the choice disabled and saves it unchanged; a zip source never saves a requirement while a NuGet
folder source can; a source saved with the requirement and no fingerprint pins nothing. This scenario is tested by
`SourceEditorViewModel_SignatureOptions_OfferedForNuGetOnlyAndOffByDefault`,
`SourceEditorViewModel_RequireSigned_SupportedPlatform_CheckboxEnabledAndTickingEnablesTheFingerprint`,
`SourceEditorViewModel_RequireSigned_UnsupportedPlatform_CheckboxDisabledWithExplanation`,
`SourceEditorViewModel_EditExistingRequiringSource_UnsupportedPlatform_KeepsTheSettings`,
`SourceEditorViewModel_Save_ZipNeverRequiresSignaturesAndFolderCan` and
`SourceEditorViewModel_Save_RequireSignedWithoutFingerprint_PinsNothing`, covering
`AgentControl-SourceEditorViewModel-SignatureOptions`.

**SourceEditorViewModel_SignerFingerprint_ValidatedAndSavedNormalized**: An unreadable fingerprint is reported only
after the user typed it and blocks saving, an empty one is fine and a readable one in any common spelling is
accepted; a fingerprint that is not in use neither blocks the save nor is saved; a source saved with the requirement
and a fingerprint carries it as 64 upper-case hexadecimal characters. This scenario is tested by
`SourceEditorViewModel_Fingerprint_ValidationFollowsTheRules`,
`SourceEditorViewModel_Fingerprint_NotInUse_IsIgnoredAndNotSaved` and
`SourceEditorViewModel_Save_RequireSignedWithFingerprint_SavesTheNormalizedFingerprint`, covering
`AgentControl-SourceEditorViewModel-SignerFingerprint`.

**SourceEditorViewModel_ForgetSavedSignIn_ConfirmsFirstAndReportsTheOutcome**: The action is available for a server
with an acceptable address and a Git credential helper that applies to it, and not for a folder, an unusable
address, no credential store or a server with no helper configured, with a tooltip that says why; while the check
for a helper is still running the action is unavailable and the tooltip says it is checking, a check that fails counts
as "no helper" without throwing, and the helper is asked once per server origin however the rest of the address is
edited; the command only asks the view to confirm, naming the server, and nothing is removed until the view calls the
forget method; the forget method reports "nothing saved", "forgotten", a credential store that refuses and a failure
in words, and does nothing when the action is not available. This scenario is tested by
`SourceEditorViewModel_ForgetSavedSignIn_AvailableOnlyForAUsableServerWithACredentialStore`,
`SourceEditorViewModel_ForgetSavedSignIn_CommandAsksForConfirmationAndForgetsOnlyOnceConfirmed`,
`SourceEditorViewModel_ForgetSavedSignIn_ReportsNothingSavedRefusalAndFailureInWords`,
`SourceEditorViewModel_ForgetSavedSignIn_NotAvailable_DoesNothing`,
`SourceEditorViewModel_ForgetSavedSignIn_NoCredentialHelper_UnavailableWithExplanation`,
`SourceEditorViewModel_ForgetSavedSignIn_HelperCheckPendingThenAnswered_FollowsTheAnswer` and
`SourceEditorViewModel_ForgetSavedSignIn_AddressEdits_AskTheHelperOncePerOrigin`, covering
`AgentControl-SourceEditorViewModel-ForgetSavedSignIn`.
