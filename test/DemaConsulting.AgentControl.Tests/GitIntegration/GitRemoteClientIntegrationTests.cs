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

using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Tests.AgentPackageManagement;

// cspell:ignore ufeffbom
namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Integration tests of <see cref="GitRemoteClient"/> against the real git executable and local
///     bare repositories (no network, no credentials): listing, the shallow fetch of one tag, the tree
///     listing and the raw reading of blobs, and the protections that must hold in real git - the transport
///     allow-list, and no trace output in messages. Skipped when git is not installed.
/// </summary>
[Collection("RealProcess")]
public sealed class GitRemoteClientIntegrationTests
{
    /// <summary>
    ///     Test the whole client sequence against a real repository: list the tags (an annotated tag
    ///     resolves to its commit), create the repository, fetch exactly one tag shallow, read its
    ///     tree, and read the bytes of one blob without a checkout.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_RealGit_ListInitFetchListTreeReadBlobs_Sequence()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var first = fixture.Commit(Files("one"), "one");
        fixture.Tag("v1.0.0", annotated: true);
        fixture.Commit(Files("two"), "two");
        fixture.Tag("v1.1.0");
        fixture.Push();
        var client = CreateClient(fixture);
        var target = new GitRemoteTarget(fixture.RemotePath, GitTransport.File);
        var repository = Path.Combine(fixture.Root, "fetched");

        // Act: list
        var tags = await client.ListTagsAsync(target, "refs/tags/v*", TestContext.Current.CancellationToken);

        // Assert: the annotated tag resolves to the commit, not the tag object
        var annotated = tags.Single(tag => tag.Name == "v1.0.0");
        Assert.Equal(first, annotated.CommitSha);
        Assert.NotEqual(annotated.CommitSha, annotated.ObjectSha);
        Assert.Equal(2, tags.Count);

        // Act: fetch one tag
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);
        var commit = await client.FetchTagAsync(target, repository, "v1.0.0", TestContext.Current.CancellationToken);

        // Assert: the commit is the tag's, and only that tag (shallow, no other tags) is there
        Assert.Equal(first, commit);
        Assert.Equal("refs/tags/v1.0.0", fixture.Git(repository, "for-each-ref", "--format=%(refname)").Trim());
        Assert.True(File.Exists(Path.Combine(repository, ".git", "shallow")));

        // Act: list the tree, read the blobs of one path
        var entries = await client.ListTreeAsync(
            repository, commit, [".github/agents", "release-notes.md"], recursive: true, TestContext.Current.CancellationToken);
        var agentFile = entries.Single(entry => entry.Path == ".github/agents/copilot.md");
        using var content = new MemoryStream();
        await client.ReadBlobsAsync(repository, [agentFile], _ => new NonClosingStream(content), 1024, TestContext.Current.CancellationToken);

        // Assert: both paths were listed, the blob's bytes are the file's, and nothing was written to the work folder
        Assert.Equal(
            [".github/agents/copilot.md", "release-notes.md"],
            entries.Select(entry => entry.Path).Order());
        Assert.All(entries, entry => Assert.Equal("100644", entry.Mode));
        Assert.Equal("one", System.Text.Encoding.UTF8.GetString(content.ToArray()));
        Assert.False(File.Exists(Path.Combine(repository, ".github", "agents", "copilot.md")));
        Assert.False(File.Exists(Path.Combine(repository, "release-notes.md")));
        Assert.False(File.Exists(Path.Combine(repository, "README.md")));
    }

    /// <summary>
    ///     Test that real git hands over the bytes of a blob exactly as they are in the commit - CRLF
    ///     and LF line endings, a lone carriage return, a NUL byte and a byte-order mark - even when the
    ///     user's configuration asks for line-ending conversion and the repository's attributes ask for
    ///     conversion and identifier expansion, because no checkout (and so no conversion) is involved.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_RealGit_ReadBlobs_DeliversExactBlobBytesDespiteConversionSettings()
    {
        // Arrange: binary-ish content committed first, then attributes that would convert it on a checkout
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var content = "crlf\r\nlf\nlone\rcr\0nul $Id$ \ufeffbom\r\n";
        fixture.Commit(new Dictionary<string, string> { ["crlf-content.txt"] = content }, "content");
        fixture.Commit(
            new Dictionary<string, string> { [".gitattributes"] = "* text eol=crlf ident\n" }, "attributes");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var config = Path.Combine(fixture.Root, "autocrlf.gitconfig");
        await File.WriteAllTextAsync(config, "[core]\n\tautocrlf = true\n\teol = crlf\n", TestContext.Current.CancellationToken);
        var environment = new Dictionary<string, string?>(fixture.IsolationEnvironment) { ["GIT_CONFIG_GLOBAL"] = config };
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => "git", Path.Combine(fixture.Root, "client-work"), ExtraEnvironment: environment,
            ListTimeout: TimeSpan.FromSeconds(60), FetchTimeout: TimeSpan.FromSeconds(120)));
        var target = new GitRemoteTarget(fixture.RemotePath, GitTransport.File);
        var repository = Path.Combine(fixture.Root, "fetched");
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);
        var commit = await client.FetchTagAsync(target, repository, "v1.0.0", TestContext.Current.CancellationToken);
        var entry = (await client.ListTreeAsync(repository, commit, ["crlf-content.txt"], recursive: true, TestContext.Current.CancellationToken)).Single();

        // Act
        using var received = new MemoryStream();
        await client.ReadBlobsAsync(repository, [entry], _ => new NonClosingStream(received), 1024, TestContext.Current.CancellationToken);

        // Assert: byte for byte what the commit holds (the blob git itself reports), not converted or expanded
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(content), received.ToArray());
        Assert.Equal(entry.Size!.Value, received.Length);
    }

    /// <summary>
    ///     A stream wrapper that forwards writes but does not close the stream underneath, so a test
    ///     can read what was written after the client disposes its destination.
    /// </summary>
    /// <param name="inner">The stream that receives the bytes and stays open.</param>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => inner.Length;

        /// <inheritdoc />
        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Flush() => inner.Flush();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>
    ///     Test that git itself enforces the transport allow-list: an address of another transport than
    ///     the one allowed is refused (a local path given as https), and a remote-helper address that
    ///     reached the client through its internal API is refused too, so nothing is executed.
    /// </summary>
    [Theory]
    [InlineData("local-as-https")]
    [InlineData("ext-helper")]
    public async Task GitRemoteClient_RealGit_TransportNotAllowed_IsRefusedByGit(string kind)
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Files("one"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var proof = Path.Combine(fixture.Root, "ext-ran.txt");
        var client = CreateClient(fixture);
        var target = kind == "local-as-https"
            ? new GitRemoteTarget(fixture.RemotePath, GitTransport.Https)
            : new GitRemoteTarget($"ext::git-upload-pack {fixture.RemotePath.Replace('\\', '/')} --stat {proof.Replace('\\', '/')}", GitTransport.Https);

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(
            () => client.ListTagsAsync(target, "refs/tags/*", TestContext.Current.CancellationToken));

        // Assert: git refused the transport (a failure with a fixed message that repeats neither the address nor
        // git's text), and nothing was run
        Assert.Equal(GitFailureKind.Other, failure.Kind);
        Assert.Equal("git ls-remote failed (exit code 128).", failure.Message);
        Assert.DoesNotContain("ext::", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(proof));
    }

    /// <summary>
    ///     Test that trace variables in the parent environment produce no trace text in a failure
    ///     message, because they are removed from the child's environment.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_RealGit_TraceVariablesInParent_ProduceNoTraceOutput()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var names = new[] { "GIT_TRACE", "GIT_TRACE_SETUP", "GIT_CURL_VERBOSE" };
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        foreach (var name in names)
        {
            Environment.SetEnvironmentVariable(name, "1");
        }

        try
        {
            var client = CreateClient(fixture);
            var missing = new GitRemoteTarget(Path.Combine(fixture.Root, "missing.git"), GitTransport.File);

            // Act
            var failure = await Assert.ThrowsAsync<GitRemoteException>(
                () => client.ListTagsAsync(missing, "refs/tags/*", TestContext.Current.CancellationToken));

            // Assert: a plain not-found message, with no trace lines
            Assert.Equal(GitFailureKind.NotFound, failure.Kind);
            Assert.DoesNotContain("trace", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var (name, value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    /// <summary>
    ///     Test that a repository created by the client has no hooks even when the user's environment
    ///     names a template directory that installs one into every new repository: plain git with that
    ///     template does install it (the control), the client's repository does not get it.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_RealGit_InitWithHostileTemplate_CreatesRepositoryWithoutHooks()
    {
        // Arrange: a template directory with a hook, named by the user's environment
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var template = Path.Combine(fixture.Root, "hostile-template");
        Directory.CreateDirectory(Path.Combine(template, "hooks"));
        await File.WriteAllTextAsync(
            Path.Combine(template, "hooks", "post-checkout"), "#!/bin/sh\necho ran\n", TestContext.Current.CancellationToken);
        var environment = new Dictionary<string, string?>(fixture.IsolationEnvironment) { ["GIT_TEMPLATE_DIR"] = template };
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => "git", Path.Combine(fixture.Root, "client-work"), ExtraEnvironment: environment,
            FetchTimeout: TimeSpan.FromSeconds(120)));

        // Control: plain git given that template installs the hook, so the test is meaningful
        var control = Path.Combine(fixture.Root, "control");
        fixture.Git(fixture.Root, "init", "-q", "--template=" + template, control);
        Assert.True(File.Exists(Path.Combine(control, ".git", "hooks", "post-checkout")));

        // Act
        var repository = Path.Combine(fixture.Root, "fetched");
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);

        // Assert: a repository without the hook (it may have no hooks folder at all)
        var hooks = Path.Combine(repository, ".git", "hooks");
        Assert.False(Directory.Exists(hooks) && Directory.EnumerateFileSystemEntries(hooks).Any());
        Assert.True(Directory.Exists(Path.Combine(repository, ".git")));
    }

    /// <summary>
    ///     Test that fetching a tag that does not exist is reported as not found.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_RealGit_FetchMissingTag_ReportsNotFound()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Files("one"), "one");
        fixture.Push();
        var client = CreateClient(fixture);
        var repository = Path.Combine(fixture.Root, "fetched");
        await client.InitRepositoryAsync(repository, TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<GitRemoteException>(() => client.FetchTagAsync(
            new GitRemoteTarget(fixture.RemotePath, GitTransport.File), repository, "v9.9.9",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(GitFailureKind.NotFound, failure.Kind);
    }

    /// <summary>
    ///     Test that the real git answers the hardened <c>--version</c> command the version check runs
    ///     (the command-scoped settings and the literal-pathspecs option do not stop it) and is
    ///     accepted.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_RealGit_EnsureSupportedVersion_AcceptsTheInstalledGit()
    {
        // Arrange: the full path, so the once-per-executable memory of other tests (which use "git") is not involved
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var git = FindGitOnPath();
        var client = new GitRemoteClient(new GitRemoteOptions(
            () => git, Path.Combine(fixture.Root, "client-work"), ExtraEnvironment: fixture.IsolationEnvironment));

        // Act
        var failure = await Record.ExceptionAsync(() => client.EnsureSupportedVersionAsync(TestContext.Current.CancellationToken));

        // Assert: the installed git is new enough (the project requires it), and the command ran
        Assert.Null(failure);
    }

    /// <summary>
    ///     Creates a client for a fixture with its own work folder, the fixture's isolation and no
    ///     prompts.
    /// </summary>
    private static GitRemoteClient CreateClient(GitRepositoryFixture fixture) => new(new GitRemoteOptions(
        () => "git",
        Path.Combine(fixture.Root, "client-work"),
        Interactive: false,
        ExtraConfiguration: [],
        ExtraEnvironment: fixture.IsolationEnvironment,
        ListTimeout: TimeSpan.FromSeconds(60),
        FetchTimeout: TimeSpan.FromSeconds(120)));

    /// <summary>
    ///     Finds the full path of git on the <c>PATH</c>.
    /// </summary>
    /// <returns>The path, or <c>git</c> when it cannot be found.</returns>
    private static string FindGitOnPath()
    {
        var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "git";
    }

    /// <summary>
    ///     Gets the files of a small repository.
    /// </summary>
    private static Dictionary<string, string> Files(string marker) => new()
    {
        [".github/agents/copilot.md"] = marker,
        ["release-notes.md"] = "notes",
        ["README.md"] = "readme"
    };
}
