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

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DemaConsulting.AgentControl.Logging;
using DemaConsulting.AgentControl.PackageCredentials;
using DemaConsulting.AgentControl.RepoSync;
using DemaConsulting.NuGet.Caching;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentControl.AgentPackageManagement;

/// <summary>
///     <see cref="IPackageSourceProvider"/> over NuGet packages held in a folder feed or on a NuGet
///     V3 server, using the DemaConsulting.NuGet.Caching library to list versions and to unpack
///     the chosen package into a private package cache.
/// </summary>
/// <remarks>
///     <para>
///     A NuGet package carries its agent content under its <c>content</c> folder
///     (<c>content/.github/...</c>, <c>content/release-notes.md</c>, <c>content/AGENTS.md</c>),
///     which <see cref="DirectoryPackageContentReader"/> exposes exactly as a zip's root. A
///     source location is either a folder or UNC path, or the <c>https</c> address of a V3
///     service index ending in <c>index.json</c> (<c>http</c> only when the source's
///     <see cref="PackageSourceDefinition.AllowInsecureHttp"/> is set, because a credential would
///     cross the network unprotected). Listing versions never downloads package content.
///     </para>
///     <para>
///     <b>Isolation.</b> Every call passes an explicit AgentControl-owned root folder (one per
///     source, under the per-user local application data folder unless a base folder is supplied)
///     to the NuGet library and never the target repository or the current directory, so no
///     <c>nuget.config</c> belonging to a repo is discovered and nothing about AgentControl's
///     sources is ever written to a repo. That per-source root holds a generated
///     <c>nuget.config</c> that only redirects NuGet's global packages folder to a
///     <c>packages</c> sub-folder of the root, so each source has a private package cache: a
///     package that another source (or the developer's own NuGet cache) holds under the same
///     ID and version can never be served as this source's package. Because the
///     <c>NUGET_PACKAGES</c> environment variable overrides that setting, acquisition fails
///     closed (with an explanatory message) rather than accept a package from a cache outside the
///     per-source root. The unpacked package is only ever read, never modified.
///     </para>
///     <para>
///     <b>Credentials (server sources).</b> This provider never registers a NuGet credential
///     service (the process-wide service is registered by the NuGet library itself, which applies
///     the per-call callback and timeout it is given) and never persists a credential. Credentials
///     are obtained in this order. First, whatever NuGet already knows: a source in the user's own
///     <c>nuget.config</c> whose address equals this source's address (compared ignoring case and
///     a trailing slash) contributes its <c>packageSourceCredentials</c> up front, and installed
///     NuGet credential-provider plug-ins (for example Azure Artifacts) run non-interactively when
///     the server challenges. Second, a credential the user already typed this session, or one
///     the user saved earlier with their own Git credential helper (looked up once per feed per
///     session, without any dialog). Third,
///     when a prompt is available, a dialog asks the user, at most once per operation. To keep
///     that order the first attempt of an operation is silent (no dialog); only if
///     the server then refuses does a second attempt ask the user, so a plug-in that can sign in
///     by itself never causes a dialog. A credential the user typed is remembered for the session
///     only after the operation succeeded, and forgotten when a server rejects it. A credential the
///     user asked to remember is handed to their own Git credential helper at that moment (and a
///     saved one the server rejects is removed from the helper), so AgentControl itself never
///     stores a secret (see <see cref="IRememberedCredentialStore"/>). Credentials
///     are offered only to the feed's own origin (see <see cref="PackageCredentialCallback"/>).
///     </para>
///     <para>
///     <b>Signatures.</b> When the source requires signed packages
///     (<see cref="PackageSourceDefinition.RequireSignedPackages"/>) the library is told to
///     require a valid signature, on a fresh download and again whenever the package is already in
///     the private cache. Without a pinned signer any validly signed package that chains to a
///     trusted root is accepted; with one
///     (<see cref="PackageSourceDefinition.SignerFingerprint"/>) the generated per-source
///     <c>nuget.config</c> holds a <c>trustedSigners</c> allow-list with exactly that author
///     certificate, so only packages signed by that publisher are accepted. The list starts with
///     <c>&lt;clear /&gt;</c>, so trusted signers in the user's own NuGet configuration can neither
///     widen nor narrow what the source accepts. A signature failure is reported as
///     <see cref="PackageSourceFailureKind.SignatureRejected"/> with a secret-free message, and a
///     source that requires signatures on a platform where verification is not supported
///     (<see cref="SignatureVerificationSupport"/>) refuses to list or acquire packages: it fails
///     closed instead of silently skipping verification. Zip packages carry no signatures and are
///     not affected.
///     </para>
///     <para>
///     Failures that the user can act on are reported as
///     <see cref="PackageSourceAccessException"/> with a secret-free message.
///     Thread-safe: shared state is limited to a one-time, locked preparation of the per-source
///     root.
///     </para>
/// </remarks>
internal sealed class NuGetSourceProvider : IPackageSourceProvider
{
    /// <summary>
    ///     The longest time one credential exchange (a prompt, or a credential plug-in) may take
    ///     before the operation is stopped: two minutes. A person has to read the dialog, find a
    ///     token and type it, which takes far longer than the seconds a silent exchange needs, yet
    ///     an abandoned dialog must not hold the operation forever.
    /// </summary>
    public static readonly TimeSpan DefaultAuthenticationTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     File name of the generated per-source NuGet configuration, in the casing every
    ///     supported platform's NuGet configuration discovery finds.
    /// </summary>
    private const string NuGetConfigFileName = "nuget.config";

    /// <summary>
    ///     Name of the per-source sub-folder used as NuGet's global packages folder.
    /// </summary>
    private const string PackagesFolderName = "packages";

    /// <summary>
    ///     The name of the <c>trustedSigners</c> entry generated for a pinned publisher.
    /// </summary>
    private const string PinnedSignerName = "AgentControl pinned signer";

    /// <summary>
    ///     Number of hexadecimal characters of the location hash used to disambiguate per-source
    ///     root folder names.
    /// </summary>
    private const int KeyHashLength = 12;

    /// <summary>
    ///     The package IDs the user configured for this source, trimmed and de-duplicated.
    /// </summary>
    private readonly IReadOnlyList<string> _packageIds;

    /// <summary>
    ///     The feed location: a folder path or a feed URL.
    /// </summary>
    private readonly string _location;

    /// <summary>
    ///     The origin of the feed for a server source, or <see langword="null"/> for a folder.
    /// </summary>
    private readonly FeedOrigin? _feed;

    /// <summary>
    ///     The shared credential facilities, or <see langword="null"/> when the provider has none
    ///     (a server source then only uses what NuGet itself knows).
    /// </summary>
    private readonly PackageCredentialContext? _credentials;

    /// <summary>
    ///     The longest time one credential exchange may take.
    /// </summary>
    private readonly TimeSpan _authenticationTimeout;

    /// <summary>
    ///     The logger given to the NuGet library, wrapped so that secrets are removed from every
    ///     message.
    /// </summary>
    private readonly ILogger _nugetLogger;

    /// <summary>
    ///     The AgentControl-owned folder that NuGet configuration discovery starts from and that
    ///     holds this source's private package cache.
    /// </summary>
    private readonly string _root;

    /// <summary>
    ///     Whether packages from this source must be validly signed.
    /// </summary>
    private readonly bool _requireSigned;

    /// <summary>
    ///     The normalized SHA-256 fingerprint of the trusted publisher's certificate, or
    ///     <see langword="null"/> when any validly signed package is acceptable.
    /// </summary>
    private readonly string? _signerFingerprint;

    /// <summary>
    ///     Whether the platform can verify signatures; a source that requires them refuses to work
    ///     when it cannot.
    /// </summary>
    private readonly bool _signaturesSupported;

    /// <summary>
    ///     Serializes the one-time preparation of <see cref="_root"/>.
    /// </summary>
    private readonly object _prepareLock = new();

    /// <summary>
    ///     Whether <see cref="_root"/> and its generated configuration have been prepared.
    /// </summary>
    private bool _prepared;

    /// <summary>
    ///     Initializes a provider for one NuGet-package source (a folder or a server).
    /// </summary>
    /// <param name="definition">The source definition; its
    ///     <see cref="PackageSourceDefinition.Type"/> must be <see cref="PackageSourceType.NuGet"/>
    ///     and its location a rooted folder or UNC path, or a valid server feed address.</param>
    /// <param name="rootBase">The absolute folder under which per-source roots are created, or
    ///     <see langword="null"/> for <c>{LocalApplicationData}/AgentControl/nuget</c>. Never the
    ///     target repository, and never relative to the current directory.</param>
    /// <param name="credentials">The credential facilities for a server source, or
    ///     <see langword="null"/> for none.</param>
    /// <param name="logger">The logger for diagnostics, or <see langword="null"/> for the
    ///     application logger. Secrets are removed from everything it receives.</param>
    /// <param name="authenticationTimeout">The longest a credential exchange may take, or
    ///     <see langword="null"/> for <see cref="DefaultAuthenticationTimeout"/>.</param>
    /// <param name="signatureVerificationSupported">Whether the platform can verify package
    ///     signatures, or <see langword="null"/> to ask <see cref="SignatureVerificationSupport"/>.
    ///     Supplied so tests can exercise the fail-closed behavior of a platform that cannot.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the definition is not a NuGet source, has
    ///     a blank alias or location, or <paramref name="rootBase"/> is not an absolute
    ///     path.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a server location is not an
    ///     acceptable feed address (see
    ///     <see cref="PackageSourceDefinition.GetServerLocationProblem"/>), a folder location
    ///     is not an absolute path, or the signer fingerprint is not a valid SHA-256 fingerprint
    ///     (see <see cref="PackageSourceDefinition.GetSignaturePolicyProblem"/>).</exception>
    public NuGetSourceProvider(
        PackageSourceDefinition definition,
        string? rootBase = null,
        PackageCredentialContext? credentials = null,
        ILogger? logger = null,
        TimeSpan? authenticationTimeout = null,
        bool? signatureVerificationSupported = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Location);
        if (definition.Type != PackageSourceType.NuGet)
        {
            throw new ArgumentException(
                $"Source '{definition.Alias}' is not a NuGet source.", nameof(definition));
        }

        // A server needs its address to be a V3 index on https (or http by explicit opt-in) with
        // no embedded credential; a folder needs a fully qualified path
        var location = definition.Location.Trim();
        if (definition.LocationKind == PackageSourceLocationKind.Server)
        {
            var problem = definition.GetServerLocationProblem();
            if (problem is not null)
            {
                throw new InvalidOperationException($"NuGet source '{definition.Alias}': {problem}");
            }

            _feed = FeedOrigin.Create(new Uri(location));
        }
        else if (!Path.IsPathFullyQualified(location))
        {
            throw new InvalidOperationException(
                $"The location of NuGet source '{definition.Alias}' must be an absolute folder path.");
        }

        // A signer fingerprint that cannot be read stops the source rather than being ignored:
        // ignoring it would silently widen trust from one publisher to any signer
        var signatureProblem = definition.GetSignaturePolicyProblem();
        if (signatureProblem is not null)
        {
            throw new InvalidOperationException(signatureProblem);
        }

        _requireSigned = definition.RequireSignedPackages;
        _signerFingerprint = _requireSigned &&
                             PackageSourceDefinition.TryNormalizeFingerprint(definition.SignerFingerprint, out var fingerprint)
            ? fingerprint
            : null;
        _signaturesSupported = signatureVerificationSupported ?? SignatureVerificationSupport.IsSupported;

        Alias = definition.Alias;
        _location = location;
        _credentials = credentials;
        _authenticationTimeout = authenticationTimeout ?? DefaultAuthenticationTimeout;
        _nugetLogger = new SecretRedactingLogger(logger ?? AppLogging.Factory.CreateLogger<NuGetSourceProvider>());
        // A definition built from JSON with an explicit null list is tolerated as "no package IDs"
        _packageIds = (definition.PackageIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // The cache root must never depend on the process current directory (which could be a
        // target repo, whose NuGet configuration is not ours to read), so a relative or
        // drive-relative base is refused rather than resolved
        var baseFolder = rootBase ?? GetDefaultRootBase();
        if (!Path.IsPathFullyQualified(baseFolder))
        {
            throw new ArgumentException(
                $"The NuGet cache root base '{baseFolder}' must be an absolute path.", nameof(rootBase));
        }

        _root = Path.Combine(baseFolder, BuildSourceKey(definition.Alias, location));
    }

    /// <inheritdoc />
    public string Alias { get; }

    /// <summary>
    ///     Gets the folder under which per-source NuGet roots are created by default:
    ///     <c>{LocalApplicationData}/AgentControl/nuget</c>.
    /// </summary>
    /// <returns>The default base folder. A cache belongs in local (non-roaming) application data;
    ///     the folder is not created here.</returns>
    public static string GetDefaultRootBase() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentControl", "nuget");

    /// <summary>
    ///     Gets the AgentControl-owned root folder this provider passes to the NuGet library.
    /// </summary>
    /// <remarks>
    ///     Exposed so tests and diagnostics can verify that the root is never the target repo;
    ///     the folder is created on first use, not at construction.
    /// </remarks>
    internal string RootFolder => _root;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A NuGet feed cannot be enumerated cheaply, so the package IDs offered are the ones the
        // user configured for the source
        return Task.FromResult<IReadOnlyList<string>>(
            _packageIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPackage>> ListVersionsAsync(
        string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        await PrepareAsync(cancellationToken);

        // Ask the library for every listed version (prereleases included, as pins may be
        // prereleases); the source is named explicitly in Sources, so it is always queried. Only
        // the version list is read: no package content is downloaded
        var versions = await RunAsync(
            options => NuGetCache.ListVersionsAsync(packageId, includePrerelease: true, _root, options, cancellationToken),
            $"Source '{Alias}' ({_feed}) could not be reached, or did not answer as a NuGet feed. " +
            "Check that the address ends in index.json and that the server is reachable.",
            cancellationToken);

        // Skip versions our version type cannot represent rather than failing the whole list;
        // the library reports normalized strings, so this only drops exotic ones
        var results = new List<DiscoveredPackage>();
        foreach (var version in versions.Where(v => v.IsListed))
        {
            if (PackageVersion.TryParse(version.Version, out var parsed) && parsed is not null)
            {
                results.Add(new DiscoveredPackage(packageId, parsed, parsed.Original, Alias));
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IPackageContentReader> AcquireAsync(DiscoveredPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!string.Equals(package.SourceAlias, Alias, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Package '{package.PackageName}' was discovered at source '{package.SourceAlias}', not at '{Alias}'.",
                nameof(package));
        }

        await PrepareAsync(cancellationToken);

        // The library unpacks the package into the global packages folder configured in the
        // generated per-source nuget.config and returns that package's folder
        var packageFolder = await RunAsync(
            options => NuGetCache.EnsureCachedAsync(
                package.PackageName, package.Version.Original, _root, options, cancellationToken),
            $"Package '{package.PackageName}' {package.Version.Original} could not be downloaded from source " +
            $"'{Alias}' ({_feed}). It may no longer exist there, or the server may be unreachable.",
            cancellationToken,
            DescribeSignatureFailure(package));

        // Fail closed if the package did not land inside this source's private cache (the
        // NUGET_PACKAGES environment variable overrides the generated configuration): a shared
        // cache could hold a same-ID, same-version package that came from a different source
        var packagesFolder = Path.GetFullPath(Path.Combine(_root, PackagesFolderName));
        var relative = Path.GetRelativePath(packagesFolder, Path.GetFullPath(packageFolder));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                $"Package '{package.PackageName}' from source '{Alias}' was unpacked outside AgentControl's " +
                "private package cache, so it cannot be trusted to come from this source. " +
                "Remove the NUGET_PACKAGES environment variable and try again.");
        }

        return DirectoryPackageContentReader.Open(packageFolder);
    }

    /// <summary>
    ///     Runs the checks and preparation every operation needs before the NuGet library is called -
    ///     a folder source exists, signature verification is possible, the private cache root and
    ///     its configuration exist - after checking cancellation, and off the caller's thread.
    /// </summary>
    /// <remarks>
    ///     The checks do synchronous file-system work that can block for a long time (an offline
    ///     UNC folder), so they run on a worker thread and the caller is released as soon as it
    ///     cancels. Cancellation is checked first, so a call that is already canceled neither
    ///     reports a missing folder nor creates the cache root or its configuration. Work that
    ///     started before a later cancellation finishes in the background; it is idempotent (the
    ///     root and configuration are the ones the next operation needs anyway).
    /// </remarks>
    /// <param name="cancellationToken">Token to abandon the call.</param>
    /// <returns>A task that completes when the source is ready to be used.</returns>
    /// <exception cref="OperationCanceledException">Thrown when
    ///     <paramref name="cancellationToken"/> is canceled.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when a folder source does not exist or
    ///     is unreachable.</exception>
    /// <exception cref="PackageSourceAccessException">Thrown when signatures are required and cannot
    ///     be verified on this platform.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the cache root cannot be
    ///     prepared.</exception>
    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(
            () =>
            {
                EnsureLocationExists();
                EnsureSignatureVerificationPossible();
                PrepareRoot();
            },
            CancellationToken.None).WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Builds the name of this source's private root folder from its alias and location, so
    ///     editing a source's location starts a fresh cache and two sources never share one.
    /// </summary>
    /// <param name="alias">The source alias.</param>
    /// <param name="location">The source location.</param>
    /// <returns>A folder-name-safe key of the form <c>{alias}-{hash}</c>.</returns>
    internal static string BuildSourceKey(string alias, string location)
    {
        // Keep the alias recognizable in the folder name but make it filesystem-safe
        var safeAlias = new string(alias.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

        // The hash covers the case-folded alias and a normalized location; case-folding matches
        // the case-insensitive alias uniqueness rule
        var normalizedLocation = location.Trim().TrimEnd('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(alias.ToUpperInvariant() + "\n" + normalizedLocation));
        return $"{safeAlias}-{Convert.ToHexString(hash)[..KeyHashLength].ToLowerInvariant()}";
    }

    /// <summary>
    ///     Runs one NuGet library call for this source, applying the credential tiers for a server
    ///     and mapping failures to messages the user can act on.
    /// </summary>
    /// <remarks>
    ///     A folder source calls the library directly (it has no credentials and its failures are
    ///     reported as before). For a server the call is made up to twice: the first attempt is
    ///     silent (a remembered session credential, then whatever NuGet itself supplies), and only
    ///     when the server then refuses with 401/403 and a prompt is available is the call made
    ///     again with the prompt allowed, so the user is asked at most once and only when nothing
    ///     else worked. The credential the user typed is remembered after the second attempt
    ///     succeeds, and every credential a server rejected is forgotten.
    /// </remarks>
    /// <typeparam name="T">The result type of the library call.</typeparam>
    /// <param name="operation">The library call, given the per-call options to use.</param>
    /// <param name="failureMessage">The message for a failure that is neither an authentication
    ///     failure nor a timeout (the server is unreachable or does not have the package).</param>
    /// <param name="cancellationToken">The caller's token; its cancellation is never turned into a
    ///     failure.</param>
    /// <returns>The library call's result.</returns>
    /// <param name="signatureFailureMessage">Describes a signature failure of the package being
    ///     acquired, or <see langword="null"/> when no signature is checked by this call.</param>
    /// <exception cref="PackageSourceAccessException">Thrown with a user-readable, secret-free
    ///     message when the server refuses access, the user declines to sign in, the sign-in
    ///     times out, a signature requirement is not met, or the server cannot be used.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/>
    ///     is canceled.</exception>
    private async Task<T> RunAsync<T>(
        Func<NuGetCacheOptions, Task<T>> operation,
        string failureMessage,
        CancellationToken cancellationToken,
        string? signatureFailureMessage = null)
    {
        // A folder has no credentials and no server to be unreachable; keep its behavior as is,
        // apart from reporting a signature failure in words
        if (_feed is null)
        {
            try
            {
                return await operation(CreateOptions(null));
            }
            catch (InvalidOperationException ex) when (IsSignatureFailure(ex))
            {
                _nugetLogger.LogWarning(ex, "A package from source {Alias} failed signature verification.", Alias);
                throw new PackageSourceAccessException(
                    PackageSourceFailureKind.SignatureRejected, signatureFailureMessage ?? DescribeSignatureFailure(null), ex);
            }
        }

        var store = _credentials?.Store ?? new SessionCredentialStore();
        var prompt = _credentials?.Prompt;

        // Attempt 1 - silent: the session credential, then a saved sign-in, then whatever NuGet
        // itself supplies
        var silent = CreateCallback(store, prompt: null, previousCredentialRejected: false);
        try
        {
            return await AttemptAsync(operation, silent, failureMessage, signatureFailureMessage, cancellationToken);
        }
        catch (NuGetAuthenticationException ex)
        {
            // A remembered credential that the server refused must never be reused (and one
            // saved on this computer is removed from there too)
            var rejected = silent.SuppliedCredential is not null;
            if (prompt is null || cancellationToken.IsCancellationRequested)
            {
                // Describe the failure (sanitizing the library's exception) before the credential
                // stops being registered for redaction
                var failure = CreateAuthenticationFailure(rejected, userDeclined: false, ex);
                if (rejected)
                {
                    // Removing a rejected credential is not abandoned just because the caller
                    // cancelled; the credential helper call has its own time limit
                    await silent.RejectAsync(CancellationToken.None);
                }

                throw failure;
            }

            if (rejected)
            {
                await silent.RejectAsync(CancellationToken.None);
            }

            // Attempt 2 - the server needs something the user has to supply: ask, once
            return await PromptAndRetryAsync(
                operation, store, prompt, rejected, failureMessage, signatureFailureMessage, cancellationToken);
        }
        finally
        {
            // However the attempt ended, this operation stops using the remembered credential,
            // and so stops holding it for redaction
            silent.Release();
        }
    }

    /// <summary>
    ///     Repeats a call that the server refused, this time allowing the user to be asked.
    /// </summary>
    /// <param name="operation">The library call.</param>
    /// <param name="store">The session store.</param>
    /// <param name="prompt">Asks the user.</param>
    /// <param name="previousCredentialRejected">Whether the first attempt had used a credential
    ///     that the server rejected.</param>
    /// <param name="failureMessage">The message for a non-authentication failure.</param>
    /// <param name="signatureFailureMessage">The message for a signature failure.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The library call's result.</returns>
    /// <exception cref="PackageSourceAccessException">Thrown when the user declines, the server
    ///     rejects the new credential, or the call fails for another reason.</exception>
    private async Task<T> PromptAndRetryAsync<T>(
        Func<NuGetCacheOptions, Task<T>> operation,
        SessionCredentialStore store,
        ICredentialPrompt prompt,
        bool previousCredentialRejected,
        string failureMessage,
        string? signatureFailureMessage,
        CancellationToken cancellationToken)
    {
        var prompting = CreateCallback(store, prompt, previousCredentialRejected);
        try
        {
            var result = await AttemptAsync(operation, prompting, failureMessage, signatureFailureMessage, cancellationToken);

            // Only a credential that worked is kept for the session (and, when the user asked,
            // saved by their credential helper)
            await prompting.CommitAsync(CancellationToken.None);
            return result;
        }
        catch (NuGetAuthenticationException ex)
        {
            // Describe the failure (which sanitizes the library's exception) while the secret is
            // still registered, then forget the credential
            var failure = CreateAuthenticationFailure(
                previousCredentialRejected || prompting.SuppliedCredential is not null, prompting.UserDeclined, ex);
            await prompting.RejectAsync(CancellationToken.None);
            throw failure;
        }
        finally
        {
            // However the attempt ended, the typed secret stops being tracked (a kept one is
            // tracked by the store)
            prompting.Release();
        }
    }

    /// <summary>
    ///     Makes one library call and maps the failures that are not authentication failures.
    /// </summary>
    /// <param name="operation">The library call.</param>
    /// <param name="callback">The credential callback for this attempt.</param>
    /// <param name="failureMessage">The message for a non-authentication failure.</param>
    /// <param name="signatureFailureMessage">The message for a signature failure, or
    ///     <see langword="null"/> when this call checks no signature.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The library call's result.</returns>
    /// <exception cref="NuGetAuthenticationException">Thrown, unchanged, when the server refused
    ///     access; the caller decides what to do next.</exception>
    /// <exception cref="PackageSourceAccessException">Thrown when the sign-in timed out, a
    ///     signature requirement was not met, or the server could not be used.</exception>
    private async Task<T> AttemptAsync<T>(
        Func<NuGetCacheOptions, Task<T>> operation,
        PackageCredentialCallback callback,
        string failureMessage,
        string? signatureFailureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(CreateOptions(callback));
        }
        catch (InvalidOperationException ex) when (IsSignatureFailure(ex))
        {
            // A signature failure is a security decision, not an availability problem: say so
            _nugetLogger.LogWarning(ex, "A package from source {FeedOrigin} failed signature verification.", _feed);
            throw new PackageSourceAccessException(
                PackageSourceFailureKind.SignatureRejected, signatureFailureMessage ?? DescribeSignatureFailure(null), ex);
        }
        catch (NuGetAuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancellation the caller did not ask for is the library stopping a sign-in that
            // took longer than the allowed time
            _nugetLogger.LogWarning(ex, "Signing in to {FeedOrigin} timed out.", _feed);
            throw new PackageSourceAccessException(
                PackageSourceFailureKind.TimedOut,
                $"Signing in to source '{Alias}' ({_feed}) did not finish within " +
                $"{FormatDuration(_authenticationTimeout)} and was stopped. Try again.",
                ex);
        }
        catch (InvalidOperationException ex)
        {
            _nugetLogger.LogWarning(ex, "Source {FeedOrigin} could not be used.", _feed);
            throw new PackageSourceAccessException(PackageSourceFailureKind.Unreachable, failureMessage, ex);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or PackageSourceAccessException))
        {
            // Anything else the library or the network stack throws (a failed connection, a
            // malformed response) is reported the same way; the exception itself never leaves
            // this method, only a secret-free description of it does
            _nugetLogger.LogWarning(ex, "Source {FeedOrigin} could not be used.", _feed);
            throw new PackageSourceAccessException(PackageSourceFailureKind.Unreachable, failureMessage, ex);
        }
    }

    /// <summary>
    ///     Creates the user-readable exception for a server that refused access.
    /// </summary>
    /// <param name="credentialRejected">Whether a credential was supplied and rejected (as
    ///     opposed to none being available).</param>
    /// <param name="userDeclined">Whether the user cancelled the sign-in dialog.</param>
    /// <param name="inner">The library's exception, kept for diagnosis only.</param>
    /// <returns>The exception to throw.</returns>
    private PackageSourceAccessException CreateAuthenticationFailure(
        bool credentialRejected, bool userDeclined, NuGetAuthenticationException inner)
    {
        _nugetLogger.LogWarning(
            "Source {FeedOrigin} refused access (credential supplied: {CredentialSupplied}).", _feed, credentialRejected);

        if (userDeclined)
        {
            return new PackageSourceAccessException(
                PackageSourceFailureKind.SignInCanceled,
                $"Sign-in to source '{Alias}' ({_feed}) was canceled.",
                inner);
        }

        var message = credentialRejected
            ? $"Source '{Alias}' ({_feed}) rejected the credentials (the server answered 401 or 403). " +
              "Check the user name and password or token, and that the account may read this feed."
            : $"Source '{Alias}' ({_feed}) needs a sign-in.";
        return new PackageSourceAccessException(PackageSourceFailureKind.AuthenticationFailed, message, inner);
    }

    /// <summary>
    ///     Creates the credential callback for one attempt.
    /// </summary>
    /// <param name="store">The session store.</param>
    /// <param name="prompt">Asks the user, or <see langword="null"/> for a silent attempt.</param>
    /// <param name="previousCredentialRejected">Whether to tell the prompt that an earlier
    ///     credential was rejected.</param>
    /// <returns>The callback.</returns>
    private PackageCredentialCallback CreateCallback(
        SessionCredentialStore store, ICredentialPrompt? prompt, bool previousCredentialRejected) =>
        new(_feed!, Alias, store, prompt, previousCredentialRejected, _nugetLogger, _credentials?.Remembered);

    /// <summary>
    ///     Determines whether an exception from the NuGet library is a signature failure: the
    ///     package is unsigned, its signature is invalid, or its signer is not trusted.
    /// </summary>
    /// <remarks>
    ///     The library reports these as <see cref="InvalidOperationException"/> whose message says
    ///     the package failed signature verification or has no package signature (and whose inner
    ///     exception, when there is one, is NuGet's signature exception). Only a source that
    ///     requires signatures can see one.
    /// </remarks>
    /// <param name="exception">The exception the library threw.</param>
    /// <returns><see langword="true"/> when the source requires signatures and the exception
    ///     reports a signature failure.</returns>
    private bool IsSignatureFailure(InvalidOperationException exception) =>
        _requireSigned &&
        exception is not (NuGetAuthenticationException or PackageSourceAccessException) &&
        (exception.Message.Contains("signature verification", StringComparison.OrdinalIgnoreCase) ||
         exception.Message.Contains("package signature", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(exception.InnerException?.GetType().Name, "SignatureException", StringComparison.Ordinal));

    /// <summary>
    ///     Describes, in words and without any secret, why a package was refused because of its
    ///     signature.
    /// </summary>
    /// <param name="package">The package being acquired, or <see langword="null"/> when it is not
    ///     known.</param>
    /// <returns>A message naming the package and the source, and saying whether the source is
    ///     pinned to a trusted publisher.</returns>
    private string DescribeSignatureFailure(DiscoveredPackage? package)
    {
        var subject = package is null
            ? $"A package from source '{Alias}'"
            : $"Package '{package.PackageName}' {package.Version.Original} from source '{Alias}'";
        return _signerFingerprint is null
            ? $"{subject} is not validly signed, but the source requires signed packages. It may be unsigned, " +
              "its signature may be invalid, or its certificate may not be trusted on this computer."
            : $"{subject} is not signed by the trusted publisher (certificate {SummarizeFingerprint(_signerFingerprint)}). " +
              "It may be unsigned, signed by someone else, or its signature may be invalid.";
    }

    /// <summary>
    ///     Shortens a fingerprint for display: its first eight and last four characters.
    /// </summary>
    /// <param name="fingerprint">The 64-character fingerprint.</param>
    /// <returns>The shortened form, for example <c>A3AF7AF1…7641</c>.</returns>
    internal static string SummarizeFingerprint(string fingerprint) =>
        fingerprint.Length > 12 ? $"{fingerprint[..8]}…{fingerprint[^4..]}" : fingerprint;

    /// <summary>
    ///     Refuses to work when the source requires signed packages but the platform cannot verify
    ///     signatures: accepting packages unverified would silently defeat the requirement.
    /// </summary>
    /// <exception cref="PackageSourceAccessException">Thrown with kind
    ///     <see cref="PackageSourceFailureKind.SignatureRejected"/> when the source requires
    ///     signatures and verification is unsupported here.</exception>
    private void EnsureSignatureVerificationPossible()
    {
        if (_requireSigned && !_signaturesSupported)
        {
            throw new PackageSourceAccessException(
                PackageSourceFailureKind.SignatureRejected,
                $"Source '{Alias}' requires signed packages, but {SignatureVerificationSupport.UnsupportedMessage.ToLowerInvariant()}, " +
                "so no package can be used from it. Edit the source on a platform that supports it, or turn " +
                "off \"Require signed packages\" for it if you accept that risk.");
        }
    }

    /// <summary>
    ///     Formats a duration for a message.
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>Whole minutes when it is a whole number of minutes, otherwise seconds.</returns>
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60 || duration.TotalSeconds % 60 != 0)
        {
            return $"{duration.TotalSeconds:0.##} seconds";
        }

        var minutes = (int)duration.TotalMinutes;
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }

    /// <summary>
    ///     Creates the per-call NuGet library options: only this source, non-interactive plug-ins
    ///     (so a credential provider can sign in silently but never opens its own dialog), and -
    ///     for a server - the credential callback and the sign-in timeout.
    /// </summary>
    /// <param name="callback">The credential callback for a server attempt, or
    ///     <see langword="null"/> for a folder.</param>
    /// <returns>The options for one library call.</returns>
    internal NuGetCacheOptions CreateOptions(PackageCredentialCallback? callback) => new()
    {
        Sources = [_location],
        Interactive = false,
        Logger = _nugetLogger,
        RequireSignedPackages = _requireSigned,
        CredentialCallback = callback?.Callback,
        AuthenticationTimeout = callback is null ? null : _authenticationTimeout
    };

    /// <summary>
    ///     Verifies a folder feed exists so a missing or offline folder is reported as such, in
    ///     the same way a missing zip folder is, instead of as an opaque NuGet failure. A server
    ///     needs no such check; its reachability is reported by the call itself.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">Thrown when the folder does not exist or is
    ///     unreachable.</exception>
    private void EnsureLocationExists()
    {
        if (_feed is null && !Directory.Exists(_location))
        {
            throw new DirectoryNotFoundException($"The NuGet package folder for source '{Alias}' was not found.");
        }
    }

    /// <summary>
    ///     Creates this source's private root folder and its generated <c>nuget.config</c> (which
    ///     only redirects the global packages folder into the root) the first time it is needed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the root cannot be created or the
    ///     configuration cannot be written.</exception>
    private void PrepareRoot()
    {
        lock (_prepareLock)
        {
            if (_prepared)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_root);
                WriteConfigIfChanged(Path.Combine(_root, NuGetConfigFileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Failed to prepare the package cache for source '{Alias}': {ex.Message}", ex);
            }

            _prepared = true;
        }
    }

    /// <summary>
    ///     Writes the generated per-source NuGet configuration unless the file already holds the
    ///     same content, replacing it atomically so concurrent processes never see a partial file.
    /// </summary>
    /// <remarks>
    ///     The file redirects the global packages folder into the root and - only when the source
    ///     requires signed packages - states which signers are trusted: a <c>trustedSigners</c>
    ///     section that begins with <c>&lt;clear /&gt;</c> (so no signer configured elsewhere on
    ///     the machine applies) followed, when a publisher is pinned, by an <c>author</c> entry
    ///     for that publisher's certificate fingerprint. No sources, credentials or signature
    ///     mode are written: the library applies the signature requirement itself.
    /// </remarks>
    /// <param name="configPath">Path of the <c>nuget.config</c> to write.</param>
    private void WriteConfigIfChanged(string configPath)
    {
        var packagesFolder = Path.GetFullPath(Path.Combine(_root, PackagesFolderName));
        var configuration = new XElement(
            "configuration",
            new XElement(
                "config",
                new XElement(
                    "add",
                    new XAttribute("key", "globalPackagesFolder"),
                    new XAttribute("value", packagesFolder))));
        if (_requireSigned)
        {
            var trustedSigners = new XElement("trustedSigners", new XElement("clear"));
            if (_signerFingerprint is not null)
            {
                trustedSigners.Add(
                    new XElement(
                        "author",
                        new XAttribute("name", PinnedSignerName),
                        new XElement(
                            "certificate",
                            new XAttribute("fingerprint", _signerFingerprint),
                            new XAttribute("hashAlgorithm", "SHA256"),
                            new XAttribute("allowUntrustedRoot", "false"))));
            }

            configuration.Add(trustedSigners);
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), configuration);
        var content = document.Declaration + Environment.NewLine + document.Root;

        if (File.Exists(configPath) && File.ReadAllText(configPath) == content)
        {
            return;
        }

        var temporaryPath = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, content);
        File.Move(temporaryPath, configPath, overwrite: true);
    }
}
