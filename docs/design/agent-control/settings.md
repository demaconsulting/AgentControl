## Settings

![Settings Structure](SettingsView.svg)

### Overview

The `Settings` subsystem spans `SettingsStore.cs` (loads/saves application-wide settings), the `RepoGroupName.cs` and
`RepoGroupHierarchy.cs` rules helpers, and the `AppSettings.cs`/`RecentRepo.cs`/`AgentToolKind.cs`/`ThemePreference.cs`
data types, which are
documented as supporting types of this subsystem rather than as units of their own: they hold the settings data and
the small repairs `SettingsStore.Load` applies to it (legacy-source migration, tolerant reading, group-name and
card-color repair). `RepoGroupName` is a unit: its rules for group names (what is a valid name, how a stored name is
repaired, how look-alike names are given one spelling) are logic of their own, with their own requirements, design
(`docs/design/agent-control/settings/repo-group-name.md`), verification, `RepoGroupNameTests.cs` and review set; the
`LauncherUI` group prompt and cards use it as well as `SettingsStore`. `RepoGroupHierarchy` is a unit too: its rules for
how groups nest (which group is inside which, the order of one level, which moves are allowed, what deleting a group
with its contents kept does to the tree, how stored parents are repaired) are pure logic of their own
(`docs/design/agent-control/settings/repo-group-hierarchy.md`). The subsystem provides the
observable behavior of persisting application-wide preferences (package sources, git/agent
tool overrides, recent repos, shell preference, keep-shell-open choice, color theme) and the user's own organizing
choices (repo groups,
card colors, collapsed sections, the order of the repo list, the color and order of the groups, and how the groups
nest)
(`AgentControl-Settings-Persistence`,
`AgentControl-Settings-PackageSources`, `AgentControl-Settings-GitSourcePersistence`,
`AgentControl-Settings-Appearance`, `AgentControl-Settings-KeepShellOpen`,
`AgentControl-Settings-RepoOrganization`, `AgentControl-Settings-RepoOrder`, `AgentControl-Settings-GroupOrganization`,
`AgentControl-Settings-GroupHierarchy`),
and of writing them in the background so the window never waits for the settings file
(`AgentControl-Settings-BackgroundWriter`). The subsystem contains four units:
`SettingsStore`, `SettingsWriter`, `RepoGroupName` and `RepoGroupHierarchy`. `SettingsWriter`
(`docs/design/agent-control/settings/settings-writer.md`) writes settings snapshots one at a time on a worker thread,
the newest state of a burst of changes winning; it calls the save delegate its owner gives it, `SettingsStore.Save`
in the application.

### Interfaces

**SettingsStore.Load**: Loads previously saved settings.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns default `AppSettings` when no settings file exists yet, so a first-ever run still produces a
  usable configuration (`AgentControl-SettingsStore-Load`). A settings file that only has the legacy single
  package-folder path loads with it as a zip source named `Default`
  (`AgentControl-SettingsStore-LegacySourceMigration`). A settings file that has no stored repo order yet (written
  before the order was the user's) loads with its repo list arranged once - favorites first, then most recently
  launched - and marked as the user's order, with the arrangement and marker written back to the file at once so it
  happens only once (`AgentControl-Settings-RepoOrder`, `AgentControl-SettingsStore-RepoOrderLegacySeed`); a file that
  already has the marker keeps its repo sequence as it is. A failed write-back is logged and never fails the load.
  The group colors and the group order are read back as saved; a file without them loads with none, and damaged,
  unknown or stale entries (a group no repo is in and that is not part of the hierarchy) are dropped
  (`AgentControl-Settings-GroupOrganization`). The group hierarchy (`GroupParents`) is read back as saved; a file
  without it loads as a flat list of top-level groups, and a damaged one is repaired - never failing the load
  (`AgentControl-Settings-GroupHierarchy`).
- *Constraints*: None beyond standard JSON deserialization failure modes.

**SettingsStore.Save**: Saves settings to the configuration directory.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Creates the configuration directory if it does not yet exist
  (`AgentControl-SettingsStore-Save`), writes the configured sources and never the legacy path,
  and replaces the file atomically (`AgentControl-SettingsStore-AtomicSave`,
  `AgentControl-SettingsStore-SourcesRoundTrip`), retrying the replace while another process briefly
  holds the file (`AgentControl-SettingsStore-LockedFileTolerance`).
- *Constraints*: Rejects a null `AppSettings` value with `ArgumentNullException`. It writes the disk and can wait for a
  locked file, so it must not run on the UI thread: it refuses the call, before anything is written, on a thread the
  tests have guarded (`BlockingCallGuard`, `AgentControl-SettingsStore-RefusesGuardedThread`).

**SettingsWriter**: Writes settings snapshots in the background.

- *Type*: In-process .NET class (`Schedule`, `SaveNowAsync`, `FlushAsync`, `HasUnsavedChanges`,
  the `WriteFailed` event).
- *Role*: Provider.
- *Contract*: `Schedule` accepts a snapshot at once and writes the newest one once no newer one came for the debounce
  delay (`AgentControl-SettingsWriter-Debounce`); writes never overlap and never run on the caller's thread
  (`AgentControl-SettingsWriter-Serialize`); `SaveNowAsync` is a confirmed write that tells its caller whether it
  reached the disk, dropping an older waiting snapshot only when it did (`AgentControl-SettingsWriter-ConfirmedSave`); a
  background write that fails is reported once per failure streak and its snapshot is kept for the next write
  (`AgentControl-SettingsWriter-FailureReportedOnce`); `FlushAsync` writes what is waiting at once and tells whether it
  was written (`AgentControl-SettingsWriter-Flush`).
- *Constraints*: The snapshots must be copies nobody changes any more (`AppSettings.CreateSnapshot`). `WriteFailed` is
  posted to the synchronization context current when the writer was created.

**SettingsStore.GetDefaultConfigDirectory**: Resolves the default configuration directory.

- *Type*: In-process .NET static method.
- *Role*: Provider.
- *Contract*: Returns a path under the user's application-data folder
  (`%APPDATA%\AgentControl\`) used when no `--config-dir` startup override is supplied
  (`AgentControl-SettingsStore-DefaultDirectory`).
- *Constraints*: Also reused by `LoggingSetup.Initialize` so a `--config-dir` override
  redirects both settings and log files together.

### Design

`AppSettings` is a plain, mutable data-transfer object (the configured package sources, git executable override,
`AgentToolKind` selection, custom agent command, shell preference, the keep-shell-open choice, and the `RecentRepo`
list) serialized as JSON by `SettingsStore` under `%APPDATA%\AgentControl\` or a config-directory override. The package
sources are a list of `PackageSourceDefinition` (see the `AgentPackageManagement` subsystem); the model has no member
that can hold a password, token or other secret, so none can be written to the settings file
(`AgentControl-SettingsStore-NoSecretsPersisted`). The older single package-folder path (`PackageSourcePath`) is still
read so existing configurations keep working: `GetEffectiveSources` treats it as a `Default` zip source when the list is
empty, `MigrateLegacySource` (called by `SettingsStore.Load`) materializes that source and clears the path, and a
persisted copy (`CreatePersistedCopy`) is what `Save` serializes so the legacy field is never written back and the
caller's instance is left unchanged. `RecentRepo` records one tracked repo's path, cached pin display fields
(`PinnedPackageName`/`PinnedPackageVersion` — copies for quick display; the repo's own `.agentcontrol.json` via
`RepoConfig` remains authoritative), `PinnedSource` (not a copy of anything in the pin: the alias of the configured
package source that last supplied this repo's agent files on this machine, a per-user cache that the pin file never
carries and that is cleared when the source is no longer configured; the property keeps its historical name so existing
settings files stay readable), favorite status, the last-launch timestamp (it records when the repo was last launched;
it no longer orders the main window's list, but it is what the one-time seed of the repo order uses), and the user's own
organizing choices: an optional `GroupName` (at most one group per repo) and an optional `CardColor` (a palette color's
name; absent means the automatic color). These and `AppSettings.CollapsedGroups` (the names of the collapsed list
sections, including the built-in *Favorites* and *Ungrouped*) are per-user view state that exists only in the settings
file and is never written to a repository. A group has no record of its own: it exists while a repo names it or, when it
takes part in the group hierarchy (see *Group hierarchy* below), until it is deleted, so there is no list of groups that
could disagree with the repos beyond that one map. `AppSettings.GetGroupNames` is the one place that says which groups
exist. `RepoGroupName` (the subsystem's unit for these rules) holds the rules for group names: control characters and
runs of white space fold into one space and the ends are trimmed, the longest name is 40 characters, *Favorites* and
*Ungrouped* are reserved (so the collapsed state of the built-in sections can be stored beside the groups'), names are
compared by ordinal comparison ignoring letter case, and `DistinctNames`/`Canonicalize` give a group one spelling (the
first). Typed names are validated (`Validate`); stored names are repaired (`Sanitize`: cut to the limit, reserved or
blank means no group). Reading is tolerant like the theme: `RecentRepo` declares a private JSON converter that reads a
string for `GroupName`/`CardColor` and anything else (a number, boolean, null, object or array) as `null`, and
`AppSettings` declares one that reads `CollapsedGroups` as the string entries of an array and anything else as an empty
list; `AppSettings.NormalizeNulls` (called by `Load`) then sanitizes every group name, gives look-alike groups the first
spelling, cleans the collapsed list, and repairs every repo's `CardColor` with `RecentRepo.NormalizeCardColor` - an
unknown or blank name becomes `null` (the automatic color) and a known name in any letter case, or with surrounding
white space, gets its canonical spelling from `RecentRepo.CardColorNames`, the one list of valid card colors that the
`LauncherUI` palette is built from - so none of these values can keep the application from starting, and an unknown
color is neither kept nor written back (`AgentControl-SettingsStore-RepoOrganization`,
`AgentControl-SettingsStore-UnknownCardColorIsAutomatic`, `AgentControl-Settings-RepoOrganization`). `GroupName` and
`CardColor` are omitted from the file when `null`.

**Repo order.** The order of `AppSettings.RecentRepos` *is* the user's display order: a repo is added at the end, and
the main window's Move menu items and drag and drop reorder the list itself (the `LauncherUI` design describes
how; the main window shows each section's repos in that relative order). Because there is no second list, a repo
that is removed is removed from the order too, and no repo can be missing from it, so nothing has to be reconciled on
load. What the list cannot say by itself is whether its sequence is already the user's: a file written before the
order existed holds the list in the order older versions stored it (newest added first), which is not what the user
saw (favorites first, then the
most recently launched). `AppSettings.HasUserRepoOrder` is the marker (a boolean, `false` for a file without it) and
`AppSettings.EnsureRepoOrder` the one-time seed: when the marker is not set it stably sorts the list by favorite
descending and then by last-launch time descending (a repo never launched last), which gives the same relative order in
every section as before, and sets the marker; when it is set it does nothing, so a user's order is never re-sorted.
`SettingsStore.Load` calls it after `NormalizeNulls` and the legacy-source migration, and the main window's view
model calls it too, so settings built in memory (a first run, which has no file and so an empty list and no marker)
are seeded the same way; the seed is written by the next save of any kind, and the arrangement is deterministic until
then. Reading the marker is tolerant like the others: `AppSettings` declares a private JSON converter that reads
`true` and `false` and reads anything else - null, a string, a number, an object or an array (which it skips whole) -
as `false`, which only causes the one-time seed to run again, and never fails the load
(`AgentControl-Settings-RepoOrder`, `AgentControl-SettingsStore-RepoOrder`, `-RepoOrderLegacySeed`,
`-RepoOrderMarkerRepaired`). The order is stored only in the user's settings file, never in a repository.

**Group colors and group order.** Two more per-user choices belong to the user's own groups and, like the rest of the
organizing values, are stored only in the settings file and are always written. `AppSettings.GroupColors`
(`Dictionary<string, string>`, created with `RepoGroupName.Comparer` so it is keyed by group name ignoring letter case)
maps a group's name to the name of a palette color - one of `RecentRepo.CardColorNames`, so a group and a card are
colored from one palette; a group without an entry has no color. `AppSettings.GroupOrder` (`List<string>`) holds group
names first to last; the main window lists Favorites, then the groups named there in that order, then the groups with no
position yet alphabetically, then Ungrouped (the `LauncherUI` design describes how the order is changed). Reading is
tolerant: `GroupColors` has a private converter (`StringMapJsonConverter`) that reads the string members of a JSON
object, skips members whose values are not strings (a nested object or array is skipped whole), skips a member whose
name repeats an earlier one ignoring letter case (the first wins) and reads anything that is not an object as empty;
`GroupOrder` reuses the converter of `CollapsedGroups`. `AppSettings.RepairGroupSettings` (internal, deterministic and
idempotent; it replaces both collections with new instances) then brings both in line with the groups that exist: each
key or name is passed through `RepoGroupName.Sanitize` and dropped when nothing usable or a built-in section's name is
left; a color is passed through `RecentRepo.NormalizeCardColor` and dropped when the palette does not know it; entries
of a group that does not exist (no repo names it and it is not registered, see *Group hierarchy*) are dropped (a
favorite still counts as a member of its group); entries that differ only in letter case are merged, the first winning,
and respelled the way the group is spelled on its repos; the order keeps each group once. Because a group that is not
registered exists only while a repo names it, its color and position are forgotten once its last repo leaves it or it is
removed, and a later group of the same name starts afresh. It runs at the end of the group-name repair of
`NormalizeNulls` (so damaged, unknown or stale entries never reach the window and are not written back) and after every
change the main window's view model saves. `AppSettings.FindGroupColor` looks a group's color up for the window: it
sanitizes the name, matches ignoring letter case even when the dictionary was built in memory with another comparer, and
reads the value through the palette, so a built-in section or an unknown color has none
(`AgentControl-Settings-GroupOrganization`, `AgentControl-SettingsStore-GroupColorsAndOrder`,
`-GroupColorsAndOrderDefaults`, `-InvalidGroupColorsAndOrderRepaired`, `-StaleGroupSettingsDropped`).

**Group hierarchy.** Groups can nest. Group names stay unique across the whole tree and a group is still identified by
its name alone, so no existing key changes and a file written before groups nested loads unchanged, every group top
level. `AppSettings.GroupParents` (`Dictionary<string, string>`, created with `RepoGroupName.Comparer`, always written)
maps a group's name to the name of the group it is inside, or to the empty string for a top-level group; its keys are
the *registered* groups - the groups created inside another group or moved within the hierarchy, and every group that
has a registered group inside it. A registered group exists until it is deleted, even with no repo in it; a group that
is not registered exists only while a repo names it, as before, and is top level. The order of the groups of one level
is the relative order of those groups in the flat `GroupOrder`, so a flat file's order keeps meaning the top-level order
and no second order exists. `GroupParents` reads tolerantly with the same private `StringMapJsonConverter` as
`GroupColors`, and `RepairGroupSettings` (which now runs first, because the hierarchy decides which groups exist)
repairs it through `RepoGroupHierarchy.RepairParents`: unusable or reserved names are dropped, an unknown or self parent
means top level, a cycle is cut at the member that sorts first, and the parent of a registered group is registered too.
The colors and the order are then kept for the groups that exist, registered groups included.
`AppSettings.GetGroupNames` is the groups a repo names plus the registered ones. An older version that reads a newer
file ignores `GroupParents` and shows the groups as a flat list; nothing is lost, because each repo still names its
group. Colors, collapsed state and order of a group are unaffected by where the group is in the tree
(`AgentControl-Settings-GroupHierarchy`, `AgentControl-SettingsStore-GroupParents`, `-GroupParentsDefaults`,
`-InvalidGroupParentsRepaired`, `-RegisteredGroupKept`).

`AgentToolKind` enumerates the well-known agent
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

`AppSettings.KeepShellOpen` is whether the shell window that hosts a launched agent tool stays open after the tool
exits; it is `true` by default, and a settings file without the value (written by an older version) means `true`, so
existing users keep what they had. It is written as a JSON boolean. Reading is tolerant in the same way as the
theme: `AppSettings` declares a private JSON converter that reads `true` and `false` and reads anything else - null,
a string, a number, an object or an array (which it skips whole) - as `true`, because the built-in boolean handling
would throw on those and `SettingsStore.Load` would report the whole file as corrupt
(`AgentControl-Settings-KeepShellOpen`, `AgentControl-SettingsStore-KeepShellOpen`).

`SettingsStore` is a static class with no persistent state of its own; it is called once at
startup (`App.OnFrameworkInitializationCompleted`) to load settings, before the window exists, and its `Save` is then
called only by the main window's `SettingsWriter`, on a worker thread, for every change made in the window and for the
`SettingsWindowViewModel`'s save. It shares its configuration-directory resolution
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

**Background writing.** The main window changes its live `AppSettings` at once and hands the writer a snapshot made by
`AppSettings.CreateSnapshot` (internal): the persisted copy (effective sources, no legacy path) with the repos and the
sources copied entry by entry (`RecentRepo.Clone`, `PackageSourceDefinition.Clone`) and the collapsed sections, group
colors, group order and group hierarchy copied, so the snapshot shares nothing mutable with the live settings and can be
serialized on a worker thread while the window keeps changing them. `SettingsWriter` writes the newest snapshot a short
delay (300 ms) after the last change, one write at a time, through `SettingsStore.Save` on a worker thread; the waits of
the replace retry therefore never reach the UI thread. A confirmed write (`SaveNowAsync`) serves the changes that must
be on disk before they are applied - an add and the Settings window's values. A background write that fails is reported
once per failure streak and its snapshot is kept: the change stays in effect and is written with the next change, the
next flush or the window's close, whose write follows every write still running and is waited for with a bounded wait.
`SettingsStore.Save` asks the `Utilities` subsystem's `BlockingCallGuard` first, so a save that slips back onto the UI
thread fails the headless tests (`AgentControl-Settings-BackgroundWriter`). The order of the writes, the debounce and
the failure streak are `SettingsWriter`'s; which changes are scheduled and which are confirmed is the `LauncherUI`
subsystem's (`MainWindowViewModel`).
