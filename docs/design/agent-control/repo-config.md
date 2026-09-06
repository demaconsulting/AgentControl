## RepoConfig

![RepoConfig Structure](RepoConfigView.svg)

### Overview

The `RepoConfig` subsystem spans `RepoPinStore.cs` (loads/saves a repo's
`.agentcontrol.json` pin file) and the `RepoPin.cs` data record, which is folded into this
subsystem-level description as it has no dedicated test file. It provides the observable
behavior of persisting a repo's pinned package name/version
(`AgentControl-RepoConfig-PinPersistence`). The subsystem contains one unit: `RepoPinStore`.

### Interfaces

**RepoPinStore.Load**: Loads a repo's pin file.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns a `RepoPin` when `.agentcontrol.json` exists at the repo root, `null`
  when it does not (`AgentControl-RepoPinStore-Load`).
- *Constraints*: Rejects a null repo root path with `ArgumentNullException`.

**RepoPinStore.Save**: Saves a repo's pin file.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Overwrites any existing `.agentcontrol.json` with the given `RepoPin`'s values
  (`AgentControl-RepoPinStore-Save`).
- *Constraints*: Rejects a null `RepoPin` value with `ArgumentNullException`.

### Design

The `RepoConfig` subsystem contains only the `RepoPinStore` unit; `RepoPin` is a plain,
mutable data record with `PackageName` and `Version` string properties, serialized as JSON
by `RepoPinStore` with no custom converters. Per architecture.md's "Mandatory version
pinning" decision, a `RepoPin` always names an exact package name and semantic version —
there is no "latest"/unpinned mode, and a repo with no `.agentcontrol.json` file is
represented as `Load` returning `null` rather than a `RepoPin` with empty fields.

`RepoPinStore` has no dependency on other tool subsystems beyond .NET BCL JSON
serialization; it is called from `RepoCardViewModel` (badge computation, upgrade detection,
ensure-synced-before-launch) and from the Select-Package/Upgrade flows after a successful
`PackageZipExtractor.Extract` call, per the `RepoSync` subsystem's documented ordering
(pin is written only after extraction succeeds).
