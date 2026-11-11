### RepoPinStore

![RepoConfig Structure](RepoConfigView.svg)

#### Purpose

`RepoPinStore` reads and writes the per-repo `.agentcontrol.json` pin file at a repo's root,
recording which package and version is currently synced into that repo, and the identity of its files. It records no
package source and no Git commit: sources are per-user configuration, which does not belong in a file under version
control that a team shares. It is the sole
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
`SchemaVersion`, `PackageName`, `Version`, `AgentsMdTemplateDeclined` and `ContentIdentity` (omitted when null),
followed by any unknown properties.

**ContentIdentity**: opaque text to the store, which reads it and writes it back
unchanged. `ContentIdentity` is `files-sha256:<hex>`, the identity of the agent files the package delivered, the
same for the same files whatever kind of source delivered them (for a NuGet source it describes the copy cached on
this machine), so that it alone proves the delivered files were not tampered with. A pin without an identity (written
before identities were recorded, or by a source that
reports none) is valid; it adopts an identity the next time the user deliberately applies a package (Select Package,
Upgrade or Change package), never at launch, which does not write the pin. Schema compatibility: pins of schema 1 to
3 are read (see *Load*) and rewritten as the current schema (4) on the next save; a pin of a newer schema is refused by
`Load` and never overwritten by `Save`, so a build that does not know a newer form can neither misread nor destroy it
(`AgentControl-RepoPinStore-RejectNewerSchema`, `AgentControl-System-IdentityAdoptedOnUpgrade`).

**Migration of schema 3 and older**: a schema 3 pin recorded an identity of the *container* (`sha256:` of a zip or
`.nupkg` file) or, for a Git source, `git-commit:`. Neither can be compared with an identity of the files, so `Load`
drops it: the pin is returned with no `ContentIdentity` - which means "no recorded identity", never "changed", and
nothing to verify a source against. A commit it recorded is dropped with it: no commit is kept anywhere
(`AgentControl-RepoPinStore-MigrateLegacyIdentity`). The files identity is recorded the
next time the user selects, changes or upgrades the package; the launch-time restore never writes the pin.

**No source, no commit, no tag**: the pin records neither the package source (its alias is the user's own
configuration, and a teammate may use any alias and any kind of source for the same files) nor the Git commit or tag
(the tag of a pinned version is derived whenever it is needed, by applying the tag pattern of a configured Git source
as it is configured at that moment to the pinned `Version`). Files are recognized by content: the repo card asks the
user's configured sources which offer the pinned package and version, and the first whose files have the recorded
identity supplies them. A tag that is moved to other files, or a package that is republished with other files,
therefore has another identity and is refused at the point where it would be applied; one that delivers identical
files is irrelevant.

**LegacySource** (`RepoPin`, `[JsonIgnore]`): the alias of the source an older version recorded in the file, kept
only as a read-time hint and never written. `Load` fills it from the file's `Source` property (matched without regard
to letter case, so a hand-edited `source` counts too) and removes that property from the unknown properties so that it
cannot be written back as one; a schema 1 file, written before sources existed, with none gets the legacy alias
`Default`. `RepoCardViewModel` uses it only to choose among the user's own sources for a pin that has no files
identity to verify a source against.

**FirstSchemaWithSource**: `const int` — `2`, the first schema version whose files may carry a `Source`; only an older
pin is hinted with the legacy `Default` source.

**FirstSchemaWithFilesIdentity**: `const int` — `4`, the first schema version whose `ContentIdentity` is computed
from the delivered files; an older pin's identity is dropped by `Load`.

**UnsupportedPinSchemaException** (same file): an `InvalidOperationException` subtype raised
when a pin file declares a newer schema than the build supports; its message names the file and
tells the user to update AgentControl.

#### Key Methods

**Load**: Loads the `RepoPin` for a repo, or `null` if it has no pin file yet.

- *Parameters*: `string repoRoot`.
- *Returns*: `RepoPin?`.
- *Postconditions*: Returns `null` (not an exception) when no pin file exists yet — a
  never-synced repo is an expected, ordinary state (`AgentControl-RepoPinStore-Load`). A `Source` an older file
  names is returned as `LegacySource` (a hint only) and removed, with any `SourceRevision` an earlier schema 4 build
  wrote, from the unknown properties; a file of schema older than 2 (including one with no schema version at all)
  that names none gets the legacy alias `Default` (`AgentControl-RepoPinStore-LegacyV1`). A pin that names no source
  is valid whatever its schema: nothing is rejected for it. A pin of schema 1 or 2 has no `ContentIdentity` and is
  returned as such (`AgentControl-RepoPinStore-LoadWithoutIdentity`); a pin of schema 3 is returned without the
  identity it recorded, whether it was a container hash or a commit (`AgentControl-RepoPinStore-MigrateLegacyIdentity`);
  `ContentIdentity` is matched case-insensitively like every property (`AgentControl-RepoPinStore-SaveContentIdentity`).
  A file declaring a schema version newer than
  `RepoPin.CurrentSchemaVersion` (4) is not interpreted: `UnsupportedPinSchemaException` is thrown
  (`AgentControl-RepoPinStore-RejectNewerSchema`, `AgentControl-RepoPinStore-SchemaVersion4`). The declared
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
  `SchemaVersion` set to the current version (4) (`AgentControl-RepoPinStore-SchemaVersion4`); the content identity is
  written when the pin has one and omitted otherwise
  (`AgentControl-RepoPinStore-SaveContentIdentity`). No source and no commit is ever written: a `Source` or
  `SourceRevision` property (in any letter case) found in the pin's unknown properties or in the existing file is
  removed, so a file an older version wrote stops carrying them the next time it is saved. Before
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
  agent files before launch (to compare the recorded content identity and to find the configured source that delivers
  it), and
  `Save` inside `ApplyPackageAndShowReleaseNotes` after a successful extraction - never from the restore at launch,
  which must not rewrite a file under version control.
- **MainWindowViewModel** — calls `Load` when a repo is added, to cache its pin.
