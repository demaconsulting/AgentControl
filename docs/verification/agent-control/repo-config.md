## RepoConfig

### Verification Approach

The `RepoConfig` subsystem is verified through its `RepoPinStore` unit's test
suite (`RepoPinStoreTests.cs`), documented in the `RepoPinStore` unit-level verification
design, and - for the content identity a pin records and the schema compatibility between versions of the application -
by `RepoConfigSubsystemTests.cs`, whose tests use the subsystem's public surface the way the application does: one
session saves, a later one loads, against real repo folders. The `RepoPin` data record has no dedicated test file and
is exercised only as a value round-tripped through `RepoPinStore`. All tests use a real temporary
repo directory and a real `.agentcontrol.json` pin file.

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `RepoPinStore` pass with zero failures.
- A repo's pinned package name and exact version survive a save-then-load round trip.
- A repo with no pin file reports no pin rather than throwing or returning a default value.
- A declined AGENTS.md template offer survives a save-then-load round trip, and a legacy pin
  file written before that field existed loads with it defaulted to false.
- The content identity of the applied package (`files-sha256:`, the same for every kind of source) and, for a Git tag,
  the source revision (`git-commit:`) survive from one session to the next as recorded, are never invented for a pin
  that has none, and a pin written before identities existed loads with none and adopts the identity of the next
  package applied, keeping its other properties. A schema 3 pin's identity of a container is dropped (never compared),
  and its Git commit becomes the source revision.
- A pin file of a newer schema version than the application writes is never interpreted or overwritten, loading it and
  saving over it both fail with advice to update, and the file is left byte for byte unchanged.

### Test Scenarios

**RepoConfig_PinPersistence_UnitTestsCoverRoundTripAndNoPinFile**: Pin persistence is
verified by the `RepoPinStore` unit tests (see the `RepoPinStore` unit verification design),
with `RepoPinStore_SaveThenLoad_RoundTripsAllFields` and
`RepoPinStore_Load_NoPinFile_ReturnsNull` cited directly at the subsystem level, covering
`AgentControl-RepoConfig-PinPersistence` (children: `AgentControl-RepoPinStore-Load`,
`AgentControl-RepoPinStore-Save`).

**RepoConfig_AgentsMdDeclinedPersistence_RoundTripsAndDefaultsForLegacyFiles**: The
`AgentsMdTemplateDeclined` flag is verified by the `RepoPinStore` unit tests, with
`RepoPinStore_SaveThenLoad_AgentsMdTemplateDeclinedTrue_RoundTrips` confirming a `true` value
survives a save-then-load round trip, and
`RepoPinStore_Load_LegacyPinFileMissingAgentsMdTemplateDeclinedField_DefaultsToFalse`
confirming a hand-written legacy pin file (written without this field) loads with it
defaulted to `false` rather than throwing, covering
`AgentControl-RepoConfig-AgentsMdDeclinedPersistence`.

**RepoConfig_SourcePin_RecordsAliasMapsLegacyPreservesFieldsAndRejectsNewerSchema**: The pin's
source alias survives a save-then-load round trip; a pin written before sources existed loads as
a pin to `Default` (a pin of schema 2 or later that names no source is rejected rather than mapped);
unknown properties survive a rewrite; and a pin from a newer schema is
rejected without being interpreted or overwritten. These are verified by the `RepoPinStore` unit
tests, with `RepoPinStore_SaveThenLoad_SourceAlias_WritesSchemaVersion4AndRoundTripsSource`,
`RepoPinStore_Load_LegacyV1PinFile_MapsToDefaultSource`,
`RepoPinStore_Load_CurrentSchemaPinWithoutSource_IsRejectedInsteadOfMappedToDefault`,
`RepoPinStore_Save_UnknownProperties_ArePreserved`, and
`RepoPinStore_Load_NewerSchemaVersion_ThrowsUnsupportedPinSchemaExceptionWithActionableMessage`
cited directly at the subsystem level, covering `AgentControl-RepoConfig-SourcePin` (children:
`AgentControl-RepoPinStore-SourceAlias`, `AgentControl-RepoPinStore-LegacyV1`,
`AgentControl-RepoPinStore-RejectUnnamedSource`,
`AgentControl-RepoPinStore-PreserveUnknownFields`, `AgentControl-RepoPinStore-RejectNewerSchema`).

**RepoConfig_ContentIdentityPin_SurvivesSessionsMigratesOlderPinsAndIsAdoptedOnTheNextApply**: Through the subsystem's
public surface (`RepoPinStore` with `RepoPin`) against real repo folders, one session saves a pin that records a
`files-sha256:` identity with a `git-commit:` source revision and one that records none, and a later session loads
them: each value comes back as recorded, the pin without them has none and no `ContentIdentity` property is in its
file. Pin files as earlier versions wrote them - schema 2 with the other known properties, schema 3 with the `sha256:`
identity of a container and schema 3 with a `git-commit:` identity - load with no content identity (the container
identity is dropped, never carried as a files identity) and the Git commit as the source revision where there was
one; the schema 2 and the schema 3 pin then adopt the identity of the next package applied - the file is rewritten
in the current schema version and the source, version and declined-template flag are kept. This scenario is tested by
`RepoConfig_ContentIdentityPin_SurvivesSessionsMigratesOlderPinsAndIsAdoptedOnTheNextApply` in
`RepoConfigSubsystemTests.cs`, covering `AgentControl-RepoConfig-ContentIdentityPin` (child:
`AgentControl-RepoPinStore-SaveContentIdentity`), `AgentControl-RepoConfig-LegacyPinWithoutIdentity` (child:
`AgentControl-RepoPinStore-LoadWithoutIdentity`) and `AgentControl-RepoConfig-LegacyIdentityMigration` (child:
`AgentControl-RepoPinStore-MigrateLegacyIdentity`). The unit-level tests of the same rules are
`RepoPinStore_SaveThenLoad_ContentIdentityAndSourceRevision_RoundTripAndAreOmittedWhenAbsent`,
`RepoPinStore_Load_PinWithoutContentIdentity_LoadsWithNullIdentityAndUpgradesToSchema4OnSave`,
`RepoPinStore_Load_Schema3PinWithContainerIdentity_DropsIdentityAndRewritesAsSchema4` and
`RepoPinStore_Load_Schema3PinWithGitCommitIdentity_CarriesItAsSourceRevision`.

**RepoConfig_PinSchemaVersion_NewerSchemaIsRefusedWithAdviceAndNeverOverwritten**: A pin file written by a newer
version of the application (whose identity property is an object, not text) is never interpreted or overwritten by this
version: loading throws an `UnsupportedPinSchemaException` that names the version that is supported and says to update
the application, saving a pin over it throws the same and leaves the file byte for byte unchanged with no temporary file
beside it; once the user replaces the file by a pin of the schema this version writes, it is read normally. The schema
version this version writes is the current one, so an older version refuses its files in the same way. This scenario is
tested by `RepoConfig_NewerSchemaPin_IsRefusedWithAdviceAndNeverOverwritten` in `RepoConfigSubsystemTests.cs`, covering
`AgentControl-RepoConfig-PinSchemaVersion` (child: `AgentControl-RepoPinStore-SchemaVersion4`). The unit-level tests
(including a newer-schema file whose known property has another type, and the schema probe) are those of
`AgentControl-RepoPinStore-RejectNewerSchema`.
