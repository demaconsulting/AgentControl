## GitIntegration

![GitIntegration Structure](GitIntegrationView.svg)

### Overview

The `GitIntegration` subsystem spans `GitClient.cs` (invokes the configured git executable
for status, pull, branch, and committed-files queries), `CommittedAgentFilesCache.cs`
(caches the committed-agent-files-changed check keyed by HEAD commit hash),
`GitCredentialClient.cs` (keeps, finds and removes a package feed's sign-in in the user's own Git
credential helper) and `GitRemoteClient.cs` (runs hardened git against untrusted remote repositories for the Git
package source). It provides the observable git-derived status/actions surfaced on
each repo card and the saved sign-ins of package servers
(`AgentControl-GitIntegration-SavedSignIns`, which supports `AgentControl-System-CredentialSafety`).
It also provides the safe use of a remote repository as a package source (`AgentControl-GitIntegration-RemoteGit`,
`-RemoteGitNoCodeExecution`, `-RemoteGitBounded`, `-RemoteGitFailureReporting` and `-RemoteGitMinimumVersion`, which
support `AgentControl-System-GitSourceSafety`, `AgentControl-System-GitSourceTransport` and
`AgentControl-System-GitSources`; and `-RemoteGitNoBackgroundPrompt` and `-RemoteGitSignIn`, which support
`AgentControl-System-GitSignInBackgroundNeverPrompts` and `AgentControl-System-GitSignIn`). The subsystem contains four
units: `GitClient`, `CommittedAgentFilesCache`, `GitCredentialClient` and `GitRemoteClient`.

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

**GitRemoteClient**: Runs git against a remote repository that is untrusted, on behalf of the Git package source.

- *Type*: In-process .NET asynchronous interface (`EnsureSupportedVersionAsync`, `ListTagsAsync`,
  `InitRepositoryAsync`, `FetchTagAsync`, `ListTreeAsync` and `ReadBlobsAsync`, plus the tag-name rule `IsSafeTagName`)
  implemented over external git processes.
- *Role*: Provider. Its one consumer is `GitSourceProvider` (`AgentPackageManagement`), so the dependency points from
  `AgentPackageManagement` to `GitIntegration`; the client uses no code of the other units of this subsystem.
- *Boundary*: Everything that touches the user's `git` executable for a remote package repository is behind this
  interface, and nothing else in the application starts git for one. What is inside the boundary: the hardening of
  every command, the neutral working folder, the time and output limits, the classification of failures and the
  protection of secrets in logs. What is outside: the meaning of a tag (the tag pattern), the address rules, the
  layout rules, the files written to the cache and the user-facing failure kinds - `GitSourceProvider` owns those.
- *Input*: Values from outside the boundary are checked before they reach a command line: a `GitRemoteTarget` (the
  address text and the one `GitTransport` allowed for it, derived by the provider from `GitRepositoryAddress`), a tag
  glob or tag name that passes the tag-name rule, a full commit id, a non-empty list of layout paths, and the entries
  of a tree listing for blob reading. Anything else is an `ArgumentException` before git starts.
- *Output*: The tags of the remote with their peeled commits, the commit a fetched tag points to, the entries of the
  listed tree (mode, type, object id, size, path), and the raw bytes of the named blobs handed to a destination
  stream the caller opens.
- *Failure contract*: Every failure of git itself is a `GitRemoteException` (an `InvalidOperationException`) with a
  `GitFailureKind` - `AuthenticationRequired`, `NotFound`, `Unreachable`, `HostKeyUnverified`, `UnsafeRepository`,
  `TimedOut`, `GitNotFound`, `OutputTooLarge`, `GitTooOld`, `ProcessNotStopped` or `Other` - and a fixed message per
  kind that never repeats git's text or the address. `OperationCanceledException` appears only when the caller's token
  was cancelled and the process was confirmed to have ended
  (`AgentControl-GitIntegration-RemoteGit`, `-RemoteGitFailureReporting`, with the unit requirements of
  `GitRemoteClient`).
- *Constraints*: Never interactive in the background; sign-in UI only for work the user started; time limit that kills
  the whole process tree; output bounded in bytes; the git version is checked against the minimum supported version
  (the `MinimumGitVersion` constant) when the caller asks.

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

`GitRemoteClient` is a fourth, deliberately separate unit because its threat model differs from both neighbors: the
repository and the server behind it are untrusted, so every command is hardened by command-scoped configuration and
environment (no hooks, one transport, no submodules or filters, no trace output), run asynchronously from a neutral
folder with a time and output limit that kills the whole process tree, and reported through classified failures with
fixed messages. A git older than the minimum supported Git version (the `MinimumGitVersion` constant; its value is
stated in the requirements and the user guide) is refused up front with an actionable message, because the single-tag
fetch needs `fetch --no-write-fetch-head`. It shares only the git executable setting with `GitClient` and
`GitCredentialClient`; none of their code was changed. The user's own credential helper, proxy and CA settings are
left in force, so sign-in is whatever the user already has - and it may show its UI only when the work was started by
the user (the same `allowPrompt` switch the package sources use). The full command lines, the environment and the
reasoning are in the *GitRemoteClient Design*; its only caller is `GitSourceProvider` (`AgentPackageManagement`), so
the dependency points from `AgentPackageManagement` to `GitIntegration`.

**Collaboration between `GitSourceProvider` and `GitRemoteClient`.** The provider owns the meaning, the client owns
the git processes. Listing versions is one step; acquiring one version is a fixed sequence in which each step
depends on the one before and a failure stops the sequence without publishing anything:

1. *Version check* - when the application asks for it (`GitSourceOptions.EnforceMinimumGitVersion`),
   `EnsureSupportedVersionAsync` runs `git --version` once per executable. A too-old git ends the sequence with
   `GitTooOld`.
2. *List tags* - `ListTagsAsync` runs `ls-remote --tags` with a glob the provider derived from the tag pattern and
   returns each tag with the commit it points to; the provider matches the names against the pattern and the address's
   transport is the only one allowed. This is the whole of what listing versions, previewing a pattern and the
   background upgrade check do - there is no clone and no fetch.
3. *Initialize an empty repository* - `InitRepositoryAsync` creates it in a temporary folder of the provider's cache,
   from an empty template.
4. *Fetch one tag* - `FetchTagAsync` fetches exactly that tag, one level deep and without other tags or submodules,
   and returns the commit it points to; the provider requires that commit to equal the one it listed.
5. *List the tree* - `ListTreeAsync` lists the entries of the fetched commit below the layout paths; the provider
   validates every entry (kinds, links and submodules, names, limits) *before* anything is written.
6. *Read blobs* - `ReadBlobsAsync` reads the validated blobs with `git cat-file --batch`, verifies each answer against
   the listed entry and hands the raw bytes to a stream the provider opens in its temporary folder. No file is checked
   out, so no filter, attribute or line-ending conversion applies and the bytes are exactly those of the commit.

**Failure propagation.** The client reports every failure of git as a `GitRemoteException` of a fixed kind with a fixed
message; the provider translates the kinds into the failures the rest of the application understands:
`AuthenticationRequired` into a `PackageSourceAccessException` of kind `AuthenticationFailed` ("sign-in needed", which
the card shows in the background and the dialogs show with the actionable text), `HostKeyUnverified` and `Unreachable`
into `Unreachable`, `NotFound` into `NotFound` and `TimedOut` into `TimedOut`; `GitTooOld` passes through unchanged so
the application can recognize it; every other kind (git missing, an unsafe repository owner, output too large, a
process that could not be stopped) becomes an `InvalidOperationException` with the client's message. Neither side puts
git's own text, the address or an argument into a message or a log: the provider's messages name the source alias
only. A cancelled token is an `OperationCanceledException` at every step. The *GitRemoteClient Design* has the
unit-level contract of each operation, and the *GitSourceProvider Design* the validation and the cache.
