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
- The location box's tooltip is the whole entered folder or feed address, announces every change of the location
  through a change notification, and is `null` (no tooltip) while the location is empty or white space.
- Saving a valid source raises `Saved` with the entered values (for a server also the insecure-HTTP choice);
  saving an invalid editor raises nothing.
- Editing an existing source keeps its alias and any value of it the dialog does not show.
- The connection test needs a valid address and a package ID, lists the first package ID only, shows progress
  and a Cancel action, reports each outcome in plain words, drops a showing result when the address changes, and
  never shows the result of a test that was still running when the address, the package IDs or the insecure-HTTP
  choice changed.
- The signature options appear for NuGet folder and server sources and not for a zip source, start off, and where
  verification is unsupported the checkbox is disabled with the explanation while an existing requiring source keeps
  its settings; the fingerprint is usable only while signatures are required, is validated and saved normalized (and
  not at all when unused); an unreadable fingerprint blocks saving.
- "Forget saved sign-in" is available only for a server address that would be accepted for saving the source (`https`
  unless insecure HTTP is ticked, ending in `index.json`, no embedded credential) with a credential store and a Git
  credential helper that applies to it - the helper is never asked about any other address - asks for confirmation
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
`AgentControl-SourceEditorViewModel-Validation`, `-AliasRules`, `-FolderLocation`, `-NoEmbeddedCredential`,
`-CredentialNeverEchoed`, `-ServerAddressIndexJson`, `-InsecureHttpAddress`, `-PackageIdRequired`,
`-PackageIdRequiredServerMessage` and `-NoErrorsUntilTouched` (each prefixed `AgentControl-SourceEditorViewModel`; the
mapping of test to requirement is in the requirements file). The null-alias-callback test is defensive and is not cited
by a requirement.

**SourceEditorViewModel_Location_TooltipShowsWholeValueAndIsNullWhenEmpty**: A new editor has no location tooltip;
after a long UNC path is typed the tooltip is that whole path and the change notification for `LocationTooltip` was
raised; after the location is changed to white space only the tooltip is `null` again. This scenario is tested by
`SourceEditorViewModel_Location_TooltipShowsWholeValueAndIsNullWhenEmpty`, covering
`AgentControl-SourceEditorViewModel-LocationTooltip`.

**SourceEditorViewModel_ServerSource_TitleLabelExamplesAndInsecureChoice**: A new server editor has the
server title, the "Feed address" label, the JFrog Artifactory and Azure Artifacts examples, the package ID
section and insecure HTTP off; a folder editor is not a server editor and does not offer the test; a valid
server is saved with its trimmed address, package IDs and insecure choice; editing an existing server keeps it
a server with its settings. This scenario is tested by
`SourceEditorViewModel_Constructor_NewServer_HasServerTitleLabelAndExamples`,
`SourceEditorViewModel_Constructor_NuGetFolder_IsNotAServerAndCannotTest`,
`SourceEditorViewModel_SaveCommand_ValidServer_SavesAddressPackageIdsAndInsecureChoice` and
`SourceEditorViewModel_EditExistingServer_StaysAServerWithItsSettings`, covering
`AgentControl-SourceEditorViewModel-ServerSource`, `-ServerExamples`, `-InsecureHttpChoice`, `-InsecureHttpWarning`
(the last tested by the insecure-HTTP toggle test above), `-ServerSettingsSaved` and `-ServerSettingsEdited`.

**SourceEditorViewModel_TestConnection_ReportsEveryOutcomeInWords**: The test is available only with a valid
address and a package ID (the disabled button's tooltip says what is missing). A successful test lists the
first package ID only, uses a placeholder alias when none was typed, and reports the version count and newest;
a server without the package is "Package not found"; each failure kind - sign-in failed, sign-in canceled,
timed out, server unreachable - gets its own headline with the provider's message; a configuration error
raised when the provider is created and an unexpected failure are both reported in the dialog; cancelling a
running test shows progress naming the package, ends with a neutral "canceled" result, and the test can be run
again; a result is dropped when the address changes. A test that is still running when the address, the package IDs
or the "Allow insecure HTTP" choice change is canceled and its eventual result is never shown: the test holds the
fake provider at the gate, edits the input, releases the provider and observes that no result, title or error appears
and that the dialog can test again. This scenario is tested by
`SourceEditorViewModel_CanTestConnection_NeedsValidAddressAndPackageId`,
`SourceEditorViewModel_TestConnection_ServerAnswers_ReportsVersionCountAndLatest`,
`SourceEditorViewModel_TestConnection_PackageMissing_ReportsPackageNotFound`,
`SourceEditorViewModel_TestConnection_ProviderFails_ReportsHeadlineAndMessage`,
`SourceEditorViewModel_TestConnection_OtherFailures_AreReportedInTheDialog`,
`SourceEditorViewModel_TestConnection_CancelWhileRunning_StopsWithCanceledResult`,
`SourceEditorViewModel_TestConnection_ResultDroppedWhenAddressChanges`,
`SourceEditorViewModel_TestConnection_LocationEditedWhileTesting_StaleResultIsNotShown`,
`SourceEditorViewModel_TestConnection_PackageIdsEditedWhileTesting_StaleResultIsNotShown` and
`SourceEditorViewModel_TestConnection_AllowInsecureHttpChangedWhileTesting_StaleResultIsNotShown`, covering
`AgentControl-SourceEditorViewModel-TestConnection`, `-TestConnectionAvailability`, `-TestConnectionProgress`,
`-TestConnectionCancel`, `-TestConnectionSuccess`, `-TestConnectionPackageNotFound`, `-TestConnectionFailureReasons`,
`-TestConnectionOtherFailures`, `-TestConnectionResultDropped` and `-TestConnectionStaleResult` (each prefixed
`AgentControl-SourceEditorViewModel`).

**SourceEditorViewModel_PackageIds_InvalidOrDuplicateRefusedAndRemovable**: An invalid or
duplicate package identifier cannot be added, and a listed identifier is removed by the remove
command. This scenario is tested by
`SourceEditorViewModel_AddPackageId_InvalidOrDuplicate_IsRefused` and
`SourceEditorViewModel_RemovePackageId_RemovesSelectedId`, covering
`AgentControl-SourceEditorViewModel-PackageIds` (adding, evidenced by the tests that add IDs for save),
`-PackageIdRemove`, `-PackageIdMalformed` and `-PackageIdDuplicate`.

**SourceEditorViewModel_Save_ValidSourceRaisesSavedWithDefinition**: A valid zip source and a
valid NuGet source raise `Saved` with the entered alias, location and identifiers; editing an
existing source keeps its alias and the values the dialog does not show; an invalid editor does
not raise `Saved`. This scenario is tested by
`SourceEditorViewModel_SaveCommand_ValidZipSource_RaisesSavedWithDefinition`,
`SourceEditorViewModel_SaveCommand_ValidNuGetSource_SavesPackageIds`,
`SourceEditorViewModel_EditExistingSource_AliasFixedAndOtherSettingsPreserved`, and
`SourceEditorViewModel_SaveCommand_InvalidEditor_DoesNotRaiseSaved`, covering
`AgentControl-SourceEditorViewModel-Save`, `-EditKeepsAlias`, `-EditKeepsHiddenSettings`, `-SourceNotModified` and
`-SaveBlockedWhenInvalid` (each prefixed `AgentControl-SourceEditorViewModel`).

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
`SourceEditorViewModel_RequireSigned_ProbeFails_CheckboxDisabledWithExplanationAndNoThrow`,
`SourceEditorViewModel_EditExistingRequiringSource_UnsupportedPlatform_KeepsTheSettings`,
`SourceEditorViewModel_Save_ZipNeverRequiresSignaturesAndFolderCan` and
`SourceEditorViewModel_Save_RequireSignedWithoutFingerprint_PinsNothing`, covering
`AgentControl-SourceEditorViewModel-SignatureOptions`, `-SignatureExplanation`, `-SignatureUnsupportedPlatform`,
`-SignatureSettingsKept`, `-SignatureChoiceSaved` and `-ZipNeverRequiresSignatures` (each prefixed
`AgentControl-SourceEditorViewModel`).

**SourceEditorViewModel_SignerFingerprint_ValidatedAndSavedNormalized**: An unreadable fingerprint is reported only
after the user typed it and blocks saving, an empty one is fine and a readable one in any common spelling is
accepted; a fingerprint that is not in use neither blocks the save nor is saved; a source saved with the requirement
and a fingerprint carries it as 64 upper-case hexadecimal characters. This scenario is tested by
`SourceEditorViewModel_Fingerprint_ValidationFollowsTheRules`,
`SourceEditorViewModel_Fingerprint_NotInUse_IsIgnoredAndNotSaved` and
`SourceEditorViewModel_Save_RequireSignedWithFingerprint_SavesTheNormalizedFingerprint`, covering
`AgentControl-SourceEditorViewModel-SignerFingerprint`, `-FingerprintOptional`, `-FingerprintSpellings`,
`-FingerprintHelp`, `-FingerprintError`, `-FingerprintNormalized` and `-FingerprintNotSavedWhenUnused` (each prefixed
`AgentControl-SourceEditorViewModel`). The validation test exercises a colon-separated fingerprint; the dash,
space and lower-case spellings are covered by the same validation rule in `PackageSourceDefinition`, verified in the
`AgentPackageManagement` subsystem.

**SourceEditorViewModel_ForgetSavedSignIn_ConfirmsFirstAndReportsTheOutcome**: The action is available for a server
with an acceptable address and a Git credential helper that applies to it, and not for a folder, an unusable
address, no credential store or a server with no helper configured, with a tooltip that says why; while the check
for a helper is still running the action is unavailable and the tooltip says it is checking, a check that fails counts
as "no helper" without throwing, and the helper is asked once per server origin however the rest of the address is
edited; an address that is not acceptable for saving the source - an `https` address that does not end in
`index.json`, a plain `http` address without the insecure-HTTP opt-in, or an address with embedded credentials - leaves
the action unavailable and the helper is never asked, while ticking "Allow insecure HTTP" makes the `http` address
acceptable and the action becomes available; the command only asks the view to confirm, naming the server, and
nothing is removed until the view calls the
forget method; the forget method reports "nothing saved", "forgotten", a credential store that refuses and a failure
in words, and does nothing when the action is not available. This scenario is tested by
`SourceEditorViewModel_ForgetSavedSignIn_AvailableOnlyForAUsableServerWithACredentialStore`,
`SourceEditorViewModel_ForgetSavedSignIn_CommandAsksForConfirmationAndForgetsOnlyOnceConfirmed`,
`SourceEditorViewModel_ForgetSavedSignIn_ReportsNothingSavedRefusalAndFailureInWords`,
`SourceEditorViewModel_ForgetSavedSignIn_NotAvailable_DoesNothing`,
`SourceEditorViewModel_ForgetSavedSignIn_NoCredentialHelper_UnavailableWithExplanation`,
`SourceEditorViewModel_ForgetSavedSignIn_HelperCheckPendingThenAnswered_FollowsTheAnswer` and
`SourceEditorViewModel_ForgetSavedSignIn_AddressEdits_AskTheHelperOncePerOrigin`,
`SourceEditorViewModel_CanForgetSavedSignIn_AddressNotAnAcceptableFeed_IsFalseAndHelperIsNeverAsked` and
`SourceEditorViewModel_CanForgetSavedSignIn_InsecureHttpOptedIn_BecomesAvailable`, covering
`AgentControl-SourceEditorViewModel-ForgetSavedSignIn`, `-ForgetAcceptableAddressOnly`,
`-ForgetUnavailableExplained`, `-ForgetChecking`, `-ForgetConfirmation`, `-ForgetResultForgotten`,
`-ForgetResultNothingSaved` and `-ForgetResultNotRemoved` (each prefixed `AgentControl-SourceEditorViewModel`).
