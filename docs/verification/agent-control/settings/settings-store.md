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
- The persisted JSON contains no property named like a secret.
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
secret, credential or key. This scenario is tested by
`SettingsStore_Save_FullyPopulatedSources_JsonContainsNoSecretLikeProperties`, covering
`AgentControl-SettingsStore-NoSecretsPersisted`.

**SettingsStore_AtomicSave_LeavesOnlySettingsFileWithLatestContent**: After two saves the
configuration directory holds exactly the settings file, with the second save's content. This
scenario is tested by `SettingsStore_Save_RepeatedSaves_LeavesOnlySettingsFileWithLatestContent`,
covering `AgentControl-SettingsStore-AtomicSave`.
