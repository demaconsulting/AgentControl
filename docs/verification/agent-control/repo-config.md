## RepoConfig

### Verification Approach

The `RepoConfig` subsystem is verified indirectly through its `RepoPinStore` unit's test
suite (`RepoPinStoreTests.cs`), documented in the `RepoPinStore` unit-level verification
design. The `RepoPin` data record has no dedicated test file and is exercised only as a
value round-tripped through `RepoPinStore`'s save/load tests. All tests use a real temporary
repo directory and a real `.agentcontrol.json` pin file.

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `RepoPinStore` pass with zero failures.
- A repo's pinned package name and exact version survive a save-then-load round trip.
- A repo with no pin file reports no pin rather than throwing or returning a default value.
- A declined AGENTS.md template offer survives a save-then-load round trip, and a legacy pin
  file written before that field existed loads with it defaulted to false.

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
a pin to `Default`; unknown properties survive a rewrite; and a pin from a newer schema is
rejected without being interpreted or overwritten. These are verified by the `RepoPinStore` unit
tests, with `RepoPinStore_SaveThenLoad_SourceAlias_WritesSchemaVersion2AndRoundTripsSource`,
`RepoPinStore_Load_LegacyV1PinFile_MapsToDefaultSource`,
`RepoPinStore_Save_UnknownProperties_ArePreserved`, and
`RepoPinStore_Load_NewerSchemaVersion_ThrowsUnsupportedPinSchemaExceptionWithActionableMessage`
cited directly at the subsystem level, covering `AgentControl-RepoConfig-SourcePin` (children:
`AgentControl-RepoPinStore-SourceAlias`, `AgentControl-RepoPinStore-LegacyV1`,
`AgentControl-RepoPinStore-PreserveUnknownFields`, `AgentControl-RepoPinStore-RejectNewerSchema`).
