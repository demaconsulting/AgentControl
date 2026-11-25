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
  parents every group is top level, and group names are cleaned, blank, built-in and repeated ones ignored.
- The tree is listed depth first with each group's parent and depth, and each navigation answer (parent, children,
  descendants, subtree, ancestors, depth, registered, inside, spelling) is right, ignoring letter case, and nothing is
  answered for an unknown name; a tree at least 5000 levels deep is listed, repaired, moved into and popped without
  failing.
- A group cannot be put inside itself or one of its descendants, nor an unknown group be moved or used as a parent, and
  every other move is allowed; a move registers the moved group and every group it is now inside, and a move to the top
  level keeps the registration a group had.
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

**RepoGroupHierarchy_Navigation_Parent_IgnoresLetterCaseAndIsNullAtTopLevel** and the other navigation scenarios: In a
chain of three nested groups next to an unrelated group, each question is asked in another letter case. The parent of
the middle and the bottom group is the group above, a top-level group has none
(`RepoGroupHierarchy_Navigation_Parent_IgnoresLetterCaseAndIsNullAtTopLevel`, covering
`AgentControl-RepoGroupHierarchy-NavigationParent`); the children of the top group are the middle one, those of the top
level are the unrelated group and the top group, and the bottom group has none
(`RepoGroupHierarchy_Navigation_Children_ListsTheGroupsDirectlyInside`, `-NavigationChildren`); the descendants of the
top group are the middle and the bottom group, depth first
(`RepoGroupHierarchy_Navigation_Descendants_ListsEveryGroupInsideDepthFirst`, `-NavigationDescendants`); the subtree is
the group first and then those (`RepoGroupHierarchy_Navigation_Subtree_ListsTheGroupThenItsDescendants`,
`-NavigationSubtree`); the ancestors of the bottom group are the middle and the top group, nearest first
(`RepoGroupHierarchy_Navigation_Ancestors_ListsTheGroupsItIsInsideNearestFirst`, `-NavigationAncestors`); the depths are
2, 1, 0 and 0 (`RepoGroupHierarchy_Navigation_Depth_CountsTheGroupsItIsInside`, `-NavigationDepth`); the chain's groups
are registered and the unrelated one is not, and which groups exist is answered
(`RepoGroupHierarchy_Navigation_Registered_TellsGroupsInTheHierarchyFromOthers`, `-NavigationRegistered`); a group is
inside itself and its ancestors, not the other way round, and `null` is inside nothing
(`RepoGroupHierarchy_Navigation_IsSameOrDescendant_TellsSelfDescendantsAndOthersApart`, `-NavigationSameOrDescendant`);
`tOP` is spelled `Top` (`RepoGroupHierarchy_Navigation_Name_GivesTheGroupsOwnSpelling`, `-NavigationSpelling`); and a
name that is no group, or `null`, gets `null`, an empty list, depth 0 or false from every question, the group list
holding the four groups once each (`RepoGroupHierarchy_Navigation_UnknownName_AnswersNothingAndNeverFails`,
`-NavigationUnknownName`).

**RepoGroupHierarchy_DeepTree_WalkedIteratively**: 5000 groups, each inside the previous one, are listed, their
descendants counted and the deepest depth asked without a stack overflow
(`RepoGroupHierarchy_DeepTree_WalkedIteratively`). A cycle that closes such a chain is repaired by making the first
group top level, a group is put inside the deepest group with every group of the chain staying registered, and a group
in the middle is deleted with its subgroup going to the group above
(`RepoGroupHierarchy_DeepTree_RepairPutInsideAndPopWork`). These scenarios cover
`AgentControl-RepoGroupHierarchy-AnyDepth`.

**RepoGroupHierarchy_CanMove_AllowsTopLevelAndOtherGroups** and the other move scenarios: In a chain of three groups,
the bottom group may go to the top level or inside the top group and the top group may go inside an unrelated group,
also asked in another letter case (`RepoGroupHierarchy_CanMove_AllowsTopLevelAndOtherGroups`, covering
`AgentControl-RepoGroupHierarchy-AllowsTopLevelAndOtherGroups`); the top group may not go inside itself, its child or
its grandchild and moving there throws (`RepoGroupHierarchy_CanMove_RefusesSelfAndDescendants`, covering
`AgentControl-RepoGroupHierarchy-RefusesLoops`); and unknown groups, the built-in section names and `null` are refused
as the group and as the parent, moving there throwing
(`RepoGroupHierarchy_CanMove_RefusesUnknownGroupsAndBuiltInSections`, covering
`AgentControl-RepoGroupHierarchy-RefusesUnknownGroups`).

**RepoGroupHierarchy_PutInside_RegistersMovedGroupAndItsNewAncestors**: Moving a group named only by a repo inside the
deepest of a registered chain registers it next to the chain, and a group moved inside a group that no key registers
registers that group as well. This scenario is tested by
`RepoGroupHierarchy_PutInside_RegistersMovedGroupAndItsNewAncestors` and
`RepoGroupHierarchy_PutInside_IntoUnregisteredGroup_RegistersIt`, covering
`AgentControl-RepoGroupHierarchy-PutInsideRegistersMovedGroupAndAncestors`. Moving a group to the top level makes its
stored parent empty and keeps it registered, and an ordinary top-level group put at the top level is not registered
(`RepoGroupHierarchy_PutInside_TopLevelRegisteredGroup_StaysRegistered` and
`RepoGroupHierarchy_PutInside_TopLevelUnregisteredGroup_StaysUnregistered`, covering
`AgentControl-RepoGroupHierarchy-PutInsideTopLevelKeepsRegistration`).

**RepoGroupHierarchy_Constructor_GroupNamesSanitizedAndNullParentsAndOrderAreFlat**: Group names with padding, in two
letter cases, a blank, a `null`, `Favorites` and `UNGROUPED`, built with no parents and no order, give the cleaned names
once each, alphabetically, all at the top level with nothing stored. This scenario is tested by
`RepoGroupHierarchy_Constructor_GroupNamesSanitizedAndNullParentsAndOrderAreFlat`, covering
`AgentControl-RepoGroupHierarchy-GroupNamesSanitized` and `AgentControl-RepoGroupHierarchy-FlatListIsTopLevel`.
**RepoGroupHierarchy_NullGroupNames_AreRefused**: a `null` list of group names to the constructor and of repo group
names to the repair throws `ArgumentNullException`
(`RepoGroupHierarchy_NullGroupNames_AreRefused`, covering `AgentControl-RepoGroupHierarchy-RefusesNullInputs`).

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
Deleting a group that does not exist, or `Favorites`, throws `ArgumentException`
(`RepoGroupHierarchy_Pop_UnknownGroup_IsRefused`, covering `AgentControl-RepoGroupHierarchy-PopUnknownGroupRefused`).

**RepoGroupHierarchy_RepairParents_DropsBlankAndReservedKeys** and the other repair scenarios: Entries named
`Favorites`, `ungrouped`, blank or empty are dropped and nothing stored gives an empty result
(`RepoGroupHierarchy_RepairParents_DropsBlankAndReservedKeys`, covering
`AgentControl-RepoGroupHierarchy-RepairDropsBlankAndReservedKeys`); of a padded `work` and a later `WORK` the first wins
(`RepoGroupHierarchy_RepairParents_KeepsTheFirstOfNamesDifferingInLetterCase`, `-RepairKeepsFirstCaseVariant`); a key
`work` with the parent `CLIENT` is spelled `Work` inside `Client` as the repos spell them
(`RepoGroupHierarchy_RepairParents_SpellsAGroupLikeItsRepos`, `-RepairSpellsLikeRepos`); a self, unknown, reserved,
blank or empty parent is read as top level (`RepoGroupHierarchy_RepairParents_ReadsABadParentAsTopLevel`,
`-RepairReadsBadParentAsTopLevel`); and a parent that only a repo names is registered, by the repair and by the
constructor (`RepoGroupHierarchy_RepairParents_ParentNamedByRepo_IsRegisteredAsTopLevel` and
`RepoGroupHierarchy_Constructor_ParentOfRegisteredGroup_IsRegistered`, `-RepairRegistersParents`).

**RepoGroupHierarchy_RepairParents_Cycle_CutAtFirstMemberOfTheCycle** and the other cycle scenarios: Three groups inside
each other in a circle, with a fourth hanging off it, are repaired by making the first member in ordinal order, ignoring
letter case, top level, the others staying inside each other
(`RepoGroupHierarchy_RepairParents_Cycle_CutAtFirstMemberOfTheCycle`); a cycle of two, a cycle of five with a group
hanging off it and a chain leading into the two-cycle are each cut at their own first member and the chain and the
hanging group are kept (`RepoGroupHierarchy_RepairParents_DisjointLongAndDeepCycles_EachCutAtItsFirstMember`); and the
constructor cuts a cycle whose names have mixed letter case the same way
(`RepoGroupHierarchy_Constructor_CycleWithMixedLetterCase_CutAtFirstMember`). These cover
`AgentControl-RepoGroupHierarchy-RepairCutsCycles`. The result is the same whichever order the entries are stored in,
whatever their letter case, and when the result is repaired again, and the constructor's stored parents given back give
the same stored parents (`RepoGroupHierarchy_RepairParents_Cycle_IsDeterministicAndIdempotent` and
`RepoGroupHierarchy_Constructor_CycleWithMixedLetterCase_CutAtFirstMember`, covering
`AgentControl-RepoGroupHierarchy-RepairIsDeterministicAndIdempotent`). A hierarchy built from a damaged value (a cycle,
an unknown parent, a self parent, an entry about a group that does not exist) still lists every group
(`RepoGroupHierarchy_Constructor_DamagedParents_StillListsEveryGroup`, covering
`AgentControl-RepoGroupHierarchy-DamagedTreeStillListsEveryGroup`).
