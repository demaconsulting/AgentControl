### GitRemoteClient

#### Verification Approach

`GitRemoteClient` is verified two ways. `GitRemoteClientTests.cs` runs the client against **`GitRemoteStub`**, a fake git
executable (a batch file on Windows, a shell script elsewhere) that records every invocation's arguments, the environment
variables that matter and the working folder, and answers each git command with canned output, error text, exit code and
an optional sleep; the tests therefore assert the *exact* command line and environment of each operation, the prompt
rules of background and interactive work, the time limit and cancellation (the sleeping stub never reaches its
`FINISHED` marker when it is killed), the output cap, redaction and the classification of failures.
`GitRemoteClientIntegrationTests.cs` and the subsystem test run the **real git** against local bare repositories (the
fixture isolates the user's git configuration) to prove the semantics the stubs assume: peeled tags, a shallow fetch of
one tag, the tree listing and path checkout, git's own refusal of a transport that is not allowed (including an `ext::`
helper forced through the internal API), and that trace variables in the parent produce no trace text.

#### Test Environment

The stub tests need nothing but the operating system's shell; they belong to the `RealProcess` collection because they
launch processes and some set variables in the test process (restored afterwards). The real-git tests need git on `PATH`
and are skipped only when it is not installed; no network, credential or user configuration is used. The batch stub
drops a `^` from its recorded argument text, so tests assert the commands and options rather than `^{commit}`.

#### Acceptance Criteria

- The `ls-remote` command line is exactly the documented list of command-scoped settings with the address and glob after
  `--`; each transport allows only its own protocol in the configuration and in `GIT_ALLOW_PROTOCOL`.
- Background work cannot prompt (terminal, credential manager, askpass, ssh), even when the user's askpass variables are
  set; user-started work leaves them; trace and redirection variables never reach git; git runs in a neutral folder.
- ssh gets batch mode and a connect timeout around the user's own OpenSSH command and is left alone for other commands.
- A hanging command is killed at its limit or on cancellation; output over the cap is refused; error text is redacted and
  bounded; logs hold no address or argument; failures are classified; a missing git is explained.
- Real git lists, fetches one tag shallow, lists the tree, checks out only the named path and refuses other transports.

#### Test Scenarios

**GitRemoteClient_Commands_UseExactHardenedArgumentsAndAddressesAfterDashDash**: the full argument list of
`ls-remote`; `init` from an empty template; the shallow single-tag `fetch` with `--no-tags`,
`--no-recurse-submodules`, `--no-write-fetch-head` and the refspec; `rev-parse`; `ls-tree -r -z --full-tree -l`;
`checkout` with `core.symlinks=false`; tag names, globs and commit ids that could be read as options are refused before
git runs; a tag listing keeps only well-formed lines and resolves annotated tags. This scenario is tested by
`GitRemoteClient_ListTags_Https_UsesExactCommandScopedConfigurationAndDashDash`,
`GitRemoteClient_ListTags_ParsesLightweightAndAnnotatedTags`, `GitRemoteClient_ListTags_UnsafeGlob_IsRefusedWithoutRunningGit`,
`GitRemoteClient_InitAndFetchTag_UseEmptyTemplateAndShallowSingleTagFetch`,
`GitRemoteClient_FetchTag_RevParseWithoutCommit_Throws`,
`GitRemoteClient_FetchTag_UnsafeTagName_IsRefusedWithoutRunningGit` and
`GitRemoteClient_ListTreeAndCheckout_UseLiteralPathsAndParseEntries`, covering
`AgentControl-GitRemoteClient-ListTagsWithoutClone`, `AgentControl-GitRemoteClient-PeeledTags`,
`AgentControl-GitRemoteClient-ShallowFetchExactTag`, `AgentControl-GitRemoteClient-ListLayoutTree`,
`AgentControl-GitRemoteClient-NoHooks`, `AgentControl-GitRemoteClient-NoSubmodulesNoLfs` and
`AgentControl-GitRemoteClient-CommandScopedConfiguration` (with `GitRemoteClient_ExtraConfigurationAndEnvironment_AreAppliedLast`).

**GitRemoteClient_Transport_OnlyTheAllowedProtocol**: for https, ssh and file targets only that protocol is allowed
(`protocol.allow=never` plus one `protocol.<kind>.allow=always`, `ext` and `fd` never, `GIT_ALLOW_PROTOCOL`); with the
real git a local path given as https and an `ext::` helper address are refused by git ("not allowed") and the helper does
not run. This scenario is tested by `GitRemoteClient_ListTags_Transport_AllowsOnlyThatProtocol` and
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
`AgentControl-GitRemoteClient-NoPromptInBackground`, `AgentControl-GitRemoteClient-InteractiveWhenUserStarted` and
`AgentControl-GitRemoteClient-SshBatchMode`.

**GitRemoteClient_Environment_NoTraceNoRedirectionNeutralFolder**: with `GIT_TRACE`, `GIT_TRACE_PACKET`,
`GIT_CURL_VERBOSE`, `GIT_DIR`, `GIT_WORK_TREE` and `GIT_INDEX_FILE` set in the parent, git sees none of them; the fixed
variables (`GIT_LFS_SKIP_SMUDGE`, `GIT_LITERAL_PATHSPECS`, `GIT_OPTIONAL_LOCKS`, `GIT_NO_LAZY_FETCH`, `LC_ALL`,
`GIT_CEILING_DIRECTORIES`) are set and git runs in the neutral folder; with the real git and trace variables in the
parent a failure message contains no trace text. This scenario is tested by
`GitRemoteClient_Environment_RemovesTraceAndRedirectionVariablesAndSetsFixedOnes` and
`GitRemoteClient_RealGit_TraceVariablesInParent_ProduceNoTraceOutput`, covering
`AgentControl-GitRemoteClient-NoTraceVariables`.

**GitRemoteClient_Limits_TimeoutCancelOutputCapRedactionAndClassification**: a command that sleeps 30 seconds is killed
at a one-second limit (`TimedOut`) and when cancelled (`OperationCanceledException`) and never finishes; more than 8 MB
of output is `OutputTooLarge`; an error text that starts with `password=...` and an `Authorization: Basic ...` line
and continues for 200 KB yields a short message with neither secret, and the logs hold neither the address nor the
arguments; the error samples of authentication, not-found, unreachable, host-key, ownership and unknown failures are
classified, a missing git is `GitNotFound` and points to Settings, and a real fetch of a missing tag is `NotFound`. This
scenario is tested by `GitRemoteClient_ListTags_ExceedsTimeLimit_KillsProcessAndReportsTimedOut`,
`GitRemoteClient_ListTags_Cancelled_KillsProcessAndThrowsOperationCanceled`,
`GitRemoteClient_ListTags_OutputOverCap_ThrowsOutputTooLarge`,
`GitRemoteClient_Failure_MessageIsRedactedBoundedAndLogsAreSecretFree`,
`GitRemoteClient_Failure_IsClassifiedFromGitsMessage`, `GitRemoteClient_GitExecutableMissing_ReportsGitNotFound` and
`GitRemoteClient_RealGit_FetchMissingTag_ReportsNotFound`, covering `AgentControl-GitRemoteClient-TimeoutAndCancel`,
`AgentControl-GitRemoteClient-OutputBoundedAndRedacted`, `AgentControl-GitRemoteClient-SecretFreeArguments` and
`AgentControl-GitRemoteClient-FailureClassification`.

**GitRemoteClient_RealGit_ListInitFetchListTreeCheckout**: against a real local repository, listing resolves an
annotated tag to its commit, a fetch brings in exactly one tag at depth one (one ref, a `shallow` file), the tree
listing reports the layout entries with their modes, and a checkout writes only the named path. This scenario is tested
by `GitRemoteClient_RealGit_ListInitFetchListTreeCheckout_Sequence`, covering
`AgentControl-GitRemoteClient-PeeledTags`, `AgentControl-GitRemoteClient-ShallowFetchExactTag` and
`AgentControl-GitRemoteClient-ListLayoutTree`.

**GitRemoteClient_NoHooks_HostileHooksAndTemplateNeverRun**: the hook scenario of the provider tests (a template
directory and a global hooks path that install a hook) shows that no hook runs when a package is fetched through the
client's configuration. This scenario is tested by `GitSourceProvider_Acquire_HostileHooksAndTemplate_NeverRun`,
covering `AgentControl-GitRemoteClient-NoHooks`.
