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
`SchemaVersion`, `Source`, `PackageName`, `Version`, `AgentsMdTemplateDeclined`, `ContentIdentity` (omitted when
null), followed by any unknown properties.

**FirstSchemaWithSource**: `const int` — `2`, the first schema version whose pins name their source; only an older
pin (no `Source`) is read as a pin to the legacy `Default` source, whatever the current schema is.

**UnsupportedPinSchemaException** (same file): an `InvalidOperationException` subtype raised
when a pin file declares a newer schema than the build supports; its message names the file and
tells the user to update AgentControl.

#### Key Methods

**Load**: Loads the `RepoPin` for a repo, or `null` if it has no pin file yet.

- *Parameters*: `string repoRoot`.
- *Returns*: `RepoPin?`.
- *Postconditions*: Returns `null` (not an exception) when no pin file exists yet — a
  never-synced repo is an expected, ordinary state (`AgentControl-RepoPinStore-Load`). A file
  with a schema version older than 2 (including one with no schema version at all)
  and no source is returned with `Source` set to the legacy alias `Default`
  (`AgentControl-RepoPinStore-LegacyV1`). A file of schema 2 or later that names no source is not
  mapped to anything: `InvalidOperationException` is thrown, so a malformed pin is never
  resolved against a source it did not name (`AgentControl-RepoPinStore-RejectUnnamedSource`).
  A pin of schema 1 or 2 has no `ContentIdentity` and is returned as such
  (`AgentControl-RepoPinStore-LoadWithoutIdentity`); `ContentIdentity` is matched case-insensitively like every
  property (`AgentControl-RepoPinStore-SaveContentIdentity`).
  A file declaring a schema version newer than
  `RepoPin.CurrentSchemaVersion` (3) is not interpreted: `UnsupportedPinSchemaException` is thrown
  (`AgentControl-RepoPinStore-RejectNewerSchema`, `AgentControl-RepoPinStore-SchemaVersion3`). The declared
  `SchemaVersion` is read first, from the raw JSON (`ProbeSchemaVersion`, a `JsonDocument` that looks at that one
  property, matched without regard to letter case and accepted as a number or as text holding a whole number; one too
  large for an `int` counts as newer), before the typed `RepoPin` is deserialized: a newer file may give a property
  this build knows another type (a `ContentIdentity` that is an object, say), and typed reading would fail before the
  schema check and make a valid newer file look corrupt. Only after the probe has found no newer schema is the
  typed read made, and only then can a `JsonException` mean that the file is corrupt.

**Save**: Writes the `RepoPin` for a repo, creating or overwriting `.agentcontrol.json`.

- *Parameters*: `string repoRoot`, `RepoPin pin`.
- *Returns*: `void`.
- *Preconditions*: `repoRoot` must already exist.
- *Postconditions*: The pin file reflects `pin` exactly (`AgentControl-RepoPinStore-Save`), with
  `SchemaVersion` set to the current version (3) and the source alias written
  (`AgentControl-RepoPinStore-SourceAlias`, `AgentControl-RepoPinStore-SchemaVersion3`); the content identity is
  written when the pin has one and omitted otherwise (`AgentControl-RepoPinStore-SaveContentIdentity`). Before
  writing, any existing file is inspected: a
  newer schema - found by the same probe, before any typed reading - aborts the save with
  `UnsupportedPinSchemaException` and leaves the file untouched, whatever the types of its other properties
  (`AgentControl-RepoPinStore-RejectNewerSchema`); properties of the existing file
  that `RepoPin` does not model are merged into `RepoPin.ExtensionData` (the pin's own values
  win) so they survive the rewrite even when the caller built a fresh `RepoPin`
  (`AgentControl-RepoPinStore-PreserveUnknownFields`); an existing file that cannot be parsed is
  treated as corrupt (invalid JSON, or a current-schema file whose known property has another type) and simply replaced.
  The file is written to a uniquely named temporary file
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

- **RepoCardViewModel** — calls `Load` during `RefreshPin`, before applying a package and when restoring missing
  agent files before launch (to compare the recorded content identity), and `Save` inside
  `ApplyPackageAndShowReleaseNotes` after a successful extraction - never from the restore at launch, which must not
  rewrite a file under version control.
- **MainWindowViewModel** — calls `Load` when a repo is added, to cache its pin.
