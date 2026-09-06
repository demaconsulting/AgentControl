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

namespace DemaConsulting.AgentControl.GitIntegration;

/// <summary>
///     Small in-memory cache of the "committed agent files" badge result, keyed by a repo path
///     plus its current <c>HEAD</c> commit hash.
/// </summary>
/// <remarks>
///     Per architecture.md's repo-fact caching strategy, whether a repo has committed one of the
///     four known agent folders is a property of the git tree, so it is safe to skip re-running
///     <c>git ls-files</c> when <c>HEAD</c> has not changed since the last check. This cache is
///     in-memory only (no persistence): the <c>HEAD</c>-hash key is naturally invalidated by a
///     process restart or a new commit, and re-running one cheap <c>git rev-parse HEAD</c> per
///     card at each app launch is already required for the branch/pin displays. Not thread-safe;
///     callers are expected to use this only from the UI thread, consistent with every other
///     view-model-adjacent class in this codebase.
/// </remarks>
internal sealed class CommittedAgentFilesCache
{
    /// <summary>
    ///     The cached results, keyed by a normalized repo path and its <c>HEAD</c> commit hash at
    ///     the time the result was recorded.
    /// </summary>
    private readonly Dictionary<(string RepoPath, string HeadHash), bool> _cache = new();

    /// <summary>
    ///     Attempts to retrieve a cached result for a repo at a specific <c>HEAD</c> commit hash.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root.</param>
    /// <param name="headHash">The repo's current <c>HEAD</c> commit hash.</param>
    /// <param name="hasCommittedFiles">On return, the cached result, or <see langword="false"/>
    ///     if no cached entry exists for this exact <paramref name="repoPath"/>/<paramref name="headHash"/>
    ///     pair.</param>
    /// <returns><see langword="true"/> if a cached entry was found; otherwise <see langword="false"/>.</returns>
    public bool TryGetCached(string repoPath, string headHash, out bool hasCommittedFiles)
    {
        return _cache.TryGetValue((Normalize(repoPath), headHash), out hasCommittedFiles);
    }

    /// <summary>
    ///     Records a result for a repo at a specific <c>HEAD</c> commit hash, overwriting any
    ///     previous entry for the same key.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root.</param>
    /// <param name="headHash">The repo's current <c>HEAD</c> commit hash.</param>
    /// <param name="hasCommittedFiles">The result to cache.</param>
    public void Set(string repoPath, string headHash, bool hasCommittedFiles)
    {
        _cache[(Normalize(repoPath), headHash)] = hasCommittedFiles;
    }

    /// <summary>
    ///     Removes every cached entry (across all <c>HEAD</c> hashes) for a specific repo path,
    ///     for use by a manual refresh that should never trust a stale cached value.
    /// </summary>
    /// <param name="repoPath">Absolute path to the repository root.</param>
    public void Invalidate(string repoPath)
    {
        var normalized = Normalize(repoPath);
        foreach (var key in _cache.Keys.Where(key => key.RepoPath == normalized).ToList())
        {
            _cache.Remove(key);
        }
    }

    /// <summary>
    ///     Normalizes a repo path for use as a cache key (trims a trailing directory separator),
    ///     so equivalent paths differing only by a trailing slash are treated as the same key.
    /// </summary>
    /// <param name="repoPath">The repo path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string Normalize(string repoPath) =>
        repoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
