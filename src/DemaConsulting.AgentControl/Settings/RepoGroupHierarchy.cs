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
///     The pure rules for how the user's repo groups are arranged relative to one another: which
///     groups there are and in what order the main window lists them.
/// </summary>
/// <remarks>
///     Kept apart from <see cref="AppSettings"/> (which stores the values) and the main window's view
///     model (which applies them) so the rules can be tested without either and are not duplicated.
///     Stateless and thread-safe; nothing here reads or changes settings.
/// </remarks>
internal static class RepoGroupHierarchy
{
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
}
