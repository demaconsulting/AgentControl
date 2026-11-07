### GitCredentialClient

![GitIntegration Structure](GitIntegrationView.svg)

#### Purpose

`GitCredentialClient` keeps, finds and removes a package feed's sign-in in the user's own Git credential
helper by running the user's git executable (`git credential fill`, `approve` and `reject`), so that
AgentControl itself never stores a secret. It is the only implementation of the `IRememberedCredentialStore`
interface of the `PackageCredentials` subsystem and satisfies `AgentControl-GitCredentialClient-FillApproveReject`,
`AgentControl-GitCredentialClient-HelperAvailability`, `AgentControl-GitCredentialClient-NeverInteractive`,
`AgentControl-GitCredentialClient-BoundedTime`, `AgentControl-GitCredentialClient-UnavailableIsNotAnError` and
`AgentControl-GitCredentialClient-SecretsNeverLogged`.

**Why Git's credential helper.** Every developer machine that has Git already has a credential helper that the
platform's installer configured (Git Credential Manager on Windows and macOS, a keychain, libsecret or the
file-based store elsewhere), and that helper keeps secrets in the operating system's protected store. Asking it
to keep a feed's sign-in therefore needs no storage code, and no secret, in AgentControl, and the user can see and
remove the entry with the operating system's own tools. The feed is described to the helper by its origin
(`protocol=https`, `host=host[:port]`), the same way a Git remote is, so the helper's own matching rules apply.

**Dependency direction.** The unit lives in `GitIntegration` but implements an interface that belongs to
`PackageCredentials`. The dependency therefore runs `GitIntegration` to `PackageCredentials` (the interface,
`FeedOrigin` and `SecretRedactor`) and never the other way: `PackageCredentials` knows only that a store exists,
not that it is Git, and `GitIntegration` knows nothing of how credentials are used. `App` wires the two together.

#### Data Model

**\_resolveGitExecutablePath**: `Func<string>` — returns the git executable path or command name, evaluated on
every call, so a changed Git setting applies at once. The paths follow the same rules as `GitClient`: the
configured path, the `--git-path` override, or `git` from `PATH`. A constructor taking a fixed path wraps it.

**\_logger**: `ILogger` — receives only the operation name, the feed origin, the exit code and the elapsed
time; never a request, a response or a credential.

**\_timeout**: `TimeSpan` — the longest one invocation may take; `DefaultTimeout` is ten seconds. A lookup that
finds a stored credential takes under a second, but a miss can take a few seconds and a stuck helper could take
forever.

**\_extraConfiguration**: `IReadOnlyList<string>` — extra `name=value` pairs passed with `-c` to every
invocation. Only tests use it, to replace the credential helper with a throw-away one so the user's real
credential store is never touched.

**\_workingDirectory**: `string` — the folder git runs in: the user's profile folder (the temporary folder when
there is none), or a folder supplied by a test.

The unit keeps no state between calls and is thread-safe.

#### Key Methods

**IsAvailableAsync(origin)**: Runs `git config --get-urlmatch credential.helper <origin>/` and reports `true`
when it exits successfully and prints a helper. `--get-urlmatch` is used instead of reading
`credential.helper` because it applies the same matching Git itself applies to a URL — configuration scoped to
the host (`credential.https://host.helper`) and a helper list that was reset with an empty entry — so the
answer is "would Git use a helper for this feed", not "is some helper mentioned somewhere". No helper, a reset
list, a missing git and a failing git all yield `false`
(`AgentControl-GitCredentialClient-HelperAvailability`).

**FillAsync(origin)**: Runs `git credential fill` with the request on standard input and parses the response.
Git exits non-zero when it finds nothing (it may not prompt), and a response without both a user name and a
password is incomplete; both yield `null`. The value after the first `=` of a line is taken whole, so a password
containing `=` survives (`AgentControl-GitCredentialClient-FillApproveReject`).

**ApproveAsync(origin, credential)** and **RejectAsync(origin, credential?)**: Run `git credential approve` and
`git credential reject` with the credential in the request, and report whether git exited successfully. Reject
without a credential removes whatever the helper stores for the origin. A credential whose user name or secret (or
an origin whose scheme or host) contains a line feed, carriage return or NUL is refused without running git: the
protocol is lines of `key=value` with no escape mechanism, so a line feed could inject further keys, a NUL ends the
request, and a carriage return is commonly trimmed or mishandled by helpers; such a value is therefore never altered
or sent (`AgentControl-GitCredentialClient-SecretsNeverLogged`). Keys are fixed by this unit and never come from user
input. A value may contain `=` (git splits each line at the first one, and Base64 padding is common in tokens), and
any other character, so such a password is sent - and read back - unchanged.

**BuildRequest(origin, credential?)**: Builds the `key=value` lines, ended by the blank line that ends a request,
or returns `null` when any value fails **IsSafeValue** - a malformed request is never produced.

**IsSafeValue(value)**: `true` when the value contains none of a line feed, a carriage return or NUL.

**ParseCredential(output)**: Parses a response into a `NuGetCredential`, or `null` when incomplete.

**BuildArguments / CreateStartInfo** (private): Every invocation passes `-c core.askPass=` and, for the
credential operations, `-c credential.<origin>.provider=generic`, then any extra configuration, then the command.
No argument ever carries a secret. The environment sets `GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=never` and
empty `GIT_ASKPASS` and `SSH_ASKPASS`; standard input is written and closed after the request, so nothing can
wait for input (`AgentControl-GitCredentialClient-NeverInteractive`).

**RunAsync** (private): Starts the process, reads standard output, drains and discards standard error (a helper
may echo what it was given), writes the request, waits for exit, all bounded by a token that combines the
caller's token with the time limit. On expiry or cancellation the whole process tree is killed
(`Kill(entireProcessTree: true)`) and the pipes are allowed to close. A timeout reports "nothing" (`NotRun`); a
cancellation by the caller propagates as `OperationCanceledException`
(`AgentControl-GitCredentialClient-BoundedTime`).

**Design decisions and their rationale.**

- *The generic provider override is scoped to the origin and passed with `-c`.* Git Credential Manager would
  otherwise probe the host over the network to detect which hosting provider it is (which tells the server about
  the lookup, and is slow or failing for unreachable hosts). Fixing the provider to `generic` for the feed's own
  origin only, on the command line, means the user's configuration is never changed and no other host is
  affected.
- *Never interactive.* The application's own sign-in dialog is the only user interface, so a prompt from Git, an
  askpass program or the helper would be a hidden, unexplained window or a hang. The environment variables, the
  empty `core.askPass` and the closed standard input remove every route to one.
- *Bounded, with the whole tree killed.* A helper may spawn children (a credential UI, a broker); killing only the
  git process would leave them running and the pipes open.
- *Neutral working folder.* Git reads repository configuration from the current directory; running from the
  user's profile means a repo the application happens to be inside cannot add a credential helper that would
  receive a feed's credential.
- *Secrets in memory only, while handled.* A credential passed to `approve` or `reject`, and a credential returned
  by `fill`, is registered with `SecretRedactor` for as long as this unit handles it (a lease), and the unit logs
  only through the redacting logger, so an unforeseen path cannot put it into a log line; the request and the
  response are never logged at all, and a credential returned by `fill` is registered for as long as the session
  store that takes it over keeps it.

#### Error Handling

A git executable that cannot be started, a failing git, a timeout and a helper that refuses are all *results*, not
exceptions: `IsAvailableAsync` yields `false`, `FillAsync` yields `null`, `ApproveAsync` and `RejectAsync` yield
`false`, and a warning without any secret is logged. A cancelled token surfaces as `OperationCanceledException`
(after the process tree has been killed). Invalid constructor arguments throw `ArgumentNullException`,
`ArgumentException` (blank fixed path) or `ArgumentOutOfRangeException` (non-positive time limit)
(`AgentControl-GitCredentialClient-UnavailableIsNotAnError`).

#### Dependencies

- **IRememberedCredentialStore**, **FeedOrigin**, **SecretRedactor** (`PackageCredentials` subsystem) — the
  interface implemented, the origin described to git, and the redaction leases.
- **AppLogging** (`Logging` subsystem) — the fallback logger factory.
- **NuGetCaching** (OTS) — the `NuGetCredential` type.
- **.NET BCL** — `System.Diagnostics.Process`.
- **The user's git executable and credential helper** — an external program, not a software item of this system.

#### Callers

- **App** — creates one instance (resolving the git executable through the same settings and `--git-path`
  override as `GitClient`) and passes it in the `PackageCredentialContext`.
- **PackageCredentialCallback** (`PackageCredentials` subsystem) — looks up, saves and removes a sign-in through
  the interface, and asks whether a helper is available before offering "Remember on this computer".
- **PackageCredentialContext** (`PackageCredentials` subsystem) — forgets a saved sign-in on request.
