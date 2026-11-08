## Settings

### Verification Approach

The `Settings` subsystem is verified at two levels. The subsystem tests (`SettingsSubsystemTests.cs`) use the subsystem
the way the application does - through its public surface (`SettingsStore.Load` and `SettingsStore.Save` with the
`AppSettings` and `RecentRepo` values) - by saving in one session and loading in a later one against a real temporary
configuration directory, and are the evidence of the theme and organization requirements. The `SettingsStore` unit's
test suite (`SettingsStoreTests.cs` and `SettingsStoreOrganizingTests.cs`) and the `RepoGroupName` unit's test suite
(`RepoGroupNameTests.cs`) carry the unit requirements and, as before, the persistence and package-source requirements
of the subsystem. The `AppSettings`, `RecentRepo`, `AgentToolKind` and `ThemePreference` data types have no dedicated
test file and are exercised only as values round-tripped through `SettingsStore`; `RepoGroupName` is a unit with its
own verification design. All tests use a real temporary configuration directory on disk.

### Test Environment

N/A - standard test environment; no external services or hardware required. Each test creates and removes its own
temporary configuration directory.

### Acceptance Criteria

- All subsystem tests and all unit tests for `SettingsStore` and `RepoGroupName` pass with zero failures.
- Saved settings survive a save-then-load round trip unchanged.
- A first-ever run with no settings file yet produces usable default settings rather than
  failing.
- The default configuration directory resolves under the user's application-data folder.
- Package sources round-trip with every field, a legacy single package-folder path migrates to a
  `Default` zip source, and no secret-like property is ever persisted.
- Each theme choice is still chosen in a later session; a missing, unknown or wrongly typed theme value reads as the
  system default without failing the load.
- A repo's group and card color and the collapsed sections survive sessions, are stored in the settings file and
  nowhere else, and read as "no group", the automatic color and "nothing collapsed" when missing, null or damaged,
  with group names repaired and unified and an unknown card color never written back.

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

**Settings_Appearance_ChosenThemeSurvivesSessionsAndDamagedValueReadsAsSystemDefault**: Each theme choice (system
default, light, dark) is saved by one session together with another setting and is the theme chosen, and the other
setting is kept, when the next session loads; then settings files whose theme is an unknown name, an out-of-range
number, a boolean, null, an object or an array, or whose theme is missing altogether (a file from before themes
existed), are loaded and read as the system default, still with the setting that follows. This scenario is tested by
`Settings_Appearance_ChosenThemeSurvivesSessionsAndDamagedValueReadsAsSystemDefault`, covering
`AgentControl-Settings-Appearance` (children: `AgentControl-SettingsStore-Theme`,
`AgentControl-SettingsStore-ThemeDefaultsToSystem`, `AgentControl-SettingsStore-ThemeInvalidReadsAsSystem`). Which
theme the application then shows is a LauncherUI concern, verified there and end to end.

**Settings_RepoOrganization_GroupsColorsAndCollapsedSectionsSurviveSessionsInSettingsFileOnly**: A first session saves
three repos (one grouped, colored and favorite, one grouped, one plain) and two collapsed sections; a later session
loads them, changes a group, a card color, the collapsed sections and another setting and saves; a third session
finds the changes and everything that was not changed, and the configuration directory holds the settings file and
nothing else (nothing about groups or colors is written elsewhere). This scenario is tested by
`Settings_RepoOrganization_GroupColorAndCollapsedSectionsSurviveSessionsInSettingsFileOnly`, covering
`AgentControl-Settings-RepoOrganization`.

**Settings_RepoOrganization_DamagedValuesFallBackAndNextSaveWritesCleanFile**: A settings file with a group name of
the wrong type, a card color of the wrong type, an unknown card color, a card color in odd letter case and spacing, a
null color, a group name that is padded or differs only in letter case from another, a built-in section name used as a
group, and a collapsed-sections list holding numbers, null, a repeated and an empty entry loads without failing: the
damaged values read as no group and the automatic color, the group names are repaired and unified, the known color
is spelled canonically, only the clean collapsed entries remain and the values around the damage are kept; saving
what was loaded writes a file that reads back the same and holds no unknown color. This scenario is tested by
`Settings_RepoOrganization_DamagedValuesFallBackAndNextSaveWritesCleanFile`, covering
`AgentControl-Settings-RepoOrganization` (children: `AgentControl-SettingsStore-RepoOrganization`,
`AgentControl-SettingsStore-RepoOrganizationOmitsUnset`, `AgentControl-SettingsStore-RepoOrganizationDefaults`,
`AgentControl-SettingsStore-InvalidGroupOrColorReadsAsNone`,
`AgentControl-SettingsStore-InvalidCollapsedGroupsRepaired`, `AgentControl-SettingsStore-GroupNamesRepaired`,
`AgentControl-SettingsStore-UnknownCardColorIsAutomatic`, and the `AgentControl-RepoGroupName-*` requirements of the
`RepoGroupName` unit, whose own tests are in its unit verification design). The `SettingsStore` unit tests
verify each of these rules separately (`SettingsStoreOrganizingTests.cs`).
