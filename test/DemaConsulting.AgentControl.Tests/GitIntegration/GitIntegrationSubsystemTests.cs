// Copyright (c) DEMA Consulting
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.ComponentModel;
using System.Diagnostics;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.Utilities;
using DemaConsulting.NuGet.Caching;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Subsystem tests for GitIntegration: the remote Git capability used the way the Git package
///     source uses it - through <see cref="GitRemoteClient"/>, against a real local repository with
///     the user's configuration isolated or against a stub standing in for the git executable - and
///     the saved-sign-in capability used the way the rest of the application uses it - through the
///     <see cref="IRememberedCredentialStore"/> contract, by
///     separate instances (as in separate sessions) and against the real git executable and
///     <c>git credential</c> protocol - with the credential store replaced by a throw-away
///     <c>store --file=...</c> helper in a temporary folder so that a test can never read or write
///     the user's actual credential store. Skipped when git is not installed (and failed instead when the
///     environment variable <c>CI</c> is set).
/// </summary>
[Collection("RealProcess")]
public class GitIntegrationSubsystemTests
{
    /// <summary>
    ///     A feed on a loopback address that nothing listens on; only ever described to git, never
    ///     contacted.
    /// </summary>
    private static readonly FeedOrigin Origin = FeedOrigin.Create(new Uri("https://127.0.0.1:59997/v3/index.json"));

    /// <summary>
    ///     Test the remote Git capability as a whole through its client, against a real local
    ///     repository with every isolation in force: tags are listed without a clone, one tag is
    ///     fetched shallow and its tree read, and a transport other than the one allowed is refused.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteClient_ListsFetchesOneTagAndRefusesOtherTransports()
    {
        // Arrange
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        var commit = fixture.Commit(
            new Dictionary<string, string> { [".github/agents/copilot.md"] = "x", ["release-notes.md"] = "notes" }, "one");
        fixture.Tag("v1.0.0", annotated: true);
        fixture.Push();
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => "git", Path.Combine(fixture.Root, "work"), ExtraEnvironment: fixture.IsolationEnvironment));
        var target = new GitRemoteTarget(fixture.RemotePath, GitTransport.File);
        var repository = Path.Combine(fixture.Root, "fetched");

        // Act
        var tags = await client.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken);
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);
        var fetched = await client.FetchTagAsync(target, repository, "v1.0.0", TestContext.Current.CancellationToken);
        var entries = await client.ListTreeAsync(
            repository, fetched, [".github/agents"], recursive: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(commit, Assert.Single(tags).CommitSha);
        Assert.Equal(commit, fetched);
        Assert.Equal(".github/agents/copilot.md", Assert.Single(entries).Path);
        await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget(fixture.RemotePath, GitTransport.Https), "refs/tags/*", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test, with the real git, the capability of taking committed agent folders out of git as a
    ///     whole: the tracked managed folders are reported, stopping the tracking of them leaves every
    ///     file on disk and every other tracked path alone, and the folders are then reported as no
    ///     longer tracked.
    /// </summary>
    [Fact]
    public void GitIntegrationSubsystem_CommittedAgentFolders_ReportedThenUntrackedWithoutTouchingFilesOrOtherPaths()
    {
        // Arrange: a real repo that tracks two managed folders and unrelated files
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string>
            {
                [".github/agents/a.md"] = "agent",
                [".github/templates/t.md"] = "template",
                [".github/workflows/ci.yml"] = "ci",
                ["README.md"] = "readme"
            },
            "Track agent files");
        var client = new GitClient();

        // Act
        var before = client.GetTrackedAgentFolders(fixture.WorkPath);
        client.UntrackAgentFolders(fixture.WorkPath, before.ToArray());
        var after = client.GetTrackedAgentFolders(fixture.WorkPath);

        // Assert: both folders were reported, none is tracked now, the other paths are still tracked
        // and every file is still on disk
        Assert.Equal([".github/agents", ".github/templates"], before);
        Assert.Empty(after);
        Assert.False(client.HasCommittedAgentFiles(fixture.WorkPath, TestContext.Current.CancellationToken));
        Assert.Equal(
            [".github/workflows/ci.yml", "README.md"],
            fixture.Git(fixture.WorkPath, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));
        Assert.True(File.Exists(Path.Combine(fixture.WorkPath, ".github", "agents", "a.md")));
        Assert.True(File.Exists(Path.Combine(fixture.WorkPath, ".github", "templates", "t.md")));
    }

    /// <summary>
    ///     Test, with the real git, the capability of telling the top folder of a Git repository: a
    ///     repository's working folder and a linked worktree of it (whose <c>.git</c> is a file) are
    ///     repository roots, while a subfolder of the repository and a plain folder are not.
    /// </summary>
    [Fact]
    public void GitIntegrationSubsystem_RepositoryRoot_RepositoryWorktreeAndSubfolder()
    {
        // Arrange: a real repository with one commit, a linked worktree, a subfolder and a plain folder
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["src/app.cs"] = "class App {}" }, "Initial");
        var worktree = Path.Combine(fixture.Root, "feature-worktree");
        fixture.Git(fixture.WorkPath, "worktree", "add", "-b", "feature", worktree);
        var subfolder = Path.Combine(fixture.WorkPath, "src");
        var plain = Directory.CreateDirectory(Path.Combine(fixture.Root, "plain")).FullName;

        // Act / Assert
        Assert.True(GitClient.IsRepositoryRoot(fixture.WorkPath));
        Assert.True(File.Exists(Path.Combine(worktree, ".git")));
        Assert.True(GitClient.IsRepositoryRoot(worktree));
        Assert.False(GitClient.IsRepositoryRoot(subfolder));
        Assert.False(GitClient.IsRepositoryRoot(plain));
    }

    /// <summary>
    ///     Test, with the real git, that the repository queries a card's background refresh makes
    ///     (branch, <c>HEAD</c>, committed agent folders - through a shared committed-files cache -
    ///     repository root and working-tree state) give the same answers when they run on several
    ///     worker threads at once as when they run one at a time.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RepositoryQueries_OnSeveralWorkerThreadsAtOnce_GiveTheSameAnswers()
    {
        // Arrange: a real repository that tracks a managed agent folder, and its answers one at a time
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { [".github/agents/a.md"] = "agent" }, "Track agent files");
        var client = new GitClient();
        var cache = new CommittedAgentFilesCache();
        var expected = (
            Branch: client.GetCurrentBranch(fixture.WorkPath, TestContext.Current.CancellationToken),
            Head: client.GetHeadCommitHash(fixture.WorkPath, TestContext.Current.CancellationToken),
            Committed: client.HasCommittedAgentFiles(fixture.WorkPath, TestContext.Current.CancellationToken),
            Root: GitClient.IsRepositoryRoot(fixture.WorkPath),
            Clean: client.IsWorkingTreeClean(fixture.WorkPath, TestContext.Current.CancellationToken));

        // Act: the same queries on eight worker threads at once, sharing the cache
        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var head = client.GetHeadCommitHash(fixture.WorkPath, TestContext.Current.CancellationToken);
            if (!cache.TryGetCached(fixture.WorkPath, head, out var committed))
            {
                committed = client.HasCommittedAgentFiles(fixture.WorkPath, TestContext.Current.CancellationToken);
                cache.Set(fixture.WorkPath, head, committed);
            }

            return (
                Branch: client.GetCurrentBranch(fixture.WorkPath, TestContext.Current.CancellationToken),
                Head: head,
                Committed: committed,
                Root: GitClient.IsRepositoryRoot(fixture.WorkPath),
                Clean: client.IsWorkingTreeClean(fixture.WorkPath, TestContext.Current.CancellationToken));
        }, TestContext.Current.CancellationToken)));

        // Assert: every thread got the one-at-a-time answers, and the cache holds the answer
        Assert.All(answers, answer => Assert.Equal(expected, answer));
        Assert.True(cache.TryGetCached(fixture.WorkPath, expected.Head, out var cached));
        Assert.True(cached);
    }

    /// <summary>
    ///     Test, with the real git, that the raw content of a listed blob is read from the fetched commit
    ///     exactly as the commit holds it - carriage returns, a NUL byte and an identifier that an
    ///     attribute asks to expand - even though the user's configuration asks for line-ending conversion
    ///     and a filter program, which is therefore never started.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteClient_ReadsRawBlobsWithoutCheckoutFiltersOrConversion()
    {
        // Arrange: content, then attributes that would convert and filter it on a checkout
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        var content = "crlf\r\nlf\nlone\rcr\0nul $Id$ end\r\n";
        fixture.Commit(new Dictionary<string, string> { [".github/agents/copilot.md"] = content }, "content");
        fixture.Commit(
            new Dictionary<string, string> { [".gitattributes"] = "* text eol=crlf ident filter=pwn\n" }, "attributes");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var marker = Path.Combine(fixture.Root, "filter-ran.txt");
        var hostile = fixture.CreateHostileFilterEnvironment(marker);
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => "git", Path.Combine(fixture.Root, "client-work"), ExtraEnvironment: hostile));
        var target = new GitRemoteTarget(fixture.RemotePath, GitTransport.File);
        var repository = Path.Combine(fixture.Root, "fetched");
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);
        var commit = await client.FetchTagAsync(target, repository, "v1.0.0", TestContext.Current.CancellationToken);
        var entry = Assert.Single(await client.ListTreeAsync(
            repository, commit, [".github/agents"], recursive: true, TestContext.Current.CancellationToken));

        // Act
        using var received = new MemoryStream();
        await client.ReadBlobsAsync(repository, [entry], _ => received, 1024, TestContext.Current.CancellationToken);

        // Assert: the bytes of the commit, and no filter program ran
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(content), received.ToArray());
        Assert.False(File.Exists(marker));
    }

    /// <summary>
    ///     Test, with the real git, that a failure is classified from git's answer and reported in one fixed
    ///     sentence that repeats neither the path nor git's text, and that a git which cannot be started is
    ///     reported as missing.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteClient_FailuresAreClassifiedAndTheirMessagesNeverRepeatThePath()
    {
        // Arrange: a repository that does not exist, and a git executable that does not exist
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        var missingPath = Path.Combine(fixture.Root, "sentinel-missing-repository.git");
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => "git", Path.Combine(fixture.Root, "client-work"), ExtraEnvironment: fixture.IsolationEnvironment));
        var noGit = new GitRemoteClient(new GitRemoteOptions(
            () => Path.Combine(fixture.Root, "no-git"), Path.Combine(fixture.Root, "client-work")));
        var target = new GitRemoteTarget(missingPath, GitTransport.File);

        // Act
        var notFound = await Assert.ThrowsAsync<GitRemoteException>(
            () => client.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken));
        var gitMissing = await Assert.ThrowsAsync<GitRemoteException>(
            () => noGit.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken));

        // Assert
        Assert.Multiple(
            () => Assert.Equal(GitFailureKind.NotFound, notFound.Kind),
            () => Assert.DoesNotContain("sentinel-missing-repository", notFound.Message, StringComparison.Ordinal),
            () => Assert.DoesNotContain("fatal", notFound.Message, StringComparison.OrdinalIgnoreCase),
            () => Assert.Equal(GitFailureKind.GitNotFound, gitMissing.Kind),
            () => Assert.Contains("Settings", gitMissing.Message, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test, with a stub standing in for git, that work the application started itself can never prompt -
    ///     the terminal, the credential manager's UI and both askpass programs are switched off - while work the
    ///     user started leaves the credential manager free to show its own sign-in, and that in neither case
    ///     does git read a terminal.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteClient_BackgroundWorkNeverPromptsWhileUserStartedWorkMay()
    {
        // Arrange: a stub git recording its environment, and the same request made in both modes
        var folder = CreateTempDirectory();
        var stub = GitRemoteStub.Create();
        try
        {
            var target = new GitRemoteTarget("https://example.invalid/org/agents.git", GitTransport.Https);
            var background = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, Path.Combine(folder, "background")));
            var interactive = new GitRemoteClient(
                new GitRemoteOptions(() => stub.Path, Path.Combine(folder, "interactive"), Interactive: true));

            // Act
            await background.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken);
            await interactive.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken);
            var invocations = stub.ReadInvocations().Where(invocation => invocation.Command == "ls-remote").ToList();

            // Assert: the background run switched every prompt off, the user-started one left the credential UI alone
            Assert.Equal(2, invocations.Count);
            var (quiet, asking) = (invocations[0], invocations[1]);
            Assert.Multiple(
                () => Assert.Equal("0", quiet.Environment["GIT_TERMINAL_PROMPT"]),
                () => Assert.Equal("never", quiet.Environment["GCM_INTERACTIVE"]),
                () => Assert.Equal(string.Empty, quiet.Environment["GIT_ASKPASS"]),
                () => Assert.Equal("never", quiet.Environment["SSH_ASKPASS_REQUIRE"]),
                () => Assert.Contains("core.askPass=", quiet.Arguments, StringComparison.Ordinal),
                () => Assert.Equal("0", asking.Environment["GIT_TERMINAL_PROMPT"]),
                () => Assert.NotEqual("never", asking.Environment["GCM_INTERACTIVE"]),
                () => Assert.DoesNotContain("core.askPass=", asking.Arguments, StringComparison.Ordinal));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test, with a stub standing in for git, that a git older than the supported minimum is refused with a
    ///     message that says what to do before any other command runs, that a git that hangs is stopped at the
    ///     time limit and reported as timed out without ever finishing, and that neither is hidden behind a
    ///     success.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteClient_OldGitIsRefusedAndHungGitIsStoppedAtTheTimeLimit()
    {
        // Arrange: one stub that reports an old version, and one whose tag listing hangs
        var folder = CreateTempDirectory();
        var old = GitRemoteStub.Create().Respond("version", "git version 2.20.1\n");
        var hung = GitRemoteStub.Create().Respond("ls-remote", sleepSeconds: GitRemoteStub.HangSeconds);
        try
        {
            var oldClient = new GitRemoteClient(new GitRemoteOptions(() => old.Path, Path.Combine(folder, "old")));
            var hungClient = new GitRemoteClient(
                new GitRemoteOptions(() => hung.Path, Path.Combine(folder, "hung"), ListTimeout: TimeSpan.FromSeconds(1)));
            var target = new GitRemoteTarget("https://example.invalid/org/agents.git", GitTransport.Https);

            // Act
            var tooOld = await Assert.ThrowsAsync<GitRemoteException>(
                () => oldClient.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken));
            var timedOut = await Assert.ThrowsAsync<GitRemoteException>(
                () => hungClient.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken));
            var hungRanToTheEnd = await hung.RanToTheEndOfItsHangAsync();

            // Assert
            Assert.Multiple(
                () => Assert.Equal(GitFailureKind.GitTooOld, tooOld.Kind),
                () => Assert.Contains("Settings > Git executable", tooOld.Message, StringComparison.Ordinal),
                () => Assert.Equal(["version"], old.ReadInvocations().Select(invocation => invocation.Command)),
                () => Assert.Equal(GitFailureKind.TimedOut, timedOut.Kind),
                () => Assert.False(hungRanToTheEnd));
        }
        finally
        {
            old.Delete();
            hung.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test, with a stub standing in for git, that a local git query that hangs is stopped at the
    ///     time limit and reported as timed out without ever finishing, and that a hung command whose
    ///     caller gives up (the window closes) is stopped as well.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_LocalGitHangs_IsStoppedAndReportedAsTimedOut()
    {
        // Arrange: a git whose working-tree query and HEAD query hang, and a one-second local limit
        var folder = CreateTempDirectory();
        var hung = GitStub.Create(statusSleepSeconds: GitStub.HangSeconds, revParseHeadSleepSeconds: GitStub.HangSeconds);
        try
        {
            var client = new GitClient(
                hung.Path,
                timeouts: new GitClientTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10)));

            // Act: the working-tree query runs into the limit; the HEAD query is abandoned by its caller
            // half a second after it started
            var timedOut = Record.Exception(() => client.IsWorkingTreeClean(folder, TestContext.Current.CancellationToken));
            using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            shutdown.CancelAfter(TimeSpan.FromMilliseconds(500));
            var canceled = Record.Exception(() => client.GetHeadCommitHash(folder, shutdown.Token));
            var hungRanToTheEnd = await hung.RanToTheEndOfItsHangAsync();

            // Assert: a timeout naming the limit, a cancellation, and neither process finished
            Assert.Multiple(
                () => Assert.Equal(TimeSpan.FromSeconds(1), Assert.IsType<GitCommandTimeoutException>(timedOut).Timeout),
                () => Assert.IsAssignableFrom<OperationCanceledException>(canceled),
                () => Assert.False(hungRanToTheEnd));
        }
        finally
        {
            hung.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test, with the real git against a private Git repository served over TLS that answers 401 without
    ///     credentials and with a credential helper that behaves like Git Credential Manager (it supplies the
    ///     credential only when it may show its sign-in UI), that work the application started itself never
    ///     prompts - git cannot read the repository and reports that a sign-in is needed, and the helper was
    ///     asked but could not prompt - while work the user started lets the helper sign in, so the same
    ///     request then succeeds.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteClient_PrivateRepositorySignInIsOnlyRequestedForUserStartedWork()
    {
        // Arrange: a repository with a tag behind a private server, and the user's own helper in an isolated configuration
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { [".github/agents/copilot.md"] = "x", ["release-notes.md"] = "n" }, "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        await using var server = await GitHttpsServer.StartAsync(fixture.RemotePath, "agent", "sentinel-pw-4f7a");
        var (configuration, helperLog) = GitHttpsServer.CreateUserConfiguration(fixture.Root, "agent", "sentinel-pw-4f7a");
        var target = new GitRemoteTarget(server.RepositoryUrl, GitTransport.Https);
        var background = new GitRemoteClient(new GitRemoteOptions(
            () => "git",
            Path.Combine(fixture.Root, "background"),
            ExtraEnvironment: new Dictionary<string, string?>
            {
                ["GIT_CONFIG_GLOBAL"] = configuration,
                ["GIT_CONFIG_NOSYSTEM"] = "1"
            }));
        var userStarted = new GitRemoteClient(new GitRemoteOptions(
            () => "git",
            Path.Combine(fixture.Root, "user-started"),
            Interactive: true,
            ExtraEnvironment: new Dictionary<string, string?>
            {
                ["GIT_CONFIG_GLOBAL"] = configuration,
                ["GIT_CONFIG_NOSYSTEM"] = "1",
                ["GCM_INTERACTIVE"] = null
            }));

        // Act: background work first, then the same request started by the user
        var refused = await Assert.ThrowsAsync<GitRemoteException>(
            () => background.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken));
        var loggedAfterBackground = File.Exists(helperLog)
            ? await File.ReadAllLinesAsync(helperLog, TestContext.Current.CancellationToken) : [];
        var signedInBefore = server.SignedInRequestCount;
        var tags = await userStarted.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken);
        var loggedAfterUser = await File.ReadAllLinesAsync(helperLog, TestContext.Current.CancellationToken);

        // Assert: background work was refused as "sign-in needed", was never signed in and never prompted
        Assert.Multiple(
            () => Assert.Equal(GitFailureKind.AuthenticationRequired, refused.Kind),
            () => Assert.DoesNotContain("prompted", loggedAfterBackground),
            () => Assert.Equal(0, signedInBefore),
            () => Assert.DoesNotContain("sentinel-pw-4f7a", refused.Message, StringComparison.Ordinal));

        // Assert: the user-started work was allowed to prompt and then succeeded with a signed-in request
        Assert.Multiple(
            () => Assert.Equal("v1.0.0", Assert.Single(tags).Name),
            () => Assert.Contains("prompted", loggedAfterUser),
            () => Assert.True(server.SignedInRequestCount > 0));
    }

    /// <summary>
    ///     Test that a sign-in kept by one session is found by another and removed on request: it
    ///     lives in the user's helper, not in the instance that kept it, and nothing is ever asked
    ///     of the user along the way.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_SavedSignIn_KeptInOneSessionFoundInAnotherAndRemovedOnRequest()
    {
        // Arrange: two independent instances sharing only the user's (isolated) helper
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            IRememberedCredentialStore firstSession = CreateClient(folder);
            IRememberedCredentialStore laterSession = CreateClient(folder);
            var credential = new NuGetCredential("svc", "subsystem-pw-4419");
            var cancellation = TestContext.Current.CancellationToken;

            // Act / Assert: nothing saved yet; the first session keeps a sign-in
            Assert.True(await laterSession.IsAvailableAsync(Origin, cancellation));
            Assert.Null(await laterSession.FillAsync(Origin, cancellation));
            Assert.True(await firstSession.ApproveAsync(Origin, credential, cancellation));

            // Assert: a later session finds it
            Assert.Equal(credential, await laterSession.FillAsync(Origin, cancellation));

            // Act / Assert: forgetting by origin removes it for every session
            Assert.True(await laterSession.RejectAsync(Origin, null, cancellation));
            Assert.Null(await firstSession.FillAsync(Origin, cancellation));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a sign-in the line-based credential protocol cannot carry - one with a line
    ///     feed, carriage return or NUL - is never handed to the helper (nothing is stored and a
    ///     lookup finds nothing), while one whose password contains <c>=</c> (a Base64 token) is
    ///     kept and found unchanged.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_SavedSignIn_ValueWithLineBreakIsNeverStoredButEqualsIs()
    {
        // Arrange
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            IRememberedCredentialStore store = CreateClient(folder);
            var cancellation = TestContext.Current.CancellationToken;

            // Act: values the protocol cannot carry
            var withLineFeed = await store.ApproveAsync(Origin, new NuGetCredential("svc", "pw\nusername=evil"), cancellation);
            var withCarriageReturn = await store.ApproveAsync(Origin, new NuGetCredential("svc", "pw\rx"), cancellation);
            var found = await store.FillAsync(Origin, cancellation);

            // Assert: refused, and nothing reached the helper
            Assert.False(withLineFeed);
            Assert.False(withCarriageReturn);
            Assert.Null(found);
            Assert.False(File.Exists(Path.Combine(folder, "credentials.txt")));

            // Act / Assert: a Base64-style token with padding is kept and found unchanged
            Assert.True(await store.ApproveAsync(Origin, new NuGetCredential("svc", "dG9rZW4tYWJjMTI=="), cancellation));
            Assert.Equal("dG9rZW4tYWJjMTI==", (await store.FillAsync(Origin, cancellation))!.Secret);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that with no credential helper configured the subsystem reports that nothing can be
    ///     kept and nothing is found, instead of failing.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_SavedSignIn_NoHelperConfigured_ReportsUnavailableAndNothingSaved()
    {
        // Arrange
        SkipWithoutGit();
        var folder = CreateTempDirectory();
        try
        {
            IRememberedCredentialStore store = new GitCredentialClient(
                "git", extraConfiguration: ["credential.helper="], workingDirectory: folder);
            var cancellation = TestContext.Current.CancellationToken;

            // Act / Assert
            Assert.False(await store.IsAvailableAsync(Origin, cancellation));
            Assert.Null(await store.FillAsync(Origin, cancellation));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a client whose only credential helper is a file store inside a temporary folder.
    /// </summary>
    /// <param name="folder">The temporary folder holding the store and used as the working
    ///     directory.</param>
    /// <returns>The client.</returns>
    private static GitCredentialClient CreateClient(string folder) => new(
        "git",
        extraConfiguration:
        [
            "credential.helper=",
            $"credential.helper=store --file={Path.Combine(folder, "credentials.txt").Replace('\\', '/')}"
        ],
        workingDirectory: folder);

    /// <summary>
    ///     Skips the calling test when no git executable can be run.
    /// </summary>
    private static void SkipWithoutGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
            if (process is { ExitCode: 0 })
            {
                return;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Not installed: skipped below
        }

        AgentPackageManagement.GitRepositoryFixture.SkipOrFail("git is not installed, so the credential protocol cannot be exercised.");
    }

    /// <summary>
    ///     Test, with stubs standing in for git, that the git of package sources and of credential helpers
    ///     is started off the thread that asks: a tag listing and a credential-helper question asked from a
    ///     thread guarded against blocking calls (as the window's UI thread is in the tests) are answered,
    ///     and nothing was refused on that thread.
    /// </summary>
    [Fact]
    public async Task GitIntegrationSubsystem_RemoteAndCredentialGit_FromGuardedThread_StartOffThatThread()
    {
        // Arrange
        var folder = CreateTempDirectory();
        var remoteStub = GitRemoteStub.Create().Respond("ls-remote", "0123456789abcdef0123456789abcdef01234567\trefs/tags/v1.0.0\n");
        var credentialStub = GitCredentialStub.Create(configOutput: "manager");
        try
        {
            var remote = new GitRemoteClient(new GitRemoteOptions(() => remoteStub.Path, Path.Combine(folder, "remote")));
            var credentials = new GitCredentialClient(credentialStub.Path, workingDirectory: folder);
            var target = new GitRemoteTarget("https://example.invalid/org/agents.git", GitTransport.Https);
            var origin = FeedOrigin.Create(new Uri("https://feeds.example.com/v3/index.json"));

            // Act: both asked from a guarded thread; the per-thread record is read before any await
            Task<IReadOnlyList<GitRemoteTag>> listing;
            Task<bool> asking;
            using (BlockingCallGuard.GuardCurrentThread())
            {
                listing = remote.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken);
                asking = credentials.IsAvailableAsync(origin, TestContext.Current.CancellationToken);
            }

            var violations = BlockingCallGuard.TakeViolations();
            var tags = await listing;
            var available = await asking;

            // Assert
            Assert.Empty(violations);
            Assert.Equal("v1.0.0", Assert.Single(tags).Name);
            Assert.True(available);
        }
        finally
        {
            remoteStub.Delete();
            credentialStub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test, with the real git and a local bare repository (no network), that the branch and clone
    ///     capability works as a whole: a repository is cloned, its branches listed, a remote-tracking
    ///     branch switched to, and the working tree and branch list follow.
    /// </summary>
    [Fact]
    public void GitIntegrationSubsystem_CloneThenListAndSwitchBranches_OnRealRepository_FollowsTheBranch()
    {
        // Arrange: a remote with main and feature, whose files differ
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["branch.txt"] = "main" }, "Initial");
        fixture.Git(fixture.WorkPath, "switch", "-q", "-c", "feature");
        fixture.Commit(new Dictionary<string, string> { ["branch.txt"] = "feature" }, "Feature");
        fixture.Git(fixture.WorkPath, "switch", "-q", "main");
        fixture.Git(fixture.WorkPath, "push", "-q", fixture.RemotePath, "main", "feature");
        fixture.Git(fixture.RemotePath, "symbolic-ref", "HEAD", "refs/heads/main");
        var client = new GitClient("git", extraEnvironment: fixture.IsolationEnvironment);
        var clone = Path.Combine(fixture.Root, "clone");

        // Act
        var cloned = client.Clone(fixture.RemotePath, clone, TestContext.Current.CancellationToken);
        var before = client.GetBranches(clone, TestContext.Current.CancellationToken);
        var switched = client.SwitchBranch(clone, "origin/feature", createTrackingFromRemote: true, TestContext.Current.CancellationToken);
        var after = client.GetBranches(clone, TestContext.Current.CancellationToken);

        // Assert
        Assert.Multiple(
            () => Assert.True(cloned.Succeeded, cloned.StandardError),
            () => Assert.Equal("main", before.CurrentBranch),
            () => Assert.Equal(["origin/feature"], before.Remote.Select(branch => branch.FullName)),
            () => Assert.True(switched.Succeeded, switched.StandardError),
            () => Assert.Equal("feature", after.CurrentBranch),
            () => Assert.Equal("feature", client.GetCurrentBranch(clone, TestContext.Current.CancellationToken)),
            () => Assert.Equal("feature", File.ReadAllText(Path.Combine(clone, "branch.txt"))),
            () => Assert.Empty(after.Remote));
    }

    /// <summary>
    ///     Test, with a stub standing in for git, that a clone URL carrying a password never leaves the
    ///     subsystem in clear text: not in the log, the result or the failure text.
    /// </summary>
    [Fact]
    public void GitIntegrationSubsystem_CloneWithCredentialsInUrl_NeverLogsThePassword()
    {
        // Arrange: a clone that fails and echoes the URL back, as git does
        const string password = "Sup3rSecretPw";
        var url = $"https://bob:{password}@git.example.com/team/agents.git";
        var folder = CreateTempDirectory();
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", Error: $"fatal: unable to access '{url}/': 401", ExitCode: 128)
        ]);
        var logger = new GitClientListLogger();
        try
        {
            var client = new GitClient(stub.Path, logger);

            // Act
            var result = client.Clone(url, Path.Combine(folder, "agents"), TestContext.Current.CancellationToken);

            // Assert: git's explanation is still there, the secret is not
            Assert.Multiple(
                () => Assert.False(result.Succeeded),
                () => Assert.Contains("git.example.com", result.StandardError, StringComparison.Ordinal),
                () => Assert.DoesNotContain(password, result.StandardError, StringComparison.Ordinal),
                () => Assert.All(logger.Inner.Snapshot(), line => Assert.DoesNotContain(password, line, StringComparison.Ordinal)));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a fresh temporary directory.
    /// </summary>
    /// <returns>The directory path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentcontrol_git_subsystem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
