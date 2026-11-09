## Settings

![Settings Structure](SettingsView.svg)

### Overview

The `Settings` subsystem spans `SettingsStore.cs` (loads/saves application-wide settings), the `RepoGroupName.cs` rules
helper, and the `AppSettings.cs`/`RecentRepo.cs`/`AgentToolKind.cs`/`ThemePreference.cs` data types, which are
documented as supporting types of this subsystem rather than as units of their own: they hold the settings data and
the small repairs `SettingsStore.Load` applies to it (legacy-source migration, tolerant reading, group-name and
card-color repair). `RepoGroupName` is a unit: its rules for group names (what is a valid name, how a stored name is
repaired, how look-alike names are given one spelling) are logic of their own, with their own requirements, design
(`docs/design/agent-control/settings/repo-group-name.md`), verification, `RepoGroupNameTests.cs` and review set; the
`LauncherUI` group prompt and cards use it as well as `SettingsStore`. The subsystem provides the
observable behavior of persisting application-wide preferences (package sources, git/agent
tool overrides, recent repos, shell preference, color theme) and the user's own organizing choices (repo groups,
card colors, collapsed sections) (`AgentControl-Settings-Persistence`,
`AgentControl-Settings-PackageSources`, `AgentControl-Settings-GitSourcePersistence`, `AgentControl-Settings-Appearance`,
`AgentControl-Settings-RepoOrganization`). The subsystem contains two units: `SettingsStore` and `RepoGroupName`.

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
with the repos. `RepoGroupName` (the subsystem's unit for these rules) holds the rules for group names: control
characters and runs of white space fold into one space and the ends are trimmed, the longest name is 40 characters,
*Favorites* and *Ungrouped* are reserved (so the collapsed state of the built-in sections can be stored beside the
groups'), names are compared by ordinal comparison ignoring letter case, and `DistinctNames`/`Canonicalize` give a
group one
spelling (the first). Typed names are validated (`Validate`); stored names are repaired (`Sanitize`: cut to the
limit, reserved or blank means no group). Reading is tolerant like the theme: `RecentRepo` declares a private JSON
converter that reads a string for `GroupName`/`CardColor` and anything else (a number, boolean, null, object or
array) as `null`, and `AppSettings` declares one that reads `CollapsedGroups` as the string entries of an array and
anything else as an empty list; `AppSettings.NormalizeNulls` (called by `Load`) then sanitizes every group name,
gives look-alike groups the first spelling, cleans the collapsed list, and repairs every repo's `CardColor` with
`RecentRepo.NormalizeCardColor` - an unknown or blank name becomes `null` (the automatic color) and a known name in
any letter case, or with surrounding white space, gets its canonical spelling from `RecentRepo.CardColorNames`, the
one list of valid card colors that the `LauncherUI` palette is built from - so none of these values can keep the
application from starting, and an unknown color is neither kept nor written back
(`AgentControl-SettingsStore-RepoOrganization`, `AgentControl-SettingsStore-UnknownCardColorIsAutomatic`,
`AgentControl-Settings-RepoOrganization`).
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

A Git package source is persisted like the others, as one entry of the `Sources` list, and the settings file holds
exactly the fields of the `AgentPackageManagement` source definition (see the *AgentPackageManagement* design,
*Source model*): `Type` is written by name (`Git`); `Location` is the repository address (an `https` or `ssh` address or
a local path); `TagPattern` is the tag pattern (for example `v{version}`); `Subfolder` is the optional folder of the
repository that holds the package layout (`null` or empty for the top); and `PackageIds` holds **exactly one** package
id, the name of the one package the repository delivers - a Git source never lists several. The source reads back as it
was written (the fields round-trip unchanged), and the file has no member that could hold a credential. The store
guards the file at its boundary: `Save` refuses a source whose address embeds a credential (user information with a
password or token, or a query string or fragment; an ssh user name alone is allowed) before anything is written, with
a message that does not repeat the address, so a secret pasted into the address never reaches the file. `Load` reads
the fields as they are and does not repair or reject a Git source; the rules a user can break (address form, tag
pattern, sub-folder, exactly one package id) are enforced where the user edits the source - the source editor and the
Settings window refuse to save a source that breaks them - and again when a provider is created for it, which stops a
hand-edited source that breaks them from being used instead of reaching git
(`AgentControl-SettingsStore-GitSourceRoundTrip`, `AgentControl-SettingsStore-GitSourceRefusesCredential`, within
`AgentControl-Settings-GitSourcePersistence`; `AgentControl-System-GitSourceSettings`).
