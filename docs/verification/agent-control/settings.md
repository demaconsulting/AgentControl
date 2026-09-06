## Settings

### Verification Approach

The `Settings` subsystem is verified indirectly through its `SettingsStore` unit's test suite
(`SettingsStoreTests.cs`), documented in the `SettingsStore` unit-level verification design.
The `AppSettings`, `RecentRepo`, and `AgentToolKind` data types have no dedicated test file
and are exercised only as values round-tripped through `SettingsStore`'s save/load tests. All
tests use a real temporary configuration directory on disk.

### Test Environment

N/A - standard test environment; no external services or hardware required.

### Acceptance Criteria

- All unit tests for `SettingsStore` pass with zero failures.
- Saved settings survive a save-then-load round trip unchanged.
- A first-ever run with no settings file yet produces usable default settings rather than
  failing.
- The default configuration directory resolves under the user's application-data folder.

### Test Scenarios

**Settings_Persistence_UnitTestsCoverRoundTripDefaultsAndDefaultDirectory**: Settings
persistence is verified by the `SettingsStore` unit tests (see the `SettingsStore` unit
verification design), with `SettingsStore_SaveThenLoad_RoundTripsAllFields` and
`SettingsStore_Load_NoSettingsFile_ReturnsDefaults` cited directly at the subsystem level,
covering `AgentControl-Settings-Persistence` (children: `AgentControl-SettingsStore-Load`,
`AgentControl-SettingsStore-Save`, `AgentControl-SettingsStore-DefaultDirectory`).
