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
///     Records a single entry in the user's recent-repos list.
/// </summary>
/// <remarks>
///     Mutable to support JSON round-tripping via <see cref="System.Text.Json.JsonSerializer"/>
///     and simple in-place updates (e.g. refreshing <see cref="PinnedPackageVersion"/> after a
///     sync) without reconstructing the containing list. Not thread-safe; callers must
///     synchronize external access when mutating shared instances.
/// </remarks>
internal sealed class RecentRepo
{
    /// <summary>
    ///     Gets or sets the absolute filesystem path to the repository root.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the agent package name last seen pinned in this repo's
    ///     <c>.agentcontrol.json</c>, or <see langword="null"/> if the repo has never been synced.
    /// </summary>
    /// <remarks>
    ///     This is a cached copy for quick display in the recent-repos list (e.g. upgrade-badge
    ///     comparisons without re-reading the pin file); the authoritative value always lives in
    ///     the repo's own <c>.agentcontrol.json</c> (see the <c>RepoConfig</c> subsystem).
    /// </remarks>
    public string? PinnedPackageName { get; set; }

    /// <summary>
    ///     Gets or sets the semantic version last seen pinned in this repo's
    ///     <c>.agentcontrol.json</c>, or <see langword="null"/> if the repo has never been synced.
    /// </summary>
    /// <remarks>
    ///     Cached copy; see <see cref="PinnedPackageName"/> remarks for the authoritative source.
    /// </remarks>
    public string? PinnedPackageVersion { get; set; }

    /// <summary>
    ///     Gets or sets whether the user has pinned/favorited this repo, per architecture.md's
    ///     recent-repos "pinning/favorites" sort-order feature.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see langword="false"/> so existing settings JSON files (written before
    ///     this field existed) deserialize cleanly - <see cref="System.Text.Json.JsonSerializer"/>
    ///     leaves a missing property at its C# default. Stored here (per-repo, per-user) rather
    ///     than in a separate cache file, following the same precedent already established by
    ///     <see cref="PinnedPackageName"/>/<see cref="PinnedPackageVersion"/>.
    /// </remarks>
    public bool IsFavorite { get; set; }

    /// <summary>
    ///     Gets or sets the UTC timestamp this repo was last successfully launched via
    ///     AgentControl, or <see langword="null"/> if it has never been launched (or was added
    ///     before this field existed).
    /// </summary>
    /// <remarks>
    ///     Drives the recent-repos list's default most-recently-launched sort order. See
    ///     <see cref="IsFavorite"/> remarks for why this lives on <see cref="RecentRepo"/> rather
    ///     than a separate cache file.
    /// </remarks>
    public DateTimeOffset? LastLaunchedUtc { get; set; }
}
