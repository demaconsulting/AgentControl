### SourceEditorViewModel

![LauncherUI Structure](LauncherUIView.svg)

#### Purpose

`SourceEditorViewModel` backs the dialog in which the user adds or edits one package source: a
folder of zip packages or a folder of NuGet packages. It validates what the user types as they
type it, keeps its Save command disabled while anything is invalid, and reports the resulting
`PackageSourceDefinition` to the owner. A single view model serves both kinds of source, since
they differ only in that a NuGet-package folder additionally lists the package identifiers it
offers; a further kind of source can be added to it without changing the settings window.

#### Data Model

**Type**: `PackageSourceType` — The kind of source being edited, fixed at construction (an
existing source keeps its own kind).

**IsNew** / **IsAliasEditable**: `bool` — `true` when a new source is being added. The alias of an
existing source cannot be changed because repos pin to a source by its alias.

**IsNuGet**, **Title**, **LocationHelp**: Derived from `Type` and `IsNew` to title the dialog and
explain what kind of folder to enter.

**Alias**, **Location**: `string` — The editable values. A field shows no error until the user
has typed in it, so a freshly opened dialog is not covered in red.

**PackageIds**: `ObservableCollection<string>` — The package identifiers a NuGet-package folder
offers, with **NewPackageId** (the "add" box) and **SelectedPackageId** (the entry the remove
button removes).

**AliasError**, **LocationError**, **NewPackageIdError**, **PackageIdsError**: `string?` — The
inline messages, `null` when the value is acceptable.

**CanSave**: `bool` (derived) — `true` only when the alias, the location and (for NuGet) the
package identifier list are all acceptable.

#### Key Methods

**Constructor**: Prepares the dialog.

- *Parameters*: The source kind, the source to edit (or `null` for a new one), and a callback
  telling whether an alias is already configured.
- *Preconditions*: The alias callback is not null.
- *Postconditions*: The values are seeded from a copy of the edited source, which is never
  modified; a new editor starts with empty values and `CanSave` false.

**Validation**: The alias must be present, use the characters `PackageSourceDefinition` allows,
and differ from every configured alias ignoring case; the location must be a full folder path,
and one that embeds a credential or is a web address is refused with its own message; a NuGet
package identifier must use the characters a package identifier allows and not repeat one
already listed (`AgentControl-SourceEditorViewModel-Validation`). A NuGet-package folder needs at
least one identifier (`AgentControl-SourceEditorViewModel-PackageIds`).

**AddPackageIdCommand**, **RemovePackageIdCommand**: Add the typed identifier to, or remove the
selected identifier from, `PackageIds`; adding is available only when the typed text is a
valid, new identifier.

**Save** (private, bound to `SaveCommand`): Raises `Saved` with the definition when `CanSave`.

- *Postconditions*: `Saved` carries a copy of the edited source (so any value of it that the
  dialog does not show is preserved) or a new definition, with the trimmed location and - for a
  NuGet-package folder only - the package identifiers
  (`AgentControl-SourceEditorViewModel-Save`).

#### Error Handling

The constructor throws `ArgumentNullException` for a null alias callback. Invalid input is never
an exception: it is shown as an inline message and keeps `CanSave` false, and `Save` does nothing
when invoked while invalid.

#### Dependencies

- **PackageSourceDefinition**, **PackageSourceType** (`AgentPackageManagement` subsystem) — the
  source data type, its alias and credential validation, and its location kind.
- **RelayCommand** (`LauncherUI` subsystem, shared MVVM support) — backs the commands.

#### Callers

- **SettingsWindowViewModel** — creates the editor through `CreateEditor` and applies the saved
  definition to its source list.
- **SourceEditorWindow** (code-behind) — closes the window when `Saved` is raised and provides the
  folder picker for the location.
