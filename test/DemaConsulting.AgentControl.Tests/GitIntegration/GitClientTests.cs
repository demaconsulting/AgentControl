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
using DemaConsulting.AgentControl.GitIntegration;
using DemaConsulting.AgentControl.Utilities;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.Tests.GitIntegration;

/// <summary>
///     Unit tests for <see cref="GitClient"/>.
/// </summary>
/// <remarks>
///     Every test substitutes a small stub script for the git executable (per architecture.md's
///     testability strategy) instead of depending on a real git installation or repository, so
///     these tests are hermetic and cross-platform. Every <see cref="GitClient"/> instance is
///     constructed with a real <see cref="ILogger{TCategoryName}"/> backed by the assembly-wide
///     <see cref="TestLoggingFixture"/> (rather than relying on <see cref="GitClient"/>'s
///     default no-op fallback), so that if the intermittent exit-code -1 flakiness documented in
///     <c>.agent-logs/implementation-agentcontrol-v1-final-3e91c7.md</c> recurs here, the
///     before/after/timing/stderr/Win32-error diagnostic detail added to
///     <c>GitClient.RunGit</c> is actually captured to this assembly's <c>test-logs</c> log
///     file instead of being silently discarded.
/// </remarks>
[Collection("RealProcess")]
public class GitClientTests
{
    /// <summary>
    ///     Logger passed to every <see cref="GitClient"/> constructed by this test class, backed
    ///     by the shared <see cref="TestLoggingFixture"/> so process-launch diagnostics are
    ///     captured to this assembly's <c>test-logs</c> log file.
    /// </summary>
    private readonly ILogger<GitClient> _logger;

    /// <summary>
    ///     Initializes a new instance of <see cref="GitClientTests"/>, receiving the
    ///     assembly-wide <see cref="TestLoggingFixture"/> that xUnit v3 injects via
    ///     <c>[assembly: Xunit.AssemblyFixture(typeof(TestLoggingFixture))]</c>.
    /// </summary>
    /// <param name="loggingFixture">The shared test-assembly logging fixture.</param>
    public GitClientTests(TestLoggingFixture loggingFixture)
    {
        _logger = loggingFixture.CreateLogger<GitClient>();
    }

    /// <summary>
    ///     Test that a clean working tree (no output from "git status --porcelain") is reported
    ///     as clean.
    /// </summary>
    [Fact]
    public void GitClient_IsWorkingTreeClean_CleanRepo_ReturnsTrue()
    {
        // Arrange: a stub that prints nothing for "status --porcelain" and exits 0
        var stub = GitStub.Create(statusOutput: "", statusExitCode: 0);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act: check working tree cleanliness
            var isClean = client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken);

            // Assert: empty status output means a clean tree
            Assert.True(isClean);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a dirty working tree (non-empty output from "git status --porcelain") is
    ///     reported as not clean.
    /// </summary>
    [Fact]
    public void GitClient_IsWorkingTreeClean_DirtyRepo_ReturnsFalse()
    {
        // Arrange: a stub that reports a modified file
        var stub = GitStub.Create(statusOutput: " M modified-file.txt", statusExitCode: 0);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act: check working tree cleanliness
            var isClean = client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken);

            // Assert: non-empty status output means a dirty tree
            Assert.False(isClean);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a failing "git status" (e.g. not a git repository) throws an
    ///     InvalidOperationException carrying the captured stderr.
    /// </summary>
    [Fact]
    public void GitClient_IsWorkingTreeClean_StatusCommandFails_ThrowsInvalidOperationException()
    {
        // Arrange: a stub that fails status with a specific error message
        var stub = GitStub.Create(statusOutput: "", statusExitCode: 128, statusError: "fatal: not a git repository");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert: the failure is surfaced with the captured error text
            var ex = Assert.Throws<InvalidOperationException>(() => client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken));
            Assert.Contains("fatal: not a git repository", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a successful pull returns a succeeded result with the captured output.
    /// </summary>
    [Fact]
    public void GitClient_Pull_Succeeds_ReturnsSucceededResult()
    {
        // Arrange: a stub that reports a successful pull
        var stub = GitStub.Create(pullOutput: "Already up to date.", pullExitCode: 0);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act: pull
            var result = client.Pull(repoDir, TestContext.Current.CancellationToken);

            // Assert: success is reported with the captured stdout
            Assert.True(result.Succeeded);
            Assert.Contains("Already up to date.", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a failing pull (e.g. merge conflict) returns a non-succeeded result rather
    ///     than throwing, since callers must surface this to the user as a normal outcome.
    /// </summary>
    [Fact]
    public void GitClient_Pull_Fails_ReturnsNonSucceededResultWithoutThrowing()
    {
        // Arrange: a stub that reports a failed pull
        var stub = GitStub.Create(pullExitCode: 1, pullError: "CONFLICT: merge conflict in file.txt");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act: pull
            var result = client.Pull(repoDir, TestContext.Current.CancellationToken);

            // Assert: the failure is reported via the result, not an exception
            Assert.False(result.Succeeded);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("CONFLICT", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that constructing a client with a null/empty/whitespace executable path throws an
    ///     ArgumentException.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GitClient_Constructor_EmptyExecutablePath_ThrowsArgumentException(string executablePath)
    {
        // Act / Assert: an empty/whitespace executable path is rejected
        Assert.Throws<ArgumentException>(() => new GitClient(executablePath, _logger));
    }

    /// <summary>
    ///     Test that a non-existent git executable path throws an InvalidOperationException
    ///     rather than an unhandled Win32Exception.
    /// </summary>
    [Fact]
    public void GitClient_IsWorkingTreeClean_ExecutableNotFound_ThrowsInvalidOperationException()
    {
        // Arrange: a client pointed at a path that does not exist
        var missingPath = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_git_" + Guid.NewGuid() + ".exe");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(missingPath, _logger);

            // Act / Assert: a missing executable is surfaced as a clear exception that can be caught
            Assert.Throws<InvalidOperationException>(() => client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Creates a unique temporary directory for test isolation.
    /// </summary>
    /// <returns>The created directory's path.</returns>
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentcontrol_git_client_test_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     Test that GetHeadCommitHash returns the trimmed stdout from "git rev-parse HEAD".
    /// </summary>
    [Fact]
    public void GitClient_GetHeadCommitHash_Succeeds_ReturnsTrimmedHash()
    {
        // Arrange
        var stub = GitStub.Create(revParseHeadOutput: "0123456789abcdef0123456789abcdef01234567");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var hash = client.GetHeadCommitHash(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", hash);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetHeadCommitHash throws when "git rev-parse HEAD" fails.
    /// </summary>
    [Fact]
    public void GitClient_GetHeadCommitHash_Fails_ThrowsInvalidOperationException()
    {
        // Arrange
        var stub = GitStub.Create(revParseHeadExitCode: 128, revParseHeadError: "fatal: bad revision 'HEAD'");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert
            var ex = Assert.Throws<InvalidOperationException>(() => client.GetHeadCommitHash(repoDir, TestContext.Current.CancellationToken));
            Assert.Contains("bad revision", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that HasCommittedAgentFiles returns true when "git ls-files" reports a tracked
    ///     file under one of the four known agent folders.
    /// </summary>
    [Fact]
    public void GitClient_HasCommittedAgentFiles_LsFilesReportsFiles_ReturnsTrue()
    {
        // Arrange
        var stub = GitStub.Create(lsFilesOutput: ".github/agents/copilot.md");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.HasCommittedAgentFiles(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that HasCommittedAgentFiles returns false when "git ls-files" reports no tracked
    ///     files under the four known agent folders.
    /// </summary>
    [Fact]
    public void GitClient_HasCommittedAgentFiles_LsFilesReportsNothing_ReturnsFalse()
    {
        // Arrange
        var stub = GitStub.Create(lsFilesOutput: "");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.HasCommittedAgentFiles(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that HasCommittedAgentFiles throws when "git ls-files" fails.
    /// </summary>
    [Fact]
    public void GitClient_HasCommittedAgentFiles_LsFilesFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var stub = GitStub.Create(lsFilesExitCode: 128, lsFilesError: "fatal: not a git repository");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => client.HasCommittedAgentFiles(repoDir, TestContext.Current.CancellationToken));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetTrackedAgentFolders reports exactly the known agent folders that have tracked
    ///     files - and not an untracked-but-present folder or other tracked paths - in the fixed
    ///     order of the known folders.
    /// </summary>
    [Fact]
    public void GitClient_GetTrackedAgentFolders_SomeFoldersTracked_ReturnsOnlyThoseInKnownOrder()
    {
        // Arrange: a real repo that tracks skills and agents (committed in the opposite order) plus
        // unrelated files, with a standards folder on disk that git does not track
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string>
            {
                [".github/skills/s.md"] = "skill",
                [".github/agents/a.md"] = "agent",
                [".github/workflows/ci.yml"] = "ci",
                ["README.md"] = "readme"
            },
            "Track agent files");
        Directory.CreateDirectory(Path.Combine(fixture.WorkPath, ".github", "standards"));
        File.WriteAllText(Path.Combine(fixture.WorkPath, ".github", "standards", "untracked.md"), "untracked");
        var client = new GitClient("git", _logger);

        // Act
        var tracked = client.GetTrackedAgentFolders(fixture.WorkPath);

        // Assert: only the tracked managed folders, in the known order
        Assert.Equal([".github/agents", ".github/skills"], tracked);
    }

    /// <summary>
    ///     Test that GetTrackedAgentFolders returns an empty list when no known agent folder has
    ///     tracked files.
    /// </summary>
    [Fact]
    public void GitClient_GetTrackedAgentFolders_NothingTracked_ReturnsEmptyList()
    {
        // Arrange: a real repo whose only tracked files are outside the agent folders
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(new Dictionary<string, string> { ["README.md"] = "readme" }, "Initial commit");
        var client = new GitClient("git", _logger);

        // Act
        var tracked = client.GetTrackedAgentFolders(fixture.WorkPath);

        // Assert
        Assert.Empty(tracked);
    }

    /// <summary>
    ///     Test that GetTrackedAgentFolders counts a known agent folder path that is itself tracked
    ///     as a single entry (here a file named <c>.github/agents</c>), and does not count a sibling
    ///     whose name only starts with a known folder's name.
    /// </summary>
    [Fact]
    public void GitClient_GetTrackedAgentFolders_ExactPathTracked_CountsItButNotPrefixSibling()
    {
        // Arrange: a real repo tracking a file at the exact path .github/agents and a sibling folder
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string>
            {
                [".github/agents"] = "a file where the folder would be",
                [".github/standards-old/s.md"] = "sibling"
            },
            "Track exact path and sibling");
        var client = new GitClient("git", _logger);

        // Act
        var tracked = client.GetTrackedAgentFolders(fixture.WorkPath);

        // Assert: the exact path counts, the prefix sibling does not
        Assert.Equal([".github/agents"], tracked);
    }

    /// <summary>
    ///     Test that RestoreAgentFolderTracking undoes UntrackAgentFolders: the folders' files are back in
    ///     git's index and the working tree is unchanged.
    /// </summary>
    [Fact]
    public void GitClient_RestoreAgentFolderTracking_AfterUntrack_RestoresIndexAndKeepsFiles()
    {
        // Arrange: a real repo whose agents folder was untracked
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string> { [".github/agents/a.md"] = "agent", ["README.md"] = "readme" },
            "Track agent files");
        var client = new GitClient("git", _logger);
        client.UntrackAgentFolders(fixture.WorkPath, [".github/agents"]);
        Assert.Empty(client.GetTrackedAgentFolders(fixture.WorkPath));

        // Act
        client.RestoreAgentFolderTracking(fixture.WorkPath, [".github/agents"]);

        // Assert: tracked again, file unchanged on disk
        Assert.Equal([".github/agents"], client.GetTrackedAgentFolders(fixture.WorkPath));
        Assert.Equal("agent", File.ReadAllText(Path.Combine(fixture.WorkPath, ".github", "agents", "a.md")));
    }

    /// <summary>
    ///     Test that GetTrackedAgentFolders throws when "git ls-files" fails.
    /// </summary>
    [Fact]
    public void GitClient_GetTrackedAgentFolders_LsFilesFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var stub = GitStub.Create(lsFilesExitCode: 128, lsFilesError: "fatal: not a git repository");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => client.GetTrackedAgentFolders(repoDir));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that UntrackAgentFolders removes the given folders from the index only: their files
    ///     stay on disk unchanged, every other tracked path stays tracked, and a tracked agent folder
    ///     that was not named stays tracked.
    /// </summary>
    [Fact]
    public void GitClient_UntrackAgentFolders_TrackedFolders_RemovesThemFromIndexOnlyAndKeepsFilesOnDisk()
    {
        // Arrange: a real repo that tracks three agent folders and unrelated files
        using var fixture = AgentPackageManagement.GitRepositoryFixture.CreateOrSkip();
        fixture.Commit(
            new Dictionary<string, string>
            {
                [".github/agents/a.md"] = "agent",
                [".github/standards/s.md"] = "standard",
                [".github/skills/k.md"] = "skill",
                [".github/workflows/ci.yml"] = "ci",
                ["README.md"] = "readme"
            },
            "Track agent files");
        var client = new GitClient("git", _logger);

        // Act: untrack two of the three
        client.UntrackAgentFolders(fixture.WorkPath, [".github/agents", ".github/standards"]);

        // Assert: the index no longer lists the two folders but still lists everything else
        var indexed = fixture.Git(fixture.WorkPath, "ls-files").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).ToList();
        Assert.Equal([".github/skills/k.md", ".github/workflows/ci.yml", "README.md"], indexed);

        // Assert: nothing was deleted or changed on disk
        Assert.Equal("agent", File.ReadAllText(Path.Combine(fixture.WorkPath, ".github", "agents", "a.md")));
        Assert.Equal("standard", File.ReadAllText(Path.Combine(fixture.WorkPath, ".github", "standards", "s.md")));
        Assert.Equal("skill", File.ReadAllText(Path.Combine(fixture.WorkPath, ".github", "skills", "k.md")));

        // Assert: the client agrees that only the skills folder is still tracked
        Assert.Equal([".github/skills"], client.GetTrackedAgentFolders(fixture.WorkPath));
    }

    /// <summary>
    ///     Test that UntrackAgentFolders throws when "git rm" fails, carrying git's explanation.
    /// </summary>
    [Fact]
    public void GitClient_UntrackAgentFolders_GitRmFails_ThrowsInvalidOperationException()
    {
        // Arrange: a stub that does not know "rm" and so exits with a failure code
        var stub = GitStub.Create();
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(
                () => client.UntrackAgentFolders(repoDir, [".github/agents"]));

            // Assert
            Assert.Contains("git rm --cached", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that UntrackAgentFolders refuses an empty list and anything that is not a known
    ///     agent folder, so it can never untrack a path that is not AgentControl's to manage.
    /// </summary>
    [Fact]
    public void GitClient_UntrackAgentFolders_EmptyOrUnknownFolders_ThrowsArgumentException()
    {
        // Arrange: a stub that would fail the test if git were ever invoked
        var invocationLog = Path.Combine(Path.GetTempPath(), $"agentcontrol_git_log_{Guid.NewGuid():N}.txt");
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert: nothing, a source folder and an option-like value are all refused
            Assert.Throws<ArgumentException>(() => client.UntrackAgentFolders(repoDir, []));
            Assert.Throws<ArgumentException>(() => client.UntrackAgentFolders(repoDir, ["src"]));
            Assert.Throws<ArgumentException>(() => client.UntrackAgentFolders(repoDir, ["--force"]));
            Assert.Throws<ArgumentNullException>(() => client.UntrackAgentFolders(repoDir, null!));
            Assert.False(File.Exists(invocationLog));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that GetCurrentBranch resolves a normal branch name via the fast path, reading a
    ///     hand-written <c>.git/HEAD</c> file directly rather than invoking the (stub) git
    ///     executable at all.
    /// </summary>
    [Fact]
    public void GitClient_GetCurrentBranch_FastPathNormalBranch_ReturnsBranchNameWithoutInvokingGit()
    {
        // Arrange: a stub that would fail if invoked, and a hand-written .git/HEAD
        var stub = GitStub.Create(abbrevRefExitCode: 1);
        var repoDir = CreateTempDirectory();
        try
        {
            var gitDir = Directory.CreateDirectory(Path.Combine(repoDir, ".git"));
            File.WriteAllText(Path.Combine(gitDir.FullName, "HEAD"), "ref: refs/heads/feature/my-branch\n");
            var client = new GitClient(stub.Path, _logger);

            // Act
            var branch = client.GetCurrentBranch(repoDir, TestContext.Current.CancellationToken);

            // Assert: resolved via the fast path (the stub would have failed abbrev-ref)
            Assert.Equal("feature/my-branch", branch);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetCurrentBranch reports "(detached)" via the fast path when
    ///     <c>.git/HEAD</c> contains a raw commit hash rather than a symbolic ref.
    /// </summary>
    [Fact]
    public void GitClient_GetCurrentBranch_FastPathDetachedHead_ReturnsDetachedMarker()
    {
        // Arrange
        var stub = GitStub.Create(abbrevRefExitCode: 1);
        var repoDir = CreateTempDirectory();
        try
        {
            var gitDir = Directory.CreateDirectory(Path.Combine(repoDir, ".git"));
            File.WriteAllText(Path.Combine(gitDir.FullName, "HEAD"), "0123456789abcdef0123456789abcdef01234567\n");
            var client = new GitClient(stub.Path, _logger);

            // Act
            var branch = client.GetCurrentBranch(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("(detached)", branch);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetCurrentBranch follows one level of "gitdir:" indirection for a worktree,
    ///     where <c>.git</c> is a file (not a directory) pointing at the real git directory.
    /// </summary>
    [Fact]
    public void GitClient_GetCurrentBranch_FastPathWorktreeIndirection_ReturnsBranchName()
    {
        // Arrange: ".git" is a file pointing at a separate real git directory
        var stub = GitStub.Create(abbrevRefExitCode: 1);
        var repoDir = CreateTempDirectory();
        var realGitDir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(repoDir, ".git"), $"gitdir: {realGitDir}\n");
            File.WriteAllText(Path.Combine(realGitDir, "HEAD"), "ref: refs/heads/worktree-branch\n");
            var client = new GitClient(stub.Path, _logger);

            // Act
            var branch = client.GetCurrentBranch(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("worktree-branch", branch);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            Directory.Delete(realGitDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetCurrentBranch falls back to "git rev-parse --abbrev-ref HEAD" when no
    ///     <c>.git/HEAD</c> file exists at all (the fast path's parse/IO anomaly case).
    /// </summary>
    [Fact]
    public void GitClient_GetCurrentBranch_NoGitDirectory_FallsBackToSubprocess()
    {
        // Arrange: no ".git" directory or file at all
        var stub = GitStub.Create(abbrevRefOutput: "fallback-branch");
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var branch = client.GetCurrentBranch(repoDir, TestContext.Current.CancellationToken);

            // Assert: the stub's canned "rev-parse --abbrev-ref HEAD" answer was used
            Assert.Equal("fallback-branch", branch);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetCurrentBranch falls back to the subprocess call when <c>.git/HEAD</c>
    ///     contains unrecognized content (neither a symbolic ref nor a plausible commit hash).
    /// </summary>
    [Fact]
    public void GitClient_GetCurrentBranch_UnrecognizedHeadContent_FallsBackToSubprocess()
    {
        // Arrange
        var stub = GitStub.Create(abbrevRefOutput: "fallback-branch");
        var repoDir = CreateTempDirectory();
        try
        {
            var gitDir = Directory.CreateDirectory(Path.Combine(repoDir, ".git"));
            File.WriteAllText(Path.Combine(gitDir.FullName, "HEAD"), "not-a-recognized-format\n");
            var client = new GitClient(stub.Path, _logger);

            // Act
            var branch = client.GetCurrentBranch(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("fallback-branch", branch);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetCurrentBranch throws when both the fast path is unavailable and the
    ///     fallback subprocess call fails.
    /// </summary>
    [Fact]
    public void GitClient_GetCurrentBranch_FallbackFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var stub = GitStub.Create(abbrevRefExitCode: 128);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => client.GetCurrentBranch(repoDir, TestContext.Current.CancellationToken));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a folder with a <c>.git</c> folder directly in it is a repository root.
    /// </summary>
    [Fact]
    public void GitClient_IsRepositoryRoot_GitDirectory_ReturnsTrue()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("agentcontrol_root_").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, ".git"));

            // Act / Assert
            Assert.True(GitClient.IsRepositoryRoot(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a folder with a <c>.git</c> file directly in it (a linked worktree or a submodule)
    ///     is a repository root.
    /// </summary>
    [Fact]
    public void GitClient_IsRepositoryRoot_GitFile_ReturnsTrue()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("agentcontrol_root_").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, ".git"), "gitdir: ../main/.git/worktrees/feature\n");

            // Act / Assert
            Assert.True(GitClient.IsRepositoryRoot(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a folder without a <c>.git</c> folder or file is not a repository root.
    /// </summary>
    [Fact]
    public void GitClient_IsRepositoryRoot_NoGit_ReturnsFalse()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("agentcontrol_root_").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "README.md"), "not a repository");

            // Act / Assert
            Assert.False(GitClient.IsRepositoryRoot(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a subfolder of a repository is not a repository root: the parent folders are not
    ///     searched for a <c>.git</c>.
    /// </summary>
    [Fact]
    public void GitClient_IsRepositoryRoot_SubfolderOfRepository_ReturnsFalse()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("agentcontrol_root_").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, ".git"));
            var subfolder = Directory.CreateDirectory(Path.Combine(folder, "src", "app")).FullName;

            // Act / Assert
            Assert.False(GitClient.IsRepositoryRoot(subfolder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a folder that does not exist is not a repository root (and the check does not
    ///     fail).
    /// </summary>
    [Fact]
    public void GitClient_IsRepositoryRoot_MissingFolder_ReturnsFalse()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "agentcontrol_missing_root_" + Guid.NewGuid().ToString("N"));

        // Act / Assert
        Assert.False(GitClient.IsRepositoryRoot(missing));
    }

    /// <summary>
    ///     Test that a blank path is refused with an <see cref="ArgumentException"/>.
    /// </summary>
    /// <param name="path">The blank path.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GitClient_IsRepositoryRoot_BlankPath_ThrowsArgumentException(string path)
    {
        // Act / Assert
        Assert.ThrowsAny<ArgumentException>(() => GitClient.IsRepositoryRoot(path));
    }

    /// <summary>
    ///     Test that a local git command that hangs past its time limit is stopped (the process is
    ///     killed before it finishes) and reported as timed out, naming the limit.
    /// </summary>
    [Fact]
    public async Task GitClient_IsWorkingTreeClean_GitHangsPastTimeout_StopsItAndThrowsTimedOut()
    {
        // Arrange: git status hangs well past a one-second local limit
        var stub = GitStub.Create(statusSleepSeconds: GitStub.HangSeconds);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                new GitClientTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10)));

            // Act
            var failure = Assert.Throws<GitCommandTimeoutException>(() => client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken));

            // Assert: reported as a timeout of the local limit (still an InvalidOperationException for
            // existing callers), and the process never got to finish
            Assert.IsAssignableFrom<InvalidOperationException>(failure);
            Assert.Equal(TimeSpan.FromSeconds(1), failure.Timeout);
            Assert.Contains("git status", failure.Message, StringComparison.Ordinal);
            Assert.False(await stub.RanToTheEndOfItsHangAsync());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a git command that exited while a process it started still holds its output open
    ///     is not waited for past its time limit: the wait is stopped and the command reported as timed
    ///     out, naming the limit.
    /// </summary>
    [Fact]
    public async Task GitClient_IsWorkingTreeClean_HelperHoldsOutputAfterGitExits_StopsWaitingAndThrowsTimedOut()
    {
        // Arrange: git status answers and exits at once, but a helper it started keeps its output open
        // well past a one-second local limit (and a one-second stop time)
        var stub = GitStub.Create(statusOutputHolderSeconds: GitStub.HangSeconds);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                new GitClientTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)));

            // Act
            var failure = Assert.Throws<GitCommandTimeoutException>(() => client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken));

            // Assert: reported as a timeout of the local limit rather than waiting for the helper
            Assert.Equal(TimeSpan.FromSeconds(1), failure.Timeout);
            Assert.Contains("git status", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            // The helper has ended (killed or run out) before the stub is deleted; whether it was
            // killed depends on the platform, so it is not asserted
            _ = await stub.RanToTheEndOfItsHangAsync();
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a pull runs under the pull limit, not the much shorter local-command limit: a pull
    ///     that takes longer than the local limit completes.
    /// </summary>
    [Fact]
    public void GitClient_Pull_UsesPullTimeoutNotLocalTimeout()
    {
        // Arrange: a pull that takes two seconds, a one-second local limit and a generous pull limit
        var stub = GitStub.Create(pullOutput: "Already up to date.", pullSleepSeconds: 2);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                new GitClientTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10)));

            // Act
            var result = client.Pull(repoDir, TestContext.Current.CancellationToken);

            // Assert: the pull ran to its end and succeeded
            Assert.True(result.Succeeded);
            Assert.True(stub.WroteFinishedMarker());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that canceling a running git command stops the process (it never finishes) and
    ///     surfaces the cancellation.
    /// </summary>
    [Fact]
    public async Task GitClient_GetHeadCommitHash_Canceled_StopsGitAndThrowsCanceled()
    {
        // Arrange: git rev-parse hangs well past the moment the caller cancels
        var stub = GitStub.Create(revParseHeadSleepSeconds: GitStub.HangSeconds);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromSeconds(1));

            // Act / Assert: the cancellation surfaces, and the process never got to finish
            Assert.ThrowsAny<OperationCanceledException>(() => client.GetHeadCommitHash(repoDir, cancellation.Token));
            Assert.False(await stub.RanToTheEndOfItsHangAsync());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that every git query made on a thread guarded against blocking calls is refused with the
    ///     guard's exception before git is started.
    /// </summary>
    [Fact]
    public void GitClient_AnyCommand_OnGuardedThread_ThrowsBlockingCallOnGuardedThread()
    {
        // Arrange: a stub that logs every invocation
        var invocationLog = Path.Combine(Path.GetTempPath(), "agentcontrol_guard_invocations_" + Guid.NewGuid() + ".log");
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);
            var refusals = new List<Exception?>();

            // Act: every query, on a guarded thread
            using (BlockingCallGuard.GuardCurrentThread())
            {
                refusals.Add(Record.Exception(() => client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken)));
                refusals.Add(Record.Exception(() => client.Pull(repoDir, TestContext.Current.CancellationToken)));
                refusals.Add(Record.Exception(() => client.GetHeadCommitHash(repoDir, TestContext.Current.CancellationToken)));
                refusals.Add(Record.Exception(() => client.HasCommittedAgentFiles(repoDir, TestContext.Current.CancellationToken)));
                refusals.Add(Record.Exception(() => client.GetCurrentBranch(repoDir, TestContext.Current.CancellationToken)));
            }

            var recorded = BlockingCallGuard.TakeViolations();

            // Assert: each refused with the guard's exception and recorded; git never started
            Assert.All(refusals, refusal => Assert.IsType<BlockingCallOnGuardedThreadException>(refusal));
            Assert.Equal(5, recorded.Count);
            Assert.False(File.Exists(invocationLog));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that GetBranches reports the local branches and the remote-tracking branches, marks the
    ///     checked-out one, splits a remote whose name contains a slash correctly, and drops the remote
    ///     HEAD pointer and a remote branch that has a local twin.
    /// </summary>
    [Fact]
    public void GitClient_GetBranches_ListsLocalAndRemoteWithCurrentMarked()
    {
        // Arrange: a repository with two local branches and remote branches of two remotes
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command(
                "for-each-ref",
                Output: "*\trefs/heads/main\n \trefs/heads/feature/x\n \trefs/remotes/origin/HEAD\n" +
                        " \trefs/remotes/origin/main\n \trefs/remotes/origin/feature/y\n \trefs/remotes/up/stream/z\n"),
            new GitStub.Command("remote", Output: "origin\nup/stream\n")
        ]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var branches = client.GetBranches(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal("main", branches.CurrentBranch);
            Assert.Null(branches.DetachedAt);
            Assert.Equal(["feature/x", "main"], branches.Local);
            Assert.Equal(
                [new GitRemoteBranch("origin", "feature/y", "origin/feature/y"), new GitRemoteBranch("up/stream", "z", "up/stream/z")],
                branches.Remote);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetBranches reports a detached HEAD: no current branch, the short commit id.
    /// </summary>
    [Fact]
    public void GitClient_GetBranches_DetachedHead_ReportsNullBranchAndShortCommit()
    {
        // Arrange: no ref carries the HEAD marker
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("for-each-ref", Output: " \trefs/heads/main\n"),
            new GitStub.Command("remote"),
            new GitStub.Command("rev-parse", "--short", Output: "1a2b3c4\n")
        ]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var branches = client.GetBranches(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.Null(branches.CurrentBranch);
            Assert.Equal("1a2b3c4", branches.DetachedAt);
            Assert.Equal(["main"], branches.Local);
            Assert.Empty(branches.Remote);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that GetBranches throws an InvalidOperationException carrying git's error when the ref
    ///     listing fails (for example the folder is not a repository).
    /// </summary>
    [Fact]
    public void GitClient_GetBranches_ForEachRefFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("for-each-ref", Error: "fatal: not a git repository", ExitCode: 128)
        ]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert
            var ex = Assert.Throws<InvalidOperationException>(() => client.GetBranches(repoDir, TestContext.Current.CancellationToken));
            Assert.Contains("fatal: not a git repository", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that switching to a local branch runs <c>git switch -- name</c>.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_Local_RunsSwitchWithDoubleDash()
    {
        // Arrange
        var argumentLog = TempFilePath();
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("switch", Error: "Switched to branch 'feature/x'")]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.SwitchBranch(repoDir, "feature/x", createTrackingFromRemote: false, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Contains("Switched to branch", result.StandardError, StringComparison.Ordinal);
            Assert.Equal(["switch -- feature/x"], ReadLines(argumentLog));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            File.Delete(argumentLog);
        }
    }

    /// <summary>
    ///     Test that switching to a remote-tracking branch runs <c>git switch --track -- remote/name</c>.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_Remote_RunsSwitchTrackWithRemoteName()
    {
        // Arrange
        var argumentLog = TempFilePath();
        var stub = GitStub.Create(argumentLogPath: argumentLog, extraCommands: [new GitStub.Command("switch")]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.SwitchBranch(repoDir, "origin/feature/y", createTrackingFromRemote: true, TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(["switch --track -- origin/feature/y"], ReadLines(argumentLog));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            File.Delete(argumentLog);
        }
    }

    /// <summary>
    ///     Test that no switch ever passes an option that forces the checkout or discards, merges or
    ///     creates anything.
    /// </summary>
    /// <param name="remote">Whether the switch is to a remote-tracking branch.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GitClient_SwitchBranch_NeverPassesForceOrDiscardOptions(bool remote)
    {
        // Arrange
        var argumentLog = TempFilePath();
        var stub = GitStub.Create(argumentLogPath: argumentLog, extraCommands: [new GitStub.Command("switch")]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            _ = client.SwitchBranch(repoDir, remote ? "origin/x" : "x", remote, TestContext.Current.CancellationToken);

            // Assert: the options are exactly the safe ones, and the one call is a switch - nothing is stashed
            var line = Assert.Single(ReadLines(argumentLog));
            var options = line.Split(' ').Where(word => word.StartsWith('-')).ToList();
            Assert.StartsWith("switch ", line, StringComparison.Ordinal);
            Assert.DoesNotContain("stash", line, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(remote ? ["--track", "--"] : ["--"], options);
            Assert.DoesNotContain(options, option => option is "-f" or "--force" or "--discard-changes" or "-C" or "-c" or "--merge" or "-m");
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            File.Delete(argumentLog);
        }
    }

    /// <summary>
    ///     Test that a checkout git refuses is returned as a result carrying git's message, not thrown.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_Refused_ReturnsGitsMessageWithoutThrowing()
    {
        // Arrange: git refuses because a local change would be overwritten
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command(
                "switch",
                Error: "error: Your local changes to the following files would be overwritten by checkout:\n\tfile.txt\n",
                ExitCode: 1)
        ]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.SwitchBranch(repoDir, "other", createTrackingFromRemote: false, TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("would be overwritten by checkout", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a blank, option-like or multi-line branch name is refused before git starts.
    /// </summary>
    /// <param name="name">The refused name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-f")]
    [InlineData("--detach")]
    [InlineData("a\nb")]
    public void GitClient_SwitchBranch_BlankOrOptionLikeName_ThrowsArgumentException(string name)
    {
        // Arrange
        var invocationLog = TempFilePath();
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert: refused, and git never started
            Assert.ThrowsAny<ArgumentException>(() => client.SwitchBranch(repoDir, name, createTrackingFromRemote: false, TestContext.Current.CancellationToken));
            Assert.ThrowsAny<ArgumentException>(() => client.SwitchBranch(repoDir, name, createTrackingFromRemote: true, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(invocationLog));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that a switch runs under the switch limit, not the much shorter local-command limit.
    /// </summary>
    [Fact]
    public void GitClient_SwitchBranch_UsesSwitchTimeoutNotLocalTimeout()
    {
        // Arrange: a switch that takes two seconds, a one-second local limit and a generous switch limit
        var stub = GitStub.Create(extraCommands: [new GitStub.Command("switch", SleepSeconds: 2)]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                new GitClientTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10)));

            // Act
            var result = client.SwitchBranch(repoDir, "other", createTrackingFromRemote: false, TestContext.Current.CancellationToken);

            // Assert: it ran to its end and succeeded
            Assert.True(result.Succeeded);
            Assert.True(stub.WroteFinishedMarker());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a successful clone runs <c>git clone -- url folder</c> in the parent folder with a
    ///     fresh hidden sibling folder as the target, and then makes that folder the destination.
    /// </summary>
    [Fact]
    public void GitClient_Clone_Succeeds_RunsCloneWithDoubleDashAndArgumentList()
    {
        // Arrange
        var argumentLog = TempFilePath();
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("clone", Error: "Cloning into 'repo'...", CreatesDestination: true)]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act: the URL is padded, as a text box may deliver it
            var result = client.Clone("  https://example.com/team/repo.git ", destination, TestContext.Current.CancellationToken);

            // Assert: git cloned into a sibling of the destination, never into the destination itself
            Assert.True(result.Succeeded);
            Assert.Contains("Cloning into", result.StandardError, StringComparison.Ordinal);
            var line = Assert.Single(ReadLines(argumentLog));
            Assert.StartsWith("clone -- https://example.com/team/repo.git ", line, StringComparison.Ordinal);
            var target = line["clone -- https://example.com/team/repo.git ".Length..];
            Assert.Equal(parent, Path.GetDirectoryName(target));
            Assert.StartsWith(".agentcontrol-clone-", Path.GetFileName(target), StringComparison.Ordinal);
            Assert.NotEqual(destination, target);

            // Assert: the clone was renamed to the destination and nothing else is left in the parent
            Assert.True(File.Exists(Path.Combine(destination, ".git", "objects", "pack", "pack-1.pack")));
            Assert.False(Directory.Exists(target));
            Assert.Equal([destination], Directory.GetFileSystemEntries(parent));
            Assert.Equal(0, (int)(File.GetAttributes(destination) & FileAttributes.Hidden));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(argumentLog);
        }
    }

    /// <summary>
    ///     Test that a URL that git could read as an option, a remote helper or a second line is refused
    ///     before git starts.
    /// </summary>
    /// <param name="url">The refused URL.</param>
    [Theory]
    [InlineData("-u=evil")]
    [InlineData("--upload-pack=calc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com/a.git\nhttps://example.com/b.git")]
    [InlineData("ext::sh -c touch% /tmp/pwned")]
    [InlineData("fd::17")]
    [InlineData("EXT::sh -c id")]
    [InlineData("foo_bar::x")]
    [InlineData("a b::x")]
    [InlineData("ext::sh -c x")]
    [InlineData("fd::0")]
    [InlineData("[::1]::x")]
    [InlineData("::x")]
    public void GitClient_Clone_RefusedUrl_ThrowsArgumentExceptionAndRunsNoGit(string url)
    {
        // Arrange
        var invocationLog = TempFilePath();
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var parent = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert: refused with a reason, and git never started
            var ex = Assert.ThrowsAny<ArgumentException>(() => client.Clone(url, Path.Combine(parent, "repo"), TestContext.Current.CancellationToken));
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            Assert.False(File.Exists(invocationLog));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that CheckCloneUrl accepts the usual kinds of repository address.
    /// </summary>
    /// <param name="url">The address.</param>
    [Theory]
    [InlineData("https://github.com/demaconsulting/AgentControl.git")]
    [InlineData("ssh://git@host.example/team/repo.git")]
    [InlineData("git@github.com:demaconsulting/AgentControl.git")]
    [InlineData("C:\\repos\\project")]
    [InlineData("/srv/git/project.git")]
    [InlineData("file:///srv/git/project.git")]
    [InlineData("https://[::1]/r.git")]
    [InlineData("ssh://git@[::1]:22/r.git")]
    [InlineData("[::1]:repo")]
    [InlineData("git@[fe80::1]:org/r.git")]
    [InlineData("C:\\repos\\r")]
    [InlineData("git@host:org/r.git")]
    [InlineData("https://example.com/a::b.git")]
    public void GitClient_CheckCloneUrl_UsualAddress_ReturnsNull(string url)
    {
        // Act / Assert
        Assert.Null(GitClient.CheckCloneUrl(url));
    }

    /// <summary>
    ///     Test that a destination where anything exists - a folder with content, an empty folder or a
    ///     file - is refused before git starts, and that what is there is left alone.
    /// </summary>
    /// <param name="kind">What exists at the destination.</param>
    [Theory]
    [InlineData("folder with a file")]
    [InlineData("empty folder")]
    [InlineData("file")]
    public void GitClient_Clone_DestinationExists_ThrowsArgumentExceptionAndTouchesNothing(string kind)
    {
        // Arrange
        var invocationLog = TempFilePath();
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var existing = Path.Combine(destination, "mine.txt");
        if (kind == "file")
        {
            File.WriteAllText(destination, "precious");
            existing = destination;
        }
        else
        {
            Directory.CreateDirectory(destination);
            if (kind == "folder with a file")
            {
                File.WriteAllText(existing, "precious");
            }
        }

        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert: refused with a reason naming what is there; git never started; it is intact
            var ex = Assert.Throws<ArgumentException>(() => client.Clone("https://example.com/r.git", destination, TestContext.Current.CancellationToken));
            Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(invocationLog));
            Assert.Equal([destination], Directory.GetFileSystemEntries(parent));
            if (kind != "empty folder")
            {
                Assert.Equal("precious", File.ReadAllText(existing));
            }
            else
            {
                Assert.Empty(Directory.GetFileSystemEntries(destination));
            }
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that a destination that is a link (a junction on Windows, a symbolic link elsewhere), or
    ///     a link whose target is gone, is refused, and that the link's target is not touched.
    /// </summary>
    /// <param name="dangling">Whether the link's target is removed before the clone is asked for.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GitClient_Clone_DestinationIsLink_IsRefusedAndTargetUntouched(bool dangling)
    {
        // Arrange: a link in the parent folder that points at a folder with a file in it
        var invocationLog = TempFilePath();
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var parent = CreateTempDirectory();
        var target = Path.Combine(parent, "target");
        var link = Path.Combine(parent, "link");
        Directory.CreateDirectory(target);
        var precious = Path.Combine(target, "mine.txt");
        File.WriteAllText(precious, "precious");
        CreateDirectoryLinkOrSkip(link, target);
        try
        {
            if (dangling)
            {
                File.Delete(precious);
                Directory.Delete(target);
            }

            var client = new GitClient(stub.Path, _logger);

            // Act / Assert: refused as a link, git never started, nothing was created next to it
            var ex = Assert.Throws<ArgumentException>(() => client.Clone("https://example.com/r.git", link, TestContext.Current.CancellationToken));
            Assert.Contains("is a link", ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(invocationLog));
            Assert.DoesNotContain(Directory.GetFileSystemEntries(parent), entry => Path.GetFileName(entry).StartsWith(".agentcontrol-clone-", StringComparison.Ordinal));
            if (!dangling)
            {
                Assert.Equal("precious", File.ReadAllText(precious));
            }
        }
        finally
        {
            stub.Delete();
            RemoveLinkThenFolder(link, parent);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that CheckCloneDestination gives the reason for a destination that is a link, dangling or
    ///     not, and for a folder and a file, without following the link.
    /// </summary>
    [Fact]
    public void GitClient_CheckCloneDestination_DestinationIsLink_ReturnsReason()
    {
        // Arrange
        var parent = CreateTempDirectory();
        var target = Path.Combine(parent, "target");
        var link = Path.Combine(parent, "link");
        var danglingLink = Path.Combine(parent, "dangling");
        var danglingTarget = Path.Combine(parent, "gone");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(danglingTarget);
        CreateDirectoryLinkOrSkip(link, target);
        CreateDirectoryLinkOrSkip(danglingLink, danglingTarget);
        Directory.Delete(danglingTarget);
        try
        {
            // Act / Assert
            Assert.Contains("is a link", GitClient.CheckCloneDestination(link), StringComparison.Ordinal);
            Assert.Contains("is a link", GitClient.CheckCloneDestination(danglingLink), StringComparison.Ordinal);
            Assert.Contains("already exists", GitClient.CheckCloneDestination(target), StringComparison.Ordinal);
            Assert.Null(GitClient.CheckCloneDestination(Path.Combine(parent, "new")));
        }
        finally
        {
            RemoveLinkThenFolder(link, parent);
            RemoveLinkThenFolder(danglingLink, parent);
        }
    }

    /// <summary>
    ///     Test that a destination whose parent folder does not exist, a relative destination, and a
    ///     destination that is a file are refused.
    /// </summary>
    [Fact]
    public void GitClient_Clone_ParentMissingRelativeOrFile_ThrowsArgumentException()
    {
        // Arrange
        var parent = CreateTempDirectory();
        var file = Path.Combine(parent, "plain-file");
        File.WriteAllText(file, "x");
        var stub = GitStub.Create();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act / Assert
            var missing = Assert.Throws<ArgumentException>(() => client.Clone("https://example.com/r.git", Path.Combine(parent, "nope", "repo"), TestContext.Current.CancellationToken));
            Assert.Contains("does not exist", missing.Message, StringComparison.Ordinal);
            var relative = Assert.Throws<ArgumentException>(() => client.Clone("https://example.com/r.git", Path.Combine("relative", "repo"), TestContext.Current.CancellationToken));
            Assert.Contains("absolute", relative.Message, StringComparison.Ordinal);
            var isFile = Assert.Throws<ArgumentException>(() => client.Clone("https://example.com/r.git", file, TestContext.Current.CancellationToken));
            Assert.Contains("is a file", isFile.Message, StringComparison.Ordinal);
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that CheckCloneDestination accepts a folder name that does not exist yet.
    /// </summary>
    [Fact]
    public void GitClient_CheckCloneDestination_NewFolder_ReturnsNull()
    {
        // Arrange
        var parent = CreateTempDirectory();
        try
        {
            // Act / Assert: also with a trailing separator
            Assert.Null(GitClient.CheckCloneDestination(Path.Combine(parent, "new")));
            Assert.Null(GitClient.CheckCloneDestination(Path.Combine(parent, "new") + Path.DirectorySeparatorChar));
        }
        finally
        {
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that CheckCloneDestination refuses an existing empty folder, a folder with content and a
    ///     file, each with a reason that says to choose a name that does not exist yet.
    /// </summary>
    [Fact]
    public void GitClient_CheckCloneDestination_ExistingEmptyFolderOrFile_ReturnsReason()
    {
        // Arrange
        var parent = CreateTempDirectory();
        var empty = Path.Combine(parent, "empty");
        var full = Path.Combine(parent, "full");
        var file = Path.Combine(parent, "file.txt");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, "mine.txt"), "x");
        File.WriteAllText(file, "x");
        try
        {
            // Act
            var emptyReason = GitClient.CheckCloneDestination(empty);
            var emptySeparatorReason = GitClient.CheckCloneDestination(empty + Path.DirectorySeparatorChar);
            var fullReason = GitClient.CheckCloneDestination(full);
            var fileReason = GitClient.CheckCloneDestination(file);

            // Assert
            Assert.Contains("already exists", emptyReason, StringComparison.Ordinal);
            Assert.Contains("does not exist yet", emptyReason, StringComparison.Ordinal);
            Assert.Equal(emptyReason, emptySeparatorReason);
            Assert.Contains("already exists", fullReason, StringComparison.Ordinal);
            Assert.Contains("is a file", fileReason, StringComparison.Ordinal);
        }
        finally
        {
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that a URL with a password never reaches the log, a result or an exception message.
    /// </summary>
    [Fact]
    public void GitClient_Clone_UrlWithPassword_IsNeverWrittenToLogOrExceptionText()
    {
        // Arrange: a clone that fails and echoes the URL, as git does, and a git that cannot be started
        const string password = "Hunter2PasswordValue";
        var url = $"https://alice:{password}@example.com/team/repo.git";
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command(
                "clone",
                Error: $"fatal: unable to access '{url}/': Could not resolve host: example.com",
                ExitCode: 128)
        ]);
        var parent = CreateTempDirectory();
        var logger = new GitClientListLogger();
        try
        {
            var client = new GitClient(stub.Path, logger);
            var missingGit = new GitClient(Path.Combine(parent, "no-such-git.exe"), logger);

            // Act
            var result = client.Clone(url, Path.Combine(parent, "repo"), TestContext.Current.CancellationToken);
            var startFailure = Assert.Throws<InvalidOperationException>(() => missingGit.Clone(url, Path.Combine(parent, "repo2"), TestContext.Current.CancellationToken));

            // Assert: the host and path survive for diagnosis; the password is nowhere
            Assert.False(result.Succeeded);
            Assert.Contains("example.com", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(password, result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(password, startFailure.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(password, startFailure.Message, StringComparison.Ordinal);
            var lines = logger.Inner.Snapshot();
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.DoesNotContain(password, line, StringComparison.Ordinal));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that canceling a clone kills git and removes only the working folder the clone made, even
    ///     though git left read-only files in it, and that a file, a folder and a look-alike folder of
    ///     the user's in the parent folder stay.
    /// </summary>
    [Fact]
    public async Task GitClient_Clone_Canceled_RemovesOnlyItsTempFolder()
    {
        // Arrange: a clone that creates its working folder (with a read-only pack file) and then hangs
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", SleepSeconds: GitStub.HangSeconds, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var keepFile = Path.Combine(parent, "keep.txt");
        var keepFolder = Path.Combine(parent, "keep-folder");
        var lookAlike = Path.Combine(parent, ".agentcontrol-clone-theirs");
        await File.WriteAllTextAsync(keepFile, "mine", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(keepFolder);
        Directory.CreateDirectory(lookAlike);
        await File.WriteAllTextAsync(Path.Combine(lookAlike, "theirs.txt"), "theirs", TestContext.Current.CancellationToken);
        try
        {
            var client = new GitClient(stub.Path, _logger);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var clone = Task.Run(() => client.Clone("https://example.com/r.git", destination, cancellation.Token), TestContext.Current.CancellationToken);
            Assert.True(WaitForReadOnlyPackFileInWorkFolder(parent, lookAlike), "The stub never created its read-only file.");

            // Act: cancel while git is writing
            await cancellation.CancelAsync();

            // Assert: canceled, git killed, only the working folder is gone, the destination never existed
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clone);
            Assert.False(Directory.Exists(destination));
            Assert.Equal(
                new[] { keepFile, keepFolder, lookAlike }.Order(StringComparer.Ordinal).ToArray(),
                Directory.GetFileSystemEntries(parent).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal("mine", await File.ReadAllTextAsync(keepFile, TestContext.Current.CancellationToken));
            Assert.Equal("theirs", await File.ReadAllTextAsync(Path.Combine(lookAlike, "theirs.txt"), TestContext.Current.CancellationToken));
            Assert.False(await stub.RanToTheEndOfItsHangAsync());
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that canceling a clone whose working folder cannot be removed (a file is still held) is
    ///     reported as a cancellation that names the folder left behind, and that the folder is indeed
    ///     still there, while a cancel that does remove its folder is a plain cancellation.
    /// </summary>
    [Fact]
    public async Task GitClient_Clone_CanceledWhenTheFolderCannotBeRemoved_ThrowsCancellationNamingTheLeftover()
    {
        // Arrange: a clone that hangs after creating its folder, and a removal that always fails
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", SleepSeconds: GitStub.HangSeconds, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        try
        {
            var client = new GitClient(stub.Path, _logger)
            {
                DeleteCloneFolder = static _ => throw new IOException("The file is being used by another process.")
            };
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var clone = Task.Run(() => client.Clone("https://example.com/r.git", destination, cancellation.Token), TestContext.Current.CancellationToken);
            Assert.True(WaitForReadOnlyPackFileInWorkFolder(parent, null), "The stub never created its read-only file.");
            var work = Directory.GetDirectories(parent, ".agentcontrol-clone-*").Single();

            // Act
            await cancellation.CancelAsync();

            // Assert: still a cancellation, but one that says what is left, and the folder is left
            var canceled = await Assert.ThrowsAsync<GitCloneCanceledException>(() => clone);
            Assert.Contains(work, canceled.Message, StringComparison.Ordinal);
            Assert.Contains("could not be removed; delete it yourself", canceled.Message, StringComparison.Ordinal);
            Assert.Equal(cancellation.Token, canceled.CancellationToken);
            Assert.IsAssignableFrom<OperationCanceledException>(canceled);
            Assert.True(Directory.Exists(work));
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that a cancel that removes its working folder stays a plain cancellation, with nothing to
    ///     tell the user.
    /// </summary>
    [Fact]
    public async Task GitClient_Clone_CanceledAndFolderRemoved_ThrowsPlainCancellation()
    {
        // Arrange
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", SleepSeconds: GitStub.HangSeconds, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var clone = Task.Run(() => client.Clone("https://example.com/r.git", Path.Combine(parent, "repo"), cancellation.Token), TestContext.Current.CancellationToken);
            Assert.True(WaitForReadOnlyPackFileInWorkFolder(parent, null), "The stub never created its read-only file.");

            // Act
            await cancellation.CancelAsync();

            // Assert: a plain cancellation, and nothing is left
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clone);
            Assert.IsNotType<GitCloneCanceledException>(canceled);
            Assert.Empty(Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that a clone that stops with another exception (git cannot be started) and whose working
    ///     folder cannot be removed says so in the exception's text, keeping the cause.
    /// </summary>
    [Fact]
    public void GitClient_Clone_FailsToStartWhenTheFolderCannotBeRemoved_NamesTheLeftover()
    {
        // Arrange: no git to start, and a removal that always fails
        var parent = CreateTempDirectory();
        try
        {
            var client = new GitClient(Path.Combine(parent, "no-such-git.exe"), _logger)
            {
                DeleteCloneFolder = static _ => throw new UnauthorizedAccessException("Access is denied.")
            };

            // Act
            var failure = Assert.Throws<InvalidOperationException>(
                () => client.Clone("https://example.com/r.git", Path.Combine(parent, "repo"), TestContext.Current.CancellationToken));

            // Assert: the start failure and the leftover are both named, and the cause is kept
            var work = Directory.GetDirectories(parent, ".agentcontrol-clone-*").Single();
            Assert.Contains("no-such-git.exe", failure.Message, StringComparison.Ordinal);
            Assert.Contains(work, failure.Message, StringComparison.Ordinal);
            Assert.Contains("delete it yourself", failure.Message, StringComparison.Ordinal);
            Assert.NotNull(failure.InnerException);
        }
        finally
        {
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that canceling a clone whose working folder holds a junction (also one nested below it)
    ///     to a folder of the user's removes the junctions as links and leaves the target folder, its
    ///     files and their read-only attributes untouched.
    /// </summary>
    [Fact]
    public async Task GitClient_Clone_Canceled_LinkInWorkFolder_RemovesTheLinkAndLeavesItsTargetUntouched() =>
        await AssertCanceledCloneRemovesLinksAndKeepsTargetAsync((work, target) =>
        {
            CreateDirectoryLinkOrSkip(Path.Combine(work, "linked"), target);
            CreateDirectoryLinkOrSkip(Path.Combine(work, ".git", "nested-linked"), target);
        });

    /// <summary>
    ///     Test that canceling a clone whose working folder holds a file symbolic link and a directory
    ///     symbolic link to the user's files removes both as links and leaves the targets untouched; the
    ///     test is skipped where the platform does not allow creating symbolic links.
    /// </summary>
    [Fact]
    public async Task GitClient_Clone_Canceled_SymbolicLinksInWorkFolder_AreRemovedAndTargetsUntouched()
    {
        // Arrange: skip before anything starts when this environment cannot create symbolic links
        var probeFolder = CreateTempDirectory();
        try
        {
            var probeTarget = Path.Combine(probeFolder, "target.txt");
            await File.WriteAllTextAsync(probeTarget, "probe", TestContext.Current.CancellationToken);
            CreateFileSymbolicLinkOrSkip(Path.Combine(probeFolder, "link.txt"), probeTarget);
        }
        finally
        {
            GitSourceProvider.DeleteDirectory(probeFolder);
        }

        await AssertCanceledCloneRemovesLinksAndKeepsTargetAsync((work, target) =>
        {
            CreateFileSymbolicLinkOrSkip(Path.Combine(work, "linked-file.txt"), Path.Combine(target, "readonly.txt"));
            CreateFileSymbolicLinkOrSkip(Path.Combine(work, ".git", "nested-linked-file.txt"), Path.Combine(target, "keep.txt"));
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(work, "linked-folder"), target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
            }
        });
    }

    /// <summary>
    ///     Runs a clone that hangs, adds links to the user's folder in its working folder, cancels, and
    ///     checks that nothing is left of the clone while the folder the links point at is unchanged.
    /// </summary>
    /// <param name="addLinks">Adds the links; receives the working folder and the target folder.</param>
    private async Task AssertCanceledCloneRemovesLinksAndKeepsTargetAsync(Action<string, string> addLinks)
    {
        // Arrange: a clone that creates its working folder and then hangs, and a folder of the user's
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", SleepSeconds: GitStub.HangSeconds, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        var target = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var keepFile = Path.Combine(target, "keep.txt");
        var readOnlyFile = Path.Combine(target, "readonly.txt");
        var innerFile = Path.Combine(target, "inner", "inner.txt");
        await File.WriteAllTextAsync(keepFile, "theirs", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(readOnlyFile, "locked", TestContext.Current.CancellationToken);
        File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);
        Directory.CreateDirectory(Path.GetDirectoryName(innerFile)!);
        await File.WriteAllTextAsync(innerFile, "deep", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task? clone = null;
        try
        {
            var client = new GitClient(stub.Path, _logger);
            clone = Task.Run(() => client.Clone("https://example.com/r.git", destination, cancellation.Token), TestContext.Current.CancellationToken);
            Assert.True(WaitForReadOnlyPackFileInWorkFolder(parent, null), "The stub never created its read-only file.");

            // The stub is still hanging, so adding links to the working folder cannot race with git
            var work = Directory.GetDirectories(parent, ".agentcontrol-clone-*").Single();
            addLinks(work, target);

            // Act: cancel while git is writing
            await cancellation.CancelAsync();

            // Assert: canceled, the working folder and the links in it are gone, the target is untouched
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clone);
            Assert.False(Directory.Exists(destination));
            Assert.Empty(Directory.GetFileSystemEntries(parent));
            Assert.Equal("theirs", await File.ReadAllTextAsync(keepFile, TestContext.Current.CancellationToken));
            Assert.Equal("locked", await File.ReadAllTextAsync(readOnlyFile, TestContext.Current.CancellationToken));
            Assert.NotEqual(0, (int)(File.GetAttributes(readOnlyFile) & FileAttributes.ReadOnly));
            Assert.Equal("deep", await File.ReadAllTextAsync(innerFile, TestContext.Current.CancellationToken));
        }
        finally
        {
            // Stop a clone left running by a skipped or failed test before removing the folders
            await cancellation.CancelAsync();
            if (clone is not null)
            {
                await Task.WhenAny(clone);
            }

            stub.Delete();
            if (File.Exists(readOnlyFile))
            {
                File.SetAttributes(readOnlyFile, FileAttributes.Normal);
            }

            GitSourceProvider.DeleteDirectory(parent);
            GitSourceProvider.DeleteDirectory(target);
        }
    }

    /// <summary>
    ///     Test that a clone that fails (git exits with an error) removes only its own working folder,
    ///     which git left behind, and returns git's reason; the user's other entries stay.
    /// </summary>
    [Fact]
    public void GitClient_Clone_Fails_RemovesItsTempFolderAndReturnsGitsReason()
    {
        // Arrange: a clone that leaves its target behind and fails
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", Error: "fatal: repository not found", ExitCode: 128, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var keepFile = Path.Combine(parent, "keep.txt");
        File.WriteAllText(keepFile, "mine");
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.Clone("https://example.com/r.git", destination, TestContext.Current.CancellationToken);

            // Assert: the failure is a result with git's text, and only the user's file is left
            Assert.False(result.Succeeded);
            Assert.Equal(128, result.ExitCode);
            Assert.Contains("repository not found", result.StandardError, StringComparison.Ordinal);
            Assert.False(Directory.Exists(destination));
            Assert.Equal([keepFile], Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that a clone that outlives the clone limit is stopped, reported as timed out and its
    ///     working folder removed.
    /// </summary>
    [Fact]
    public async Task GitClient_Clone_TimesOut_StopsGitAndRemovesItsTempFolder()
    {
        // Arrange: a clone that hangs well past a one-second clone limit
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", SleepSeconds: GitStub.HangSeconds, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                new GitClientTimeouts(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10))
                {
                    CloneRepository = TimeSpan.FromSeconds(1)
                });

            // Act
            var failure = Assert.Throws<GitCommandTimeoutException>(() => client.Clone("https://example.com/r.git", destination, TestContext.Current.CancellationToken));

            // Assert: reported as a timeout of the clone limit, nothing left behind, git never finished
            Assert.Equal(TimeSpan.FromSeconds(1), failure.Timeout);
            Assert.Contains("git clone", failure.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(destination));
            Assert.Empty(Directory.GetFileSystemEntries(parent));
            Assert.False(await stub.RanToTheEndOfItsHangAsync());
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that something that appears at the destination while git is cloning - a folder with
    ///     content or an <em>empty</em> folder, which a plain Unix rename would silently replace - is never
    ///     replaced or changed: the clone is not moved there, the failure is reported without throwing, and
    ///     the working folder is removed. Runs the operating system's own no-replace rename on every
    ///     platform.
    /// </summary>
    /// <param name="withContent">Whether the folder that appears holds a file; otherwise it is
    ///     empty.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GitClient_Clone_DestinationAppearsWhileCloning_FailsClearlyAndTouchesNothingOfTheUsers(bool withContent)
    {
        // Arrange: a clone that takes two seconds, during which the user creates the destination
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", SleepSeconds: 2, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        try
        {
            var client = new GitClient(stub.Path, _logger);
            var clone = Task.Run(() => client.Clone("https://example.com/r.git", destination, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            Assert.True(WaitForReadOnlyPackFileInWorkFolder(parent, null), "The stub never created its read-only file.");
            Directory.CreateDirectory(destination);
            var userFile = Path.Combine(destination, "mine.txt");
            if (withContent)
            {
                await File.WriteAllTextAsync(userFile, "precious", TestContext.Current.CancellationToken);
            }

            // Act
            var result = await clone;

            // Assert: a failed result naming the folder, what the user made intact, the working folder gone
            Assert.False(result.Succeeded);
            Assert.Contains(destination, result.StandardError, StringComparison.Ordinal);
            Assert.Contains("was created while the repository was being cloned", result.StandardError, StringComparison.Ordinal);
            Assert.Equal([destination], Directory.GetFileSystemEntries(parent));
            if (withContent)
            {
                Assert.Equal("precious", await File.ReadAllTextAsync(userFile, TestContext.Current.CancellationToken));
                Assert.Equal([userFile], Directory.GetFileSystemEntries(destination));
            }
            else
            {
                Assert.Empty(Directory.GetFileSystemEntries(destination));
            }
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that a working folder name under which something already exists - a folder with content or
    ///     a file - is never used, changed or deleted: the clone takes the next name, and the earlier
    ///     entries are left exactly as they were.
    /// </summary>
    [Fact]
    public void GitClient_Clone_WorkFolderNameTaken_IsNeverUsedOrDeleted()
    {
        // Arrange: the first two names are taken by somebody else's folder and file; the third is free
        var argumentLog = TempFilePath();
        var stub = GitStub.Create(
            argumentLogPath: argumentLog,
            extraCommands: [new GitStub.Command("clone", Error: "Cloning into 'repo'...", CreatesDestination: true)]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var takenFolder = Path.Combine(parent, ".agentcontrol-clone-first");
        var takenFile = Path.Combine(parent, ".agentcontrol-clone-second");
        Directory.CreateDirectory(takenFolder);
        var folderContent = Path.Combine(takenFolder, "theirs.txt");
        File.WriteAllText(folderContent, "precious");
        File.WriteAllText(takenFile, "also precious");
        var names = new Queue<string>(["first", "second", "third"]);
        try
        {
            var client = new GitClient(stub.Path, _logger) { CloneWorkFolderNameSource = names.Dequeue };

            // Act
            var result = client.Clone("https://example.com/r.git", destination, TestContext.Current.CancellationToken);

            // Assert: git cloned into the third name, and the two that were taken are untouched
            Assert.True(result.Succeeded);
            var line = Assert.Single(ReadLines(argumentLog));
            Assert.EndsWith(Path.Combine(parent, ".agentcontrol-clone-third"), line, StringComparison.Ordinal);
            Assert.Equal("precious", File.ReadAllText(folderContent));
            Assert.Equal("also precious", File.ReadAllText(takenFile));
            Assert.True(File.Exists(Path.Combine(destination, ".git", "objects", "pack", "pack-1.pack")));
            Assert.Equal(3, Directory.GetFileSystemEntries(parent).Length);
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(argumentLog);
        }
    }

    /// <summary>
    ///     Test that when every name tried for the working folder is taken, the clone fails with a result
    ///     that says so, git never starts, and what exists under the name is never deleted.
    /// </summary>
    [Fact]
    public void GitClient_Clone_EveryWorkFolderNameTaken_FailsWithoutStartingGitOrTouchingTheFolder()
    {
        // Arrange: a folder with content under the only name the source ever offers
        var invocationLog = TempFilePath();
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var parent = CreateTempDirectory();
        var taken = Path.Combine(parent, ".agentcontrol-clone-same");
        Directory.CreateDirectory(taken);
        var content = Path.Combine(taken, "theirs.txt");
        File.WriteAllText(content, "precious");
        try
        {
            var client = new GitClient(stub.Path, _logger) { CloneWorkFolderNameSource = static () => "same" };

            // Act
            var result = client.Clone("https://example.com/r.git", Path.Combine(parent, "repo"), TestContext.Current.CancellationToken);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Contains("Could not create a working folder", result.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(invocationLog));
            Assert.Equal("precious", File.ReadAllText(content));
            Assert.Equal([taken], Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that a platform (or file system) that has no rename that refuses to replace does not
    ///     complete the clone - it never falls back to a rename that could replace - and removes only the
    ///     working folder.
    /// </summary>
    [Fact]
    public void GitClient_Clone_NoReplaceRenameUnsupported_RefusesAndRemovesOnlyTheWorkFolder()
    {
        // Arrange: a clone that works, on a platform whose no-replace rename is unavailable
        var stub = GitStub.Create(
            extraCommands: [new GitStub.Command("clone", Error: "Cloning into 'repo'...", CreatesDestination: true)]);
        var parent = CreateTempDirectory();
        var destination = Path.Combine(parent, "repo");
        var keepFile = Path.Combine(parent, "keep.txt");
        File.WriteAllText(keepFile, "mine");
        try
        {
            var client = new GitClient(stub.Path, _logger)
            {
                NoReplaceMove = static (_, _) => GitClient.NoReplaceMoveOutcome.Unsupported
            };

            // Act
            var result = client.Clone("https://example.com/r.git", destination, TestContext.Current.CancellationToken);

            // Assert: a failed result that explains it, no destination, and only the user's file left
            Assert.False(result.Succeeded);
            Assert.Contains("cannot rename a folder without the risk of replacing", result.StandardError, StringComparison.Ordinal);
            Assert.False(Directory.Exists(destination));
            Assert.Equal([keepFile], Directory.GetFileSystemEntries(parent));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that the operating system's own rename that refuses to replace - the one a clone uses -
    ///     moves a folder to a name that is free, and reports an existing empty folder as a conflict
    ///     without changing it or the source (on Windows, Linux and macOS alike).
    /// </summary>
    [Fact]
    public void GitClient_Clone_NativeNoReplaceMove_MovesToFreeNameAndNeverReplacesAnEmptyFolder()
    {
        // Arrange
        var parent = CreateTempDirectory();
        var source = Path.Combine(parent, "source");
        var free = Path.Combine(parent, "free");
        var occupied = Path.Combine(parent, "occupied");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        Directory.CreateDirectory(occupied);
        var move = new GitClient("git", _logger).NoReplaceMove;
        try
        {
            // Act
            var conflict = move(source, occupied);
            var moved = move(source, free);

            // Assert: the empty folder was not replaced, and the free name received the folder
            Assert.Equal(GitClient.NoReplaceMoveOutcome.DestinationExists, conflict);
            Assert.Equal(GitClient.NoReplaceMoveOutcome.Moved, moved);
            Assert.Empty(Directory.GetFileSystemEntries(occupied));
            Assert.False(Directory.Exists(source));
            Assert.Equal("a", File.ReadAllText(Path.Combine(free, "a.txt")));
        }
        finally
        {
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that the messages of a canceled and of a timed-out clone never contain the credentials of
    ///     the address, and neither does the log.
    /// </summary>
    [Fact]
    public async Task GitClient_Clone_TimeoutAndCancelMessages_NeverContainTheCredentials()
    {
        // Arrange
        const string password = "Hunter2PasswordValue";
        var url = $"https://alice:{password}@example.com/team/repo.git";
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", SleepSeconds: GitStub.HangSeconds, CreatesDestination: true)
        ]);
        var parent = CreateTempDirectory();
        var logger = new GitClientListLogger();
        try
        {
            var client = new GitClient(
                stub.Path,
                logger,
                new GitClientTimeouts(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10))
                {
                    CloneRepository = TimeSpan.FromSeconds(1)
                });

            // Act: one clone times out, another is canceled
            var timeout = Assert.Throws<GitCommandTimeoutException>(() => client.Clone(url, Path.Combine(parent, "one"), TestContext.Current.CancellationToken));
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var slowClient = new GitClient(stub.Path, logger);
            var second = Task.Run(() => slowClient.Clone(url, Path.Combine(parent, "two"), cancellation.Token), TestContext.Current.CancellationToken);
            Assert.True(WaitForReadOnlyPackFileInWorkFolder(parent, null), "The stub never created its read-only file.");
            await cancellation.CancelAsync();
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

            // Assert
            Assert.DoesNotContain(password, timeout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(password, canceled.ToString(), StringComparison.Ordinal);
            var lines = logger.Inner.Snapshot();
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.DoesNotContain(password, line, StringComparison.Ordinal));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that a token-only address, one with a token in the query string and one with only a user
    ///     name are redacted from every log message and result, while the host stays.
    /// </summary>
    /// <param name="url">The address.</param>
    /// <param name="secret">The part of it that must never be shown.</param>
    [Theory]
    [InlineData("https://TokenValue1234567@example.com/team/repo.git", "TokenValue1234567")]
    [InlineData("https://example.com/team/repo.git?token=TokenValue1234567", "TokenValue1234567")]
    [InlineData("https://some-user-name@example.com/team/repo.git", "some-user-name")]
    public void GitClient_Clone_TokenOnlyAndQueryStringUrls_AreRedactedEverywhere(string url, string secret)
    {
        // Arrange: a clone that fails and echoes the address, as git does
        var stub = GitStub.Create(extraCommands:
        [
            new GitStub.Command("clone", Error: $"fatal: unable to access '{url}': Could not resolve host", ExitCode: 128)
        ]);
        var parent = CreateTempDirectory();
        var logger = new GitClientListLogger();
        try
        {
            var client = new GitClient(stub.Path, logger);

            // Act
            var result = client.Clone(url, Path.Combine(parent, "repo"), TestContext.Current.CancellationToken);

            // Assert: the host survives for diagnosis; the secret is in no result and no log line
            Assert.False(result.Succeeded);
            Assert.Contains("example.com", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, result.StandardError, StringComparison.Ordinal);
            var lines = logger.Inner.Snapshot();
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that a clone runs under the clone limit, not the much shorter local-command limit.
    /// </summary>
    [Fact]
    public void GitClient_Clone_UsesCloneTimeoutNotLocalTimeout()
    {
        // Arrange: a clone that takes two seconds and a one-second local limit
        var stub = GitStub.Create(extraCommands: [new GitStub.Command("clone", SleepSeconds: 2)]);
        var parent = CreateTempDirectory();
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                new GitClientTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10)));

            // Act
            var result = client.Clone("https://example.com/r.git", Path.Combine(parent, "repo"), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.Succeeded);
            Assert.True(stub.WroteFinishedMarker());
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
        }
    }

    /// <summary>
    ///     Test that constructing a client with a zero switch or clone limit is refused.
    /// </summary>
    [Fact]
    public void GitClient_Constructor_NonPositiveSwitchOrCloneLimit_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var valid = new GitClientTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new GitClient("git", _logger, valid with { SwitchBranch = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GitClient("git", _logger, valid with { CloneRepository = TimeSpan.Zero }));
    }

    /// <summary>
    ///     Test that listing, switching and cloning on a guarded thread are refused before anything is
    ///     read or started.
    /// </summary>
    [Fact]
    public void GitClient_BranchAndCloneCommands_OnGuardedThread_ThrowBlockingCallOnGuardedThread()
    {
        // Arrange
        var invocationLog = TempFilePath();
        var stub = GitStub.Create(invocationLogPath: invocationLog);
        var parent = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);
            var refusals = new List<Exception?>();

            // Act
            using (BlockingCallGuard.GuardCurrentThread())
            {
                refusals.Add(Record.Exception(() => client.GetBranches(parent, TestContext.Current.CancellationToken)));
                refusals.Add(Record.Exception(() => client.SwitchBranch(parent, "x", createTrackingFromRemote: false, TestContext.Current.CancellationToken)));
                refusals.Add(Record.Exception(() => client.Clone("https://example.com/r.git", Path.Combine(parent, "repo"), TestContext.Current.CancellationToken)));
                refusals.Add(Record.Exception(() => GitClient.CheckCloneDestination(Path.Combine(parent, "repo"))));
            }

            var recorded = BlockingCallGuard.TakeViolations();

            // Assert
            Assert.All(refusals, refusal => Assert.IsType<BlockingCallOnGuardedThreadException>(refusal));
            Assert.Equal(4, recorded.Count);
            Assert.False(File.Exists(invocationLog));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(invocationLog);
        }
    }

    /// <summary>
    ///     Test that output with non-ASCII characters is decoded as UTF-8, whatever the console code
    ///     page is: branch names and git's messages can hold any character.
    /// </summary>
    [Fact]
    public void GitClient_Pull_NonAsciiOutput_IsDecodedAsUtf8()
    {
        // Arrange: a pull whose output is read from a UTF-8 file by the stub; the non-ASCII characters are
        // an umlaut pair, an en dash and two Chinese characters, given by code so no foreign word is spelled
        var special = new string([(char)0x00FC, (char)0x00F6, (char)0x2013, (char)0x4E2D, (char)0x6587]);
        var text = "Updated branch " + special;
        var errorText = "Problem " + special;
        var stub = GitStub.Create(extraCommands: [new GitStub.Command("pull", Output: text, Error: errorText)]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.Pull(repoDir, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(text, result.StandardOutput.Trim());
            Assert.Equal(errorText, result.StandardError.Trim());
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }

    /// <summary>
    ///     Test that a clone never prompts on a terminal and may use only the allowed transports.
    /// </summary>
    [Fact]
    public void GitClient_Clone_SetsPromptOffAndAllowedProtocols()
    {
        // Arrange
        var environmentLog = TempFilePath();
        var stub = GitStub.Create(
            environmentLogPath: environmentLog,
            environmentNames: ["GIT_TERMINAL_PROMPT", "GIT_ALLOW_PROTOCOL"],
            extraCommands: [new GitStub.Command("clone")]);
        var parent = CreateTempDirectory();
        try
        {
            var client = new GitClient(stub.Path, _logger);

            // Act
            var result = client.Clone("https://example.com/r.git", Path.Combine(parent, "repo"), TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(
                ["GIT_TERMINAL_PROMPT=0", "GIT_ALLOW_PROTOCOL=file:git:http:https:ssh"],
                ReadLines(environmentLog));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(environmentLog);
        }
    }

    /// <summary>
    ///     Test that the other commands never prompt either, but do not restrict the transports (they
    ///     use no network except a pull, which uses the repository's own remotes).
    /// </summary>
    [Fact]
    public void GitClient_SwitchAndList_SetPromptOff_NoProtocolRestriction()
    {
        // Arrange: the user's own protocol allow-list is removed for the test, so only the client can set it
        var environmentLog = TempFilePath();
        var stub = GitStub.Create(
            environmentLogPath: environmentLog,
            environmentNames: ["GIT_TERMINAL_PROMPT", "GIT_ALLOW_PROTOCOL"],
            extraCommands: [new GitStub.Command("switch")]);
        var repoDir = CreateTempDirectory();
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                extraEnvironment: new Dictionary<string, string?> { ["GIT_ALLOW_PROTOCOL"] = null });

            // Act
            _ = client.SwitchBranch(repoDir, "other", createTrackingFromRemote: false, TestContext.Current.CancellationToken);
            _ = client.IsWorkingTreeClean(repoDir, TestContext.Current.CancellationToken);

            // Assert: one pair of lines per invocation
            Assert.Equal(
                [
                    "GIT_TERMINAL_PROMPT=0", "GIT_ALLOW_PROTOCOL=<unset>",
                    "GIT_TERMINAL_PROMPT=0", "GIT_ALLOW_PROTOCOL=<unset>"
                ],
                ReadLines(environmentLog));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
            File.Delete(environmentLog);
        }
    }

    /// <summary>
    ///     Test that the user's credential helper, askpass and ssh settings reach git unchanged, so a
    ///     sign-in window or an ssh agent keeps working.
    /// </summary>
    [Fact]
    public void GitClient_UserAskpassGcmAndSshSettings_AreLeftAlone()
    {
        // Arrange
        var environmentLog = TempFilePath();
        var stub = GitStub.Create(
            environmentLogPath: environmentLog,
            environmentNames: ["GCM_INTERACTIVE", "GIT_ASKPASS", "SSH_ASKPASS", "GIT_SSH_COMMAND"],
            extraCommands: [new GitStub.Command("clone")]);
        var parent = CreateTempDirectory();
        try
        {
            var client = new GitClient(
                stub.Path,
                _logger,
                extraEnvironment: new Dictionary<string, string?>
                {
                    ["GCM_INTERACTIVE"] = "always",
                    ["GIT_ASKPASS"] = "my-askpass",
                    ["SSH_ASKPASS"] = null,
                    ["GIT_SSH_COMMAND"] = "ssh -i my-key"
                });

            // Act
            _ = client.Clone("https://example.com/r.git", Path.Combine(parent, "repo"), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(
                ["GCM_INTERACTIVE=always", "GIT_ASKPASS=my-askpass", "SSH_ASKPASS=<unset>", "GIT_SSH_COMMAND=ssh -i my-key"],
                ReadLines(environmentLog));
        }
        finally
        {
            stub.Delete();
            GitSourceProvider.DeleteDirectory(parent);
            File.Delete(environmentLog);
        }
    }

    /// <summary>
    ///     Test that the base environment policy removes the variables that redirect which repository
    ///     or git programs a command uses and the ones that trace git's traffic, sets the prompt off,
    ///     and leaves everything else alone.
    /// </summary>
    [Fact]
    public void GitClient_ApplyBaseEnvironment_RemovesRepositoryRedirectingVariables()
    {
        // Arrange: a copy of an environment, never the process's own
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_DIR"] = "elsewhere",
            ["GIT_WORK_TREE"] = "elsewhere",
            ["GIT_INDEX_FILE"] = "elsewhere",
            ["GIT_OBJECT_DIRECTORY"] = "elsewhere",
            ["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = "elsewhere",
            ["GIT_CEILING_DIRECTORIES"] = "elsewhere",
            ["GIT_EXEC_PATH"] = "elsewhere",
            ["GIT_TRACE"] = "1",
            ["GIT_TRACE_CURL"] = "1",
            ["GIT_CURL_VERBOSE"] = "1",
            ["GIT_TERMINAL_PROMPT"] = "1",
            ["GIT_ASKPASS"] = "my-askpass",
            ["GCM_INTERACTIVE"] = "always",
            ["PATH"] = "somewhere",
            ["LC_ALL"] = "de_DE.UTF-8"
        };

        // Act
        GitClient.ApplyBaseEnvironment(environment);

        // Assert
        Assert.Equal(
            new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["GCM_INTERACTIVE"] = "always",
                ["GIT_ASKPASS"] = "my-askpass",
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["LC_ALL"] = "de_DE.UTF-8",
                ["PATH"] = "somewhere"
            },
            new SortedDictionary<string, string?>(environment, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Test that an ssh address whose host starts with a dash, which ssh would read as an option, is
    ///     refused, and that a dash elsewhere in the address is not.
    /// </summary>
    [Theory]
    [InlineData("ssh://-oProxyCommand=calc/repo.git", true)]
    [InlineData("ssh://git@-oProxyCommand=calc/repo.git", true)]
    [InlineData("SSH://-host", true)]
    [InlineData("ssh://git@my-host.example/team/repo.git", false)]
    [InlineData("ssh://git@host.example/-team/-repo.git", false)]
    [InlineData("ssh://user@host.example:2222/repo.git", false)]
    public void GitClient_CheckCloneUrl_SshHostStartingWithDash_IsRefused(string url, bool refused)
    {
        // Act
        var problem = GitClient.CheckCloneUrl(url);

        // Assert
        Assert.Equal(refused, problem is not null);
    }

    /// <summary>
    ///     Gets a path for a temporary file that does not exist yet.
    /// </summary>
    /// <returns>The path.</returns>
    private static string TempFilePath() =>
        Path.Combine(Path.GetTempPath(), "agentcontrol_git_client_file_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     Reads the non-blank lines of a stub log file; none when the file does not exist.
    /// </summary>
    /// <param name="path">The log file.</param>
    /// <returns>The trimmed lines.</returns>
    private static string[] ReadLines(string path) =>
        File.Exists(path)
            ? [.. File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0)]
            : [];

    /// <summary>
    ///     Waits until a stub clone has created its hidden working folder next to the destination, with
    ///     the read-only pack file in it.
    /// </summary>
    /// <param name="parent">The folder the destination is in.</param>
    /// <param name="ignoredFolder">A folder of the parent to disregard (a look-alike of the user's), or
    ///     <see langword="null"/> for none.</param>
    /// <returns><see langword="true"/> when the file appeared and is read-only within the wait.</returns>
    private static bool WaitForReadOnlyPackFileInWorkFolder(string parent, string? ignoredFolder) =>
        SpinWait.SpinUntil(
            () => Directory.GetDirectories(parent, ".agentcontrol-clone-*")
                .Where(folder => !string.Equals(folder, ignoredFolder, StringComparison.Ordinal))
                .Select(folder => Path.Combine(folder, ".git", "objects", "pack", "pack-1.pack"))
                .Any(pack => File.Exists(pack) && (File.GetAttributes(pack) & FileAttributes.ReadOnly) != 0),
            TimeSpan.FromSeconds(5));

    /// <summary>
    ///     Creates a file symbolic link or skips the test when the platform does not allow it.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="target">The file it points at.</param>
    private static void CreateFileSymbolicLinkOrSkip(string linkPath, string target)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
        }
    }

    /// <summary>
    ///     Creates a link to a folder - a junction on Windows (it needs no privilege), a symbolic link
    ///     elsewhere - or skips the test when the platform does not allow it.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="target">The existing folder it points at.</param>
    private static void CreateDirectoryLinkOrSkip(string linkPath, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Skip($"Cannot create symbolic links in this environment: {ex.Message}");
            }

            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/c");
        // cspell:disable-next-line
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a junction.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating the junction failed ({process.ExitCode}): {output}");
        }
    }

    /// <summary>
    ///     Removes a link (never its target) and then the folder that held it.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="folder">The folder to delete afterwards.</param>
    private static void RemoveLinkThenFolder(string link, string folder)
    {
        try
        {
            Directory.Delete(link);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(link);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                // Best effort: the temporary folder is cleaned up below
            }
        }

        GitSourceProvider.DeleteDirectory(folder);
    }
}
