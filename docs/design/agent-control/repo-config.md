## RepoConfig

![RepoConfig Structure](RepoConfigView.svg)

### Overview

The `RepoConfig` subsystem spans `RepoPinStore.cs` (loads/saves a repo's
`.agentcontrol.json` pin file) and the `RepoPin.cs` data record, which is folded into this
subsystem-level description as it has no dedicated test file. It provides the observable
behavior of persisting a repo's pinned package name and version, the identity of the delivered files, and whether the
user has declined the AGENTS.md template offer for this repo - and nothing about where the files came from: the
pin records no package source and no Git commit, because sources are per-user configuration and the committed file
is shared
(`AgentControl-RepoConfig-PinPersistence`,
`AgentControl-RepoConfig-ContentIdentityPin`, `AgentControl-RepoConfig-LegacyPinWithoutIdentity`,
`AgentControl-RepoConfig-LegacyIdentityMigration`, `AgentControl-RepoConfig-PinSchemaVersion`). The subsystem
contains one unit: `RepoPinStore`.

### Interfaces

**RepoPinStore.Load**: Loads a repo's pin file.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns a `RepoPin` when `.agentcontrol.json` exists at the repo root, `null`
  when it does not (`AgentControl-RepoPinStore-Load`). A `Source` that an older version wrote is returned as
  `RepoPin.LegacySource`, a read-time hint only (a schema 1 pin, written before sources existed, gets the legacy
  alias `Default`; `AgentControl-RepoPinStore-LegacyV1`), and is never kept as an unknown property, so it is never
  written back; a pin written before identities were recorded is returned with no `ContentIdentity`
  (`AgentControl-RepoPinStore-LoadWithoutIdentity`), and so is a pin of schema 3 or older, whose identity of a package
  file (or of a Git commit) cannot be compared with one of the files
  (`AgentControl-RepoPinStore-MigrateLegacyIdentity`).
  A pin of any schema without a source is valid.
- *Constraints*: Rejects a null repo root path with `ArgumentNullException`, and a pin file
  from a newer schema with `UnsupportedPinSchemaException`
  (`AgentControl-RepoPinStore-RejectNewerSchema`).

**RepoPinStore.Save**: Saves a repo's pin file.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Overwrites any existing `.agentcontrol.json` with the given `RepoPin`'s values
  (`AgentControl-RepoPinStore-Save`), writing the current schema version (4)
  (`AgentControl-RepoPinStore-SchemaVersion4`), the content identity when the pin has one
  (`AgentControl-RepoPinStore-SaveContentIdentity`) and keeping unknown
  properties of the existing file (`AgentControl-RepoPinStore-PreserveUnknownFields`) - except a `Source` or
  `SourceRevision` property, in any letter case, which is dropped so that a file an older version wrote stops
  carrying a source or a commit as soon as it is saved again.
- *Constraints*: Rejects a null `RepoPin` value with `ArgumentNullException`, and never
  overwrites a file written by a newer schema (`AgentControl-RepoPinStore-RejectNewerSchema`).

### Design

The `RepoConfig` subsystem contains only the `RepoPinStore` unit; `RepoPin` is a plain,
mutable data record with `SchemaVersion`, `PackageName`, `Version`,
`AgentsMdTemplateDeclined` and `ContentIdentity` properties, a read-only-hint `LegacySource` (never serialized), plus
a JSON extension-data dictionary
that retains
properties this build does not know, serialized as JSON by `RepoPinStore` with no custom
converters. `ContentIdentity` is the fingerprint of the agent files of the package that was applied - computed from
the files themselves, so it is the same whatever kind of source delivered them - written only when known, so that a
later sync of the same version can tell it was republished (or a Git tag moved to other files) and so that each
user's own configured sources can be searched for one that delivers the identical files
(`AgentControl-RepoConfig-ContentIdentityPin`, `AgentControl-System-PackageContentIntegrity`,
`AgentControl-System-PortablePin`). It is always `files-sha256:<hex>`. The store keeps it as opaque text (what
compares as the same content is `PackageContentIdentity`'s rule, not the store's). No commit and no tag is
recorded: the identity alone proves that the delivered files were not tampered with, and a tag is derived from a
source's pattern whenever it is needed. The identity needed a new
schema version (4) rather than an additive field: the store carries properties it does not know forward when it
rewrites a pin, so a build that predates the identity of the files, re-pinning a repo to another version, would keep
the old identity next to the new version and the next build would raise a false "content changed" alarm; a raised
schema version makes such a build refuse the file with the existing "update AgentControl" message instead. Pins of
schema 1 to 3 still load (with no identity: a container hash or a Git commit cannot be compared with one of the
files and is therefore dropped, which means "no recorded identity", never "changed"); all are rewritten as schema 4
when next saved. Schema compatibility
therefore reads: a pin of an older schema is read (a pin without an identity adopts one on the next deliberate
apply - Select Package, Change package or Upgrade - never at launch, which does not write the pin); a pin of the
current schema is read and written; a pin of a newer schema is neither interpreted nor overwritten, whatever the
types of its properties (`AgentControl-RepoPinStore-RejectNewerSchema`, `AgentControl-System-IdentityAdoptedOnUpgrade`).
A pin names no package source (`AgentControl-System-SourcePinIntegrity`): which of the user's own configured sources
supplies the files is decided by `RepoCardViewModel` from what the sources offer and the identity, and remembered in
the user's own settings, never in the pin (`AgentControl-System-PortablePin`). `AgentsMdTemplateDeclined` is
a `bool` defaulting to `false`, so a legacy pin file written before this field existed
deserializes with it implicitly `false` ("not yet declined") with no migration code required
— plain `System.Text.Json` behavior for a missing property on a value-type field
(`AgentControl-RepoConfig-AgentsMdDeclinedPersistence`). The flag is sticky by design: once
set, nothing in this codebase ever resets it back to `false` for the same repo, so a user who
declines the offer is never re-prompted for that repo again, even if they later delete their
`AGENTS.md` file. Per architecture.md's "Mandatory version
pinning" decision, a `RepoPin` always names an exact package name and version —
there is no "latest"/unpinned mode, and a repo with no `.agentcontrol.json` file is
represented as `Load` returning `null` rather than a `RepoPin` with empty fields.

`RepoPinStore` has no dependency on other tool subsystems beyond .NET BCL JSON
serialization; it is called from `RepoCardViewModel` (badge computation, upgrade detection,
ensure-synced-before-launch, and the AGENTS.md template offer/accept/decline flow) and from
the Select-Package/Upgrade flows after a successful package extraction, per
the `RepoSync` subsystem's documented ordering (pin is written only after extraction
succeeds); when rewriting the pin after a new Select-Package/Upgrade extraction,
`RepoCardViewModel` preserves any pre-existing `AgentsMdTemplateDeclined` value rather than
resetting it, since that decision is orthogonal to which package/version is currently pinned.
