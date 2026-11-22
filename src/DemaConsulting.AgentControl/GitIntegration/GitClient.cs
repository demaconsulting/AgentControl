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
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.Utilities;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.GitIntegration;

/// <summary>
///     How long a <see cref="GitClient"/> lets a git command run before it stops it.
/// </summary>
/// <remarks>
///     A git command against a local working tree normally answers in well under a second, but one on
///     a network share or a huge tree, or a git that is stuck (a lock held by a crashed process, a
///     credential helper waiting for input nobody sees), may never answer. Without a limit such a
///     command holds its caller - and the worker slot it runs in - forever.
/// </remarks>
/// <param name="LocalCommand">The limit for every command that only reads or changes the local
///     repository (status, branch, <c>HEAD</c>, the committed-files listing, stopping the tracking of agent folders). Must be
///     positive.</param>
/// <param name="Pull">The limit for <c>git pull</c>, which talks to a remote and may wait for the
///     credential helper's own sign-in window. Must be positive.</param>
/// <param name="Stop">How long to wait for a stopped command to end. Must be positive.</param>
internal sealed record GitClientTimeouts(TimeSpan LocalCommand, TimeSpan Pull, TimeSpan Stop)
{
    /// <summary>
    ///     Gets the limits the application uses: a minute for a local command (enough for a cold
    ///     <c>git status</c> of a large tree on a network share), ten minutes for a pull (network work
    ///     that may include signing in), and ten seconds for a stopped command to end.
    /// </summary>
    public static GitClientTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(10));
}

/// <summary>
///     Thrown by <see cref="GitClient"/> when a git command did not finish within its time limit and
///     was stopped.
/// </summary>
/// <remarks>
///     Derives from <see cref="InvalidOperationException"/>, the type every <see cref="GitClient"/>
///     method already throws when git cannot answer, so existing callers keep treating it as "git could
///     not tell"; a caller that wants to tell the user that git hung catches this type first.
/// </remarks>
internal sealed class GitCommandTimeoutException : InvalidOperationException
{
    /// <summary>
    ///     Initializes the exception with a default message and no time limit.
    /// </summary>
    public GitCommandTimeoutException()
        : base("A git command did not finish within its time limit and was stopped.")
    {
    }

    /// <summary>
    ///     Initializes the exception with a message and no time limit.
    /// </summary>
    /// <param name="message">What timed out.</param>
    public GitCommandTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes the exception with a message, no time limit and the exception that caused it.
    /// </summary>
    /// <param name="message">What timed out.</param>
    /// <param name="innerException">The cause.</param>
    public GitCommandTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Initializes the exception with a message and the limit that was exceeded.
    /// </summary>
    /// <param name="message">What timed out.</param>
    /// <param name="timeout">The time limit the command exceeded.</param>
    public GitCommandTimeoutException(string message, TimeSpan timeout)
        : base(message)
    {
        Timeout = timeout;
    }

    /// <summary>
    ///     Gets the time limit the command exceeded, or <see cref="TimeSpan.Zero"/> when not known.
    /// </summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
///     Result of running a single git subcommand: exit code plus captured stdout/stderr.
/// </summary>
/// <param name="ExitCode">The process exit code; <c>0</c> conventionally means success.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    ///     Gets a value indicating whether the command completed successfully (exit code 0).
    /// </summary>
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
///     Shells out to a git executable to check working-tree cleanliness and perform a pull.
/// </summary>
/// <remarks>
///     Per architecture.md, git status gates whether AgentControl offers a "pull" action (a
///     dirty working tree is not pulled automatically), and the git executable path is
///     user-configurable rather than hardcoded, defaulting to <c>"git"</c> so normal <c>PATH</c>
///     resolution applies. The executable path is a constructor parameter specifically so tests
///     can substitute a stub script/executable instead of depending on a real git installation.
///     Not thread-safe for concurrent calls against the same repository path (concurrent git
///     invocations against one working tree are inherently unsafe regardless of this wrapper).
///     Every command runs under a time limit (see <see cref="GitClientTimeouts"/>): a command that
///     does not finish in time is stopped together with every process it started and reported as a
///     <see cref="GitCommandTimeoutException"/>; a canceled command is stopped the same way. Every
///     method blocks its calling thread until git answers, so it must be called off the UI thread; a
///     call on a thread guarded by <see cref="BlockingCallGuard"/> is refused before git starts.
/// </remarks>
internal sealed class GitClient
{
    /// <summary>
    ///     Wall-clock duration threshold above which <see cref="RunGit"/> logs a warning that the
    ///     git process took unexpectedly long to exit, to help distinguish a hung process from a
    ///     fast failure when investigating the intermittent exit-code -1 flakiness described in
    ///     <c>.agent-logs/implementation-agentcontrol-v1-final-3e91c7.md</c>.
    /// </summary>
    private static readonly TimeSpan SlowExitWarningThreshold = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Path (or bare command name, resolved via <c>PATH</c>) to the git executable this
    ///     client invokes.
    /// </summary>
    private readonly string _gitExecutablePath;

    /// <summary>
    ///     Logger used to record process-launch diagnostics (arguments, exit code, timing,
    ///     stderr, and native error codes) for <see cref="RunGit"/>.
    /// </summary>
    private readonly ILogger<GitClient> _logger;

    /// <summary>
    ///     The time limits of the commands this client runs; see the constructor.
    /// </summary>
    private readonly GitClientTimeouts _timeouts;

    /// <summary>
    ///     Initializes a new <see cref="GitClient"/>.
    /// </summary>
    /// <param name="gitExecutablePath">
    ///     Path or command name of the git executable to invoke. Defaults to <c>"git"</c>, which
    ///     resolves via the process <c>PATH</c>. Tests may substitute a stub script/executable
    ///     here to avoid depending on a real git installation or repository.
    /// </param>
    /// <param name="logger">
    ///     Logger for process-launch diagnostics, or <see langword="null"/> to fall back to
    ///     <see cref="AppLogging.Factory"/>. Accepting the portable
    ///     <see cref="ILogger{TCategoryName}"/> abstraction (rather than a concrete Serilog type)
    ///     keeps this class decoupled from the logging backend and testable in isolation, per
    ///     coding-principles.md's "Dependency Injection" guidance. Existing call sites that do not
    ///     pass a logger are unaffected.
    /// </param>
    /// <param name="timeouts">The time limits of the commands, or <see langword="null"/> for
    ///     <see cref="GitClientTimeouts.Default"/>. Supplied so tests can use a short limit against a
    ///     stub that hangs.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="gitExecutablePath"/> is
    ///     null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a limit in
    ///     <paramref name="timeouts"/> is zero or negative.</exception>
    public GitClient(string gitExecutablePath = "git", ILogger<GitClient>? logger = null, GitClientTimeouts? timeouts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutablePath);
        _timeouts = timeouts ?? GitClientTimeouts.Default;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.LocalCommand, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.Pull, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.Stop, TimeSpan.Zero);
        _gitExecutablePath = gitExecutablePath;
        _logger = logger ?? AppLogging.Factory.CreateLogger<GitClient>();
    }

    /// <summary>
    ///     Determines whether a repository's working tree is clean (no uncommitted changes) by
    ///     running <c>git status --porcelain</c>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="cancellationToken">Stops git and abandons the call when signaled.</param>
    /// <returns>
    ///     <see langword="true"/> if <c>git status --porcelain</c> produced no output (clean
    ///     working tree); otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git status</c> exits with a non-zero code (e.g. the path is not a
    ///     git repository).</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when git did not answer within the local
    ///     command limit and was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; git was not started.</exception>
    public bool IsWorkingTreeClean(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = RunGit(repositoryPath, _timeouts.LocalCommand, cancellationToken, "status", "--porcelain");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git status' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    /// <summary>
    ///     Performs a <c>git pull</c> in the given repository.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="cancellationToken">Stops git and abandons the call when signaled.</param>
    /// <returns>The command result; callers should check <see cref="GitCommandResult.Succeeded"/>
    ///     rather than assuming success, since a failed pull (e.g. merge conflict) is a normal
    ///     outcome the caller must surface to the user rather than an exceptional one.</returns>
    /// <remarks>
    ///     Runs under the pull limit (<see cref="GitClientTimeouts.Pull"/>), not the local-command
    ///     limit: a pull talks to a remote and may wait for the user to sign in.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started.</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when the pull did not finish within the
    ///     pull limit and was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; git was not started.</exception>
    public GitCommandResult Pull(string repositoryPath, CancellationToken cancellationToken = default)
    {
        return RunGit(repositoryPath, _timeouts.Pull, cancellationToken, "pull");
    }

    /// <summary>
    ///     Gets the repository's current <c>HEAD</c> commit hash via <c>git rev-parse HEAD</c>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="cancellationToken">Stops git and abandons the call when signaled.</param>
    /// <returns>The trimmed <c>HEAD</c> commit hash.</returns>
    /// <remarks>
    ///     Per architecture.md's repo-fact caching strategy, this is the cheap operation used to
    ///     key the committed-agent-files cache: the commit hash changes if and only if the tree
    ///     git would report for <c>ls-files</c> could have changed.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git rev-parse HEAD</c> exits with a non-zero code (e.g. the path
    ///     is not a git repository, or has no commits yet).</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when git did not answer within the local
    ///     command limit and was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; git was not started.</exception>
    public string GetHeadCommitHash(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = RunGit(repositoryPath, _timeouts.LocalCommand, cancellationToken, "rev-parse", "HEAD");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git rev-parse HEAD' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    /// <summary>
    ///     Determines whether any of the four known agent folders (<c>.github/agents</c>,
    ///     <c>.github/standards</c>, <c>.github/templates</c>, <c>.github/skills</c>) are
    ///     tracked by git at <c>HEAD</c>, via <c>git ls-files</c>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="cancellationToken">Stops git and abandons the call when signaled.</param>
    /// <returns>
    ///     <see langword="true"/> if <c>git ls-files</c> reports any tracked file under one of
    ///     the four known agent folders; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    ///     Per architecture.md's "Committed agent files" badge: this query is purely advisory - it
    ///     never modifies git tracking state, it only reports whether the developer appears to
    ///     have accidentally committed proprietary agent content into a repo meant to gitignore
    ///     it. Stopping the tracking is a separate, deliberate step: see
    ///     <see cref="UntrackAgentFolders"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git ls-files</c> exits with a non-zero code.</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when git did not answer within the local
    ///     command limit and was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; git was not started.</exception>
    public bool HasCommittedAgentFiles(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "ls-files", "--" };
        arguments.AddRange(KnownAgentFolders);

        var result = RunGit(repositoryPath, _timeouts.LocalCommand, cancellationToken, arguments.ToArray());
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git ls-files' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    /// <summary>
    ///     Lists which of the four known agent folders currently have at least one file tracked by
    ///     git (in the index), via a single <c>git ls-files</c>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <returns>
    ///     The tracked folders, as repo-relative <c>/</c>-separated paths (for example
    ///     <c>.github/agents</c>), in the fixed order of the known folders; an empty list when none
    ///     is tracked.
    /// </returns>
    /// <remarks>
    ///     Exists so a caller that is about to stop the tracking (see
    ///     <see cref="UntrackAgentFolders"/>) can tell the user exactly which folders are affected
    ///     and can untrack only those: <c>git rm</c> refuses a path that matches nothing. Reads the
    ///     index afresh on every call and never uses a cache, so the answer is current at the moment
    ///     of the decision. The output is requested NUL-separated (<c>-z</c>) so that git never
    ///     quotes an unusual file name and the folder prefix is always recognizable.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git ls-files</c> exits with a non-zero code.</exception>
    public IReadOnlyList<string> GetTrackedAgentFolders(string repositoryPath)
    {
        var arguments = new List<string> { "ls-files", "-z", "--" };
        arguments.AddRange(KnownAgentFolders);

        var result = RunGit(repositoryPath, _timeouts.LocalCommand, CancellationToken.None, arguments.ToArray());
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git ls-files' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        // A folder is tracked when any listed path is the folder itself (a file, link or submodule
        // tracked at that exact path) or lies below it; matching is by whole path segment so a
        // sibling such as ".github/agents-old" never counts
        var trackedPaths = result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return
        [
            .. KnownAgentFolders.Where(folder => trackedPaths.Any(path =>
                path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)))
        ];
    }

    /// <summary>
    ///     Stops git tracking the given agent folders - removes their files from the index only -
    ///     by running <c>git rm -r --cached --quiet -- &lt;folders&gt;</c>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="folders">The folders to stop tracking, each one of the four known agent
    ///     folders (as returned by <see cref="GetTrackedAgentFolders"/>) and each with at least one
    ///     tracked file. Must not be empty.</param>
    /// <remarks>
    ///     This is the one place AgentControl changes git tracking state, and it is deliberately
    ///     narrow: <c>--cached</c> means the working tree is never touched (the files stay on disk
    ///     until the package replaces them), no force flag is used (so git's own protection against
    ///     losing staged work applies), the paths follow <c>--</c> so they can never be read as
    ///     options, and only the known agent folders are accepted so a caller can never untrack
    ///     anything else. Nothing is committed; the deletions stay staged for the user to commit.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace, when <paramref name="folders"/> is empty, or when it contains
    ///     anything other than a known agent folder.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folders"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git rm</c> exits with a non-zero code (for example another git
    ///     process holds the index lock, or a path has staged changes git refuses to discard); the
    ///     message carries git's own explanation.</exception>
    public void UntrackAgentFolders(string repositoryPath, IReadOnlyCollection<string> folders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(folders);

        // Only the known folders may be untracked: nothing else is AgentControl's to stop tracking
        if (folders.Count == 0)
        {
            throw new ArgumentException("At least one folder must be given.", nameof(folders));
        }

        var unknown = folders.FirstOrDefault(folder => !KnownAgentFolders.Contains(folder, StringComparer.Ordinal));
        if (unknown is not null)
        {
            throw new ArgumentException($"'{unknown}' is not one of the known agent folders.", nameof(folders));
        }

        var arguments = new List<string> { "rm", "-r", "--cached", "--quiet", "--" };
        arguments.AddRange(folders);

        var result = RunGit(repositoryPath, _timeouts.LocalCommand, CancellationToken.None, arguments.ToArray());
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git rm --cached' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    /// <summary>
    ///     Undoes <see cref="UntrackAgentFolders"/>: restores the index entries of the given agent
    ///     folders from <c>HEAD</c> with <c>git reset -q -- &lt;folders&gt;</c>, leaving the working
    ///     tree untouched.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="folders">The folders to track again, each one of the four known agent folders.
    ///     Must not be empty.</param>
    /// <remarks>
    ///     Used to roll back a conversion whose package could not be applied, so a failed apply never
    ///     leaves the folders' removal from git staged. Accepts only the known agent folders and puts
    ///     the paths after <c>--</c>, like <see cref="UntrackAgentFolders"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace, when <paramref name="folders"/> is empty, or when it contains
    ///     anything other than a known agent folder.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folders"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git reset</c> exits with a non-zero code.</exception>
    public void RestoreAgentFolderTracking(string repositoryPath, IReadOnlyCollection<string> folders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(folders);
        if (folders.Count == 0)
        {
            throw new ArgumentException("At least one folder must be given.", nameof(folders));
        }

        var unknown = folders.FirstOrDefault(folder => !KnownAgentFolders.Contains(folder, StringComparer.Ordinal));
        if (unknown is not null)
        {
            throw new ArgumentException($"'{unknown}' is not one of the known agent folders.", nameof(folders));
        }

        var arguments = new List<string> { "reset", "-q", "--" };
        arguments.AddRange(folders);

        var result = RunGit(repositoryPath, _timeouts.LocalCommand, CancellationToken.None, arguments.ToArray());
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git reset' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }
    }

    /// <summary>
    ///     Gets the repository's current branch name, or <c>"(detached)"</c> when <c>HEAD</c> is
    ///     detached.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="cancellationToken">Stops the fallback git and abandons the call when
    ///     signaled.</param>
    /// <returns>The current branch name, or <c>"(detached)"</c> for a detached <c>HEAD</c>.</returns>
    /// <remarks>
    ///     Implements a hybrid fast-path: this first attempts a direct read of
    ///     <c>&lt;repositoryPath&gt;/.git/HEAD</c> (following one level of <c>gitdir:</c>
    ///     indirection when <c>.git</c> is itself a file, as for worktrees/submodules), parsing
    ///     <c>ref: refs/heads/&lt;name&gt;</c> into <c>&lt;name&gt;</c>, or reporting
    ///     <c>"(detached)"</c> for a raw hex commit hash. On any parse/IO anomaly (missing file,
    ///     unexpected format, permission failure) this falls back unconditionally to
    ///     <c>git rev-parse --abbrev-ref HEAD</c> via <see cref="RunGit"/>, so correctness never
    ///     depends on the fast path being right - only on it usually being right, which keeps the
    ///     common case free of a subprocess spawn. The fast path reads the disk, so it is refused on
    ///     a guarded thread just like git.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the fallback git executable
    ///     cannot be started, or when <c>git rev-parse --abbrev-ref HEAD</c> exits with a
    ///     non-zero code.</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when the fallback git did not answer
    ///     within the local command limit and was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; nothing was read.</exception>
    public string GetCurrentBranch(string repositoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        BlockingCallGuard.ThrowIfGuarded("git branch");

        var fastPathResult = TryReadBranchFromGitHead(repositoryPath);
        if (fastPathResult is not null)
        {
            return fastPathResult;
        }

        var result = RunGit(repositoryPath, _timeouts.LocalCommand, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git rev-parse --abbrev-ref HEAD' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    /// <summary>
    ///     Attempts the fast-path direct read of <c>.git/HEAD</c> described by
    ///     <see cref="GetCurrentBranch"/>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <returns>The resolved branch name (or <c>"(detached)"</c>), or <see langword="null"/> if
    ///     the fast path could not be completed and the caller should fall back to a subprocess
    ///     call.</returns>
    private static string? TryReadBranchFromGitHead(string repositoryPath)
    {
        try
        {
            var gitPath = Path.Combine(repositoryPath, ".git");
            string headFilePath;
            if (Directory.Exists(gitPath))
            {
                headFilePath = Path.Combine(gitPath, "HEAD");
            }
            else if (File.Exists(gitPath))
            {
                // Worktrees/submodules: ".git" is a file containing "gitdir: <path>" pointing at
                // the real git directory, which itself contains its own HEAD file.
                var indirection = File.ReadAllText(gitPath).Trim();
                const string gitdirPrefix = "gitdir:";
                if (!indirection.StartsWith(gitdirPrefix, StringComparison.Ordinal))
                {
                    return null;
                }

                var indirectPath = indirection[gitdirPrefix.Length..].Trim();
                if (!Path.IsPathRooted(indirectPath))
                {
                    indirectPath = Path.GetFullPath(Path.Combine(repositoryPath, indirectPath));
                }

                headFilePath = Path.Combine(indirectPath, "HEAD");
            }
            else
            {
                return null;
            }

            if (!File.Exists(headFilePath))
            {
                return null;
            }

            var headContent = File.ReadAllText(headFilePath).Trim();
            const string refPrefix = "ref: refs/heads/";
            if (headContent.StartsWith(refPrefix, StringComparison.Ordinal))
            {
                return headContent[refPrefix.Length..].Trim();
            }

            return LooksLikeCommitHash(headContent) ? "(detached)" : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Determines whether a string looks like a raw git commit hash (all hex digits, of a
    ///     plausible length), as opposed to some other unrecognized <c>HEAD</c> file content.
    /// </summary>
    /// <param name="value">The candidate string.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> looks like a commit hash.</returns>
    private static bool LooksLikeCommitHash(string value) =>
        value.Length is >= 4 and <= 64 && value.All(Uri.IsHexDigit);

    /// <summary>
    ///     Determines whether a folder is the top folder of a Git repository: it has a <c>.git</c>
    ///     folder, or a <c>.git</c> file, directly in it.
    /// </summary>
    /// <remarks>
    ///     Exists so the launcher can refuse to track a folder that is not a repository (a
    ///     tracked repo is synced, pulled and launched as a Git working tree). A <c>.git</c> file
    ///     counts because a linked worktree and a submodule have one (it names the real git
    ///     directory); its content is not read. Deliberately does not walk up to the parent
    ///     folders as <c>git</c> itself would: a subfolder of a repository is not its top folder,
    ///     and tracking it would put the agent folders in the wrong place. Starts no git process,
    ///     so it is cheap enough to call for every folder of a multi-folder add. Static, stateless
    ///     and thread-safe; it only reads the file system.
    /// </remarks>
    /// <param name="folderPath">Absolute path of the folder to check. A folder that does not exist
    ///     (or cannot be read) is not a repository root.</param>
    /// <returns><see langword="true"/> when <c>&lt;folderPath&gt;/.git</c> exists as a folder or a
    ///     file; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="folderPath"/> is null,
    ///     empty, or whitespace.</exception>
    public static bool IsRepositoryRoot(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        // Only the folder itself is looked at: the parents are not walked, so a subfolder of a
        // repository is not a root. Directory/File.Exists never throw; an unreadable path is false.
        var gitPath = Path.Combine(folderPath, ".git");
        return Directory.Exists(gitPath) || File.Exists(gitPath);
    }

    /// <summary>
    ///     The four known agent folders whose committed-tracking status the "committed agent
    ///     files" badge checks, per architecture.md.
    /// </summary>
    private static readonly string[] KnownAgentFolders =
    [
        ".github/agents",
        ".github/standards",
        ".github/templates",
        ".github/skills"
    ];

    /// <summary>
    ///     Runs the configured git executable with the given arguments in a repository directory,
    ///     capturing stdout/stderr, under a time limit and a cancellation token.
    /// </summary>
    /// <remarks>
    ///     A command that does not exit within <paramref name="timeout"/>, or whose
    ///     <paramref name="cancellationToken"/> is signaled while it runs, is killed together with every
    ///     process it started (git may start helpers such as a credential manager or an SSH client), so
    ///     a hung git never keeps running unnoticed or holds its caller. A command that exited but whose
    ///     output is still held open, past the limit, by a process it started after it exited is
    ///     treated the same way (see <see cref="ReadOutput"/>): the wait for the output is bounded, and
    ///     an abandoned read never blocks the caller. The blocking-call guard is consulted before
    ///     anything starts.
    /// </remarks>
    /// <param name="repositoryPath">Working directory the git process runs in.</param>
    /// <param name="timeout">How long the command may run before it is stopped.</param>
    /// <param name="cancellationToken">Stops the command when signaled.</param>
    /// <param name="arguments">Command-line arguments to pass to git; the first is the
    ///     subcommand.</param>
    /// <returns>The captured exit code and output streams.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process fails to start (e.g.
    ///     the configured executable path does not exist).</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when the command did not exit within
    ///     <paramref name="timeout"/>, or its output was still held open, past the limit, by a process
    ///     it started after it exited; it was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled before or while the command ran; a running
    ///     command was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown on a thread guarded by
    ///     <see cref="BlockingCallGuard"/>; nothing was started.</exception>
    private GitCommandResult RunGit(
        string repositoryPath, TimeSpan timeout, CancellationToken cancellationToken, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        // Refuse before anything starts: a git process must never be waited for on a guarded thread
        BlockingCallGuard.ThrowIfGuarded($"git {arguments[0]}");
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(_gitExecutablePath)
        {
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Logged before the process starts so a crash/hang mid-invocation still leaves a record
        // of exactly what was about to run and where - the key data point missing from prior
        // occurrences of the intermittent exit-code -1 failure. Lazily computed so the join is
        // only ever performed once, and only if some enabled log statement actually needs it.
        var argumentsText = new Lazy<string>(() => string.Join(' ', arguments));
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Starting git process '{GitExecutable}' with arguments '{Arguments}' in working directory '{WorkingDirectory}'",
                _gitExecutablePath, argumentsText.Value, repositoryPath);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(startInfo)
                                 ?? throw new InvalidOperationException(
                                     $"Failed to start git executable '{_gitExecutablePath}'.");

            // Read output asynchronously to avoid deadlock from full stdout/stderr buffers
            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            // Wait for the exit within the limit; a cancellation kills the process tree, which ends
            // the wait at once
            bool exited;
            using (cancellationToken.Register(() => TryKillProcessTree(process)))
            {
                exited = process.WaitForExit(timeout);
            }

            stopwatch.Stop();
            if (!exited)
            {
                StopTimedOutProcess(process, argumentsText.Value, timeout);
                throw new GitCommandTimeoutException(
                    $"'git {arguments[0]}' did not finish within {timeout.TotalSeconds:0.###} seconds and was stopped.",
                    timeout);
            }

            // Canceled while it ran: the process was killed, so its output means nothing
            cancellationToken.ThrowIfCancellationRequested();

            // A long WaitForExit points at a hung/slow process rather than a fast failure -
            // exactly the ambiguity the flaky-test investigation could not previously resolve.
            if (stopwatch.Elapsed > SlowExitWarningThreshold && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "git process '{GitExecutable} {Arguments}' took an unexpectedly long {ElapsedMilliseconds}ms to exit",
                    _gitExecutablePath, argumentsText.Value, stopwatch.ElapsedMilliseconds);
            }

            var (stdout, stderr) = ReadOutput(
                process, stdoutTask, stderrTask, stopwatch.Elapsed, timeout, argumentsText.Value, arguments[0], cancellationToken);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "git process '{GitExecutable} {Arguments}' exited with code {ExitCode} after {ElapsedMilliseconds}ms",
                    _gitExecutablePath, argumentsText.Value, process.ExitCode, stopwatch.ElapsedMilliseconds);
            }

            if (process.ExitCode != 0 && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "git process '{GitExecutable} {Arguments}' failed with exit code {ExitCode}; stderr: {StandardError}",
                    _gitExecutablePath, argumentsText.Value, process.ExitCode, stderr);
            }

            return new GitCommandResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            stopwatch.Stop();

            // NativeErrorCode is the actual OS error code behind a Win32Exception - identified in
            // the prior investigation as critical missing diagnostic data (only "-1" was ever
            // observed, with no underlying Win32/IO detail).
            var nativeErrorCode = ex is Win32Exception win32Exception
                ? win32Exception.NativeErrorCode
                : (int?)null;

            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    ex,
                    "Failed to run '{GitExecutable} {Arguments}' after {ElapsedMilliseconds}ms (Win32 NativeErrorCode={NativeErrorCode})",
                    _gitExecutablePath, argumentsText.Value, stopwatch.ElapsedMilliseconds, nativeErrorCode);
            }

            throw new InvalidOperationException(
                $"Failed to run '{_gitExecutablePath} {argumentsText.Value}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Collects the output of a git process that has exited, waiting a bounded time for its output
    ///     streams to close.
    /// </summary>
    /// <remarks>
    ///     The streams normally close when git exits. A process git started (a credential helper, an
    ///     SSH client, a background maintenance job) that inherited them keeps them open after git
    ///     exited, so the wait is bounded: by what is left of <paramref name="timeout"/>, but at least
    ///     the stop time (<see cref="GitClientTimeouts.Stop"/>), so a git that exits at the very end of
    ///     its limit still gets to hand over its output. When the streams are still open after that,
    ///     everything git started is killed (best effort: on Windows the processes git started can
    ///     still be found after it exited; elsewhere they were handed to another parent and may
    ///     survive), the reads are abandoned (their faults are observed, so they are never reported as
    ///     unobserved) and the command is reported as timed out. An abandoned read never blocks the
    ///     caller.
    /// </remarks>
    /// <param name="process">The git process; has exited.</param>
    /// <param name="stdoutTask">The read of its standard output.</param>
    /// <param name="stderrTask">The read of its standard error.</param>
    /// <param name="elapsed">How long the command has run so far.</param>
    /// <param name="timeout">The command's time limit.</param>
    /// <param name="argumentsText">The command's arguments, for the log.</param>
    /// <param name="subcommand">The git subcommand, for the exception message.</param>
    /// <param name="cancellationToken">Stops waiting when signaled.</param>
    /// <returns>The standard output and standard error text.</returns>
    /// <exception cref="GitCommandTimeoutException">Thrown when the output was still held open past
    ///     the bound.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled while waiting.</exception>
    private (string Stdout, string Stderr) ReadOutput(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        TimeSpan elapsed,
        TimeSpan timeout,
        string argumentsText,
        string subcommand,
        CancellationToken cancellationToken)
    {
        // What is left of the limit, but never less than the stop time
        var readLimit = timeout - elapsed;
        if (readLimit < _timeouts.Stop)
        {
            readLimit = _timeouts.Stop;
        }

        var outputs = Task.WhenAll(stdoutTask, stderrTask);
        Task[] waited = [outputs];
        try
        {
            // WaitAny never throws for a faulted read; the read's own exception is rethrown below
            if (Task.WaitAny(waited, (int)Math.Ceiling(readLimit.TotalMilliseconds), cancellationToken) == 0)
            {
                return (stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
            }
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTreeAfterExit(process);
            ObserveAbandoned(outputs);
            throw;
        }

        // Still held open by a process git started: stop it (best effort), give the reads a bounded
        // moment to end, and abandon them
        TryKillProcessTreeAfterExit(process);
        Task.WaitAny(waited, _timeouts.Stop);
        ObserveAbandoned(outputs);
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "git process '{GitExecutable} {Arguments}' exited, but its output was still held open by a process it started after {TimeoutSeconds}s; stopped waiting",
                _gitExecutablePath, argumentsText, readLimit.TotalSeconds);
        }

        throw new GitCommandTimeoutException(
            $"'git {subcommand}' did not finish within {timeout.TotalSeconds:0.###} seconds and was stopped.",
            timeout);
    }

    /// <summary>
    ///     Observes the fault of an abandoned output read whenever it ends, so it is never reported as
    ///     an unobserved task exception.
    /// </summary>
    /// <param name="outputs">The abandoned reads.</param>
    private static void ObserveAbandoned(Task outputs) =>
        outputs.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>
    ///     Stops a git process that outlived its time limit: kills it and everything it started, waits
    ///     a bounded time for it to end, and logs what happened.
    /// </summary>
    /// <param name="process">The process.</param>
    /// <param name="argumentsText">The command's arguments, for the log.</param>
    /// <param name="timeout">The limit it exceeded, for the log.</param>
    private void StopTimedOutProcess(Process process, string argumentsText, TimeSpan timeout)
    {
        TryKillProcessTree(process);

        // The kill is asynchronous; wait (bounded) so the process is normally gone when the caller
        // hears about the timeout
        var ended = TryWaitForExit(process, _timeouts.Stop);
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "git process '{GitExecutable} {Arguments}' did not finish within {TimeoutSeconds}s and was stopped (ended: {Ended})",
                _gitExecutablePath, argumentsText, timeout.TotalSeconds, ended);
        }
    }

    /// <summary>
    ///     Kills every process a git process that has already exited started, as far as they can still
    ///     be found.
    /// </summary>
    /// <remarks>
    ///     Best effort, and never throws. Unlike <see cref="TryKillProcessTree"/> it does not skip a
    ///     process that has exited: on Windows the handle still held keeps the process's identity, so
    ///     the processes it started can still be found and killed; elsewhere they were handed to
    ///     another parent when it exited and may survive.
    /// </remarks>
    /// <param name="process">The process.</param>
    private static void TryKillProcessTreeAfterExit(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // Nothing could be found or killed; the bounded wait that follows does not depend on it
        }
    }

    /// <summary>
    ///     Kills a process and every process it started, ignoring a process that has already ended.
    /// </summary>
    /// <remarks>
    ///     Called from a cancellation callback too, so it never throws: whether the process really
    ///     ended is decided by the wait that follows, not by the kill.
    /// </remarks>
    /// <param name="process">The process.</param>
    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // The process ended between the check and the kill, or could not be killed; the wait
            // that follows tells which
        }
    }

    /// <summary>
    ///     Waits a bounded time for a process to end.
    /// </summary>
    /// <param name="process">The process.</param>
    /// <param name="limit">How long to wait.</param>
    /// <returns><see langword="true"/> when the process has ended.</returns>
    private static bool TryWaitForExit(Process process, TimeSpan limit)
    {
        try
        {
            return process.WaitForExit(limit);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // No process is associated any more: there is nothing left to wait for
            return true;
        }
    }
}
