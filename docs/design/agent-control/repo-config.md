## RepoConfig

![RepoConfig Structure](RepoConfigView.svg)

### Overview

The `RepoConfig` subsystem spans `RepoPinStore.cs` (loads/saves a repo's
`.agentcontrol.json` pin file) and the `RepoPin.cs` data record, which is folded into this
subsystem-level description as it has no dedicated test file. It provides the observable
behavior of persisting a repo's pinned package name, version and source alias and whether the
user has declined the AGENTS.md template offer for this repo
(`AgentControl-RepoConfig-PinPersistence`, `AgentControl-RepoConfig-SourcePin`). The subsystem
contains one unit: `RepoPinStore`.

### Interfaces

**RepoPinStore.Load**: Loads a repo's pin file.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns a `RepoPin` when `.agentcontrol.json` exists at the repo root, `null`
  when it does not (`AgentControl-RepoPinStore-Load`). A pin written before pins named a source
  is returned as a pin to the source named `Default`
  (`AgentControl-RepoPinStore-LegacyV1`).
- *Constraints*: Rejects a null repo root path with `ArgumentNullException`, and a pin file
  from a newer schema with `UnsupportedPinSchemaException`
  (`AgentControl-RepoPinStore-RejectNewerSchema`).

**RepoPinStore.Save**: Saves a repo's pin file.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Overwrites any existing `.agentcontrol.json` with the given `RepoPin`'s values
  (`AgentControl-RepoPinStore-Save`), writing the current schema version and the source alias
  (`AgentControl-RepoPinStore-SourceAlias`) and keeping unknown properties of the existing file
  (`AgentControl-RepoPinStore-PreserveUnknownFields`).
- *Constraints*: Rejects a null `RepoPin` value with `ArgumentNullException`, and never
  overwrites a file written by a newer schema (`AgentControl-RepoPinStore-RejectNewerSchema`).

### Design

The `RepoConfig` subsystem contains only the `RepoPinStore` unit; `RepoPin` is a plain,
mutable data record with `SchemaVersion`, `Source`, `PackageName`, `Version`, and
`AgentsMdTemplateDeclined` properties plus a JSON extension-data dictionary that retains
properties this build does not know, serialized as JSON by `RepoPinStore` with no custom
converters. `Source` is the alias of the package source the pin came from, so a pin is only ever
resolved at that source (`AgentControl-System-SourcePinIntegrity`). `AgentsMdTemplateDeclined` is
a `bool` defaulting to `false`, so a legacy pin file written before this field existed
deserializes with it implicitly `false` ("not yet declined") with no migration code required
— plain `System.Text.Json` behavior for a missing property on a value-type field
(`AgentControl-RepoConfig-AgentsMdDeclinedPersistence`). The flag is sticky by design: once
set, nothing in this codebase ever resets it back to `false` for the same repo, so a user who
declines the offer is never re-prompted for that repo again, even if they later delete their
`AGENTS.md` file. Per architecture.md's "Mandatory version
pinning" decision, a `RepoPin` always names an exact package name, version and source —
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
