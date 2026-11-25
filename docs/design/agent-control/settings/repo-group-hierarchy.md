### RepoGroupHierarchy

![Settings Structure](SettingsView.svg)

#### Purpose

`RepoGroupHierarchy` holds the rules for how the user's repo groups nest: which group is inside which, in what order the
groups of one level are listed, which moves keep the tree a tree, what deleting a group with its contents kept does to
the tree, and how stored parents are repaired. The rules are one shared, pure unit so that the settings loader
(`AppSettings.RepairGroupSettings`, which must tolerate any stored text) and the main window's view model (which shows,
moves and deletes groups) agree. Its behavior is specified by the unit requirements
`AgentControl-RepoGroupHierarchy-ArrangeLevel`, `-FlatListIsTopLevel`, `-TreeOrder`, `-Navigation`, `-AnyDepth`,
`-RefusesLoops`, `-PutInsideRegisters`, `-PopKeepsSubgroups`, `-RepairDropsUnusable`, `-RepairRegistersParents` and
`-RepairCycles` (each prefixed `AgentControl-RepoGroupHierarchy`).

**Design decisions** (recorded here because the requirements say what, not how):

- **Names stay globally unique.** A group is still identified by its name alone (compared with
  `RepoGroupName.Comparer`) wherever it is - in `RecentRepo.GroupName`, `CollapsedGroups`, `GroupColors`, `GroupOrder`,
  drag data and drop targets - so the file format and every existing key stay valid and a file written before groups
  nested loads unchanged. The limitation is that two groups at different places of the tree cannot share a name.
- **The hierarchy is stored as `AppSettings.GroupParents`**, child name to parent name, the empty string meaning
  "registered, top level". **The order of the groups of one level is the relative order of those groups inside the
  existing flat `GroupOrder`**, so no second order structure exists and a flat file's `GroupOrder` keeps meaning "the
  top-level order".
- **Registered groups.** A group is *registered* when it is a key of `GroupParents`: it was created inside another
  group or moved within the hierarchy, and every ancestor of a registered group is registered too. A registered group
  exists until it is deleted, even with no repo in it; a group that is not registered keeps the original rule - it
  exists only while a repo names it. `AppSettings.GetGroupNames` is the single source for "which groups exist".
- **No recursion.** Every walk uses an explicit stack or a loop, so a hand-edited file that claims thousands of levels
  cannot overflow the call stack (`AgentControl-RepoGroupHierarchy-AnyDepth`).

#### Data Model

`RepoGroupHierarchy` is an `internal sealed class`: an immutable, repaired view built once from three inputs and then
only read. `RepoGroupNode` (`internal readonly record struct`, same file) is one entry of the depth-first listing:
`Name`, `Parent` (`null` for a top-level group) and `Depth` (0 for a top-level group).

**Constructor inputs**: the names of the groups that exist (`AppSettings.GetGroupNames`), the stored parents
(`AppSettings.GroupParents`, may be `null`) and the stored order (`AppSettings.GroupOrder`, may be `null`).

**Private state** (all keyed with `RepoGroupName.Comparer`): `_spellings` (each existing group, spelled once),
`_registered` (the keys of the stored parents that name an existing group), `_parents` (child to parent, nested groups
only), `_children` (the groups of each level in listing order, the top level under the empty string) and `_order` (a
copy of the stored order).

**Invariants**: the tree has no cycle and no group is its own parent (the constructor applies the same repair as
`RepairParents`); a group whose stored parent does not exist is top level; every existing group is in exactly one level.

#### Key Methods

**ArrangeGroups** (static): Puts group names in the order the list shows them.

- *Parameters*: `IEnumerable<string> names`, `IReadOnlyList<string>? order`.
- *Returns*: `List<string>` — the names with a stored position in the stored order, then the others alphabetically
  (ordinal, ignoring letter case); duplicates ignoring letter case are dropped, a stored name matching no group is
  skipped, and names keep their spelling from `names` (`AgentControl-RepoGroupHierarchy-ArrangeLevel`).

**RepairParents** (static): Brings stored parents in line with the groups that exist.

- *Parameters*: `IEnumerable<KeyValuePair<string,string>>? stored`, `IEnumerable<string?> repoGroupNames` (must not be
  `null`).
- *Returns*: `Dictionary<string,string>` — a new dictionary whose keys are the registered groups.
- *Algorithm*: every key and parent goes through `RepoGroupName.Sanitize`; an entry with a blank or reserved key is
  dropped and, of keys differing only in letter case, the first wins, spelled as the repos spell the group
  (`-RepairDropsUnusable`); a parent that is blank, reserved, the group itself, or neither a registered group nor a
  group a repo is in means top level; cycles are cut at the member that sorts first, ignoring letter case, which does
  not depend on the order the entries are stored in and so is deterministic and idempotent (`-RepairCycles`); finally
  the parent of every registered group is registered (a parent only a repo names becomes a top-level key,
  `-RepairRegistersParents`).

**Constructor**: Builds the repaired view. It is tolerant by design: an entry about a group that does not exist is
ignored, a parent that does not exist or is the group itself means top level and a cycle is cut like `RepairParents`
does, so a damaged file still shows every group (`-RepairCycles`).

**GetTreeOrder / GetChildren / GetDescendants / GetSubtree / GetAncestors / GetDepth / GetParent / IsSameOrDescendant /
Contains / IsRegistered / GetName**: Read-only answers about the tree, all ignoring letter case and returning the
spelling the group has. `GetTreeOrder` lists every group depth first, each followed by its subtree, with the groups of
a level in `ArrangeGroups` order (`-TreeOrder`, `-FlatListIsTopLevel` for a file without a hierarchy);
the others answer `-Navigation`. An unknown name gives `null`, an empty list or depth 0, never an exception.

**CanMove / PutInside**: `CanMove(group, newParent)` is `false` for a group or parent that does not exist (the built-in
sections never do) and when the new parent is the group itself or one of its descendants (`-RefusesLoops`);
`newParent == null` means the top level. `PutInside` returns the new stored parents: a group put inside another group is
registered together with every group it is now inside, a group put at the top level keeps the registration it had (an
ordinary top-level group that is only reordered stays unregistered, so flat lists behave exactly as before)
(`-PutInsideRegisters`); it does not change the order, which the caller places among the new
siblings. *Preconditions*: `CanMove` is `true`; otherwise `InvalidOperationException`.

**Pop**: The stored values after the group is deleted but its contents are kept: its subgroups get the group's parent
(or the top level) and take the group's place in the stored order, in their own order; a group with no stored place
leaves them where they are; the group's key is forgotten (`-PopKeepsSubgroups`). It does not touch repos: moving the
group's own repos to the parent group is the caller's job. *Preconditions*: the group exists; otherwise
`ArgumentException`.

**GetStoredParents**: The stored form of the repaired hierarchy (registered groups only, empty string for top level),
used by `PutInside` and `Pop`.

#### Error Handling

Reading never throws: any text, `null` or damaged structure produces a repaired result (`RepairParents`, the
constructor, the navigation methods). `ArgumentNullException` is thrown by the constructor for `null` group names and by
`RepairParents` for `null` repo group names. `PutInside` throws `InvalidOperationException` for a move `CanMove` refuses
and `Pop` throws `ArgumentException` for a group that does not exist; both are programming errors in the caller, which
asks `CanMove` / checks existence first.

#### Dependencies

`RepoGroupName` (same subsystem) — the comparer, `Sanitize`, and the reserved-name rule. Otherwise only the .NET base
class library.

#### Callers

- **AppSettings** (same subsystem) — `RepairGroupSettings`, run by `SettingsStore.Load` and after every change the main
  window saves, repairs the stored parents through `RepairParents`.
- **MainWindowViewModel** (`LauncherUI`) — builds the group tree it lists from the groups, parents and order in the
  settings, asks it whether a move is allowed, and arranges the groups of one level with `ArrangeGroups`.
