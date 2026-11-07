## Settings

![Settings Structure](SettingsView.svg)

### Overview

The `Settings` subsystem spans `SettingsStore.cs` (loads/saves application-wide settings),
and the `AppSettings.cs`/`RecentRepo.cs`/`AgentToolKind.cs` data types, which are folded into
this subsystem-level description as they have no dedicated test file. It provides the
observable behavior of persisting application-wide preferences (package sources, git/agent
tool overrides, recent repos, shell preference) (`AgentControl-Settings-Persistence`,
`AgentControl-Settings-PackageSources`). The subsystem contains one unit: `SettingsStore`.

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
`.agentcontrol.json` via `RepoConfig` remains authoritative), favorite status, and last-launch
timestamp used by `LauncherUI`'s sort order. `AgentToolKind` enumerates the well-known agent
tools (`CopilotCli`, `Cursor`, `ClaudeCode`) plus `Custom`, per architecture.md's
"Configurable agent tool and shell" decision — the tool is never hardcoded to Copilot CLI.

`SettingsStore` is a static class with no persistent state of its own; it is called once at
startup (`App.OnFrameworkInitializationCompleted`) to load settings, and again whenever
`SettingsWindowViewModel` reports a save. It shares its configuration-directory resolution
with the `Logging` subsystem's `LoggingSetup.Initialize` so a `--config-dir` startup override
(used by FlaUI end-to-end tests for hermetic isolation) redirects both settings and log
files consistently rather than duplicating path-resolution logic.
