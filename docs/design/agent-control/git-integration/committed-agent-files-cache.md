### CommittedAgentFilesCache

![GitIntegration Structure](GitIntegrationView.svg)

#### Purpose

`CommittedAgentFilesCache` is a small in-memory cache of the "committed agent files" badge
result, keyed by a repo path plus its current `HEAD` commit hash. Per architecture.md's
repo-fact caching strategy, whether a repo has committed one of the four known agent folders
is a property of the git tree, so it is safe to skip re-running `git ls-files` when `HEAD` has
not changed since the last check.

#### Data Model

**\_cache**: `Dictionary<(string RepoPath, string HeadHash), bool>` — the cached results,
keyed by a normalized repo path and the `HEAD` commit hash at the time the result was
recorded.

This cache is in-memory only (no persistence): the `HEAD`-hash key is naturally invalidated by
a process restart or a new commit. Not thread-safe; used only from the UI thread, consistent
with every other view-model-adjacent class in this codebase.

#### Key Methods

**TryGetCached**: Attempts to retrieve a cached result for a repo at a specific `HEAD` hash.

- *Parameters*: `string repoPath`, `string headHash`, `out bool hasCommittedFiles`.
- *Returns*: `bool` — `true` if a cached entry was found
  (`AgentControl-CommittedAgentFilesCache-Cache`).

**Set**: Records a result for a repo at a specific `HEAD` hash, overwriting any previous entry
for the same key (`AgentControl-CommittedAgentFilesCache-Cache`).

**Invalidate**: Removes every cached entry (across all `HEAD` hashes) for a specific repo path,
for use by a manual refresh that should never trust a stale cached value.

#### Error Handling

No member throws for its own logic; all three public methods operate purely on the in-memory
dictionary. Repo paths are normalized (trailing directory separator trimmed) before use as a
cache key, so equivalent paths differing only by a trailing slash are treated as the same key
rather than silently producing duplicate, inconsistent cache entries.

#### Dependencies

- **.NET BCL** — `System.Collections.Generic.Dictionary`, `System.IO.Path`.

#### Callers

- **RepoCardViewModel.RefreshBranchAndCommittedFiles** — consults `TryGetCached` after
  resolving the current `HEAD` hash via `GitClient.GetHeadCommitHash`, and calls `Set` on a
  cache miss after invoking `GitClient.HasCommittedAgentFiles`.
