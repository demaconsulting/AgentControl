### SettingsStore

![Settings Structure](SettingsView.svg)

#### Purpose

`SettingsStore` reads and writes `AppSettings` as JSON under a configurable directory,
defaulting to `%APPDATA%\AgentControl\`. The configuration directory is always caller-supplied
rather than read from process-wide state, so that `StartupOptions.ConfigDirectory` (the FlaUI
test-isolation override) can redirect settings I/O to a temporary folder without any
global/static mutable state.

#### Data Model

`SettingsStore` holds no instance state; it is a static class.

**SettingsFileName**: `const string` — `settings.json`, within the configuration directory.

**JsonOptions**: `static readonly JsonSerializerOptions` — shared `WriteIndented = true`
options used by both `Load` and `Save`.

#### Key Methods

**GetDefaultConfigDirectory**: Gets the default configuration directory.

- *Parameters*: None.
- *Returns*: `string` — `%APPDATA%\AgentControl\` on Windows (the platform-appropriate
  equivalent elsewhere), per architecture.md's data-storage decision
  (`AgentControl-SettingsStore-DefaultDirectory`).

**Load**: Loads `AppSettings` from a configuration directory, migrating a legacy
single package-folder path to a `Default` zip source when no sources are configured
(`AgentControl-SettingsStore-LegacySourceMigration`); a configured source list wins over a legacy
path.

- *Parameters*: `string? configDirectory = null` (defaults to `GetDefaultConfigDirectory`).
- *Returns*: `AppSettings`.
- *Postconditions*: Returns a fresh default `AppSettings` instance if no settings file exists
  yet — first run is expected, not an error (`AgentControl-SettingsStore-Load`). An explicit JSON
  `null` for the source list, the recent-repos list or a source's package IDs (which bypasses the
  property initializers) is normalized to an empty collection by `AppSettings.NormalizeNulls`
  before migration runs, so it can never crash migration, startup or a source provider. A missing
  `Theme` value loads as `ThemePreference.System` (`AgentControl-SettingsStore-ThemeDefaultsToSystem`), and an
  unknown or invalid one does too instead of failing the load (`AgentControl-SettingsStore-ThemeInvalidReadsAsSystem`);
  `Save` writes the theme by name and `Load` reads it back (`AgentControl-SettingsStore-Theme`). A repo's `GroupName`
  and `CardColor` and the `CollapsedGroups` list are written to the settings file only and read back
  (`AgentControl-SettingsStore-RepoOrganization`); `Save` omits a group or color that is `null`
  (`AgentControl-SettingsStore-RepoOrganizationOmitsUnset`). They are read tolerantly: a file without them loads as no
  group, the automatic color and an empty list (`AgentControl-SettingsStore-RepoOrganizationDefaults`), a group or
  color that is null or of the wrong type reads as `null` and the values after it are still read
  (`AgentControl-SettingsStore-InvalidGroupOrColorReadsAsNone`), and a collapsed list that is not an array of strings
  reads as empty while a damaged array keeps its clean, distinct string entries
  (`AgentControl-SettingsStore-InvalidCollapsedGroupsRepaired`). `AppSettings.NormalizeNulls` (run by `Load`) then
  repairs what was read: every group name goes through the `RepoGroupName` rules - the store only applies them, the
  rules are the `RepoGroupName` unit's (`AgentControl-SettingsStore-GroupNamesRepaired`) - and a card color name
  that is not one of the known colors (`RecentRepo.CardColorNames`) is read as `null`, the automatic color, so a later
  `Save` does not write it back, while a known name in any letter case or with surrounding white space is kept in its
  canonical spelling (`RecentRepo.NormalizeCardColor`) (`AgentControl-SettingsStore-UnknownCardColorIsAutomatic`).

**Save**: Saves `AppSettings` to a configuration directory, creating the directory if needed.

- *Parameters*: `AppSettings settings`, `string? configDirectory = null`.
- *Returns*: `void`.
- *Postconditions*: The settings file at the configuration directory reflects `settings`
  (`AgentControl-SettingsStore-Save`) with the effective package sources in `Sources` and no
  legacy `PackageSourcePath`, serialized from a persisted copy so the caller's instance is not
  changed (`AgentControl-SettingsStore-SourcesRoundTrip`). The JSON is written to a uniquely
  named temporary file in the same directory and moved over the settings file, so a failure
  never leaves a truncated or partial file, and a leftover temporary file is removed
  (`AgentControl-SettingsStore-AtomicSave`). Because the model has no secret-bearing member,
  nothing secret is written, and before anything is written every effective source is checked with
  `PackageSourceDefinition.GetCredentialProblem`: a location with URL user information (or a
  server URL with a query string or fragment) makes `Save` throw `InvalidOperationException`
  without creating or changing any file and without repeating the location in the message
  (`AgentControl-SettingsStore-NoSecretsPersisted`).

**Git sources**: A Git source is saved and loaded like any other source - its `TagPattern` and `Subfolder` are plain fields,
the type is written by name - and `Save` refuses a source whose address embeds a credential (the type-aware
`GetCredentialProblem`: user information on an https address, a password in an ssh address, a query or fragment; an ssh
user name is allowed) before anything is written (`AgentControl-SettingsStore-GitSourceRoundTrip`).

#### Error Handling

`Load` throws `InvalidOperationException` when the settings file exists but cannot be read or
contains invalid JSON (wrapping `IOException`/`UnauthorizedAccessException`/`JsonException`).
`Save` throws `ArgumentNullException` for a null `settings` and `InvalidOperationException`
when the configuration directory cannot be created or the file cannot be written.

#### Dependencies

- **AppSettings** (data type, same subsystem) — the data type read/written, including its
  legacy-migration and persisted-copy members.
- **RecentRepo** (same subsystem) — the per-repo group and card-color values, and the list of known card colors.
- **RepoGroupName** (same subsystem, its own unit) — the rules that repair stored group names.
- **PackageSourceDefinition** (`AgentPackageManagement` subsystem) — the source data type held in
  `AppSettings.Sources`.
- **.NET BCL** — `System.Text.Json`, `Environment.GetFolderPath`.

#### Callers

- **Program**/**App** — calls `Load` at startup to obtain the initial `AppSettings`, and the
  `MainWindowViewModel`/`SettingsWindowViewModel` save path calls `Save` after every settings
  change (favorite toggle, pin update, launch timestamp, or an explicit Settings-window save).
