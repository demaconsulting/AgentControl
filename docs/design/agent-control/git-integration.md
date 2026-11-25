## GitIntegration

![GitIntegration Structure](GitIntegrationView.svg)

### Overview

The `GitIntegration` subsystem spans `GitClient.cs` (invokes the configured git executable
for status, pull, branch, and committed-files queries, for listing and switching branches, for cloning a repository
and for stopping git tracking managed agent
folders), `CommittedAgentFilesCache.cs`
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
`AgentControl-System-GitSignInBackgroundNeverPrompts` and `AgentControl-System-GitSignIn`). It also tells whether a
folder is the top folder of a Git repository (`AgentControl-GitIntegration-RepositoryRoot`, which supports
`AgentControl-System-AddGitReposOnly`), and stops a local git command that does not answer within its time limit or
whose caller gives up (`AgentControl-GitIntegration-LocalGitTimeout`, which supports
`AgentControl-System-ResponsiveRepoListWork`). It lists and switches branches (`-ListBranches` and `-SwitchBranch`,
which support `AgentControl-System-SwitchBranch`) and clones a repository (`-CloneRepository`, which supports
`AgentControl-System-CloneRepository`; and `-CloneUrlIsData`, `-CloneNeverLogsCredentials` and
`-CloneRemovesOnlyWhatItCreated`, which support `AgentControl-System-CloneSafety`, the credentials one also
`AgentControl-System-CredentialSafety`). The subsystem contains four
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

**GitClient.GetTrackedAgentFolders / UntrackAgentFolders**: Reports which managed agent
folders git tracks and stops git tracking chosen ones.

- *Type*: In-process .NET instance methods.
- *Role*: Provider.
- *Contract*: `GetTrackedAgentFolders` lists, from git's current index, which of the four managed
  folders have tracked files; `UntrackAgentFolders` removes the given folders from the index only,
  leaving every file on disk and every other tracked path as it was
  (`AgentControl-GitIntegration-UntrackAgentFolders`, `AgentControl-GitClient-ListTrackedAgentFolders`,
  `AgentControl-GitClient-UntrackAgentFolders`, `AgentControl-GitClient-UntrackOnlyAgentFolders`).
- *Constraints*: `UntrackAgentFolders` accepts only the four known folders (anything else is
  refused before git runs) and raises `InvalidOperationException` with git's explanation when git
  fails; both raise `InvalidOperationException` when the query cannot run.

**GitClient.GetCurrentBranch**: Resolves a repo's current branch name.

- *Type*: In-process .NET instance method.
- *Role*: Provider.
- *Contract*: Reads `.git/HEAD` directly as a fast path (recognizing a normal branch, a
  detached HEAD, and a worktree indirection), falling back to invoking git when the fast path
  cannot determine an answer (`AgentControl-GitIntegration-CurrentBranch`,
  `AgentControl-GitClient-CurrentBranch`).
- *Constraints*: Throws `InvalidOperationException` if both the fast path and the subprocess
  fallback fail.

**GitClient.GetBranches / SwitchBranch**: Lists the branches a repo already knows about and switches the repo to one of
them.

- *Type*: In-process .NET instance methods.
- *Role*: Provider. Its consumer is the card's "Switch branch..." action in `LauncherUI`.
- *Contract*: `GetBranches` returns the local and remote-tracking branches git already has (no fetch), the current
  branch or, for a detached HEAD, the short commit id; `SwitchBranch` runs `git switch -- <name>` (local) or
  `git switch --track -- <remote>/<name>` (remote) and returns the outcome, a refusal by git being a result with git's
  explanation, not an exception (`AgentControl-GitIntegration-ListBranches`, `AgentControl-GitIntegration-SwitchBranch`,
  `AgentControl-GitClient-ListBranches`, `AgentControl-GitClient-ListBranchesRemoteDistinct`,
  `AgentControl-GitClient-ListBranchesDetached`, `AgentControl-GitClient-SwitchBranch`,
  `AgentControl-GitClient-SwitchRefusalReported`).
- *Constraints*: A switch never forces, discards, merges or stashes (`AgentControl-GitClient-SwitchNeverForces`); a
  blank or option-like name is refused with `ArgumentException` before git runs; `GetBranches` throws
  `InvalidOperationException` when git cannot list the refs.

**GitClient.Clone / CheckCloneUrl / CheckCloneDestination**: Clones a repository into a new or empty folder, and
tells beforehand why an address or a folder cannot be used.

- *Type*: In-process .NET instance method (`Clone`) and static methods (`CheckCloneUrl`, pure; `CheckCloneDestination`,
  reads the disk).
- *Role*: Provider. Its consumers are the clone dialog and the main window's clone operation in `LauncherUI`.
- *Contract*: `Clone` runs `git clone -- <address> <destination>` in the destination's parent folder under the clone
  limit and returns the outcome with credentials removed; a clone git refuses is a result, not an exception; on cancel
  or timeout the partial result is removed - only what the call created - and after a failed clone nothing is deleted
  (`AgentControl-GitIntegration-CloneRepository`, `-CloneUrlIsData`, `-CloneNeverLogsCredentials`,
  `-CloneRemovesOnlyWhatItCreated`).
- *Constraints*: An address that is blank, multi-line, starts with `-` or uses a remote-helper syntax such as `ext::`,
  and a destination that is relative, has no parent folder, is a file or is a non-empty folder, are refused with
  `ArgumentException` (reason in the message) before git starts; a `GitCommandTimeoutException` or
  `OperationCanceledException` reports a stopped clone; on a guarded thread nothing is read or started.

**GitClient time limits and cancellation**: Every `GitClient` query, `Pull`, `SwitchBranch` and `Clone` runs under a
time limit and accepts a cancellation token.

- *Type*: In-process .NET: the optional `GitClientTimeouts` constructor argument and the optional
  `CancellationToken` parameter of `IsWorkingTreeClean`, `Pull`, `GetHeadCommitHash`, `HasCommittedAgentFiles`,
  `GetCurrentBranch`, `GetBranches`, `SwitchBranch` and `Clone`.
- *Role*: Provider. Its consumers are the cards' reads in the `LauncherUI` shared refresh queue, which pass the queue's
  shutdown token, and the add's folder checks.
- *Contract*: A command that has not finished within its limit (60 seconds on the local repository, 10 minutes for a
  pull or a branch switch, 60 minutes for a clone) is stopped with every process it started and reported as
  `GitCommandTimeoutException` (an
  `InvalidOperationException` that names the limit); a canceled command is stopped the same way and reported as
  `OperationCanceledException` (`AgentControl-GitIntegration-LocalGitTimeout`, `AgentControl-GitClient-TimeoutStopsGit`,
  `AgentControl-GitClient-CancelStopsGit`).
- *Constraints*: The methods block their calling thread and must run off the UI thread; on a thread guarded by
  `BlockingCallGuard` (`Utilities`) they are refused before git starts.

**GitClient.IsRepositoryRoot**: Reports whether a folder is the top folder of a Git repository.

- *Type*: In-process .NET static method (file-system check only; no git process).
- *Role*: Provider. Its consumer is `MainWindowViewModel` (`LauncherUI`), which refuses to add a folder for which it
  returns `false`.
- *Contract*: `true` when the folder has a `.git` folder or a `.git` file (a linked worktree or a submodule) directly
  in it; `false` for a subfolder of a repository (the parents are not walked) and for a folder that does not exist
  (`AgentControl-GitIntegration-RepositoryRoot`, `AgentControl-GitClient-RepositoryRoot`).
- *Constraints*: Throws `ArgumentException` for a blank path; stateless and thread-safe.

**CommittedAgentFilesCache**'s get/set/invalidate methods.

- *Type*: In-process .NET instance methods.
- *Role*: Provider.
- *Contract*: Returns a cached value for a repo path and HEAD hash previously stored; treats a
  different hash or unknown repo path as a cache miss; overwrites a previously cached value
  for the same key (`AgentControl-CommittedAgentFilesCache-Cache`).
- *Constraints*: `Invalidate` clears only the entry for the given repo path, leaving other
  repos' cached entries untouched. Every method is safe to call from several threads at the same time (a private lock
  guards the dictionary), because cards consult it from worker threads while the UI thread may invalidate it
  (`AgentControl-CommittedAgentFilesCache-ConcurrentUse`).

### Design

**Queries from any thread** (`AgentControl-GitIntegration-QueriesFromAnyThread`). The launcher reads git for many repos
off its UI thread (adding folders, refreshing the cards after a settings save, the working-tree check of shown
cards), so every repository query of the subsystem - branch, `HEAD` commit, committed agent folders, repository root
and working-tree state - must give the same answer when made from several threads at once. `GitClient` holds no
mutable state (it starts a new git process for every call, and `IsRepositoryRoot` is a static file-system check), so
concurrent calls are independent; `CommittedAgentFilesCache`, the one shared mutable object, takes a private lock
around its dictionary in every member.

**Branches and clones** (`AgentControl-GitIntegration-ListBranches`, `-SwitchBranch`, `-CloneRepository`,
`-CloneUrlIsData`, `-CloneNeverLogsCredentials`, `-CloneRemovesOnlyWhatItCreated`). Switching and cloning are the two
places where the launcher acts on a developer's files at the developer's request, so both keep git as the arbiter and
the user's data intact. The branch list is what git already has locally: no implicit fetch, so it is quick and never
prompts for a sign-in (a pull updates the remote-tracking branches). A switch is `git switch` with no force, discard,
merge or stash: git refuses what would lose work and the launcher passes its explanation on. A clone treats the address
as data (a leading `-` or a remote-helper address is refused, `--` separates it from the options), asks nothing on a
terminal, shows no credential in a log (the one place a command line is logged, `RunGit`, redacts it) and, when it is
killed, removes only what it created - an existing empty folder is kept - never following links and never after a
failure git reported itself.

`GitClient` invokes the configured git executable (default `git`, overridable via Settings)
as an external process for every query. Status/pull/committed-files queries surface failures
as either a result value (`Pull`, since failure is a normal outcome the UI must display) or an
`InvalidOperationException` (status, committed-files, HEAD hash — an unexpected environment
problem such as a missing executable or a corrupt repo). `GetCurrentBranch` is optimized
separately: because the recent-repos list may show many repo cards, it reads `.git/HEAD`
directly first (recognizing a normal branch ref, a detached HEAD, and a worktree
indirection file) and only falls back to invoking `git` when that fast path cannot determine
an answer, keeping the common case free of subprocess overhead.

Every `GitClient` command runs under a limit (`GitClientTimeouts`): a git on a network share, on a huge tree or held
by a stuck lock may never answer, and without a limit it would hold the worker that waits for it - in the launcher one
of the two slots of the shared refresh queue - and keep running unnoticed. A command past its limit, or one whose
caller cancels (the window closed), is killed together with every process it started (`Process.Kill` of the whole
tree), so no git is left behind; the timeout is reported as `GitCommandTimeoutException`, which existing callers
already treat as "git could not tell" and the card shows as a message. Before starting anything `GitClient` asks
`BlockingCallGuard` whether the calling thread may block, so the tests catch a git call made on the UI thread.

**Package-source and credential git off the caller's thread** (`AgentControl-GitIntegration-NoBlockingStart`).
`GitRemoteClient` and `GitCredentialClient` are asynchronous and are asked from the UI thread (a connection test, an
upgrade, the source editor's credential-helper question and "Forget saved sign-in") or from whatever thread the NuGet
library calls the credential service on. Both start git (and `GitRemoteClient` creates its work folders) inside
`Task.Run` and await everything with `ConfigureAwait(false)`, so the asking thread never waits for a process start, and
both ask `BlockingCallGuard` right before `Process.Start`, so a start that slips back onto a thread the tests guard
fails them.

`GitClient.IsRepositoryRoot` is the one question answered without git at all: it only looks for `.git` (a folder or a
file) directly in the folder, so the launcher can check every folder of a multi-folder add or a file-manager drop
cheaply. It deliberately does not walk up to the parent folders as `git` would, because a subfolder of a repository is
not a place to put the agent folders.

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
