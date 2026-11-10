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
  saving writes the current schema version (4) and the source alias.
- A recorded content identity and source revision round-trip and are omitted when the pin has none; a pin of schema 1
  or 2 loads with no identity and is written back as schema 4; a schema 3 pin loses its container identity (it cannot
  be compared with an identity of files) and keeps a `git-commit:` identity as its source revision; a hand-written
  camelCase identity is read.
- Unknown properties survive a rewrite, with or without the caller having loaded the pin first.
- A pin file from a newer schema is neither interpreted nor overwritten - also when a property this build knows has
  another type in it (an identity that is an object or a number) - and a save replaces the file atomically (no
  temporary file remains). A current-schema file with such a type mismatch is corrupt and is replaced by a save.
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
`RepoPinStore_SaveThenLoad_SourceAlias_WritesSchemaVersion4AndRoundTripsSource`,
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
declares a newer schema (here version 5) throws `UnsupportedPinSchemaException` whose message names the version and
tells the user to update; saving over such a file throws the same exception and leaves the file
byte-for-byte unchanged with no other file created. This scenario is tested by
`RepoPinStore_Load_NewerSchemaVersion_ThrowsUnsupportedPinSchemaExceptionWithActionableMessage`
and `RepoPinStore_Save_ExistingNewerSchemaFile_ThrowsAndLeavesFileUnchanged`, covering
`AgentControl-RepoPinStore-RejectNewerSchema`.

**RepoPinStore_RejectNewerSchema_IncompatibleKnownPropertyType**: A schema 5 file whose `ContentIdentity` is an object
or a number, whose `PackageName` is a list, or whose `AgentsMdTemplateDeclined` is text (and one that spells
`schemaVersion` in lower case) is reported on load as `UnsupportedPinSchemaException` - never as an unreadable file -
and a save over it throws the same exception and leaves the file byte-for-byte unchanged. The schema probe reads a
version given as text or as a number beyond `int` as newer, reports none for a document without a usable version, and
rejects text that is not JSON; a schema 4 file with a mistyped property is corrupt: loading reports it as
unreadable, and saving replaces it. This scenario is tested by
`RepoPinStore_Load_NewerSchemaWithIncompatibleKnownPropertyType_ThrowsUnsupportedPinSchemaException`,
`RepoPinStore_Save_NewerSchemaWithIncompatibleKnownPropertyType_ThrowsAndLeavesFileUnchanged`,
`RepoPinStore_ProbeSchemaVersion_TextOrOversizedOrDifferentlyCasedVersion_IsReadAsNewer`,
`RepoPinStore_ProbeSchemaVersion_NoUsableVersion_ReturnsNullAndInvalidJsonThrows` and
`RepoPinStore_CurrentSchemaWithIncompatibleKnownPropertyType_IsCorruptAndReplacedOnSave`, covering
`AgentControl-RepoPinStore-RejectNewerSchema` and `AgentControl-RepoPinStore-SchemaVersion4`.

**RepoPinStore_ContentIdentity_RoundTripsIsOmittedWhenAbsentAndOlderPinsLoadWithout**: A pin saved with a content
identity (a `files-sha256:` value) and a source revision (a `git-commit:` value) writes them (properties
`ContentIdentity` and `SourceRevision`) and loads them back, a pin saved without them writes no such property,
a hand-written camelCase `contentIdentity` is read, and pin files of schema 1 and 2 load with no identity and are
rewritten as schema 4 when saved. This scenario is tested by
`RepoPinStore_SaveThenLoad_ContentIdentityAndSourceRevision_RoundTripAndAreOmittedWhenAbsent`,
`RepoPinStore_Load_CamelCaseContentIdentity_IsRead` and
`RepoPinStore_Load_PinWithoutContentIdentity_LoadsWithNullIdentityAndUpgradesToSchema4OnSave`, covering
`AgentControl-RepoPinStore-SaveContentIdentity` and `AgentControl-RepoPinStore-LoadWithoutIdentity`.

**RepoPinStore_MigrateLegacyIdentity_Schema3IdentityIsDroppedAndGitCommitBecomesSourceRevision**: A schema 3 pin that
records the `sha256:` identity of a container (zip or `.nupkg`) loads with no content identity and no source revision,
so nothing is ever compared against it; a schema 3 pin that records a `git-commit:` identity loads with no content
identity and that commit as its source revision, so a moved tag stays detectable; either file is rewritten as schema 4
on the next save, keeping the other properties. This scenario is tested by
`RepoPinStore_Load_Schema3PinWithContainerIdentity_DropsIdentityAndRewritesAsSchema4` and
`RepoPinStore_Load_Schema3PinWithGitCommitIdentity_CarriesItAsSourceRevision`, covering
`AgentControl-RepoPinStore-MigrateLegacyIdentity`.

**RepoPinStore_SchemaVersion4_WrittenAndNewerRefused**: Saving writes `"SchemaVersion": 4`, and a pin file of
version 5 is refused on load with a message that names version 5 and the supported version 4. This scenario is
tested by `RepoPinStore_SaveThenLoad_SourceAlias_WritesSchemaVersion4AndRoundTripsSource` and
`RepoPinStore_Load_NewerSchemaVersion_ThrowsUnsupportedPinSchemaExceptionWithActionableMessage`, covering
`AgentControl-RepoPinStore-SchemaVersion4`.
