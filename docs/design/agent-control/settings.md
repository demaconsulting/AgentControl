## Settings

![Settings Structure](SettingsView.svg)

### Overview

The `Settings` subsystem spans `SettingsStore.cs` (loads/saves application-wide settings),
and the `AppSettings.cs`/`RecentRepo.cs`/`AgentToolKind.cs`/`ThemePreference.cs` data types and the `RepoGroupName.cs`
rules helper, which are documented as supporting types of this subsystem rather than as units of their own: the data
types carry no behavior, and `RepoGroupName` only validates and cleans group names (it has its own
`RepoGroupNameTests.cs`, and is exercised through `SettingsStore` and the `LauncherUI` group prompt). It provides the
observable behavior of persisting application-wide preferences (package sources, git/agent
tool overrides, recent repos, shell preference, color theme) and the user's own organizing choices (repo groups,
card colors, collapsed sections) (`AgentControl-Settings-Persistence`,
`AgentControl-Settings-PackageSources`, `AgentControl-Settings-Appearance`,
`AgentControl-Settings-RepoOrganization`). The subsystem contains one unit: `SettingsStore`.

### Interfaces

**SettingsStore.Load**: Loads previously saved settings.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns default `AppSettings` when no settings file exists yet, so a first-ever
  run still produces a usable configuration (`AgentControl-SettingsStore-Load`). A settings file
  that only has the legacy single package-folder path loads with it as a zip source named
  `Default` (`AgentControl-SettingsStore-LegacySourceMigration`).
- *Constraints*: None beyond standard JSON deserialization failure modes.

**SettingsStore.Save**: Saves settings to the configuration directory.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Creates the configuration directory if it does not yet exist
  (`AgentControl-SettingsStore-Save`), writes the configured sources and never the legacy path,
  and replaces the file atomically (`AgentControl-SettingsStore-AtomicSave`,
  `AgentControl-SettingsStore-SourcesRoundTrip`).
- *Constraints*: Rejects a null `AppSettings` value with `ArgumentNullException`.

**SettingsStore.GetDefaultConfigDirectory**: Resolves the default configuration directory.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns a path under the user's application-data folder
  (`%APPDATA%\AgentControl\`) used when no `--config-dir` startup override is supplied
  (`AgentControl-SettingsStore-DefaultDirectory`).
- *Constraints*: Also reused by `LoggingSetup.Initialize` so a `--config-dir` override
  redirects both settings and log files together.

### Design

`AppSettings` is a plain, mutable data-transfer object (the configured package sources, git
executable override, `AgentToolKind` selection, custom agent command, shell preference, and the
`RecentRepo` list) serialized as JSON by `SettingsStore` under `%APPDATA%\AgentControl\` or a
config-directory override. The package sources are a list of `PackageSourceDefinition` (see the
`AgentPackageManagement` subsystem); the model has no member that can hold a password, token or
other secret, so none can be written to the settings file
(`AgentControl-SettingsStore-NoSecretsPersisted`). The older single package-folder path
(`PackageSourcePath`) is still read so existing configurations keep working: `GetEffectiveSources`
treats it as a `Default` zip source when the list is empty, `MigrateLegacySource` (called by
`SettingsStore.Load`) materializes that source and clears the path, and a persisted copy
(`CreatePersistedCopy`) is what `Save` serializes so the legacy field is never written back and
the caller's instance is left unchanged. `RecentRepo` records one tracked repo's path, cached pin
display fields (`PinnedPackageName`/`PinnedPackageVersion`/`PinnedSource` — copies for quick
display; the repo's own
`.agentcontrol.json` via `RepoConfig` remains authoritative), favorite status, last-launch
timestamp used by `LauncherUI`'s sort order, and the user's own organizing choices: an optional `GroupName` (at most
one group per repo) and an optional `CardColor` (a palette color's name; absent means the automatic color). These
and `AppSettings.CollapsedGroups` (the names of the collapsed list sections, including the built-in *Favorites* and
*Ungrouped*) are per-user view state that exists only in the settings file and is never written to a repository. A
group has no record of its own: it exists while a repo names it, so there is no list of groups that could disagree
with the repos. `RepoGroupName` (a static helper of this subsystem) holds the rules for group names: control
characters and runs of white space fold into one space and the ends are trimmed, the longest name is 40 characters,
*Favorites* and *Ungrouped* are reserved (so the collapsed state of the built-in sections can be stored beside the
groups'), names are compared by ordinal comparison ignoring letter case, and `DistinctNames`/`Canonicalize` give a
group one
spelling (the first). Typed names are validated (`Validate`); stored names are repaired (`Sanitize`: cut to the
limit, reserved or blank means no group). Reading is tolerant like the theme: `RecentRepo` declares a private JSON
converter that reads a string for `GroupName`/`CardColor` and anything else (a number, boolean, null, object or
array) as `null`, and `AppSettings` declares one that reads `CollapsedGroups` as the string entries of an array and
anything else as an empty list; `AppSettings.NormalizeNulls` (called by `Load`) then sanitizes every group name,
gives look-alike groups the first spelling, and cleans the collapsed list - so none of these values can keep the
application from starting (`AgentControl-SettingsStore-RepoOrganization`, `AgentControl-Settings-RepoOrganization`).
`GroupName` and `CardColor` are omitted from the file when `null`. `AgentToolKind` enumerates the well-known agent
tools (`CopilotCli`, `Cursor`, `ClaudeCode`) plus `Custom`, per architecture.md's
"Configurable agent tool and shell" decision — the tool is never hardcoded to Copilot CLI.
`ThemePreference` (`System`, `Light`, `Dark`) is the user's color-theme choice, stored in `AppSettings.Theme` and
written by member name. `System` is the default, and is what a settings file without the value (written by an
older version) means. Reading is tolerant: `AppSettings` declares a private JSON converter that accepts a name
(ignoring case) or a number that is a defined member, and reads anything else - an unknown name, an
out-of-range number, null, a boolean, an object or an array (which it skips whole) - as `System`, because the
built-in enum handling would throw on an unknown name and `SettingsStore.Load` would report the whole file as
corrupt; the appearance can therefore never keep the application from starting
(`AgentControl-SettingsStore-Theme`, `AgentControl-Settings-Appearance`).

`SettingsStore` is a static class with no persistent state of its own; it is called once at
startup (`App.OnFrameworkInitializationCompleted`) to load settings, and again whenever
`SettingsWindowViewModel` reports a save. It shares its configuration-directory resolution
with the `Logging` subsystem's `LoggingSetup.Initialize` so a `--config-dir` startup override
(used by FlaUI end-to-end tests for hermetic isolation) redirects both settings and log
files consistently rather than duplicating path-resolution logic.
