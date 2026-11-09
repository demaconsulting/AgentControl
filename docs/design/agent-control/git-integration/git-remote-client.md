### GitRemoteClient

![GitIntegration Structure](GitIntegrationView.svg)

#### Purpose

`GitRemoteClient` runs git against a remote repository whose content and answers are untrusted
(`AgentControl-GitIntegration-RemoteGit`, `AgentControl-System-GitSourceSafety`, `AgentControl-System-GitSignIn`). Its
operations are listing tags, creating an empty repository, fetching one tag, listing the entries of a tree and reading
the
raw bytes of blobs - never a checkout. It
is a separate unit from `GitClient` (which asks questions about a repo the user owns, synchronously, with no hardening)
and from `GitCredentialClient` (which talks to the credential helper): none of their code was changed, and this client
is asynchronous, bounded and hardened for a different threat model.

#### Data Model

**GitTransport** (`Https`, `Ssh`, `File`) and **GitRemoteTarget** (`Url`, `Transport`): the address text and the one
transport allowed for it (the provider derives it from `GitRepositoryAddress`).

**GitFailureKind**: `Other`, `AuthenticationRequired`, `NotFound`, `Unreachable`, `HostKeyUnverified`,
`UnsafeRepository`, `TimedOut`, `GitNotFound`, `OutputTooLarge`, `GitTooOld`, `ProcessNotStopped` (a process that
was stopped could not be confirmed to have ended). **GitRemoteException**: `InvalidOperationException` with `Kind` and a
fixed message that never repeats git's text.

**GitRemoteTag** (`Name`, `ObjectSha`, `CommitSha`), **GitTreeEntry** (`Mode`, `Type`, `Sha`, `Size`, `Path`).

**GitRemoteOptions**: git executable resolver, an AgentControl-owned `WorkRoot` (holds the neutral working folder, the
empty hooks folder and the empty template folder, created when needed), `Interactive`, `Logger`, `ExtraConfiguration`
and
`ExtraEnvironment` (test seams, applied last), `ListTimeout`/`FetchTimeout` (defaults: 30 s / 3 min in the background,
5 min when interactive, so a credential-manager dialog can be answered), and two internal test seams: `KillProcessTree`
(replaces the termination of a process tree) and `StopTimeout` (how long a stopped process is waited for; default 10 s).

#### Key Methods

Every command is `git [-C repo] -c ... --literal-pathspecs <command>`. The requirements of this unit state the
observable outcomes (never a hook, only the transport of the address, no prompt in the background, nothing
leaked); this section is the mechanism that achieves them. The `-c` list, in order
(`AgentControl-GitRemoteClient-CommandScopedConfiguration`):

```text
protocol.allow=never  protocol.<kind>.allow=always  protocol.ext.allow=never  protocol.fd.allow=never
core.hooksPath=<empty folder>  core.fsmonitor=false  core.untrackedCache=false
core.autocrlf=false  core.eol=lf  core.protectNTFS=true  core.protectHFS=true
transfer.fsckObjects=true  fetch.fsckObjects=true  fetch.recurseSubmodules=false  submodule.recurse=false
gc.auto=0  maintenance.auto=false  filter.lfs.smudge=  filter.lfs.process=  filter.lfs.required=false
http.followRedirects=initial  http.lowSpeedLimit=1000  http.lowSpeedTime=30
[core.askPass=: background only]  advice.detachedHead=false  <extra configuration>
```

Which settings serve which requirement:

- **No hook** (`-NoHooks`): `core.hooksPath` is an empty AgentControl-owned folder, and `init` is given
  `--template=<empty folder>`, so the user's template directory (`init.templateDir`, `GIT_TEMPLATE_DIR`) and a global
  hooks path
  cannot put a hook into the new repository and a repository cannot ship one into it.
- **Only the transport of the address** (`-TransportAllowList`): `protocol.allow=never`, then
  `protocol.<kind>.allow=always` for the one kind of the address, `protocol.ext.allow=never`, `protocol.fd.allow=never`,
  and
  `GIT_ALLOW_PROTOCOL` = the one kind, so git itself refuses a local path given as https or an `ext::` helper.
- **No submodules** (`-NoSubmodules`): `fetch.recurseSubmodules=false`, `submodule.recurse=false` and
  `--no-recurse-submodules` on the fetch.
- **No filters** (`-NoContentFilters`): `filter.lfs.smudge=`, `filter.lfs.process=`, `filter.lfs.required=false` and
  `GIT_LFS_SKIP_SMUDGE=1` turn the large-file filter off; any other `filter.<name>` driver a repository's
  `.gitattributes` could select from the user's configuration cannot be listed in advance, so content is never
  checked out at all (see *ReadBlobsAsync* below): no checkout, no filter, no attribute or line-ending conversion.
- **No prompt in the background** (`-NoPromptInBackground`) and **sign-in UI when the user started the work**
  (`-InteractiveWhenUserStarted`): the environment below, plus `core.askPass=` for background work only.
- **No leaking** (`-SecretFreeLogs`, `-FailureMessagesFixed`): the arguments and the address are never logged and no
  message repeats git's text; see the process rules.

The remaining settings (`core.fsmonitor=false`, `core.untrackedCache=false`, `core.autocrlf=false`, `core.eol=lf`,
`core.protectNTFS=true`, `core.protectHFS=true`, `transfer.fsckObjects=true`, `fetch.fsckObjects=true`, `gc.auto=0`,
`maintenance.auto=false`, `http.followRedirects=initial`, `http.lowSpeedLimit`/`http.lowSpeedTime`) keep a hostile
repository from running a monitor or maintenance job, being accepted with malformed objects or hanging a transfer. (The
conversion settings matter only to a checkout, which this client never runs; they are kept as a second line of defense.)
The environment, applied to a copy for the child process only: `GIT_TERMINAL_PROMPT=0`; background work also
`GCM_INTERACTIVE=never`, empty `GIT_ASKPASS`/`SSH_ASKPASS`, `SSH_ASKPASS_REQUIRE=never` (work the user started leaves
the
inherited values, so the credential manager and the user's askpass may show their UI); `GIT_ALLOW_PROTOCOL` = the one
transport; `GIT_LFS_SKIP_SMUDGE=1`, `GIT_LITERAL_PATHSPECS=1`, `GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`,
`LC_ALL=LANG=C` (stable English messages for classification), `GIT_CEILING_DIRECTORIES` = the work root; removed: every
`GIT_TRACE*`, `GIT_CURL_VERBOSE`, `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_EXEC_PATH` and similar redirecting
variables (`-NoTraceVariables`). For an ssh remote `GIT_SSH_COMMAND` is the user's own command (from the environment,
else
`core.sshCommand` read once) with `-o BatchMode=yes -o ConnectTimeout=15` appended when it is OpenSSH, and is left alone
when it is a wrapper such as `plink` or the user set only `GIT_SSH` (`-SshBatchMode`); an https or local remote gets
none.

**EnsureSupportedVersionAsync / ParseVersion**: The minimum supported Git version (the `MinimumGitVersion` constant; its
value is stated in the requirements and the user guide) is the oldest git that has `fetch --no-write-fetch-head`, the
capability the single-tag fetch relies on. The method runs `git --version` through the same
hardened path as every command (`git -c ... --literal-pathspecs --version`, in the neutral folder, 30-second limit, 4 KB
output cap), one caller at a time per executable (a per-executable `SemaphoreSlim` gate: callers that arrive while the
check runs wait for it and then find its result, so `-MinimumGitVersionCheckedOnce` holds for concurrent callers too),
reads the version with `ParseVersion` (the text after `git version`: a dotted number, optionally followed by vendor
text such as a Windows or Apple build suffix, of which only the leading numbers are used; anything else is `null`) and
throws a `GitRemoteException` of kind `GitTooOld` whose
message names the minimum and the version in use and says to update Git or choose a newer one under
Settings > Git executable (`-MinimumGitVersionMessage`). A text that cannot be read is logged as a warning and the git
is treated as supported (`-UnreadableVersionNotBlocked`). A
supported (or unreadable) result is remembered in a process-wide dictionary keyed by the resolved executable text
(case-insensitive on Windows), so `git --version` runs at most once per executable; a too-old result and a failure to
run
git
are not remembered, so an updated git is accepted by the next check (`-MinimumGitVersion`). The caller decides when to
ask
(`GitSourceProvider` does, when its options say so).

**ListTagsAsync**: `ls-remote --tags -- <url> <glob>`; the glob must start with `refs/tags/` and use only tag-name
characters and `*`; output lines are parsed, annotated tags resolved through their `^{}` line, and every tag whose
name is
not a safe tag name (below) is dropped without an error (`-ListTagsWithoutClone`, `-PeeledTags`, `-SafeTagNames`).

**IsSafeTagName**: the one implementation of the tag-name rule: only `A-Z a-z 0-9 . _ / + -`, at most 255 characters, no
`..`, not starting with `-`, not ending in `.`, and no path component that is empty, starts with `.` or ends in `.lock`
(git's ref-name rules; `@{` and the other forbidden characters are outside the allow-list).
`GitTagPattern.IsSafeTagName`
delegates to it. The rule sits here, not in `AgentPackageManagement`, because the client must apply it to what a server
reports and to the tag it is asked to fetch, and `GitIntegration` must not depend on the subsystem above it
(`AgentPackageManagement` already depends on `GitIntegration`) (`-SafeTagNames`).

**InitRepositoryAsync**: `init -q --template=<empty folder> -- <path>`.

**FetchTagAsync**: refuses a tag name that is not a safe tag name with `ArgumentException` before git starts, then
`fetch --depth=1 --no-tags --no-recurse-submodules --no-write-fetch-head -- <url> refs/tags/<t>:refs/tags/<t>` and
`rev-parse --verify --quiet refs/tags/<t>^{commit}`; returns the commit (`-ShallowFetchExactTag`, `-SafeTagNames`).

**ListTreeAsync**: `ls-tree [-r] -z --full-tree -l <commit> -- <paths>`; the commit must be a full object id
(`-AcceptOnlyFullCommitId`) and at least one path must be named, otherwise `ArgumentException` before git starts: an
empty pathspec list would make `ls-tree` enumerate the whole tree (`-ListLayoutTreeRejectsEmptyPaths`); output is capped
at 8 MB (`-ListLayoutTree`).

**ReadBlobsAsync** (replaces a checkout; `-ReadBlobsWithoutCheckout`): a checkout would run whatever
`filter.<name>.smudge` or `.process` driver the fetched repository's `.gitattributes` selects from the user's own
configuration (only the large-file filter is switched off above), and would convert line endings and expand
identifiers, so the bytes on disk would neither be what was validated nor what the commit identifies. The method instead
starts one `git cat-file --batch` (in the hardened path: command-scoped `-c` settings, neutral folder, time limit),
writes the object ids of the entries to its standard input one per line (concurrently with reading, so neither pipe can
fill and block the other) and reads the answers from its standard output: a header `<id> blob <size>` then exactly
`size` bytes then a line feed. `cat-file --batch` reads objects only: no attribute, filter or conversion is applied
(`-ReadBlobsRawBytes`). *Parameters*: the repository, the entries (from `ListTreeAsync`), a callback that opens the
destination stream of an entry just before its bytes are written (the caller owns names and safety; the client disposes
the stream), and the budget in bytes. *Preconditions*: at least one entry; every entry a blob with a full object id and
a
known size; the declared sizes together within the budget, otherwise `ArgumentException` or `OutputTooLarge` before git
starts (`-ReadBlobsBounded`). *Postconditions*: every header is compared with the entry that was asked for (id, type
`blob`, size) and anything else - another object, `missing`, a wrong size, a header that is too long, a missing line
feed, bytes after the last answer, or output that ends early - is a `GitRemoteException` (`-ReadBlobsVerifyAnswers`); a
non-zero exit is classified like any other failure; a time limit or cancellation stops the process like any other
command. Blobs are not written with their mode: the package files are read, never executed, so the executable bit is not
preserved.

**Process rules**: argument list, no shell, no window, standard input closed immediately (except for `cat-file`, which
reads its requests there), working folder = the neutral folder (never a repo, never the target)
(`-NeutralWorkingFolder`),
standard output and error read as bytes from the raw pipe streams and decoded as UTF-8 afterwards; output is read to a
cap
counted in **bytes**, not characters (more is `OutputTooLarge`; `-OutputBoundedInBytes`), error text capped at 64 KB and
used only to classify the failure.

*Time limit and cancellation* (`-TimeoutAndCancel`, `-TimeoutReportedAsTimedOut`,
`-CancellationReportedAsCancellation`, `-UnstoppedProcessReported`): a time limit or cancellation kills the entire
process tree (`Process.Kill(entireProcessTree)`), waits for the pending reads and then for the process itself, each wait
bounded by a stop timeout (10 seconds), and confirms that the process ended. An expired limit is a `TimedOut`
failure and a cancelled token is an `OperationCanceledException`, but when the kill throws and the process is still
there, when it does not end within the stop timeout, or when killing the tree reports that a descendant could not be
killed, the client throws a `GitRemoteException` of kind `ProcessNotStopped` instead - in both cases - so a process
that may still be running is never hidden behind an ordinary timeout or cancellation, and the call is not held longer
than
the stop timeout. The termination and the stop timeout are internal test seams of the options, so a process that cannot
be stopped can be simulated.

*Failure messages* (`-FailureMessagesFixed`): a non-zero exit becomes a `GitRemoteException` whose kind is classified
from git's error text (authentication, host key, dubious ownership, not found, unreachable, other;
`-FailureClassification`) and whose message is one fixed sentence per kind with the operation name and, for `Other`, the
exit code - for example
`git ls-remote did not find the repository or the requested tag, or access to it is not allowed.` -
never any of git's text, because git's error text commonly repeats the remote address, the host or a local path and a
redaction pass cannot know every spelling of them. Logs contain only the operation, mode and outcome, never the address,
the arguments or git's output (`-SecretFreeLogs`). Values that come from outside (tag filter, tag name, commit id) are
checked before they reach a command line (`-RefuseUnsafeCommandLineValues`). A git that cannot be started is
`GitNotFound` with a message that points to Settings (`-MissingGitExplained`).

#### Error Handling

`GitRemoteException` for everything about git (including `GitTooOld`, `OutputTooLarge` and `ProcessNotStopped`);
`ArgumentException` for an unsafe tag name, glob, commit id, an empty path list or an entry that is not a blob (before
git
starts); `OperationCanceledException` only when the caller's token is cancelled and the process was confirmed to have
ended.

#### Dependencies

- **SecretRedactor**, **SecretRedactingLogger** (PackageCredentials) - redaction of error text and logs.
- **.NET BCL** - `Process`, `Regex`.

#### Test Support

`GitRemoteClientIntegrationTests` (this unit's real-git tests) reuse `GitRepositoryFixture`, a test-support class that
lives in the `AgentPackageManagement` test folder because the Git source tests built it first. This is a deliberate
cross-hierarchy dependency of test code only: the fixture builds real local repositories with the user's git
configuration isolated, and nothing in the shipped code depends on it. `GitRemoteStub` (a fake git executable) is the
test support of the unit's stub-based tests and is local to the `GitIntegration` tests.

#### Callers

- **GitSourceProvider** (AgentPackageManagement) - the only caller; it also reaches `IsSafeTagName` through
  `GitTagPattern`.
