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

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.GitIntegration;

/// <summary>
///     The transport a git command may use to reach a remote: the only one git is allowed to use
///     for an address.
/// </summary>
internal enum GitTransport
{
    /// <summary>
    ///     <c>https</c>.
    /// </summary>
    Https,

    /// <summary>
    ///     <c>ssh</c>.
    /// </summary>
    Ssh,

    /// <summary>
    ///     A local path (git's <c>file</c> transport).
    /// </summary>
    File
}

/// <summary>
///     Why a git operation against a remote failed, in terms the caller can act on.
/// </summary>
internal enum GitFailureKind
{
    /// <summary>
    ///     The failure is not one of the known kinds.
    /// </summary>
    Other,

    /// <summary>
    ///     The remote needs credentials that were not available (or were refused) and git was not
    ///     allowed or able to ask.
    /// </summary>
    AuthenticationRequired,

    /// <summary>
    ///     The repository or the requested ref does not exist (or is not visible to the user).
    /// </summary>
    NotFound,

    /// <summary>
    ///     The remote could not be reached.
    /// </summary>
    Unreachable,

    /// <summary>
    ///     ssh does not trust the host key of the server (it is not in <c>known_hosts</c>) and
    ///     cannot ask.
    /// </summary>
    HostKeyUnverified,

    /// <summary>
    ///     git refused a local repository owned by another user (<c>safe.directory</c>).
    /// </summary>
    UnsafeRepository,

    /// <summary>
    ///     The operation took longer than its time limit and was stopped.
    /// </summary>
    TimedOut,

    /// <summary>
    ///     The git executable could not be started.
    /// </summary>
    GitNotFound,

    /// <summary>
    ///     A git process exceeded its time limit or was cancelled and could not be confirmed to have
    ///     stopped, so a process may still be running.
    /// </summary>
    ProcessNotStopped,

    /// <summary>
    ///     The output was larger than AgentControl is willing to read.
    /// </summary>
    OutputTooLarge,

    /// <summary>
    ///     The git in use is older than the oldest version AgentControl supports for Git
    ///     repository sources.
    /// </summary>
    GitTooOld
}

/// <summary>
///     A failure of a git operation, with a message that is safe to show and to log.
/// </summary>
/// <remarks>
///     Derives from <see cref="InvalidOperationException"/>. The message is one of a small set of
///     fixed sentences chosen by <see cref="Kind"/> (plus the operation name and, for an unclassified
///     failure, the exit code); it never repeats anything git printed, because git's error text can
///     contain the remote address, host or local paths.
/// </remarks>
internal sealed class GitRemoteException : InvalidOperationException
{
    /// <summary>
    ///     Initializes the exception.
    /// </summary>
    /// <param name="kind">The classified cause.</param>
    /// <param name="message">A secret-free message.</param>
    public GitRemoteException(GitFailureKind kind, string message)
        : base(SecretRedactor.Redact(message))
    {
        Kind = kind;
    }

    /// <summary>
    ///     Gets the classified cause.
    /// </summary>
    public GitFailureKind Kind { get; }
}

/// <summary>
///     The remote a command talks to: its address text and the one transport that may be used.
/// </summary>
/// <param name="Url">The validated address text.</param>
/// <param name="Transport">The only transport git may use for it.</param>
internal readonly record struct GitRemoteTarget(string Url, GitTransport Transport);

/// <summary>
///     A tag a remote reports.
/// </summary>
/// <param name="Name">The tag name without <c>refs/tags/</c>.</param>
/// <param name="ObjectSha">The object the tag ref points to (the tag object for an annotated
///     tag, the commit for a lightweight one).</param>
/// <param name="CommitSha">The commit the tag finally points to (the peeled value for an
///     annotated tag).</param>
internal sealed record GitRemoteTag(string Name, string ObjectSha, string CommitSha);

/// <summary>
///     An entry of a git tree listing.
/// </summary>
/// <param name="Mode">The octal mode: <c>100644</c>/<c>100755</c> file, <c>120000</c> symbolic
///     link, <c>160000</c> submodule, <c>040000</c> tree.</param>
/// <param name="Type">The object type: <c>blob</c>, <c>tree</c> or <c>commit</c>.</param>
/// <param name="Sha">The object id.</param>
/// <param name="Size">The size in bytes of a blob; <see langword="null"/> for other objects.</param>
/// <param name="Path">The path relative to the repository root, <c>/</c>-separated.</param>
internal sealed record GitTreeEntry(string Mode, string Type, string Sha, long? Size, string Path);

/// <summary>
///     Settings of a <see cref="GitRemoteClient"/>.
/// </summary>
/// <param name="ResolveGitExecutable">Returns the git executable path or command name, evaluated
///     for every invocation.</param>
/// <param name="WorkRoot">An AgentControl-owned folder under which the client keeps the neutral
///     folder git runs in and the empty hooks and template folders; created when needed.</param>
/// <param name="Interactive"><see langword="true"/> when the operation was started by the user, so
///     git may show its credential UI; <see langword="false"/> for background work, which must
///     never prompt.</param>
/// <param name="Logger">Receives secret-free diagnostics, or <see langword="null"/> for the
///     application logger.</param>
/// <param name="ExtraConfiguration">Extra <c>name=value</c> settings passed with <c>-c</c> after
///     the built-in ones, for tests that must isolate the user's configuration.</param>
/// <param name="ExtraEnvironment">Extra environment variables for the child process, applied
///     last; a <see langword="null"/> value removes the variable. For tests.</param>
/// <param name="ListTimeout">The limit for listing the tags of a remote, or <see langword="null"/>
///     for the default (30 seconds in the background, 5 minutes when interactive).</param>
/// <param name="FetchTimeout">The limit for fetching a tag, or <see langword="null"/> for the
///     default (3 minutes in the background, 5 minutes when interactive).</param>
internal sealed record GitRemoteOptions(
    Func<string> ResolveGitExecutable,
    string WorkRoot,
    bool Interactive = false,
    ILogger? Logger = null,
    IReadOnlyList<string>? ExtraConfiguration = null,
    IReadOnlyDictionary<string, string?>? ExtraEnvironment = null,
    TimeSpan? ListTimeout = null,
    TimeSpan? FetchTimeout = null)
{
    /// <summary>
    ///     Gets a test seam that replaces the termination of a git process tree on timeout or
    ///     cancellation, so a process that cannot be stopped can be simulated; <see langword="null"/>
    ///     for the real termination (<see cref="Process.Kill(bool)"/> of the whole tree).
    /// </summary>
    internal Action<Process>? KillProcessTree { get; init; }

    /// <summary>
    ///     Gets how long a killed process is waited for before its termination is reported as not
    ///     confirmed; <see langword="null"/> for the default (10 seconds). For tests.
    /// </summary>
    internal TimeSpan? StopTimeout { get; init; }
}

/// <summary>
///     Runs git against a remote repository whose content and answers are untrusted, with every
///     protection that makes that safe, never prompting in the background and never leaving a
///     process behind.
/// </summary>
/// <remarks>
///     <para>
///     <b>Command-scoped configuration.</b> Everything AgentControl needs to be set is passed with
///     <c>-c</c> on the command line of that one process (or in its environment), never written to
///     the user's configuration or to a repository: only these transports (<c>protocol.allow=never</c>
///     plus the single kind of the address, and <c>GIT_ALLOW_PROTOCOL</c>), no hooks
///     (<c>core.hooksPath</c> is an empty AgentControl-owned folder and the temporary repository is
///     created from an empty template), no filesystem monitor, no submodules, no LFS smudge, object
///     checks on transfer, and no automatic maintenance that could outlive the command. Content is
///     never checked out: a repository's <c>.gitattributes</c> can select any <c>filter.&lt;name&gt;</c>
///     driver the user's configuration defines, and checkout would run it, so validated blobs are
///     read with <c>git cat-file --batch</c>, which applies no filter, attribute or line-ending
///     conversion, and handed to the caller as raw bytes. The user's own configuration (credential
///     helpers, proxies, CA settings, <c>insteadOf</c> rewrites) is deliberately left in force.
///     </para>
///     <para>
///     <b>Prompts.</b> No terminal exists, so <c>GIT_TERMINAL_PROMPT=0</c> always. Background work
///     (<see cref="GitRemoteOptions.Interactive"/> false) additionally sets
///     <c>GCM_INTERACTIVE=never</c>, empty <c>GIT_ASKPASS</c>/<c>SSH_ASKPASS</c>,
///     <c>SSH_ASKPASS_REQUIRE=never</c> and <c>core.askPass=</c>, so nothing can open a dialog; work the
///     user started leaves Git Credential Manager and the user's askpass free to show their UI.
///     ssh (when the command is OpenSSH) always runs with <c>BatchMode=yes</c> and a connect
///     timeout, so a passphrase or host-key question fails fast instead of hanging; a host key must
///     have been accepted once by the user in a terminal.
///     </para>
///     <para>
///     <b>Arguments and environment.</b> Arguments are passed as a list (no shell), the address
///     and refs follow <c>--</c>, tag names are validated again here, and pathspecs are literal.
///     Trace and redirection variables (<c>GIT_TRACE*</c>, <c>GIT_CURL_VERBOSE</c>, <c>GIT_DIR</c>,
///     <c>GIT_WORK_TREE</c>, <c>GIT_INDEX_FILE</c>, <c>GIT_EXEC_PATH</c> and similar) are removed
///     so trace output cannot carry a header into an error message and a stray variable cannot
///     redirect an operation. git runs in a neutral folder so a surrounding repository's
///     configuration is never read.
///     </para>
///     <para>
///     <b>Bounded.</b> Every operation has a time limit; output is read up to a cap counted in
///     bytes and the rest drained, standard error is capped and used only to classify the failure,
///     and canceling or timing out kills the whole process tree and waits for it to end; a process
///     that cannot be confirmed to have ended is reported as
///     <see cref="GitFailureKind.ProcessNotStopped"/>. Failures become
///     <see cref="GitRemoteException"/> with a classified <see cref="GitRemoteException.Kind"/> and a
///     fixed message that never repeats git's text. Thread-safe: no state is kept between calls other
///     than the cached ssh command and the per-executable version result.
///     </para>
///     <para>
///     <b>Version.</b> The commands need Git 2.29 or newer (<c>fetch --no-write-fetch-head</c>);
///     <see cref="EnsureSupportedVersionAsync"/> checks it once per executable and process (callers
///     that ask at the same time share the one check) and fails with
///     <see cref="GitFailureKind.GitTooOld"/> and an actionable message.
///     </para>
/// </remarks>
internal sealed partial class GitRemoteClient
{
    /// <summary>
    ///     The longest standard output read from <c>git ls-remote</c> and <c>git ls-tree</c>:
    ///     8 MB; more is reported as too much.
    /// </summary>
    internal const int MaxListOutputBytes = 8 * 1024 * 1024;

    /// <summary>
    ///     The longest standard error kept for classification: 64 KB; the rest is discarded.
    /// </summary>
    internal const int MaxErrorBytes = 64 * 1024;

    /// <summary>
    ///     The longest header line of <c>git cat-file --batch</c> that is accepted
    ///     (<c>&lt;object id&gt; blob &lt;size&gt;</c>).
    /// </summary>
    private const int MaxBatchHeaderBytes = 256;

    /// <summary>
    ///     The oldest git AgentControl supports for Git repository sources: 2.29, which added
    ///     <c>fetch --no-write-fetch-head</c>.
    /// </summary>
    internal static readonly Version MinimumGitVersion = new(2, 29);

    /// <summary>
    ///     The longest tag name that is considered.
    /// </summary>
    private const int MaxTagNameLength = 255;

    /// <summary>
    ///     The connect timeout, in seconds, given to ssh.
    /// </summary>
    private const int SshConnectTimeoutSeconds = 15;

    /// <summary>
    ///     The longest standard output read from <c>git --version</c>: 4 KB.
    /// </summary>
    private const int MaxVersionOutputBytes = 4096;

    /// <summary>
    ///     The time limit for <c>git --version</c>.
    /// </summary>
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The git executables (as resolved) already found to be new enough, so <c>git --version</c>
    ///     runs at most once for each in this process.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> VerifiedExecutables =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    ///     One gate per git executable (as resolved), so callers that ask for the version check of the
    ///     same executable at the same time wait for one <c>git --version</c> instead of each running
    ///     their own.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> VersionGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    ///     The default time to wait for a killed process to end before its termination is reported as
    ///     not confirmed.
    /// </summary>
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Environment variables that are removed from the child process: trace output could carry
    ///     an Authorization header into an error message, and the others redirect what an
    ///     operation works on or which git programs it runs.
    /// </summary>
    private static readonly string[] RemovedVariables =
    [
        "GIT_CURL_VERBOSE", "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_EXEC_PATH",
        "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_COMMON_DIR", "GIT_NAMESPACE",
        "GIT_PREFIX", "GIT_REPLACE_REF_BASE", "GIT_SHALLOW_FILE"
    ];

    /// <summary>
    ///     Standard output must not be given a byte-order mark and is read as UTF-8.
    /// </summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    ///     The settings.
    /// </summary>
    private readonly GitRemoteOptions _options;

    /// <summary>
    ///     Receives diagnostics that never contain a secret.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     The neutral folder git runs in.
    /// </summary>
    private readonly string _neutralDirectory;

    /// <summary>
    ///     The empty folder used as <c>core.hooksPath</c>.
    /// </summary>
    private readonly string _emptyHooksDirectory;

    /// <summary>
    ///     The empty folder used as the template of a new repository.
    /// </summary>
    private readonly string _emptyTemplateDirectory;

    /// <summary>
    ///     The ssh command resolved once from the environment and git configuration.
    /// </summary>
    private string? _sshCommand;

    /// <summary>
    ///     Initializes a client.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or its
    ///     executable resolver is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the work root is blank.</exception>
    public GitRemoteClient(GitRemoteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ResolveGitExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkRoot);

        _options = options;
        _logger = new SecretRedactingLogger(options.Logger ?? AppLogging.Factory.CreateLogger<GitRemoteClient>());
        var root = Path.GetFullPath(options.WorkRoot);
        _neutralDirectory = Path.Combine(root, "neutral");
        _emptyHooksDirectory = Path.Combine(root, "empty-hooks");
        _emptyTemplateDirectory = Path.Combine(root, "empty-template");
    }

    /// <summary>
    ///     Gets the folder that <c>GIT_CEILING_DIRECTORIES</c> names: the parent of the neutral
    ///     folder, so git never looks above it for a repository.
    /// </summary>
    internal string CeilingDirectory => Path.GetDirectoryName(_neutralDirectory)!;

    /// <summary>
    ///     Gets the pattern of a full object id: 40 (SHA-1) or 64 (SHA-256) lower-case
    ///     hexadecimal digits.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[0-9a-f]{40}([0-9a-f]{24})?\z", RegexOptions.CultureInvariant)]
    internal static partial Regex ObjectIdPattern();

    /// <summary>
    ///     Gets the pattern of the tag names this client will put on a command line.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9._/+-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TagNamePattern();

    /// <summary>
    ///     Gets the pattern of the version in the output of <c>git --version</c>, for example
    ///     <c>git version 2.51.0.windows.2</c>.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"\bgit version (\d{1,4})\.(\d{1,4})(?:\.(\d{1,4}))?", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    /// <summary>
    ///     Gets the pattern of a listing glob: tag-name characters and <c>*</c>.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex(@"^[A-Za-z0-9._/+*-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex GlobPattern();

    /// <summary>
    ///     Checks that the git in use is new enough for Git repository sources, running
    ///     <c>git --version</c> once per executable and process.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation; the process tree is killed.</param>
    /// <returns>A task that completes when git is known to be supported. Callers that ask at the same
    ///     time for the same executable share one <c>git --version</c> run. A git whose version text
    ///     cannot be read is not blocked (a warning is logged), and a failure to run git is not
    ///     remembered, so a later call tries again.</returns>
    /// <exception cref="GitRemoteException">Thrown with <see cref="GitFailureKind.GitTooOld"/> when
    ///     git is older than <see cref="MinimumGitVersion"/>, or when git cannot be run.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
    public async Task EnsureSupportedVersionAsync(CancellationToken cancellationToken)
    {
        var executable = _options.ResolveGitExecutable();
        if (VerifiedExecutables.ContainsKey(executable))
        {
            return;
        }

        // One check at a time for one executable: callers that arrive while it runs wait for it and
        // then find its result, so git --version never runs twice for the same executable
        var gate = VersionGates.GetOrAdd(executable, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (VerifiedExecutables.ContainsKey(executable))
            {
                return;
            }

            var arguments = BuildArguments(null, GitTransport.File, "--version");
            var result = await RunAsync(
                "version", arguments, GitTransport.File, VersionTimeout, MaxVersionOutputBytes, cancellationToken);
            var version = ParseVersion(result.Output);
            if (version is null)
            {
                _logger.LogWarning("The version of git could not be read from its output; it is assumed to be supported.");
            }
            else if (version < MinimumGitVersion)
            {
                throw new GitRemoteException(
                    GitFailureKind.GitTooOld,
                    $"AgentControl needs Git {MinimumGitVersion} or newer for Git repository sources, but the Git in use " +
                    $"is {version}. Update Git (https://git-scm.com/downloads) or choose a newer Git under " +
                    "Settings > Git executable.");
            }

            VerifiedExecutables[executable] = true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    ///     Reads the version from the output of <c>git --version</c>.
    /// </summary>
    /// <param name="output">The standard output, for example <c>git version 2.51.0.windows.2</c>
    ///     or <c>git version 2.39.5 (Apple Git-154)</c>.</param>
    /// <returns>The major, minor and patch numbers (patch zero when absent), or
    ///     <see langword="null"/> when the text is not a git version.</returns>
    internal static Version? ParseVersion(string? output)
    {
        var match = VersionPattern().Match(output ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return new Version(
            int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            patch);
    }

    /// <summary>
    ///     Lists the tags of a remote without cloning it.
    /// </summary>
    /// <param name="target">The remote.</param>
    /// <param name="refGlob">The glob that narrows the listing, for example
    ///     <c>refs/tags/release/*</c>; must start with <c>refs/tags/</c> and contain only tag-name
    ///     characters and <c>*</c>.</param>
    /// <param name="cancellationToken">Cancels the operation; the process tree is killed.</param>
    /// <returns>The tags (one entry per tag, with an annotated tag resolved to its commit),
    ///     ordered by name. Lines that are not well-formed tag refs, and tags whose name is not a
    ///     safe tag name (see <see cref="IsSafeTagName"/>), are ignored.</returns>
    /// <exception cref="ArgumentException">Thrown when the glob is not acceptable.</exception>
    /// <exception cref="GitRemoteException">Thrown when git fails, times out, is missing, or prints
    ///     more than 8 MB.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
    public async Task<IReadOnlyList<GitRemoteTag>> ListTagsAsync(
        GitRemoteTarget target, string refGlob, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Url);
        ArgumentNullException.ThrowIfNull(refGlob);
        if (!refGlob.StartsWith("refs/tags/", StringComparison.Ordinal) || !GlobPattern().IsMatch(refGlob))
        {
            throw new ArgumentException("The tag glob is not acceptable.", nameof(refGlob));
        }

        var arguments = BuildArguments(null, target.Transport, "ls-remote", "--tags", "--", target.Url, refGlob);
        var result = await RunAsync(
            "ls-remote", arguments, target.Transport, ListTimeout, MaxListOutputBytes, cancellationToken);
        return ParseTags(result.Output);
    }

    /// <summary>
    ///     Creates an empty repository (from an empty template, so it has no hooks) for a fetch.
    /// </summary>
    /// <param name="repositoryPath">The folder to create the repository in.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the repository exists.</returns>
    /// <exception cref="GitRemoteException">Thrown when git fails.</exception>
    public async Task InitRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        EnsureWorkFolders();

        var arguments = BuildArguments(
            null, GitTransport.File,
            "init", "-q", "--template=" + _emptyTemplateDirectory, "--", repositoryPath);
        await RunAsync("init", arguments, GitTransport.File, FetchTimeout, 64 * 1024, cancellationToken);
    }

    /// <summary>
    ///     Fetches exactly one tag, shallow (depth one, no other tags, no submodules), into a
    ///     repository made by <see cref="InitRepositoryAsync"/>.
    /// </summary>
    /// <param name="target">The remote.</param>
    /// <param name="repositoryPath">The repository to fetch into.</param>
    /// <param name="tagName">The tag, without <c>refs/tags/</c>; must be a safe tag name
    ///     (<see cref="IsSafeTagName"/>).</param>
    /// <param name="cancellationToken">Cancels the operation; the process tree is killed.</param>
    /// <returns>The commit the tag points to in the fetched repository.</returns>
    /// <exception cref="ArgumentException">Thrown when the tag name is not acceptable.</exception>
    /// <exception cref="GitRemoteException">Thrown when git fails, times out or is missing.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
    public async Task<string> FetchTagAsync(
        GitRemoteTarget target, string repositoryPath, string tagName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        RequireSafeTagName(tagName);

        var refspec = $"refs/tags/{tagName}:refs/tags/{tagName}";
        var fetch = BuildArguments(
            repositoryPath, target.Transport,
            "fetch", "--depth=1", "--no-tags", "--no-recurse-submodules", "--no-write-fetch-head", "--", target.Url, refspec);
        await RunAsync("fetch", fetch, target.Transport, FetchTimeout, 64 * 1024, cancellationToken);

        var parse = BuildArguments(
            repositoryPath, target.Transport,
            "rev-parse", "--verify", "--quiet", $"refs/tags/{tagName}^{{commit}}");
        var result = await RunAsync("rev-parse", parse, target.Transport, FetchTimeout, 4096, cancellationToken);
        var commit = result.Output.Trim();
        if (!ObjectIdPattern().IsMatch(commit))
        {
            throw new GitRemoteException(GitFailureKind.Other, "The fetched tag does not point to a commit.");
        }

        return commit;
    }

    /// <summary>
    ///     Lists the entries of a commit's tree, so the content can be checked before anything is
    ///     written to disk.
    /// </summary>
    /// <param name="repositoryPath">The repository.</param>
    /// <param name="commit">The full commit id.</param>
    /// <param name="paths">The paths to list (literal, relative to the repository root).</param>
    /// <param name="recursive"><see langword="true"/> to list files below directories (as
    ///     <c>-r</c>); <see langword="false"/> to list the named entries themselves.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The entries, possibly none.</returns>
    /// <exception cref="ArgumentException">Thrown when the commit id is not a full object id or no
    ///     path is given (an empty list would make git list the whole tree).</exception>
    /// <exception cref="GitRemoteException">Thrown when git fails or prints more than 8 MB.</exception>
    public async Task<IReadOnlyList<GitTreeEntry>> ListTreeAsync(
        string repositoryPath,
        string commit,
        IReadOnlyList<string> paths,
        bool recursive,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(paths);
        RequireObjectId(commit);
        if (paths.Count == 0)
        {
            throw new ArgumentException("At least one path is required.", nameof(paths));
        }

        var command = new List<string> { "ls-tree" };
        if (recursive)
        {
            command.Add("-r");
        }

        command.AddRange(["-z", "--full-tree", "-l", commit, "--"]);
        command.AddRange(paths);
        var arguments = BuildArguments(repositoryPath, GitTransport.File, [.. command]);
        var result = await RunAsync(
            "ls-tree", arguments, GitTransport.File, FetchTimeout, MaxListOutputBytes, cancellationToken);
        return ParseTree(result.Output);
    }

    /// <summary>
    ///     Reads the raw bytes of blobs that were listed with <see cref="ListTreeAsync"/> and hands
    ///     them to the caller, without a checkout.
    /// </summary>
    /// <remarks>
    ///     A checkout would run whatever <c>filter.&lt;name&gt;.smudge</c> or <c>.process</c> driver
    ///     the repository's <c>.gitattributes</c> selects from the user's configuration, and would
    ///     convert line endings and expand identifiers, so the bytes on disk would no longer be the
    ///     bytes in the commit. <c>git cat-file --batch</c> reads objects only: no attribute, filter
    ///     or conversion is applied, and each header (object id, type, size) is checked against the
    ///     listing before the bytes are accepted.
    /// </remarks>
    /// <param name="repositoryPath">The repository.</param>
    /// <param name="blobs">The entries to read; each must be a blob with a known size. The same
    ///     object id may appear more than once.</param>
    /// <param name="openDestination">Called once per entry, just before its bytes are written, to
    ///     get the stream to write to; the client disposes the stream when the entry is complete.
    ///     The caller decides the file name and is responsible for its safety.</param>
    /// <param name="maxTotalBytes">The most bytes all entries together may hold; more is refused
    ///     before git starts.</param>
    /// <param name="cancellationToken">Cancels the operation; the process tree is killed.</param>
    /// <returns>A task that completes when every blob has been written.</returns>
    /// <exception cref="ArgumentException">Thrown when no entry is given, or an entry is not a blob
    ///     with a full object id and a known size.</exception>
    /// <exception cref="GitRemoteException">Thrown when git fails, times out or is missing, when the
    ///     sizes exceed <paramref name="maxTotalBytes"/> (<see cref="GitFailureKind.OutputTooLarge"/>),
    ///     or when git's answer does not match the entry that was asked for.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
    public async Task ReadBlobsAsync(
        string repositoryPath,
        IReadOnlyList<GitTreeEntry> blobs,
        Func<GitTreeEntry, Stream> openDestination,
        long maxTotalBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(blobs);
        ArgumentNullException.ThrowIfNull(openDestination);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTotalBytes);
        if (blobs.Count == 0)
        {
            throw new ArgumentException("At least one blob is required.", nameof(blobs));
        }

        // Everything is checked before git starts: only well-formed blobs are asked for, and the
        // declared sizes must fit the budget
        long declared = 0;
        foreach (var blob in blobs)
        {
            if (blob.Type != "blob" || !ObjectIdPattern().IsMatch(blob.Sha) || blob.Size is null or < 0)
            {
                throw new ArgumentException("Every entry must be a blob with an object id and a size.", nameof(blobs));
            }

            if (blob.Size.Value > maxTotalBytes - declared)
            {
                throw new GitRemoteException(
                    GitFailureKind.OutputTooLarge,
                    "git cat-file would return more data than AgentControl will read (the repository content is too large).");
            }

            declared += blob.Size.Value;
        }

        EnsureWorkFolders();
        var arguments = BuildArguments(repositoryPath, GitTransport.File, "cat-file", "--batch");
        var startInfo = CreateStartInfo(arguments, GitTransport.File, null);
        _logger.LogDebug(
            "Running git cat-file ({Interactive}).", _options.Interactive ? "interactive" : "background");
        using var process = StartProcess("cat-file", startInfo);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(FetchTimeout);

        // The requests are written while the answers are read, so neither pipe can fill and block
        // the other; standard error is drained for classification only
        var error = ReadBoundedAsync(process.StandardError.BaseStream, MaxErrorBytes, limit.Token);
        var requests = WriteRequestsAsync(process, blobs, limit.Token);
        try
        {
            var complete = await ReadBatchAnswersAsync(
                process.StandardOutput.BaseStream, blobs, openDestination, limit.Token);
            await requests;
            await process.WaitForExitAsync(limit.Token);
            var (errorText, _) = await error;
            if (process.ExitCode != 0)
            {
                throw CreateFailure("cat-file", errorText, process.ExitCode);
            }

            if (!complete)
            {
                throw new GitRemoteException(GitFailureKind.Other, "git cat-file returned less data than was asked for.");
            }
        }
        catch (OperationCanceledException ex)
        {
            await StopProcessAsync(process, "cat-file", output: null, error, requests);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw CreateTimeout("cat-file", FetchTimeout, ex);
        }
        catch (Exception)
        {
            // Any other failure (a refused answer, a destination that cannot be written) also ends
            // the process: it must not be left waiting for requests or output
            await StopProcessAsync(process, "cat-file", output: null, error, requests);
            throw;
        }
    }

    /// <summary>
    ///     Writes the object ids of the requested blobs to the standard input of
    ///     <c>git cat-file --batch</c>, one per line, then closes it.
    /// </summary>
    /// <param name="process">The running process.</param>
    /// <param name="blobs">The entries to ask for.</param>
    /// <param name="cancellationToken">Cancels the writing.</param>
    /// <returns>A task that completes when the requests are written (or git stopped reading them;
    ///     its exit code then tells why).</returns>
    private static async Task WriteRequestsAsync(
        Process process, IReadOnlyList<GitTreeEntry> blobs, CancellationToken cancellationToken)
    {
        try
        {
            var input = process.StandardInput.BaseStream;
            foreach (var blob in blobs)
            {
                await input.WriteAsync(Encoding.ASCII.GetBytes(blob.Sha + "\n"), cancellationToken);
            }

            await input.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // Git ended before it read every request; the answers and its exit code say why
        }
        finally
        {
            CloseInput(process);
        }
    }

    /// <summary>
    ///     Reads the answers of <c>git cat-file --batch</c> and writes each blob's bytes to the
    ///     destination the caller opens for it, checking every header against the entry asked for.
    /// </summary>
    /// <param name="output">The standard output of git.</param>
    /// <param name="blobs">The entries that were asked for, in order.</param>
    /// <param name="openDestination">Opens the destination of an entry.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns><see langword="true"/> when every blob was read; <see langword="false"/> when git's
    ///     output ended before the first missing answer (its exit code then tells why).</returns>
    /// <exception cref="GitRemoteException">Thrown when an answer is not the blob that was asked
    ///     for, or git prints more than was asked for.</exception>
    private static async Task<bool> ReadBatchAnswersAsync(
        Stream output,
        IReadOnlyList<GitTreeEntry> blobs,
        Func<GitTreeEntry, Stream> openDestination,
        CancellationToken cancellationToken)
    {
        var reader = new BatchReader(output);
        foreach (var blob in blobs)
        {
            var header = await reader.ReadLineAsync(MaxBatchHeaderBytes, cancellationToken);
            if (header is null)
            {
                return false;
            }

            // "<object id> blob <size>": the answer must be exactly the blob that was listed
            var parts = header.Split(' ');
            if (parts.Length != 3 || parts[0] != blob.Sha || parts[1] != "blob" ||
                !long.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) ||
                size != blob.Size)
            {
                throw new GitRemoteException(GitFailureKind.Other, "git cat-file did not return the object that was asked for.");
            }

            await using (var destination = openDestination(blob))
            {
                if (!await reader.CopyAsync(destination, size, cancellationToken))
                {
                    return false;
                }
            }

            // Each answer ends with a line feed
            if (await reader.ReadByteAsync(cancellationToken) != '\n')
            {
                throw new GitRemoteException(GitFailureKind.Other, "git cat-file returned unexpected data.");
            }
        }

        if (await reader.ReadByteAsync(cancellationToken) != -1)
        {
            throw new GitRemoteException(GitFailureKind.Other, "git cat-file returned more data than was asked for.");
        }

        return true;
    }

    /// <summary>
    ///     Builds the ssh command git is given for an ssh remote: the user's own command (from
    ///     <c>GIT_SSH_COMMAND</c> or <c>core.sshCommand</c>, else <c>ssh</c>) with
    ///     <c>-o BatchMode=yes -o ConnectTimeout=15</c> appended when it is OpenSSH.
    /// </summary>
    /// <param name="existing">The user's ssh command, or <see langword="null"/> or empty for the
    ///     default <c>ssh</c>.</param>
    /// <returns>The command to put in <c>GIT_SSH_COMMAND</c>, or <see langword="null"/> when the
    ///     user's command is not OpenSSH (for example <c>plink</c>), which is then left untouched
    ///     and bounded only by the operation's time limit.</returns>
    internal static string? ComposeSshCommand(string? existing)
    {
        var command = string.IsNullOrWhiteSpace(existing) ? "ssh" : existing.Trim();

        // The program is the first word (quoted when its path has spaces)
        string program;
        if (command.StartsWith('"'))
        {
            var close = command.IndexOf('"', 1);
            program = close > 0 ? command[1..close] : command[1..];
        }
        else
        {
            var space = command.IndexOf(' ', StringComparison.Ordinal);
            program = space > 0 ? command[..space] : command;
        }

        var name = Path.GetFileNameWithoutExtension(program.Replace('\\', '/').Split('/')[^1]);
        if (!name.Equals("ssh", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return command + $" -o BatchMode=yes -o ConnectTimeout={SshConnectTimeoutSeconds}";
    }

    /// <summary>
    ///     Parses the output of <c>git ls-remote --tags</c>.
    /// </summary>
    /// <param name="output">The standard output.</param>
    /// <returns>One entry per tag, ordered by name.</returns>
    internal static IReadOnlyList<GitRemoteTag> ParseTags(string output)
    {
        const string Prefix = "refs/tags/";
        var objects = new Dictionary<string, string>(StringComparer.Ordinal);
        var peeled = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0)
            {
                continue;
            }

            var sha = line[..tab];
            var reference = line[(tab + 1)..];
            if (!ObjectIdPattern().IsMatch(sha) || !reference.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var name = reference[Prefix.Length..];
            var isPeeled = name.EndsWith("^{}", StringComparison.Ordinal);
            var tagName = isPeeled ? name[..^3] : name;

            // A name a server reports is untrusted: one that is not a legal, safe tag name is
            // dropped here and never used
            if (!IsSafeTagName(tagName))
            {
                continue;
            }

            if (isPeeled)
            {
                peeled[tagName] = sha;
            }
            else
            {
                objects[tagName] = sha;
            }
        }

        return [.. objects
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new GitRemoteTag(pair.Key, pair.Value, peeled.TryGetValue(pair.Key, out var commit) ? commit : pair.Value))];
    }

    /// <summary>
    ///     Parses the output of <c>git ls-tree -z -l</c>.
    /// </summary>
    /// <param name="output">The standard output (NUL-separated records).</param>
    /// <returns>The entries; a record that is not well-formed is ignored.</returns>
    internal static IReadOnlyList<GitTreeEntry> ParseTree(string output)
    {
        var entries = new List<GitTreeEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<mode> <type> <sha> <size>\t<path>" - the size is right-aligned with spaces
            var tab = record.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0)
            {
                continue;
            }

            var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 4 || !ObjectIdPattern().IsMatch(fields[2]))
            {
                continue;
            }

            long? size = null;
            if (fields[3] != "-" && long.TryParse(fields[3], out var parsed))
            {
                size = parsed;
            }

            entries.Add(new GitTreeEntry(fields[0], fields[1], fields[2], size, record[(tab + 1)..]));
        }

        return entries;
    }

    /// <summary>
    ///     Classifies a failed git invocation from its redacted error text.
    /// </summary>
    /// <param name="errorText">What git wrote to standard error.</param>
    /// <returns>The classified cause.</returns>
    internal static GitFailureKind Classify(string errorText)
    {
        var text = errorText.ToLowerInvariant();
        if (text.Contains("dubious ownership", StringComparison.Ordinal))
        {
            return GitFailureKind.UnsafeRepository;
        }

        if (text.Contains("host key verification failed", StringComparison.Ordinal))
        {
            return GitFailureKind.HostKeyUnverified;
        }

        if (ContainsAny(
                text,
                "authentication failed", "could not read username", "could not read password",
                "terminal prompts disabled", "permission denied", "unable to get password",
                "returned error: 401", "returned error: 403", "access denied", "invalid credentials",
                "invalid username or password", "authentication required"))
        {
            return GitFailureKind.AuthenticationRequired;
        }

        if (ContainsAny(
                text,
                "repository not found", "does not appear to be a git repository", "returned error: 404",
                "couldn't find remote ref", "not our ref") ||
            (text.Contains("repository '", StringComparison.Ordinal) && text.Contains("' not found", StringComparison.Ordinal)))
        {
            return GitFailureKind.NotFound;
        }

        return ContainsAny(
            text,
            "could not resolve host", "connection refused", "connection timed out", "network is unreachable",
            "failed to connect", "could not connect", "unable to access", "operation timed out",
            "ssl ", "tls ", "proxy", "connection reset", "connection closed", "kex_exchange_identification")
            ? GitFailureKind.Unreachable
            : GitFailureKind.Other;
    }

    /// <summary>
    ///     Determines whether a text contains any of the given fragments.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="fragments">The fragments.</param>
    /// <returns><see langword="true"/> when any fragment occurs.</returns>
    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.Ordinal));

    /// <summary>
    ///     Determines whether a tag name is one AgentControl is willing to put on a command line:
    ///     only the characters <c>A-Z a-z 0-9 . _ / + -</c>, at most 255 characters, and a legal
    ///     git ref name (no empty path component, none starting with <c>.</c> or ending in
    ///     <c>.lock</c>, no <c>..</c>, not ending in <c>.</c>, not starting with <c>-</c>). The one
    ///     implementation of the rule, shared with the tag pattern (which sits above this unit).
    /// </summary>
    /// <param name="tagName">The tag name without the <c>refs/tags/</c> prefix.</param>
    /// <returns><see langword="true"/> when the name is acceptable.</returns>
    internal static bool IsSafeTagName(string? tagName)
    {
        if (string.IsNullOrEmpty(tagName) || tagName.Length > MaxTagNameLength ||
            !TagNamePattern().IsMatch(tagName) || tagName.StartsWith('-') ||
            tagName.EndsWith('.') || tagName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var component in tagName.Split('/'))
        {
            if (component.Length == 0 || component.StartsWith('.') ||
                component.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Requires a tag name that is safe to put in a refspec.
    /// </summary>
    /// <param name="tagName">The tag name.</param>
    /// <exception cref="ArgumentException">Thrown when it is not acceptable.</exception>
    private static void RequireSafeTagName(string tagName)
    {
        if (!IsSafeTagName(tagName))
        {
            throw new ArgumentException("The tag name is not acceptable.", nameof(tagName));
        }
    }

    /// <summary>
    ///     Requires a full object id.
    /// </summary>
    /// <param name="commit">The id.</param>
    /// <exception cref="ArgumentException">Thrown when it is not 40 or 64 lower-case hex digits.</exception>
    private static void RequireObjectId(string commit)
    {
        if (!ObjectIdPattern().IsMatch(commit ?? string.Empty))
        {
            throw new ArgumentException("The commit id is not a full object id.", nameof(commit));
        }
    }

    /// <summary>
    ///     Gets the time limit for listing tags.
    /// </summary>
    private TimeSpan ListTimeout =>
        _options.ListTimeout ?? (_options.Interactive ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30));

    /// <summary>
    ///     Gets the time limit for the operations that move or read objects.
    /// </summary>
    private TimeSpan FetchTimeout =>
        _options.FetchTimeout ?? (_options.Interactive ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(3));

    /// <summary>
    ///     Creates the folders git runs in and the empty hooks and template folders.
    /// </summary>
    private void EnsureWorkFolders()
    {
        Directory.CreateDirectory(_neutralDirectory);
        Directory.CreateDirectory(_emptyHooksDirectory);
        Directory.CreateDirectory(_emptyTemplateDirectory);
    }

    /// <summary>
    ///     Builds the argument list of one git invocation: the folder, the command-scoped
    ///     configuration, literal pathspecs, then the command.
    /// </summary>
    /// <param name="repositoryPath">The repository to run in (<c>-C</c>), or <see langword="null"/>.</param>
    /// <param name="transport">The only transport allowed.</param>
    /// <param name="command">The git command and its arguments.</param>
    /// <returns>The arguments.</returns>
    internal List<string> BuildArguments(
        string? repositoryPath, GitTransport transport, params string[] command)
    {
        var arguments = new List<string>();
        if (repositoryPath is not null)
        {
            arguments.AddRange(["-C", repositoryPath]);
        }

        var settings = new List<string>
        {
            "protocol.allow=never",
            transport switch
            {
                GitTransport.Https => "protocol.https.allow=always",
                GitTransport.Ssh => "protocol.ssh.allow=always",
                _ => "protocol.file.allow=always"
            },
            "protocol.ext.allow=never",
            "protocol.fd.allow=never",
            "core.hooksPath=" + _emptyHooksDirectory,
            "core.fsmonitor=false",
            "core.untrackedCache=false"
        };

        settings.AddRange(
        [
            "core.autocrlf=false", "core.eol=lf", "core.protectNTFS=true", "core.protectHFS=true",
            "transfer.fsckObjects=true", "fetch.fsckObjects=true",
            "fetch.recurseSubmodules=false", "submodule.recurse=false",
            "gc.auto=0", "maintenance.auto=false",
            "filter.lfs.smudge=", "filter.lfs.process=", "filter.lfs.required=false",
            "http.followRedirects=initial", "http.lowSpeedLimit=1000", "http.lowSpeedTime=30"
        ]);
        if (!_options.Interactive)
        {
            settings.Add("core.askPass=");
        }

        settings.Add("advice.detachedHead=false");
        settings.AddRange(_options.ExtraConfiguration ?? []);

        foreach (var setting in settings)
        {
            arguments.AddRange(["-c", setting]);
        }

        arguments.Add("--literal-pathspecs");
        arguments.AddRange(command);
        return arguments;
    }

    /// <summary>
    ///     Determines whether an environment variable is removed from the child process.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns><see langword="true"/> for any <c>GIT_TRACE*</c> variable and the redirecting ones.</returns>
    private static bool IsRemovedVariable(string name) =>
        name.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase) ||
        RemovedVariables.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Applies the environment of every invocation to a start-info.
    /// </summary>
    /// <param name="environment">The child's environment, initially a copy of ours.</param>
    /// <param name="transport">The only transport allowed.</param>
    /// <param name="sshCommand">The ssh command to set for an ssh remote, or <see langword="null"/>.</param>
    internal void ApplyEnvironment(IDictionary<string, string?> environment, GitTransport transport, string? sshCommand)
    {
        // Trace output could carry an Authorization header; the others redirect the operation
        foreach (var key in environment.Keys.Where(IsRemovedVariable).ToList())
        {
            environment.Remove(key);
        }

        // No terminal exists, so git must never try to read one
        environment["GIT_TERMINAL_PROMPT"] = "0";
        if (!_options.Interactive)
        {
            environment["GCM_INTERACTIVE"] = "never";
            environment["GIT_ASKPASS"] = string.Empty;
            environment["SSH_ASKPASS"] = string.Empty;
            environment["SSH_ASKPASS_REQUIRE"] = "never";
        }

        environment["GIT_ALLOW_PROTOCOL"] = transport switch
        {
            GitTransport.Https => "https",
            GitTransport.Ssh => "ssh",
            _ => "file"
        };
        environment["GIT_LFS_SKIP_SMUDGE"] = "1";
        environment["GIT_LITERAL_PATHSPECS"] = "1";
        environment["GIT_OPTIONAL_LOCKS"] = "0";
        environment["GIT_NO_LAZY_FETCH"] = "1";
        environment["LC_ALL"] = "C";
        environment["LANG"] = "C";
        environment["GIT_CEILING_DIRECTORIES"] = CeilingDirectory;
        if (sshCommand is not null)
        {
            environment["GIT_SSH_COMMAND"] = sshCommand;
        }

        foreach (var (name, value) in _options.ExtraEnvironment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                environment.Remove(name);
            }
            else
            {
                environment[name] = value;
            }
        }
    }

    /// <summary>
    ///     Resolves the ssh command for an ssh remote, once: the user's <c>GIT_SSH_COMMAND</c>, else
    ///     <c>core.sshCommand</c>, else <c>ssh</c>, made non-interactive when it is OpenSSH.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The command to use, or <see langword="null"/> to leave the user's setup alone.</returns>
    private async Task<string?> ResolveSshCommandAsync(CancellationToken cancellationToken)
    {
        if (_sshCommand is not null)
        {
            return _sshCommand.Length == 0 ? null : _sshCommand;
        }

        var extra = _options.ExtraEnvironment;
        var fromEnvironment = extra is not null && extra.TryGetValue("GIT_SSH_COMMAND", out var overridden)
            ? overridden
            : Environment.GetEnvironmentVariable("GIT_SSH_COMMAND");
        var configured = fromEnvironment;
        var wrapper = Environment.GetEnvironmentVariable("GIT_SSH");
        if (string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                var result = await RunAsync(
                    "config", ["config", "--get", "core.sshCommand"], GitTransport.Ssh, ListTimeout, 4096,
                    cancellationToken, allowFailure: true, applySsh: false);
                configured = result.ExitCode == 0 ? result.Output.Trim() : null;
            }
            catch (GitRemoteException)
            {
                configured = null;
            }
        }

        // A GIT_SSH wrapper program (plink, a custom tool) is the user's choice and is left alone
        // unless they also named an ssh command
        string? composed;
        if (string.IsNullOrWhiteSpace(configured) && !string.IsNullOrWhiteSpace(wrapper))
        {
            composed = null;
        }
        else
        {
            composed = ComposeSshCommand(configured);
        }

        _sshCommand = composed ?? string.Empty;
        return composed;
    }

    /// <summary>
    ///     Runs git with the arguments, bounded by a time limit and an output cap.
    /// </summary>
    /// <param name="operation">A short name of the operation, for diagnostics and messages.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="transport">The only transport allowed.</param>
    /// <param name="timeout">The time limit.</param>
    /// <param name="outputCap">The most standard output (bytes) to keep; more is an error
    ///     unless <paramref name="allowFailure"/>.</param>
    /// <param name="cancellationToken">Cancels the run; the process tree is killed.</param>
    /// <param name="allowFailure">Whether a non-zero exit code is returned instead of thrown.</param>
    /// <param name="applySsh">Whether to resolve and set the ssh command for an ssh remote.</param>
    /// <returns>The outcome of a run that exited with zero (or any code when
    ///     <paramref name="allowFailure"/>).</returns>
    /// <exception cref="GitRemoteException">Thrown when git cannot be started, times out, exits
    ///     with a failure, or prints more than the cap.</exception>
    private async Task<GitRunResult> RunAsync(
        string operation,
        IReadOnlyList<string> arguments,
        GitTransport transport,
        TimeSpan timeout,
        int outputCap,
        CancellationToken cancellationToken,
        bool allowFailure = false,
        bool applySsh = true)
    {
        EnsureWorkFolders();
        var sshCommand = transport == GitTransport.Ssh && applySsh
            ? await ResolveSshCommandAsync(cancellationToken)
            : null;

        var startInfo = CreateStartInfo(arguments, transport, sshCommand);
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug(
            "Running git {Operation} ({Interactive}).", operation, _options.Interactive ? "interactive" : "background");

        using var process = StartProcess(operation, startInfo);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        // No input is ever given: closing it stops git (or ssh) waiting on a terminal
        CloseInput(process);
        var output = ReadBoundedAsync(process.StandardOutput.BaseStream, outputCap, limit.Token);
        var error = ReadBoundedAsync(process.StandardError.BaseStream, MaxErrorBytes, limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            var (text, truncated) = await output;
            var (errorText, _) = await error;
            _logger.LogDebug(
                "git {Operation} exited with code {ExitCode} after {ElapsedMilliseconds}ms.",
                operation, process.ExitCode, stopwatch.ElapsedMilliseconds);

            if (truncated && !allowFailure)
            {
                throw new GitRemoteException(
                    GitFailureKind.OutputTooLarge,
                    $"git {operation} returned more data than AgentControl will read (the repository has too many entries).");
            }

            if (process.ExitCode != 0 && !allowFailure)
            {
                throw CreateFailure(operation, errorText, process.ExitCode);
            }

            return new GitRunResult(process.ExitCode, text);
        }
        catch (OperationCanceledException ex)
        {
            await StopProcessAsync(process, operation, output, error);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw CreateTimeout(operation, timeout, ex);
        }
    }

    /// <summary>
    ///     Starts a git process.
    /// </summary>
    /// <param name="operation">A short name of the operation, for diagnostics.</param>
    /// <param name="startInfo">The start information.</param>
    /// <returns>The running process; the caller disposes it.</returns>
    /// <exception cref="GitRemoteException">Thrown with <see cref="GitFailureKind.GitNotFound"/> when
    ///     the process cannot be started.</exception>
    private Process StartProcess(string operation, ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException("The git process did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogWarning(ex, "Git could not be started for {Operation}.", operation);
            throw new GitRemoteException(
                GitFailureKind.GitNotFound,
                "Git could not be started. Check that Git is installed and, if needed, set the Git executable in Settings.");
        }
    }

    /// <summary>
    ///     Creates the exception for a git command that exited with a failure: classified from git's
    ///     error text, but worded from a fixed sentence so that nothing git printed - which can hold
    ///     the remote address, the host or a local path - reaches a message.
    /// </summary>
    /// <param name="operation">A short name of the operation.</param>
    /// <param name="errorText">What git wrote to standard error; used only to classify.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <returns>The exception to throw.</returns>
    internal static GitRemoteException CreateFailure(string operation, string errorText, int exitCode)
    {
        var kind = Classify(errorText);
        var message = kind switch
        {
            GitFailureKind.AuthenticationRequired =>
                $"git {operation} needs a sign-in that was not available or was refused.",
            GitFailureKind.NotFound =>
                $"git {operation} did not find the repository or the requested tag, or access to it is not allowed.",
            GitFailureKind.Unreachable =>
                $"git {operation} could not reach the remote repository.",
            GitFailureKind.HostKeyUnverified =>
                $"git {operation} stopped because the SSH host key of the server is not trusted yet.",
            GitFailureKind.UnsafeRepository =>
                $"git {operation} refused a repository owned by another user.",
            _ => $"git {operation} failed (exit code {exitCode})."
        };
        return new GitRemoteException(kind, message);
    }

    /// <summary>
    ///     Creates the exception for a git command that exceeded its time limit and was stopped.
    /// </summary>
    /// <param name="operation">A short name of the operation.</param>
    /// <param name="timeout">The limit that was exceeded.</param>
    /// <param name="cause">The cancellation that the limit raised.</param>
    /// <returns>The exception to throw.</returns>
    private GitRemoteException CreateTimeout(string operation, TimeSpan timeout, OperationCanceledException cause)
    {
        _logger.LogWarning(
            cause,
            "git {Operation} did not finish within {TimeoutSeconds} seconds and was stopped.",
            operation, timeout.TotalSeconds);
        return new GitRemoteException(
            GitFailureKind.TimedOut,
            $"git {operation} did not finish within {(int)timeout.TotalSeconds} seconds and was stopped.");
    }

    /// <summary>
    ///     Creates the start information: no window, no shell, every stream redirected, a neutral
    ///     working folder and the prepared environment.
    /// </summary>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="transport">The only transport allowed.</param>
    /// <param name="sshCommand">The ssh command to set, or <see langword="null"/>.</param>
    /// <returns>The start information.</returns>
    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments, GitTransport transport, string? sshCommand)
    {
        var startInfo = new ProcessStartInfo(_options.ResolveGitExecutable())
        {
            WorkingDirectory = _neutralDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8WithoutBom,
            StandardErrorEncoding = Utf8WithoutBom,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ApplyEnvironment(startInfo.Environment, transport, sshCommand);
        return startInfo;
    }

    /// <summary>
    ///     Closes the standard input of a process, ignoring a process that has already gone.
    /// </summary>
    /// <param name="process">The process.</param>
    private static void CloseInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Git exited before the input was closed; its exit code says what happened
        }
    }

    /// <summary>
    ///     Reads a stream to its end, keeping at most a number of bytes and discarding the rest so
    ///     the writer never blocks on a full pipe.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="cap">The most bytes to keep; the cap counts bytes, not decoded characters, so
    ///     multi-byte text cannot exceed it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The kept bytes decoded as UTF-8, and whether more was written than the cap.</returns>
    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        Stream stream, int cap, CancellationToken cancellationToken)
    {
        var kept = new MemoryStream();
        var buffer = new byte[8192];
        var truncated = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return (Utf8WithoutBom.GetString(kept.GetBuffer(), 0, (int)kept.Length), truncated);
            }

            var room = cap - (int)kept.Length;
            if (read <= room)
            {
                await kept.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            else
            {
                await kept.WriteAsync(buffer.AsMemory(0, Math.Max(room, 0)), cancellationToken);
                truncated = true;
            }
        }
    }

    /// <summary>
    ///     Kills a process and everything it started and confirms that it ended, so a timeout or a
    ///     cancellation never leaves a git (or a helper it started) running unnoticed.
    /// </summary>
    /// <param name="process">The process to stop.</param>
    /// <param name="operation">A short name of the operation, for the message.</param>
    /// <param name="output">The pending read of its standard output, or <see langword="null"/>.</param>
    /// <param name="error">The pending read of its standard error.</param>
    /// <param name="others">Any other pending work that ends with the process.</param>
    /// <returns>A task that completes when the process is known to have ended.</returns>
    /// <exception cref="GitRemoteException">Thrown with <see cref="GitFailureKind.ProcessNotStopped"/>
    ///     when the process could not be killed or did not end within the stop timeout: the failure
    ///     is surfaced, never swallowed, because a process may still be running.</exception>
    private async Task StopProcessAsync(
        Process process, string operation, Task? output, Task error, params Task[] others)
    {
        Exception? killFailure = null;
        try
        {
            if (!process.HasExited)
            {
                (_options.KillProcessTree ?? KillProcessTree)(process);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // The process may have ended between the check and the kill; whether it really ended
            // is decided below from the process itself, not from this exception
            killFailure = ex;
        }

        // The reads end when their pipes close or their token is cancelled; neither outcome says
        // whether the process ended, so they are only waited for, within a limit
        var stopTimeout = _options.StopTimeout ?? DefaultStopTimeout;
        var pending = new List<Task>(others) { error };
        if (output is not null)
        {
            pending.Add(output);
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(stopTimeout);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException)
        {
            // The reads were cancelled with the run or the pipes did not close in time
        }

        // The process itself must be gone: this is the confirmation
        var ended = false;
        try
        {
            await process.WaitForExitAsync().WaitAsync(stopTimeout);
            ended = true;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Not ended in time: reported below
        }
        catch (InvalidOperationException)
        {
            // No process is associated any more, so there is nothing left to wait for
            ended = process.HasExited;
        }

        if (!ended || killFailure is AggregateException)
        {
            _logger.LogError(killFailure, "git {Operation} could not be confirmed to have stopped.", operation);
            throw new GitRemoteException(
                GitFailureKind.ProcessNotStopped,
                $"git {operation} was stopped but could not be confirmed to have ended, so a git process may still be running.");
        }
    }

    /// <summary>
    ///     Kills a process and all its descendants.
    /// </summary>
    /// <param name="process">The process.</param>
    private static void KillProcessTree(Process process) => process.Kill(entireProcessTree: true);

    /// <summary>
    ///     Reads a pipe through a small buffer so a header line and then a counted run of bytes can
    ///     be taken from the output of <c>git cat-file --batch</c> without a synchronous read.
    /// </summary>
    /// <param name="stream">The pipe.</param>
    private sealed class BatchReader(Stream stream)
    {
        /// <summary>
        ///     The read buffer.
        /// </summary>
        private readonly byte[] _buffer = new byte[64 * 1024];

        /// <summary>
        ///     The next unread byte in the buffer.
        /// </summary>
        private int _position;

        /// <summary>
        ///     The number of valid bytes in the buffer.
        /// </summary>
        private int _length;

        /// <summary>
        ///     Makes sure at least one unread byte is buffered.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns><see langword="false"/> at the end of the stream.</returns>
        private async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
        {
            if (_position < _length)
            {
                return true;
            }

            _position = 0;
            _length = await stream.ReadAsync(_buffer.AsMemory(), cancellationToken);
            return _length > 0;
        }

        /// <summary>
        ///     Reads one byte.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The byte, or -1 at the end of the stream.</returns>
        public async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken) =>
            await FillAsync(cancellationToken) ? _buffer[_position++] : -1;

        /// <summary>
        ///     Reads a line of ASCII text up to a line feed.
        /// </summary>
        /// <param name="maxBytes">The longest line accepted.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The line without its line feed, or <see langword="null"/> when the stream ended
        ///     before any byte.</returns>
        /// <exception cref="GitRemoteException">Thrown when the line is longer than the limit or
        ///     the stream ends inside it.</exception>
        public async Task<string?> ReadLineAsync(int maxBytes, CancellationToken cancellationToken)
        {
            var line = new StringBuilder();
            while (true)
            {
                var next = await ReadByteAsync(cancellationToken);
                if (next == -1)
                {
                    return line.Length == 0
                        ? null
                        : throw new GitRemoteException(GitFailureKind.Other, "git cat-file returned unexpected data.");
                }

                if (next == '\n')
                {
                    return line.ToString();
                }

                if (line.Length >= maxBytes)
                {
                    throw new GitRemoteException(GitFailureKind.Other, "git cat-file returned unexpected data.");
                }

                line.Append((char)next);
            }
        }

        /// <summary>
        ///     Copies an exact number of bytes to a destination.
        /// </summary>
        /// <param name="destination">Where the bytes go.</param>
        /// <param name="count">How many bytes to copy.</param>
        /// <param name="cancellationToken">Cancels the copy.</param>
        /// <returns><see langword="false"/> when the stream ended before all bytes were read.</returns>
        public async Task<bool> CopyAsync(Stream destination, long count, CancellationToken cancellationToken)
        {
            while (count > 0)
            {
                if (!await FillAsync(cancellationToken))
                {
                    return false;
                }

                var chunk = (int)Math.Min(_length - _position, count);
                await destination.WriteAsync(_buffer.AsMemory(_position, chunk), cancellationToken);
                _position += chunk;
                count -= chunk;
            }

            return true;
        }
    }

    /// <summary>
    ///     The outcome of one git invocation that ran to completion.
    /// </summary>
    /// <param name="ExitCode">The process exit code.</param>
    /// <param name="Output">What git wrote to standard output (up to the cap).</param>
    private readonly record struct GitRunResult(int ExitCode, string Output);
}
