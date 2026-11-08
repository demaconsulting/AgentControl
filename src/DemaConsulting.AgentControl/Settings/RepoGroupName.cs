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

using System.Text;

namespace DemaConsulting.AgentControl.Settings;

/// <summary>
///     Why a user-entered repo group name cannot be used.
/// </summary>
internal enum RepoGroupNameProblem
{
    /// <summary>
    ///     The name is acceptable.
    /// </summary>
    None,

    /// <summary>
    ///     The name is empty or only white space.
    /// </summary>
    Empty,

    /// <summary>
    ///     The name is longer than <see cref="RepoGroupName.MaxLength"/> characters.
    /// </summary>
    TooLong,

    /// <summary>
    ///     The name is one of the two built-in section names (<see cref="RepoGroupName.FavoritesName"/>
    ///     or <see cref="RepoGroupName.UngroupedName"/>), which a group of the user's own may not take.
    /// </summary>
    Reserved
}

/// <summary>
///     The rules for the names of the user's own repo groups: how a typed or stored name is
///     cleaned up, which names are acceptable, and how names that differ only in letter case are
///     treated as one group.
/// </summary>
/// <remarks>
///     A group exists only as a name stored on the recent-repos entries of its members (in the
///     per-user settings file), so there is no separate list of groups that could disagree with the
///     repos. These rules are one shared, pure unit so the settings loader (which must tolerate any
///     stored text), the view models and the name prompt all agree. Stateless and thread-safe.
/// </remarks>
internal static class RepoGroupName
{
    /// <summary>
    ///     The most characters a group name may have; longer stored names are cut to this length and
    ///     longer typed names are refused, so a header can never be unmanageably wide.
    /// </summary>
    public const int MaxLength = 40;

    /// <summary>
    ///     The built-in section that lists favorite repos. Reserved: no group of the user's own may
    ///     have this name, so the collapsed state of the section can be stored beside the groups'.
    /// </summary>
    public const string FavoritesName = "Favorites";

    /// <summary>
    ///     The built-in section that lists the repos that belong to no group. Reserved like
    ///     <see cref="FavoritesName"/>.
    /// </summary>
    public const string UngroupedName = "Ungrouped";

    /// <summary>
    ///     The comparison that decides whether two names are the same group: ordinal and case
    ///     insensitive, so <c>Work</c> and <c>work</c> are one group on every machine and culture.
    /// </summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>
    ///     Cleans a name up: control characters become spaces, runs of white space become one space
    ///     and the ends are trimmed.
    /// </summary>
    /// <param name="name">The typed or stored name; may be <see langword="null"/>.</param>
    /// <returns>The cleaned name, or <see langword="null"/> when nothing is left (a missing,
    ///     empty or blank name means "no group").</returns>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // One pass: a control character or white space ends a word, and words are joined by a
        // single space, which also trims both ends
        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var character in name)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    ///     Checks a name the user entered.
    /// </summary>
    /// <param name="name">The name to check; it is <see cref="Normalize"/>d first.</param>
    /// <returns><see cref="RepoGroupNameProblem.None"/> when the name can be used, otherwise the
    ///     reason it cannot.</returns>
    public static RepoGroupNameProblem Validate(string? name)
    {
        var normalized = Normalize(name);
        if (normalized is null)
        {
            return RepoGroupNameProblem.Empty;
        }

        if (normalized.Length > MaxLength)
        {
            return RepoGroupNameProblem.TooLong;
        }

        return IsReserved(normalized) ? RepoGroupNameProblem.Reserved : RepoGroupNameProblem.None;
    }

    /// <summary>
    ///     Makes whatever a settings file holds into a usable group name, never failing.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="Validate"/>, which refuses what a user typed, a stored name is repaired:
    ///     a too long name is cut to <see cref="MaxLength"/> characters, and a reserved name means
    ///     "no group" (the repo is shown as ungrouped), so a hand-edited or damaged file can never
    ///     keep the application from starting.
    /// </remarks>
    /// <param name="stored">The stored name; may be <see langword="null"/>.</param>
    /// <returns>A valid name, or <see langword="null"/> for no group.</returns>
    public static string? Sanitize(string? stored)
    {
        var normalized = Normalize(stored);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > MaxLength)
        {
            normalized = Normalize(normalized[..MaxLength]);
        }

        return normalized is null || IsReserved(normalized) ? null : normalized;
    }

    /// <summary>
    ///     Gets whether a name is one of the two built-in section names (compared like group names).
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <returns><see langword="true"/> for <see cref="FavoritesName"/> or
    ///     <see cref="UngroupedName"/>.</returns>
    public static bool IsReserved(string name) =>
        Comparer.Equals(name, FavoritesName) || Comparer.Equals(name, UngroupedName);

    /// <summary>
    ///     Lists the distinct groups among some members' names, spelled the way the first member
    ///     that uses each group spells it.
    /// </summary>
    /// <param name="names">The group name of each repo (<see langword="null"/> or blank for repos
    ///     in no group); each is <see cref="Sanitize"/>d.</param>
    /// <returns>One name per group, sorted alphabetically (ordinal, ignoring case).</returns>
    public static IReadOnlyList<string> DistinctNames(IEnumerable<string?> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var firstSpelling = new Dictionary<string, string>(Comparer);
        foreach (var name in names.Select(Sanitize).OfType<string>())
        {
            firstSpelling.TryAdd(name, name);
        }

        return [.. firstSpelling.Values.Order(Comparer)];
    }

    /// <summary>
    ///     Finds how a group is already spelled, so a name typed with different letter case joins the
    ///     existing group instead of creating a look-alike.
    /// </summary>
    /// <param name="name">The (valid) name to place.</param>
    /// <param name="existing">The names of the existing groups; may contain <see langword="null"/>.</param>
    /// <returns>The existing group's spelling when one matches ignoring case, otherwise
    ///     <paramref name="name"/> unchanged.</returns>
    public static string Canonicalize(string name, IEnumerable<string?> existing)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(existing);

        return existing.FirstOrDefault(other => other is not null && Comparer.Equals(other, name)) ?? name;
    }
}
