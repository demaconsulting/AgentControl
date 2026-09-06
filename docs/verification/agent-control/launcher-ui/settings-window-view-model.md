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
