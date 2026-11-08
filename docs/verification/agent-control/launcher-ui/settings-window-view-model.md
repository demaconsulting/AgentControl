### SettingsWindowViewModel

#### Verification Approach

`SettingsWindowViewModel` is verified with unit tests defined in
`SettingsWindowViewModelTests.cs`. No Avalonia `Window` is constructed; tests exercise the
view-model class directly against real `AppSettings` values and an injected folder-exists check
(so no real folders are needed and a check can be made slow or failing on purpose). The server-source
behavior is in `SettingsWindowServerSourceTests.cs`, which also drives the headless Settings and editor
windows; the provider factory that the editor's connection test uses is replaced by a fake there. The window
is exercised by the headless Avalonia tests in `DialogWindowTests.cs` and, end to end with the
editor dialogs, by the FlaUI tests.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Editable properties and the source list are seeded from the current application settings on
  construction, and a null initial settings value is rejected.
- The custom-agent-tool indicator reflects only whether the selected tool is a well-known
  built-in tool.
- Saving invokes the owner's callback with the current values, including the edited source list,
  and raises `Saved`.
- `AvailableShellPreferences` is non-empty, has no duplicate entries, starts with a blank
  auto-detect entry, and contains the expected OS-appropriate preset names.
- A source location that embeds a credential is refused with a message that does not repeat it,
  and nothing is saved.
- The source list shows each source and a folder status without blocking the caller; a folder
  that cannot be examined shows as missing and pending checks are abandoned on dispose; a server source is
  shown as "Not checked" without any check.
- A NuGet server source can be added and edited through the server editor.
- Add, edit and remove change the list as documented, and a removal's warning names the repos
  pinned to the source.
- A new source's alias must differ from every configured alias, ignoring case.

#### Test Scenarios

**SettingsWindowViewModel_Seed_SeedsFromSettingsAndRejectsNull**: The constructor seeds every
editable property from a fully-populated initial `AppSettings`, and rejects a null initial
settings value with `ArgumentNullException`. This scenario is tested by
`SettingsWindowViewModel_Constructor_SeedsPropertiesFromInitialSettings` and
`SettingsWindowViewModel_Constructor_NullInitial_ThrowsArgumentNullException`, covering
`AgentControl-SettingsWindowViewModel-Seed`.

**SettingsWindowViewModel_CustomAgentTool_ReflectsWellKnownVsCustomSelection**:
`IsCustomAgentToolSelected` is false for a well-known built-in tool, and setting `AgentTool` to
`Custom` toggles it to true. This scenario is tested by
`SettingsWindowViewModel_IsCustomAgentToolSelected_WellKnownTool_ReturnsFalse` and
`SettingsWindowViewModel_AgentTool_SetToCustom_TogglesIsCustomAgentToolSelected`, covering
`AgentControl-SettingsWindowViewModel-CustomAgentTool`.

**SettingsWindowViewModel_Save_InvokesCallbackAndRaisesSaved**: `SaveCommand` invokes the
`onSave` callback with the current property values and raises `Saved`; a location holding a URL
with user information and a sentinel password does not invoke the callback or raise `Saved`,
sets `HasError`, and the message does not contain the password; when the callback throws
`InvalidOperationException` the window stays open with `HasError` set and `Saved` is not raised. This scenario is
tested by
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved`,
`SettingsWindowViewModel_Save_SourceLocationWithEmbeddedCredential_IsRefusedWithErrorMessage` and
`SettingsWindowViewModel_SaveCommand_OnSaveThrowsInvalidOperation_ShowsErrorAndDoesNotRaiseSaved`,
covering `AgentControl-SettingsWindowViewModel-Save`.

**SettingsWindowViewModel_ShellPreferencePresets_NonEmptyBlankFirstNoDuplicatesAndOsAppropriate**:
`AvailableShellPreferences` is checked for being non-empty with a blank first entry, for having
no duplicate entries, and for matching the expected preset names for the OS the test runs on
(`pwsh`/`powershell`/`cmd` on Windows, `bash`/`zsh`/`sh` elsewhere). This scenario is tested by
`SettingsWindowViewModel_AvailableShellPreferences_IsNonEmptyWithBlankFirstEntry`,
`SettingsWindowViewModel_AvailableShellPreferences_NoDuplicateEntries`, and
`SettingsWindowViewModel_AvailableShellPreferences_CurrentOs_ContainsExpectedPresets`, covering
`AgentControl-SettingsWindowViewModel-ShellPreferencePresets`.

**SettingsWindowViewModel_SourceList_ListsSourcesAndChecksFoldersWithoutBlocking**: The configured
sources are listed in order, an empty configuration reports no sources, a slow folder check shows
a "checking" state and then its result while the constructor returns immediately, a missing or
failing check shows as missing, disposing cancels pending checks, and a saved server source is listed as
"Not checked" - with the server icon - without the folder check ever running for it. This scenario is tested by
`SettingsWindowViewModel_Constructor_ListsConfiguredSourcesInOrder`,
`SettingsWindowViewModel_Constructor_NoSources_ReportsHasNoSources`,
`SettingsWindowViewModel_Constructor_FolderCheckSlow_ShowsCheckingThenResultWithoutBlocking`,
`SettingsWindowViewModel_FolderCheck_MissingOrFailing_ShowsMissing`,
`SettingsWindowViewModel_Dispose_CancelsPendingFolderChecks`, and
`SettingsWindowViewModel_SaveServerSource_ListsItAsNotCheckedAndPersistsIt`, covering
`AgentControl-SettingsWindowViewModel-SourceList`.

**SettingsWindowViewModel_AddSource_AddsEachKindOfSource**: The add commands
request an editor for each source kind (including the NuGet server, whose editor is in server mode), and
saving an editor adds and selects a zip or NuGet source, keeping the NuGet package identifiers. This scenario is
tested by
`SettingsWindowViewModel_AddCommands_RaiseEditorRequestedForNewSourceOfEachKind`,
`SettingsWindowViewModel_AddZipSource_EditorSaved_AddsAndSelectsSource`,
`SettingsWindowViewModel_AddNuGetFolderSource_EditorSaved_KeepsPackageIds` and
`SettingsWindowViewModel_AddNuGetServerSourceCommand_RequestsServerEditor`, covering
`AgentControl-SettingsWindowViewModel-AddSource`.

**SettingsWindowViewModel_EditSource_ReplacesTheSourceInPlace**: An edit replaces the source in place keeping its
alias, and editing a listed server opens the server editor whose connection test goes through the supplied provider
factory. This scenario is tested by
`SettingsWindowViewModel_EditSource_EditorSaved_ReplacesSourceInPlaceKeepingAlias` and
`SettingsWindowViewModel_EditServerSource_OpensServerEditorThatTestsThroughTheFactory`, covering
`AgentControl-SettingsWindowViewModel-EditSource`.

**SettingsWindowViewModel_RemoveSource_WarnsAboutPinnedReposBeforeRemoval**: The remove command requests a removal
for the selected source, and a removal's warning names the pinned repos before the source is removed. This scenario
is tested by
`SettingsWindowViewModel_RemoveSourceCommand_RaisesRemoveRequestedForSelectedSource` and
`SettingsWindowViewModel_RemoveSource_WarnsAboutPinnedReposAndRemoves`, covering
`AgentControl-SettingsWindowViewModel-RemoveSource`.

**SettingsWindowViewModel_Save_WritesTheResultingSourcesInOrder**: After additions, an edit and a removal, the
saved settings hold the resulting list in order. This scenario is tested by
`SettingsWindowViewModel_Save_AfterAddEditRemove_WritesResultingSourcesInOrder`, covering
`AgentControl-SettingsWindowViewModel-SaveResultingSources`.

**SettingsWindowViewModel_AliasUnique_DuplicateIgnoringCaseCannotBeSaved**: An editor created for a
new source cannot be saved with an alias already configured under a different letter case. This
scenario is tested by
`SettingsWindowViewModel_AddSource_AliasAlreadyConfiguredIgnoringCase_EditorCannotSave`, covering
`AgentControl-SettingsWindowViewModel-AliasUnique`.
