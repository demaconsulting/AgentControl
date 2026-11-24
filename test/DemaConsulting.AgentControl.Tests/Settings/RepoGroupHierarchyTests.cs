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
    ///     Test the questions about where a group is in the tree: parent, children, descendants,
    ///     subtree, ancestors, depth and whether one group is inside another, ignoring letter case.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Navigation_AnswersParentChildrenAncestorsAndDepth()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.Null(hierarchy.GetParent("Top")),
            () => Assert.Equal("Middle", hierarchy.GetParent("bottom")),
            () => Assert.Equal(["Middle"], hierarchy.GetChildren("TOP")),
            () => Assert.Equal(["Elsewhere", "Top"], hierarchy.GetChildren(null)),
            () => Assert.Empty(hierarchy.GetChildren("Bottom")),
            () => Assert.Equal(["Middle", "Bottom"], hierarchy.GetDescendants("top")),
            () => Assert.Equal(["Top", "Middle", "Bottom"], hierarchy.GetSubtree("TOP")),
            () => Assert.Equal(["Middle", "Top"], hierarchy.GetAncestors("Bottom")),
            () => Assert.Equal(2, hierarchy.GetDepth("Bottom")),
            () => Assert.Equal(0, hierarchy.GetDepth("Top")),
            () => Assert.True(hierarchy.IsSameOrDescendant("bottom", "TOP")),
            () => Assert.True(hierarchy.IsSameOrDescendant("Top", "top")),
            () => Assert.False(hierarchy.IsSameOrDescendant("Top", "Bottom")),
            () => Assert.False(hierarchy.IsSameOrDescendant(null, "Top")),
            () => Assert.Empty(hierarchy.GetSubtree("Unknown")),
            () => Assert.Empty(hierarchy.GetDescendants("Unknown")),
            () => Assert.Null(hierarchy.GetParent(null)),
            () => Assert.Equal("Top", hierarchy.GetName("tOP")),
            () => Assert.True(hierarchy.IsRegistered("middle")),
            () => Assert.False(hierarchy.IsRegistered("Unknown")));
    }

    /// <summary>
    ///     Test that a group may be put at the top level or inside any group that is not itself or one
    ///     of its descendants, and that nothing can be moved into or out of a name that is no group
    ///     (the built-in sections never are).
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_CanMove_RefusesLoopsAndUnknownGroups()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Multiple(
            () => Assert.True(hierarchy.CanMove("Bottom", null)),
            () => Assert.True(hierarchy.CanMove("Bottom", "Top")),
            () => Assert.True(hierarchy.CanMove("Top", "Elsewhere")),
            () => Assert.False(hierarchy.CanMove("Top", "Top")),
            () => Assert.False(hierarchy.CanMove("Top", "middle")),
            () => Assert.False(hierarchy.CanMove("Top", "Bottom")),
            () => Assert.False(hierarchy.CanMove("Unknown", null)),
            () => Assert.False(hierarchy.CanMove("Top", "Unknown")),
            () => Assert.False(hierarchy.CanMove("Favorites", null)),
            () => Assert.False(hierarchy.CanMove("Top", "Ungrouped")),
            () => Assert.False(hierarchy.CanMove(null, null)));
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
    ///     Test that moving a group into itself or one of its descendants, or a group that does not
    ///     exist, is refused instead of making a loop.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_PutInside_NotAllowed_Throws()
    {
        // Arrange
        var hierarchy = CreateChain();

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => hierarchy.PutInside("Top", "Bottom"));
        Assert.Throws<InvalidOperationException>(() => hierarchy.PutInside("Top", "Top"));
        Assert.Throws<InvalidOperationException>(() => hierarchy.PutInside("Unknown", null));
    }

    /// <summary>
    ///     Test that deleting a group with its contents kept moves its subgroups to its parent, in
    ///     their own order and in the group's place in the stored order, and forgets the group.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_SubgroupsGoToTheParentInTheGroupsPlace()
    {
        // Arrange: Root has Before, Mid, After; Mid has Two (first) and One
        var hierarchy = new RepoGroupHierarchy(
            ["Root", "Before", "Mid", "After", "One", "Two", "Other"],
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
        var after = new RepoGroupHierarchy(
            ["Root", "Before", "After", "One", "Two", "Other"], parents, order);

        // Assert
        Assert.Multiple(
            () => Assert.False(parents.ContainsKey("Mid")),
            () => Assert.Equal("Root", parents["One"]),
            () => Assert.Equal("Root", parents["Two"]),
            () => Assert.Equal(["Before", "Two", "One", "After", "Root"], order),
            () => Assert.Equal(["Before", "Two", "One", "After"], after.GetChildren("Root")));
    }

    /// <summary>
    ///     Test that deleting a top-level group with no stored place leaves its subgroups top level and
    ///     the stored order untouched, and that a group that does not exist is refused.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_Pop_TopLevelGroupWithoutPlace_SubgroupsBecomeTopLevel()
    {
        // Arrange
        var hierarchy = new RepoGroupHierarchy(
            ["Parent", "Child"],
            new Dictionary<string, string> { ["Parent"] = string.Empty, ["Child"] = "Parent" },
            ["Other"]);

        // Act
        var (parents, order) = hierarchy.Pop("Parent");

        // Assert
        Assert.Multiple(
            () => Assert.Equal(new Dictionary<string, string> { ["Child"] = string.Empty }, parents),
            () => Assert.Equal(["Other"], order),
            () => Assert.Throws<ArgumentException>(() => hierarchy.Pop("Unknown")));
    }

    /// <summary>
    ///     Test that repair keeps usable entries, spells a group as its repos spell it, makes a missing,
    ///     blank, reserved, unknown or self parent top level, and drops entries with no usable name,
    ///     the first of names that differ only in letter case winning.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_DropsUnusableEntriesAndTopLevelsBadParents()
    {
        // Arrange
        var stored = new List<KeyValuePair<string, string>>
        {
            new("  work  ", "Client"),
            new("WORK", "Other"),
            new("Favorites", "Client"),
            new("   ", "Client"),
            new("Self", "self"),
            new("Orphan", "Gone"),
            new("Reserved", "Ungrouped"),
            new("Blank", "  "),
            new("Client", string.Empty)
        };

        // Act
        var repaired = RepoGroupHierarchy.RepairParents(stored, ["Work", "work"]);

        // Assert
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Work"] = "Client",
                ["Self"] = string.Empty,
                ["Orphan"] = string.Empty,
                ["Reserved"] = string.Empty,
                ["Blank"] = string.Empty,
                ["Client"] = string.Empty
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
    ///     Test that a cycle is cut at the member that sorts first, ignoring letter case, whatever order
    ///     the entries are stored in, and that repairing again changes nothing.
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_RepairParents_Cycle_CutAtFirstMemberDeterministicallyAndIdempotently()
    {
        // Arrange: B > C > A > B is a cycle (A inside B inside C inside A... see the entries); D hangs off it
        List<KeyValuePair<string, string>> forward =
        [
            new("b", "A"), new("C", "B"), new("A", "C"), new("D", "C")
        ];
        List<KeyValuePair<string, string>> backward = [.. forward.AsEnumerable().Reverse()];

        // Act
        var first = RepoGroupHierarchy.RepairParents(forward, []);
        var second = RepoGroupHierarchy.RepairParents(backward, []);
        var again = RepoGroupHierarchy.RepairParents(first, []);

        // Assert: A (first in order) became top level; the others still chain up to it
        var expected = new Dictionary<string, string>
        {
            ["b"] = "A",
            ["C"] = "b",
            ["A"] = string.Empty,
            ["D"] = "C"
        };
        Assert.Multiple(
            () => Assert.Equal(expected, first),
            () => Assert.Equal(expected, second),
            () => Assert.Equal(expected, again));
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
    ///     Test that a tree thousands of levels deep is walked without recursion (no stack overflow).
    /// </summary>
    [Fact]
    public void RepoGroupHierarchy_DeepTree_WalkedIteratively()
    {
        // Arrange: G0 > G1 > ... > G4999
        const int depth = 5000;
        var names = Enumerable.Range(0, depth).Select(index => $"G{index}").ToList();
        var parents = new Dictionary<string, string> { [names[0]] = string.Empty };
        for (var index = 1; index < depth; index++)
        {
            parents[names[index]] = names[index - 1];
        }

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
