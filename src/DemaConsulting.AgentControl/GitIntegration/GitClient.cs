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
using DemaConsulting.AgentControl.PackageCredentials;
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
/// <remarks>
///     <see cref="SwitchBranch"/> and <see cref="CloneRepository"/> are init-only properties with
///     defaults (rather than positional parameters) so that the three-argument form callers and tests
///     already use keeps compiling.
/// </remarks>
internal sealed record GitClientTimeouts(TimeSpan LocalCommand, TimeSpan Pull, TimeSpan Stop)
{
    /// <summary>
    ///     Gets the limits the application uses: a minute for a local command (enough for a cold
    ///     <c>git status</c> of a large tree on a network share), ten minutes for a pull (network work
    ///     that may include signing in), and ten seconds for a stopped command to end. A branch switch
    ///     defaults to ten minutes and a clone to an hour.
    /// </summary>
    public static GitClientTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(10));

    /// <summary>
    ///     Gets the limit for <c>git switch</c>, which rewrites the working tree: checking out a large
    ///     tree can take far longer than the local-command limit. Must be positive; defaults to ten
    ///     minutes.
    /// </summary>
    public TimeSpan SwitchBranch { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     Gets the limit for <c>git clone</c>, which downloads a whole repository and may wait for
    ///     the credential helper's own sign-in window. Cancel is the normal way out of a slow clone, so
    ///     the limit is only a backstop. Must be positive; defaults to sixty minutes.
    /// </summary>
    public TimeSpan CloneRepository { get; init; } = TimeSpan.FromMinutes(60);
}

/// <summary>
///     A branch of a remote that the local repository already knows (a remote-tracking ref).
/// </summary>
/// <param name="Remote">The remote's name, for example <c>origin</c>.</param>
/// <param name="Name">The branch's name on the remote, for example <c>feature/x</c>.</param>
/// <param name="FullName">The name git uses for the remote-tracking ref, <c>&lt;remote&gt;/&lt;name&gt;</c>,
///     for example <c>origin/feature/x</c>.</param>
internal sealed record GitRemoteBranch(string Remote, string Name, string FullName);

/// <summary>
///     The branches of a repository as <see cref="GitClient.GetBranches"/> reports them.
/// </summary>
/// <param name="CurrentBranch">The checked-out local branch, or <see langword="null"/> when
///     <c>HEAD</c> is detached.</param>
/// <param name="DetachedAt">The short commit id <c>HEAD</c> points at while it is detached;
///     <see langword="null"/> when a branch is checked out (or when no commit can be named).</param>
/// <param name="Local">The local branch names, sorted.</param>
/// <param name="Remote">The remote-tracking branches that have no local branch of the same name
///     (the local one is the one to switch to), sorted; <c>&lt;remote&gt;/HEAD</c> is never listed.</param>
internal sealed record GitBranchList(
    string? CurrentBranch,
    string? DetachedAt,
    IReadOnlyList<string> Local,
    IReadOnlyList<GitRemoteBranch> Remote);

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
    ///     Environment variables applied to every git process this client starts (a
    ///     <see langword="null"/> value removes the variable), or <see langword="null"/> for none; see
    ///     the constructor.
    /// </summary>
    private readonly IReadOnlyDictionary<string, string?>? _extraEnvironment;

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
    /// <param name="extraEnvironment">Environment variables to apply to every git process (a
    ///     <see langword="null"/> value removes the variable), or <see langword="null"/> for none.
    ///     Exists so tests that run a real git can replace the user's global configuration (a
    ///     credential helper, <c>insteadOf</c> rewrites) with an empty one; the application passes
    ///     none.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="gitExecutablePath"/> is
    ///     null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a limit in
    ///     <paramref name="timeouts"/> is zero or negative.</exception>
    public GitClient(
        string gitExecutablePath = "git",
        ILogger<GitClient>? logger = null,
        GitClientTimeouts? timeouts = null,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutablePath);
        _timeouts = timeouts ?? GitClientTimeouts.Default;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.LocalCommand, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.Pull, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.Stop, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.SwitchBranch, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeouts.CloneRepository, TimeSpan.Zero);
        _gitExecutablePath = gitExecutablePath;
        _extraEnvironment = extraEnvironment;
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
    ///     Lists the branches the repository already knows about: its local branches and the
    ///     remote-tracking branches of its remotes, and which local branch (if any) is checked out.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="cancellationToken">Stops git and abandons the call when signaled.</param>
    /// <returns>The branch list; see <see cref="GitBranchList"/>.</returns>
    /// <remarks>
    ///     Runs <c>git for-each-ref</c> over <c>refs/heads</c> and <c>refs/remotes</c> and
    ///     <c>git remote</c> (so <c>&lt;remote&gt;/&lt;branch&gt;</c> can be split without guessing
    ///     where a remote name ends), plus <c>git rev-parse --short HEAD</c> when <c>HEAD</c> is
    ///     detached. It never fetches: the answer is what git already has locally, which keeps the call
    ///     fast and free of network and sign-in prompts; the user updates the remote-tracking branches
    ///     with a pull or fetch. The symbolic <c>&lt;remote&gt;/HEAD</c> ref is dropped (it names
    ///     another branch, not a branch of its own), and a remote branch that has a local branch of the
    ///     same name is dropped too, because the local branch is the one to switch to. The ref listing
    ///     is requested tab-separated (<c>%09</c>) because a tab can never occur in a ref name.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be started,
    ///     or when <c>git for-each-ref</c> or <c>git remote</c> exits with a non-zero code (for example
    ///     the path is not a git repository).</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when git did not answer within the local
    ///     command limit and was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; git was not started.</exception>
    public GitBranchList GetBranches(string repositoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        // The refs, then the remote names that let a remote-tracking ref be split into remote and branch
        var refs = RunGit(
            repositoryPath, _timeouts.LocalCommand, cancellationToken,
            "for-each-ref", "--format=%(HEAD)%09%(refname)", "refs/heads", "refs/remotes");
        if (!refs.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git for-each-ref' failed with exit code {refs.ExitCode}: {refs.StandardError.Trim()}");
        }

        var remoteResult = RunGit(repositoryPath, _timeouts.LocalCommand, cancellationToken, "remote");
        if (!remoteResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git remote' failed with exit code {remoteResult.ExitCode}: {remoteResult.StandardError.Trim()}");
        }

        // Longest first, so a remote named "up" never swallows the start of one named "up/stream"
        var remoteNames = SplitLines(remoteResult.StandardOutput)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .OrderByDescending(name => name.Length)
            .ToList();

        const string localPrefix = "refs/heads/";
        const string remotePrefix = "refs/remotes/";
        string? currentBranch = null;
        var local = new List<string>();
        var remote = new List<GitRemoteBranch>();
        foreach (var line in SplitLines(refs.StandardOutput))
        {
            // "<HEAD marker>\t<full ref name>": the marker is "*" for the checked-out branch
            var tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var isCurrent = line[..tab].Trim() == "*";
            var refName = line[(tab + 1)..].Trim();
            if (refName.StartsWith(localPrefix, StringComparison.Ordinal))
            {
                var name = refName[localPrefix.Length..];
                local.Add(name);
                if (isCurrent)
                {
                    currentBranch = name;
                }
            }
            else if (refName.StartsWith(remotePrefix, StringComparison.Ordinal))
            {
                var tracking = refName[remotePrefix.Length..];
                var remoteName = remoteNames.FirstOrDefault(name => tracking.StartsWith(name + "/", StringComparison.Ordinal))
                                 ?? tracking[..Math.Max(tracking.IndexOf('/'), 0)];
                var branchName = tracking[Math.Min(remoteName.Length + 1, tracking.Length)..];

                // "<remote>/HEAD" only says which branch the remote's default is; it is not a branch
                if (remoteName.Length > 0 && branchName.Length > 0 && branchName != "HEAD")
                {
                    remote.Add(new GitRemoteBranch(remoteName, branchName, tracking));
                }
            }
        }

        // A detached HEAD has no branch; the short commit id is what the user can recognize it by
        string? detachedAt = null;
        if (currentBranch is null)
        {
            var head = RunGit(repositoryPath, _timeouts.LocalCommand, cancellationToken, "rev-parse", "--short", "HEAD");
            if (head.Succeeded && head.StandardOutput.Trim().Length > 0)
            {
                detachedAt = head.StandardOutput.Trim();
            }
        }

        // The local branch of the same name is the one to switch to, so its remote twin is not listed
        var localNames = local.ToHashSet(StringComparer.Ordinal);
        return new GitBranchList(
            currentBranch,
            detachedAt,
            [.. local.Order(StringComparer.OrdinalIgnoreCase)],
            [
                .. remote
                    .Where(branch => !localNames.Contains(branch.Name))
                    .OrderBy(branch => branch.FullName, StringComparer.OrdinalIgnoreCase)
            ]);
    }

    /// <summary>
    ///     Switches the repository to another branch with <c>git switch</c>, never forcing it and never
    ///     discarding or stashing anything.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <param name="branchName">For a local branch its name; for a remote-tracking branch its
    ///     <see cref="GitRemoteBranch.FullName"/> (<c>&lt;remote&gt;/&lt;name&gt;</c>). Must not be
    ///     blank, contain control characters or start with <c>-</c>.</param>
    /// <param name="createTrackingFromRemote"><see langword="true"/> when
    ///     <paramref name="branchName"/> names a remote-tracking branch: a local branch of the same
    ///     name is created from it and set up to track it (<c>git switch --track</c>).</param>
    /// <param name="cancellationToken">Stops git and abandons the call when signaled.</param>
    /// <returns>The command result. A checkout git refuses (local changes that would be overwritten,
    ///     an unknown branch) is a normal outcome, reported as a result that has not
    ///     <see cref="GitCommandResult.Succeeded"/> with git's own explanation in
    ///     <see cref="GitCommandResult.StandardError"/>, exactly as <see cref="Pull"/> reports a failed
    ///     pull; nothing has been changed then.</returns>
    /// <remarks>
    ///     Runs <c>git switch -- &lt;name&gt;</c> or <c>git switch --track -- &lt;remote&gt;/&lt;name&gt;</c>.
    ///     None of <c>--force</c>, <c>--discard-changes</c>, <c>-C</c> or <c>--merge</c> is ever passed,
    ///     so git's own protection applies: uncommitted changes that do not conflict travel with the
    ///     switch, and changes that would be overwritten stop it. The name follows <c>--</c> and may not
    ///     start with <c>-</c>, so it can never be read as an option. Runs under the switch limit
    ///     (<see cref="GitClientTimeouts.SwitchBranch"/>) because a checkout of a large tree can take
    ///     longer than a local command; git writes its success text to standard error.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace, or when <paramref name="branchName"/> is blank, contains control
    ///     characters or starts with <c>-</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started.</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when git did not finish within the switch
    ///     limit and was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; git was not started.</exception>
    public GitCommandResult SwitchBranch(
        string repositoryPath,
        string branchName,
        bool createTrackingFromRemote,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        // The name is data, never an option: refuse a leading '-' and control characters, and put it
        // after "--" as well
        if (branchName.StartsWith('-') || branchName.Any(char.IsControl))
        {
            throw new ArgumentException("The branch name is not valid.", nameof(branchName));
        }

        return createTrackingFromRemote
            ? RunGit(repositoryPath, _timeouts.SwitchBranch, cancellationToken, "switch", "--track", "--", branchName)
            : RunGit(repositoryPath, _timeouts.SwitchBranch, cancellationToken, "switch", "--", branchName);
    }

    /// <summary>
    ///     Clones a repository into a new folder with <c>git clone</c>.
    /// </summary>
    /// <param name="url">The repository to clone: a URL, an scp-like address or a local path. Checked
    ///     by <see cref="CheckCloneUrl"/>.</param>
    /// <param name="destinationPath">The absolute path of the folder to clone into: it must not exist,
    ///     or must be an empty folder, and its parent folder must exist. Checked by
    ///     <see cref="CheckCloneDestination"/>.</param>
    /// <param name="cancellationToken">Stops git and abandons the call when signaled; the normal way
    ///     to leave a clone that takes too long.</param>
    /// <returns>The command result, with credentials removed from both output streams. A clone git
    ///     refuses or cannot finish (unknown repository, sign-in needed) is a normal outcome, reported as
    ///     a result that has not <see cref="GitCommandResult.Succeeded"/>; git has cleaned up after
    ///     itself then and nothing is deleted here.</returns>
    /// <remarks>
    ///     <para>
    ///     Runs <c>git clone -- &lt;url&gt; &lt;destination&gt;</c> in the destination's parent folder
    ///     (a clone needs an existing working directory) through an argument list, never a shell, with
    ///     <c>GIT_TERMINAL_PROMPT=0</c> because there is no terminal to ask in (a credential helper such
    ///     as Git Credential Manager still shows its own sign-in window). The URL is data: one that
    ///     starts with <c>-</c> or names a remote helper (<c>ext::</c>, <c>fd::</c>) is refused before
    ///     git starts, and <c>--</c> separates it from the options. The URL may contain credentials, so
    ///     it is redacted in every log message and exception text (see <see cref="RunGit"/>).
    ///     </para>
    ///     <para>
    ///     When the clone is <b>canceled or times out</b>, git is killed in the middle of writing, so the
    ///     partial result is removed - but only what this call created: a destination that did not exist
    ///     is removed whole; an existing empty folder is kept and only its contents are removed.
    ///     Read-only attributes (git writes read-only object files) are cleared first and links are
    ///     never followed. A leftover that cannot be removed is logged, and named in the text of the
    ///     timeout exception. After a <b>failed</b> clone nothing is deleted, because git cleans up
    ///     after itself and a deletion could only remove somebody else's folder.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the URL or the destination is refused; the
    ///     message is a reason the user can read.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started.</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when the clone did not finish within the
    ///     clone limit and was stopped; the partial folder was removed.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; git was stopped and the partial folder
    ///     removed.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; nothing was checked or started.</exception>
    public GitCommandResult Clone(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        // Disk checks and git both block: refuse on a guarded thread before either
        BlockingCallGuard.ThrowIfGuarded("git clone");

        var urlProblem = CheckCloneUrl(url);
        if (urlProblem is not null)
        {
            throw new ArgumentException(urlProblem, nameof(url));
        }

        var destinationProblem = CheckCloneDestination(destinationPath);
        if (destinationProblem is not null)
        {
            throw new ArgumentException(destinationProblem, nameof(destinationPath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Only what this call creates may be removed after a kill: remember whether the folder existed
        var destination = NormalizeFolder(destinationPath);
        var parent = Path.GetDirectoryName(destination)!;
        var createdByThisCall = !Directory.Exists(destination);

        GitCommandResult result;
        try
        {
            result = RunGitCore(
                parent,
                _timeouts.CloneRepository,
                cancellationToken,
                new Dictionary<string, string?> { ["GIT_TERMINAL_PROMPT"] = "0" },
                ["clone", "--", url.Trim(), destination]);
        }
        catch (OperationCanceledException)
        {
            // Killed while writing: the partial clone is not worth keeping
            RemovePartialClone(destination, removeFolder: createdByThisCall);
            throw;
        }
        catch (GitCommandTimeoutException ex)
        {
            var leftover = RemovePartialClone(destination, removeFolder: createdByThisCall);
            if (leftover is null)
            {
                throw;
            }

            throw new GitCommandTimeoutException($"{ex.Message} {leftover}", ex.Timeout);
        }

        // The URL may carry credentials, and git can echo it back in its messages
        return new GitCommandResult(
            result.ExitCode,
            SecretRedactor.Redact(result.StandardOutput),
            SecretRedactor.Redact(result.StandardError));
    }

    /// <summary>
    ///     Tells why a repository address cannot be cloned, or that it can.
    /// </summary>
    /// <param name="url">The address as typed.</param>
    /// <returns>The reason, in words the user can read, or <see langword="null"/> when the address is
    ///     acceptable.</returns>
    /// <remarks>
    ///     Only rules that keep the address from being read as something other than a repository: it
    ///     must not be blank, must be a single line without control characters, must not start with
    ///     <c>-</c> (git would read it as an option) and must not use the remote-helper syntax
    ///     <c>&lt;name&gt;::&lt;address&gt;</c> (<c>ext::</c> runs a command, <c>fd::</c> reads an open
    ///     file; neither is a repository location). Whether the repository exists is for git to say.
    ///     Static, pure and thread-safe; no disk or process access.
    /// </remarks>
    public static string? CheckCloneUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Enter the address of the repository to clone.";
        }

        var trimmed = url.Trim();
        if (trimmed.Any(char.IsControl))
        {
            return "The repository address must be a single line of text.";
        }

        if (trimmed.StartsWith('-'))
        {
            return "The repository address must not start with a dash.";
        }

        // "<name>::<address>" is git's remote-helper syntax; the helper may run arbitrary commands
        var helperEnd = trimmed.IndexOf("::", StringComparison.Ordinal);
        if (helperEnd > 0 && trimmed[..helperEnd].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '.' or '-'))
        {
            return "Addresses that use a remote helper (such as ext:: or fd::) are not supported.";
        }

        return null;
    }

    /// <summary>
    ///     Tells why a folder cannot be the destination of a clone, or that it can.
    /// </summary>
    /// <param name="destinationPath">The folder to clone into, as typed.</param>
    /// <returns>The reason, in words the user can read, or <see langword="null"/> when the folder can
    ///     be used: an absolute path whose parent folder exists and that does not exist yet or is an
    ///     empty folder.</returns>
    /// <remarks>
    ///     Reads the disk, so it must be called off the UI thread; the clone dialog runs it through the
    ///     background work runner, and <see cref="Clone"/> repeats it right before git starts because
    ///     the folder may have changed in between. Static and thread-safe.
    /// </remarks>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown when called on a thread guarded
    ///     by <see cref="BlockingCallGuard"/>; nothing was read.</exception>
    public static string? CheckCloneDestination(string? destinationPath)
    {
        BlockingCallGuard.ThrowIfGuarded("git clone destination check");

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return "Enter the folder to clone into.";
        }

        if (!Path.IsPathFullyQualified(destinationPath))
        {
            return "The folder to clone into must be an absolute path.";
        }

        try
        {
            var destination = NormalizeFolder(destinationPath);
            var parent = Path.GetDirectoryName(destination);
            if (parent is null)
            {
                return "A repository cannot be cloned into the root of a drive; choose a folder in it.";
            }

            if (!Directory.Exists(parent))
            {
                return $"The folder '{parent}' does not exist.";
            }

            if (File.Exists(destination))
            {
                return $"'{destination}' already exists and is a file.";
            }

            // An empty folder is fine (git clones into it); anything inside it is not ours to mix with
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            {
                return $"The folder '{destination}' already exists and is not empty.";
            }

            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "The folder to clone into is not a valid path.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The folder '{destinationPath}' cannot be read.";
        }
    }

    /// <summary>
    ///     Gets the full path of a folder without a trailing separator (except for a drive root).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalized full path.</returns>
    private static string NormalizeFolder(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    /// <summary>
    ///     Splits command output into lines, accepting both <c>\n</c> and <c>\r\n</c>.
    /// </summary>
    /// <param name="text">The output.</param>
    /// <returns>The non-empty lines.</returns>
    private static string[] SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    ///     How many times a partial clone's removal is tried: git's helper processes and anti-virus
    ///     scanners may hold a file for a moment after git was killed.
    /// </summary>
    private const int RemovalAttempts = 5;

    /// <summary>
    ///     How long to wait between two attempts to remove a partial clone.
    /// </summary>
    private static readonly TimeSpan RemovalRetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     Removes what a killed <c>git clone</c> left behind.
    /// </summary>
    /// <param name="destination">The clone destination.</param>
    /// <param name="removeFolder"><see langword="true"/> to remove the folder itself (this call created
    ///     it); <see langword="false"/> to keep the folder and remove only what is in it (it existed,
    ///     empty, before).</param>
    /// <returns><see langword="null"/> when nothing is left; otherwise a sentence naming what could not
    ///     be removed, which is also logged.</returns>
    /// <remarks>
    ///     Never throws and never follows a link: read-only attributes (git writes read-only object
    ///     files) are cleared on everything below the destination except through links, and a link is
    ///     removed as a link by the recursive delete. Retries a few times because a process git started
    ///     may still hold a file for a moment after it was killed.
    /// </remarks>
    private string? RemovePartialClone(string destination, bool removeFolder)
    {
        Exception? failure = null;
        for (var attempt = 1; attempt <= RemovalAttempts; attempt++)
        {
            try
            {
                DeleteCloneContent(destination, removeFolder);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = ex;
                if (attempt < RemovalAttempts)
                {
                    Thread.Sleep(RemovalRetryDelay);
                }
            }
        }

        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                failure, "The partly cloned folder '{Destination}' could not be removed", destination);
        }

        return removeFolder
            ? $"The partly cloned folder '{destination}' could not be removed; delete it yourself."
            : $"The partly cloned content of the folder '{destination}' could not be removed; delete it yourself.";
    }

    /// <summary>
    ///     Deletes a clone destination (or its content) once, clearing read-only attributes first.
    /// </summary>
    /// <param name="destination">The clone destination.</param>
    /// <param name="removeFolder">See <see cref="RemovePartialClone"/>.</param>
    /// <exception cref="IOException">Thrown when something cannot be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when access is denied.</exception>
    private static void DeleteCloneContent(string destination, bool removeFolder)
    {
        if (!Directory.Exists(destination))
        {
            return;
        }

        // Links are skipped (never entered, never changed); hidden and system entries are not
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var root = new DirectoryInfo(destination);
        foreach (var entry in root.EnumerateFileSystemInfos("*", options))
        {
            ClearReadOnly(entry);
        }

        ClearReadOnly(root);

        if (removeFolder)
        {
            root.Delete(recursive: true);
            return;
        }

        // The folder was there before: keep it, empty it
        foreach (var entry in root.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo directory)
            {
                directory.Delete(recursive: true);
            }
            else
            {
                entry.Delete();
            }
        }
    }

    /// <summary>
    ///     Clears the read-only attribute of a file or folder.
    /// </summary>
    /// <param name="entry">The file or folder.</param>
    private static void ClearReadOnly(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
        {
            entry.Attributes &= ~FileAttributes.ReadOnly;
        }
    }

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
        string repositoryPath, TimeSpan timeout, CancellationToken cancellationToken, params string[] arguments) =>
        RunGitCore(repositoryPath, timeout, cancellationToken, null, arguments);

    /// <summary>
    ///     Runs git like <see cref="RunGit"/>, with environment variables for this one command added on
    ///     top of the client's own.
    /// </summary>
    /// <remarks>
    ///     Everything that can reach a log or an exception message is redacted with
    ///     <see cref="SecretRedactor"/> first: the argument text (a clone URL may carry
    ///     <c>user:password@</c>), git's standard error and the operating system's error text. The value
    ///     handed back to the caller is the raw output; a caller that shows it redacts it.
    /// </remarks>
    /// <param name="repositoryPath">Working directory the git process runs in.</param>
    /// <param name="timeout">How long the command may run before it is stopped.</param>
    /// <param name="cancellationToken">Stops the command when signaled.</param>
    /// <param name="environment">Environment variables for this command (a <see langword="null"/>
    ///     value removes the variable), or <see langword="null"/> for none.</param>
    /// <param name="arguments">Command-line arguments to pass to git; the first is the
    ///     subcommand.</param>
    /// <returns>The captured exit code and output streams.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process fails to start.</exception>
    /// <exception cref="GitCommandTimeoutException">Thrown when the command did not finish in time; it
    ///     was stopped.</exception>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> was signaled; a running command was stopped.</exception>
    /// <exception cref="BlockingCallOnGuardedThreadException">Thrown on a thread guarded by
    ///     <see cref="BlockingCallGuard"/>; nothing was started.</exception>
    private GitCommandResult RunGitCore(
        string repositoryPath,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment,
        string[] arguments)
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

        ApplyEnvironment(startInfo, _extraEnvironment);
        ApplyEnvironment(startInfo, environment);

        // Logged before the process starts so a crash/hang mid-invocation still leaves a record
        // of exactly what was about to run and where - the key data point missing from prior
        // occurrences of the intermittent exit-code -1 failure. Lazily computed so the join is
        // only ever performed once, and only if some enabled log statement actually needs it.
        // Redacted: a clone URL may carry credentials (user:password@host) that must never be logged.
        var argumentsText = new Lazy<string>(() => SecretRedactor.Redact(string.Join(' ', arguments)));
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
                    _gitExecutablePath, argumentsText.Value, process.ExitCode, SecretRedactor.Redact(stderr));
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
                $"Failed to run '{_gitExecutablePath} {argumentsText.Value}': {SecretRedactor.Redact(ex.Message)}", ex);
        }
    }

    /// <summary>
    ///     Applies environment variables to a process about to start.
    /// </summary>
    /// <param name="startInfo">The process start information.</param>
    /// <param name="environment">The variables (a <see langword="null"/> value removes the variable),
    ///     or <see langword="null"/> for none.</param>
    private static void ApplyEnvironment(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string?>? environment)
    {
        if (environment is null)
        {
            return;
        }

        foreach (var (name, value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
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
