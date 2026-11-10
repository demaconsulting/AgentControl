### SettingsWindowViewModel

#### Verification Approach

`SettingsWindowViewModel` is verified with unit tests defined in
`SettingsWindowViewModelTests.cs`. No Avalonia `Window` is constructed; tests exercise the
view-model class directly against real `AppSettings` values and an injected folder-exists check
(so no real folders are needed and a check can be made slow or failing on purpose). The server-source
behavior is in `SettingsWindowServerSourceTests.cs`, which also drives the headless Settings and editor
windows; the provider factory that the editor's connection test uses is replaced by a fake there. The Git-source
behavior (the Add menu's fourth item, the row of a Git source, the removal warning and the refusal of unusable Git
settings) is in `SettingsWindowGitSourceTests.cs`, with a folder check that records what it is asked so a remote
repository can be shown never to be asked about. The window is exercised by the headless Avalonia tests in
`DialogWindowTests.cs` and, end to end with the
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
  shown as an "Online source" without any check.
- A NuGet server source can be added and edited through the server editor.
- A Git repository source can be added through the Git editor, is listed with the Git icon and kind name, as an
  "Online source" (without any folder check) when its address is remote and with its folder status when it is a local
  path, is
  saved with its address, tag pattern, folder and package name, and a Git source with unusable settings is refused on
  save with a message.
- Add, edit and remove change the list as documented, and a removal's warning names the repos
  pinned to the source.
- A new source's alias must differ from every configured alias, ignoring case.
- The theme choices are system default, light and dark in that order; the theme starts from the saved choice, the
  system default when none was saved; and the chosen theme reaches the owner's save callback with the other settings.

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
"Online source" - a neutral status with the cloud icon - without the folder check ever running for it. This
scenario is tested by
`SettingsWindowViewModel_Constructor_ListsConfiguredSourcesInOrder`,
`SettingsWindowViewModel_Constructor_NoSources_ReportsHasNoSources`,
`SettingsWindowViewModel_Constructor_FolderCheckSlow_ShowsCheckingThenResultWithoutBlocking`,
`SettingsWindowViewModel_FolderCheck_MissingOrFailing_ShowsMissing`,
`SettingsWindowViewModel_Dispose_CancelsPendingFolderChecks`, and
`SettingsWindowViewModel_SaveServerSource_ListsItAsOnlineSourceAndPersistsIt`, covering
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

**SettingsWindowViewModel_AddGitSource_RequestsTheGitEditorAndListsTheSourceAsOnlineSource**: The Git add command
requests an editor in Git mode for a new source (titled "Add Git Repository"); saving that editor with an https address,
a tag pattern, a folder and a package name lists the source with the Git icon and the kind name "Git repository", as
"Online source" - the folder check is never asked about it - and saving the settings passes the source to the owner with
its address, tag pattern, folder and package name; a Git source on a local path gets the folder status instead (found
or not found). Removing a Git source warns, like any source, by name and by how many repos are pinned to it, and
saving settings that hold a Git source whose tag pattern has no version placeholder, whose address embeds a credential,
whose folder inside the repository leaves the repository or whose package name has characters a name may not have (as
a hand-edited settings file could) is refused, one test per field: the owner's callback is not invoked, the window does
not report saved and the explanation is shown, without repeating the address, the folder or the name that was
rejected.
This scenario is tested by
`SettingsWindowViewModel_AddGitSourceCommand_RequestsGitEditor`,
`SettingsWindowViewModel_SaveGitSource_ListsItAsOnlineSourceAndPersistsIt`,
`SettingsWindowViewModel_SaveGitSource_PersistsAddressPatternFolderAndPackageName`,
`SettingsWindowViewModel_GitSourceWithLocalPath_ShowsFolderStatus`,
`SettingsWindowViewModel_RemoveGitSource_WarnsAboutPinnedRepos`,
`SettingsWindowViewModel_Save_GitSourceWithBadTagPattern_IsRefusedWithErrorMessage`,
`SettingsWindowViewModel_Save_GitSourceWithBadAddress_IsRefusedWithErrorMessage`,
`SettingsWindowViewModel_Save_GitSourceWithBadSubfolder_IsRefusedWithErrorMessage` and
`SettingsWindowViewModel_Save_GitSourceWithBadPackageName_IsRefusedWithErrorMessage`, covering
`AgentControl-SettingsWindowViewModel-AddGitSourceMenu`, `-GitSourceRowStatus`, `-GitSourceLocalFolderStatus`,
`-GitSourcePersisted`, `-RefuseInvalidGitSource` (tag pattern), `-RefuseInvalidGitAddress`,
`-RefuseInvalidGitSubfolder`, `-RefuseInvalidGitPackageName` and, for the
removal warning, `-RemoveSource` (each prefixed `AgentControl-SettingsWindowViewModel`). The window's menu item for
Git (the fourth item, with the Git icon) is shown by `SettingsWindow_AddGitSourceButton_IsInTheAddMenuAndOpensGitEditor`
in the same test file, cited by the `LauncherUI` subsystem requirement `AgentControl-LauncherUI-AddSourceMenu`.

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

**SettingsWindowViewModel_Appearance_OffersSystemLightAndDarkInOrder**: A view model built from settings without a
theme offers system default, light and dark in that order. This scenario is tested by
`SettingsWindowViewModel_Theme_DefaultsToSystemAndIsSavedWithSettings`, covering
`AgentControl-SettingsWindowViewModel-Appearance`.

**SettingsWindowViewModel_Appearance_StartsFromSavedTheme**: A view model built from settings without a theme starts
at the system default, and one built from settings that saved the light theme starts at light. This scenario is
tested by `SettingsWindowViewModel_Theme_DefaultsToSystemAndIsSavedWithSettings`, covering
`AgentControl-SettingsWindowViewModel-AppearanceStartsFromSaved`.

**SettingsWindowViewModel_Appearance_ChosenThemeIsSaved**: After the dark theme is chosen and saved, the owner's
save callback receives settings whose theme is dark. This scenario is tested by
`SettingsWindowViewModel_Theme_DefaultsToSystemAndIsSavedWithSettings`, covering
`AgentControl-SettingsWindowViewModel-AppearanceSaved`.

The picker's friendly theme names belong to the supporting type `ThemePreferenceDisplayNameConverter` and are
checked by `ThemePreferenceDisplayNameConverterTests` at the `LauncherUI` subsystem level.
