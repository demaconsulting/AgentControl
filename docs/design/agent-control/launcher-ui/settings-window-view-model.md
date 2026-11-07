### SettingsWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SettingsWindowViewModel` backs the settings window: it edits a working copy of
`AppSettings`'s user-configurable fields (the package source path - which edits the `Default`
package source -, git executable override, agent tool selection, custom command, shell
preference) and persists them on `Save`. It edits a
copy rather than the live `AppSettings` instance so that closing the window without saving
never partially mutates the caller's settings.

#### Data Model

**PackageSourcePath**: `string?` — The location of the package source named `Default` (the
source a single-folder configuration migrates to), seeded from `AppSettings.GetEffectiveSources`.
Blank removes the `Default` source on save; a non-blank value creates it as a zip source if
absent (`AgentControl-SettingsWindowViewModel-DefaultSource`). This single field is an interim
editor: every other configured source is carried through a save unchanged until a full
multi-source editor exists.

**\_sources**: `List<PackageSourceDefinition>` — private working copies (clones) of the configured
sources taken at construction, so the live settings are never mutated.

**GitExecutablePath**, **CustomAgentCommand**, **ShellPreference**:
`string?` — Editable copies of the corresponding `AppSettings` fields.

**AgentTool**: `AgentToolKind` — The selected agent tool; setting it also raises a change
notification for `IsCustomAgentToolSelected`.

**IsCustomAgentToolSelected**: `bool` (derived) — `true` only when `AgentTool` is
`AgentToolKind.Custom`, driving the custom-command textbox's visibility
(`AgentControl-SettingsWindowViewModel-CustomAgentTool`).

**AvailableAgentTools**: `static IReadOnlyList<AgentToolKind>` — The fixed picker order
(`CopilotCli`, `Cursor`, `ClaudeCode`, `Custom`), per architecture.md's "Configurable agent
tool and shell" decision.

**AvailableShellPreferences**: `static IReadOnlyList<string>` — The OS-conditional
shell/terminal preset choices offered by the picker: `pwsh`, `powershell`, `cmd` on Windows, or
`bash`, `zsh`, `sh` on other platforms, per architecture.md's "Configurable agent tool and
shell" decision. Each list is prefixed with a blank (`string.Empty`) entry representing the
"auto-detect" default, matching the executables `ShellDetector` recognizes for the current OS.
The backing control is an editable `ComboBox`, so a user may still type a custom shell path not
present in this list (`AgentControl-SettingsWindowViewModel-ShellPreferencePresets`).

Notably absent: the recent-repos list is intentionally not exposed here — it is owned
exclusively by `MainWindowViewModel` and is preserved by `MainWindowViewModel.ApplySettings`
when the `AppSettings` built by `Save` is applied.

#### Key Methods

**Constructor**: Seeds editable properties from the current settings.

- *Parameters*: `AppSettings initial`, `Action<AppSettings> onSave`.
- *Preconditions*: Neither parameter is null.
- *Postconditions*: Every editable property equals its corresponding field on `initial`
  (`AgentControl-SettingsWindowViewModel-Seed`).

**Save** (private, bound to `SaveCommand`): Builds an `AppSettings` from the current editable
properties and invokes the `onSave` callback.

- *Parameters*: None.
- *Returns*: `void`.
- *Postconditions*: `onSave` has been invoked with a new `AppSettings` reflecting the current
  property values; `Saved` has been raised (`AgentControl-SettingsWindowViewModel-Save`). The
  configured sources are written to `AppSettings.Sources` (never the legacy path): clones of the
  working sources with the `Default` source updated, created at the front, or removed according to
  `PackageSourcePath`, and all other sources unchanged
  (`AgentControl-SettingsWindowViewModel-DefaultSource`).

The returned `AppSettings.RecentRepos` is left as a fresh empty list; callers (specifically
`MainWindowViewModel.ApplySettings`) are responsible for preserving the authoritative
recent-repos list, since this view model never owns or edits it.

#### Error Handling

The constructor throws `ArgumentNullException` for a null `initial` or `onSave`. `Save`
itself performs no validation and cannot fail; any failure persisting the resulting
`AppSettings` (e.g. `InvalidOperationException` from `SettingsStore.Save`) originates from the
`onSave` callback, not from this view model.

#### Dependencies

- **AppSettings**, **AgentToolKind** (`Settings` subsystem) — the data types this view model
  edits a working copy of.
- **PackageSourceDefinition** (`AgentPackageManagement` subsystem) — the source data type it
  clones and edits.
- **RelayCommand** (`LauncherUI` subsystem, shared MVVM support) — backs `SaveCommand`.

#### Callers

- **MainWindow** (code-behind) — constructs `SettingsWindowViewModel` from
  `MainWindowViewModel.Settings` with `onSave` bound to `MainWindowViewModel.ApplySettings`,
  when the user opens the Settings window.
