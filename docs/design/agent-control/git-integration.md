## GitIntegration

![GitIntegration Structure](GitIntegrationView.svg)

### Overview

The `GitIntegration` subsystem spans `GitClient.cs` (invokes the configured git executable
for status, pull, branch, and committed-files queries), `CommittedAgentFilesCache.cs`
(caches the committed-agent-files-changed check keyed by HEAD commit hash) and
`GitCredentialClient.cs` (keeps, finds and removes a package feed's sign-in in the user's own Git
credential helper) and `GitRemoteClient.cs` (runs hardened git against untrusted remote repositories for the Git
package source). It provides the observable git-derived status/actions surfaced on
each repo card and the saved sign-ins of package servers
(`AgentControl-GitIntegration-SavedSignIns`, which supports `AgentControl-System-CredentialSafety`).
It also provides the safe use of a remote repository as a package source (`AgentControl-GitIntegration-RemoteGit`, which
supports `AgentControl-System-GitSourceSafety` and `AgentControl-System-GitSignIn`). The subsystem contains four units:
`GitClient`, `CommittedAgentFilesCache`, `GitCredentialClient` and `GitRemoteClient`.

### Interfaces

**GitCredentialClient (as IRememberedCredentialStore)**: Finds, keeps and removes a package feed's sign-in
and reports whether a credential helper applies to the feed.

- *Type*: In-process .NET asynchronous interface (`IRememberedCredentialStore`, owned by the
  `PackageCredentials` subsystem) implemented over an external git process.
- *Role*: Provider.
- *Contract*: `IsAvailableAsync`, `FillAsync`, `ApproveAsync` and `RejectAsync` act on the feed's origin only;
  an unavailable store is reported as "nothing" or `false`, never as an exception
  (`AgentControl-GitCredentialClient-FillApproveReject`, `AgentControl-GitCredentialClient-HelperAvailability`,
  `AgentControl-GitCredentialClient-UnavailableIsNotAnError`).
- *Constraints*: Never interactive, bounded by a time limit that kills the whole process tree, never logs a
  request or response (`AgentControl-GitCredentialClient-NeverInteractive`,
  `AgentControl-GitCredentialClient-BoundedTime`, `AgentControl-GitCredentialClient-SecretsNeverLogged`).
  Only a cancelled token surfaces as `OperationCanceledException`.

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

`GitCredentialClient` is a separate concern from `GitClient`, and deliberately a separate unit: `GitClient` asks
questions *about a repo* and runs inside it, whereas `GitCredentialClient` talks to the user's credential helper
about a *package feed* and runs from the user's profile folder, so a repo's configuration cannot influence it.
Both use the same git executable (the settings path or the `--git-path` override, resolved per call). The credential
client implements an interface owned by the `PackageCredentials` subsystem (`IRememberedCredentialStore`), so the
dependency between the subsystems points from `GitIntegration` to `PackageCredentials` only; `App` connects the two.
Why Git's credential helper, the `--get-urlmatch` availability check, the origin-scoped generic-provider override, the
never-interactive environment, the time limit with process-tree kill and the neutral working folder are explained in
the *GitCredentialClient Design*.

`GitRemoteClient` is a fourth, deliberately separate unit because its threat model differs from both neighbors: the repository
and the server behind it are untrusted, so every command is hardened by command-scoped configuration and environment (no
hooks, one transport, no submodules or filters, no trace output), run asynchronously from a neutral folder with a time and
output limit that kills the whole process tree, and reported through classified, redacted failures. It shares only the
git executable setting with `GitClient` and `GitCredentialClient`; none of their code was changed. The user's own
credential helper, proxy and CA settings are left in force, so sign-in is whatever the user already has - and
it may show its
UI only when the work was started by the user (the same `allowPrompt` switch the package sources use). The full command
lines, the environment and the reasoning are in the *GitRemoteClient Design*; its only caller is `GitSourceProvider`
(`AgentPackageManagement`), so the dependency points from `AgentPackageManagement` to `GitIntegration`.
