### RepoPinStore

#### Verification Approach

`RepoPinStore` is verified with unit tests defined in `RepoPinStoreTests.cs`. Every test uses
a real temporary repo directory and a real `.agentcontrol.json` file on disk, so save/load
round-tripping and the no-pin-file case are exercised against genuine filesystem state.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- A pin file without a source (the original schema) loads as a pin to the Default source, and
  saving writes the current schema version and the source alias.
- Unknown properties survive a rewrite, with or without the caller having loaded the pin first.
- A pin file from a newer schema is neither interpreted nor overwritten, and a save replaces the
  file atomically (no temporary file remains).
- A repo with no pin file returns none rather than throwing.
- A saved pin's values are returned unchanged by a subsequent load.
- A null repo root path or a null pin value is rejected.

#### Test Scenarios

**RepoPinStore_Load_NoFileReturnsNullAndRejectsNullRoot**: Loading a repo with no pin file
returns null, and loading with a null repo root path throws `ArgumentNullException`. This
scenario is tested by `RepoPinStore_Load_NoPinFile_ReturnsNull` and
`RepoPinStore_Load_NullRepoRoot_ThrowsArgumentNullException`, covering
`AgentControl-RepoPinStore-Load`.

**RepoPinStore_SaveThenLoad_RoundTripsAllFields**: Saving a pin and then loading it back
returns all fields unchanged. This scenario is tested by
`RepoPinStore_SaveThenLoad_RoundTripsAllFields`, covering both
`AgentControl-RepoPinStore-Load` and `AgentControl-RepoPinStore-Save`.

**RepoPinStore_Save_OverwritesExistingAndRejectsNullPin**: Saving over an existing pin file
overwrites it with the new values, and saving a null pin throws `ArgumentNullException`. This
scenario is tested by `RepoPinStore_Save_ExistingPinFile_OverwritesWithNewValues` and
`RepoPinStore_Save_NullPin_ThrowsArgumentNullException`, covering
`AgentControl-RepoPinStore-Save`.

**RepoPinStore_AgentsMdTemplateDeclinedRoundTripsAndDefaultsForLegacyFiles**: Saving a pin
with the `AgentsMdTemplateDeclined` flag set to `true` and reloading it returns the flag
unchanged, and loading a hand-written legacy pin file that lacks this field entirely returns
it defaulted to `false` rather than throwing. This scenario is tested by
`RepoPinStore_SaveThenLoad_AgentsMdTemplateDeclinedTrue_RoundTrips` and
`RepoPinStore_Load_LegacyPinFileMissingAgentsMdTemplateDeclinedField_DefaultsToFalse`,
covering `AgentControl-RepoPinStore-Load` and `AgentControl-RepoPinStore-Save`.

**RepoPinStore_SourceAlias_WritesSchemaAndSourceAndReadsBack**: Saving a pin writes the current
schema version and the source alias (with version text containing a fourth segment and metadata
preserved) and loading returns them; camelCase property names written by hand are read; saving
over a corrupt file replaces it and leaves no temporary file. This scenario is tested by
`RepoPinStore_SaveThenLoad_SourceAlias_WritesSchemaVersion2AndRoundTripsSource`,
`RepoPinStore_Load_CamelCasePropertyNames_AreRead`, and
`RepoPinStore_Save_CorruptExistingFile_IsReplacedAndLeavesNoTemporaryFile`, covering
`AgentControl-RepoPinStore-SourceAlias`.

**RepoPinStore_LegacyV1_LoadsAsPinToDefaultSource**: A hand-written pin file with no schema
version and no source loads with its source mapped to `Default` and its other fields kept. This
scenario is tested by `RepoPinStore_Load_LegacyV1PinFile_MapsToDefaultSource` and
`RepoPinStore_Load_LegacyPinFileMissingAgentsMdTemplateDeclinedField_DefaultsToFalse`, covering
`AgentControl-RepoPinStore-LegacyV1`.

**RepoPinStore_CurrentSchemaWithoutSource_IsRejected**: A hand-written pin file of the current
schema version that names no source (or only whitespace) is not read as a pin to `Default`;
loading it throws `InvalidOperationException` naming the file. This scenario is tested by
`RepoPinStore_Load_CurrentSchemaPinWithoutSource_IsRejectedInsteadOfMappedToDefault`, covering
`AgentControl-RepoPinStore-RejectUnnamedSource`.
**RepoPinStore_PreserveUnknownFields_SurviveRewrite**: Properties of the existing file that the
tool does not model (including a nested value) are present after a load-modify-save and after a
save of a freshly built pin over the existing file. This scenario is tested by
`RepoPinStore_Save_UnknownProperties_ArePreserved`, covering
`AgentControl-RepoPinStore-PreserveUnknownFields`.

**RepoPinStore_RejectNewerSchema_NeitherInterpretedNorOverwritten**: Loading a pin file that
declares a newer schema throws `UnsupportedPinSchemaException` whose message names the version and
tells the user to update; saving over such a file throws the same exception and leaves the file
byte-for-byte unchanged with no other file created. This scenario is tested by
`RepoPinStore_Load_NewerSchemaVersion_ThrowsUnsupportedPinSchemaExceptionWithActionableMessage`
and `RepoPinStore_Save_ExistingNewerSchemaFile_ThrowsAndLeavesFileUnchanged`, covering
`AgentControl-RepoPinStore-RejectNewerSchema`.
