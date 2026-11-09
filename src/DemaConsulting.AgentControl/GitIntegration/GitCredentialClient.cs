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
using System.Text;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.GitIntegration;

/// <summary>
///     Keeps, finds and removes package-feed sign-ins in the user's own Git credential helper by
///     running <c>git credential fill|approve|reject</c>, so AgentControl never stores a secret
///     itself.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why Git's credential helper.</b> Every developer machine that has Git already has a
///     credential helper configured by the platform's installer (Git Credential Manager on
///     Windows and macOS, a keychain or libsecret elsewhere), which stores secrets in the
///     operating system's protected store. Asking it to keep a feed's sign-in therefore needs no
///     storage code, and no secret, in AgentControl. The feed is described to the helper by its
///     origin (<c>protocol=https</c>, <c>host=host[:port]</c>), the same way a Git remote is.
///     </para>
///     <para>
///     <b>Never interactive.</b> AgentControl's own sign-in dialog is the only user interface, so
///     every invocation runs with <c>GIT_TERMINAL_PROMPT=0</c>, <c>GCM_INTERACTIVE=never</c>, no
///     askpass program and the standard input closed after the request, so neither Git nor the
///     helper can ever open a prompt or wait for input. Git Credential Manager would otherwise
///     probe the host over the network to detect which hosting provider it is (which tells the
///     server about the lookup, and is slow or failing for unreachable hosts), so the provider is
///     fixed to <c>generic</c> for the feed's origin only, with a <c>-c</c> option scoped to that
///     origin - never in the user's configuration. The invocation runs from a neutral folder
///     (the user's profile) rather than the current directory, so the configuration of a
///     repository the application happens to be inside cannot add a credential helper.
///     </para>
///     <para>
///     <b>Bounded.</b> A lookup normally takes under a second, but a miss can take a few seconds
///     and a stuck helper could take forever, so each invocation has a time limit (ten seconds by
///     default); on a timeout the whole process tree is killed and the call reports "nothing"
///     rather than failing the operation that asked. Canceling the token kills the process tree
///     too.
///     </para>
///     <para>
///     <b>No secret reaches a log.</b> The request (which carries a password for
///     <c>approve</c>/<c>reject</c>) is written to the process's standard input and never logged;
///     the response (which carries a password for <c>fill</c>) is parsed and never logged; the
///     standard error stream is drained and discarded because a helper may echo what it was
///     given; arguments never carry a secret. Diagnostics record only the operation, the feed
///     origin, the exit code and the elapsed time. A secret read from a response is registered
///     with <see cref="SecretRedactor"/> while it is being handled. The git executable is the one
///     the user configured (the same path <see cref="GitClient"/> uses, including the
///     <c>--git-path</c> override), resolved on every call so a changed setting applies at once.
///     Thread-safe: it keeps no state between calls.
///     </para>
/// </remarks>
internal sealed class GitCredentialClient : IRememberedCredentialStore
{
    /// <summary>
    ///     The longest one git invocation may take before it is stopped: ten seconds. A lookup
    ///     that finds a stored credential takes under a second; this leaves room for a slow
    ///     keychain without letting a stuck helper hold an operation indefinitely.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How long to wait for a killed process to be reaped and its pipes to close.
    /// </summary>
    private static readonly TimeSpan ReapTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Standard input must not start with a byte-order mark, or git would read it as part of
    ///     the first key.
    /// </summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    ///     Resolves the git executable path (or command name) at the moment of each call.
    /// </summary>
    private readonly Func<string> _resolveGitExecutablePath;

    /// <summary>
    ///     Receives diagnostics that never contain a secret.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     The longest one git invocation may take.
    /// </summary>
    private readonly TimeSpan _timeout;

    /// <summary>
    ///     Extra <c>-c</c> configuration (<c>name=value</c> pairs) passed to every invocation, for
    ///     tests that must isolate the credential store.
    /// </summary>
    private readonly IReadOnlyList<string> _extraConfiguration;

    /// <summary>
    ///     The folder git runs in.
    /// </summary>
    private readonly string _workingDirectory;

    /// <summary>
    ///     Initializes a client that resolves the git executable on every call.
    /// </summary>
    /// <param name="resolveGitExecutablePath">Returns the path or command name of the git
    ///     executable to run (the configured path, a <c>--git-path</c> override, or
    ///     <c>git</c>), evaluated for every invocation.</param>
    /// <param name="logger">Receives secret-free diagnostics, or <see langword="null"/> for the
    ///     application logger.</param>
    /// <param name="timeout">The longest one invocation may take, or <see langword="null"/> for
    ///     <see cref="DefaultTimeout"/>.</param>
    /// <param name="extraConfiguration">Extra <c>name=value</c> configuration to pass to git with
    ///     <c>-c</c> on every invocation, or <see langword="null"/> for none. Supplied so tests can
    ///     replace the credential helper with a throw-away one (for example
    ///     <c>credential.helper=</c> followed by <c>credential.helper=store --file=...</c>) and
    ///     never touch the user's real credential store.</param>
    /// <param name="workingDirectory">The folder git runs in, or <see langword="null"/> for the
    ///     user's profile folder (the temporary folder when there is none).</param>
    /// <exception cref="ArgumentNullException">Thrown when
    ///     <paramref name="resolveGitExecutablePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is not
    ///     positive.</exception>
    public GitCredentialClient(
        Func<string> resolveGitExecutablePath,
        ILogger? logger = null,
        TimeSpan? timeout = null,
        IReadOnlyList<string>? extraConfiguration = null,
        string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(resolveGitExecutablePath);
        if (timeout is { } limit && limit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The time limit must be positive.");
        }

        _resolveGitExecutablePath = resolveGitExecutablePath;

        // Everything logged goes through the redacting wrapper as well, so a secret that is
        // registered while an operation handles it can never be written out by accident
        _logger = new SecretRedactingLogger(logger ?? AppLogging.Factory.CreateLogger<GitCredentialClient>());
        _timeout = timeout ?? DefaultTimeout;
        _extraConfiguration = extraConfiguration ?? [];
        _workingDirectory = workingDirectory ?? GetNeutralWorkingDirectory();
    }

    /// <summary>
    ///     Initializes a client that runs a fixed git executable.
    /// </summary>
    /// <param name="gitExecutablePath">The path or command name of the git executable.</param>
    /// <param name="logger">Receives secret-free diagnostics, or <see langword="null"/>.</param>
    /// <param name="timeout">The longest one invocation may take, or <see langword="null"/> for
    ///     <see cref="DefaultTimeout"/>.</param>
    /// <param name="extraConfiguration">Extra <c>name=value</c> configuration passed with
    ///     <c>-c</c>, or <see langword="null"/>.</param>
    /// <param name="workingDirectory">The folder git runs in, or <see langword="null"/> for the
    ///     user's profile folder.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="gitExecutablePath"/> is
    ///     null, empty or whitespace.</exception>
    public GitCredentialClient(
        string gitExecutablePath,
        ILogger? logger = null,
        TimeSpan? timeout = null,
        IReadOnlyList<string>? extraConfiguration = null,
        string? workingDirectory = null)
        : this(RequirePath(gitExecutablePath), logger, timeout, extraConfiguration, workingDirectory)
    {
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Asks git which credential helper applies to the feed's origin
    ///     (<c>git config --get-urlmatch credential.helper</c>), which honors configuration scoped
    ///     to the origin and a helper list that was reset by an empty entry. No helper, a helper
    ///     list reset to nothing, a missing git executable and a failing git all mean "not
    ///     available".
    /// </remarks>
    public async Task<bool> IsAvailableAsync(FeedOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var arguments = BuildArguments(origin, withProviderOverride: false, "config", "--get-urlmatch", "credential.helper", origin + "/");
        var result = await RunAsync("config", origin, arguments, standardInput: null, cancellationToken);
        return result.Completed && result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Runs <c>git credential fill</c> for the origin. A missing credential (git exits
    ///     non-zero because it may not prompt), a response without both a user name and a
    ///     password, a timeout and a failure to run git all yield <see langword="null"/>.
    /// </remarks>
    public async Task<NuGetCredential?> FillAsync(FeedOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var request = BuildRequest(origin, credential: null);
        if (request is null)
        {
            _logger.LogWarning("No saved credential was looked up for {FeedOrigin} because the address cannot be sent safely.", origin);
            return null;
        }

        var arguments = BuildArguments(origin, withProviderOverride: true, "credential", "fill");
        var result = await RunAsync("fill", origin, arguments, request, cancellationToken);
        if (!result.Completed || result.ExitCode != 0)
        {
            return null;
        }

        var credential = ParseCredential(result.Output);

        // The secret just read is registered for redaction while this method handles it; what
        // the caller keeps is registered by whoever keeps it
        using var lease = credential is null ? null : SecretRedactor.Lease(credential);
        _logger.LogDebug(
            "git credential fill for {FeedOrigin} {Outcome}.", origin, credential is null ? "found nothing" : "found a credential");
        return credential;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Runs <c>git credential approve</c>, which asks the helper to store the credential. A
    ///     credential (or origin) with a value that the <c>key=value</c> protocol cannot carry
    ///     safely - a line feed, carriage return or NUL - is refused and
    ///     nothing is sent (see <see cref="IsSafeValue"/>).
    /// </remarks>
    public async Task<bool> ApproveAsync(FeedOrigin origin, NuGetCredential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(credential);

        var request = BuildRequest(origin, credential);
        if (request is null)
        {
            _logger.LogWarning("A credential for {FeedOrigin} was not saved because it contains a character the credential protocol cannot carry safely.", origin);
            return false;
        }

        using var lease = SecretRedactor.Lease(credential);
        var result = await RunAsync(
            "approve", origin, BuildArguments(origin, withProviderOverride: true, "credential", "approve"),
            request, cancellationToken);
        return result.Completed && result.ExitCode == 0;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Runs <c>git credential reject</c>, which asks the helper to erase the credential (or,
    ///     without one, whatever it stores for the origin). A value the protocol cannot carry
    ///     safely is refused as for approve; a credential that could never have been saved
    ///     cannot be in the helper either.
    /// </remarks>
    public async Task<bool> RejectAsync(FeedOrigin origin, NuGetCredential? credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var request = BuildRequest(origin, credential);
        if (request is null)
        {
            _logger.LogWarning("A credential for {FeedOrigin} was not removed because it contains a character the credential protocol cannot carry safely.", origin);
            return false;
        }

        using var lease = credential is null ? null : SecretRedactor.Lease(credential);
        var result = await RunAsync(
            "reject", origin, BuildArguments(origin, withProviderOverride: true, "credential", "reject"),
            request, cancellationToken);
        return result.Completed && result.ExitCode == 0;
    }

    /// <summary>
    ///     Parses the <c>key=value</c> lines of a <c>git credential fill</c> response into a
    ///     credential.
    /// </summary>
    /// <param name="output">The response text.</param>
    /// <returns>The credential, or <see langword="null"/> when the response lacks a user name or
    ///     a password.</returns>
    internal static NuGetCredential? ParseCredential(string output)
    {
        string? userName = null;
        string? password = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            // The value is everything after the first '=': a password may contain '='
            var key = line[..separator];
            var value = line[(separator + 1)..];
            switch (key)
            {
                case "username":
                    userName = value;
                    break;
                case "password":
                    password = value;
                    break;
            }
        }

        return string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password)
            ? null
            : new NuGetCredential(userName, password);
    }

    /// <summary>
    ///     Builds the request that goes to git's standard input: the feed's protocol and host and,
    ///     when given, the credential, ended by the blank line that terminates a request. A request
    ///     is never built when any value could be misread by git's <c>key=value</c> line protocol.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="credential">The credential to describe, or <see langword="null"/> for a
    ///     lookup.</param>
    /// <returns>The request text, or <see langword="null"/> when a value contains a character
    ///     the protocol cannot carry (see <see cref="IsSafeValue"/>); a malformed
    ///     request is never produced.</returns>
    internal static string? BuildRequest(FeedOrigin origin, NuGetCredential? credential)
    {
        if (!IsSafeValue(origin.Scheme) || !IsSafeValue(origin.HostAndPort)
            || (credential is not null && (!IsSafeValue(credential.UserName) || !IsSafeValue(credential.Secret))))
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("protocol=").Append(origin.Scheme).Append('\n');
        builder.Append("host=").Append(origin.HostAndPort).Append('\n');
        if (credential is not null)
        {
            builder.Append("username=").Append(credential.UserName).Append('\n');
            builder.Append("password=").Append(credential.Secret).Append('\n');
        }

        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>
    ///     Determines whether a value can be written into a request without any chance of being
    ///     misread. The protocol is made of lines of <c>key=value</c> ended by a blank line; git
    ///     splits each line at the first <c>=</c>, so a value may contain <c>=</c> (Base64 padding
    ///     in a token, for example) and any other character except a line feed or NUL. A value
    ///     containing a line feed (injecting further keys or ending the request early), a NUL or a
    ///     carriage return (which helpers commonly trim or mishandle) is refused rather than
    ///     escaped: the protocol has no escape mechanism. Keys are fixed by this class and never
    ///     come from user input.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the value contains none of those characters.</returns>
    internal static bool IsSafeValue(string value) =>
        value.IndexOfAny(['\n', '\r', '\0']) < 0;

    /// <summary>
    ///     Requires a non-blank executable path and returns a resolver that always yields it.
    /// </summary>
    /// <param name="gitExecutablePath">The executable path.</param>
    /// <returns>The resolver.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is null, empty or whitespace.</exception>
    private static Func<string> RequirePath(string gitExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutablePath);
        return () => gitExecutablePath;
    }

    /// <summary>
    ///     Gets the folder git runs in when none is given: one that is not part of any repository
    ///     the application might be inside.
    /// </summary>
    /// <returns>The user's profile folder, or the temporary folder when there is none.</returns>
    private static string GetNeutralWorkingDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrEmpty(profile) && Directory.Exists(profile) ? profile : Path.GetTempPath();
    }

    /// <summary>
    ///     Builds the argument list for one git invocation.
    /// </summary>
    /// <param name="origin">The feed origin.</param>
    /// <param name="withProviderOverride">Whether to fix Git Credential Manager's provider to
    ///     <c>generic</c> for this origin, so it does not probe the host over the network.</param>
    /// <param name="command">The git command and its arguments.</param>
    /// <returns>The arguments, configuration first.</returns>
    private List<string> BuildArguments(FeedOrigin origin, bool withProviderOverride, params string[] command)
    {
        // No askpass program: git would otherwise run one (which can show a window) before
        // giving up on a missing credential
        var arguments = new List<string> { "-c", "core.askPass=" };
        if (withProviderOverride)
        {
            // Scoped to this feed's origin only; never written to the user's configuration
            arguments.Add("-c");
            arguments.Add($"credential.{origin}.provider=generic");
        }

        foreach (var setting in _extraConfiguration)
        {
            arguments.Add("-c");
            arguments.Add(setting);
        }

        arguments.AddRange(command);
        return arguments;
    }

    /// <summary>
    ///     Runs git with the given arguments and standard input, bounded by the time limit.
    /// </summary>
    /// <param name="operation">A short name of the operation, for diagnostics.</param>
    /// <param name="origin">The feed origin, for diagnostics.</param>
    /// <param name="arguments">The git arguments (none carries a secret).</param>
    /// <param name="standardInput">The text to write to standard input, or <see langword="null"/>
    ///     for none.</param>
    /// <param name="cancellationToken">Cancels the run; the process tree is then killed.</param>
    /// <returns>The outcome; <see cref="GitRunResult.Completed"/> is <see langword="false"/> when
    ///     git could not be started or the time limit was exceeded.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is cancelled.</exception>
    private async Task<GitRunResult> RunAsync(
        string operation,
        FeedOrigin origin,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(arguments);
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("Running git credential operation {Operation} for {FeedOrigin}.", operation, origin);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("The git process did not start.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // Not having git (or being unable to run it) is "no credential store", not a failure
            _logger.LogWarning(
                ex, "Git could not be started for credential operation {Operation}.", operation);
            return GitRunResult.NotRun;
        }

        using (process)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(_timeout);

            var output = process.StandardOutput.ReadToEndAsync(limit.Token);

            // Standard error is drained so the process cannot block on a full pipe, and discarded:
            // a helper may echo what it was given
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await WriteInputAsync(process, standardInput, limit.Token);
                await process.WaitForExitAsync(limit.Token);
                var text = await output;
                await error;

                _logger.LogDebug(
                    "git credential operation {Operation} for {FeedOrigin} exited with code {ExitCode} after {ElapsedMilliseconds}ms.",
                    operation, origin, process.ExitCode, stopwatch.ElapsedMilliseconds);
                return new GitRunResult(true, process.ExitCode, text);
            }
            catch (OperationCanceledException ex)
            {
                await KillAsync(process, output, error);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _logger.LogWarning(
                    ex,
                    "git credential operation {Operation} for {FeedOrigin} did not finish within {TimeoutSeconds} seconds and was stopped.",
                    operation, origin, _timeout.TotalSeconds);
                return GitRunResult.NotRun;
            }
        }
    }

    /// <summary>
    ///     Creates the process start information: no window, every stream redirected, and an
    ///     environment that makes every kind of prompt impossible.
    /// </summary>
    /// <param name="arguments">The git arguments.</param>
    /// <returns>The start information.</returns>
    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(_resolveGitExecutablePath())
        {
            WorkingDirectory = _workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8WithoutBom,
            StandardOutputEncoding = Utf8WithoutBom,
            StandardErrorEncoding = Utf8WithoutBom,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Neither git nor the credential helper may ever prompt: the application's own dialog is
        // the only user interface. An empty askpass variable disables the programs git would
        // otherwise run to ask
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["GIT_ASKPASS"] = string.Empty;
        startInfo.Environment["SSH_ASKPASS"] = string.Empty;
        return startInfo;
    }

    /// <summary>
    ///     Writes the request to the process and closes its standard input, so git sees the end of
    ///     the request.
    /// </summary>
    /// <param name="process">The running process.</param>
    /// <param name="standardInput">The request, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the input has been written and closed.</returns>
    private static async Task WriteInputAsync(Process process, string? standardInput, CancellationToken cancellationToken)
    {
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Git exited without reading its input (for example it failed to start the helper); its
            // exit code says what happened
        }
    }

    /// <summary>
    ///     Kills a process and everything it started, and lets its pipes close.
    /// </summary>
    /// <param name="process">The process to stop.</param>
    /// <param name="output">The pending read of its standard output.</param>
    /// <param name="error">The pending read of its standard error.</param>
    /// <returns>A task that completes when the process has been stopped or the wait for it
    ///     expired.</returns>
    private static async Task KillAsync(Process process, Task output, Task error)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process ended on its own between the check and the kill
        }

        try
        {
            await Task.WhenAll(output, error).WaitAsync(ReapTimeout);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException)
        {
            // The reads were cancelled with the run or the pipes did not close in time; nothing
            // they held is needed
        }
    }

    /// <summary>
    ///     The outcome of one git invocation.
    /// </summary>
    /// <param name="Completed"><see langword="true"/> when git ran to completion (whatever its
    ///     exit code); <see langword="false"/> when it could not be started or was stopped for
    ///     exceeding the time limit.</param>
    /// <param name="ExitCode">The process exit code, when <paramref name="Completed"/>.</param>
    /// <param name="Output">What git wrote to standard output, when <paramref name="Completed"/>.
    ///     May hold a secret: it is parsed by the caller and never logged.</param>
    private readonly record struct GitRunResult(bool Completed, int ExitCode, string Output)
    {
        /// <summary>
        ///     Gets the outcome for an invocation that did not run to completion.
        /// </summary>
        public static GitRunResult NotRun { get; } = new(false, -1, string.Empty);
    }
}
