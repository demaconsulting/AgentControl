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

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Integration tests of <see cref="GitRemoteClient"/> against the real git executable and local
///     bare repositories (no network, no credentials): listing, the shallow fetch of one tag, the tree
///     listing and the path checkout, and the protections that must hold in real git - the transport
///     allow-list, and no trace output in messages. Skipped when git is not installed.
/// </summary>
[Collection("RealProcess")]
public sealed class GitRemoteClientIntegrationTests
{
    /// <summary>
    ///     Test the whole client sequence against a real repository: list the tags (an annotated tag
    ///     resolves to its commit), create the repository, fetch exactly one tag shallow, read its
    ///     tree, and check out only the named path.
    /// </summary>
    [Fact]
    public async Task GitRemoteClient_RealGit_ListInitFetchListTreeCheckout_Sequence()
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

        // Act: list the tree, check out one path
        var entries = await client.ListTreeAsync(
            repository, commit, [".github/agents", "release-notes.md"], recursive: true, TestContext.Current.CancellationToken);
        await client.CheckoutPathsAsync(repository, commit, [".github/agents"], TestContext.Current.CancellationToken);

        // Assert: only the requested path is on disk
        Assert.Equal(
            [".github/agents/copilot.md", "release-notes.md"],
            entries.Select(entry => entry.Path).Order());
        Assert.All(entries, entry => Assert.Equal("100644", entry.Mode));
        Assert.Equal(
            "one",
            await File.ReadAllTextAsync(Path.Combine(repository, ".github", "agents", "copilot.md"), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(repository, "release-notes.md")));
        Assert.False(File.Exists(Path.Combine(repository, "README.md")));
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

        // Assert: git said the transport is not allowed, and nothing was run
        Assert.Contains("not allowed", failure.Message, StringComparison.OrdinalIgnoreCase);
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
    ///     Gets the files of a small repository.
    /// </summary>
    private static Dictionary<string, string> Files(string marker) => new()
    {
        [".github/agents/copilot.md"] = marker,
        ["release-notes.md"] = "notes",
        ["README.md"] = "readme"
    };
}
