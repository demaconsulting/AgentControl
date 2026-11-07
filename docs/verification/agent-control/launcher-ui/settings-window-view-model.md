### SettingsWindowViewModel

#### Verification Approach

`SettingsWindowViewModel` is verified with unit tests defined in
`SettingsWindowViewModelTests.cs`. No Avalonia `Window` is constructed; tests exercise the
view-model class directly against real `AppSettings` values, so no mocking is required.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Editable properties are seeded from the current application settings on construction.
- A null initial settings value is rejected.
- The custom-agent-tool indicator reflects only whether the selected tool is a well-known
  built-in tool.
- Saving invokes the owner's callback with the current values and raises `Saved`.
- `AvailableShellPreferences` is non-empty, has no duplicate entries, starts with a blank
  auto-detect entry, and contains the expected OS-appropriate preset names.
- A package source field that embeds a credential is refused with a message that does not repeat
  it, and nothing is saved.
- Saving writes the package source field as the Default source (never the legacy path), keeps all
  other configured sources unchanged, and removes the Default source when the field is blank.

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
`onSave` callback with the current property values and raises `Saved`. This scenario is
tested by
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved`,
covering `AgentControl-SettingsWindowViewModel-Save`.

**SettingsWindowViewModel_ShellPreferencePresets_NonEmptyBlankFirstNoDuplicatesAndOsAppropriate**:
`AvailableShellPreferences` is checked for being non-empty with a blank first entry, for having
no duplicate entries, and for matching the expected preset names for the OS the test runs on
(`pwsh`/`powershell`/`cmd` on Windows, `bash`/`zsh`/`sh` elsewhere). This scenario is tested by
`SettingsWindowViewModel_AvailableShellPreferences_IsNonEmptyWithBlankFirstEntry`,
`SettingsWindowViewModel_AvailableShellPreferences_NoDuplicateEntries`, and
`SettingsWindowViewModel_AvailableShellPreferences_CurrentOs_ContainsExpectedPresets`, covering
`AgentControl-SettingsWindowViewModel-ShellPreferencePresets`.

**SettingsWindowViewModel_DefaultSource_EditedWhileOtherSourcesPreserved**: Saving with an edited
package source field writes a single `Default` zip source and no legacy path; with a `Default` zip
source and a NuGet source configured, only the `Default` location changes and the NuGet source
(with its package IDs) is carried through while the live settings stay untouched; a blank field
removes `Default` but keeps other sources; a legacy single path in the initial settings seeds the
field. This scenario is tested by
`SettingsWindowViewModel_SaveCommand_Execute_InvokesOnSaveWithCurrentValuesAndRaisesSaved`,
`SettingsWindowViewModel_Save_OtherSourcesConfigured_PreservesThemAndEditsOnlyDefault`,
`SettingsWindowViewModel_Save_BlankPackageSourcePath_RemovesDefaultSourceOnly`, and
`SettingsWindowViewModel_Constructor_LegacyPackageSourcePath_SeedsPackageSourcePathField`,
covering `AgentControl-SettingsWindowViewModel-DefaultSource`.

**SettingsWindowViewModel_Save_CredentialBearingPath_IsRefused**: Saving with a package source
field holding a URL with user information and a sentinel password does not invoke the save
callback or raise `Saved`, sets `HasError`, and the message does not contain the password. This
scenario is tested by
`SettingsWindowViewModel_Save_PackageSourcePathWithEmbeddedCredential_IsRefusedWithErrorMessage`,
covering `AgentControl-SettingsWindowViewModel-Save`.
