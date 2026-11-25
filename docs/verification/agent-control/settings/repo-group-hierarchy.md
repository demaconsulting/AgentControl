### RepoGroupHierarchy

#### Verification Approach

`RepoGroupHierarchy` is verified with unit tests defined in `RepoGroupHierarchyTests.cs`. The class is pure and holds no
state beyond the values it was built from, so every test builds a hierarchy from literal group names, stored parents and
a stored order and compares the results; nothing is mocked, and no file, window or process is involved. How the settings
loader applies the repair to a stored settings file is verified by the `SettingsStore` unit
(`SettingsStoreGroupHierarchyTests.cs`) and the `Settings` subsystem tests, and how the window shows and edits the tree
by the `LauncherUI` subsystem.

#### Test Environment

N/A - standard test environment (no files, windows or processes are involved).

#### Acceptance Criteria

- All unit tests pass with zero failures.
- The groups of one level are listed in the stored order and then alphabetically, ignoring letter case; with no stored
  parents every group is top level.
- The tree is listed depth first with each group's parent and depth, and the navigation answers (parent, children,
  descendants, subtree, ancestors, depth, inside) are right, ignoring letter case and for unknown names; a tree
  at least 5000 levels deep is handled without failing.
- A group cannot be put inside itself or one of its descendants, nor an unknown group be moved or used as a parent; a
  move registers the moved group and every group it is now inside.
- Deleting a group with its contents kept gives its subgroups to its parent in the group's displayed place among its
  siblings (also when the group had no stored place), leaves no stored entry that names the group or names a subgroup
  twice, and leaves every other level in its order.
- Repair drops entries with an unusable name, keeps the first of names differing in letter case, spells groups as their
  repos do, reads an unknown, self or reserved parent as top level, registers the parent of a registered group, and cuts
  a cycle at the member that sorts first, deterministically and idempotently.

#### Test Scenarios

**RepoGroupHierarchy_ArrangeGroups_StoredOrderThenAlphabetical**: The names `delta`, `Alpha`, `charlie`, `Bravo` and
`ALPHA` with the stored order `CHARLIE`, `Missing`, `delta` are arranged as `charlie`, `delta`, `Alpha`, `Bravo`: stored
places first (found ignoring letter case, the missing name skipped), the others alphabetically, the duplicate dropped;
with no stored order they are alphabetical. This scenario is tested by
`RepoGroupHierarchy_ArrangeGroups_StoredOrderThenAlphabetical`, covering `AgentControl-RepoGroupHierarchy-ArrangeLevel`.

**RepoGroupHierarchy_GetTreeOrder_NoParents_EveryGroupIsTopLevelInStoredOrder**: Three groups with no parents and a
stored order of two of them are listed at depth 0, the ordered ones first, and the stored parents are empty. This
scenario is tested by `RepoGroupHierarchy_GetTreeOrder_NoParents_EveryGroupIsTopLevelInStoredOrder`, covering
`AgentControl-RepoGroupHierarchy-FlatListIsTopLevel`.

**RepoGroupHierarchy_GetTreeOrder_NestedGroups_ListsDepthFirstInSiblingOrder**: A group with two subgroups, one of which
has a subgroup, next to a top-level group, with a stored order that puts one subgroup and the other top-level group
first, is listed depth first with the right parents and depths. This scenario is tested by
`RepoGroupHierarchy_GetTreeOrder_NestedGroups_ListsDepthFirstInSiblingOrder`, covering
`AgentControl-RepoGroupHierarchy-TreeOrder`.

**RepoGroupHierarchy_Navigation_AnswersParentChildrenAncestorsAndDepth**: In a chain of three nested groups next to an
unrelated group, the parent, children (top level included), descendants, subtree, ancestors and depth of each group are
as expected when asked in another letter case, an unknown group answers nothing, and the registered state is right. This
scenario is tested by `RepoGroupHierarchy_Navigation_AnswersParentChildrenAncestorsAndDepth`, covering
`AgentControl-RepoGroupHierarchy-Navigation`.

**RepoGroupHierarchy_DeepTree_WalkedIteratively**: 5000 groups, each inside the previous one, are listed, their
descendants counted and the deepest depth asked without a stack overflow. This scenario is tested by
`RepoGroupHierarchy_DeepTree_WalkedIteratively`, covering `AgentControl-RepoGroupHierarchy-AnyDepth`.

**RepoGroupHierarchy_CanMove_RefusesLoopsAndUnknownGroups**: In a chain of three groups, the bottom group may go to the
top level or inside the top group, the top group may go inside an unrelated group, and the top group may not go inside
itself, its child or its grandchild; unknown groups, the built-in section names and `null` are refused. Moving into a
refused place throws. This scenario is tested by `RepoGroupHierarchy_CanMove_RefusesLoopsAndUnknownGroups` and
`RepoGroupHierarchy_PutInside_NotAllowed_Throws`, covering `AgentControl-RepoGroupHierarchy-RefusesLoops`.

**RepoGroupHierarchy_PutInside_RegistersMovedGroupAndItsNewAncestors**: Moving a group named only by a repo inside the
deepest of a registered chain registers it next to the chain; moving a group to the top level makes its stored parent
empty, and a group moved inside a group that no key registers registers that group as well; an ordinary top-level group
put at the top level is not registered. This scenario is tested by
`RepoGroupHierarchy_PutInside_RegistersMovedGroupAndItsNewAncestors`,
`RepoGroupHierarchy_PutInside_IntoUnregisteredGroup_RegistersIt` and
`RepoGroupHierarchy_PutInside_TopLevelUnregisteredGroup_StaysUnregistered`, covering
`AgentControl-RepoGroupHierarchy-PutInsideRegisters`.

**RepoGroupHierarchy_Pop_SubgroupsGoToTheParentInTheGroupsPlace**: A group in the middle of its parent's subgroups, with
two subgroups of its own, is deleted: the subgroups go to the parent in the group's slot of the stored order, in their
own order, and the group is forgotten, while the top-level order is untouched. A hierarchy built from the result lists
the parent's subgroups in that order at every level. This scenario is tested by
`RepoGroupHierarchy_Pop_SubgroupsGoToTheParentInTheGroupsPlace`, covering
`AgentControl-RepoGroupHierarchy-PopKeepsSubgroups`.

**RepoGroupHierarchy_Pop_GroupWithoutStoredPlace_NestedSubgroupsTakeItsDisplayedPlace**: A nested group that has no
place of its own in the stored order (nothing is stored at all) lists between two siblings; deleting it puts its
subgroups, in their listed order, in its slot between the same siblings, not at their alphabetical slot. This scenario
is tested by `RepoGroupHierarchy_Pop_GroupWithoutStoredPlace_NestedSubgroupsTakeItsDisplayedPlace`, covering
`AgentControl-RepoGroupHierarchy-PopKeepsSubgroups`.

**RepoGroupHierarchy_Pop_TopLevelGroupWithoutStoredPlace_SubgroupsTakeItsDisplayedPlaceAmongTopLevelGroups**: A
top-level group with a subgroup and no stored place, listed between two other top-level groups, is deleted: its
subgroup becomes a top-level group in its slot between them, an unrelated stored name is kept, and a group that does
not exist is refused. This scenario is tested by
`RepoGroupHierarchy_Pop_TopLevelGroupWithoutStoredPlace_SubgroupsTakeItsDisplayedPlaceAmongTopLevelGroups`, covering
`AgentControl-RepoGroupHierarchy-PopKeepsSubgroups`.

**RepoGroupHierarchy_Pop_DeeperNestedGroup_SubgroupsGoToTheirGrandparentInPlace**: A group two levels down with two
subgroups of its own, between two siblings, is deleted: its subgroups go to its parent in its slot, and the levels above
are unchanged. This scenario is tested by
`RepoGroupHierarchy_Pop_DeeperNestedGroup_SubgroupsGoToTheirGrandparentInPlace`, covering
`AgentControl-RepoGroupHierarchy-PopKeepsSubgroups`.

**RepoGroupHierarchy_Pop_DuplicateStoredLater_IsDropped**: A stored order that names the deleted group and its subgroup
more than once, in another letter case the second time, ends up naming the subgroup once and the group not at all, the
other stored names kept. A group with no subgroups only leaves the order
(`RepoGroupHierarchy_Pop_LeafGroup_OnlyForgetsTheGroup`: its siblings keep their order and the stored parents lose only
its key). This scenario is tested by `RepoGroupHierarchy_Pop_DuplicateStoredLater_IsDropped` and
`RepoGroupHierarchy_Pop_LeafGroup_OnlyForgetsTheGroup`, covering `AgentControl-RepoGroupHierarchy-PopForgetsGroup`.

**RepoGroupHierarchy_RepairParents_DropsUnusableEntriesAndTopLevelsBadParents**: Stored parents with a padded name, a
case duplicate, the built-in `Favorites`, a blank name, a self parent, an unknown parent, a reserved parent and a blank
parent are repaired as described: usable keys kept and spelled like their repos, the others dropped or made top level. A
parent that only a repo names is registered, by the repair and by the constructor. This scenario is tested by
`RepoGroupHierarchy_RepairParents_DropsUnusableEntriesAndTopLevelsBadParents`,
`RepoGroupHierarchy_RepairParents_ParentNamedByRepo_IsRegisteredAsTopLevel` and
`RepoGroupHierarchy_Constructor_ParentOfRegisteredGroup_IsRegistered`, covering
`AgentControl-RepoGroupHierarchy-RepairDropsUnusable` and `AgentControl-RepoGroupHierarchy-RepairRegistersParents`.

**RepoGroupHierarchy_RepairParents_Cycle_CutAtFirstMemberDeterministicallyAndIdempotently**: Three groups inside each
other in a circle (with a fourth hanging off it) are repaired the same way whichever order they are stored in and when
the result is repaired again, the first member in ordinal order, ignoring letter case, becoming top level; a hierarchy
built from a damaged value (a cycle, an unknown parent, a self parent, an entry about a group that does not exist) still
lists every group. This scenario is tested by
`RepoGroupHierarchy_RepairParents_Cycle_CutAtFirstMemberDeterministicallyAndIdempotently` and
`RepoGroupHierarchy_Constructor_DamagedParents_StillListsEveryGroup`, covering
`AgentControl-RepoGroupHierarchy-RepairCycles`.
