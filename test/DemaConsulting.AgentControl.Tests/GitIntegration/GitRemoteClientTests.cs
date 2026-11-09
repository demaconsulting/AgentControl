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

using System.Diagnostics;
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Tests.PackageCredentials;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Tests of <see cref="GitRemoteClient"/> against a recording stub in place of git: the exact
///     arguments and environment of every command, the prompt rules of interactive and background
///     work, time limits, cancellation, output bounds, redaction and failure classification.
/// </summary>
/// <remarks>
///     Several tests set variables in this process's environment (so the stub inherits them) and
///     restore them afterwards, which is why the class is in the sequential collection.
/// </remarks>
[Collection("RealProcess")]
public sealed class GitRemoteClientTests : IDisposable
{
    /// <summary>
    ///     A sentinel SHA-1 object id.
    /// </summary>
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>
    ///     A second sentinel object id.
    /// </summary>
    private const string OtherSha = "fedcba9876543210fedcba9876543210fedcba98";

    /// <summary>
    ///     The stubs and folders to remove after a test.
    /// </summary>
    private readonly List<string> _folders = [];

    /// <summary>
    ///     The stubs to delete after a test.
    /// </summary>
    private readonly List<GitRemoteStub> _stubs = [];

    /// <summary>
    ///     Test that listing tags over https runs exactly the hardened command: every
    ///     command-scoped setting in order, only the https transport allowed, literal pathspecs, and
    ///     the address and glob after <c>--</c>.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_Https_UsesExactCommandScopedConfigurationAndDashDash()
    {
        // Arrange
        var (client, stub, workRoot) = CreateClient();
        stub.Respond("ls-remote", $"{Sha}\trefs/tags/v1.0.0\n");

        // Act
        var tags = await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/org/repo.git", GitTransport.Https),
            "refs/tags/v*",
            TestContext.Current.CancellationToken);

        // Assert: the whole argument list, so no setting can be added or dropped unnoticed
        var hooks = Path.Combine(workRoot, "empty-hooks");
        var expected =
            "-c protocol.allow=never -c protocol.https.allow=always -c protocol.ext.allow=never " +
            "-c protocol.fd.allow=never " + $"-c core.hooksPath={hooks} " +
            "-c core.fsmonitor=false -c core.untrackedCache=false -c core.autocrlf=false -c core.eol=lf " +
            "-c core.protectNTFS=true -c core.protectHFS=true -c transfer.fsckObjects=true " +
            "-c fetch.fsckObjects=true -c fetch.recurseSubmodules=false -c submodule.recurse=false " +
            "-c gc.auto=0 -c maintenance.auto=false -c filter.lfs.smudge= -c filter.lfs.process= " +
            "-c filter.lfs.required=false -c http.followRedirects=initial -c http.lowSpeedLimit=1000 " +
            "-c http.lowSpeedTime=30 -c core.askPass= -c advice.detachedHead=false " +
            "--literal-pathspecs ls-remote --tags -- https://example.invalid/org/repo.git refs/tags/v*";
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.Equal(expected, invocation.Arguments);
        Assert.Equal("v1.0.0", Assert.Single(tags).Name);
        Assert.True(Directory.Exists(hooks));
        Assert.Empty(Directory.EnumerateFileSystemEntries(hooks));
    }

    /// <summary>
    ///     Test that each transport kind allows exactly its own protocol, in the configuration and in
    ///     <c>GIT_ALLOW_PROTOCOL</c>.
    /// </summary>
    [Theory]
    [InlineData("Https", "protocol.https.allow=always", "https")]
    [InlineData("Ssh", "protocol.ssh.allow=always", "ssh")]
    [InlineData("File", "protocol.file.allow=always", "file")]
    public async Task GitRemoteClient_ListTags_Transport_AllowsOnlyThatProtocol(
        string transportName, string expectedSetting, string expectedProtocol)
    {
        // Arrange
        var transport = Enum.Parse<GitTransport>(transportName);
        var (client, stub, _) = CreateClient();
        var url = transport == GitTransport.Ssh ? "ssh://git@example.invalid/r.git" : "https://example.invalid/r.git";

        // Act
        await client.ListTagsAsync(new GitRemoteTarget(url, transport), "refs/tags/*", TestContext.Current.CancellationToken);

        // Assert
        var invocation = stub.ReadInvocations().First(i => i.Command == "ls-remote");
        Assert.Contains("-c protocol.allow=never ", invocation.Arguments, StringComparison.Ordinal);
        Assert.Contains("-c " + expectedSetting + " ", invocation.Arguments, StringComparison.Ordinal);
        foreach (var other in new[] { "https", "ssh", "file" }.Where(name => name != expectedProtocol))
        {
            Assert.DoesNotContain($"protocol.{other}.allow=always", invocation.Arguments, StringComparison.Ordinal);
        }

        Assert.Contains("-c protocol.ext.allow=never ", invocation.Arguments, StringComparison.Ordinal);
        Assert.Contains("-c protocol.fd.allow=never ", invocation.Arguments, StringComparison.Ordinal);
        Assert.Equal(expectedProtocol, invocation.Environment["GIT_ALLOW_PROTOCOL"]);
    }

    /// <summary>
    ///     Test that background work can never prompt: the terminal, the credential manager's UI and
    ///     both askpass programs are switched off, and ssh may not ask either.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_BackgroundWork_NeverPrompts()
    {
        // Arrange
        using var environment = new TemporaryEnvironment(("GIT_ASKPASS", "user-askpass"), ("SSH_ASKPASS", "user-ssh-askpass"));
        var (client, stub, _) = CreateClient(interactive: false);

        // Act
        await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken);

        // Assert: even the user's own askpass variables are overridden for background work
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.Equal("0", invocation.Environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("never", invocation.Environment["GCM_INTERACTIVE"]);
        Assert.Equal(string.Empty, invocation.Environment["GIT_ASKPASS"]);
        Assert.Equal(string.Empty, invocation.Environment["SSH_ASKPASS"]);
        Assert.Equal("never", invocation.Environment["SSH_ASKPASS_REQUIRE"]);
        Assert.Contains("-c core.askPass= ", invocation.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that work the user started leaves the credential manager and the user's askpass free
    ///     to show their UI, while still never reading a terminal.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_InteractiveWork_LeavesCredentialUiAvailable()
    {
        // Arrange: the user's own askpass settings, and no GCM_INTERACTIVE
        using var environment = new TemporaryEnvironment(
            ("GIT_ASKPASS", "user-askpass"), ("SSH_ASKPASS", "user-ssh-askpass"), ("GCM_INTERACTIVE", null), ("SSH_ASKPASS_REQUIRE", null));
        var (client, stub, _) = CreateClient(interactive: true);

        // Act
        await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken);

        // Assert
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.Equal("0", invocation.Environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal(string.Empty, invocation.Environment["GCM_INTERACTIVE"]);
        Assert.Equal("user-askpass", invocation.Environment["GIT_ASKPASS"]);
        Assert.Equal("user-ssh-askpass", invocation.Environment["SSH_ASKPASS"]);
        Assert.Equal(string.Empty, invocation.Environment["SSH_ASKPASS_REQUIRE"]);
        Assert.DoesNotContain("core.askPass=", invocation.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that trace and redirection variables of the parent never reach git, the fixed
    ///     variables are set, and git runs in AgentControl's neutral folder with a ceiling above it.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_Environment_RemovesTraceAndRedirectionVariablesAndSetsFixedOnes()
    {
        // Arrange: the parent environment would write headers to stderr and redirect operations
        using var environment = new TemporaryEnvironment(
            ("GIT_TRACE", "1"), ("GIT_TRACE_PACKET", "1"), ("GIT_CURL_VERBOSE", "1"), ("GIT_DIR", "elsewhere"),
            ("GIT_WORK_TREE", "elsewhere"), ("GIT_INDEX_FILE", "elsewhere"));
        var (client, stub, workRoot) = CreateClient();

        // Act
        await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken);

        // Assert
        var invocation = Assert.Single(stub.ReadInvocations());
        foreach (var name in new[] { "GIT_TRACE", "GIT_TRACE_PACKET", "GIT_CURL_VERBOSE", "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
        {
            Assert.Equal(string.Empty, invocation.Environment[name]);
        }

        Assert.Equal("1", invocation.Environment["GIT_LFS_SKIP_SMUDGE"]);
        Assert.Equal("1", invocation.Environment["GIT_LITERAL_PATHSPECS"]);
        Assert.Equal("0", invocation.Environment["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("1", invocation.Environment["GIT_NO_LAZY_FETCH"]);
        Assert.Equal("C", invocation.Environment["LC_ALL"]);
        Assert.Equal(Path.GetFullPath(workRoot), invocation.Environment["GIT_CEILING_DIRECTORIES"]);
        Assert.EndsWith(
            Path.Combine(Path.GetFileName(workRoot), "neutral"),
            invocation.WorkingDirectory.TrimEnd('\\', '/'),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that the ssh command is made non-interactive: the user's own OpenSSH command (from
    ///     <c>core.sshCommand</c>) keeps its options and gains <c>BatchMode</c> and a connect timeout,
    ///     the default is plain <c>ssh</c>, and an https remote gets none.
    /// </summary>
    [Theory]
    [InlineData("", "ssh -o BatchMode=yes -o ConnectTimeout=15")]
    [InlineData("ssh -F custom-config", "ssh -F custom-config -o BatchMode=yes -o ConnectTimeout=15")]
    public async Task GitRemoteClient_ListTags_SshRemote_ComposesBatchModeSshCommand(string configured, string expected)
    {
        // Arrange: no ssh command in the parent environment; the stub answers git config
        using var environment = new TemporaryEnvironment(("GIT_SSH_COMMAND", null), ("GIT_SSH", null));
        var (client, stub, _) = CreateClient();
        stub.Respond("config", configured.Length > 0 ? configured + "\n" : string.Empty, exitCode: configured.Length > 0 ? 0 : 1);

        // Act
        await client.ListTagsAsync(
            new GitRemoteTarget("ssh://git@example.invalid/r.git", GitTransport.Ssh), "refs/tags/*",
            TestContext.Current.CancellationToken);

        // Assert
        var list = stub.ReadInvocations().Single(i => i.Command == "ls-remote");
        Assert.Equal(expected, list.Environment["GIT_SSH_COMMAND"]);
        Assert.Contains(stub.ReadInvocations(), i => i.Command == "config" && i.Arguments == "config --get core.sshCommand");
    }

    /// <summary>
    ///     Test that an ssh command set in the environment is used as the base, without asking git
    ///     for <c>core.sshCommand</c>, and that an https remote is given no ssh command.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_EnvironmentSshCommandAndHttpsRemote_AreHandled()
    {
        // Arrange
        using var environment = new TemporaryEnvironment(("GIT_SSH_COMMAND", "ssh -vv"));
        var (client, stub, _) = CreateClient();

        // Act
        await client.ListTagsAsync(
            new GitRemoteTarget("ssh://git@example.invalid/r.git", GitTransport.Ssh), "refs/tags/*",
            TestContext.Current.CancellationToken);
        await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken);

        // Assert
        var invocations = stub.ReadInvocations();
        Assert.DoesNotContain(invocations, i => i.Command == "config");
        Assert.Equal("ssh -vv -o BatchMode=yes -o ConnectTimeout=15", invocations[0].Environment["GIT_SSH_COMMAND"]);
        Assert.Equal("ssh -vv", invocations[1].Environment["GIT_SSH_COMMAND"]);
    }

    /// <summary>
    ///     Test that only an OpenSSH command is given OpenSSH options: a wrapper such as
    ///     <c>plink</c> is left untouched.
    /// </summary>
    [Theory]
    [InlineData(null, "ssh -o BatchMode=yes -o ConnectTimeout=15")]
    [InlineData("  ", "ssh -o BatchMode=yes -o ConnectTimeout=15")]
    [InlineData("/usr/bin/ssh -i key", "/usr/bin/ssh -i key -o BatchMode=yes -o ConnectTimeout=15")]
    [InlineData("\"C:\\Program Files\\OpenSSH\\ssh.exe\" -v", "\"C:\\Program Files\\OpenSSH\\ssh.exe\" -v -o BatchMode=yes -o ConnectTimeout=15")]
    [InlineData("plink -batch", null)]
    [InlineData("C:\\tools\\TortoisePlink.exe -batch", null)]
    [InlineData("my-ssh-wrapper", null)]
    public void GitRemoteClient_ComposeSshCommand_OnlyOpenSshGetsBatchMode(string? existing, string? expected) =>
        Assert.Equal(expected, GitRemoteClient.ComposeSshCommand(existing));

    /// <summary>
    ///     Test that command-scoped extra configuration and environment of the options are applied
    ///     after the built-in ones, and that none of it is written anywhere but the command line.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ExtraConfigurationAndEnvironment_AreAppliedLast()
    {
        // Arrange
        var stub = NewStub();
        var workRoot = NewFolder();
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => stub.Path, workRoot, ExtraConfiguration: ["credential.helper="],
            ExtraEnvironment: new Dictionary<string, string?> { ["GIT_TERMINAL_PROMPT"] = "7", ["GIT_DIR"] = null }));

        // Act
        await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken);

        // Assert
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.Contains("-c advice.detachedHead=false -c credential.helper= --literal-pathspecs", invocation.Arguments, StringComparison.Ordinal);
        Assert.Equal("7", invocation.Environment["GIT_TERMINAL_PROMPT"]);
    }

    /// <summary>
    ///     Test that a listing keeps only well-formed tag lines, resolves an annotated tag to its
    ///     peeled commit (not the tag object), and orders by name.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_ParsesLightweightAndAnnotatedTags()
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        stub.Respond(
            "ls-remote",
            $"{Sha}\trefs/tags/v1.0.0\n" +
            $"{OtherSha}\trefs/tags/v1.1.0\n{Sha}\trefs/tags/v1.1.0^{{}}\n" +
            $"{Sha}\trefs/heads/main\n" +
            $"not-hex\trefs/tags/bad\n" +
            "no-tab-here\n");

        // Act
        var tags = await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/v*",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["v1.0.0", "v1.1.0"], tags.Select(tag => tag.Name));
        Assert.Equal(Sha, tags[0].CommitSha);
        Assert.Equal(OtherSha, tags[1].ObjectSha);
        Assert.Equal(Sha, tags[1].CommitSha);
    }

    /// <summary>
    ///     Test that a tag glob that could carry anything but tag-name characters is refused before
    ///     git runs.
    /// </summary>
    [Theory]
    [InlineData("--upload-pack=calc")]
    [InlineData("refs/heads/*")]
    [InlineData("refs/tags/a b")]
    [InlineData("refs/tags/$(calc)")]
    public async Task GitRemoteClient_ListTags_UnsafeGlob_IsRefusedWithoutRunningGit(string glob)
    {
        // Arrange
        var (client, stub, _) = CreateClient();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), glob, TestContext.Current.CancellationToken));
        Assert.Empty(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test that fetching a tag creates the repository from an empty template, fetches exactly one
    ///     tag shallow with no tags, no submodules and no fetch-head, names the address after
    ///     <c>--</c>, and returns the commit that <c>rev-parse</c> reports.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_InitAndFetchTag_UseEmptyTemplateAndShallowSingleTagFetch()
    {
        // Arrange
        var (client, stub, workRoot) = CreateClient();
        var repository = Path.Combine(NewFolder(), "tmp-repo");
        stub.Respond("rev-parse", Sha + "\n");
        var target = new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https);

        // Act
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);
        var commit = await client.FetchTagAsync(target, repository, "release/1.2.3", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Sha, commit);
        var invocations = stub.ReadInvocations();
        var init = invocations[0];
        Assert.Equal("init", init.Command);
        Assert.EndsWith(
            $"--literal-pathspecs init -q --template={Path.Combine(workRoot, "empty-template")} -- {repository}",
            init.Arguments, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(workRoot, "empty-template")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(workRoot, "empty-template")));
        var fetch = invocations[1];
        Assert.StartsWith($"-C {repository} -c protocol.allow=never ", fetch.Arguments, StringComparison.Ordinal);
        Assert.EndsWith(
            "--literal-pathspecs fetch --depth=1 --no-tags --no-recurse-submodules --no-write-fetch-head -- " +
            "https://example.invalid/r.git refs/tags/release/1.2.3:refs/tags/release/1.2.3",
            fetch.Arguments, StringComparison.Ordinal);
        Assert.Contains("rev-parse --verify --quiet refs/tags/release/1.2.3", invocations[2].Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a fetched tag that does not resolve to a commit id is an error rather than a
    ///     guess.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_FetchTag_RevParseWithoutCommit_Throws()
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        stub.Respond("rev-parse", "not-a-commit\n");

        // Act / Assert
        await Assert.ThrowsAsync<GitRemoteException>(() => client.FetchTagAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), NewFolder(), "v1.0.0",
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a tag name that could be read as an option or inject into a refspec is refused
    ///     before git runs.
    /// </summary>
    [Theory]
    [InlineData("-oProxyCommand=calc")]
    [InlineData("--upload-pack=calc")]
    [InlineData("a b")]
    [InlineData("v1..2")]
    [InlineData("v1:other")]
    [InlineData("v1$(calc)")]
    [InlineData("")]
    [InlineData("a//b")]
    [InlineData(".hidden")]
    [InlineData("a/.b")]
    [InlineData("x.lock")]
    [InlineData("a/x.lock/b")]
    [InlineData("trailing/")]
    [InlineData("/leading")]
    [InlineData("ends.")]
    public async Task GitRemoteClient_FetchTag_UnsafeTagName_IsRefusedWithoutRunningGit(string tag)
    {
        // Arrange
        var (client, stub, _) = CreateClient();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.FetchTagAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), NewFolder(), tag,
            TestContext.Current.CancellationToken));
        Assert.Empty(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test that the tree listing runs with literal pathspecs, accepts only a full object id and
    ///     at least one path, and parses the NUL-separated listing into modes, types, sizes and paths.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTree_UsesLiteralPathsAndParsesEntries()
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        var repository = NewFolder();
        stub.Respond(
            "ls-tree",
            $"100644 blob {Sha}     12\t.github/agents/a.md\0120000 blob {OtherSha}      5\t.github/agents/link\0" +
            $"160000 commit {Sha}      -\t.github/agents/sub\0garbage\0");

        // Act
        var entries = await client.ListTreeAsync(
            repository, Sha, [".github/agents", "release-notes.md"], recursive: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, entries.Count);
        Assert.Equal(new GitTreeEntry("100644", "blob", Sha, 12, ".github/agents/a.md"), entries[0]);
        Assert.Equal("120000", entries[1].Mode);
        Assert.Equal("commit", entries[2].Type);
        Assert.Null(entries[2].Size);
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.EndsWith(
            $"--literal-pathspecs ls-tree -r -z --full-tree -l {Sha} -- .github/agents release-notes.md",
            invocation.Arguments, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => client.ListTreeAsync(
            repository, "HEAD", [".github"], recursive: false, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that an empty path list is refused before git starts, because an empty pathspec list
    ///     would make <c>ls-tree</c> enumerate the whole tree.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTree_EmptyPathList_IsRefusedWithoutRunningGit()
    {
        // Arrange: a valid commit id and no paths
        var (client, stub, _) = CreateClient();

        // Act
        var failure = await Assert.ThrowsAsync<ArgumentException>(() => client.ListTreeAsync(
            NewFolder(), Sha, [], recursive: true, TestContext.Current.CancellationToken));

        // Assert: refused because of the paths, and git never ran
        Assert.Equal("paths", failure.ParamName);
        Assert.Empty(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test that blobs are read with <c>cat-file --batch</c> and no checkout: the arguments are
    ///     exactly the hardened ones, the object ids are the requests, and the bytes of each answer
    ///     reach the destination the caller opened for that entry.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ReadBlobs_UsesCatFileBatchAndDeliversRawBytes()
    {
        // Arrange: two blobs, answered in order
        var (client, stub, workRoot) = CreateClient();
        stub.Respond("cat-file", $"{Sha} blob 5\nhello\n{OtherSha} blob 0\n\n");
        var blobs = new[] { Blob(Sha, 5, ".github/agents/a.md"), Blob(OtherSha, 0, "release-notes.md") };
        var written = new Dictionary<string, MemoryStream>();

        // Act
        await client.ReadBlobsAsync(
            NewFolder(), blobs, entry => written[entry.Path] = new MemoryStream(), 1024,
            TestContext.Current.CancellationToken);

        // Assert: the bytes of each answer, and a command that is cat-file --batch and nothing else
        Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(written[".github/agents/a.md"].ToArray()));
        Assert.Empty(written["release-notes.md"].ToArray());
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.EndsWith("--literal-pathspecs cat-file --batch", invocation.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("checkout", invocation.Arguments, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(Path.GetFileName(workRoot), "empty-hooks"), invocation.Arguments, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that blobs that are not well-formed, no blobs at all, and sizes over the budget are
    ///     refused before git starts.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ReadBlobs_InvalidEntriesOrOverBudget_AreRefusedWithoutRunningGit()
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        var repository = NewFolder();
        var token = TestContext.Current.CancellationToken;

        // Act / Assert: no entries, a tree, a bad object id, an unknown size
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadBlobsAsync(repository, [], _ => Stream.Null, 10, token));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadBlobsAsync(
            repository, [new GitTreeEntry("040000", "tree", Sha, null, "a")], _ => Stream.Null, 10, token));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadBlobsAsync(
            repository, [Blob("--orphan", 1, "a")], _ => Stream.Null, 10, token));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadBlobsAsync(
            repository, [new GitTreeEntry("100644", "blob", Sha, null, "a")], _ => Stream.Null, 10, token));

        // Act: sizes that together exceed the budget, one byte over
        var tooLarge = await Assert.ThrowsAsync<GitRemoteException>(() => client.ReadBlobsAsync(
            repository, [Blob(Sha, 6, "a"), Blob(OtherSha, 5, "b")], _ => Stream.Null, 10, token));

        // Assert
        Assert.Equal(GitFailureKind.OutputTooLarge, tooLarge.Kind);
        Assert.Empty(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test that an answer that is not the blob that was asked for - another object id, a missing
    ///     object, another size, data after the last answer or a header without its data - is refused.
    /// </summary>
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234568 blob 5\nhello\n")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 missing\n")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 blob 6\nhello!\n")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 tree 5\nhello\n")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 blob 5\nhello\nextra\n")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 blob 5\nhelloX")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 blob 5\nhel")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 blob five\nhello\n")]
    public async Task GitRemoteClient_ReadBlobs_AnswerThatIsNotTheRequestedBlob_IsRefused(string answer)
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        stub.Respond("cat-file", answer);

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ReadBlobsAsync(
            NewFolder(), [Blob(Sha, 5, "a")], _ => new MemoryStream(), 1024, TestContext.Current.CancellationToken));

        // Assert: a refusal that names no object, path or address
        Assert.Equal(GitFailureKind.Other, failure.Kind);
        Assert.DoesNotContain(Sha, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that git failing while reading blobs is classified and worded from a fixed sentence.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ReadBlobs_GitFails_IsClassifiedWithFixedMessage()
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        stub.Respond("cat-file", standardError: "fatal: not a git repository: /private/place/repo", exitCode: 128);

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ReadBlobsAsync(
            NewFolder(), [Blob(Sha, 5, "a")], _ => new MemoryStream(), 1024, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("git cat-file failed (exit code 128).", failure.Message);
    }

    /// <summary>
    ///     Test that reading blobs that outlives its time limit is stopped and reported as timed out.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ReadBlobs_ExceedsTimeLimit_KillsProcessAndReportsTimedOut()
    {
        // Arrange: git that hangs for 30 seconds, against a limit of one second
        var stub = NewStub();
        stub.Respond("cat-file", sleepSeconds: 30);
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder(), FetchTimeout: TimeSpan.FromSeconds(1)));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ReadBlobsAsync(
            NewFolder(), [Blob(Sha, 5, "a")], _ => new MemoryStream(), 1024, TestContext.Current.CancellationToken));

        // Assert: timed out, and the process never got to finish
        Assert.Equal(GitFailureKind.TimedOut, failure.Kind);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(stub.WroteFinishedMarker());
    }

    /// <summary>
    ///     Test that a command that outlives its time limit is stopped (the process is killed before it
    ///     finishes) and reported as timed out.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_ExceedsTimeLimit_KillsProcessAndReportsTimedOut()
    {
        // Arrange: git that hangs for 30 seconds, against a limit of one second
        var stub = NewStub();
        stub.Respond("ls-remote", sleepSeconds: 30);
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder(), ListTimeout: TimeSpan.FromSeconds(1)));
        var stopwatch = Stopwatch.StartNew();

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert: reported promptly, and the process never got to finish
        Assert.Equal(GitFailureKind.TimedOut, failure.Kind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20));
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(stub.WroteFinishedMarker());
    }

    /// <summary>
    ///     Test that cancelling stops the command promptly and kills the process.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_Cancelled_KillsProcessAndThrowsOperationCanceled()
    {
        // Arrange
        var stub = NewStub();
        stub.Respond("ls-remote", sleepSeconds: 30);
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder()));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*", cancellation.Token));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20));
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(stub.WroteFinishedMarker());
    }

    /// <summary>
    ///     Test that when a process that exceeded its time limit cannot be confirmed to have stopped
    ///     (the termination does nothing, so it keeps running), the failure is surfaced as
    ///     <c>ProcessNotStopped</c> instead of being swallowed as an ordinary timeout, and the call
    ///     still returns promptly.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_ProcessCannotBeStopped_ReportsProcessNotStoppedAfterTimeout()
    {
        // Arrange: git hangs for 30 seconds; the kill seam records the process but leaves it running
        var stub = NewStub();
        stub.Respond("ls-remote", sleepSeconds: 30);
        var started = new List<int>();
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => stub.Path, NewFolder(), ListTimeout: TimeSpan.FromSeconds(1))
        {
            KillProcessTree = process => started.Add(process.Id),
            StopTimeout = TimeSpan.FromSeconds(1)
        });
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Act
            var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
                new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
                TestContext.Current.CancellationToken));

            // Assert: the kill was attempted, termination was not confirmed, and the message is fixed
            Assert.Equal(GitFailureKind.ProcessNotStopped, failure.Kind);
            Assert.Single(started);
            Assert.Equal(
                "git ls-remote was stopped but could not be confirmed to have ended, so a git process may still be running.",
                failure.Message);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20));
        }
        finally
        {
            StopForReal(started);
        }
    }

    /// <summary>
    ///     Test that a cancellation whose process cannot be confirmed to have stopped is reported as
    ///     <c>ProcessNotStopped</c> too - the caller must learn that something may still be running -
    ///     rather than as a plain cancellation, and that a kill that throws is surfaced the same way.
    /// </summary>
    [Theory]
    [InlineData("does-nothing")]
    [InlineData("throws-access-denied")]
    public async Task GitRemoteClient_ListTags_CancelledAndProcessCannotBeStopped_ReportsProcessNotStopped(string kill)
    {
        // Arrange: git hangs; the kill seam either does nothing or fails with an access error
        var stub = NewStub();
        stub.Respond("ls-remote", sleepSeconds: 30);
        var started = new List<int>();
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder())
        {
            KillProcessTree = process =>
            {
                started.Add(process.Id);
                if (kill == "throws-access-denied")
                {
                    throw new System.ComponentModel.Win32Exception(5, "Access is denied");
                }
            },
            StopTimeout = TimeSpan.FromSeconds(1)
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));

        try
        {
            // Act
            var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
                new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*", cancellation.Token));

            // Assert
            Assert.Equal(GitFailureKind.ProcessNotStopped, failure.Kind);
            Assert.Single(started);
        }
        finally
        {
            StopForReal(started);
        }
    }

    /// <summary>
    ///     Test that the real termination (no test seam) is what stops a hanging process on timeout:
    ///     it ends, nothing is reported beyond the timeout, and the call is not held up.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_ExceedsTimeLimit_ConfirmsTerminationAndReportsOnlyTimeout()
    {
        // Arrange
        var stub = NewStub();
        stub.Respond("ls-remote", sleepSeconds: 30);
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder(), ListTimeout: TimeSpan.FromSeconds(1)));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert: a plain timeout, not a failure to stop
        Assert.Equal(GitFailureKind.TimedOut, failure.Kind);
        Assert.Equal("git ls-remote did not finish within 1 seconds and was stopped.", failure.Message);
    }

    /// <summary>
    ///     Test that more standard output than the cap is refused, not buffered without limit.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_OutputOverCap_ThrowsOutputTooLarge()
    {
        // Arrange: a little over 8 MB of well-formed lines
        var (client, stub, _) = CreateClient();
        var line = $"{Sha}\trefs/tags/v1.0.0\n";
        stub.Respond("ls-remote", string.Concat(Enumerable.Repeat(line, (GitRemoteClient.MaxListOutputBytes / line.Length) + 10)));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(GitFailureKind.OutputTooLarge, failure.Kind);
    }

    /// <summary>
    ///     Test that the output cap counts bytes, not characters: text whose character count is well
    ///     under the cap but whose UTF-8 size is over it is refused.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_MultiByteOutputOverCapInBytes_ThrowsOutputTooLarge()
    {
        // Arrange: 5,000,000 two-byte characters - under 8 MB as characters, 10 MB as bytes
        var (client, stub, _) = CreateClient();
        var characters = 5_000_000;
        Assert.True(characters < GitRemoteClient.MaxListOutputBytes);
        stub.Respond("ls-remote", new string('\u00e9', characters));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(GitFailureKind.OutputTooLarge, failure.Kind);
    }

    /// <summary>
    ///     Test the boundary of the output cap: output of exactly the cap in bytes is accepted, one
    ///     byte more is refused.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task GitRemoteClient_ListTags_OutputAtAndOneByteOverCap_IsAcceptedAndRefused(int extraBytes, bool expectRefusal)
    {
        // Arrange: one tag line, then ignorable filler up to exactly the cap (plus the extra bytes)
        var (client, stub, _) = CreateClient();
        var line = $"{Sha}\trefs/tags/v1.0.0\n";
        stub.Respond("ls-remote", line + new string('x', GitRemoteClient.MaxListOutputBytes - line.Length + extraBytes));

        // Act
        var outcome = await Record.ExceptionAsync(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert
        if (expectRefusal)
        {
            Assert.Equal(GitFailureKind.OutputTooLarge, Assert.IsType<GitRemoteException>(outcome).Kind);
        }
        else
        {
            Assert.Null(outcome);
        }
    }

    /// <summary>
    ///     Test that a failure message is a fixed sentence: a secret in git's error text, the address,
    ///     the host, a local path and a very long error never reach the exception, and the logs never
    ///     hold the address or arguments.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_Failure_MessageIsFixedBoundedAndLogsAreSecretFree()
    {
        // Arrange: a noisy failure whose first lines carry secrets, then 200 KB of text
        var stub = NewStub();
        stub.Respond(
            "ls-remote",
            standardError: "fatal: password=sentinel-pw failed\nremote: Authorization: Basic c2VudGluZWxzZWNyZXQ=\n" +
                           new string('x', 200_000) + "\n",
            exitCode: 128);
        var logger = new ListLogger();
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder(), Logger: logger));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/private/repo.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert: a fixed sentence with the exit code, and nothing git printed
        Assert.Equal("git ls-remote failed (exit code 128).", failure.Message);
        var logged = string.Join("\n", logger.Snapshot());
        Assert.NotEmpty(logger.Snapshot());
        Assert.DoesNotContain("example.invalid", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("sentinel-pw", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("--literal-pathspecs", logged, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that git's error text, which commonly repeats the remote address, the host or a local
    ///     path, never reaches the message of the exception, whichever kind it is classified as; each
    ///     kind has its own fixed message.
    /// </summary>
    [Theory]
    [InlineData("fatal: Authentication failed for 'https://user@host.example.invalid/private/repo.git/'", "AuthenticationRequired", "needs a sign-in")]
    [InlineData("fatal: repository 'https://host.example.invalid/private/repo.git/' not found", "NotFound", "did not find the repository")]
    [InlineData("fatal: '/srv/private/repo.git' does not appear to be a git repository", "NotFound", "did not find the repository")]
    [InlineData("fatal: unable to access 'https://host.example.invalid/private/repo.git/': Could not resolve host: host.example.invalid", "Unreachable", "could not reach")]
    [InlineData("Host key verification failed. (host.example.invalid)", "HostKeyUnverified", "host key")]
    [InlineData("fatal: detected dubious ownership in repository at '/srv/private/repo.git'", "UnsafeRepository", "owned by another user")]
    [InlineData("fatal: something unforeseen about https://host.example.invalid/private/repo.git in /srv/private", "Other", "failed (exit code 128)")]
    public async Task GitRemoteClient_Failure_MessageNeverRepeatsAddressHostOrPath(string stderr, string kindName, string messagePart)
    {
        // Arrange
        var stub = NewStub();
        stub.Respond("ls-remote", standardError: stderr, exitCode: 128);
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder()));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://host.example.invalid/private/repo.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert: classified, worded from a fixed sentence, and free of address, host and paths
        Assert.Equal(Enum.Parse<GitFailureKind>(kindName), failure.Kind);
        Assert.Contains(messagePart, failure.Message, StringComparison.OrdinalIgnoreCase);
        foreach (var leaked in new[] { "example.invalid", "host.", "private", "/srv", "repo.git", "https://", "user@" })
        {
            Assert.DoesNotContain(leaked, failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    ///     Test the classification of what git says when a command fails.
    /// </summary>
    [Theory]
    [InlineData("remote: Invalid username or password.\nfatal: Authentication failed for 'https://h/r.git/'", "AuthenticationRequired")]
    [InlineData("fatal: could not read Username for 'https://h': terminal prompts disabled", "AuthenticationRequired")]
    [InlineData("fatal: unable to get password from user", "AuthenticationRequired")]
    [InlineData("git@h: Permission denied (publickey).\nfatal: Could not read from remote repository.", "AuthenticationRequired")]
    [InlineData("fatal: unable to access 'https://h/r.git/': The requested URL returned error: 403", "AuthenticationRequired")]
    [InlineData("remote: Repository not found.\nfatal: repository 'https://h/r.git/' not found", "NotFound")]
    [InlineData("fatal: '/x/y' does not appear to be a git repository", "NotFound")]
    [InlineData("fatal: couldn't find remote ref refs/tags/v1", "NotFound")]
    [InlineData("fatal: unable to access 'https://h/r.git/': Could not resolve host: h", "Unreachable")]
    [InlineData("ssh: connect to host h port 22: Connection refused", "Unreachable")]
    [InlineData("Host key verification failed.\nfatal: Could not read from remote repository.", "HostKeyUnverified")]
    [InlineData("fatal: detected dubious ownership in repository at '/x'", "UnsafeRepository")]
    [InlineData("fatal: something unforeseen", "Other")]
    [InlineData("", "Other")]
    public async Task GitRemoteClient_Failure_IsClassifiedFromGitsMessage(string errorText, string expectedName)
    {
        // Arrange
        var stub = NewStub();
        stub.Respond("ls-remote", standardError: errorText, exitCode: 128);
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder()));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(Enum.Parse<GitFailureKind>(expectedName), failure.Kind);
    }

    /// <summary>
    ///     Test that a git executable that cannot be started is reported with a message that points to
    ///     the Git setting.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_GitExecutableMissing_ReportsGitNotFound()
    {
        // Arrange
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => Path.Combine(NewFolder(), "no-such-git"), NewFolder()));

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(GitFailureKind.GitNotFound, failure.Kind);
        Assert.Contains("Settings", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a tag name the server reports that is not a legal, safe ref name is dropped (with
    ///     its peeled line) and never throws, while good names around it are kept.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_ListTags_HostileServerTagNames_AreDropped()
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        var hostile = new[]
        {
            "a//b", ".hidden", "x.lock", "a/.b", "a/b.lock/c", "trailing/", "/leading", "a..b", "ends.", "-dash",
            "has space", "colon:name", "a@{b}", new string('x', 256)
        };
        var output = new System.Text.StringBuilder();
        output.Append($"{Sha}\trefs/tags/v1.0.0\n");
        foreach (var name in hostile)
        {
            output.Append($"{OtherSha}\trefs/tags/{name}\n{Sha}\trefs/tags/{name}^{{}}\n");
        }

        output.Append($"{OtherSha}\trefs/tags/release/v2.0.0\n");
        stub.Respond("ls-remote", output.ToString());

        // Act
        var tags = await client.ListTagsAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), "refs/tags/*",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["release/v2.0.0", "v1.0.0"], tags.Select(tag => tag.Name));
    }

    /// <summary>
    ///     Test that legal tag names, including nested ones, are still accepted for a fetch.
    /// </summary>
    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("release/1.2.3-beta.1")]
    [InlineData("pkg+build_1/v1.0.0")]
    public async Task GitRemoteClient_FetchTag_LegalTagName_IsFetched(string tag)
    {
        // Arrange
        var (client, stub, _) = CreateClient();
        stub.Respond("rev-parse", Sha + "\n");

        // Act
        var commit = await client.FetchTagAsync(
            new GitRemoteTarget("https://example.invalid/r.git", GitTransport.Https), NewFolder(), tag,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Sha, commit);
        Assert.True(GitRemoteClient.IsSafeTagName(tag));
    }

    /// <summary>
    ///     Test that the version of git is read from the text of different git builds, and that
    ///     anything else is not a version.
    /// </summary>
    [Theory]
    [InlineData("git version 2.51.0.windows.2", "2.51.0")]
    [InlineData("git version 2.39.5 (Apple Git-154)\n", "2.39.5")]
    [InlineData("git version 2.29", "2.29.0")]
    [InlineData("git version 2.20.1\r\n", "2.20.1")]
    [InlineData("git version 3.0.0-rc1", "3.0.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("hello 2.30.0", null)]
    [InlineData("git version two", null)]
    public void GitRemoteClient_ParseVersion_ReadsKnownFormsAndRejectsOthers(string? output, string? expected) =>
        Assert.Equal(expected, GitRemoteClient.ParseVersion(output)?.ToString());

    /// <summary>
    ///     Test that a git older than the minimum is refused with an actionable message before any
    ///     other git command runs, and that the refusal is not remembered, so a git that was updated
    ///     is accepted by the next check.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_EnsureSupportedVersion_OldGit_IsRefusedWithActionableMessageAndNotRemembered()
    {
        // Arrange
        var (client, stub, workRoot) = CreateClient();
        stub.Respond("version", "git version 2.20.1\n");

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(
            () => client.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken));

        // Assert: refused, saying what to do, after exactly one hardened `git --version`
        Assert.Equal(GitFailureKind.GitTooOld, failure.Kind);
        Assert.Contains("Git 2.29 or newer", failure.Message, StringComparison.Ordinal);
        Assert.Contains("2.20.1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Settings > Git executable", failure.Message, StringComparison.Ordinal);
        var invocation = Assert.Single(stub.ReadInvocations());
        Assert.Equal("version", invocation.Command);
        Assert.EndsWith("--literal-pathspecs --version", invocation.Arguments, StringComparison.Ordinal);
        Assert.Equal("0", invocation.Environment["GIT_TERMINAL_PROMPT"]);
        Assert.EndsWith(
            Path.Combine(Path.GetFileName(workRoot), "neutral"),
            invocation.WorkingDirectory.TrimEnd('\\', '/'),
            StringComparison.Ordinal);

        // Act: git is updated; the old answer was not cached
        stub.Respond("version", "git version 2.43.0\n");
        await client.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, stub.ReadInvocations().Count(i => i.Command == "version"));
    }

    /// <summary>
    ///     Test that a new enough git passes, and that <c>git --version</c> runs only once for the
    ///     same executable, however many clients ask.
    /// </summary>
    [Theory]
    [InlineData("git version 2.29.0")]
    [InlineData("git version 2.51.0.windows.2")]
    public async Task GitRemoteClient_EnsureSupportedVersion_NewGit_PassesAndRunsOncePerExecutable(string versionText)
    {
        // Arrange
        var stub = NewStub().Respond("version", versionText + "\n");
        var first = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder()));
        var second = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder()));

        // Act
        await first.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken);
        await first.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken);
        await second.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(stub.ReadInvocations());
    }

    /// <summary>
    ///     Test that a version text that cannot be read does not block git (a warning is logged), and
    ///     that a git that cannot be run is reported and not remembered.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_EnsureSupportedVersion_UnreadableVersionIsNotBlocked_AndMissingGitIsReported()
    {
        // Arrange
        var stub = NewStub().Respond("version", "something unexpected\n");
        var logger = new ListLogger();
        var client = new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder(), Logger: logger));
        var missing = new GitRemoteClient(new GitRemoteOptions(() => Path.Combine(NewFolder(), "no-such-git"), NewFolder()));

        // Act
        await client.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<GitRemoteException>(
            () => missing.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(logger.Snapshot(), line => line.Contains("could not be read", StringComparison.Ordinal));
        Assert.Equal(GitFailureKind.GitNotFound, failure.Kind);
    }

    /// <summary>
    ///     Test that callers that ask for the version check of the same executable at the same time
    ///     share one <c>git --version</c> run, instead of each running their own while the first is
    ///     still in flight.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_EnsureSupportedVersion_ConcurrentCallers_RunGitVersionOnce()
    {
        // Arrange: a git whose version answer takes a moment, and several clients over it
        var stub = NewStub().Respond("version", "git version 2.43.0\n", sleepSeconds: 1);
        var clients = Enumerable.Range(0, 8)
            .Select(_ => new GitRemoteClient(new GitRemoteOptions(() => stub.Path, NewFolder())))
            .ToList();

        // Act: all of them ask at once
        await Task.WhenAll(clients.Select(
            client => Task.Run(() => client.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        // Assert: one invocation for all callers
        Assert.Single(stub.ReadInvocations());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var stub in _stubs)
        {
            stub.Delete();
        }

        foreach (var folder in _folders)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    ///     Creates a blob entry as a tree listing would report it.
    /// </summary>
    /// <param name="sha">The object id.</param>
    /// <param name="size">The size in bytes.</param>
    /// <param name="path">The repository path.</param>
    /// <returns>The entry.</returns>
    private static GitTreeEntry Blob(string sha, long size, string path) => new("100644", "blob", sha, size, path);

    /// <summary>
    ///     Kills processes that a test left running on purpose (the process of a termination that the
    ///     test made fail), so no hanging stub outlives the test.
    /// </summary>
    /// <param name="processIds">The process ids recorded by the test seam.</param>
    private static void StopForReal(IEnumerable<int> processIds)
    {
        foreach (var id in processIds)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process has already ended
            }
        }
    }

    /// <summary>
    ///     Creates a client over a new stub and work folder.
    /// </summary>
    private (GitRemoteClient Client, GitRemoteStub Stub, string WorkRoot) CreateClient(bool interactive = false)
    {
        var stub = NewStub();
        var workRoot = NewFolder();
        return (new GitRemoteClient(new GitRemoteOptions(() => stub.Path, workRoot, interactive)), stub, workRoot);
    }

    /// <summary>
    ///     Creates a stub that is deleted after the test.
    /// </summary>
    private GitRemoteStub NewStub()
    {
        var stub = GitRemoteStub.Create();
        _stubs.Add(stub);
        return stub;
    }

    /// <summary>
    ///     Creates a folder path that is removed after the test.
    /// </summary>
    private string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_git_remote_test_" + Guid.NewGuid().ToString("N"));
        _folders.Add(path);
        return path;
    }

    /// <summary>
    ///     Sets environment variables of this process for the duration of a test and restores them.
    /// </summary>
    private sealed class TemporaryEnvironment : IDisposable
    {
        /// <summary>
        ///     The original values by name.
        /// </summary>
        private readonly Dictionary<string, string?> _original = [];

        /// <summary>
        ///     Initializes the scope.
        /// </summary>
        /// <param name="variables">The variables to set; a <see langword="null"/> value removes one.</param>
        public TemporaryEnvironment(params (string Name, string? Value)[] variables)
        {
            foreach (var (name, value) in variables)
            {
                _original[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var (name, value) in _original)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
