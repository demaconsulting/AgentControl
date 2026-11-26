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
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Unit tests for the branch and clone members of <see cref="GitClient"/> against a real git and
///     real repositories (the tests are skipped when git is not installed, and fail when the
///     environment variable <c>CI</c> is set).
/// </summary>
/// <remarks>
///     The stub-based <see cref="GitClientTests"/> pin down the command lines; these tests prove that
///     git really does what the command lines assume: which refs <c>for-each-ref</c> reports, that
///     <c>git switch --track</c> creates a tracking branch, that a checkout that would overwrite local
///     work is refused and changes nothing, and that a clone from a local bare repository (never the
///     network) produces a working repository. Every git runs with the user's global configuration
///     replaced by an empty file (see <see cref="GitRepositoryFixture.IsolationEnvironment"/>).
/// </remarks>
[Collection("RealProcess")]
public class GitClientRealRepositoryTests
{
    /// <summary>
    ///     Logger passed to every <see cref="GitClient"/> constructed by this class.
    /// </summary>
    private readonly ILogger<GitClient> _logger;

    /// <summary>
    ///     Initializes a new instance of <see cref="GitClientRealRepositoryTests"/>.
    /// </summary>
    /// <param name="loggingFixture">The shared test-assembly logging fixture.</param>
    public GitClientRealRepositoryTests(TestLoggingFixture loggingFixture)
    {
        _logger = loggingFixture.CreateLogger<GitClient>();
    }

    /// <summary>
    ///     Test that GetBranches lists local and remote-tracking branches of a real clone, marks the
    ///     current one, and leaves out the remote HEAD pointer and a remote branch that has a local twin.
    /// </summary>
    [Fact]
    public void GitClient_GetBranches_RealRepo_ListsLocalAndRemoteTracking()
    {
        // Arrange: a remote with main, feature/x and other; a clone of it
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clone = CloneOfRemoteWithBranches(fixture, out var client);

        // Act
        var branches = client.GetBranches(clone, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("main", branches.CurrentBranch);
        Assert.Null(branches.DetachedAt);
        Assert.Equal(["main"], branches.Local);
        Assert.Equal(
            [
                new GitRemoteBranch("origin", "feature/x", "origin/feature/x"),
                new GitRemoteBranch("origin", "other", "origin/other")
            ],
            branches.Remote);
    }

    /// <summary>
    ///     Test that switching to a remote-tracking branch creates a local branch that tracks it, and
    ///     that switching to an existing local branch works.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_RealRepo_LocalAndRemoteCreatesTrackingBranch()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clone = CloneOfRemoteWithBranches(fixture, out var client);

        // Act: to the remote branch, then back to the local one
        var toRemote = client.SwitchBranch(clone, "origin/feature/x", createTrackingFromRemote: true, TestContext.Current.CancellationToken);
        var afterRemote = client.GetBranches(clone, TestContext.Current.CancellationToken);
        var tracking = fixture.Git(clone, "config", "branch.feature/x.remote").Trim();
        var contentOnFeature = File.ReadAllText(Path.Combine(clone, "branch.txt"));
        var toLocal = client.SwitchBranch(clone, "main", createTrackingFromRemote: false, TestContext.Current.CancellationToken);
        var afterLocal = client.GetBranches(clone, TestContext.Current.CancellationToken);

        // Assert: a tracking branch was created and the remote twin is no longer listed on its own
        Assert.True(toRemote.Succeeded, toRemote.StandardError);
        Assert.Equal("feature/x", afterRemote.CurrentBranch);
        Assert.Equal(["feature/x", "main"], afterRemote.Local);
        Assert.Equal("origin", tracking);
        Assert.Equal("feature on x", contentOnFeature);
        Assert.True(toLocal.Succeeded, toLocal.StandardError);
        Assert.Equal("main", afterLocal.CurrentBranch);
        Assert.Equal("main", File.ReadAllText(Path.Combine(clone, "branch.txt")));
    }

    /// <summary>
    ///     Test that a switch that would overwrite an uncommitted change is refused with git's reason,
    ///     and changes nothing: the branch stays and the change is intact.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_RealRepo_DirtyConflictingFile_IsRefusedWithGitsReasonAndChangesNothing()
    {
        // Arrange: a clone with a local branch whose branch.txt differs from main's, and an uncommitted edit of branch.txt
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clone = CloneOfRemoteWithBranches(fixture, out var client);
        fixture.Git(clone, "branch", "feature/x", "origin/feature/x");
        var file = Path.Combine(clone, "branch.txt");
        File.WriteAllText(file, "my unsaved work");

        // Act
        var result = client.SwitchBranch(clone, "feature/x", createTrackingFromRemote: false, TestContext.Current.CancellationToken);

        // Assert: refused with git's explanation; still on main; the edit is intact
        Assert.False(result.Succeeded);
        Assert.Contains("branch.txt", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("overwritten", result.StandardError, StringComparison.Ordinal);
        Assert.Equal("main", client.GetBranches(clone, TestContext.Current.CancellationToken).CurrentBranch);
        Assert.Equal("my unsaved work", File.ReadAllText(file));
    }

    /// <summary>
    ///     Test that a switch that would overwrite an untracked file is refused, and the file is kept.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_RealRepo_UntrackedFileWouldBeOverwritten_IsRefused()
    {
        // Arrange: the "other" branch tracks only-on-other.txt, which exists untracked in the clone
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clone = CloneOfRemoteWithBranches(fixture, out var client);
        var file = Path.Combine(clone, "only-on-other.txt");
        File.WriteAllText(file, "untracked work");

        // Act
        var result = client.SwitchBranch(clone, "origin/other", createTrackingFromRemote: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("only-on-other.txt", result.StandardError, StringComparison.Ordinal);
        Assert.Equal("main", client.GetBranches(clone, TestContext.Current.CancellationToken).CurrentBranch);
        Assert.Equal("untracked work", File.ReadAllText(file));
    }

    /// <summary>
    ///     Test that from a detached HEAD the branch list reports the commit and a switch to a branch
    ///     works.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_RealRepo_FromDetachedHead_Works()
    {
        // Arrange: a clone with a detached HEAD
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var clone = CloneOfRemoteWithBranches(fixture, out var client);
        fixture.Git(clone, "checkout", "-q", "--detach");

        // Act
        var detached = client.GetBranches(clone, TestContext.Current.CancellationToken);
        var result = client.SwitchBranch(clone, "main", createTrackingFromRemote: false, TestContext.Current.CancellationToken);
        var after = client.GetBranches(clone, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(detached.CurrentBranch);
        Assert.False(string.IsNullOrEmpty(detached.DetachedAt));
        Assert.StartsWith(detached.DetachedAt, fixture.Git(clone, "rev-parse", "HEAD").Trim(), StringComparison.Ordinal);
        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("main", after.CurrentBranch);
        Assert.Null(after.DetachedAt);
    }

    /// <summary>
    ///     Test that a real local bare repository is cloned into a new folder, giving a working
    ///     repository, and that no working folder is left behind next to it.
    /// </summary>
    [Fact]
    public void GitClient_Clone_RealLocalBareRepository_ClonesIntoNewFolderAndLeavesNoTempBehind()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        PublishBranches(fixture);
        var client = CreateClient(fixture);
        var parent = Path.Combine(fixture.Root, "clones");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "into-new");

        // Act
        var result = client.Clone(fixture.RemotePath, destination, TestContext.Current.CancellationToken);

        // Assert: a repository root on main with the committed file that git itself finds clean after the move
        Assert.True(result.Succeeded, result.StandardError);
        Assert.True(GitClient.IsRepositoryRoot(destination));
        Assert.Equal("main", client.GetCurrentBranch(destination, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(destination, "branch.txt")));
        Assert.True(client.IsWorkingTreeClean(destination, TestContext.Current.CancellationToken));
        Assert.Equal([destination], Directory.GetFileSystemEntries(parent));
        Assert.Equal(0, (int)(File.GetAttributes(destination) & FileAttributes.Hidden));
    }

    /// <summary>
    ///     Test that a clone of something that is not a repository is a failed result, not an
    ///     exception, and that nothing - neither the destination nor a working folder - is left in the
    ///     parent folder.
    /// </summary>
    [Fact]
    public void GitClient_Clone_RealGit_NotARepository_ReturnsFailedResultAndLeavesParentEmpty()
    {
        // Arrange
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        var client = CreateClient(fixture);
        var parent = Path.Combine(fixture.Root, "clones");
        Directory.CreateDirectory(parent);
        var destination = Path.Combine(parent, "target");

        // Act
        var result = client.Clone(Path.Combine(fixture.Root, "does-not-exist.git"), destination, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
        Assert.Empty(Directory.GetFileSystemEntries(parent));
    }

    /// <summary>
    ///     Test that branch names with a slash and with non-ASCII characters, local and remote-tracking,
    ///     are listed exactly as git has them and can be switched to (git's output is read as UTF-8).
    /// </summary>
    [Fact]
    public void GitClient_GetBranchesAndSwitch_RealRepo_NonAsciiAndSlashBranchNames_RoundTrip()
    {
        // Arrange: a remote with a non-ASCII branch, and a clone with two more local ones
        // The non-ASCII characters (an o and an a with an umlaut, a u with an umlaut) are given by code
        var umlauts = new string([(char)0x00FC, (char)0x00F6, (char)0x00E4]);
        var remoteName = "remote-" + umlauts;
        var localName = "feature/local-" + umlauts;
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        PublishBranches(fixture);
        fixture.Git(fixture.WorkPath, "switch", "-q", "-c", remoteName);
        fixture.Commit(new Dictionary<string, string> { ["branch.txt"] = "non-ascii" }, "Non-ASCII");
        fixture.Git(fixture.WorkPath, "switch", "-q", "main");
        fixture.Git(fixture.WorkPath, "push", "-q", fixture.RemotePath, remoteName);
        var client = CreateClient(fixture);
        var clone = Path.Combine(fixture.Root, "clone");
        Assert.True(client.Clone(fixture.RemotePath, clone, TestContext.Current.CancellationToken).Succeeded);
        fixture.Git(clone, "branch", localName, "origin/feature/x");

        // Act
        var listed = client.GetBranches(clone, TestContext.Current.CancellationToken);
        var toLocal = client.SwitchBranch(clone, localName, createTrackingFromRemote: false, TestContext.Current.CancellationToken);
        var afterLocal = client.GetBranches(clone, TestContext.Current.CancellationToken);
        var toRemote = client.SwitchBranch(clone, "origin/" + remoteName, createTrackingFromRemote: true, TestContext.Current.CancellationToken);
        var afterRemote = client.GetBranches(clone, TestContext.Current.CancellationToken);

        // Assert: exact names, nothing mangled into replacement characters
        Assert.Contains(localName, listed.Local);
        Assert.Contains(new GitRemoteBranch("origin", remoteName, "origin/" + remoteName), listed.Remote);
        Assert.Contains(new GitRemoteBranch("origin", "feature/x", "origin/feature/x"), listed.Remote);
        Assert.True(toLocal.Succeeded, toLocal.StandardError);
        Assert.Equal(localName, afterLocal.CurrentBranch);
        Assert.True(toRemote.Succeeded, toRemote.StandardError);
        Assert.Equal(remoteName, afterRemote.CurrentBranch);
        Assert.Equal("non-ascii", File.ReadAllText(Path.Combine(clone, "branch.txt")));
    }

    /// <summary>
    ///     Test that an uncommitted edit and an untracked file that do not conflict with the target
    ///     branch travel with the switch (nothing is stashed, discarded or merged).
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_RealRepo_UnrelatedDirtyFile_SurvivesTheSwitch()
    {
        // Arrange: notes.md is the same on both branches; the other branch only adds another file
        using var fixture = GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["branch.txt"] = "main", ["notes.md"] = "notes" }, "Initial");
        fixture.Git(fixture.WorkPath, "switch", "-q", "-c", "other");
        fixture.Commit(new Dictionary<string, string> { ["only-on-other.txt"] = "other" }, "Other");
        fixture.Git(fixture.WorkPath, "switch", "-q", "main");
        fixture.Git(fixture.WorkPath, "push", "-q", fixture.RemotePath, "main", "other");
        fixture.Git(fixture.RemotePath, "symbolic-ref", "HEAD", "refs/heads/main");
        var client = CreateClient(fixture);
        var clone = Path.Combine(fixture.Root, "clone");
        Assert.True(client.Clone(fixture.RemotePath, clone, TestContext.Current.CancellationToken).Succeeded);
        File.WriteAllText(Path.Combine(clone, "notes.md"), "my edit");
        File.WriteAllText(Path.Combine(clone, "scratch.txt"), "scratch");

        // Act
        var result = client.SwitchBranch(clone, "origin/other", createTrackingFromRemote: true, TestContext.Current.CancellationToken);

        // Assert: the branch changed, both edits are still there, and nothing was stashed
        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("other", client.GetBranches(clone, TestContext.Current.CancellationToken).CurrentBranch);
        Assert.Equal("other", File.ReadAllText(Path.Combine(clone, "only-on-other.txt")));
        Assert.Equal("my edit", File.ReadAllText(Path.Combine(clone, "notes.md")));
        Assert.Equal("scratch", File.ReadAllText(Path.Combine(clone, "scratch.txt")));
        Assert.Equal(string.Empty, fixture.Git(clone, "stash", "list").Trim());
        Assert.Contains("notes.md", fixture.Git(clone, "status", "--porcelain"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a client that runs the real git with the fixture's isolated configuration.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The client.</returns>
    private GitClient CreateClient(GitRepositoryFixture fixture) =>
        new("git", _logger, null, fixture.IsolationEnvironment);

    /// <summary>
    ///     Commits <c>main</c> (with <c>branch.txt</c>), <c>feature/x</c> (a different <c>branch.txt</c>)
    ///     and <c>other</c> (an extra file) in the fixture's working repository and pushes all three to
    ///     its bare remote, whose default branch is main.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    private static void PublishBranches(GitRepositoryFixture fixture)
    {
        fixture.Commit(new Dictionary<string, string> { ["branch.txt"] = "main" }, "Initial");
        fixture.Git(fixture.WorkPath, "switch", "-q", "-c", "feature/x");
        fixture.Commit(new Dictionary<string, string> { ["branch.txt"] = "feature on x" }, "Feature");
        fixture.Git(fixture.WorkPath, "switch", "-q", "main");
        fixture.Git(fixture.WorkPath, "switch", "-q", "-c", "other");
        fixture.Commit(new Dictionary<string, string> { ["only-on-other.txt"] = "other" }, "Other");
        fixture.Git(fixture.WorkPath, "switch", "-q", "main");
        fixture.Git(fixture.WorkPath, "push", "-q", fixture.RemotePath, "main", "feature/x", "other");
        fixture.Git(fixture.RemotePath, "symbolic-ref", "HEAD", "refs/heads/main");
    }

    /// <summary>
    ///     Publishes the branches and clones the remote with the client under test.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="client">The client that cloned.</param>
    /// <returns>The path of the clone.</returns>
    private string CloneOfRemoteWithBranches(GitRepositoryFixture fixture, out GitClient client)
    {
        PublishBranches(fixture);
        client = CreateClient(fixture);
        var clone = Path.Combine(fixture.Root, "clone");
        var result = client.Clone(fixture.RemotePath, clone, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.StandardError);
        return clone;
    }
}
