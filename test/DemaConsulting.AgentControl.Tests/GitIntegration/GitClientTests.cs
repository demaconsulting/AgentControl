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
            var isClean = client.IsWorkingTreeClean(repoDir);

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
            var isClean = client.IsWorkingTreeClean(repoDir);

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
            var ex = Assert.Throws<InvalidOperationException>(() => client.IsWorkingTreeClean(repoDir));
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
            var result = client.Pull(repoDir);

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
            var result = client.Pull(repoDir);

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
            Assert.Throws<InvalidOperationException>(() => client.IsWorkingTreeClean(repoDir));
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
            var hash = client.GetHeadCommitHash(repoDir);

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
            var ex = Assert.Throws<InvalidOperationException>(() => client.GetHeadCommitHash(repoDir));
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
            var result = client.HasCommittedAgentFiles(repoDir);

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
            var result = client.HasCommittedAgentFiles(repoDir);

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
            Assert.Throws<InvalidOperationException>(() => client.HasCommittedAgentFiles(repoDir));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
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
            var branch = client.GetCurrentBranch(repoDir);

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
            var branch = client.GetCurrentBranch(repoDir);

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
            var branch = client.GetCurrentBranch(repoDir);

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
            var branch = client.GetCurrentBranch(repoDir);

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
            var branch = client.GetCurrentBranch(repoDir);

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
            Assert.Throws<InvalidOperationException>(() => client.GetCurrentBranch(repoDir));
        }
        finally
        {
            stub.Delete();
            Directory.Delete(repoDir, recursive: true);
        }
    }
}


