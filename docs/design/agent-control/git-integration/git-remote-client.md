### GitRemoteClient

![GitIntegration Structure](GitIntegrationView.svg)

#### Purpose

`GitRemoteClient` runs git against a remote repository whose content and answers are untrusted
(`AgentControl-GitIntegration-RemoteGit`, `AgentControl-System-GitSourceSafety`, `AgentControl-System-GitSignIn`). It
is a separate unit from `GitClient` (which asks questions about a repo the user owns, synchronously, with no hardening)
and from `GitCredentialClient` (which talks to the credential helper): none of their code was changed, and this client
is asynchronous, bounded and hardened for a different threat model.

#### Data Model

**GitTransport** (`Https`, `Ssh`, `File`) and **GitRemoteTarget** (`Url`, `Transport`): the address text and the one
transport allowed for it (the provider derives it from `GitRepositoryAddress`).

**GitFailureKind**: `Other`, `AuthenticationRequired`, `NotFound`, `Unreachable`, `HostKeyUnverified`,
`UnsafeRepository`, `TimedOut`, `GitNotFound`, `OutputTooLarge`. **GitRemoteException**: `InvalidOperationException`
with `Kind` and a redacted message.

**GitRemoteTag** (`Name`, `ObjectSha`, `CommitSha`), **GitTreeEntry** (`Mode`, `Type`, `Sha`, `Size`, `Path`).

**GitRemoteOptions**: git executable resolver, an AgentControl-owned `WorkRoot` (holds the neutral working folder, the
empty hooks folder and the empty template folder, created when needed), `Interactive`, `Logger`, `ExtraConfiguration` and
`ExtraEnvironment` (test seams, applied last), `ListTimeout`/`FetchTimeout` (defaults: 30 s / 3 min in the background,
5 min when interactive, so a credential-manager dialog can be answered).

#### Key Methods

Every command is `git [-C repo] -c ... --literal-pathspecs <command>`. The `-c` list, in order
(`AgentControl-GitRemoteClient-CommandScopedConfiguration`):

```text
protocol.allow=never  protocol.<kind>.allow=always  protocol.ext.allow=never  protocol.fd.allow=never
core.hooksPath=<empty folder>  core.fsmonitor=false  core.untrackedCache=false
[core.symlinks=false: checkout only]  core.autocrlf=false  core.eol=lf  core.protectNTFS=true  core.protectHFS=true
transfer.fsckObjects=true  fetch.fsckObjects=true  fetch.recurseSubmodules=false  submodule.recurse=false
gc.auto=0  maintenance.auto=false  filter.lfs.smudge=  filter.lfs.process=  filter.lfs.required=false
http.followRedirects=initial  http.lowSpeedLimit=1000  http.lowSpeedTime=30
[core.askPass=: background only]  advice.detachedHead=false  <extra configuration>
```

The environment, applied to a copy for the child process only: `GIT_TERMINAL_PROMPT=0`; background work also
`GCM_INTERACTIVE=never`, empty `GIT_ASKPASS`/`SSH_ASKPASS`, `SSH_ASKPASS_REQUIRE=never` (work the user started leaves the
inherited values, so the credential manager and the user's askpass may show their UI); `GIT_ALLOW_PROTOCOL` = the one
transport; `GIT_LFS_SKIP_SMUDGE=1`, `GIT_LITERAL_PATHSPECS=1`, `GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`,
`LC_ALL=LANG=C` (stable English messages for classification), `GIT_CEILING_DIRECTORIES` = the work root; removed: every
`GIT_TRACE*`, `GIT_CURL_VERBOSE`, `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_EXEC_PATH` and similar redirecting
variables (`-NoTraceVariables`). For an ssh remote `GIT_SSH_COMMAND` is the user's own command (from the environment, else
`core.sshCommand` read once) with `-o BatchMode=yes -o ConnectTimeout=15` appended when it is OpenSSH, and is left alone
when it is a wrapper such as `plink` or the user set only `GIT_SSH` (`-SshBatchMode`); an https or local remote gets none.

**ListTagsAsync**: `ls-remote --tags -- <url> <glob>`; the glob must start with `refs/tags/` and use only tag-name
characters and `*`; output lines are parsed, annotated tags resolved through their `^{}` line
(`-ListTagsWithoutClone`, `-PeeledTags`).

**InitRepositoryAsync**: `init -q --template=<empty folder> -- <path>`.

**FetchTagAsync**: validates the tag name again (letters, digits, `. _ / + -`, no `..`, not starting with `-`), then
`fetch --depth=1 --no-tags --no-recurse-submodules --no-write-fetch-head -- <url> refs/tags/<t>:refs/tags/<t>` and
`rev-parse --verify --quiet refs/tags/<t>^{commit}`; returns the commit (`-ShallowFetchExactTag`).

**ListTreeAsync / CheckoutPathsAsync**: `ls-tree [-r] -z --full-tree -l <commit> -- <paths>` and
`checkout <commit> -- <paths>`; the commit must be a full object id; output is capped at 8 MB
(`-ListLayoutTree`).

**Process rules**: argument list, no shell, no window, standard input closed immediately, working folder = the neutral
folder (never a repo, never the target), output read to a cap (more is `OutputTooLarge`), error text capped at 64 KB;
a time limit or cancellation kills the entire process tree and reaps it (`-TimeoutAndCancel`). A non-zero exit becomes a
`GitRemoteException` whose kind is classified from the redacted error text (authentication, host key, dubious ownership,
not found, unreachable, other) and whose message is a three-line, 300-character, redacted summary - never raw output;
logs contain only the operation, mode and outcome, never the address or arguments (`-OutputBoundedAndRedacted`,
`-SecretFreeArguments`, `-FailureClassification`). A git that cannot be started is `GitNotFound` with a message that points
to Settings.

#### Error Handling

`GitRemoteException` for everything about git; `ArgumentException` for an unsafe tag name, glob, commit id or empty path
list (before git starts); `OperationCanceledException` only when the caller's token is cancelled.

#### Dependencies

- **SecretRedactor**, **SecretRedactingLogger** (PackageCredentials) - redaction of error text and logs.
- **.NET BCL** - `Process`, `Regex`.

#### Callers

- **GitSourceProvider** (AgentPackageManagement) - the only caller.
