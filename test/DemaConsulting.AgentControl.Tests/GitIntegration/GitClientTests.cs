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
}
