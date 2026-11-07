### RepoPinStore

![RepoConfig Structure](RepoConfigView.svg)

#### Purpose

`RepoPinStore` reads and writes the per-repo `.agentcontrol.json` pin file at a repo's root,
recording which package, version and source is currently synced into that repo. It is the sole
reader/writer of the pin file; the package extractor never touches it directly, keeping file
extraction and pin bookkeeping as separate concerns per architecture.md.

#### Data Model

`RepoPinStore` holds no instance state; it is a static class.

**PinFileName**: `const string` — `.agentcontrol.json`, always located directly at the repo
root.

**JsonOptions**: `static readonly JsonSerializerOptions` — shared options used by `Load` and
`Save`: `WriteIndented = true` so the on-disk file is human-readable, and
`PropertyNameCaseInsensitive = true` so hand-edited camelCase names are accepted. Property
names are written exactly as declared on `RepoPin` (PascalCase, as pin files always have been):
`SchemaVersion`, `Source`, `PackageName`, `Version`, `AgentsMdTemplateDeclined`, followed by
any unknown properties.

**UnsupportedPinSchemaException** (same file): an `InvalidOperationException` subtype raised
when a pin file declares a newer schema than the build supports; its message names the file and
tells the user to update AgentControl.

#### Key Methods

**Load**: Loads the `RepoPin` for a repo, or `null` if it has no pin file yet.

- *Parameters*: `string repoRoot`.
- *Returns*: `RepoPin?`.
- *Postconditions*: Returns `null` (not an exception) when no pin file exists yet — a
  never-synced repo is an expected, ordinary state (`AgentControl-RepoPinStore-Load`). A file
  with a schema version older than the current one (including one with no schema version at all)
  and no source is returned with `Source` set to the legacy alias `Default`
  (`AgentControl-RepoPinStore-LegacyV1`). A file declaring a schema version newer than
  `RepoPin.CurrentSchemaVersion` is not interpreted: `UnsupportedPinSchemaException` is thrown
  (`AgentControl-RepoPinStore-RejectNewerSchema`).

**Save**: Writes the `RepoPin` for a repo, creating or overwriting `.agentcontrol.json`.

- *Parameters*: `string repoRoot`, `RepoPin pin`.
- *Returns*: `void`.
- *Preconditions*: `repoRoot` must already exist.
- *Postconditions*: The pin file reflects `pin` exactly (`AgentControl-RepoPinStore-Save`), with
  `SchemaVersion` set to the current version and the source alias written
  (`AgentControl-RepoPinStore-SourceAlias`). Before writing, any existing file is inspected: a
  newer schema aborts the save with `UnsupportedPinSchemaException` and leaves the file
  untouched (`AgentControl-RepoPinStore-RejectNewerSchema`); properties of the existing file
  that `RepoPin` does not model are merged into `RepoPin.ExtensionData` (the pin's own values
  win) so they survive the rewrite even when the caller built a fresh `RepoPin`
  (`AgentControl-RepoPinStore-PreserveUnknownFields`); an existing file that cannot be parsed is
  treated as corrupt and simply replaced. The file is written to a uniquely named temporary file
  beside it and moved over the pin file, so a failure never leaves a half-written pin, and a
  leftover temporary file is removed.

#### Error Handling

`Load` and `Save` throw `ArgumentNullException` for a null `repoRoot` (and `Save` also for a
null `pin`). `Load` throws `InvalidOperationException` when the pin file exists but cannot be
read or contains invalid JSON (wrapping `IOException`/`UnauthorizedAccessException`/
`JsonException`), and `UnsupportedPinSchemaException` for a newer schema. `Save` throws
`InvalidOperationException` when the file cannot be written (wrapping
`IOException`/`UnauthorizedAccessException`) and `UnsupportedPinSchemaException` when the
existing file has a newer schema. Callers that must not alter a repo before knowing the pin is
usable (see `RepoCardViewModel`) load the pin first and let `UnsupportedPinSchemaException`
propagate.

#### Dependencies

- **RepoPin** (data type, same subsystem) — the data type read/written, including its schema
  constants and extension-data dictionary.
- **PathHelpers** (`Utilities` subsystem) — `SafePathCombine` resolves the pin file path so a
  malformed repo root cannot escape the intended directory.
- **.NET BCL** — `System.Text.Json`.

#### Callers

- **RepoCardViewModel** — calls `Load` during `RefreshPin` and before applying a package, and
  `Save` inside `ApplyPackageAndShowReleaseNotes` after a successful extraction.
- **MainWindowViewModel** — calls `Load` when a repo is added, to cache its pin.
