### GitClient

![GitIntegration Structure](GitIntegrationView.svg)

#### Purpose

`GitClient` wraps the configured git executable to answer every git-derived question the
`LauncherUI` subsystem needs about a repo: working-tree cleanliness, pull execution, current
branch, `HEAD` commit hash, and whether any of the four known agent folders are committed to
the tree. It is also the one place AgentControl changes what git tracks: it reports which of
the four folders are tracked and can stop git tracking them (index only), which lets the
`LauncherUI` subsystem convert a repo that committed its agent files. Each public method
invokes a single git subcommand via `RunGit` and translates its
exit code/output into a typed result or exception, isolating every other unit in the codebase
from git's process-based interface. It also answers, without running git, whether a folder is the
top folder of a Git repository, so the launcher can refuse to track a folder that is not one.
Every git command runs under a time limit and can be canceled, so a git that never answers (a
network share, a huge tree, a stuck lock) neither holds its caller forever nor keeps running
unnoticed (`AgentControl-GitClient-TimeoutStopsGit`, `AgentControl-GitClient-CancelStopsGit`).

#### Data Model

**GitCommandResult** (record, same file): `ExitCode: int`, `StandardOutput: string`,
`StandardError: string`, plus a derived `Succeeded` (`ExitCode == 0`) property — the uniform
result type returned by `Pull` and used internally by every other method.

**\_gitExecutablePath**: `string` — the configured git executable path or bare command name
(defaults to `"git"`, resolved via `PATH`).

**\_logger**: `ILogger<GitClient>` — records process-launch diagnostics (arguments, exit code,
timing, stderr, native error codes) for `RunGit`, added specifically to diagnose the
intermittent exit-code `-1` flakiness documented in
`.agent-logs/implementation-agentcontrol-v1-final-3e91c7.md`.

**GitClientTimeouts** (record, same file): `LocalCommand`, `Pull` and `Stop` (`TimeSpan`, each
positive) — how long a command on the local repository may run, how long a pull may run, and how
long to wait for a stopped command to end. `Default` is 60 seconds (enough for a cold
`git status` of a large tree on a network share), 10 minutes (a pull is network work and may wait
for the credential helper's own sign-in window) and 10 seconds.

**\_timeouts**: `GitClientTimeouts` — the constructor's optional `timeouts` argument, or
`GitClientTimeouts.Default`; tests pass a one-second local limit against a stub that hangs.

**GitCommandTimeoutException** (sealed, same file): derives from `InvalidOperationException`, the
type every method already throws when git cannot answer, so existing callers keep treating a hung
git as "git could not tell"; carries the exceeded **Timeout** (`TimeSpan`) so a caller can tell the
user.

**KnownAgentFolders**: `static readonly string[]` — the four folders (`.github/agents`,
`.github/standards`, `.github/templates`, `.github/skills`) whose tracking status
`HasCommittedAgentFiles` checks, `GetTrackedAgentFolders` reports and `UntrackAgentFolders`
accepts (and refuses anything else).

#### Key Methods

**IsWorkingTreeClean**: Determines whether a repo's working tree is clean via
`git status --porcelain`.

- *Parameters*: `string repositoryPath`, `CancellationToken cancellationToken = default`.
- *Returns*: `bool`.
- *Postconditions*: `true` when the command produces no output
  (`AgentControl-GitClient-CheckWorkingTree`).

**Pull**: Performs `git pull`, under the pull limit rather than the local-command limit.

- *Parameters*: `string repositoryPath`, `CancellationToken cancellationToken = default`.
- *Returns*: `GitCommandResult` — callers check `Succeeded` rather than assuming success, since
  a failed pull (e.g. merge conflict) is a normal outcome to surface to the user, not an
  exceptional one (`AgentControl-GitClient-Pull`).

**GetHeadCommitHash**: Gets the repo's current `HEAD` commit hash via `git rev-parse HEAD`.

- *Parameters*: `string repositoryPath`, `CancellationToken cancellationToken = default`.
- *Returns*: `string` — the trimmed hash. Per architecture.md's repo-fact caching strategy,
  this is the cheap operation used to key `CommittedAgentFilesCache`
  (`AgentControl-GitClient-HeadHash`).

**HasCommittedAgentFiles**: Determines whether any of the four known agent folders are
tracked at `HEAD`, via `git ls-files -- {folders}`.

- *Parameters*: `string repositoryPath`, `CancellationToken cancellationToken = default`.
- *Returns*: `bool`. Purely advisory — never modifies git tracking state, only reports whether
  the developer appears to have accidentally committed proprietary agent content
  (`AgentControl-GitClient-DetectCommittedFiles`).

**GetTrackedAgentFolders**: Lists which of the four known agent folders currently have files
tracked by git, via a single `git ls-files -z -- {folders}`.

- *Parameters*: `string repositoryPath`.
- *Returns*: `IReadOnlyList<string>` — repo-relative `/`-separated folder paths (for example
  `.github/agents`) in the fixed order of `KnownAgentFolders`; empty when none is tracked.
- *Postconditions*: Reads the index afresh on every call (no cache), so the answer is current
  at the moment of a decision. `-z` output is used so git never quotes an unusual file name,
  and a folder counts as tracked when a listed path is the folder path itself (a file, link or submodule tracked at
  that exact path) or lies below `{folder}/` - never a sibling that only shares the name's prefix
  (`AgentControl-GitClient-ListTrackedAgentFolders`).

**UntrackAgentFolders**: Stops git tracking the given agent folders via
`git rm -r --cached --quiet -- {folders}`.

- *Parameters*: `string repositoryPath`, `IReadOnlyCollection<string> folders` — each one of
  `KnownAgentFolders` and each with at least one tracked file (as returned by
  `GetTrackedAgentFolders`; `git rm` refuses a path that matches nothing).
- *Postconditions*: Only the index changes: `--cached` leaves every working-tree file in
  place, no force flag is used (git's own protection of staged work applies), paths follow `--`
  so they cannot be read as options, and nothing is committed (the deletions stay staged for the
  user to commit) (`AgentControl-GitClient-UntrackAgentFolders`).
- *Errors*: An empty list, `null`, or any value that is not a known agent folder throws
  `ArgumentException`/`ArgumentNullException` before git is run
  (`AgentControl-GitClient-UntrackOnlyAgentFolders`); a git failure (for example an index lock
  held by another git process) throws `InvalidOperationException` carrying git's message.

**RestoreAgentFolderTracking**: Undoes `UntrackAgentFolders` for the given agent folders via
`git reset -q -- {folders}`, restoring their index entries from `HEAD` and leaving the working tree untouched; used
to roll back a conversion whose package could not be applied (`AgentControl-GitClient-RestoreAgentFolderTracking`). It
accepts only `KnownAgentFolders` (same argument checks as `UntrackAgentFolders`) and throws
`InvalidOperationException` carrying git's message when `git reset` fails.

**GetCurrentBranch**: Gets the repo's current branch name, or `"(detached)"` for a detached
`HEAD`.

- *Parameters*: `string repositoryPath`, `CancellationToken cancellationToken = default`.
- *Returns*: `string`.
- *Postconditions*: Implements a hybrid fast path: first attempts a direct read of
  `<repositoryPath>/.git/HEAD` (following one level of `gitdir:` indirection for worktrees/
  submodules), parsing `ref: refs/heads/<name>` or detecting a raw hex commit hash. On any
  parse/IO anomaly, falls back unconditionally to `git rev-parse --abbrev-ref HEAD` via
  `RunGit`, so correctness never depends on the fast path being right — only on it usually
  being right, keeping the common case free of a subprocess spawn
  (`AgentControl-GitClient-CurrentBranch`).

**RunGit** (private): Runs one git subcommand in the repository folder and captures its exit
code and output. The other methods pass the limit (`LocalCommand`, or `Pull` for `Pull`) and their
token (`GetTrackedAgentFolders`, `UntrackAgentFolders` and `RestoreAgentFolderTracking` pass none).

- *Algorithm*: (1) ask `BlockingCallGuard.ThrowIfGuarded("git {subcommand}")` (see *BlockingCallGuard*), so a call on
  a thread the tests guard is refused before anything starts; (2) throw `OperationCanceledException` when the token is
  already signaled; (3) start the process with redirected output read asynchronously (so full pipes never deadlock);
  (4) register on the token a kill of the whole process tree (`Process.Kill(entireProcessTree: true)`, so helpers git
  started - a credential manager, an SSH client - end too) and wait for the exit up to the limit; (5) when the limit
  passed, kill the tree, wait up to `Stop` for it to end, log a warning and throw `GitCommandTimeoutException` ("'git
  {subcommand}' did not finish within {n} seconds and was stopped."); (6) when the token was signaled, throw
  `OperationCanceledException` (the output of a killed process means nothing); (7) otherwise read the output and
  return the result.
- *Postconditions*: no git process this call started is left running when it throws for a timeout or a
  cancellation, unless the kill itself failed (logged).

**IsRepositoryRoot** (static): Determines whether a folder is the top folder of a Git repository.

- *Parameters*: `string folderPath` — absolute path of the folder to check.
- *Returns*: `bool` — `true` when `<folderPath>/.git` exists as a folder or as a file; otherwise `false`.
- *Algorithm*: `Path.Combine(folderPath, ".git")`, then `Directory.Exists || File.Exists`. A `.git` file counts
  because a linked worktree and a submodule have one (it names the real git directory); its content is not read.
- *Preconditions*: `folderPath` is not null, empty or whitespace.
- *Postconditions*: Only the folder itself is examined: the parent folders are deliberately not walked as `git`
  itself would, so a subfolder of a repository is not a root (tracking it would put the agent folders in the wrong
  place). A folder that does not exist or cannot be read is not a root. No git process is started, so the check is
  cheap enough for every folder of a multi-folder add; the method is stateless and thread-safe
  (`AgentControl-GitClient-RepositoryRoot`).

#### Error Handling

Every public method throws `ArgumentException` for a null/empty/whitespace `repositoryPath`
and `InvalidOperationException` when the git executable cannot be started or the underlying
git subcommand exits non-zero (except `Pull`, whose non-zero exit is reported via
`GitCommandResult.Succeeded` rather than thrown, since a failed pull is an expected outcome).
`IsRepositoryRoot` throws `ArgumentException` for a null/empty/whitespace `folderPath` and otherwise never throws: a
missing or unreadable folder is reported as `false`.
A command that outlives its limit throws `GitCommandTimeoutException` (an `InvalidOperationException`), and a
canceled one `OperationCanceledException`; both stop the process tree first. The constructor throws
`ArgumentOutOfRangeException` for a limit that is not positive. On a thread guarded by `BlockingCallGuard` every method
except `IsRepositoryRoot` throws `BlockingCallOnGuardedThreadException` without starting git (or reading `.git/HEAD`);
callers must not catch it as an ordinary failure.
`RunGit` logs a warning when the process takes longer than 10 seconds to exit (distinguishing
a hung process from a fast failure) and logs a caught `Win32Exception`'s `NativeErrorCode` at
`Error` level before wrapping it in an `InvalidOperationException` — both added specifically to
close the diagnostic gaps identified while investigating the intermittent exit-code `-1`
flakiness.

#### Dependencies

- **AppLogging** (`Logging` subsystem) — supplies the fallback `ILoggerFactory` when no
  `logger` is passed to the constructor.
- **BlockingCallGuard** (`Utilities` subsystem) — asked before every git process and before the `.git/HEAD` read, so
  the tests can catch a git call made on the UI thread.
- **.NET BCL** — `System.Diagnostics.Process`, `System.Diagnostics.Stopwatch`.

#### Callers

- **RepoCardViewModel** — constructs a `GitClient` per action (using the resolved git executable path and the card's
  time limits; the reads of its state run on worker threads of the shared refresh queue, whose shutdown token they pass,
  see *RepoRefreshQueue*) and calls `IsWorkingTreeClean`, `Pull`, `GetHeadCommitHash`, `HasCommittedAgentFiles`, and
  `GetCurrentBranch` across its refresh/pull methods, and `GetTrackedAgentFolders` and `UntrackAgentFolders` when it
  applies a package to a repo whose agent folders are tracked by git.
- **MainWindowViewModel** (`LauncherUI` subsystem) — reads a new repo's cheap facts through a `GitClient` on a worker
  thread of the shared refresh queue while it is added, and calls the static `IsRepositoryRoot` when a folder is added
  as a new repo (single add, add to a group, several folders from the picker or dropped from the file manager), and
  refuses the folder as not a Git repository when it returns `false`.
