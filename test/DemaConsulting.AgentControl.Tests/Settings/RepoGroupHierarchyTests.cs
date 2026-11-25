// Copyright (c) DEMA Consulting
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using DemaConsulting.AgentControl.Settings;

namespace DemaConsulting.AgentControl.Tests.Settings;

/// <summary>
///     Unit tests for <see cref="RepoGroupHierarchy"/>: how groups nest, in what order each level is
///     listed, which moves are allowed, how a group is deleted with its contents kept, and how stored
///     parents are repaired.
/// </summary>
public class RepoGroupHierarchyTests
{
    /// <summary>
    ///     Test that the groups of one level are listed in the stored order first and the others
    ///     alphabetically, ignoring letter case and names that match no group.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_ArrangeGroups_StoredOrderThenAlphabetical()
    {
        // Arrange
        string[] names = ["delta", "Alpha", "charlie", "Bravo", "ALPHA"];

        // Act
        var arranged = RepoGroupHierarchy.ArrangeGroups(names, ["CHARLIE", "Missing", "delta"]);
        var withoutOrder = RepoGroupHierarchy.ArrangeGroups(names, null);

        // Assert
        Assert.Equal(["charlie", "delta", "Alpha", "Bravo"], arranged);
        Assert.Equal(["Alpha", "Bravo", "charlie", "delta"], withoutOrder);
    }

    /// <summary>
    ///     Test that a hierarchy with no stored parents lists every group at the top level in the
    ///     stored order, exactly as a flat list of groups always did.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_GetTreeOrder_NoParents_EveryGroupIsTopLevelInStoredOrder()
    {
        // Arrange
        var hierarchy = new RepoGroupHierarchy(["Work", "Home", "Admin"], null, ["Work", "Home"]);

        // Act
        var nodes = hierarchy.GetTreeOrder();

        // Assert
        Assert.Equal(
            [new RepoGroupNode("Work", null, 0), new RepoGroupNode("Home", null, 0), new RepoGroupNode("Admin", null, 0)],
            nodes);
        Assert.Empty(hierarchy.GetStoredParents());
    }

    /// <summary>
    ///     Test that the tree is listed depth first, each group followed by its subtree, with the
    ///     siblings of every level in their order and with the parent and depth of each group.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_GetTreeOrder_NestedGroups_ListsDepthFirstInSiblingOrder()
    {
        // Arrange: Client has Beta (first, by the stored order) and Alpha; Beta has Gamma
        var hierarchy = new RepoGroupHierarchy(
            ["Client", "Alpha", "Beta", "Gamma", "Home"],
            new Dictionary<string, string>
            {
                ["Client"] = string.Empty,
                ["Alpha"] = "Client",
                ["Beta"] = "Client",
                ["Gamma"] = "Beta"
            },
            ["Beta", "Home", "Client"]);

        // Act
        var nodes = hierarchy.GetTreeOrder();

        // Assert
        Assert.Equal(
            [
                new RepoGroupNode("Home", null, 0),
                new RepoGroupNode("Client", null, 0),
                new RepoGroupNode("Beta", "Client", 1),
                new RepoGroupNode("Gamma", "Beta", 2),
                new RepoGroupNode("Alpha", "Client", 1)
            ],
            nodes);
    }

    /// <summary>
    ///     Test that the parent of a group is answered ignoring letter case, and is <see langword="null"/>
    ///     for a top-level group.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Parent_IgnoresLetterCaseAndIsNullAtTopLevel()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Null(hierarchy.GetParent("Top")),
            () => Assert.Null(hierarchy.GetParent("elsewhere")),
            () => Assert.Equal("Middle", hierarchy.GetParent("bottom")),
            () => Assert.Equal("Top", hierarchy.GetParent("MIDDLE")));
    }

    /// <summary>
    ///     Test that the children of a group, or of the top level, are the groups directly inside it in
    ///     their listing order, and none for a group with no subgroup.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Children_ListsTheGroupsDirectlyInside()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal(["Middle"], hierarchy.GetChildren("TOP")),
            () => Assert.Equal(["Elsewhere", "Top"], hierarchy.GetChildren(null)),
            () => Assert.Empty(hierarchy.GetChildren("Bottom")));
    }

    /// <summary>
    ///     Test that the descendants of a group are every group anywhere inside it, depth first, and none
    ///     for a group with no subgroup.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Descendants_ListsEveryGroupInsideDepthFirst()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal(["Middle", "Bottom"], hierarchy.GetDescendants("top")),
            () => Assert.Empty(hierarchy.GetDescendants("Bottom")));
    }

    /// <summary>
    ///     Test that the subtree of a group is the group itself followed by its descendants.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Subtree_ListsTheGroupThenItsDescendants()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal(["Top", "Middle", "Bottom"], hierarchy.GetSubtree("TOP")),
            () => Assert.Equal(["Bottom"], hierarchy.GetSubtree("bottom")));
    }

    /// <summary>
    ///     Test that the ancestors of a group are the groups it is inside, nearest first, and none for a
    ///     top-level group.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Ancestors_ListsTheGroupsItIsInsideNearestFirst()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal(["Middle", "Top"], hierarchy.GetAncestors("Bottom")),
            () => Assert.Equal(["Top"], hierarchy.GetAncestors("middle")),
            () => Assert.Empty(hierarchy.GetAncestors("Top")));
    }

    /// <summary>
    ///     Test that the depth of a group is how many groups it is inside.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Depth_CountsTheGroupsItIsInside()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal(2, hierarchy.GetDepth("Bottom")),
            () => Assert.Equal(1, hierarchy.GetDepth("MIDDLE")),
            () => Assert.Equal(0, hierarchy.GetDepth("Top")),
            () => Assert.Equal(0, hierarchy.GetDepth("Elsewhere")));
    }

    /// <summary>
    ///     Test that whether a group is registered, and whether it exists, are answered ignoring letter
    ///     case; the chain's groups are registered, an unrelated group is not.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Registered_TellsGroupsInTheHierarchyFromOthers()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.True(hierarchy.IsRegistered("middle")),
            () => Assert.True(hierarchy.IsRegistered("TOP")),
            () => Assert.False(hierarchy.IsRegistered("Elsewhere")),
            () => Assert.False(hierarchy.IsRegistered("Unknown")),
            () => Assert.True(hierarchy.Contains("elsewhere")),
            () => Assert.False(hierarchy.Contains("Unknown")));
    }

    /// <summary>
    ///     Test that whether a group is another group or anywhere inside it is answered ignoring letter
    ///     case, a group counting as inside itself.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_IsSameOrDescendant_TellsSelfDescendantsAndOthersApart()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.True(hierarchy.IsSameOrDescendant("bottom", "TOP")),
            () => Assert.True(hierarchy.IsSameOrDescendant("Top", "top")),
            () => Assert.False(hierarchy.IsSameOrDescendant("Top", "Bottom")),
            () => Assert.False(hierarchy.IsSameOrDescendant("Elsewhere", "Top")),
            () => Assert.False(hierarchy.IsSameOrDescendant(null, "Top")),
            () => Assert.False(hierarchy.IsSameOrDescendant("Top", null)));
    }

    /// <summary>
    ///     Test that the spelling a group has in the tree is answered for a name in any letter case.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_Name_GivesTheGroupsOwnSpelling()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Equal("Top", hierarchy.GetName("tOP")),
            () => Assert.Equal("Elsewhere", hierarchy.GetName("ELSEWHERE")),
            () => Assert.Null(hierarchy.GetName("Unknown")),
            () => Assert.Null(hierarchy.GetName(null)));
    }

    /// <summary>
    ///     Test that every question about a name that is no group - or about <see langword="null"/> - is
    ///     answered with nothing (null, an empty list, depth 0, false) and never fails; the group list holds
    ///     every group once.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_UnknownName_AnswersNothingAndNeverFails()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Null(hierarchy.GetParent("Unknown")),
            () => Assert.Null(hierarchy.GetParent(null)),
            () => Assert.Empty(hierarchy.GetChildren("Unknown")),
            () => Assert.Empty(hierarchy.GetDescendants("Unknown")),
            () => Assert.Empty(hierarchy.GetDescendants(null)),
            () => Assert.Empty(hierarchy.GetSubtree("Unknown")),
            () => Assert.Empty(hierarchy.GetAncestors("Unknown")),
            () => Assert.Empty(hierarchy.GetAncestors(null)),
            () => Assert.Equal(0, hierarchy.GetDepth("Unknown")),
            () => Assert.Equal(0, hierarchy.GetDepth(null)),
            () => Assert.False(hierarchy.IsSameOrDescendant("Unknown", "Top")),
            () => Assert.False(hierarchy.IsRegistered(null)),
            () => Assert.False(hierarchy.Contains(null)),
            () => Assert.Equal(["Bottom", "Elsewhere", "Middle", "Top"], hierarchy.Groups.Order().ToArray()));
    }

    /// <summary>
    ///     Test that a group may be put at the top level or inside any group that is not itself or one
    ///     of its descendants.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_CanMove_AllowsTopLevelAndOtherGroups()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.True(hierarchy.CanMove("Bottom", null)),
            () => Assert.True(hierarchy.CanMove("Bottom", "Top")),
            () => Assert.True(hierarchy.CanMove("Top", "Elsewhere")),
            () => Assert.True(hierarchy.CanMove("top", "ELSEWHERE")));
    }

    /// <summary>
    ///     Test that a group may not be put inside itself or inside one of its descendants, and that
    ///     <see cref="RepoGroupHierarchy.PutInside"/> throws for such a move instead of making a loop.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_CanMove_RefusesSelfAndDescendants()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.False(hierarchy.CanMove("Top", "Top")),
            () => Assert.False(hierarchy.CanMove("Top", "middle")),
            () => Assert.False(hierarchy.CanMove("Top", "Bottom")),
            () => Assert.False(hierarchy.CanMove("Middle", "Bottom")));
        Assert.Throws<InvalidOperationException>(() => hierarchy.PutInside("Top", "Bottom"));
        Assert.Throws<InvalidOperationException>(() => hierarchy.PutInside("Top", "Top"));
    }

    /// <summary>
    ///     Test that a group that does not exist (the built-in sections never are groups) cannot be moved
    ///     or be a parent, and that <see cref="RepoGroupHierarchy.PutInside"/> throws for such a move.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_CanMove_RefusesUnknownGroupsAndBuiltInSections()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.False(hierarchy.CanMove("Unknown", null)),
            () => Assert.False(hierarchy.CanMove("Top", "Unknown")),
            () => Assert.False(hierarchy.CanMove("Favorites", null)),
            () => Assert.False(hierarchy.CanMove("Top", "Ungrouped")),
            () => Assert.False(hierarchy.CanMove(null, null)));
        Assert.Throws<InvalidOperationException>(() => hierarchy.PutInside("Unknown", null));
        Assert.Throws<InvalidOperationException>(() => hierarchy.PutInside("Top", "Favorites"));
    }

    /// <summary>
    ///     Test that moving a group inside another registers the moved group and every group it is now
    ///     inside, so they exist until deleted, and leaves the hierarchy it was asked on unchanged.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_PutInside_RegistersMovedGroupAndItsNewAncestors()
    {
        // Arrange: Home and Loose are only named by repos (not registered); Top > Middle > Bottom
        var hierarchy = new RepoGroupHierarchy(
            ["Top", "Middle", "Bottom", "Loose"],
            new Dictionary<string, string>
            {
                ["Top"] = string.Empty,
                ["Middle"] = "Top",
                ["Bottom"] = "Middle"
            },
            null);

        // Act
        var parents = hierarchy.PutInside("loose", "BOTTOM");
        var toTop = hierarchy.PutInside("Middle", null);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(
                new Dictionary<string, string>
                {
                    ["Top"] = string.Empty,
                    ["Middle"] = "Top",
                    ["Bottom"] = "Middle",
                    ["Loose"] = "Bottom"
                },
                parents),
            () => Assert.Equal("Top", hierarchy.GetParent("Middle")),
            () => Assert.Equal(string.Empty, toTop["Middle"]),
            () => Assert.Equal("Middle", toTop["Bottom"]),
            () => Assert.Equal(3, toTop.Count));
    }

    /// <summary>
    ///     Test that moving a group into a group that is only named by a repo registers that group too.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_PutInside_IntoUnregisteredGroup_RegistersIt()
    {
        // Arrange
        var hierarchy = new RepoGroupHierarchy(["Alpha", "Beta"], null, null);

        // Act
        var parents = hierarchy.PutInside("Beta", "alpha");

        // Assert
        Assert.Equal(
            new Dictionary<string, string> { ["Alpha"] = string.Empty, ["Beta"] = "Alpha" },
            parents);
    }

    /// <summary>
    ///     Test that putting an ordinary top-level group at the top level changes nothing: it is not
    ///     registered by being reordered.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_PutInside_TopLevelUnregisteredGroup_StaysUnregistered()
    {
        // Arrange
        var hierarchy = new RepoGroupHierarchy(["Alpha", "Beta"], null, null);

        // Act
        var parents = hierarchy.PutInside("Beta", null);

        // Assert
        Assert.Empty(parents);
    }

    /// <summary>
    ///     Test that putting a registered group at the top level keeps it registered, with the empty
    ///     string as its parent, and keeps the groups below it registered.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_PutInside_TopLevelRegisteredGroup_StaysRegistered()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act
        var parents = hierarchy.PutInside("Middle", null);

        // Assert
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Top"] = string.Empty,
                ["Middle"] = string.Empty,
                ["Bottom"] = "Middle"
            },
            parents);
    }

    /// <summary>
    ///     Test that deleting a group with its contents kept moves its subgroups to its parent, in
    ///     their own order and in the group's place in the stored order, forgets the group, and leaves
    ///     the order of every other level as it was.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_SubgroupsGoToTheParentInTheGroupsPlace()
    {
        // Arrange: Root has Before, Mid, After; Mid has Two (first) and One; Root is also on the top level
        var groups = new[] { "Root", "Before", "Mid", "After", "One", "Two", "Other" };
        var hierarchy = new RepoGroupHierarchy(
            groups,
            new Dictionary<string, string>
            {
                ["Root"] = string.Empty,
                ["Before"] = "Root",
                ["Mid"] = "Root",
                ["After"] = "Root",
                ["One"] = "Mid",
                ["Two"] = "Mid"
            },
            ["Before", "Two", "Mid", "After", "Root", "One"]);

        // Act
        var (parents, order) = hierarchy.Pop("mid");
        var after = new RepoGroupHierarchy(["Root", "Before", "After", "One", "Two", "Other"], parents, order);

        // Assert
        Assert.Multiple(
            () => Assert.False(parents.ContainsKey("Mid")),
            () => Assert.Equal("Root", parents["One"]),
            () => Assert.Equal("Root", parents["Two"]),
            () => Assert.DoesNotContain("Mid", order),
            () => Assert.Equal(["Before", "Two", "One", "After"], after.GetChildren("Root")),
            () => Assert.Equal(["Root", "Other"], after.GetChildren(null)));
    }

    /// <summary>
    ///     Test that deleting a nested group that was never dragged (so it has no stored place) puts its
    ///     subgroups where the group was listed among its siblings, not at their alphabetical slot,
    ///     and leaves the other levels as they were.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_GroupWithoutStoredPlace_NestedSubgroupsTakeItsDisplayedPlace()
    {
        // Arrange: Root lists Alpha, Mid, Zulu alphabetically (no stored order at all); Mid lists One, Two
        string[] groups = ["Root", "Alpha", "Mid", "Zulu", "One", "Two"];
        var hierarchy = new RepoGroupHierarchy(
            groups,
            new Dictionary<string, string>
            {
                ["Root"] = string.Empty,
                ["Alpha"] = "Root",
                ["Mid"] = "Root",
                ["Zulu"] = "Root",
                ["One"] = "Mid",
                ["Two"] = "Mid"
            },
            null);

        // Act
        var (parents, order) = hierarchy.Pop("Mid");
        var after = new RepoGroupHierarchy(["Root", "Alpha", "Zulu", "One", "Two"], parents, order);

        // Assert: Alpha, One, Two, Zulu (the subgroups in Mid's slot) - not One, Two after Zulu or before Alpha
        Assert.Multiple(
            () => Assert.Equal(["Alpha", "One", "Two", "Zulu"], after.GetChildren("Root")),
            () => Assert.Equal(["Root"], after.GetChildren(null)),
            () => Assert.Equal("Root", after.GetParent("One")),
            () => Assert.False(parents.ContainsKey("Mid")));
    }

    /// <summary>
    ///     Test that deleting a top-level group with no stored place puts its subgroups where the group
    ///     was listed among the top-level groups, makes them top level, and that a group that does not
    ///     exist is refused.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_TopLevelGroupWithoutStoredPlace_SubgroupsTakeItsDisplayedPlaceAmongTopLevelGroups()
    {
        // Arrange: the top level lists Alpha, Parent, Zulu alphabetically; Parent has Child; Other is in the order only
        string[] groups = ["Parent", "Child", "Alpha", "Zulu"];
        var hierarchy = new RepoGroupHierarchy(
            groups,
            new Dictionary<string, string> { ["Parent"] = string.Empty, ["Child"] = "Parent" },
            ["Other"]);

        // Act
        var (parents, order) = hierarchy.Pop("Parent");
        var after = new RepoGroupHierarchy(["Child", "Alpha", "Zulu"], parents, order);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(new Dictionary<string, string> { ["Child"] = string.Empty }, parents),
            () => Assert.Equal(["Alpha", "Child", "Zulu"], after.GetChildren(null)),
            () => Assert.Contains("Other", order),
            () => Assert.Throws<ArgumentException>(() => hierarchy.Pop("Unknown")));
    }

    /// <summary>
    ///     Test that a stored order that names the deleted group or its subgroups more than once ends up
    ///     naming the subgroups once and the group not at all.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_DuplicateStoredLater_IsDropped()
    {
        // Arrange: Mid and One are stored twice, in another letter case the second time
        var hierarchy = new RepoGroupHierarchy(
            ["Root", "Mid", "One", "Other"],
            new Dictionary<string, string> { ["Root"] = string.Empty, ["Mid"] = "Root", ["One"] = "Mid" },
            ["Mid", "Other", "One", "MID", "one", "Root"]);

        // Act
        var (_, order) = hierarchy.Pop("Mid");

        // Assert: One once, no Mid, Other and Root still there
        Assert.Multiple(
            () => Assert.Single(order, entry => string.Equals(entry, "One", StringComparison.OrdinalIgnoreCase)),
            () => Assert.DoesNotContain(order, entry => string.Equals(entry, "Mid", StringComparison.OrdinalIgnoreCase)),
            () => Assert.Contains("Other", order),
            () => Assert.Contains("Root", order));
    }

    /// <summary>
    ///     Test that deleting a group with no subgroups only forgets it: its siblings keep their order
    ///     and nothing else changes in the stored parents.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_LeafGroup_OnlyForgetsTheGroup()
    {
        // Arrange
        var hierarchy = new RepoGroupHierarchy(
            ["Root", "A", "Leaf", "C"],
            new Dictionary<string, string>
            {
                ["Root"] = string.Empty,
                ["A"] = "Root",
                ["Leaf"] = "Root",
                ["C"] = "Root"
            },
            ["C", "Leaf", "A"]);

        // Act
        var (parents, order) = hierarchy.Pop("Leaf");
        var after = new RepoGroupHierarchy(["Root", "A", "C"], parents, order);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(
                new Dictionary<string, string> { ["Root"] = string.Empty, ["A"] = "Root", ["C"] = "Root" }, parents),
            () => Assert.Equal(["C", "A"], after.GetChildren("Root")),
            () => Assert.DoesNotContain("Leaf", order));
    }

    /// <summary>
    ///     Test that deleting a group nested more than one level deep gives its subgroups to the group it
    ///     was inside, in its place there, and leaves the groups above it as they were.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_DeeperNestedGroup_SubgroupsGoToTheirGrandparentInPlace()
    {
        // Arrange: Top > Mid > (X, Deep, Y); Deep > (D1, D2); Top also has Side
        string[] groups = ["Top", "Side", "Mid", "X", "Deep", "Y", "D1", "D2"];
        var hierarchy = new RepoGroupHierarchy(
            groups,
            new Dictionary<string, string>
            {
                ["Top"] = string.Empty,
                ["Side"] = "Top",
                ["Mid"] = "Top",
                ["X"] = "Mid",
                ["Deep"] = "Mid",
                ["Y"] = "Mid",
                ["D1"] = "Deep",
                ["D2"] = "Deep"
            },
            ["Side", "Mid", "X", "Deep", "Y", "D2", "D1"]);

        // Act
        var (parents, order) = hierarchy.Pop("Deep");
        var after = new RepoGroupHierarchy(["Top", "Side", "Mid", "X", "Y", "D1", "D2"], parents, order);

        // Assert: Mid lists X, D2, D1, Y (Deep's slot, D2 before D1 as stored); Top is unchanged
        Assert.Multiple(
            () => Assert.Equal(["X", "D2", "D1", "Y"], after.GetChildren("Mid")),
            () => Assert.Equal(["Side", "Mid"], after.GetChildren("Top")),
            () => Assert.Equal(["Top"], after.GetChildren(null)),
            () => Assert.Equal("Mid", after.GetParent("D1")),
            () => Assert.False(parents.ContainsKey("Deep")));
    }

    /// <summary>
    ///     Test that the constructor registers the group a registered group is inside, even when no stored
    ///     entry names that parent, as the repair of stored parents does.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Constructor_ParentOfRegisteredGroup_IsRegistered()
    {
        // Arrange: only Child is stored; its parent Parent is named by a repo but not registered
        var hierarchy = new RepoGroupHierarchy(
            ["Parent", "Child", "Plain"],
            new Dictionary<string, string> { ["Child"] = "Parent" },
            null);

        // Act
        var stored = hierarchy.GetStoredParents();

        // Assert
        Assert.Multiple(
            () => Assert.True(hierarchy.IsRegistered("Parent")),
            () => Assert.False(hierarchy.IsRegistered("Plain")),
            () => Assert.Equal(
                new Dictionary<string, string> { ["Parent"] = string.Empty, ["Child"] = "Parent" }, stored));
    }

    /// <summary>
    ///     Test that repair drops an entry whose group name is blank or one of the built-in section names.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_DropsBlankAndReservedKeys()
    {
        // Arrange
        var stored = new List<KeyValuePair<string, string>>
        {
            new("Favorites", "Client"),
            new("ungrouped", "Client"),
            new("   ", "Client"),
            new(string.Empty, "Client"),
            new("Client", string.Empty)
        };

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(stored, []);
        var fromNothing = RepoGroupHierarchy.RepairParents(null, ["Work"]);

        // Assert
        Assert.Equal(new Dictionary<string, string> { ["Client"] = string.Empty }, repaired);
        Assert.Empty(fromNothing);
    }

    /// <summary>
    ///     Test that of entries whose names differ only in letter case, and of the padded form of a
    ///     name, the first one wins.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_KeepsTheFirstOfNamesDifferingInLetterCase()
    {
        // Arrange
        var stored = new List<KeyValuePair<string, string>>
        {
            new("  work  ", "Client"),
            new("WORK", "Other"),
            new("Client", string.Empty),
            new("Other", string.Empty)
        };

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(stored, []);

        // Assert: the first entry won (so Work is inside Client), under its cleaned spelling
        Assert.Equal(
            new Dictionary<string, string> { ["work"] = "Client", ["Client"] = string.Empty, ["Other"] = string.Empty },
            repaired);
    }

    /// <summary>
    ///     Test that repair spells a group the way its repos spell it, whatever spelling the key has.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_SpellsAGroupLikeItsRepos()
    {
        // Arrange
        var stored = new List<KeyValuePair<string, string>> { new("work", "CLIENT") };

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(stored, ["Work", "work", "Client"]);

        // Assert: Work and Client as the repos spell them
        Assert.Equal(
            new Dictionary<string, string> { ["Work"] = "Client", ["Client"] = string.Empty },
            repaired);
    }

    /// <summary>
    ///     Test that repair reads a missing, blank, reserved, unknown or self parent as the top level.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_ReadsABadParentAsTopLevel()
    {
        // Arrange
        var stored = new List<KeyValuePair<string, string>>
        {
            new("Self", "self"),
            new("Orphan", "Gone"),
            new("Reserved", "Ungrouped"),
            new("Blank", "  "),
            new("Empty", string.Empty)
        };

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(stored, []);

        // Assert
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Self"] = string.Empty,
                ["Orphan"] = string.Empty,
                ["Reserved"] = string.Empty,
                ["Blank"] = string.Empty,
                ["Empty"] = string.Empty
            },
            repaired);
    }

    /// <summary>
    ///     Test that a parent a repo is in (but nothing registered) is accepted and registered too, so
    ///     every ancestor of a registered group is registered.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_ParentNamedByRepo_IsRegisteredAsTopLevel()
    {
        // Act
        var repaired = RepoGroupHierarchy.RepairParents(
            [new("Child", "parent")], ["Parent", "Unrelated"]);

        // Assert
        Assert.Equal(
            new Dictionary<string, string> { ["Child"] = "Parent", ["Parent"] = string.Empty },
            repaired);
    }

    /// <summary>
    ///     Test that a cycle is cut at the member that sorts first, ignoring letter case, whatever the
    ///     entries' order, the other members still chaining up to it.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_Cycle_CutAtFirstMemberOfTheCycle()
    {
        // Arrange: the entries put b inside A, C inside B and A inside C, which is a cycle of three
        // (A inside C inside B inside A); D is inside C and hangs off the cycle
        List<KeyValuePair<string, string>> forward =
        [
            new("b", "A"), new("C", "B"), new("A", "C"), new("D", "C")
        ];

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(forward, []);

        // Assert: A (first in order) became top level; the others still chain up to it
        Assert.Equal(
            new Dictionary<string, string> { ["b"] = "A", ["C"] = "b", ["A"] = string.Empty, ["D"] = "C" },
            repaired);
    }

    /// <summary>
    ///     Test that disjoint cycles are each cut at their own first member, a two-cycle and a five-cycle
    ///     alike, and that a cycle deep in a chain is cut while the chain leading to it is kept.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_DisjointLongAndDeepCycles_EachCutAtItsFirstMember()
    {
        // Arrange: P and Q inside each other; E1 > E2 > E3 > E4 > E5 > E1 (inside); T1 > T2 > P is a tail, and
        // Z hangs off the five-cycle
        List<KeyValuePair<string, string>> stored =
        [
            new("Q", "P"), new("P", "Q"),
            new("E1", "E5"), new("E2", "E1"), new("E3", "E2"), new("E4", "E3"), new("E5", "E4"),
            new("T2", "T1"), new("T1", "P"),
            new("Z", "E3")
        ];

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(stored, []);

        // Assert: E1 and P (the first of their cycles) are the only new top-level groups
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Q"] = "P",
                ["P"] = string.Empty,
                ["E1"] = string.Empty,
                ["E2"] = "E1",
                ["E3"] = "E2",
                ["E4"] = "E3",
                ["E5"] = "E4",
                ["T2"] = "T1",
                ["T1"] = "P",
                ["Z"] = "E3"
            },
            repaired);
    }

    /// <summary>
    ///     Test that a cycle is cut the same way whatever order the entries are stored in, whatever letter
    ///     case the names have, and when the repaired result is repaired again.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_Cycle_IsDeterministicAndIdempotent()
    {
        // Arrange
        List<KeyValuePair<string, string>> forward =
        [
            new("b", "A"), new("C", "B"), new("A", "C"), new("D", "C")
        ];
        List<KeyValuePair<string, string>> backward = [.. forward.AsEnumerable().Reverse()];
        List<KeyValuePair<string, string>> shouted = [new("B", "a"), new("c", "b"), new("a", "c"), new("d", "C")];

        // Act
        var first = RepoGroupHierarchy.RepairParents(forward, []);
        var second = RepoGroupHierarchy.RepairParents(backward, []);
        var again = RepoGroupHierarchy.RepairParents(first, []);
        var viaOtherCase = RepoGroupHierarchy.RepairParents(shouted, []);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(first, second),
            () => Assert.Equal(first, again),
            () => Assert.Equal(string.Empty, viaOtherCase["a"]),
            () => Assert.Equal("a", viaOtherCase["B"]),
            () => Assert.Equal(first.Count, viaOtherCase.Count));
    }

    /// <summary>
    ///     Test that a cycle among the groups given to the constructor is cut at the member that sorts first
    ///     whatever the letter case of the stored names, and that the result is the same when the repaired
    ///     parents are given back.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Constructor_CycleWithMixedLetterCase_CutAtFirstMember()
    {
        // Arrange
        string[] groups = ["Alpha", "bravo", "Charlie"];
        var damaged = new Dictionary<string, string> { ["ALPHA"] = "charlie", ["Bravo"] = "alpha", ["CHARLIE"] = "BRAVO" };

        // Act
        var first = new RepoGroupHierarchy(groups, damaged, null);
        var second = new RepoGroupHierarchy(groups, first.GetStoredParents(), null);

        // Assert: Alpha is the first, so it is the top; Bravo is inside it, Charlie inside Bravo
        Assert.Multiple(
            () => Assert.Null(first.GetParent("alpha")),
            () => Assert.Equal("Alpha", first.GetParent("bravo")),
            () => Assert.Equal("bravo", first.GetParent("charlie")),
            () => Assert.Equal(first.GetStoredParents(), second.GetStoredParents()));
    }

    /// <summary>
    ///     Test that a hierarchy built from a damaged stored value still shows every group: a cycle is
    ///     cut, an unknown or self parent is top level and an entry about a group that does not exist is
    ///     ignored.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Constructor_DamagedParents_StillListsEveryGroup()
    {
        // Arrange
        var hierarchy = new RepoGroupHierarchy(
            ["A", "B", "Lonely", "Self"],
            new Dictionary<string, string>
            {
                ["A"] = "B",
                ["B"] = "A",
                ["Lonely"] = "Missing",
                ["Self"] = "Self",
                ["Ghost"] = "A"
            },
            null);

        // Act
        var nodes = hierarchy.GetTreeOrder();

        // Assert
        Assert.Multiple(
            () => Assert.Equal(["A", "B", "Lonely", "Self"], nodes.Select(node => node.Name).Order().ToArray()),
            () => Assert.Equal(
                new Dictionary<string, string>
                {
                    ["A"] = string.Empty,
                    ["B"] = "A",
                    ["Lonely"] = string.Empty,
                    ["Self"] = string.Empty
                },
                hierarchy.GetStoredParents()),
            () => Assert.False(hierarchy.Contains("Ghost")));
    }

    /// <summary>
    ///     Test that the group names the constructor is given are cleaned of surrounding white space, that a
    ///     blank name, a built-in section name and a duplicate in another letter case are ignored, and that
    ///     <see langword="null"/> stored parents and a <see langword="null"/> order give a flat alphabetical
    ///     list.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Constructor_GroupNamesSanitizedAndNullParentsAndOrderAreFlat()
    {
        // Arrange
        string?[] names = ["  Work  ", "work", "Favorites", "UNGROUPED", "   ", null, "Admin"];

        // Act
        var hierarchy = new RepoGroupHierarchy(names, null, null);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(["Admin", "Work"], hierarchy.GetChildren(null)),
            () => Assert.Equal(["Admin", "Work"], hierarchy.Groups.Order().ToArray()),
            () => Assert.False(hierarchy.Contains("Favorites")),
            () => Assert.Empty(hierarchy.GetStoredParents()));
    }

    /// <summary>
    ///     Test that a <see langword="null"/> list of group names given to the constructor, or of repo group
    ///     names given to <see cref="RepoGroupHierarchy.RepairParents"/>, is refused.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_NullGroupNames_AreRefused()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new RepoGroupHierarchy(null!, null, null));
        Assert.Throws<ArgumentNullException>(() => RepoGroupHierarchy.RepairParents([], null!));
    }

    /// <summary>
    ///     Test that deleting a group that does not exist (a built-in section included) is refused.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_UnknownGroup_IsRefused()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Throws<ArgumentException>(() => hierarchy.Pop("Unknown"));
        Assert.Throws<ArgumentException>(() => hierarchy.Pop("Favorites"));
    }

    /// <summary>
    ///     Test that a tree thousands of levels deep is walked without recursion (no stack overflow).
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_DeepTree_WalkedIteratively()
    {
        // Arrange: G0 > G1 > ... > G4999
        const int depth = 5000;
        var (names, parents) = CreateDeepChain(depth);

        // Act
        var hierarchy = new RepoGroupHierarchy(names, parents, null);
        var nodes = hierarchy.GetTreeOrder();

        // Assert
        Assert.Multiple(
            () => Assert.Equal(depth, nodes.Count),
            () => Assert.Equal(depth - 1, nodes[^1].Depth),
            () => Assert.Equal(depth - 1, hierarchy.GetDescendants("G0").Count),
            () => Assert.Equal(depth - 1, hierarchy.GetDepth($"G{depth - 1}")));
    }

    /// <summary>
    ///     Test that repairing, moving into and deleting groups work in a tree thousands of levels deep:
    ///     a cycle closing the chain is cut at its first member, a group is put inside the deepest one with
    ///     every group above it registered, and a group in the middle is popped.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_DeepTree_RepairPutInsideAndPopWork()
    {
        // Arrange: G0 > G1 > ... > G4999, with G0 stored as inside the deepest group (a cycle)
        const int depth = 5000;
        var (names, parents) = CreateDeepChain(depth);
        var damaged = new Dictionary<string, string>(parents) { ["G0"] = $"G{depth - 1}" };

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(damaged, []);
        var hierarchy = new RepoGroupHierarchy([.. names, "Extra"], parents, null);
        var put = hierarchy.PutInside("Extra", $"G{depth - 1}");
        var (popParents, popOrder) = hierarchy.Pop("G2500");

        // Assert
        Assert.Multiple(
            () => Assert.Equal(depth, repaired.Count),
            () => Assert.Equal(string.Empty, repaired["G0"]),
            () => Assert.Equal("G2499", repaired["G2500"]),
            () => Assert.Equal($"G{depth - 1}", put["Extra"]),
            () => Assert.Equal(depth + 1, put.Count),
            () => Assert.False(popParents.ContainsKey("G2500")),
            () => Assert.Equal("G2499", popParents["G2501"]),
            () => Assert.DoesNotContain("G2500", popOrder),
            () => Assert.False(hierarchy.CanMove("G0", $"G{depth - 1}")),
            () => Assert.True(hierarchy.CanMove("Extra", null)));
    }

    /// <summary>
    ///     Creates the names and stored parents of a chain of groups nested <paramref name="depth"/> levels
    ///     deep: G0 &gt; G1 &gt; ... .
    /// </summary>
    /// <param name="depth">The number of groups.</param>
    /// <returns>The names and the stored parents (G0 registered at the top level).</returns>
    private static (List<string> Names, Dictionary<string, string> Parents) CreateDeepChain(int depth)
    {
        var names = Enumerable.Range(0, depth).Select(index => $"G{index}").ToList();
        var parents = new Dictionary<string, string> { [names[0]] = string.Empty };
        for (var index = 1; index < depth; index++)
        {
            parents[names[index]] = names[index - 1];
        }

        return (names, parents);
    }

    /// <summary>
    ///     Creates a hierarchy of three nested groups, Top &gt; Middle &gt; Bottom, and an unrelated one.
    /// </summary>
    /// <returns>The hierarchy.</returns>
    private static RepoGroupHierarchy CreateChain() =>
        new(
            ["Top", "Middle", "Bottom", "Elsewhere"],
            new Dictionary<string, string>
            {
                ["Top"] = string.Empty,
                ["Middle"] = "Top",
                ["Bottom"] = "Middle"
            },
            null);
}
