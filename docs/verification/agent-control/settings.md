## Settings

### Verification Approach

The `Settings` subsystem is verified indirectly through its `SettingsStore` unit's test suite
(`SettingsStoreTests.cs` and `SettingsStoreOrganizingTests.cs`, with `RepoGroupNameTests.cs` for the group-name
rules), documented in the `SettingsStore` unit-level verification design.
The `AppSettings`, `RecentRepo`, `AgentToolKind` and `ThemePreference` data types have no dedicated test file
and are exercised only as values round-tripped through `SettingsStore`'s save/load tests; `RepoGroupName` is a
supporting rules helper with its own `RepoGroupNameTests.cs`. All
tests use a real temporary configuration directory on disk.

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `SettingsStore` pass with zero failures.
- Saved settings survive a save-then-load round trip unchanged.
- A first-ever run with no settings file yet produces usable default settings rather than
  failing.
- The default configuration directory resolves under the user's application-data folder.
- Package sources round-trip with every field, a legacy single package-folder path migrates to a
  `Default` zip source, and no secret-like property is ever persisted.

### Test Scenarios

**Settings_Persistence_UnitTestsCoverRoundTripDefaultsAndDefaultDirectory**: Settings
persistence is verified by the `SettingsStore` unit tests (see the `SettingsStore` unit
verification design), with `SettingsStore_SaveThenLoad_RoundTripsAllFields` and
`SettingsStore_Load_NoSettingsFile_ReturnsDefaults` cited directly at the subsystem level,
covering `AgentControl-Settings-Persistence` (children: `AgentControl-SettingsStore-Load`,
`AgentControl-SettingsStore-Save`, `AgentControl-SettingsStore-DefaultDirectory`).

**Settings_PackageSources_RoundTripMigrateAndPersistNoSecrets**: A fully populated source list
survives a save-then-load round trip; a settings file with only the legacy single package-folder
path loads with it as the `Default` zip source; and the persisted JSON of a fully populated source
contains no property named like a secret. These are verified by the `SettingsStore` unit tests,
with `SettingsStore_SaveThenLoad_RoundTripsAllFields`,
`SettingsStore_Load_LegacyPackageSourcePath_MigratesToDefaultZipSource`, and
`SettingsStore_Save_FullyPopulatedSources_JsonContainsNoSecretLikeProperties` cited directly at
the subsystem level, covering `AgentControl-Settings-PackageSources` (children:
`AgentControl-SettingsStore-SourcesRoundTrip`, `AgentControl-SettingsStore-LegacySourceMigration`,
`AgentControl-SettingsStore-NoSecretsPersisted`, `AgentControl-SettingsStore-AtomicSave`).

**Settings_RepoOrganization_GroupsColorsAndCollapsedSectionsArePersonalAndTolerant**: A repo's group, a repo's
card color and the collapsed section names survive a save and a load; a file without them, or with them
missing, null or wrongly typed, loads with no group, the automatic color and nothing collapsed; and stored
group names are repaired. These are verified by the `SettingsStore` unit tests, with
`SettingsStore_SaveThenLoad_GroupColorAndCollapsedGroups_RoundTrip`,
`SettingsStore_Load_FileWithoutOrganizingValues_UsesDefaults`,
`SettingsStore_Load_InvalidGroupOrColorType_ReadsAsNone`,
`SettingsStore_Load_InvalidCollapsedGroups_KeepsOnlyCleanStrings` and
`SettingsStore_Load_GroupNames_AreRepairedAndUnified` cited directly at the subsystem level, covering
`AgentControl-Settings-RepoOrganization` (child: `AgentControl-SettingsStore-RepoOrganization`).
