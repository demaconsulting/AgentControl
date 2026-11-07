### SourceEditorViewModel

#### Verification Approach

`SourceEditorViewModel` is verified with unit tests defined in `SourceEditorViewModelTests.cs`.
No Avalonia `Window` is constructed; the tests drive the view model's properties and commands
directly and observe its validation messages and `Saved` event, with the alias-exists callback
supplied by the test. The editor window is exercised by the headless Avalonia tests in
`DialogWindowTests.cs` and end to end by the FlaUI tests, which add a zip source and a NuGet
folder source through the dialogs.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A new editor shows no error until the user types, and cannot be saved while empty.
- An invalid alias, an invalid location (including one embedding a credential) and a missing
  NuGet package identifier each show an inline message and block saving.
- A package identifier that is invalid or already listed is refused; a listed one can be removed.
- Saving a valid source raises `Saved` with the entered values; saving an invalid editor raises
  nothing.
- Editing an existing source keeps its alias and any value of it the dialog does not show.

#### Test Scenarios

**SourceEditorViewModel_Validation_InvalidInputShowsInlineMessageAndBlocksSave**: A new editor of
each kind has the matching title and sections; a freshly opened editor cannot be saved but shows
no error; an invalid alias or location reports its problem and blocks saving; a NuGet-package
folder cannot be saved until a package identifier is added; a null alias callback is rejected.
This scenario is tested by
`SourceEditorViewModel_Constructor_NewSourceOfEachKind_HasMatchingTitleAndSections`,
`SourceEditorViewModel_NewEmptyEditor_CannotSaveButShowsNoErrorsYet`,
`SourceEditorViewModel_Alias_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_Location_Invalid_ReportsProblemAndBlocksSave`,
`SourceEditorViewModel_NuGetSource_NoPackageIds_CannotSaveUntilOneIsAdded`, and
`SourceEditorViewModel_Constructor_NullAliasCheck_ThrowsArgumentNullException`, covering
`AgentControl-SourceEditorViewModel-Validation`.

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
