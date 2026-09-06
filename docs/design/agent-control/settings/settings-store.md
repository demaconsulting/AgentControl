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

**Load**: Loads `AppSettings` from a configuration directory.

- *Parameters*: `string? configDirectory = null` (defaults to `GetDefaultConfigDirectory`).
- *Returns*: `AppSettings`.
- *Postconditions*: Returns a fresh default `AppSettings` instance if no settings file exists
  yet — first run is expected, not an error (`AgentControl-SettingsStore-Load`).

**Save**: Saves `AppSettings` to a configuration directory, creating the directory if needed.

- *Parameters*: `AppSettings settings`, `string? configDirectory = null`.
- *Returns*: `void`.
- *Postconditions*: The settings file at the configuration directory reflects `settings`
  exactly (`AgentControl-SettingsStore-Save`).

#### Error Handling

`Load` throws `InvalidOperationException` when the settings file exists but cannot be read or
contains invalid JSON (wrapping `IOException`/`UnauthorizedAccessException`/`JsonException`).
`Save` throws `ArgumentNullException` for a null `settings` and `InvalidOperationException`
when the configuration directory cannot be created or the file cannot be written.

#### Dependencies

- **AppSettings** (record, same subsystem) — the data type read/written.
- **.NET BCL** — `System.Text.Json`, `Environment.GetFolderPath`.

#### Callers

- **Program**/**App** — calls `Load` at startup to obtain the initial `AppSettings`, and the
  `MainWindowViewModel`/`SettingsWindowViewModel` save path calls `Save` after every settings
  change (favorite toggle, pin update, launch timestamp, or an explicit Settings-window save).
