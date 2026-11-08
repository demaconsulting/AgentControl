### GitCredentialClient

#### Verification Approach

`GitCredentialClient` is verified at two levels.

**Stub git (`GitCredentialClientTests.cs`).** A fake git executable, `GitCredentialStub` (a `.bat` file on Windows
and a POSIX shell script elsewhere), stands in for git. It records, for every invocation, the arguments, the
environment variables that matter, the working directory and everything written to its standard input, and answers
`config --get-urlmatch` and `credential fill|approve|reject` with canned output and exit codes; it can also hang for
a configured time and then append a marker to its log, so a test can observe whether the process was killed (a
killed process never writes the marker). The stub tests therefore check exactly what the unit sends - arguments,
environment, request text, working folder - and how it reacts to every kind of answer, without any real credential
store, and run on every operating system. Logging is observed through `ListLogger`, a logger that records the text it
receives, and the tests use a sentinel password so any appearance of the secret in a log line is detected. Tests that
launch the stub belong to the `RealProcess` xUnit collection (disabling parallelization), as for `GitClient`.

**Real git (`GitCredentialClientIntegrationTests.cs`).** The tests run the real git executable and the real
`git credential` protocol, with the credential helper replaced - through the unit's optional extra-configuration
argument - by a throw-away `store --file=...` helper in a temporary folder, so a test can never read or write the
user's actual credential store (Git Credential Manager, a keychain, libsecret). They prove the protocol assumptions
the stub cannot: that a miss exits without ever prompting, that an approved credential is found again, that a
rejected credential is gone, that rejecting without a credential removes only that origin's entry, and that with no
helper configured the unit reports "unavailable". They are skipped only when git is not installed (the project's CI
platforms all have it).

#### Test Environment

The real-git tests need a git executable on `PATH` and a writable temporary folder; they set no machine or user
configuration. The stub tests need only a temporary folder and the ability to run a script (a `.bat` on Windows, a
shell script elsewhere). The tests pass an explicit working folder, so they never depend on the user's profile.

#### Acceptance Criteria

- All stub and real-git tests pass with zero failures (the real-git tests may be skipped only where git is missing).
- A lookup, a save and a removal send exactly the feed's protocol and host (with the port only when it is not the
  default) and, for save and remove, the credential, ended by the blank line that ends a request; the arguments carry
  no secret.
- Every invocation passes the empty askpass setting, fixes the generic provider for the feed's origin only (never for
  the availability question), sets the never-prompt environment, closes standard input after the request and runs in
  the given working folder.
- A lookup that finds nothing or an incomplete credential reports no credential; a helper that is configured, absent,
  reset or failing is reported as available or not accordingly; a missing git and a failing git are results, not
  exceptions.
- A hanging git is killed, with its process tree, at the time limit and reported as "nothing"; cancelling kills it too
  and propagates the cancellation.
- No request line, response line, credential or credential-bearing command-line argument appears in the log, and a
  credential (or origin) with a line feed, carriage
  return or NUL is refused without running git, while a password containing `=` is sent and read back unchanged.
- With a real git and an isolated helper the full miss, approve, fill, reject life cycle behaves as specified.

#### Test Scenarios

**GitCredentialClient_FillApproveReject_DescribeTheFeedAndHandleEveryAnswer**: A lookup sends
`protocol=https`, `host=feeds.example.com:8443` and the blank line, with the origin-scoped generic provider, and
returns the credential git printed (an `=` inside the password kept); an address on the default port is described
without a port; no output with a non-zero exit, a user name without a password, a password without a user name and
empty output all yield `null`; approve and reject send the credential (reject also without one) with the matching
git command; the response parser accepts only complete responses. With real git and an isolated helper a miss yields
nothing, approve then fill returns the credential, reject then fill yields nothing, and rejecting without a
credential removes only that origin. This scenario is tested by
`GitCredentialClient_Fill_CredentialStored_ReturnsItWithTheDocumentedRequestAndEnvironment`,
`GitCredentialClient_Fill_DefaultPort_SendsHostWithoutPort`,
`GitCredentialClient_Fill_NothingOrIncomplete_ReturnsNull`,
`GitCredentialClient_Approve_Credential_SendsItWithApprove`,
`GitCredentialClient_Reject_WithAndWithoutCredential_SendsItWithReject`,
`GitCredentialClient_ParseCredential_VariousResponses_ParsesOnlyCompleteOnes`,
`GitCredentialClient_RealGitWithIsolatedStore_MissApproveFillRejectLifeCycle` and
`GitCredentialClient_RealGit_RejectWithoutCredential_RemovesOnlyThatOrigin`, covering
`AgentControl-GitCredentialClient-FillApproveReject`.

**GitCredentialClient_HelperAvailability_FollowsGitsOwnMatching**: The availability question runs
`git config --get-urlmatch credential.helper https://feeds.example.com:8443/` (without the provider override) and is
`true` for a printed helper; no helper output, a non-zero exit and a missing git are `false`; with real git and no
helper configured the unit reports unavailable and finds nothing. This scenario is tested by
`GitCredentialClient_IsAvailable_HelperConfigured_ReturnsTrueAndAsksAboutTheOrigin`,
`GitCredentialClient_IsAvailable_NoHelperOrGitFails_ReturnsFalse` and
`GitCredentialClient_RealGit_NoCredentialHelper_IsUnavailableAndFindsNothing`, covering
`AgentControl-GitCredentialClient-HelperAvailability`.

**GitCredentialClient_NeverInteractive_NothingCanPromptAndGitRunsAwayFromRepos**: The stub records
`-c core.askPass= -c credential.https://feeds.example.com:8443.provider=generic credential fill`,
`GIT_TERMINAL_PROMPT=0`, `GCM_INTERACTIVE=never`, empty `GIT_ASKPASS` and `SSH_ASKPASS`, the request on standard
input (so input was supplied and closed) and the working folder given to the client; the default-port origin gets its
own scoped provider setting. This scenario is tested by
`GitCredentialClient_Fill_CredentialStored_ReturnsItWithTheDocumentedRequestAndEnvironment`,
`GitCredentialClient_Fill_DefaultPort_SendsHostWithoutPort` and
`GitCredentialClient_Approve_Credential_SendsItWithApprove`, covering
`AgentControl-GitCredentialClient-NeverInteractive`. The default working folder (the user's profile) is a
constructor default that the tests deliberately override, so that no test depends on the machine's profile; the
override mechanism is itself what the test asserts.

**GitCredentialClient_BoundedTime_HangingGitIsKilled**: A stub that hangs for far longer than a short time limit is
killed at the limit - its log never receives the finished marker - and the lookup returns `null`; a stub that hangs
while the caller cancels is killed the same way and the call throws `OperationCanceledException`. This scenario is
tested by `GitCredentialClient_Fill_ProcessHangs_IsKilledAtTheTimeLimitAndReturnsNull` and
`GitCredentialClient_Fill_CancelledWhileGitHangs_KillsTheProcessAndThrows`, covering
`AgentControl-GitCredentialClient-BoundedTime`.

**GitCredentialClient_UnavailableIsNotAnError_ReportsNothingWithoutThrowing**: A git executable that does not exist
yields unavailable, no credential and `false` for approve and reject without any exception; a stub whose approve and
reject exit non-zero yields `false`; the resolver constructor evaluates the executable on every call (a changed path
applies to the next call); a blank path, a null resolver and a non-positive time limit are refused with the
documented exceptions; extra configuration passes through with `-c`. This scenario is tested by
`GitCredentialClient_GitExecutableMissing_ReportsNothingWithoutThrowing`,
`GitCredentialClient_ApproveAndReject_GitFails_ReturnFalse`,
`GitCredentialClient_ResolverConstructor_ResolvesTheExecutableOnEveryCall`,
`GitCredentialClient_Constructors_InvalidArguments_Throw` and
`GitCredentialClient_ExtraConfiguration_IsPassedToGitWithDashC`, covering
`AgentControl-GitCredentialClient-UnavailableIsNotAnError`.

**GitCredentialClient_SecretsNeverLogged_ContentNeverReachesTheLog**: Every operation is run with a sentinel
password in the request and the response and a recording logger; no logged line contains the password, the user name,
any line of the request (the protocol, host, path, user name and password lines), any line of the helper's response
(including its non-secret lines such as the protocol and host it echoes), or any command-line argument that bears a
credential. A credential whose user name or password contains a character the line protocol
cannot carry - a line feed, a carriage return or NUL - is refused for approve and reject: no git process is started
and the result is `false`; the request builder yields no request for such a value. A password containing `=`,
including the `=` and `==` padding of a Base64 token, is not refused: it is sent unchanged (checked against the stub's
recorded input) and, against the real git, approve followed by fill returns the identical password. This scenario is
tested by `GitCredentialClient_AllOperations_NeverLogRequestOrResponseContent`,
`GitCredentialClient_ApproveAndReject_ValueWithLineFeedCarriageReturnOrNul_AreRefusedWithoutRunningGit`,
`GitCredentialClient_Approve_PasswordWithEquals_IsSentUnchanged`,
`GitCredentialClient_BuildRequestAndParse_EqualsRoundTripsAndLineBreaksAreNull`,
`GitCredentialClient_IsSafeValue_OnlyLineFeedCarriageReturnAndNul_AreRejected` and
`GitCredentialClient_RealGit_PasswordWithEquals_RoundTripsIdentically`, covering
`AgentControl-GitCredentialClient-SecretsNeverLogged`.
