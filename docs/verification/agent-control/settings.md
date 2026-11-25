## Settings

### Verification Approach

The `Settings` subsystem is verified at two levels. The subsystem tests (`SettingsSubsystemTests.cs`) use the subsystem
the way the application does - through its public surface (`SettingsStore.Load` and `SettingsStore.Save` with the
`AppSettings` and `RecentRepo` values, and `SettingsWriter` over `SettingsStore.Save` the way the main window writes)

- by saving in one session and loading in a later one against a real temporary configuration directory, and are the
evidence of the theme, organization and background-writing requirements. The `SettingsStore` unit's
test suite (`SettingsStoreTests.cs`, `SettingsStoreOrganizingTests.cs`, `SettingsStoreGroupHierarchyTests.cs` and
`SettingsStoreRepoOrderTests.cs`), the `RepoGroupName` unit's test suite
(`RepoGroupNameTests.cs`), the `RepoGroupHierarchy` unit's test suite (`RepoGroupHierarchyTests.cs`) and the
`SettingsWriter` unit's test suite (`SettingsWriterTests.cs`) carry the unit
requirements and, as before, the persistence and package-source requirements
of the subsystem. The `AppSettings`, `RecentRepo`, `AgentToolKind` and `ThemePreference` data types have no dedicated
test file and are exercised only as values round-tripped through `SettingsStore`; `RepoGroupName` and
`RepoGroupHierarchy` are units with their own verification designs. All tests use a real temporary configuration
directory on disk.

### Test Environment

N/A - standard test environment; no external services or hardware required. Each test creates and removes its own
temporary configuration directory.

### Acceptance Criteria

- All subsystem tests and all unit tests for `SettingsStore`, `SettingsWriter`, `RepoGroupName` and
  `RepoGroupHierarchy` pass with zero failures.
- The hierarchy of nested groups survives a later session, a group that takes part in it survives with no repo in it, a
  file from before groups nested reads as a flat list, and a damaged hierarchy is repaired without failing the load.
- Changes handed to the background writer on a thread guarded against blocking calls are written once, with the newest
  state, off that thread and read by the next session; a direct save on that thread is refused.
- Saved settings survive a save-then-load round trip unchanged.
- A first-ever run with no settings file yet produces usable default settings rather than
  failing.
- The default configuration directory resolves under the user's application-data folder.
- Package sources round-trip with every field, a legacy single package-folder path migrates to a
  `Default` zip source, and no secret-like property is ever persisted.
- A Git source (address, tag pattern, sub-folder, single package name, type by name) survives a later session next to
  the other sources, and an address that embeds a credential is refused without repeating it and without changing the
  saved file.
- Each theme choice is still chosen in a later session; a missing, unknown or wrongly typed theme value reads as the
  system default without failing the load.
- A repo's group and card color and the collapsed sections survive sessions, are stored in the settings file and
  nowhere else, and read as "no group", the automatic color and "nothing collapsed" when missing, null or damaged,
  with group names repaired and unified and an unknown card color never written back.
- The user's repo order survives sessions in the settings file only; a file written before the order was stored is
  arranged once as favorites first and then by most recent launch, and a damaged marker reads as not arranged yet.
- The color and the position of each of the user's groups survive sessions in the settings file only, are found by
  group name ignoring letter case, and damaged entries, unknown colors and entries of groups that do not exist (no
  repo names them and they are not registered in the hierarchy) are dropped
  without failing the load and are not written back.

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
`AgentControl-SettingsStore-NoSecretsPersisted`, `AgentControl-SettingsStore-AtomicSave`,
`AgentControl-SettingsStore-LockedFileTolerance`).

**Settings_Appearance_ChosenThemeSurvivesSessionsAndDamagedValueReadsAsSystemDefault**: Each theme choice (system
default, light, dark) is saved by one session together with another setting and is the theme chosen, and the other
setting is kept, when the next session loads; then settings files whose theme is an unknown name, an out-of-range
number, a boolean, null, an object or an array, or whose theme is missing altogether (a file from before themes
existed), are loaded and read as the system default, still with the setting that follows. This scenario is tested by
`Settings_Appearance_ChosenThemeSurvivesSessionsAndDamagedValueReadsAsSystemDefault`, covering
`AgentControl-Settings-Appearance` (children: `AgentControl-SettingsStore-Theme`,
`AgentControl-SettingsStore-ThemeDefaultsToSystem`, `AgentControl-SettingsStore-ThemeInvalidReadsAsSystem`). Which
theme the application then shows is a LauncherUI concern, verified there and end to end.

**Settings_KeepShellOpen_ChosenValueSurvivesSessionsAndMissingOrDamagedValueReadsAsOn**: Both keep-shell-open choices
(off, then on) are saved by one session together with another setting and are the choice made, with the other
setting kept, when the next session loads; then settings files whose value is a string, a number, null, an object or
an array, or missing altogether (a file from before the choice existed), are loaded and read as on, still with the
setting that follows. This scenario is tested by
`Settings_KeepShellOpen_ChosenValueSurvivesSessionsAndMissingOrDamagedValueReadsAsOn`, covering
`AgentControl-Settings-KeepShellOpen` (children: `AgentControl-SettingsStore-KeepShellOpen`,
`AgentControl-SettingsStore-KeepShellOpenDefaultsToOn`, `AgentControl-SettingsStore-KeepShellOpenInvalidReadsAsOn`).

**Settings_RepoOrganization_GroupColorAndCollapsedSectionsSurviveSessionsInSettingsFileOnly**: A first session saves
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

**Settings_GitSource_SurvivesSessionsAndAddressWithCredentialIsRefusedWithoutChangingTheFile**: Through the subsystem's
public surface (`SettingsStore.Save` and `SettingsStore.Load` with `AppSettings` and the Git source description of the
`AgentPackageManagement` subsystem) against a real configuration folder, one session saves a Git source - its address,
tag pattern, sub-folder and single package name, next to a zip source and another setting - and a later session loads
it: the Git source comes back whole, in its place in the list, with the type written by name and no secret-like word in
the file, and the other source and setting are kept; the later session then tries to keep an address that embeds a
password, which is refused without repeating the secret, and the file the earlier session wrote and the folder's
contents are unchanged. The Git source description itself - what it holds, that it has exactly one package ID, and the
rules of the address - is the `AgentPackageManagement` subsystem's (see its design and verification design); the store
persists what it is given and refuses a credential in the address. This scenario is tested by
`Settings_GitSource_SurvivesSessionsAndAddressWithCredentialIsRefusedWithoutChangingTheFile` in
`SettingsSubsystemTests.cs`, covering `AgentControl-Settings-GitSourcePersistence` (children:
`AgentControl-SettingsStore-GitSourceRoundTrip` and `AgentControl-SettingsStore-GitSourceRefusesCredential`, whose unit
tests are `SettingsStore_SaveThenLoad_GitSource_RoundTripsByNameWithoutSecretLikeProperties` and
`SettingsStore_Save_GitSourceAddressWithCredential_IsRefusedWithoutChangingTheFile`).

**SettingsSubsystem_RepoOrder_SurvivesSessionsAndSeedsALegacyFileOnce**: A settings file as an older version wrote it
(the repos in the order they were added, no repo-order marker) is loaded by a first session, which finds it arranged as
the favorite first and then by most recent launch; the user then swaps two repos and saves; a later session sees the
swapped order although it changes a launch time and saves again, and the configuration directory holds the settings file
and nothing else. A file whose marker has the wrong type is loaded and arranged once without failing. This scenario is
tested by `SettingsSubsystem_RepoOrder_SurvivesSessionsAndSeedsALegacyFileOnce`, covering
`AgentControl-Settings-RepoOrder` (children: `AgentControl-SettingsStore-RepoOrder`,
`AgentControl-SettingsStore-RepoOrderLegacySeed` and `AgentControl-SettingsStore-RepoOrderMarkerRepaired`, whose unit
tests are in `SettingsStoreRepoOrderTests.cs`).

**SettingsSubsystem_GroupOrganization_ColorAndOrderSurviveSessionsAndDamagedOrStaleEntriesAreDropped**: A first session
saves three repos (one in *Client Work*, one in *Home* that is also a favorite, one without a group) with a color for
each group and the order *Home* then *Client Work*; a later session loads them and finds each color by the group's name
in another letter case and the order unchanged, and the configuration directory holds the settings file and nothing
else. Then a damaged file - a color under a group name in another letter case and spelled in lower case, a color of a
group that does not exist (no repo names it and it is not registered in the hierarchy), a color for the built-in
*Ungrouped* section, a color that is a number, and an order holding a stale group, a number and the group in another
letter case - is loaded without failing: only the usable color and position are kept, spelled the way the group is
spelled on its repo, the setting after the damage is kept, and saving what was loaded does not write the stale group
back. This scenario is tested by
`SettingsSubsystem_GroupOrganization_ColorAndOrderSurviveSessionsAndDamagedOrStaleEntriesAreDropped`, covering
`AgentControl-Settings-GroupOrganization` (children: `AgentControl-SettingsStore-GroupColorsAndOrder`,
`AgentControl-SettingsStore-GroupColorsAndOrderDefaults`,
`AgentControl-SettingsStore-InvalidGroupColorsAndOrderRepaired` and
`AgentControl-SettingsStore-StaleGroupSettingsDropped`, whose unit tests are in `SettingsStoreOrganizingTests.cs`).

**SettingsSubsystem_GroupHierarchy_NestedGroupsSurviveSessionsAndDamagedHierarchyIsRepaired**: A first session saves two
repos (one in *Project*, one in *Home*), a hierarchy that registers *Client* with no repo in it and puts *Project*
inside it, and a color for *Client*; a later session loads them and finds *Client*, *Home* and *Project* as existing
groups, *Project* inside *Client*, *Home* at the top level and *Client* with its color, and the configuration directory
holds the settings file and nothing else. A file written before groups nested loads with an empty hierarchy and its
group as the only top-level group. Then a damaged file - two groups that are each other's parent, a group with an
unknown parent and a setting after them - is loaded without failing: the cycle is cut at the group that sorts first, the
unknown parent means top level, the setting after the damage is read, and saving what was loaded does not write the
unknown parent back. This scenario is tested by
`SettingsSubsystem_GroupHierarchy_NestedGroupsSurviveSessionsAndDamagedHierarchyIsRepaired`, covering
`AgentControl-Settings-GroupHierarchy` (children: `AgentControl-SettingsStore-GroupParents`,
`AgentControl-SettingsStore-GroupParentsDefaults`, `AgentControl-SettingsStore-InvalidGroupParentsRepaired` and
`AgentControl-SettingsStore-RegisteredGroupKept`, whose unit tests are in `SettingsStoreGroupHierarchyTests.cs`, and the
`AgentControl-RepoGroupHierarchy-*` requirements of the `RepoGroupHierarchy` unit, whose own tests are in its unit
verification design).

**SettingsSubsystem_BackgroundWriter_WritesNewestStateOffTheGuardedThread**: A `SettingsWriter` over the real
`SettingsStore.Save`, on a clock the test moves: two changes are scheduled in quick succession on a thread guarded
against blocking calls and the delay passes; a direct `SettingsStore.Save` on that thread is attempted as well. After a
flush the settings were written once, the next load reads the newest change, and the direct save was refused and
recorded, covering `AgentControl-Settings-BackgroundWriter` (children: `AgentControl-SettingsWriter-Debounce`,
`-Serialize`, `-ConfirmedSave`, `-FailureReportedOnce`, `-Flush`, and `AgentControl-SettingsStore-RefusesGuardedThread`,
each verified by its unit tests).
