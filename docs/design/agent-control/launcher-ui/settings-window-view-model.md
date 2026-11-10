### SettingsWindowViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SettingsWindowViewModel` backs the settings window: it edits a working copy of
`AppSettings`'s user-configurable fields (the list of package sources, git executable override,
agent tool selection, custom command, shell preference) and persists them on `Save`. It edits a
copy rather than the live `AppSettings` instance so that closing the window without saving
never partially mutates the caller's settings.

#### Data Model

**Sources**: `ObservableCollection<PackageSourceItemViewModel>` — One row per configured package
source, built from `AppSettings.GetEffectiveSources` (so a previously configured single
package-source folder appears as the source named `Default`). Each row wraps a private clone of
the `PackageSourceDefinition`, so the live settings are never mutated, and exposes the alias, a
kind icon and label (from `PackageSourceIcons`), the location, the number of repos pinned to it,
and a status text (`AgentControl-SettingsWindowViewModel-SourceList`). A folder row's status is computed
in the background ("Checking", then found or missing). A server row shows the neutral status **"Online source"** (a
cloud icon, no warning or question styling) from the start:
AgentControl contacts a server only when it is needed (selecting a package, checking for upgrades, syncing at launch
or the editor's **Test connection**), never just to draw the list, since doing so could mean a network wait and a
sign-in dialog for a window that was only opened to look. The row's tooltip explains this and points
to *Edit > Test connection*. A Git source is classified by `PackageSourceItemViewModel.IsRemote`: one whose address is
an https or SSH address is remote and behaves exactly like a server row ("Online source", no folder check, the same
tooltip), while one whose address is a local path is a folder like any other and gets the folder status
(`AgentControl-SettingsWindowViewModel-GitSourceRowStatus`, `-GitSourceLocalFolderStatus`). Its icon is the Git logo and
its kind name "Git
repository" (`PackageSourceIcons`). A row also exposes `RequiresSignature` (true only for a NuGet source, folder or
server, with `RequireSignedPackages` on) and `SignatureTooltip`, which the row's "Signed only" badge shows: signed
packages are required and, when a publisher fingerprint is pinned, that only that publisher is accepted, naming the
fingerprint by its start and end only (`NuGetSourceProvider.SummarizeFingerprint`, as the repo card does)
(`AgentControl-LauncherUI-SourceSignedOnlyBadge`).

**SelectedSource**: `PackageSourceItemViewModel?` — The row the Edit and Remove commands act on.

**HasNoSources**: `bool` (derived) — `true` while the list is empty, so the window can show a
hint instead of a blank list.

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

**Theme** / **AvailableThemes**: `ThemePreference` / `static IReadOnlyList<ThemePreference>` — The
Appearance choice. `AvailableThemes` lists system default, light, dark, in that order
(`AgentControl-SettingsWindowViewModel-Appearance`); `Theme` is seeded from the saved setting, the system
default when none was saved (`AgentControl-SettingsWindowViewModel-AppearanceStartsFromSaved`), and is passed to
the owner with the other settings on save (`AgentControl-SettingsWindowViewModel-AppearanceSaved`); the Settings
window shows it in a picker with friendly names, which are produced by the `LauncherUI` supporting type
`ThemePreferenceDisplayNameConverter`, not by this view model.

**AvailableShellPreferences**: `static IReadOnlyList<string>` — The OS-conditional
shell/terminal preset choices offered by the picker: `pwsh`, `powershell`, `cmd` on Windows, or
`bash`, `zsh`, `sh` on other platforms, per architecture.md's "Configurable agent tool and
shell" decision. Each list is prefixed with a blank (`string.Empty`) entry representing the
"auto-detect" default, matching the executables `ShellDetector` recognizes for the current OS.
The backing control is an editable `ComboBox`, so a user may still type a custom shell path not
present in this list (`AgentControl-SettingsWindowViewModel-ShellPreferencePresets`).

**ErrorMessage** / **HasError**: `string?` / `bool` — A message shown in red when a save is
refused.

Notably absent: the recent-repos list is intentionally not exposed here — it is owned
exclusively by `MainWindowViewModel` and is preserved by `MainWindowViewModel.ApplySettings`
when the `AppSettings` built by `Save` is applied. The recent-repos list is only read to count
the repos pinned to each source.

#### Key Methods

**Constructor**: Seeds editable properties from the current settings and starts the status
check of every source.

- *Parameters*: `AppSettings initial`, `Action<AppSettings> onSave`, an optional folder-exists
  check (so tests need no real folders), and an optional provider factory that the editor's
  **Test connection** uses (so the application can give the test the session credentials, and
  tests can use a fake).
- *Preconditions*: `initial` and `onSave` are not null.
- *Postconditions*: Every editable property equals its corresponding field on `initial`, and
  `Sources` lists the configured sources in order
  (`AgentControl-SettingsWindowViewModel-Seed`). Each row shows a "checking" status until its
  background folder check completes; a folder that cannot be examined, or that does not exist,
  shows as missing. The checks run on worker threads, so constructing the view model never
  waits for a slow location.

**AddZipSourceCommand**, **AddNuGetFolderSourceCommand**, **AddNuGetServerSourceCommand**,
**AddGitSourceCommand**, **EditSourceCommand**: Raise
`EditorRequested` asking the window to open a `SourceEditorViewModel`, created by
`CreateEditor` (which passes the provider factory on), for a new source of the chosen kind
(the four items of the window's **Add** menu: zip-package folder, NuGet-package folder, NuGet-package
server, Git repository - one command per menu item, so another source type is one more command and one more
item) or for the
selected source; editing a source whose location is a web address opens the server editor, and editing a Git source
opens the Git editor. `AddGitSourceCommand` requests an editor in Git mode
(`AgentControl-SettingsWindowViewModel-AddGitSourceMenu`). When the editor
reports `Saved`, the resulting definition is appended to `Sources` and selected, or replaces the
edited row in place keeping its position (`AgentControl-SettingsWindowViewModel-AddSource`,
`AgentControl-SettingsWindowViewModel-EditSource`).
The editor's alias check refuses an alias already configured, ignoring case
(`AgentControl-SettingsWindowViewModel-AliasUnique`). Because the editor works on the source's
own definition, any value of that source the dialog does not show is carried through an edit
unchanged.

**RemoveSourceCommand**: Raises `RemoveRequested` for the selected source so the window can ask
for confirmation. `GetRemovalWarning` builds the confirmation text, which, when repos are
pinned to the source, states how many and that they will show a package source problem
afterwards; `RemoveSource` then removes the row.

**Save** (private, bound to `SaveCommand`): Builds an `AppSettings` from the current editable
properties and invokes the `onSave` callback.

- *Parameters*: None.
- *Returns*: `void`.
- *Postconditions*: `onSave` has been invoked with a new `AppSettings` reflecting the current
  property values, whose `Sources` are clones of the current rows in order and whose legacy
  single-path field is empty; `Saved` has been raised
  (`AgentControl-SettingsWindowViewModel-Save`).

The returned `AppSettings.RecentRepos` is left as a fresh empty list; callers (specifically
`MainWindowViewModel.ApplySettings`) are responsible for preserving the authoritative
recent-repos list, since this view model never owns or edits it.

**Dispose**: Cancels the folder checks still running and releases their cancellation source;
the window calls it when it closes (`AgentControl-SettingsWindowViewModel-SourceList`).

#### Error Handling

The constructor throws `ArgumentNullException` for a null `initial` or `onSave`. `Save` checks
every source location with `PackageSourceDefinition.GetCredentialProblem` and every Git source with
`PackageSourceDefinition.GetGitSettingsProblem` (address, tag pattern, folder inside the repository and package
name; this also stops a hand-edited settings file, `AgentControl-SettingsWindowViewModel-RefuseInvalidGitSource`,
`-RefuseInvalidGitAddress`, `-RefuseInvalidGitSubfolder`, `-RefuseInvalidGitPackageName`):
a location embedding a
credential (for example a URL with user information) or a Git source with unusable settings is not
saved - `onSave` is not invoked, `Saved` is not raised, and `ErrorMessage` (shown in red in the
window, and never containing the location) explains why. Any failure persisting the resulting
`AppSettings` (e.g. `InvalidOperationException` from `SettingsStore.Save`) originates from the
`onSave` callback, not from this view model. A failing folder check is not an error: the row
shows the source as missing.

#### Dependencies

- **AppSettings**, **AgentToolKind** (`Settings` subsystem) — the data types this view model
  edits a working copy of.
- **PackageSourceDefinition**, **PackageSourceType** (`AgentPackageManagement` subsystem) — the
  source data type it clones and edits.
- **SourceEditorViewModel**, **PackageSourceItemViewModel**, **PackageSourceIcons** (`LauncherUI`
  subsystem) — the add/edit dialog's view model and the list row support.
- **IPackageSourceProvider** (`AgentPackageManagement` subsystem) — the type of the provider the
  optional factory creates for the connection test.
- **RelayCommand** (`LauncherUI` subsystem, shared MVVM support) — backs the commands.

#### Callers

- **MainWindow** (code-behind) — constructs `SettingsWindowViewModel` from
  `MainWindowViewModel.Settings` with `onSave` bound to `MainWindowViewModel.ApplySettings` and the
  provider factory bound to `MainWindowViewModel.CreateConnectionTestProvider`,
  when the user opens the Settings window.
- **SettingsWindow** (code-behind) — subscribes to `EditorRequested` and `RemoveRequested` to
  show the `SourceEditorWindow` and the removal confirmation.
