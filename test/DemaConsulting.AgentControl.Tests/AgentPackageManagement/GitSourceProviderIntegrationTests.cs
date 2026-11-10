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

using DemaConsulting.AgentControl.AgentPackageManagement;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.AgentControl.Tests.PackageCredentials;

// cspell:ignore ufeffu
namespace DemaConsulting.AgentControl.Tests.AgentPackageManagement;

/// <summary>
///     Tests of <see cref="GitSourceProvider"/> against real Git repositories: a local bare
///     repository plays the remote, so the real git semantics (peeled tags, shallow fetch of one
///     tag, refusal of links, hook neutralization) are proven on every platform without a network.
///     The tests are skipped when git is not installed.
/// </summary>
[Collection("RealProcess")]
public sealed class GitSourceProviderIntegrationTests
{
    /// <summary>
    ///     The package ID every fixture source offers.
    /// </summary>
    private const string PackageId = "contoso-agents";

    /// <summary>
    ///     Test that listing resolves lightweight and annotated tags to the commit they point to (never
    ///     the annotated tag object), skips tags that are not strict versions under the pattern, and
    ///     offers no versions for another package ID.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_ListVersions_LightweightAndAnnotatedTags_ResolveToCommitsAndFilterByPattern()
    {
        // Arrange: one lightweight tag, one annotated tag on another commit, and tags that are not versions
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("one"), "one");
        fixture.Tag("v1.0.0");
        var first = fixture.Resolve("v1.0.0");
        fixture.Commit(Layout("two"), "two");
        fixture.Tag("v1.1.0", annotated: true);
        fixture.Tag("v2.0.0-beta.1");
        foreach (var other in new[] { "latest", "v1.2", "v1.2.3+build", "x1.0.0", "V1.0.1", "v1.2.3.4", "v01.2.3", "v1.2.3-01" })
        {
            fixture.Tag(other);
        }

        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());

        // Act
        var versions = await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken);

        // Assert: three versions, resolved to commits
        Assert.Equal(["1.0.0", "1.1.0", "2.0.0-beta.1"], versions.Select(v => v.Version.ToString()).Order());
        var lightweight = versions.Single(v => v.Location == "v1.0.0");
        Assert.Equal(first, lightweight.Revision);
        var annotated = versions.Single(v => v.Location == "v1.1.0");
        Assert.Equal(fixture.Resolve("v1.1.0"), annotated.Revision);
        Assert.NotEqual(fixture.TagObject("v1.1.0"), annotated.Revision);
        Assert.All(versions, v =>
        {
            Assert.Equal("Repo", v.SourceAlias);
            Assert.Equal(PackageId, v.PackageName);
        });

        // Assert: a different package ID has no versions, and the single ID is the configured one
        Assert.Empty(await provider.ListVersionsAsync("other-package", TestContext.Current.CancellationToken));
        Assert.Equal([PackageId], await provider.ListPackageIdsAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a repository without tags, or with none matching, offers no versions rather than an
    ///     error.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_ListVersions_NoMatchingTags_ReturnsEmpty()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("one"), "one");
        fixture.Tag("latest");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());

        // Act / Assert
        Assert.Empty(await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that the tag preview separates the tags that are versions from the others, with a few
    ///     sample names of the others.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Preview_SeparatesMatchingFromOtherTags()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("one"), "one");
        foreach (var tag in new[] { "v1.0.0", "v1.1.0", "latest", "nightly", "v2", "x1.0.0", "y", "z", "w" })
        {
            fixture.Tag(tag);
        }

        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());

        // Act
        var preview = await ((IGitTagPreview)provider).PreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(9, preview.TotalTags);
        Assert.Equal(["1.1.0", "1.0.0"], preview.Matching.Select(m => m.Version.ToString()));
        Assert.Equal(7, preview.NonMatchingCount);
        Assert.Equal(5, preview.NonMatchingSamples.Count);
    }

    /// <summary>
    ///     Test that acquiring a tag delivers exactly the package layout: the managed folders and the
    ///     root files, no <c>.git</c> folder, none of the repository's other files, the commit as its
    ///     source revision and the identity of the files; and that the cache folder is named by the commit.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_DeliversOnlyLayoutWithCommitRevisionAndNoGitFolder()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var commit = fixture.Commit(Layout("marker-1"), "one");
        fixture.Tag("v1.0.0", annotated: true);
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert: the layout, nothing else
        var files = reader.ListFiles().Order().ToList();
        Assert.Equal(
            [
                ".github/agents/copilot.md", ".github/skills/skill.md", ".github/standards/style.md",
                ".github/templates/template.md", "AGENTS.md", "release-notes.md"
            ],
            files);
        Assert.Equal("marker-1", reader.ReadText(".github/agents/copilot.md"));
        Assert.Equal("# Release notes marker-1", reader.ReadText("release-notes.md"));
        Assert.Equal(PackageContentIdentity.FromGitCommit(commit), reader.SourceRevision);
        Assert.True(PackageContentIdentity.IsFilesIdentity(reader.ContentIdentity));
        Assert.Throws<InvalidOperationException>(() => reader.ReadText("README.md"));

        // Assert: the cache folder is named by the commit, holds no .git and leaves no temporary folder
        var cacheFolder = Path.Combine(provider.RepositoryCacheFolder, commit);
        Assert.True(File.Exists(Path.Combine(cacheFolder, GitSourceProvider.MarkerFileName)));
        Assert.False(Directory.Exists(Path.Combine(cacheFolder, ".git")));
        Assert.False(File.Exists(Path.Combine(cacheFolder, "README.md")));
        Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*"));

        // Assert: the content extracts into a repo
        var repo = Path.Combine(fixture.Root, "target");
        Directory.CreateDirectory(repo);
        PackageContentExtractor.Extract(reader, repo);
        Assert.Equal(
            "marker-1",
            await File.ReadAllTextAsync(Path.Combine(repo, ".github", "agents", "copilot.md"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Test that a second acquisition of the same commit uses the cache without contacting the
    ///     remote, so the cache is keyed by repository and commit.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_SecondTime_UsesCacheWithoutRemote()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("cached"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();
        (await provider.AcquireAsync(package, TestContext.Current.CancellationToken)).Dispose();

        // Act: the remote disappears
        GitSourceProvider.DeleteDirectory(fixture.RemotePath);
        using var again = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("cached", again.ReadText(".github/agents/copilot.md"));
    }

    /// <summary>
    ///     Test that a tag that was moved is seen at listing as a different commit, that acquiring the
    ///     old listing entry is refused because the repository no longer lists the tag at that commit, and
    ///     that the new listing entry is acquired into its own cache folder.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_MovedTag_ShowsNewRevisionAndRefusesStaleListing()
    {
        // Arrange: v1.0.0 at commit A, acquired
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var first = fixture.Commit(Layout("original"), "A");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var listedBefore = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();
        (await provider.AcquireAsync(listedBefore, TestContext.Current.CancellationToken)).Dispose();

        // Act: the maintainer moves the tag to commit B
        var second = fixture.Commit(Layout("republished"), "B");
        fixture.Tag("v1.0.0", force: true);
        fixture.Push();
        var listedAfter = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Assert: the listing shows the new commit, so a caller can compare it with the pinned one
        Assert.Equal(first, listedBefore.Revision);
        Assert.Equal(second, listedAfter.Revision);

        // Assert: the stale entry is refused before anything is fetched (the tag no longer points where it was listed)
        var stale = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(listedBefore with { Revision = "0123456789abcdef0123456789abcdef01234567" }, TestContext.Current.CancellationToken));
        Assert.Contains("Refresh the list", stale.Message, StringComparison.Ordinal);

        // Assert: the new entry is acquired into its own folder, and the old commit's folder is kept
        using var reader = await provider.AcquireAsync(listedAfter, TestContext.Current.CancellationToken);
        Assert.Equal("republished", reader.ReadText(".github/agents/copilot.md"));
        Assert.Equal(PackageContentIdentity.FromGitCommit(second), reader.SourceRevision);
        Assert.True(Directory.Exists(Path.Combine(provider.RepositoryCacheFolder, first)));
        Assert.True(Directory.Exists(Path.Combine(provider.RepositoryCacheFolder, second)));
        Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*"));
    }

    /// <summary>
    ///     Test that a link, a submodule or a link in place of a managed folder inside the layout is
    ///     refused, nothing is cached, and the message names the problem.
    /// </summary>
    [Theory]
    [InlineData("symlink-file", "a symbolic link")]
    [InlineData("symlink-folder", "a symbolic link")]
    [InlineData("symlink-release-notes", "a symbolic link")]
    [InlineData("symlink-github", "a symbolic link")]
    [InlineData("submodule", "a submodule")]
    public async Task GitSourceProvider_Acquire_LinkOrSubmoduleInLayout_IsRefusedAndNothingCached(string kind, string expected)
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = Layout("evil");
        switch (kind)
        {
            case "symlink-folder":
                files.Remove(".github/skills/skill.md");
                break;
            case "symlink-release-notes":
                files.Remove("release-notes.md");
                break;
            case "symlink-github":
                files.Clear();
                files["README.md"] = "readme";
                break;
        }

        var head = fixture.Commit(files, "base");
        switch (kind)
        {
            case "symlink-file":
                fixture.AddSymbolicLink(".github/agents/evil.md", "../../../outside.txt");
                break;
            case "symlink-folder":
                fixture.AddSymbolicLink(".github/skills", "../../outside");
                break;
            case "symlink-release-notes":
                fixture.AddSymbolicLink("release-notes.md", "/etc/passwd");
                break;
            case "symlink-github":
                fixture.AddSymbolicLink(".github", "../outside");
                break;
            default:
                fixture.AddSubmodule(".github/agents/sub", head);
                break;
        }

        fixture.CommitStaged("with " + kind);
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert: refused for the right reason, with nothing left in the cache
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(provider.RepositoryCacheFolder, package.Revision!)));
        Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*"));
    }

    /// <summary>
    ///     Test that links and other content outside the package layout are never checked out and so
    ///     never matter: a repository with a symbolic link in <c>docs</c> acquires normally.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_SymbolicLinkOutsideLayout_IsIgnored()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("fine"), "base");
        fixture.AddSymbolicLink("docs/link", "../../../outside.txt");
        fixture.AddSymbolicLink(".github/workflows", "../outside");
        fixture.CommitStaged("links outside the layout");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("fine", reader.ReadText(".github/agents/copilot.md"));
        Assert.DoesNotContain(reader.ListFiles(), file => file.Contains("link", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Test that a layout in a sub-folder is read from that sub-folder, and that a missing, linked
    ///     or layout-less sub-folder is refused.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_Subfolder_ReadsLayoutFromThatFolder()
    {
        // Arrange: the layout lives under packaging/agents, with other files at the top
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = Layout("sub").ToDictionary(pair => "packaging/agents/" + pair.Key, pair => pair.Value);
        files[".github/agents/other.md"] = "top-level, not the package";
        fixture.Commit(files, "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(
            fixture.CreateDefinition(subfolder: "packaging/agents"), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("sub", reader.ReadText(".github/agents/copilot.md"));
        Assert.DoesNotContain(".github/agents/other.md", reader.ListFiles());
    }

    /// <summary>
    ///     Test that a sub-folder that does not exist in the tag, that is a link, or that holds no
    ///     layout is refused with a message that says why.
    /// </summary>
    [Theory]
    [InlineData("missing", "does not exist")]
    [InlineData("linked", "a symbolic link")]
    [InlineData("empty", "no agent package layout")]
    public async Task GitSourceProvider_Acquire_BadSubfolder_IsRefused(string subfolder, string expected)
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["empty/readme.txt"] = "no layout here", ["x.txt"] = "x" }, "base");
        fixture.AddSymbolicLink("linked", "empty");
        fixture.CommitStaged("link");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(subfolder: subfolder), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that a tag without any package layout is refused.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_TagWithoutLayout_IsRefused()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["README.md"] = "no layout" }, "base");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("no agent package layout", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Test that hooks never run: a hostile template directory and a hostile global
    ///     <c>core.hooksPath</c> that would run a post-checkout hook for a plain git are both
    ///     neutralized, so the marker the hook would write never appears.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_HostileHooksAndTemplate_NeverRun()
    {
        // Arrange: a repository, and a user environment that installs a post-checkout hook everywhere
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("hooked"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var marker = Path.Combine(fixture.Root, "hook-ran.txt");
        var hostile = fixture.CreateHostileHooksEnvironment(marker);

        // Control: plain git with the same hostile environment does run the hook, so the test is meaningful
        var control = Path.Combine(fixture.Root, "control");
        fixture.RunWithEnvironment(hostile, fixture.Root, "init", "-q", control);
        fixture.RunWithEnvironment(hostile, control, "fetch", "-q", fixture.RemotePath, "refs/tags/v1.0.0:refs/tags/v1.0.0");
        fixture.RunWithEnvironment(hostile, control, "checkout", "-q", "refs/tags/v1.0.0", "--", ".github");
        if (!File.Exists(marker))
        {
            Assert.Skip("Hooks do not run in this environment, so the neutralization cannot be demonstrated.");
        }

        File.Delete(marker);
        var options = fixture.CreateOptions() with { ExtraEnvironment = hostile };
        var provider = new GitSourceProvider(fixture.CreateDefinition(), options);
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert: acquired, and no hook ran
        Assert.Equal("hooked", reader.ReadText(".github/agents/copilot.md"));
        Assert.False(File.Exists(marker));
    }

    /// <summary>
    ///     Test that canceling stops the acquisition with an <see cref="OperationCanceledException"/>.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_AlreadyCancelled_ThrowsOperationCanceled()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("one"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.AcquireAsync(package, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ListVersionsAsync(PackageId, cancellation.Token));
    }

    /// <summary>
    ///     Test that a repository that does not exist is reported as not found, in the provider's own
    ///     failure type, with no path or git text beyond a short summary.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_ListVersions_RepositoryMissing_ReportsNotFound()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var definition = fixture.CreateDefinition();
        definition.Location = Path.Combine(fixture.Root, "no-such-repository.git");
        var provider = new GitSourceProvider(definition, fixture.CreateOptions());

        // Act
        var failure = await Assert.ThrowsAsync<PackageSourceAccessException>(
            () => provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(PackageSourceFailureKind.NotFound, failure.Kind);
    }

    /// <summary>
    ///     Test that only the most recently used commits are kept, the one just used among them.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_PrunesOldestCommitsBeyondTheLimit()
    {
        // Arrange: four tags on four commits, a cache that keeps two
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var commits = new List<string>();
        foreach (var version in new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0" })
        {
            commits.Add(fixture.Commit(Layout("m" + version), "v" + version));
            fixture.Tag("v" + version);
        }

        fixture.Push();
        var provider = new GitSourceProvider(
            fixture.CreateDefinition(), fixture.CreateOptions() with { MaxCachedCommits = 2 });
        var packages = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken))
            .OrderBy(p => p.Version).ToList();

        // Act: acquire in order, giving each folder a clearly later time than the last
        var start = DateTime.UtcNow.AddHours(-1);
        for (var index = 0; index < packages.Count; index++)
        {
            using var reader = await provider.AcquireAsync(packages[index], TestContext.Current.CancellationToken);
            Directory.SetLastWriteTimeUtc(
                Path.Combine(provider.RepositoryCacheFolder, packages[index].Revision!), start.AddMinutes(index));
        }

        // The last acquisition pruned before its own folder got its time; one more cache hit prunes again
        (await provider.AcquireAsync(packages[^1], TestContext.Current.CancellationToken)).Dispose();

        // Assert: only the two newest remain
        var remaining = Directory.EnumerateDirectories(provider.RepositoryCacheFolder)
            .Select(Path.GetFileName).Order().ToList();
        Assert.Equal(new[] { commits[2], commits[3] }.Order(), remaining);
    }

    /// <summary>
    ///     Test that a reader that is still open keeps its commit folder: while it is held, other
    ///     acquisitions that take the cache beyond its limit prune the other old folders but never
    ///     the held one, which stays complete and readable; and once the reader is disposed a later
    ///     prune may remove the folder.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_ReaderHeldWhileOthersExceedLimit_KeepsHeldFolderUntilDisposed()
    {
        // Arrange: four commits, a cache that keeps two, and the oldest one held by an open reader
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        foreach (var version in new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0" })
        {
            fixture.Commit(Layout("m" + version), "v" + version);
            fixture.Tag("v" + version);
        }

        fixture.Push();
        var provider = new GitSourceProvider(
            fixture.CreateDefinition(), fixture.CreateOptions() with { MaxCachedCommits = 2 });
        var packages = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken))
            .OrderBy(p => p.Version).ToList();
        string Folder(int index) => Path.Combine(provider.RepositoryCacheFolder, packages[index].Revision!);
        var start = DateTime.UtcNow.AddHours(-1);
        var held = await provider.AcquireAsync(packages[0], TestContext.Current.CancellationToken);
        try
        {
            Directory.SetLastWriteTimeUtc(Folder(0), start);

            // Act: three other commits are acquired and released, each pruning the cache
            for (var index = 1; index < packages.Count; index++)
            {
                using var other = await provider.AcquireAsync(packages[index], TestContext.Current.CancellationToken);
                Directory.SetLastWriteTimeUtc(Folder(index), start.AddMinutes(index));
            }

            // Assert: the held folder is intact and readable though it is the oldest beyond the limit,
            // while the old folder that no reader holds was pruned
            Assert.True(GitSourceProvider.IsCacheFolderLeased(Folder(0)));
            Assert.True(File.Exists(Path.Combine(Folder(0), GitSourceProvider.MarkerFileName)));
            Assert.Equal("m1.0.0", held.ReadText(".github/agents/copilot.md"));
            Assert.Equal(6, held.ListFiles().Count);
            Assert.False(Directory.Exists(Folder(1)));
            Assert.True(Directory.Exists(Folder(2)));
            Assert.True(Directory.Exists(Folder(3)));
        }
        finally
        {
            held.Dispose();
        }

        // Act: the reader is gone; a cache hit prunes again
        (await provider.AcquireAsync(packages[3], TestContext.Current.CancellationToken)).Dispose();

        // Assert: nothing holds the old folder any more, so it is pruned
        Assert.False(GitSourceProvider.IsCacheFolderLeased(Folder(0)));
        Assert.False(Directory.Exists(Folder(0)));
        Assert.Equal(
            new[] { packages[2].Revision!, packages[3].Revision! }.Order(),
            Directory.EnumerateDirectories(provider.RepositoryCacheFolder).Select(Path.GetFileName).Order());
    }

    /// <summary>
    ///     Test that a reader gives its lease back exactly once: disposing it again does not release
    ///     the lease of another reader of the same commit, and the folder is free once both are
    ///     disposed.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_ReaderDisposedTwice_ReleasesItsLeaseOnlyOnce()
    {
        // Arrange: two readers of the same commit
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("shared"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();
        var folder = Path.Combine(provider.RepositoryCacheFolder, package.Revision!);
        var first = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);
        var second = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Act: the first reader is disposed twice
        first.Dispose();
        first.Dispose();

        // Assert: the second reader still holds the folder, and releasing it frees the folder
        Assert.True(GitSourceProvider.IsCacheFolderLeased(folder));
        second.Dispose();
        Assert.False(GitSourceProvider.IsCacheFolderLeased(folder));
    }

    /// <summary>
    ///     Test that an acquisition that fails leaves no lease behind, because no reader exists to
    ///     release it.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_Refused_LeavesNoLease()
    {
        // Arrange: a tag without any package layout, which is refused after the fetch
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["readme.txt"] = "no layout" }, "base");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert
        Assert.False(GitSourceProvider.IsCacheFolderLeased(Path.Combine(provider.RepositoryCacheFolder, package.Revision!)));
    }

    /// <summary>
    ///     Test that an old temporary folder left by a killed acquisition is swept, and that a cache
    ///     folder without a marker (an incomplete write) is replaced rather than trusted.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_SweepsStaleTemporaryFolderAndReplacesUnmarkedCacheFolder()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var commit = fixture.Commit(Layout("fresh"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();
        var stale = Path.Combine(provider.RepositoryCacheFolder, ".tmp-leftover");
        Directory.CreateDirectory(stale);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        var unmarked = Path.Combine(provider.RepositoryCacheFolder, commit);
        Directory.CreateDirectory(Path.Combine(unmarked, ".github", "agents"));
        await File.WriteAllTextAsync(
            Path.Combine(unmarked, ".github", "agents", "copilot.md"), "poisoned", TestContext.Current.CancellationToken);

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Directory.Exists(stale));
        Assert.Equal("fresh", reader.ReadText(".github/agents/copilot.md"));
    }

    /// <summary>
    ///     Test that two layout entries whose names differ only in letter case or Unicode form - two
    ///     files, a file and a folder, two folders, or the composed and decomposed spelling of one
    ///     name - are refused before anything is checked out or cached, on every platform, because
    ///     the cache may be read on a file system that treats them as one.
    /// </summary>
    [Theory]
    [InlineData("files-case")]
    [InlineData("file-and-folder")]
    [InlineData("folders-case")]
    [InlineData("unicode-forms")]
    public async Task GitSourceProvider_Acquire_FoldedPathCollisionInLayout_IsRefusedAndNothingCached(string kind)
    {
        // Arrange: a good layout plus two entries that collide when case or Unicode form is folded
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("base"), "base");
        var (first, second) = kind switch
        {
            "files-case" => (".github/agents/A.md", ".github/agents/a.md"),
            "file-and-folder" => (".github/skills/notes", ".github/skills/NOTES/entry.md"),
            "folders-case" => (".github/standards/Docs/one.md", ".github/standards/docs/two.md"),
            _ => (".github/agents/caf\u00e9.md", ".github/agents/cafe\u0301.md")
        };
        fixture.AddFile(first, "first");
        fixture.AddFile(second, "second");
        fixture.CommitStaged("colliding " + kind);
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert: refused for the right reason, with nothing published or left behind
        Assert.Contains("differ only in letter case or Unicode form", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(provider.RepositoryCacheFolder, package.Revision!)));
        Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*"));
    }

    /// <summary>
    ///     Test that when the <c>.git</c> folder cannot be removed from the fetched package - the
    ///     removal fails, or silently leaves it - the acquisition fails, no cache folder of the commit
    ///     is published or marked complete and nothing is left behind, and that a later acquisition
    ///     succeeds and delivers a package without <c>.git</c>.
    /// </summary>
    [Theory]
    [InlineData("removal-throws")]
    [InlineData("removal-does-nothing")]
    public async Task GitSourceProvider_Acquire_GitFolderRemovalFails_IsRefusedNothingPublishedAndLaterAcquisitionSucceeds(string kind)
    {
        // Arrange: the first removal of .git fails, later ones are real
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var commit = fixture.Commit(Layout("clean"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var removals = 0;
        var options = fixture.CreateOptions() with
        {
            RemoveGitFolder = path =>
            {
                if (removals++ > 0)
                {
                    GitSourceProvider.DeleteDirectoryOrThrow(path);
                }
                else if (kind == "removal-throws")
                {
                    throw new IOException("simulated: the folder is in use");
                }
            }
        };
        var provider = new GitSourceProvider(fixture.CreateDefinition(), options);
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert: refused with an actionable message, and nothing that could be reused remains
        Assert.IsNotType<PackageSourceAccessException>(failure);
        Assert.Contains("could not be finished in the cache folder", failure.Message, StringComparison.Ordinal);
        var published = Path.Combine(provider.RepositoryCacheFolder, commit);
        Assert.False(Directory.Exists(published));
        Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*"));

        // Act: a later acquisition (the folder is no longer in use)
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert: delivered, complete, and without the repository metadata
        Assert.Equal("clean", reader.ReadText(".github/agents/copilot.md"));
        Assert.True(File.Exists(Path.Combine(published, GitSourceProvider.MarkerFileName)));
        Assert.False(Directory.Exists(Path.Combine(published, ".git")));
        Assert.Equal(2, removals);
    }

    /// <summary>
    ///     Test that a temporary folder that could not be cleaned up after a failed acquisition is
    ///     never used as a package: it is not named by a commit, so it is not served from the cache,
    ///     and a later acquisition sweeps it once it is old enough.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_LeftoverTemporaryFolderWithGit_IsNeverServedAndIsSweptWhenStale()
    {
        // Arrange: a leftover that holds a repository and even a marker for the commit
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var commit = fixture.Commit(Layout("fresh"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();
        var leftover = Path.Combine(provider.RepositoryCacheFolder, ".tmp-failed");
        Directory.CreateDirectory(Path.Combine(leftover, ".git"));
        await File.WriteAllTextAsync(Path.Combine(leftover, GitSourceProvider.MarkerFileName), commit, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(leftover, "AGENTS.md"), "poisoned", TestContext.Current.CancellationToken);
        Directory.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddDays(-2));

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert: the package came from the tag, not from the leftover, and the leftover is gone
        Assert.Equal("# Agents template", reader.ReadText("AGENTS.md"));
        Assert.False(Directory.Exists(leftover));
    }

    /// <summary>
    ///     Test that a package this source did not report is refused: another alias, another package, a
    ///     missing revision, or a tag that does not match the pattern or the version.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_PackageNotReportedBySource_IsRefused()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("one"), "one");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var good = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act / Assert
        foreach (var bad in new[]
                 {
                     good with { SourceAlias = "Other" },
                     good with { PackageName = "other-package" },
                     good with { Revision = null },
                     good with { Revision = "not-a-commit" },
                     good with { Location = "v9.9.9" },
                     good with { Location = "--upload-pack=calc" }
                 })
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => provider.AcquireAsync(bad, TestContext.Current.CancellationToken));
        }
    }

    /// <summary>
    ///     Test that a layout folder that is a file where a managed folder belongs, and a root file
    ///     that is a folder, are refused with nothing cached: such a tag would otherwise make an
    ///     extraction delete a managed folder and put nothing back.
    /// </summary>
    [Theory]
    [InlineData("folder-is-file-skills", "'.github/skills' in this tag is not a folder")]
    [InlineData("folder-is-file-agents", "'.github/agents' in this tag is not a folder")]
    [InlineData("root-file-is-folder", "'release-notes.md' in this tag is not an ordinary file")]
    [InlineData("agents-file-is-folder", "'AGENTS.md' in this tag is not an ordinary file")]
    public async Task GitSourceProvider_Acquire_LayoutFolderAsFileOrRootFileAsFolder_IsRefusedAndNothingCached(
        string kind, string expected)
    {
        // Arrange: a good layout in which one entry has the wrong kind
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = Layout("wrong-kind");
        switch (kind)
        {
            case "folder-is-file-skills":
                files.Remove(".github/skills/skill.md");
                files[".github/skills"] = "a file where a managed folder belongs";
                break;
            case "folder-is-file-agents":
                files.Remove(".github/agents/copilot.md");
                files[".github/agents"] = "a file where a managed folder belongs";
                break;
            case "root-file-is-folder":
                files.Remove("release-notes.md");
                files["release-notes.md/inner.md"] = "a folder where a root file belongs";
                break;
            default:
                files.Remove("AGENTS.md");
                files["AGENTS.md/inner.md"] = "a folder where a root file belongs";
                break;
        }

        fixture.Commit(files, "wrong kind");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert: refused for the right reason, with nothing published or left behind
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(provider.RepositoryCacheFolder, package.Revision!)));
        Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*"));
    }

    /// <summary>
    ///     Test that an entry whose name is not safe on every file system - a Windows device name, a
    ///     stream separator, a trailing dot - is refused, because the files are written by AgentControl
    ///     itself and nothing else checks the names.
    /// </summary>
    [Theory]
    [InlineData(".github/agents/CON.md")]
    [InlineData(".github/agents/com1")]
    [InlineData(".github/agents/with:colon.md")]
    [InlineData(".github/standards/sub/trailing.")]
    public async Task GitSourceProvider_Acquire_UnsafeEntryName_IsRefusedAndNothingCached(string path)
    {
        // Arrange: a good layout plus an entry staged straight into the index, so the name needs no real file
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(Layout("unsafe-name"), "base");
        fixture.AddFile(path, "content");
        fixture.CommitStaged("unsafe name");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var provider = new GitSourceProvider(fixture.CreateDefinition(), fixture.CreateOptions());
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireAsync(package, TestContext.Current.CancellationToken));

        // Assert: refused, with nothing published or left behind
        Assert.Contains("not safe on every file system", failure.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(provider.RepositoryCacheFolder, package.Revision!)));
        Assert.Empty(Directory.EnumerateDirectories(provider.RepositoryCacheFolder, ".tmp-*"));
    }

    /// <summary>
    ///     Test that a filter driver the user's own git configuration defines, and that a fetched
    ///     repository selects through <c>.gitattributes</c>, never runs: the program the driver names
    ///     (which writes a marker) is not started, and the delivered content is exactly the bytes of
    ///     the blobs - no identifier expansion, no line-ending conversion - even though the user's
    ///     configuration asks for <c>autocrlf</c>.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_HostileFilterSelectedByRepository_NeverRunsAndContentIsRaw()
    {
        // Arrange: CRLF content committed first, then attributes that would filter and convert it on checkout
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        const string crlf = "one\r\ntwo\r\nthree\r\n";
        const string ident = "$Id$ stays literal\n";
        fixture.Commit(
            new Dictionary<string, string>(Layout("hostile"))
            {
                [".github/standards/style.md"] = crlf,
                [".github/agents/copilot.md"] = ident
            },
            "content");
        fixture.Commit(
            new Dictionary<string, string>
            {
                [".github/agents/.gitattributes"] = "* filter=pwn ident\n",
                [".github/standards/.gitattributes"] = "* text eol=lf filter=pwn\n"
            },
            "attributes");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var marker = Path.Combine(fixture.Root, "filter-ran.txt");
        var hostile = fixture.CreateHostileFilterEnvironment(marker);

        // Control: a plain git checkout with the same hostile configuration does run the filter, so the
        // test is meaningful
        var control = Path.Combine(fixture.Root, "control");
        fixture.RunWithEnvironment(hostile, fixture.Root, "init", "-q", control);
        fixture.RunWithEnvironment(hostile, control, "fetch", "-q", fixture.RemotePath, "refs/tags/v1.0.0:refs/tags/v1.0.0");
        fixture.RunWithEnvironment(hostile, control, "checkout", "-q", "refs/tags/v1.0.0", "--", ".github/agents", ".github/standards");
        if (!File.Exists(marker))
        {
            Assert.Skip("Filter drivers do not run in this environment, so their neutralization cannot be demonstrated.");
        }

        File.Delete(marker);
        var options = fixture.CreateOptions() with { ExtraEnvironment = hostile };
        var provider = new GitSourceProvider(fixture.CreateDefinition(), options);
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert: acquired, the filter program never ran, and the content is byte for byte what the commit holds
        Assert.False(File.Exists(marker));
        Assert.Equal(ident, reader.ReadText(".github/agents/copilot.md"));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(crlf), ReadBytes(reader, ".github/standards/style.md"));
    }

    /// <summary>
    ///     Test that content is delivered byte for byte as committed - a carriage return, a NUL byte,
    ///     a byte-order mark, no final line feed, an empty file - under the user's <c>autocrlf</c>
    ///     setting, with no attributes involved.
    /// </summary>
    [Fact]
    public async Task GitSourceProvider_Acquire_UserAutocrlfSetting_DoesNotChangeDeliveredBytes()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var files = Layout("bytes");
        files[".github/templates/crlf.md"] = "a\r\nb\r\nc";
        files[".github/templates/mixed.md"] = "x\ny\r\nz\rw\0v\ufeffu\n";
        files[".github/templates/empty.md"] = string.Empty;
        fixture.Commit(files, "bytes");
        fixture.Tag("v1.0.0");
        fixture.Push();
        var config = Path.Combine(fixture.Root, "autocrlf.gitconfig");
        await File.WriteAllTextAsync(
            config, "[core]\n\tautocrlf = true\n\teol = crlf\n[protocol \"file\"]\n\tallow = always\n", TestContext.Current.CancellationToken);
        var environment = new Dictionary<string, string?>(fixture.IsolationEnvironment) { ["GIT_CONFIG_GLOBAL"] = config };
        var provider = new GitSourceProvider(
            fixture.CreateDefinition(), fixture.CreateOptions() with { ExtraEnvironment = environment });
        var package = (await provider.ListVersionsAsync(PackageId, TestContext.Current.CancellationToken)).Single();

        // Act
        using var reader = await provider.AcquireAsync(package, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("a\r\nb\r\nc"), ReadBytes(reader, ".github/templates/crlf.md"));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("x\ny\r\nz\rw\0v\ufeffu\n"), ReadBytes(reader, ".github/templates/mixed.md"));
        Assert.Empty(ReadBytes(reader, ".github/templates/empty.md"));
    }

    /// <summary>
    ///     Reads all bytes of one file of a package.
    /// </summary>
    /// <param name="reader">The package content.</param>
    /// <param name="path">The relative path.</param>
    /// <returns>The bytes.</returns>
    private static byte[] ReadBytes(IPackageContentReader reader, string path)
    {
        using var stream = reader.OpenRead(path);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    ///     Gets a set of package files with a marker, plus files that are not part of the layout.
    /// </summary>
    /// <param name="marker">Text for the agents file, so versions can be told apart.</param>
    /// <returns>The files by repository path.</returns>
    private static Dictionary<string, string> Layout(string marker) => new()
    {
        [".github/agents/copilot.md"] = marker,
        [".github/standards/style.md"] = "standards",
        [".github/templates/template.md"] = "templates",
        [".github/skills/skill.md"] = "skills",
        ["release-notes.md"] = "# Release notes " + marker,
        ["AGENTS.md"] = "# Agents template",
        ["README.md"] = "not part of the layout",
        ["src/app.txt"] = "not part of the layout"
    };
}
