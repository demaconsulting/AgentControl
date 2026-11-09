### GitRemoteClient

#### Verification Approach

`GitRemoteClient` is verified two ways. `GitRemoteClientTests.cs` runs the client against **`GitRemoteStub`**,
a fake git
executable (a batch file on Windows, a shell script elsewhere) that records every invocation's arguments,
the environment
variables that matter and the working folder, and answers each git command with canned output, error text, exit code and
an optional sleep; the tests therefore assert the *exact* command line and environment of each operation, the prompt
rules of background and interactive work, the time limit and cancellation (the sleeping stub never reaches its
`FINISHED` marker when it is killed), the failure to stop a process (a test seam replaces the termination, so a process
that cannot be stopped can be simulated and is killed by the test afterwards), the output cap in bytes, the fixed
failure messages and the classification of failures.
`GitRemoteClientIntegrationTests.cs` and the subsystem test run the **real git** against local bare repositories (the
fixture, `GitRepositoryFixture` of the `AgentPackageManagement` tests, isolates the user's git configuration; see Test
Environment) to prove the semantics the stubs assume: peeled tags, a shallow fetch of one tag, the tree listing, the
raw bytes of a blob under conversion settings, a repository created without hooks, git's own refusal of a transport
that is not allowed (including an `ext::` helper forced through the internal API), and that trace variables in the
parent produce no trace text.

#### Test Environment

The stub tests need nothing but the operating system's shell; they belong to the `RealProcess` collection because they
launch processes and some set variables in the test process (restored afterwards). The real-git tests need git on `PATH`
and are skipped only when it is not installed; no network, credential or user configuration is used. They build their
repositories with `GitRepositoryFixture`, which belongs to the `AgentPackageManagement` test folder
(the Git source tests
created it first): a deliberate, test-only cross-hierarchy dependency,
so a change to the fixture must be checked against
both `GitRemoteClientIntegrationTests` and `GitSourceProviderIntegrationTests`. The batch stub
drops a `^` from its recorded argument text, so tests assert the commands and options rather than `^{commit}`. The
version of git is remembered per executable for the whole test process, so every stub-based version test uses its own
stub (a unique executable path) and the real-git version test uses the full path of git rather than `git`.

#### Acceptance Criteria

- The `ls-remote` command line is exactly the documented list of command-scoped settings with the address and glob after
  `--`; each transport allows only its own protocol in the configuration and in `GIT_ALLOW_PROTOCOL`.
- A tag name that is not a legal ref name (empty, dot-leading or `.lock`-ending component, `//`,
  leading or trailing `/`,
  `..`, trailing `.`, a forbidden character, too long) is dropped from a server listing and refused for a fetch
  before git
  runs; legal names, including nested ones, are fetched.
- A tree listing with no path, an object id that is not a full id and blob entries that are not well-formed are refused
  before git runs.
- Blobs are read with `cat-file --batch` and never checked out: their bytes arrive exactly as committed (carriage
  returns, NUL, a byte-order mark, no final line feed) under `autocrlf`, `eol=crlf` and identifier attributes; an answer
  that is not the blob asked for, or git's output ending early or running on, is refused; sizes over the budget are
  refused before git runs.
- A git older than 2.29 is refused as `GitTooOld` with a message that names the minimum, the version in use and the way
  to get a newer one; a newer one passes and `git --version` runs once per executable, also when eight callers ask at
  once; an unreadable version does not block.
- Background work cannot prompt (terminal, credential manager, askpass, ssh), even when the user's askpass variables are
  set; user-started work leaves them; trace and redirection variables never reach git; git runs in a neutral folder.
- ssh gets batch mode and a connect timeout around the user's own OpenSSH command and is left alone for other commands.
- A hanging command is killed at its limit or on cancellation and the termination is confirmed; a process that cannot be
  confirmed to have ended is reported as `ProcessNotStopped`, within the stop timeout, in both cases;
  output over the cap
  is refused, counted in bytes (a multi-byte text over the cap is refused, exactly the cap is accepted); a failure
  message is one fixed sentence that repeats neither the address, the host nor a path; logs hold no address or argument;
  failures are classified; a missing git is explained.
- Real git lists, fetches one tag shallow, lists the tree, reads a blob's raw bytes, creates a repository without hooks
  and refuses other transports.

#### Test Scenarios

**GitRemoteClient_Commands_UseExactHardenedArgumentsAndAddressesAfterDashDash**: the full argument list of
`ls-remote`; `init` from an empty template; the shallow single-tag `fetch` with `--no-tags`,
`--no-recurse-submodules`, `--no-write-fetch-head` and the refspec; `rev-parse`; `ls-tree -r -z --full-tree -l`; globs
and commit ids that could be read as options are refused before git runs; a tag listing keeps only well-formed lines
and resolves annotated tags. The exact lists are the design document's mechanism, asserted here so that no setting can
be added or dropped unnoticed; the requirements they serve state only the outcome. This scenario is tested by
`GitRemoteClient_ListTags_Https_UsesExactCommandScopedConfigurationAndDashDash`,
`GitRemoteClient_ListTags_ParsesLightweightAndAnnotatedTags`,
`GitRemoteClient_ListTags_UnsafeGlob_IsRefusedWithoutRunningGit`,
`GitRemoteClient_InitAndFetchTag_UseEmptyTemplateAndShallowSingleTagFetch`,
`GitRemoteClient_FetchTag_RevParseWithoutCommit_Throws`,
`GitRemoteClient_FetchTag_UnsafeTagName_IsRefusedWithoutRunningGit`,
`GitRemoteClient_ListTree_UsesLiteralPathsAndParsesEntries` and
`GitRemoteClient_ListTree_EmptyPathList_IsRefusedWithoutRunningGit`, covering
`AgentControl-GitRemoteClient-ListTagsWithoutClone`, `AgentControl-GitRemoteClient-ListTagsLiteralArguments`,
`AgentControl-GitRemoteClient-ListTagsKeepsWellFormedTags`, `AgentControl-GitRemoteClient-PeeledTags`,
`AgentControl-GitRemoteClient-ShallowFetchExactTag`, `AgentControl-GitRemoteClient-FetchIntoHooklessRepository`,
`AgentControl-GitRemoteClient-ReportFetchedCommit`, `AgentControl-GitRemoteClient-ListLayoutTree`,
`AgentControl-GitRemoteClient-ListLayoutTreeRejectsEmptyPaths`, `AgentControl-GitRemoteClient-AcceptOnlyFullCommitId`,
`AgentControl-GitRemoteClient-RefuseUnsafeCommandLineValues`, `AgentControl-GitRemoteClient-NoHooks`,
`AgentControl-GitRemoteClient-NoSubmodules` and `AgentControl-GitRemoteClient-CommandScopedConfiguration` (with
`GitRemoteClient_ExtraConfigurationAndEnvironment_AreAppliedLast`, a defensive test of the test seam that no requirement
cites).

**GitRemoteClient_TagNames_OnlyLegalRefNamesReachGit**: a listing in which a server reports `a//b`, `.hidden`, `x.lock`,
`a/.b`, `a/b.lock/c`, `trailing/`, `/leading`, `a..b`, `ends.`, `-dash`, a name with a space or colon, `a@{b}` and a
256-character name (each with a peeled line) yields only the legal names around them and never throws; a fetch of the
same hostile names (and of option-like names such as `-oProxyCommand=calc`, `--upload-pack=calc`, `v1$(calc)`)
is refused
with `ArgumentException` before git is started; legal names (`v1.0.0`, `release/1.2.3-beta.1`, `pkg+build_1/v1.0.0`) are
fetched. The rule is `GitRemoteClient.IsSafeTagName`,
the same implementation `GitTagPattern.IsSafeTagName` delegates to.
This scenario is tested by `GitRemoteClient_ListTags_HostileServerTagNames_AreDropped`,
`GitRemoteClient_FetchTag_UnsafeTagName_IsRefusedWithoutRunningGit` and
`GitRemoteClient_FetchTag_LegalTagName_IsFetched`, covering `AgentControl-GitRemoteClient-SafeTagNames`,
`AgentControl-GitRemoteClient-IgnoreHostileServerTagNames` and
`AgentControl-GitRemoteClient-RefuseUnsafeTagNameBeforeGit`.

**GitRemoteClient_ReadBlobs_RawBytesWithoutCheckout**: with the stub, `ReadBlobsAsync` runs exactly
`... --literal-pathspecs cat-file --batch` (no `checkout`), delivers each answer's bytes to the destination opened for
its entry (including an empty blob), and refuses before git starts an empty entry list, an entry that is a tree, an
object id that is an option, an entry without a size and sizes that together exceed the budget by one byte; an answer
with another object id, `missing`, another type, another size, a header that is not a number, bytes after the last
answer, a following byte that is not a line feed or data that ends early is a `GitRemoteException` that names no object;
a failing `cat-file` is classified with the fixed message `git cat-file failed (exit code 128).`;
a `cat-file` that sleeps
is stopped at the limit and reported as timed out without finishing. With the real git,
a blob committed with CRLF and LF
line endings, a lone carriage return, a NUL byte, a byte-order mark and an identifier keyword, in a repository whose
attributes select `text eol=crlf ident`, is delivered byte for byte equal to the content (and to the size `ls-tree`
reported) although the user's configuration sets `core.autocrlf=true` and `core.eol=crlf`. This scenario is tested by
`GitRemoteClient_ReadBlobs_UsesCatFileBatchAndDeliversRawBytes`,
`GitRemoteClient_ReadBlobs_InvalidEntriesOrOverBudget_AreRefusedWithoutRunningGit`,
`GitRemoteClient_ReadBlobs_AnswerThatIsNotTheRequestedBlob_IsRefused`,
`GitRemoteClient_ReadBlobs_GitFails_IsClassifiedWithFixedMessage`,
`GitRemoteClient_ReadBlobs_ExceedsTimeLimit_KillsProcessAndReportsTimedOut` and
`GitRemoteClient_RealGit_ReadBlobs_DeliversExactBlobBytesDespiteConversionSettings`, covering
`AgentControl-GitRemoteClient-ReadBlobsWithoutCheckout`, `AgentControl-GitRemoteClient-ReadBlobsRawBytes`,
`AgentControl-GitRemoteClient-ReadBlobsVerifyAnswers`, `AgentControl-GitRemoteClient-ReadBlobsBounded` and
`AgentControl-GitRemoteClient-NoContentFilters` (with
`GitRemoteClient_Environment_RemovesTraceAndRedirectionVariablesAndSetsFixedOnes`, which asserts
`GIT_LFS_SKIP_SMUDGE=1`). That a filter driver selected by a repository's attributes and defined in the user's
configuration does not run is proven where the files are written, by
`GitSourceProvider_Acquire_HostileFilterSelectedByRepository_NeverRunsAndContentIsRaw`.

**GitRemoteClient_MinimumGitVersion_OldGitRefusedNewGitCheckedOnce**: `ParseVersion` reads
`git version 2.51.0.windows.2`, `git version 2.39.5 (Apple Git-154)`, `git version 2.29` and CRLF-terminated output and
returns nothing for text that is not a git version. With a stub that answers `git version 2.20.1`, the check throws a
`GitRemoteException` of kind `GitTooOld` whose message contains "Git 2.29 or newer", the version in use and
"Settings > Git executable", after exactly one invocation of the hardened `git ... --literal-pathspecs --version` in the
neutral folder; the refusal is not remembered (after the stub is changed to answer 2.43.0 the next check passes and
`--version` has run twice). With a new enough git, two checks over two clients of the same executable run `--version`
once, and so do eight clients that ask at the same moment against a stub whose answer takes a second. A text that cannot
be read does not block (a warning is logged) and a git that cannot be started is `GitNotFound`. With the real git the
hardened command line is accepted and the installed git passes. This scenario is tested by
`GitRemoteClient_ParseVersion_ReadsKnownFormsAndRejectsOthers`,
`GitRemoteClient_EnsureSupportedVersion_OldGit_IsRefusedWithActionableMessageAndNotRemembered`,
`GitRemoteClient_EnsureSupportedVersion_NewGit_PassesAndRunsOncePerExecutable`,
`GitRemoteClient_EnsureSupportedVersion_ConcurrentCallers_RunGitVersionOnce`,
`GitRemoteClient_EnsureSupportedVersion_UnreadableVersionIsNotBlocked_AndMissingGitIsReported` and
`GitRemoteClient_RealGit_EnsureSupportedVersion_AcceptsTheInstalledGit`, covering
`AgentControl-GitRemoteClient-MinimumGitVersion`, `AgentControl-GitRemoteClient-MinimumGitVersionMessage`,
`AgentControl-GitRemoteClient-UnreadableVersionNotBlocked`
and `AgentControl-GitRemoteClient-MinimumGitVersionCheckedOnce`.

**GitRemoteClient_Transport_OnlyTheAllowedProtocol**: for https, ssh and file targets only that protocol is allowed
(`protocol.allow=never` plus one `protocol.<kind>.allow=always`, `ext` and `fd` never, `GIT_ALLOW_PROTOCOL`); with the
real git a local path given as https and an `ext::` helper address are refused by git (a failure with the fixed message
`git ls-remote failed (exit code 128).`, which repeats neither the address nor git's words) and the helper does not run.
This scenario is tested by `GitRemoteClient_ListTags_Transport_AllowsOnlyThatProtocol` and
`GitRemoteClient_RealGit_TransportNotAllowed_IsRefusedByGit`, covering
`AgentControl-GitRemoteClient-TransportAllowList`.

**GitRemoteClient_Prompts_BackgroundNeverInteractiveMaySignIn**: background work sets `GIT_TERMINAL_PROMPT=0`,
`GCM_INTERACTIVE=never`, empty askpass variables (overriding the user's), `SSH_ASKPASS_REQUIRE=never` and
`core.askPass=`; interactive work keeps the user's askpass variables, leaves `GCM_INTERACTIVE` and
`SSH_ASKPASS_REQUIRE` unset and omits `core.askPass=`. The ssh command gets `-o BatchMode=yes -o ConnectTimeout=15`
around the user's OpenSSH command (default `ssh`, from `GIT_SSH_COMMAND` or `core.sshCommand`), a wrapper such as
`plink` is untouched, and an https remote gets none. This scenario is tested by
`GitRemoteClient_BackgroundWork_NeverPrompts`, `GitRemoteClient_InteractiveWork_LeavesCredentialUiAvailable`,
`GitRemoteClient_ListTags_SshRemote_ComposesBatchModeSshCommand`,
`GitRemoteClient_ListTags_EnvironmentSshCommandAndHttpsRemote_AreHandled` and
`GitRemoteClient_ComposeSshCommand_OnlyOpenSshGetsBatchMode`, covering
`AgentControl-GitRemoteClient-NoPromptInBackground`, `AgentControl-GitRemoteClient-InteractiveWhenUserStarted`,
`AgentControl-GitRemoteClient-SshBatchMode` and `AgentControl-GitRemoteClient-SshNonOpenSshUntouched`.

**GitRemoteClient_Environment_NoTraceNoRedirectionNeutralFolder**: with `GIT_TRACE`, `GIT_TRACE_PACKET`,
`GIT_CURL_VERBOSE`, `GIT_DIR`, `GIT_WORK_TREE` and `GIT_INDEX_FILE` set in the parent, git sees none of them; the fixed
variables (`GIT_LFS_SKIP_SMUDGE`, `GIT_LITERAL_PATHSPECS`, `GIT_OPTIONAL_LOCKS`, `GIT_NO_LAZY_FETCH`, `LC_ALL`,
`GIT_CEILING_DIRECTORIES`) are set and git runs in the neutral folder; with the real git and trace variables in the
parent a failure message contains no trace text. This scenario is tested by
`GitRemoteClient_Environment_RemovesTraceAndRedirectionVariablesAndSetsFixedOnes` and
`GitRemoteClient_RealGit_TraceVariablesInParent_ProduceNoTraceOutput`, covering
`AgentControl-GitRemoteClient-NoTraceVariables` and `AgentControl-GitRemoteClient-NeutralWorkingFolder`.

**GitRemoteClient_Process_TimeoutCancelAndTerminationConfirmed**: a command that sleeps 30 seconds is killed at a
one-second limit and reported as `TimedOut` with the message `git ls-remote did not finish within 1 seconds and was
stopped.`, and is killed when cancelled and reported as an `OperationCanceledException`; the process never finishes in
either case. With a test seam that replaces the termination (it records the process and leaves it running, or throws an
access error), a timeout and a cancellation are both reported as a `GitRemoteException` of kind `ProcessNotStopped`
with the fixed message "was stopped but could not be confirmed to have ended" within the one-second stop timeout,
and the
test kills the stub itself afterwards. This scenario is tested by
`GitRemoteClient_ListTags_ExceedsTimeLimit_KillsProcessAndReportsTimedOut`,
`GitRemoteClient_ListTags_Cancelled_KillsProcessAndThrowsOperationCanceled`,
`GitRemoteClient_ListTags_ExceedsTimeLimit_ConfirmsTerminationAndReportsOnlyTimeout`,
`GitRemoteClient_ListTags_ProcessCannotBeStopped_ReportsProcessNotStoppedAfterTimeout` and
`GitRemoteClient_ListTags_CancelledAndProcessCannotBeStopped_ReportsProcessNotStopped`, covering
`AgentControl-GitRemoteClient-TimeoutAndCancel`, `AgentControl-GitRemoteClient-TimeoutReportedAsTimedOut`,
`AgentControl-GitRemoteClient-CancellationReportedAsCancellation` and
`AgentControl-GitRemoteClient-UnstoppedProcessReported`. The termination of a whole process *tree* is
`Process.Kill(entireProcessTree)`; the tests prove that it is invoked and that its failure is surfaced, and rely on the
operating system for the descent into children.

**GitRemoteClient_Output_BytesCapFixedMessagesAndClassification**: more than 8 MB of output is `OutputTooLarge`; 5
million two-byte characters (under the cap as characters, 10 MB as bytes) is `OutputTooLarge`; exactly the cap in bytes
is accepted and one byte more is refused; an error text that starts with `password=...`
and an `Authorization: Basic ...`
line and continues for 200 KB yields the fixed sentence `git ls-remote failed (exit code 128).` with neither secret,
and the logs hold neither the address nor the arguments; git's error text that repeats the address, the host and a local
path (for authentication, not-found, unreachable, host-key, ownership and unknown failures) yields the fixed sentence of
its kind - classified as such - that contains none of them; the error samples are classified, a missing git is
`GitNotFound` and points to Settings, and a real fetch of a missing tag is `NotFound`. This scenario is tested by
`GitRemoteClient_ListTags_OutputOverCap_ThrowsOutputTooLarge`,
`GitRemoteClient_ListTags_MultiByteOutputOverCapInBytes_ThrowsOutputTooLarge`,
`GitRemoteClient_ListTags_OutputAtAndOneByteOverCap_IsAcceptedAndRefused`,
`GitRemoteClient_Failure_MessageIsFixedBoundedAndLogsAreSecretFree`,
`GitRemoteClient_Failure_MessageNeverRepeatsAddressHostOrPath`,
`GitRemoteClient_Failure_IsClassifiedFromGitsMessage`, `GitRemoteClient_GitExecutableMissing_ReportsGitNotFound` and
`GitRemoteClient_RealGit_FetchMissingTag_ReportsNotFound`, covering `AgentControl-GitRemoteClient-OutputBoundedInBytes`,
`AgentControl-GitRemoteClient-FailureMessagesFixed`, `AgentControl-GitRemoteClient-SecretFreeLogs`,
`AgentControl-GitRemoteClient-FailureClassification` and `AgentControl-GitRemoteClient-MissingGitExplained`.

**GitRemoteClient_RealGit_ListInitFetchListTreeReadBlobs**: against a real local repository, listing resolves an
annotated tag to its commit, a fetch brings in exactly one tag at depth one (one ref, a `shallow` file), the tree
listing reports the layout entries with their modes, and reading a blob delivers its bytes without writing anything to
the work folder. This scenario is tested by `GitRemoteClient_RealGit_ListInitFetchListTreeReadBlobs_Sequence`, covering
`AgentControl-GitRemoteClient-PeeledTags`, `AgentControl-GitRemoteClient-ShallowFetchExactTag`,
`AgentControl-GitRemoteClient-ListLayoutTree` and `AgentControl-GitRemoteClient-ReadBlobsWithoutCheckout`.

**GitRemoteClient_NoHooks_HostileTemplateInstallsNoHook**: with a template directory that installs a `post-checkout`
hook named in the user's environment (plain `git init --template=...` installs it, which the test shows first), the
repository the client creates holds no hook. This scenario is tested by
`GitRemoteClient_RealGit_InitWithHostileTemplate_CreatesRepositoryWithoutHooks`, covering
`AgentControl-GitRemoteClient-FetchIntoHooklessRepository` and `AgentControl-GitRemoteClient-NoHooks`. That no hook runs
while a package is fetched and written is proven at the provider by
`GitSourceProvider_Acquire_HostileHooksAndTemplate_NeverRun`.
