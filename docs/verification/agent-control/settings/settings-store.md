### SettingsStore

#### Verification Approach

`SettingsStore` is verified with unit tests defined in `SettingsStoreTests.cs`. Every test
uses a real temporary configuration directory on disk, so load/save round-tripping, the
no-file-yet default case, and directory-creation-on-save are exercised against genuine
filesystem state.

#### Test Environment

N/A - standard test environment.

#### Acceptance Criteria

- All unit tests pass with zero failures.
- Package sources (with all fields) round-trip; a legacy single package-folder path migrates to a
  Default zip source, loses to a configured source list, and is never written back.
- The persisted JSON contains no property named like a secret, and a source location that embeds a
  credential is refused with nothing written.
- A repo's group, card color and the collapsed sections round-trip, damaged or wrongly typed values read as
  "none" without failing the load, and stored group names are repaired.
- A save leaves only the settings file, holding the latest content.
- Settings saved and then loaded again return every field unchanged.
- Loading with no settings file present returns default settings rather than throwing.
- Saving creates the configuration directory when it does not yet exist.
- A null settings value passed to save is rejected.
- The default configuration directory resolves under the user's application-data folder.

#### Test Scenarios

**SettingsStore_Load_RoundTripsAndReturnsDefaultsWhenAbsent**: Saving settings and then
loading them back returns all fields unchanged, and loading when no settings file exists
returns default settings. This scenario is tested by
`SettingsStore_SaveThenLoad_RoundTripsAllFields` and
`SettingsStore_Load_NoSettingsFile_ReturnsDefaults`, covering
`AgentControl-SettingsStore-Load`. Settings files with an explicit JSON null for the source list,
the recent-repos list, a source's package IDs (or its alias/location, or a null list entry) load
as empty collections and can be saved, and a null source list still migrates a legacy path;
these are tested by `SettingsStore_Load_ExplicitJsonNulls_NormalizedToEmptyCollections` and
`SettingsStore_Load_NullSourcesWithLegacyPath_MigratesToDefaultZipSource`, also covering
`AgentControl-SettingsStore-Load`.

**SettingsStore_Save_CreatesDirectoryAndRejectsNull**: Saving when the configuration
directory does not yet exist creates it, and saving a null settings value throws
`ArgumentNullException`. This scenario is tested by
`SettingsStore_Save_DirectoryDoesNotExist_CreatesDirectory` and
`SettingsStore_Save_NullSettings_ThrowsArgumentNullException`, covering
`AgentControl-SettingsStore-Save`.

**SettingsStore_DefaultDirectory_ResolvesUnderApplicationData**: The default configuration
directory resolves to a path under the user's application-data folder. This scenario is
tested by `SettingsStore_GetDefaultConfigDirectory_ReturnsPathUnderApplicationData`, covering
`AgentControl-SettingsStore-DefaultDirectory`.

**SettingsStore_SourcesRoundTrip_AllSourceFieldsSurvive**: A zip source and a NuGet source with
package IDs and trust options survive a save-then-load round trip, as does a recent repo's pinned
source. This scenario is tested by `SettingsStore_SaveThenLoad_RoundTripsAllFields`, covering
`AgentControl-SettingsStore-SourcesRoundTrip`.

**SettingsStore_LegacySourceMigration_DefaultZipSourceAndNoLegacyWrite**: A settings file with
only the legacy path loads with it as the `Default` zip source and the legacy field cleared; when
both a source list and a legacy path are present the list wins; saving in-memory settings that
only carry the legacy path writes a `Sources` entry, writes no legacy field, and leaves the
caller's instance unchanged. This scenario is tested by
`SettingsStore_Load_LegacyPackageSourcePath_MigratesToDefaultZipSource`,
`SettingsStore_Load_SourcesAndLegacyPath_SourcesWin`, and
`SettingsStore_Save_LegacyPackageSourcePath_WritesSourcesNotLegacyField`, covering
`AgentControl-SettingsStore-LegacySourceMigration`.

**SettingsStore_NoSecretsPersisted_JsonHasNoSecretLikeProperties**: The persisted JSON of settings
holding a fully populated NuGet server source has no property named like a password, token,
secret, credential or key, and settings whose source location embeds a sentinel password (as URL
user information, for a source or for the legacy single path) are refused without writing any file
and without the password appearing in the error. This scenario is tested by
`SettingsStore_Save_FullyPopulatedSources_JsonContainsNoSecretLikeProperties`,
`SettingsStore_Save_SourceLocationWithEmbeddedCredential_ThrowsAndWritesNothing`, and
`SettingsStore_Save_LegacyPackageSourcePathWithEmbeddedCredential_ThrowsAndWritesNothing`, covering
`AgentControl-SettingsStore-NoSecretsPersisted`.

**SettingsStore_AtomicSave_LeavesOnlySettingsFileWithLatestContent**: After two saves the
configuration directory holds exactly the settings file, with the second save's content. This
scenario is tested by `SettingsStore_Save_RepeatedSaves_LeavesOnlySettingsFileWithLatestContent`,
covering `AgentControl-SettingsStore-AtomicSave`.

**SettingsStore_Theme_DefaultsToSystemRoundTripsByNameAndToleratesInvalidValues**: A settings file without a
`Theme` value loads as the system default (as does a new `AppSettings`); each of system default, light and dark
survives save and load and is written by name; and an unknown name, an empty string, an out-of-range or negative
number, null, a boolean, an object and an array all load as the system default - with the setting that follows
the value still read, proving the reader stays positioned - while a name in another letter case and a defined
number are accepted. This scenario is tested by `SettingsStore_Load_NoThemeValue_DefaultsToSystem`,
`SettingsStore_SaveThenLoad_Theme_RoundTripsByName` and
`SettingsStore_Load_UnknownOrInvalidTheme_FallsBackToSystem`, covering `AgentControl-SettingsStore-Theme`.

**SettingsStore_RepoOrganization_RoundTripsAndToleratesDamagedValues**: Tested by `SettingsStoreOrganizingTests.cs`
and `RepoGroupNameTests.cs` (temporary configuration directories, no UI). A repo's group, a repo's card color
and the collapsed section names survive a save and a load, and a repo without a group or color writes
nothing for them; a settings file from before these values existed loads with no group, the automatic color
and nothing collapsed; a group or color that is null, a number, a boolean, an object or an array reads as none
and the value after it is still read; a collapsed list that is null, a number, a string, an object or empty reads as
empty, and a list with mixed entries keeps only its clean, distinct string entries; and stored group names are
repaired on load - white space cleaned, *Favorites* and *Ungrouped* read as no group, an overlong name cut to 40
characters and look-alike names given the first one's spelling. The name rules themselves are checked directly:
normalizing (trim, fold white space and control characters, null for nothing), validating (blank, too long,
reserved), sanitizing a stored name, listing the distinct names sorted with the first spelling, and adopting an
existing group's spelling. This scenario is tested by
`SettingsStore_SaveThenLoad_GroupColorAndCollapsedGroups_RoundTrip`,
`SettingsStore_Load_FileWithoutOrganizingValues_UsesDefaults`,
`SettingsStore_Load_InvalidGroupOrColorType_ReadsAsNone`,
`SettingsStore_Load_InvalidCollapsedGroups_KeepsOnlyCleanStrings`,
`SettingsStore_Load_GroupNames_AreRepairedAndUnified`,
`RepoGroupName_Normalize_CleansWhiteSpaceAndControlCharacters`, `RepoGroupName_Validate_ReportsEachProblem`,
`RepoGroupName_Sanitize_RepairsStoredNames`, `RepoGroupName_DistinctNames_UnifiesCaseAndSorts` and
`RepoGroupName_Canonicalize_AdoptsExistingSpelling`, covering `AgentControl-SettingsStore-RepoOrganization`.
