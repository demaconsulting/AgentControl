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

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     One group in the depth-first listing of the group tree.
/// </summary>
/// <param name="Name">The group's name, spelled as the group is.</param>
/// <param name="Parent">The name of the group it is inside, or <see langword="null"/> for a top-level
///     group.</param>
/// <param name="Depth">How many groups it is inside: 0 for a top-level group.</param>
internal readonly record struct RepoGroupNode(string Name, string? Parent, int Depth);

/// <summary>
///     The rules for how the user's repo groups nest: which group is inside which, in what order the
///     groups of one level are listed, and which moves keep the tree a tree.
/// </summary>
/// <remarks>
///     <para>
///     A group is still identified by its name (names are unique across the whole tree, compared
///     with <see cref="RepoGroupName.Comparer"/>); the tree is stored in
///     <see cref="AppSettings.GroupParents"/> as child name to parent name (an empty parent means
///     "registered, top level") and the order of the groups of one level is the relative order of
///     those groups inside the single flat <see cref="AppSettings.GroupOrder"/>, so a file with no
///     hierarchy keeps meaning what it always meant: every group is top level and
///     <c>GroupOrder</c> is their order.
///     </para>
///     <para>
///     An instance is an immutable, repaired view built from the groups that exist, the stored
///     parents and the stored order; the methods that "change" the tree return the new stored values
///     and leave the instance as it was. Everything is iterative (no recursion), so any depth works.
///     The static <see cref="RepairParents"/> brings stored parents in line with the existing groups
///     when the settings are loaded or changed. Pure: no I/O and no shared state, so it is
///     thread-safe.
///     </para>
/// </remarks>
internal sealed class RepoGroupHierarchy
{
    /// <summary>
    ///     The distinct groups, each spelled as it was first given.
    /// </summary>
    private readonly Dictionary<string, string> _spellings = new(RepoGroupName.Comparer);

    /// <summary>
    ///     The registered groups (the keys of the stored parents that name an existing group).
    /// </summary>
    private readonly HashSet<string> _registered = new(RepoGroupName.Comparer);

    /// <summary>
    ///     Each nested group's parent; top-level groups have no entry.
    /// </summary>
    private readonly Dictionary<string, string> _parents = new(RepoGroupName.Comparer);

    /// <summary>
    ///     The groups of each level in listing order, keyed by the parent's name; the top level is
    ///     under the empty string.
    /// </summary>
    private readonly Dictionary<string, List<string>> _children = new(RepoGroupName.Comparer);

    /// <summary>
    ///     The stored order the instance was built with.
    /// </summary>
    private readonly List<string> _order;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RepoGroupHierarchy"/> class.
    /// </summary>
    /// <remarks>
    ///     Tolerant by design, so a damaged file can never keep the window from showing its groups:
    ///     a stored entry that names a group that does not exist is ignored, a parent that does not
    ///     exist or is the group itself means top level, and a cycle is cut at the member that sorts
    ///     first (the same rule as <see cref="RepairParents"/>).
    /// </remarks>
    /// <param name="groups">The names of the groups that exist (see
    ///     <see cref="AppSettings.GetGroupNames"/>); each is <see cref="RepoGroupName.Sanitize"/>d and
    ///     duplicates ignoring letter case are dropped.</param>
    /// <param name="parents">The stored parents (<see cref="AppSettings.GroupParents"/>); may be
    ///     <see langword="null"/>. Its keys are the registered groups.</param>
    /// <param name="order">The stored order of the groups (<see cref="AppSettings.GroupOrder"/>);
    ///     may be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="groups"/> is
    ///     <see langword="null"/>.</exception>
    public RepoGroupHierarchy(
        IEnumerable<string?> groups,
        IReadOnlyDictionary<string, string>? parents,
        IReadOnlyList<string>? order)
    {
        ArgumentNullException.ThrowIfNull(groups);

        // The groups that exist, each spelled once
        foreach (var name in groups.Select(RepoGroupName.Sanitize).OfType<string>())
        {
            _spellings.TryAdd(name, name);
        }

        _order = [.. order ?? []];

        // The stored parents: only entries about groups that exist; an unknown or self parent is
        // top level, the group still being registered
        var resolved = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var (key, value) in parents ?? new Dictionary<string, string>())
        {
            var child = RepoGroupName.Sanitize(key);
            if (child is null || !_spellings.TryGetValue(child, out var childName) || resolved.ContainsKey(childName))
            {
                continue;
            }

            _registered.Add(childName);
            var parent = RepoGroupName.Sanitize(value);
            resolved[childName] = parent is not null &&
                                  _spellings.TryGetValue(parent, out var parentName) &&
                                  !RepoGroupName.Comparer.Equals(parentName, childName)
                ? parentName
                : string.Empty;
        }

        BreakCycles(resolved);
        foreach (var (child, parent) in resolved.Where(entry => entry.Value.Length > 0))
        {
            _parents[child] = parent;

            // Every group a registered group is inside is registered too (as RepairParents does)
            _registered.Add(parent);
        }

        // The listing order of every level: the stored order first, the rest alphabetically
        var levels = new Dictionary<string, List<string>>(RepoGroupName.Comparer);
        foreach (var name in _spellings.Values)
        {
            var parentKey = _parents.TryGetValue(name, out var found) ? found : string.Empty;
            if (!levels.TryGetValue(parentKey, out var level))
            {
                levels[parentKey] = level = [];
            }

            level.Add(name);
        }

        foreach (var (parentKey, level) in levels)
        {
            _children[parentKey] = ArrangeGroups(level, _order);
        }
    }

    /// <summary>
    ///     Gets the names of all groups that exist, in no particular order.
    /// </summary>
    public IReadOnlyCollection<string> Groups => _spellings.Values;

    /// <summary>
    ///     Puts group names in the order the list shows them: the ones with a stored position in that
    ///     order, then the others alphabetically.
    /// </summary>
    /// <remarks>
    ///     Pure. A stored name that matches none of <paramref name="names"/> is skipped; names are
    ///     compared ignoring letter case and returned as spelled in <paramref name="names"/>.
    /// </remarks>
    /// <param name="names">The groups to arrange (duplicates ignoring letter case are dropped).</param>
    /// <param name="order">The user's stored group order (<see cref="AppSettings.GroupOrder"/>), or
    ///     <see langword="null"/> for none.</param>
    /// <returns>The arranged names.</returns>
    public static List<string> ArrangeGroups(IEnumerable<string> names, IReadOnlyList<string>? order)
    {
        var remaining = names.Distinct(RepoGroupName.Comparer).ToList();
        var arranged = new List<string>();
        foreach (var stored in order ?? [])
        {
            var match = remaining.FirstOrDefault(name => RepoGroupName.Comparer.Equals(name, stored));
            if (match is not null)
            {
                arranged.Add(match);
                remaining.Remove(match);
            }
        }

        arranged.AddRange(remaining.Order(RepoGroupName.Comparer));
        return arranged;
    }

    /// <summary>
    ///     Repairs stored parents against the groups that exist, so a hand-edited, damaged or newer
    ///     file can never leave the tree in a state the window cannot show.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The rules, all deterministic and idempotent: each key and parent is
    ///     <see cref="RepoGroupName.Sanitize"/>d and an entry whose key is blank or reserved is
    ///     dropped (a blank or reserved parent means top level); a group that differs only in letter
    ///     case from an earlier key is dropped (the first wins) and a group named by a repo is spelled
    ///     as its repos spell it; a parent that is the group itself, or names no group that a repo is in
    ///     or that is registered, means top level (an empty value); a cycle is cut by making the member
    ///     that sorts first (ordinal, ignoring letter case) top level; and the parent of a registered
    ///     group is registered too, so every ancestor of a registered group stays while the group does.
    ///     </para>
    ///     <para>
    ///     The keys of the result are the registered groups. A key is kept even when no repo is in the
    ///     group (see <see cref="AppSettings.GetGroupNames"/>: a registered group exists until it is
    ///     deleted). Returns a new dictionary; <paramref name="stored"/> is not changed.
    ///     </para>
    /// </remarks>
    /// <param name="stored">The stored parents; may be <see langword="null"/>.</param>
    /// <param name="repoGroupNames">The group name of every repo (<see langword="null"/> or blank for
    ///     repos in no group); each is sanitized.</param>
    /// <returns>The repaired parents, keyed ignoring letter case, an empty value meaning top level.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repoGroupNames"/> is
    ///     <see langword="null"/>.</exception>
    public static Dictionary<string, string> RepairParents(
        IEnumerable<KeyValuePair<string, string>>? stored,
        IEnumerable<string?> repoGroupNames)
    {
        ArgumentNullException.ThrowIfNull(repoGroupNames);

        // The groups some repo is in, each spelled as its first repo spells it
        var repoSpellings = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var name in repoGroupNames.Select(RepoGroupName.Sanitize).OfType<string>())
        {
            repoSpellings.TryAdd(name, name);
        }

        // The registered groups: the usable keys, spelled like the repos when a repo is in the group
        var rawParents = new List<(string Child, string? Parent)>();
        var keySpellings = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var (key, value) in stored ?? [])
        {
            var child = RepoGroupName.Sanitize(key);
            if (child is null || keySpellings.ContainsKey(child))
            {
                continue;
            }

            var spelled = repoSpellings.TryGetValue(child, out var fromRepo) ? fromRepo : child;
            keySpellings[child] = spelled;
            rawParents.Add((spelled, value));
        }

        // Each parent must be a group that exists (a registered one or one a repo is in), and not
        // the group itself; anything else means top level
        var result = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var (child, rawParent) in rawParents)
        {
            var parent = RepoGroupName.Sanitize(rawParent);
            var resolved = string.Empty;
            if (parent is not null && !RepoGroupName.Comparer.Equals(parent, child))
            {
                if (keySpellings.TryGetValue(parent, out var keyed))
                {
                    resolved = keyed;
                }
                else if (repoSpellings.TryGetValue(parent, out var named))
                {
                    resolved = named;
                }
            }

            result[child] = resolved;
        }

        BreakCycles(result);

        // The parent of a registered group is registered too (a group a repo is in becomes a top-level
        // key); a registered parent is already a key and needs nothing
        foreach (var parent in result.Values.Where(value => value.Length > 0).Distinct(RepoGroupName.Comparer).ToList())
        {
            result.TryAdd(parent, string.Empty);
        }

        return result;
    }

    /// <summary>
    ///     Gets whether a group exists.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns><see langword="true"/> when the group exists.</returns>
    public bool Contains(string? name) => name is not null && _spellings.ContainsKey(name);

    /// <summary>
    ///     Gets whether a group is registered: it takes part in the hierarchy and so exists until it
    ///     is deleted, even with no repo in it.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns><see langword="true"/> when the group is registered.</returns>
    public bool IsRegistered(string? name) => name is not null && _registered.Contains(name);

    /// <summary>
    ///     Gets the spelling the group has in the tree.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns>The group's own spelling, or <see langword="null"/> when no such group exists.</returns>
    public string? GetName(string? name) =>
        name is not null && _spellings.TryGetValue(name, out var spelled) ? spelled : null;

    /// <summary>
    ///     Gets the group a group is directly inside.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns>The parent's name, or <see langword="null"/> for a top-level group (or a name that
    ///     is no group).</returns>
    public string? GetParent(string? name) =>
        name is not null && _parents.TryGetValue(name, out var parent) ? parent : null;

    /// <summary>
    ///     Gets the groups directly inside a group, or at the top level, in the order they are listed.
    /// </summary>
    /// <param name="parent">The parent's name, or <see langword="null"/> for the top level.</param>
    /// <returns>The children's names, first to last: the ones with a stored position in the order they
    ///     are stored, then the others alphabetically.</returns>
    public IReadOnlyList<string> GetChildren(string? parent) =>
        _children.TryGetValue(parent ?? string.Empty, out var children) ? children : [];

    /// <summary>
    ///     Lists every group depth first, each group followed by its subtree, in the order the window
    ///     shows them.
    /// </summary>
    /// <returns>The nodes, first to last.</returns>
    public IReadOnlyList<RepoGroupNode> GetTreeOrder()
    {
        // An explicit stack instead of recursion, so the depth of the tree is not limited by the
        // call stack; children are pushed last-first so the first child is popped first
        var nodes = new List<RepoGroupNode>();
        var pending = new Stack<RepoGroupNode>();
        PushChildren(pending, parent: null, depth: 0);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            nodes.Add(node);
            PushChildren(pending, node.Name, node.Depth + 1);
        }

        return nodes;
    }

    /// <summary>
    ///     Gets every group inside a group, however deeply, depth first.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns>The descendants' names in the order <see cref="GetTreeOrder"/> lists them; empty for a
    ///     group with none or for a name that is no group.</returns>
    public IReadOnlyList<string> GetDescendants(string? name)
    {
        var descendants = new List<string>();
        var spelled = GetName(name);
        if (spelled is null)
        {
            return descendants;
        }

        var pending = new Stack<RepoGroupNode>();
        PushChildren(pending, spelled, depth: 1);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            descendants.Add(node.Name);
            PushChildren(pending, node.Name, node.Depth + 1);
        }

        return descendants;
    }

    /// <summary>
    ///     Gets a group together with every group inside it - what deleting the group with its
    ///     contents removes.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns>The group first, then its descendants depth first; empty for a name that is no
    ///     group.</returns>
    public IReadOnlyList<string> GetSubtree(string? name)
    {
        var spelled = GetName(name);
        return spelled is null ? [] : [spelled, .. GetDescendants(spelled)];
    }

    /// <summary>
    ///     Gets the groups a group is inside, nearest first.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns>The parent, its parent and so on up to a top-level group; empty for a top-level
    ///     group or a name that is no group.</returns>
    public IReadOnlyList<string> GetAncestors(string? name)
    {
        var ancestors = new List<string>();
        var current = GetParent(name);

        // The tree has no cycle, so this ends; the bound only guards against a defect
        while (current is not null && ancestors.Count <= _spellings.Count)
        {
            ancestors.Add(current);
            current = GetParent(current);
        }

        return ancestors;
    }

    /// <summary>
    ///     Gets how many groups a group is inside.
    /// </summary>
    /// <param name="name">The group's name; letter case is ignored.</param>
    /// <returns>0 for a top-level group (and for a name that is no group).</returns>
    public int GetDepth(string? name) => GetAncestors(name).Count;

    /// <summary>
    ///     Gets whether a group is another group or is anywhere inside it.
    /// </summary>
    /// <param name="name">The group to test; letter case is ignored.</param>
    /// <param name="ancestor">The other group; letter case is ignored.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is <paramref name="ancestor"/>
    ///     itself or one of its descendants.</returns>
    public bool IsSameOrDescendant(string? name, string? ancestor)
    {
        if (name is null || ancestor is null)
        {
            return false;
        }

        return RepoGroupName.Comparer.Equals(name, ancestor) ||
               GetAncestors(name).Contains(ancestor, RepoGroupName.Comparer);
    }

    /// <summary>
    ///     Gets whether a group may be put inside another group (or at the top level) without making
    ///     the tree a loop.
    /// </summary>
    /// <param name="group">The group to move; letter case is ignored. It must exist.</param>
    /// <param name="newParent">The group to put it inside, or <see langword="null"/> for the top
    ///     level. It must exist.</param>
    /// <returns><see langword="false"/> when a group does not exist (the built-in sections never do),
    ///     or the new parent is the group itself or one of its descendants.</returns>
    public bool CanMove(string? group, string? newParent) =>
        Contains(group) && (newParent is null || (Contains(newParent) && !IsSameOrDescendant(newParent, group)));

    /// <summary>
    ///     Computes the stored parents after a group is put inside another group, or at the top
    ///     level.
    /// </summary>
    /// <remarks>
    ///     A group put inside another group, and every group it is now inside, become registered (a
    ///     group that takes part in the hierarchy exists until deleted). A group put at the top level
    ///     stays registered when it was, and is not registered when it was not, so putting an ordinary
    ///     top-level group at the top level changes nothing. The order of the groups is not touched
    ///     here; the caller places the group among its new siblings. Pure.
    /// </remarks>
    /// <param name="group">The group to move.</param>
    /// <param name="newParent">The group to put it inside, or <see langword="null"/> for the top
    ///     level.</param>
    /// <returns>A new stored parents dictionary.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the move is not allowed (see
    ///     <see cref="CanMove"/>).</exception>
    public Dictionary<string, string> PutInside(string group, string? newParent)
    {
        if (!CanMove(group, newParent))
        {
            throw new InvalidOperationException($"The group '{group}' cannot be put inside '{newParent}'.");
        }

        var result = GetStoredParents();
        var moved = _spellings[group];
        var parent = newParent is null ? string.Empty : _spellings[newParent];
        if (parent.Length > 0 || _registered.Contains(moved))
        {
            result[moved] = parent;
        }

        // Every group the moved one is now inside takes part in the hierarchy as well
        for (var ancestor = parent; ancestor.Length > 0; ancestor = GetParent(ancestor) ?? string.Empty)
        {
            result.TryAdd(ancestor, GetParent(ancestor) ?? string.Empty);
        }

        return result;
    }

    /// <summary>
    ///     Computes the stored parents and order after a group is deleted but everything in it is
    ///     kept: the group's subgroups go to the group's parent (or the top level) and take the
    ///     group's place among its siblings, in their own order.
    /// </summary>
    /// <remarks>
    ///     The place is the one the window shows, whether or not the group has a position of its own
    ///     in the stored order: the siblings are listed as <see cref="GetChildren"/> lists them, the
    ///     group is replaced there by its subgroups (in their listed order), and that level is stored
    ///     in full, so the arrangement the user saw stays put and a group that was never dragged is
    ///     handled like one that was. Every stored occurrence of the group, of its subgroups and of
    ///     its siblings is replaced by that one run, so the result names the group nowhere and no
    ///     group twice; the relative order within every other level is untouched. A group with no
    ///     subgroups only leaves the stored order. The group's own
    ///     repos are not part of the tree; the caller moves them to the parent group. Pure.
    /// </remarks>
    /// <param name="group">The group to delete.</param>
    /// <returns>The new stored parents and the new stored order, neither containing the group.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="group"/> does not exist.</exception>
    public (Dictionary<string, string> Parents, List<string> Order) Pop(string group)
    {
        if (!Contains(group))
        {
            throw new ArgumentException($"The group '{group}' does not exist.", nameof(group));
        }

        var removed = _spellings[group];
        var grandParent = GetParent(removed);
        var subgroups = GetChildren(removed);

        // The parents: the subgroups move up one level, the group is forgotten
        var parents = GetStoredParents();
        parents.Remove(removed);
        foreach (var subgroup in subgroups)
        {
            parents[subgroup] = grandParent ?? string.Empty;
        }

        // The order: the level the group was in, as shown, with the group replaced by its subgroups.
        // A group with no subgroups only leaves the order (the others keep their relative places, so
        // the siblings that have no stored place need not be written down)
        if (subgroups.Count == 0)
        {
            return (parents, _order.Where(entry => !RepoGroupName.Comparer.Equals(entry, removed)).ToList());
        }

        var level = GetChildren(grandParent);
        var newLevel = new List<string>(level.Count + subgroups.Count);
        foreach (var sibling in level)
        {
            if (RepoGroupName.Comparer.Equals(sibling, removed))
            {
                newLevel.AddRange(subgroups);
            }
            else
            {
                newLevel.Add(sibling);
            }
        }

        // Everything else of the stored order stays, in its order; the rewritten level is stored last
        var replaced = new HashSet<string>(newLevel, RepoGroupName.Comparer) { removed };
        var order = _order.Where(entry => !replaced.Contains(entry)).ToList();
        order.AddRange(newLevel);
        return (parents, order);
    }

    /// <summary>
    ///     Gets the stored form of the hierarchy: one entry per registered group, naming its parent or
    ///     the empty string for the top level.
    /// </summary>
    /// <returns>A new dictionary keyed ignoring letter case.</returns>
    public Dictionary<string, string> GetStoredParents()
    {
        var stored = new Dictionary<string, string>(RepoGroupName.Comparer);
        foreach (var name in _registered)
        {
            stored[name] = GetParent(name) ?? string.Empty;
        }

        return stored;
    }

    /// <summary>
    ///     Pushes the children of a group (or the top level) onto a stack so that the first child is
    ///     popped first.
    /// </summary>
    /// <param name="stack">The stack to push onto.</param>
    /// <param name="parent">The parent's name, or <see langword="null"/> for the top level.</param>
    /// <param name="depth">The depth the children have.</param>
    private void PushChildren(Stack<RepoGroupNode> stack, string? parent, int depth)
    {
        var children = GetChildren(parent);
        for (var index = children.Count - 1; index >= 0; index--)
        {
            stack.Push(new RepoGroupNode(children[index], parent, depth));
        }
    }

    /// <summary>
    ///     Cuts every cycle in a map of child to parent by making the member of each cycle that sorts
    ///     first top level (an empty value).
    /// </summary>
    /// <remarks>
    ///     Every group has at most one parent, so each cycle is found once by following the parents from
    ///     each unvisited group; the rule (the first member in ordinal order, ignoring letter case) does
    ///     not depend on where the walk starts, so the result is deterministic and a second pass changes
    ///     nothing.
    /// </remarks>
    /// <param name="parents">The map to repair in place; it must be keyed ignoring letter case and
    ///     name parents spelled as the groups are.</param>
    private static void BreakCycles(Dictionary<string, string> parents)
    {
        var visited = new HashSet<string>(RepoGroupName.Comparer);
        foreach (var start in parents.Keys.Order(RepoGroupName.Comparer).ToList())
        {
            if (visited.Contains(start))
            {
                continue;
            }

            // Follow the parents until the top level, an already settled group or a group already on
            // this walk (a cycle)
            var path = new List<string>();
            var current = start;
            while (!visited.Contains(current))
            {
                visited.Add(current);
                path.Add(current);
                if (!parents.TryGetValue(current, out var next) || next.Length == 0)
                {
                    current = string.Empty;
                    break;
                }

                current = next;
            }

            // The walk ended on a group of this very path: that is a cycle
            if (current.Length > 0)
            {
                var index = path.FindIndex(name => RepoGroupName.Comparer.Equals(name, current));
                if (index >= 0)
                {
                    var cut = path.Skip(index).Order(RepoGroupName.Comparer).First();
                    parents[cut] = string.Empty;
                }
            }
        }
    }
}
