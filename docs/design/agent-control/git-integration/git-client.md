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
from git's process-based interface.

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

**KnownAgentFolders**: `static readonly string[]` — the four folders (`.github/agents`,
`.github/standards`, `.github/templates`, `.github/skills`) whose tracking status
`HasCommittedAgentFiles` checks, `GetTrackedAgentFolders` reports and `UntrackAgentFolders`
accepts (and refuses anything else).

#### Key Methods

**IsWorkingTreeClean**: Determines whether a repo's working tree is clean via
`git status --porcelain`.

- *Parameters*: `string repositoryPath`.
- *Returns*: `bool`.
- *Postconditions*: `true` when the command produces no output
  (`AgentControl-GitClient-CheckWorkingTree`).

**Pull**: Performs `git pull`.

- *Parameters*: `string repositoryPath`.
- *Returns*: `GitCommandResult` — callers check `Succeeded` rather than assuming success, since
  a failed pull (e.g. merge conflict) is a normal outcome to surface to the user, not an
  exceptional one (`AgentControl-GitClient-Pull`).

**GetHeadCommitHash**: Gets the repo's current `HEAD` commit hash via `git rev-parse HEAD`.

- *Parameters*: `string repositoryPath`.
- *Returns*: `string` — the trimmed hash. Per architecture.md's repo-fact caching strategy,
  this is the cheap operation used to key `CommittedAgentFilesCache`
  (`AgentControl-GitClient-HeadHash`).

**HasCommittedAgentFiles**: Determines whether any of the four known agent folders are
tracked at `HEAD`, via `git ls-files -- {folders}`.

- *Parameters*: `string repositoryPath`.
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

**GetCurrentBranch**: Gets the repo's current branch name, or `"(detached)"` for a detached
`HEAD`.

- *Parameters*: `string repositoryPath`.
- *Returns*: `string`.
- *Postconditions*: Implements a hybrid fast path: first attempts a direct read of
  `<repositoryPath>/.git/HEAD` (following one level of `gitdir:` indirection for worktrees/
  submodules), parsing `ref: refs/heads/<name>` or detecting a raw hex commit hash. On any
  parse/IO anomaly, falls back unconditionally to `git rev-parse --abbrev-ref HEAD` via
  `RunGit`, so correctness never depends on the fast path being right — only on it usually
  being right, keeping the common case free of a subprocess spawn
  (`AgentControl-GitClient-CurrentBranch`).

#### Error Handling

Every public method throws `ArgumentException` for a null/empty/whitespace `repositoryPath`
and `InvalidOperationException` when the git executable cannot be started or the underlying
git subcommand exits non-zero (except `Pull`, whose non-zero exit is reported via
`GitCommandResult.Succeeded` rather than thrown, since a failed pull is an expected outcome).
`RunGit` logs a warning when the process takes longer than 10 seconds to exit (distinguishing
a hung process from a fast failure) and logs a caught `Win32Exception`'s `NativeErrorCode` at
`Error` level before wrapping it in an `InvalidOperationException` — both added specifically to
close the diagnostic gaps identified while investigating the intermittent exit-code `-1`
flakiness.

#### Dependencies

- **AppLogging** (`Logging` subsystem) — supplies the fallback `ILoggerFactory` when no
  `logger` is passed to the constructor.
- **.NET BCL** — `System.Diagnostics.Process`, `System.Diagnostics.Stopwatch`.

#### Callers

- **RepoCardViewModel** — constructs a `GitClient` per action (using the resolved git
  executable path) and calls `IsWorkingTreeClean`, `Pull`, `GetHeadCommitHash`,
  `HasCommittedAgentFiles`, and `GetCurrentBranch` across its refresh/pull methods, and
  `GetTrackedAgentFolders` and `UntrackAgentFolders` when it applies a package to a repo whose
  agent folders are tracked by git.
