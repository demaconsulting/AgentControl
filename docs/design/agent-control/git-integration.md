## GitIntegration

![GitIntegration Structure](GitIntegrationView.svg)

### Overview

The `GitIntegration` subsystem spans `GitClient.cs` (invokes the configured git executable
for status, pull, branch, and committed-files queries) and `CommittedAgentFilesCache.cs`
(caches the committed-agent-files-changed check keyed by HEAD commit hash). It provides the
observable git-derived status/actions surfaced on each repo card. The subsystem contains two
units: `GitClient` and `CommittedAgentFilesCache`.

### Interfaces

**GitClient.IsWorkingTreeClean**: Reports whether a repo's working tree is clean.

- *Type*: In-process .NET instance method (invokes an external git process).
- *Role*: Provider.
- *Contract*: Gates the Pull action; a git status failure raises a clear error rather than a
  silent false negative (`AgentControl-GitIntegration-PullGating`,
  `AgentControl-GitClient-CheckWorkingTree`).
- *Constraints*: Throws `InvalidOperationException` when the git executable cannot be found
  or the status command fails.

**GitClient.Pull**: Pulls a repo's latest commits.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Returns a result value indicating success or failure; a failed pull (no
  network, auth failure, no upstream) is expected and must not throw
  (`AgentControl-GitIntegration-Pull`, `AgentControl-GitClient-Pull`).
- *Constraints*: Never throws for a failed pull outcome.

**GitClient.HasCommittedAgentFiles / GetHeadCommitHash**: Detects committed agent files and
resolves the cache key for that check.

- *Type*: In-process .NET instance methods.
- *Role*: Provider.
- *Contract*: `HasCommittedAgentFiles` reports whether any of the four managed folders are
  tracked by git at `HEAD`; `GetHeadCommitHash` returns the trimmed current HEAD hash used to
  key `CommittedAgentFilesCache` (`AgentControl-GitIntegration-DetectCommittedFiles`,
  `AgentControl-GitClient-DetectCommittedFiles`, `AgentControl-GitClient-HeadHash`).
- *Constraints*: Both raise `InvalidOperationException` on query failure.

**GitClient.GetCurrentBranch**: Resolves a repo's current branch name.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Reads `.git/HEAD` directly as a fast path (recognizing a normal branch, a
  detached HEAD, and a worktree indirection), falling back to invoking git when the fast path
  cannot determine an answer (`AgentControl-GitIntegration-CurrentBranch`,
  `AgentControl-GitClient-CurrentBranch`).
- *Constraints*: Throws `InvalidOperationException` if both the fast path and the subprocess
  fallback fail.

**CommittedAgentFilesCache**'s get/set/invalidate methods.

- *Type*: In-process .NET instance methods.
- *Role*: Provider.
- *Contract*: Returns a cached value for a repo path and HEAD hash previously stored; treats a
  different hash or unknown repo path as a cache miss; overwrites a previously cached value
  for the same key (`AgentControl-CommittedAgentFilesCache-Cache`).
- *Constraints*: `Invalidate` clears only the entry for the given repo path, leaving other
  repos' cached entries untouched.

### Design

`GitClient` invokes the configured git executable (default `git`, overridable via Settings)
as an external process for every query. Status/pull/committed-files queries surface failures
as either a result value (`Pull`, since failure is a normal outcome the UI must display) or an
`InvalidOperationException` (status, committed-files, HEAD hash — an unexpected environment
problem such as a missing executable or a corrupt repo). `GetCurrentBranch` is optimized
separately: because the recent-repos list may show many repo cards, it reads `.git/HEAD`
directly first (recognizing a normal branch ref, a detached HEAD, and a worktree
indirection file) and only falls back to invoking `git` when that fast path cannot determine
an answer, keeping the common case free of subprocess overhead.

`CommittedAgentFilesCache` implements architecture.md's "Repo-fact caching strategy" for the
committed-agent-files badge specifically: since that fact is a property of the git tree, it is
safe to reuse a cached result keyed by the repo's current HEAD commit hash (cheap to obtain
via `GetHeadCommitHash`) rather than re-running `git ls-files` against the four known folders
on every refresh. By contrast, working-tree dirty/clean state is deliberately **not** cached
this way (per the same architecture.md decision) since it reflects uncommitted local edits
independent of any commit; `RepoCardViewModel`'s full `Refresh` recomputes it lazily rather
than eagerly for every card at app launch.
