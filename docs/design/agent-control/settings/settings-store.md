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

**Replace retry policy**: `ReplaceMaxAttempts` (`8`, the first try plus seven retries),
`ReplaceInitialDelay` (`20 ms`) and `ReplaceMaxDelay` (`200 ms`). A hold by another process (virus scanner, search
indexer, sync client) normally lasts well under a second, so eight attempts cover it. The wait doubles from 20 ms and
is capped at 200 ms (20, 40, 80, 160, 200, 200, 200 ms), at most 900 ms in all, because `Save` usually runs on the UI
thread and must not freeze the window for long when a lock never clears.

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
- *Returns*: `AppSettings`, with the repo list in the user's order.
- *Postconditions*: Returns a fresh default `AppSettings` instance if no settings file exists
  yet — first run is expected, not an error (`AgentControl-SettingsStore-Load`). An explicit JSON
  `null` for the source list, the recent-repos list or a source's package IDs (which bypasses the
  property initializers) is normalized to an empty collection by `AppSettings.NormalizeNulls`
  before migration runs, so it can never crash migration, startup or a source provider. A missing
  `Theme` value loads as `ThemePreference.System` (`AgentControl-SettingsStore-ThemeDefaultsToSystem`), and an
  unknown or invalid one does too instead of failing the load (`AgentControl-SettingsStore-ThemeInvalidReadsAsSystem`);
  `Save` writes the theme by name and `Load` reads it back (`AgentControl-SettingsStore-Theme`). A missing
  `KeepShellOpen` value loads as `true` (`AgentControl-SettingsStore-KeepShellOpenDefaultsToOn`), and a null or
  non-boolean one does too instead of failing the load (`AgentControl-SettingsStore-KeepShellOpenInvalidReadsAsOn`);
  `Save` writes it as a boolean and `Load` reads it back (`AgentControl-SettingsStore-KeepShellOpen`). A repo's `GroupName`
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
  Last, once the legacy source is migrated, `AppSettings.EnsureRepoOrder` seeds the repo order: the order of
  `RecentRepos` is the user's display order and `AppSettings.HasUserRepoOrder` (written as a boolean named
  `HasUserRepoOrder`) says that it already is. A file without the marker - written before the order existed - has its
  repo list arranged exactly once, favorites first and then most recently launched (a repo never launched last), and
  loads with the marker set; a file with the marker keeps its repo sequence as it is, whatever the launch times.
  The arrangement is written back at once (`Save`, through `PersistSeededRepoOrder`) so that it really happens only
  once: the next load finds the marker and does not seed again from launch times that have changed since. A failed
  write-back (for example an unwritable file) never fails the load: it is logged as a warning, the in-memory order is
  used, and the file stays unmarked so the next load tries again.
  `Save` writes the list in its sequence and the marker, and `Load` reads both back, so the sequence
  round-trips (`AgentControl-SettingsStore-RepoOrder`, `-RepoOrderLegacySeed`). A marker that is null or not a
  boolean reads as not set, without failing the load, so the one-time arrangement runs
  (`AgentControl-SettingsStore-RepoOrderMarkerRepaired`). A first run, which has no file, returns a default
  `AppSettings` with an empty list and no marker; the main window's view model seeds it.

**Save**: Saves `AppSettings` to a configuration directory, creating the directory if needed.

- *Parameters*: `AppSettings settings`, `string? configDirectory = null`, and two optional test seams (`Action<string,
  string>? replaceFile`, which moves the temporary file over the settings file, and `Action<TimeSpan>? delay`, which
  waits between attempts; `null` selects `File.Move(..., overwrite: true)` and `Thread.Sleep`). `Load` takes the same
  two seams and passes them to the write-back of the seeded repo order.
- *Returns*: `void`.
- *Postconditions*: The settings file at the configuration directory reflects `settings`
  (`AgentControl-SettingsStore-Save`) with the effective package sources in `Sources` and no
  legacy `PackageSourcePath`, serialized from a persisted copy so the caller's instance is not
  changed (`AgentControl-SettingsStore-SourcesRoundTrip`). The JSON is written to a uniquely
  named temporary file in the same directory and moved over the settings file, so a failure
  never leaves a truncated or partial file, and a leftover temporary file is removed
  (`AgentControl-SettingsStore-AtomicSave`). The move over the settings file is retried (per the replace retry
  policy above) while it fails with a failure another process's brief hold causes: an `UnauthorizedAccessException`
  (what Windows reports for a destination that is open without delete sharing or pending deletion) or an
  `IOException` whose Win32 error is a sharing violation (32) or a lock violation (33). Any other failure - a missing
  directory, a full disk, any other I/O error - is not retried, and a lock that is still there after the last attempt
  fails like any other failure, so a briefly locked file never shows as a failed save while a real failure still does
  (`AgentControl-SettingsStore-LockedFileTolerance`). The write-back of the seeded repo order made by `Load` goes
  through `Save` and so has the same tolerance. Because the model has no secret-bearing member,
  nothing secret is written, and before anything is written every effective source is checked with
  `PackageSourceDefinition.GetCredentialProblem`: a location with URL user information (or a
  server URL with a query string or fragment) makes `Save` throw `InvalidOperationException`
  without creating or changing any file and without repeating the location in the message
  (`AgentControl-SettingsStore-NoSecretsPersisted`).

**Git sources**: A Git source is saved and loaded like any other source, as one entry of `Sources` with these fields:
`Type` (written by name, `Git`), `Location` (the repository address), `TagPattern`, an optional `Subfolder` and
`PackageIds` with **exactly one** package id (the name of the one package the repository delivers). The fields
round-trip unchanged. The definition of each field and the rules for it belong to `PackageSourceDefinition` (see the
*AgentPackageManagement* design, *Source model*). `Save` refuses a source whose address embeds a credential (the
type-aware `GetCredentialProblem`: user information on an https address, a password in an ssh address, a query or
fragment; an ssh user name is allowed) before anything is written, with a message that does not repeat the address
(`AgentControl-SettingsStore-GitSourceRoundTrip`). `Load` reads the fields as they are: it neither repairs nor rejects
a Git source, so a hand-edited source with, for example, two package ids is loaded unchanged and is stopped later,
where the user edits it (the source editor and the Settings window validate every field before a save) and when a
provider is created for it.

#### Error Handling

`Load` throws `InvalidOperationException` when the settings file exists but cannot be read or
contains invalid JSON (wrapping `IOException`/`UnauthorizedAccessException`/`JsonException`); a failure to write back
the seeded repo order is logged and does not throw.
`Save` throws `ArgumentNullException` for a null `settings` and `InvalidOperationException`
when the configuration directory cannot be created or the file cannot be written, including when a lock on the
settings file outlasts the retries; the temporary file is removed in every failure case.

#### Dependencies

- **AppSettings** (data type, same subsystem) — the data type read/written, including its
  legacy-migration and persisted-copy members.
- **RecentRepo** (same subsystem) — the per-repo group and card-color values, and the list of known card colors.
- **RepoGroupName** (same subsystem, its own unit) — the rules that repair stored group names.
- **PackageSourceDefinition** (`AgentPackageManagement` subsystem) — the source data type held in
  `AppSettings.Sources`.
- **.NET BCL** — `System.Text.Json`, `Environment.GetFolderPath`, `File.Move`, `Thread.Sleep` (the defaults of the
  replace and delay seams).
- **AppLogging** (`Logging` subsystem) — the logger factory used to log a seeded repo order that could not be saved.

#### Callers

- **Program**/**App** — calls `Load` at startup to obtain the initial `AppSettings`, and the
  `MainWindowViewModel`/`SettingsWindowViewModel` save path calls `Save` after every settings
  change (favorite toggle, pin update, launch timestamp, or an explicit Settings-window save).
