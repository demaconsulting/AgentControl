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
`UnsafeRepository`, `TimedOut`, `GitNotFound`, `OutputTooLarge`, `GitTooOld`. **GitRemoteException**:
`InvalidOperationException` with `Kind` and a redacted message.

**GitRemoteTag** (`Name`, `ObjectSha`, `CommitSha`), **GitTreeEntry** (`Mode`, `Type`, `Sha`, `Size`, `Path`).

**GitRemoteOptions**: git executable resolver, an AgentControl-owned `WorkRoot` (holds the neutral working folder, the
empty hooks folder and the empty template folder, created when needed), `Interactive`, `Logger`, `ExtraConfiguration` and
`ExtraEnvironment` (test seams, applied last), `ListTimeout`/`FetchTimeout` (defaults: 30 s / 3 min in the background,
5 min when interactive, so a credential-manager dialog can be answered).

#### Key Methods

Every command is `git [-C repo] -c ... --literal-pathspecs <command>`. The requirements of this unit state the
observable outcomes (never a hook, only the transport of the address, no prompt in the background, nothing
leaked); this section is the mechanism that achieves them. The `-c` list, in order
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

Which settings serve which requirement:

- **No hook** (`-NoHooks`): `core.hooksPath` is an empty AgentControl-owned folder, and `init` is given
  `--template=<empty folder>`, so the user's template directory (`init.templateDir`, `GIT_TEMPLATE_DIR`) and a global
  hooks path
  cannot put a hook into the new repository and a repository cannot ship one into it.
- **Only the transport of the address** (`-TransportAllowList`): `protocol.allow=never`, then
  `protocol.<kind>.allow=always` for the one kind of the address, `protocol.ext.allow=never`, `protocol.fd.allow=never`,
  and
  `GIT_ALLOW_PROTOCOL` = the one kind, so git itself refuses a local path given as https or an `ext::` helper.
- **No submodules, no large-file filters** (`-NoSubmodulesNoLfs`): `fetch.recurseSubmodules=false`,
  `submodule.recurse=false`, `--no-recurse-submodules` on the fetch, `filter.lfs.smudge=`, `filter.lfs.process=`,
  `filter.lfs.required=false` and `GIT_LFS_SKIP_SMUDGE=1`.
- **No prompt in the background** (`-NoPromptInBackground`) and **sign-in UI when the user started the work**
  (`-InteractiveWhenUserStarted`): the environment below, plus `core.askPass=` for background work only.
- **No leaking** (`-SecretFreeArguments`): the arguments and the address are never logged; see the process rules.

The remaining settings (`core.fsmonitor=false`, `core.untrackedCache=false`, `core.autocrlf=false`, `core.eol=lf`,
`core.protectNTFS=true`, `core.protectHFS=true`, `transfer.fsckObjects=true`, `fetch.fsckObjects=true`, `gc.auto=0`,
`maintenance.auto=false`, `http.followRedirects=initial`, `http.lowSpeedLimit`/`http.lowSpeedTime`, `core.symlinks=false`
on a
checkout) keep a hostile repository from running a monitor or maintenance job, converting content, being accepted with
malformed objects or hanging a transfer, and keep symbolic links from being created.
The environment, applied to a copy for the child process only: `GIT_TERMINAL_PROMPT=0`; background work also
`GCM_INTERACTIVE=never`, empty `GIT_ASKPASS`/`SSH_ASKPASS`, `SSH_ASKPASS_REQUIRE=never` (work the user started leaves the
inherited values, so the credential manager and the user's askpass may show their UI); `GIT_ALLOW_PROTOCOL` = the one
transport; `GIT_LFS_SKIP_SMUDGE=1`, `GIT_LITERAL_PATHSPECS=1`, `GIT_OPTIONAL_LOCKS=0`, `GIT_NO_LAZY_FETCH=1`,
`LC_ALL=LANG=C` (stable English messages for classification), `GIT_CEILING_DIRECTORIES` = the work root; removed: every
`GIT_TRACE*`, `GIT_CURL_VERBOSE`, `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_EXEC_PATH` and similar redirecting
variables (`-NoTraceVariables`). For an ssh remote `GIT_SSH_COMMAND` is the user's own command (from the environment, else
`core.sshCommand` read once) with `-o BatchMode=yes -o ConnectTimeout=15` appended when it is OpenSSH, and is left alone
when it is a wrapper such as `plink` or the user set only `GIT_SSH` (`-SshBatchMode`); an https or local remote gets none.

**EnsureSupportedVersionAsync / ParseVersion**: Git 2.29 (`MinimumGitVersion`) is the oldest git that has
`fetch --no-write-fetch-head`, which the single-tag fetch uses. The method runs `git --version` through the same
hardened path as every command (`git -c ... --literal-pathspecs --version`, in the neutral folder, 30-second limit, 4 KB
output cap), reads the version with `ParseVersion` (the text after `git version`, for example `2.51.0.windows.2`,
`2.39.5 (Apple Git-154)` or `2.29`; anything else is `null`) and throws a `GitRemoteException` of kind `GitTooOld` whose
message names the minimum and the version in use and says to update Git or choose a newer one under
Settings > Git executable. A text that cannot be read is logged as a warning and the git is treated as supported. A
supported (or unreadable) result is remembered in a process-wide dictionary keyed by the resolved executable text
(case-insensitive on Windows), so `git --version` runs at most once per executable; a too-old result and a failure to run
git
are not remembered, so an updated git is accepted by the next check (`-MinimumGitVersion`). The caller decides when to ask
(`GitSourceProvider` does, when its options say so).

**ListTagsAsync**: `ls-remote --tags -- <url> <glob>`; the glob must start with `refs/tags/` and use only tag-name
characters and `*`; output lines are parsed, annotated tags resolved through their `^{}` line, and every tag whose
name is
not a safe tag name (below) is dropped without an error (`-ListTagsWithoutClone`, `-PeeledTags`, `-SafeTagNames`).

**IsSafeTagName**: the one implementation of the tag-name rule: only `A-Z a-z 0-9 . _ / + -`, at most 255 characters, no
`..`, not starting with `-`, not ending in `.`, and no path component that is empty, starts with `.` or ends in `.lock`
(git's ref-name rules; `@{` and the other forbidden characters are outside the allow-list). `GitTagPattern.IsSafeTagName`
delegates to it. The rule sits here, not in `AgentPackageManagement`, because the client must apply it to what a server
reports and to the tag it is asked to fetch, and `GitIntegration` must not depend on the subsystem above it
(`AgentPackageManagement` already depends on `GitIntegration`) (`-SafeTagNames`).

**InitRepositoryAsync**: `init -q --template=<empty folder> -- <path>`.

**FetchTagAsync**: refuses a tag name that is not a safe tag name with `ArgumentException` before git starts, then
`fetch --depth=1 --no-tags --no-recurse-submodules --no-write-fetch-head -- <url> refs/tags/<t>:refs/tags/<t>` and
`rev-parse --verify --quiet refs/tags/<t>^{commit}`; returns the commit (`-ShallowFetchExactTag`, `-SafeTagNames`).

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

`GitRemoteException` for everything about git (including `GitTooOld`); `ArgumentException` for an unsafe tag name, glob,
commit id or empty path list (before git starts); `OperationCanceledException` only when the caller's token is cancelled.

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
