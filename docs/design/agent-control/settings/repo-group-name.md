### RepoGroupName

![Settings Structure](SettingsView.svg)

#### Purpose

`RepoGroupName` holds the rules for the names of the user's own repo groups: how a typed or stored name is cleaned up,
which names are acceptable, how a stored name is repaired, and how names that differ only in letter case are treated
as one group. A group exists only as a name stored on the recent-repos entries of its members (in the per-user
settings file), so there is no separate list of groups that could disagree with the repos. The rules are one shared,
pure, stateless unit so that the settings loader (which must tolerate any stored text), the view models and the group
name prompt all agree. Its behavior is specified by the unit requirements `AgentControl-RepoGroupName-Normalize`,
`-NormalizeNothingLeft`, `-AcceptsUsableName`, `-RefusesBlankName`, `-RefusesTooLongName`, `-RefusesReservedNames`,
`-SanitizeStoredName`, `-SanitizeCutsLongName`, `-SanitizeReservedIsNoGroup`, `-DistinctNames`,
`-DistinctNamesSorted`, `-DistinctNamesLeaveOutNoGroup`, `-CanonicalizeAdoptsExisting` and `-CanonicalizeKeepsNewName`
(each prefixed `AgentControl-RepoGroupName`).

#### Data Model

`RepoGroupName` is an `internal static class` and holds no state.

**MaxLength**: `const int` — 40, the most characters a group name may have. Longer stored names are cut to this
length and longer typed names are refused, so a header can never be unmanageably wide.

**FavoritesName**, **UngroupedName**: `const string` — `Favorites` and `Ungrouped`, the names of the two built-in
list sections. They are reserved: no group of the user's own may have either name, so the collapsed state of a
built-in section can be stored beside the groups' in `AppSettings.CollapsedGroups`.

**Comparer**: `StringComparer` — `StringComparer.OrdinalIgnoreCase`, the comparison that decides whether two names are
the same group, so `Work` and `work` are one group on every machine and culture.

**RepoGroupNameProblem**: `enum` (same file) — why a typed name cannot be used: `None` (acceptable), `Empty`,
`TooLong` or `Reserved`.

#### Key Methods

**Normalize**: Cleans a name up.

- *Parameters*: `string? name`.
- *Returns*: `string?` — the cleaned name, or `null` when nothing is left.
- *Algorithm*: a single pass in which every white-space or control character ends a word and the words are joined by
  one space, which also trims both ends (`AgentControl-RepoGroupName-Normalize`). A missing, empty or blank name, and
  one made only of white space and control characters, returns `null`, meaning "no group"
  (`AgentControl-RepoGroupName-NormalizeNothingLeft`).

**Validate**: Checks a name the user entered.

- *Parameters*: `string? name` — normalized first.
- *Returns*: `RepoGroupNameProblem`.
- *Postconditions*: `None` for a name that is not empty, has at most `MaxLength` characters and is not reserved
  (`AgentControl-RepoGroupName-AcceptsUsableName`); `Empty` for a name with nothing left after cleaning
  (`-RefusesBlankName`); `TooLong` for a cleaned name over `MaxLength` characters, while one of exactly `MaxLength`
  is accepted (`-RefusesTooLongName`); `Reserved` for `Favorites` or `Ungrouped` in any letter case, also when
  surrounded by white space (`-RefusesReservedNames`).

**Sanitize**: Makes whatever a settings file holds into a usable group name, never failing.

- *Parameters*: `string? stored`.
- *Returns*: `string?` — a valid name, or `null` for no group.
- *Postconditions*: unlike `Validate`, which refuses what a user typed, a stored name is repaired, so a hand-edited or
  damaged file can never keep the application from starting: a usable name is returned cleaned up like a typed one
  (`AgentControl-RepoGroupName-SanitizeStoredName`), a name over `MaxLength` characters is cut to its first
  `MaxLength` characters and cleaned again (`-SanitizeCutsLongName`), and a missing, blank or reserved name returns
  `null` so the repo is shown ungrouped (`-SanitizeReservedIsNoGroup`).

**IsReserved**: Returns whether a name is `FavoritesName` or `UngroupedName`, compared with `Comparer`.

**DistinctNames**: Lists the distinct groups among the names of some repos.

- *Parameters*: `IEnumerable<string?> names` — each is sanitized; must not be `null`.
- *Returns*: `IReadOnlyList<string>` — one name per group, spelled the way the first member that uses the group
  spells it (`AgentControl-RepoGroupName-DistinctNames`), sorted alphabetically with `Comparer`
  (`-DistinctNamesSorted`); repos in no group add nothing (`-DistinctNamesLeaveOutNoGroup`).

**Canonicalize**: Finds how a group is already spelled.

- *Parameters*: `string name` — the valid name to place; `IEnumerable<string?> existing` — the names of the existing
  groups, which may contain `null`. Neither may be `null`.
- *Returns*: `string` — the existing group's spelling when one matches `name` with `Comparer`
  (`AgentControl-RepoGroupName-CanonicalizeAdoptsExisting`), otherwise `name` unchanged
  (`-CanonicalizeKeepsNewName`).

#### Error Handling

`DistinctNames` and `Canonicalize` throw `ArgumentNullException` for a `null` argument. `Normalize`, `Validate`,
`Sanitize` and `IsReserved` never throw: any text, including `null`, produces a result. An unusable name is reported
as a `RepoGroupNameProblem` or repaired, never raised as an error.

#### Dependencies

None beyond the .NET base class library (`System.Text.StringBuilder`, `System.StringComparer`).

#### Callers

- **AppSettings** (same subsystem) — `NormalizeNulls`, run by `SettingsStore.Load`, sanitizes every stored group name,
  gives look-alike groups the first spelling and cleans the collapsed-sections list.
- **RepoCardViewModel**/**MainWindowViewModel** (`LauncherUI`) — read a repo's group through `Sanitize`, validate and
  place a group a user typed, list the existing groups, compare group names and use the built-in section names.
- **GroupNameViewModel** (`LauncherUI`) — the prompt's live validation and the adoption of an existing group's
  spelling.
- **MainWindow** (`LauncherUI` view) — the group-name comparison.
