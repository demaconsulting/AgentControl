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
