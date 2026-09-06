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
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.GitIntegration;

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
    /// <exception cref="ArgumentException">Thrown when <paramref name="gitExecutablePath"/> is
    ///     null, empty, or whitespace.</exception>
    public GitClient(string gitExecutablePath = "git", ILogger<GitClient>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutablePath);
        _gitExecutablePath = gitExecutablePath;
        _logger = logger ?? AppLogging.Factory.CreateLogger<GitClient>();
    }

    /// <summary>
    ///     Determines whether a repository's working tree is clean (no uncommitted changes) by
    ///     running <c>git status --porcelain</c>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
    /// <returns>
    ///     <see langword="true"/> if <c>git status --porcelain</c> produced no output (clean
    ///     working tree); otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git status</c> exits with a non-zero code (e.g. the path is not a
    ///     git repository).</exception>
    public bool IsWorkingTreeClean(string repositoryPath)
    {
        var result = RunGit(repositoryPath, "status", "--porcelain");
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
    /// <returns>The command result; callers should check <see cref="GitCommandResult.Succeeded"/>
    ///     rather than assuming success, since a failed pull (e.g. merge conflict) is a normal
    ///     outcome the caller must surface to the user rather than an exceptional one.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started.</exception>
    public GitCommandResult Pull(string repositoryPath)
    {
        return RunGit(repositoryPath, "pull");
    }

    /// <summary>
    ///     Gets the repository's current <c>HEAD</c> commit hash via <c>git rev-parse HEAD</c>.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
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
    public string GetHeadCommitHash(string repositoryPath)
    {
        var result = RunGit(repositoryPath, "rev-parse", "HEAD");
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
    /// <returns>
    ///     <see langword="true"/> if <c>git ls-files</c> reports any tracked file under one of
    ///     the four known agent folders; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    ///     Per architecture.md's "Committed agent files" badge: this is purely advisory - it
    ///     never modifies git tracking state, it only reports whether the developer appears to
    ///     have accidentally committed proprietary agent content into a repo meant to gitignore
    ///     it.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the git executable cannot be
    ///     started, or when <c>git ls-files</c> exits with a non-zero code.</exception>
    public bool HasCommittedAgentFiles(string repositoryPath)
    {
        var arguments = new List<string> { "ls-files", "--" };
        arguments.AddRange(KnownAgentFolders);

        var result = RunGit(repositoryPath, arguments.ToArray());
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"'git ls-files' failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    /// <summary>
    ///     Gets the repository's current branch name, or <c>"(detached)"</c> when <c>HEAD</c> is
    ///     detached.
    /// </summary>
    /// <param name="repositoryPath">Absolute path to the repository working directory.</param>
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
    ///     common case free of a subprocess spawn.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the fallback git executable
    ///     cannot be started, or when <c>git rev-parse --abbrev-ref HEAD</c> exits with a
    ///     non-zero code.</exception>
    public string GetCurrentBranch(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var fastPathResult = TryReadBranchFromGitHead(repositoryPath);
        if (fastPathResult is not null)
        {
            return fastPathResult;
        }

        var result = RunGit(repositoryPath, "rev-parse", "--abbrev-ref", "HEAD");
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
    ///     capturing stdout/stderr.
    /// </summary>
    /// <param name="repositoryPath">Working directory the git process runs in.</param>
    /// <param name="arguments">Command-line arguments to pass to git.</param>
    /// <returns>The captured exit code and output streams.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryPath"/> is null,
    ///     empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the process fails to start (e.g.
    ///     the configured executable path does not exist).</exception>
    private GitCommandResult RunGit(string repositoryPath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

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
        // occurrences of the intermittent exit-code -1 failure.
        var argumentsText = string.Join(' ', arguments);
        _logger.LogDebug(
            "Starting git process '{GitExecutable}' with arguments '{Arguments}' in working directory '{WorkingDirectory}'",
            _gitExecutablePath, argumentsText, repositoryPath);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(startInfo)
                                 ?? throw new InvalidOperationException(
                                     $"Failed to start git executable '{_gitExecutablePath}'.");

            // Read output asynchronously to avoid deadlock from full stdout/stderr buffers
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            stopwatch.Stop();

            // A long WaitForExit points at a hung/slow process rather than a fast failure -
            // exactly the ambiguity the flaky-test investigation could not previously resolve.
            if (stopwatch.Elapsed > SlowExitWarningThreshold)
            {
                _logger.LogWarning(
                    "git process '{GitExecutable} {Arguments}' took an unexpectedly long {ElapsedMilliseconds}ms to exit",
                    _gitExecutablePath, argumentsText, stopwatch.ElapsedMilliseconds);
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            _logger.LogInformation(
                "git process '{GitExecutable} {Arguments}' exited with code {ExitCode} after {ElapsedMilliseconds}ms",
                _gitExecutablePath, argumentsText, process.ExitCode, stopwatch.ElapsedMilliseconds);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "git process '{GitExecutable} {Arguments}' failed with exit code {ExitCode}; stderr: {StandardError}",
                    _gitExecutablePath, argumentsText, process.ExitCode, stderr);
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

            _logger.LogError(
                ex,
                "Failed to run '{GitExecutable} {Arguments}' after {ElapsedMilliseconds}ms (Win32 NativeErrorCode={NativeErrorCode})",
                _gitExecutablePath, argumentsText, stopwatch.ElapsedMilliseconds, nativeErrorCode);

            throw new InvalidOperationException(
                $"Failed to run '{_gitExecutablePath} {argumentsText}': {ex.Message}", ex);
        }
    }
}
